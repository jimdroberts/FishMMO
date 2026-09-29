#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Pictures of one generated scene, side by side: what the atlas globe shows under its
	/// rectangle, the heightmap the generator cuts from there, the heightmap the Unity terrain
	/// actually holds, and the scene photographed from overhead.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The question it answers is whether the ground is the ground the map promised, and each
	/// picture is taken from a different link of the chain so a break shows where it is: the
	/// globe's baked texture, the pure height function, the terrain assets on disk, and the
	/// renderer. The two heightmaps use the same colour scale, and the log reports how far the
	/// terrain strays from the cut in metres, so "accurate" is a number as well as a look.
	/// </para>
	/// <para>
	/// The atlas image is sampled from the baked surface exactly as the globe maps it —
	/// equirectangular, longitude across and latitude down — through the scene's footprint at
	/// the radius it was cut at. It is the globe's own picture of that ground, not a re-render.
	/// </para>
	/// </remarks>
	public static class SceneCutSnapshot
	{
		/// <summary>Where snapshots go when nothing else is asked for.</summary>
		public const string DefaultRoot = "Library/FishMMO/CutSnapshots";

		/// <summary>Width of every image, in pixels. Height follows the scene's aspect.</summary>
		public const int ImageWidth = 1024;

		/// <summary>What one snapshot found.</summary>
		public sealed class Report
		{
			public string Folder;
			public readonly List<string> Wrote = new List<string>();
			public float LowestMetres, HighestMetres;
			public float MeanDifferenceMetres, LargestDifferenceMetres;
			public float LandShareCut, LandShareTerrain;
			public string Problem;
		}

		/// <summary>The request the generator would use for this scene now.</summary>
		public static SceneGenerationRequest RequestFor(WorldAtlasScene entry, bool fineDetail = true)
		{
			return new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
				FineDetail = fineDetail,
				RadiusKm = entry.CutRadiusKm > 0f ? entry.CutRadiusKm : 0.0,
			};
		}

		/// <summary>Writes the snapshot of one scene. Opens the scene additively and closes it again.</summary>
		public static Report Write(WorldAtlasScene entry, string folder = null)
		{
			var report = new Report();
			if (entry == null || entry.Body == null)
			{
				report.Problem = "Only a scene placed on a body can be compared with its globe.";
				return report;
			}
			string scenePath = $"{SceneGenerator.WorldFolder(entry.Body)}/{entry.SceneName}.unity";
			if (!File.Exists(scenePath))
			{
				report.Problem = $"'{scenePath}' does not exist.";
				return report;
			}

			folder ??= $"{DefaultRoot}/{WorldEditorAssets.Sanitize(entry.SceneName)}";
			Directory.CreateDirectory(folder);
			report.Folder = folder;

			SceneGenerationRequest request = RequestFor(entry);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(entry.SizeKm);
			int width = ImageWidth;
			int height = Mathf.Max(16, Mathf.RoundToInt(ImageWidth * plan.DepthMetres / plan.WidthMetres));
			float metresPerPixel = plan.WidthMetres / (width - 1);

			// ── 1. The globe's picture of the rectangle ──
			Texture2D surface = LoadSurface(entry.Body);
			if (surface != null)
			{
				double radius = request.ResolvedRadiusKm;
				AtlasFootprint footprint = request.Footprint;
				Save(report, folder, "1 atlas cut.png", width, height, (column, row) =>
					SampleSurface(surface, footprint, radius, PixelX(column, width, plan), PixelZ(row, height, plan)));

				// Three times the footprint, with the rectangle drawn on, so it can be found on the globe.
				const float Context = 3f;
				float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
				Save(report, folder, "0 atlas context.png", width, height, (column, row) =>
				{
					float cx = PixelX(column, width, plan) * Context, cz = PixelZ(row, height, plan) * Context;
					float edge = metresPerPixel * Context * 1.5f;
					bool onOutline = (Mathf.Abs(Mathf.Abs(cx) - halfW) < edge && Mathf.Abs(cz) <= halfD + edge)
						|| (Mathf.Abs(Mathf.Abs(cz) - halfD) < edge && Mathf.Abs(cx) <= halfW + edge);
					return onOutline ? new Color(1f, 0.85f, 0.1f) : SampleSurface(surface, footprint, radius, cx, cz);
				});
				Object.DestroyImmediate(surface);
			}
			else
			{
				Debug.LogWarning($"[Cut snapshot] {entry.Body.ResolvedName} has no baked surface, so there is no atlas picture. Bake it from the World Atlas page first.");
			}

			// ── 2. The heightmap the generator cuts ──
			var cut = new float[width * height];
			Grid(width, height, plan, (i, x, z) => cut[i] = SceneGeneration.AltitudeMetres(request, x, z));

			// ── 3. The heightmap the terrain holds ──
			var ground = new float[width * height];
			bool hasTerrain = ReadTerrain(scenePath, width, height, plan, ground, out Scene opened);

			float lowest = float.MaxValue, highest = float.MinValue;
			foreach (float v in cut)
			{
				lowest = Mathf.Min(lowest, v);
				highest = Mathf.Max(highest, v);
			}
			report.LowestMetres = lowest;
			report.HighestMetres = highest;
			report.LandShareCut = LandShare(cut);

			Save(report, folder, "2 cut heightmap.png", width, height, Relief(cut, width, height, metresPerPixel, lowest, highest));
			SaveGrey(report, folder, "2 cut heightmap raw.png", width, height, cut, lowest, highest);

			if (hasTerrain)
			{
				report.LandShareTerrain = LandShare(ground);
				double sum = 0.0;
				float largest = 0f;
				for (int i = 0; i < cut.Length; i++)
				{
					float d = Mathf.Abs(cut[i] - ground[i]);
					sum += d;
					largest = Mathf.Max(largest, d);
				}
				report.MeanDifferenceMetres = (float)(sum / cut.Length);
				report.LargestDifferenceMetres = largest;

				Save(report, folder, "3 terrain heightmap.png", width, height, Relief(ground, width, height, metresPerPixel, lowest, highest));
				SaveGrey(report, folder, "3 terrain heightmap raw.png", width, height, ground, lowest, highest);

				// ── 4. The scene from overhead ──
				string topDown = CaptureTopDown(opened, plan, width, height, highest, folder);
				if (topDown != null)
				{
					report.Wrote.Add(topDown);
				}
			}
			else
			{
				report.Problem = "The scene has no terrain to compare.";
			}

			if (opened.IsValid())
			{
				EditorSceneManager.CloseScene(opened, true);
			}

			string side = SideBySide(report, folder, width, height);
			if (side != null)
			{
				report.Wrote.Add(side);
			}

			File.WriteAllText($"{folder}/report.txt",
				$"{entry.SceneName} on {entry.Body.ResolvedName}, {entry.Latitude:0.####}°, {entry.Longitude:0.####}°, {plan}\n" +
				$"cut at atlas radius {request.ResolvedRadiusKm:0.##} km (sky radius {entry.Body.SkyRadiusKm:0} km), vertical scale {request.VerticalScale:0.####}\n" +
				$"cut: {lowest:0.0} .. {highest:0.0} m, {report.LandShareCut:P1} land\n" +
				(hasTerrain
					? $"terrain: {report.LandShareTerrain:P1} land; differs from the cut by {report.MeanDifferenceMetres:0.00} m on average, {report.LargestDifferenceMetres:0.00} m at most\n"
					: "terrain: none\n"));
			report.Wrote.Add($"{folder}/report.txt");
			return report;
		}

		// ── Command line ──────────────────────────────────────────────

		/// <summary>
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.SceneCutSnapshot.RecutFromCommandLine</c>:
		/// re-bakes the body's globe, re-cuts <c>FISHMMO_RECUT_SCENE</c> and writes its snapshot
		/// to <c>FISHMMO_SNAPSHOT_DIR</c>. <c>FISHMMO_RECUT=0</c> only snapshots.
		/// </summary>
		public static void RecutFromCommandLine()
		{
			int code = 1;
			try
			{
				string sceneName = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
				string folder = Environment.GetEnvironmentVariable("FISHMMO_SNAPSHOT_DIR");
				bool recut = Environment.GetEnvironmentVariable("FISHMMO_RECUT") != "0";
				code = RecutAndSnapshot(sceneName, recut, string.IsNullOrEmpty(folder) ? null : folder) ? 0 : 1;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Cut snapshot] {ex}");
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		/// <summary>Re-bakes the globe, optionally re-cuts, then snapshots. True on success.</summary>
		public static bool RecutAndSnapshot(string sceneName, bool recut, string folder)
		{
			WorldAtlasScene entry = FindEntry(sceneName);
			if (entry == null)
			{
				Debug.LogError($"[Cut snapshot] No atlas entry named '{sceneName}'.");
				return false;
			}
			/* Paths from here on, never the objects: bake and re-cut both end in a refresh, and a
			 * ScriptableObject held across one comes back fake-null. */
			string entryPath = AssetDatabase.GetAssetPath(entry);
			string bodyPath = AssetDatabase.GetAssetPath(entry.Body);

			PlanetSurfaceBaker.Bake(entry.Body);
			entry = Reload(entryPath, bodyPath);

			if (recut)
			{
				SceneGenerationResult result = SceneGenerator.Recut(entry);
				if (!result.Success)
				{
					Debug.LogError($"[Cut snapshot] Re-cut refused: {result.Problem}");
					return false;
				}
				Debug.Log($"[Cut snapshot] Re-cut '{sceneName}': {result.Plan}, {result.ReliefMetres:0} m of relief, floor at {result.GroundAltitudeMetres:0} m, " +
					$"radius {result.RadiusKm:0.##} km, vertical scale {result.VerticalScale:0.####}, backup in '{result.BackupFolder}'.");
				entry = Reload(entryPath, bodyPath);
			}

			Report report = Write(entry, folder);
			if (report.Problem != null)
			{
				Debug.LogError($"[Cut snapshot] {report.Problem}");
			}
			Debug.Log($"[Cut snapshot] {sceneName}: cut {report.LowestMetres:0} .. {report.HighestMetres:0} m, {report.LandShareCut:P1} land; " +
				$"terrain differs by {report.MeanDifferenceMetres:0.00} m on average, {report.LargestDifferenceMetres:0.00} m at most.\n  " +
				string.Join("\n  ", report.Wrote));
			return report.Problem == null;
		}

		/// <summary>
		/// The entry again after a refresh, with its body re-attached. The entry object itself
		/// survives a refresh, but its field can still point at the body's old, destroyed wrapper,
		/// which reads as null. Re-attached in memory only; nothing is marked dirty.
		/// </summary>
		private static WorldAtlasScene Reload(string entryPath, string bodyPath)
		{
			WorldAtlasScene entry = AssetDatabase.LoadAssetAtPath<WorldAtlasScene>(entryPath);
			if (entry != null && entry.Body == null && !string.IsNullOrEmpty(bodyPath))
			{
				entry.Body = AssetDatabase.LoadAssetAtPath<WorldBody>(bodyPath);
			}
			return entry;
		}

		private static WorldAtlasScene FindEntry(string sceneName)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return null;
			}
			foreach (WorldAtlasScene candidate in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (candidate != null && string.Equals(candidate.SceneName, sceneName, StringComparison.OrdinalIgnoreCase))
				{
					return candidate;
				}
			}
			return null;
		}

		// ── Sampling ──────────────────────────────────────────────────

		/// <summary>
		/// Visits every pixel with its scene position: X across, Z up the image, so north is at the
		/// top when the heading is 0 — the same way round the globe draws it.
		/// </summary>
		private static void Grid(int width, int height, TerrainTilePlan plan, Action<int, float, float> visit)
		{
			for (int row = 0; row < height; row++)
			{
				float z = PixelZ(row, height, plan);
				for (int column = 0; column < width; column++)
				{
					visit(row * width + column, PixelX(column, width, plan), z);
				}
			}
		}

		/// <summary>Scene X of a pixel column: the scene's west edge to its east edge.</summary>
		private static float PixelX(int column, int width, TerrainTilePlan plan) => (column / (float)(width - 1) - 0.5f) * plan.WidthMetres;

		/// <summary>Scene Z of a pixel row: row 0 is the north edge.</summary>
		private static float PixelZ(int row, int height, TerrainTilePlan plan) => (0.5f - row / (float)(height - 1)) * plan.DepthMetres;

		/// <summary>The body's baked surface as a readable texture, or null.</summary>
		private static Texture2D LoadSurface(WorldBody body)
		{
			string path = PlanetSurfaceBaker.BakedImagePath(body.name);
			if (!File.Exists(path))
			{
				return null;
			}
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
			return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
		}

		/// <summary>The globe's colour under a scene point, mapped exactly as <c>GlobeView</c> maps it.</summary>
		private static Color SampleSurface(Texture2D surface, in AtlasFootprint footprint, double radiusKm, float x, float z)
		{
			AtlasGeometry.FromUnit(AtlasGeometry.SceneToUnit(footprint, x / 1000.0, z / 1000.0, radiusKm), out double lat, out double lon);
			return surface.GetPixelBilinear((float)((lon + 180.0) / 360.0), (float)((90.0 - lat) / 180.0));
		}

		/// <summary>Reads every terrain tile of a scene into a grid of altitudes. Leaves the scene open in <paramref name="opened"/>.</summary>
		private static bool ReadTerrain(string scenePath, int width, int height, TerrainTilePlan plan, float[] into, out Scene opened)
		{
			opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
			var tiles = new List<Terrain>();
			foreach (GameObject root in opened.GetRootGameObjects())
			{
				tiles.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			tiles.RemoveAll(t => t == null || t.terrainData == null);
			if (tiles.Count == 0)
			{
				return false;
			}

			Grid(width, height, plan, (i, x, z) =>
			{
				var position = new Vector3(x, 0f, z);
				Terrain best = tiles[0];
				float bestDistance = float.MaxValue;
				foreach (Terrain tile in tiles)
				{
					Vector3 origin = tile.GetPosition();
					Vector3 size = tile.terrainData.size;
					float dx = Mathf.Max(0f, Mathf.Max(origin.x - x, x - (origin.x + size.x)));
					float dz = Mathf.Max(0f, Mathf.Max(origin.z - z, z - (origin.z + size.z)));
					float distance = dx + dz;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = tile;
					}
				}
				into[i] = best.GetPosition().y + best.SampleHeight(position);
			});
			return true;
		}

		private static float LandShare(float[] altitudes)
		{
			int land = 0;
			foreach (float a in altitudes)
			{
				if (a > 0f)
				{
					land++;
				}
			}
			return altitudes.Length > 0 ? land / (float)altitudes.Length : 0f;
		}

		// ── Pictures ──────────────────────────────────────────────────

		/// <summary>
		/// Hypsometric colour with hill shading, on a fixed scale so two heightmaps can be compared
		/// pixel for pixel. Sea is blue by depth, land green to rock to snow by height.
		/// </summary>
		private static Func<int, int, Color> Relief(float[] altitudes, int width, int height, float metresPerPixel, float lowest, float highest)
		{
			float top = Mathf.Max(1f, highest);
			float deepest = Mathf.Max(1f, -lowest);
			var light = new Vector3(-0.5f, 0.7071f, 0.5f).normalized; // from the north-west, 45° up
			return (column, row) => ReliefAt(altitudes, width, height, column, row, metresPerPixel, top, deepest, light);
		}

		private static Color ReliefAt(float[] a, int width, int height, int column, int row, float metresPerPixel, float top, float deepest, Vector3 light)
		{
			float here = a[row * width + column];
			float east = a[row * width + Mathf.Min(width - 1, column + 1)] - a[row * width + Mathf.Max(0, column - 1)];
			float north = a[Mathf.Max(0, row - 1) * width + column] - a[Mathf.Min(height - 1, row + 1) * width + column];
			var normal = new Vector3(-east / (2f * metresPerPixel), 1f, -north / (2f * metresPerPixel)).normalized;
			float shade = Mathf.Clamp01(Vector3.Dot(normal, light));

			if (here < 0f)
			{
				float depth = Mathf.Clamp01(-here / deepest);
				Color sea = Color.Lerp(new Color(0.16f, 0.43f, 0.63f), new Color(0.04f, 0.12f, 0.27f), depth);
				return sea * (0.85f + 0.15f * shade);
			}

			float t = Mathf.Clamp01(here / top);
			Color land = t < 0.12f ? Color.Lerp(new Color(0.27f, 0.47f, 0.24f), new Color(0.43f, 0.55f, 0.27f), t / 0.12f)
				: t < 0.3f ? Color.Lerp(new Color(0.43f, 0.55f, 0.27f), new Color(0.59f, 0.55f, 0.35f), (t - 0.12f) / 0.18f)
				: t < 0.55f ? Color.Lerp(new Color(0.59f, 0.55f, 0.35f), new Color(0.55f, 0.43f, 0.33f), (t - 0.3f) / 0.25f)
				: t < 0.8f ? Color.Lerp(new Color(0.55f, 0.43f, 0.33f), new Color(0.67f, 0.63f, 0.59f), (t - 0.55f) / 0.25f)
				: Color.Lerp(new Color(0.67f, 0.63f, 0.59f), new Color(0.96f, 0.96f, 0.98f), (t - 0.8f) / 0.2f);
			return land * (0.35f + 0.85f * shade);
		}

		/// <summary>Writes an image whose row 0 is the north edge, as every picture here is laid out.</summary>
		private static void Save(Report report, string folder, string name, int width, int height, Func<int, int, Color> colourAt)
		{
			var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
			var pixels = new Color32[width * height];
			for (int row = 0; row < height; row++)
			{
				for (int column = 0; column < width; column++)
				{
					// A texture's row 0 is its bottom, so the north row goes last.
					pixels[(height - 1 - row) * width + column] = colourAt(column, row);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			string path = $"{folder}/{name}";
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			report.Wrote.Add(path);
		}

		private static void SaveGrey(Report report, string folder, string name, int width, int height, float[] altitudes, float lowest, float highest)
		{
			float span = Mathf.Max(1e-3f, highest - lowest);
			var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
			var pixels = new Color32[width * height];
			for (int row = 0; row < height; row++)
			{
				for (int column = 0; column < width; column++)
				{
					byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01((altitudes[row * width + column] - lowest) / span) * 255f);
					pixels[(height - 1 - row) * width + column] = new Color32(v, v, v, 255);
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			string path = $"{folder}/{name}";
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			report.Wrote.Add(path);
		}

		/// <summary>
		/// Photographs the scene from straight overhead with a plain sun, so the ground itself is
		/// what shows rather than whatever time of day the editor happens to be at.
		/// </summary>
		private static string CaptureTopDown(Scene scene, TerrainTilePlan plan, int width, int height, float highest, string folder)
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
			{
				Debug.LogWarning("[Cut snapshot] No graphics device, so no overhead picture. Run without -nographics (under xvfb-run when headless).");
				return null;
			}

			Scene previousActive = SceneManager.GetActiveScene();
			bool previousFog = RenderSettings.fog;
			RenderTexture previousTarget = RenderTexture.active;
			var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			var cameraObject = new GameObject("CutSnapshotCamera") { hideFlags = HideFlags.HideAndDontSave };
			var sunObject = new GameObject("CutSnapshotSun") { hideFlags = HideFlags.HideAndDontSave };
			try
			{
				SceneManager.SetActiveScene(scene);
				RenderSettings.fog = false;

				Light sun = sunObject.AddComponent<Light>();
				sun.type = LightType.Directional;
				sun.intensity = 1.2f;
				sun.shadows = LightShadows.None;
				sunObject.transform.rotation = Quaternion.Euler(50f, 135f, 0f);

				float cameraHeight = Mathf.Max(highest, 0f) + 2000f;
				Camera camera = cameraObject.AddComponent<Camera>();
				camera.transform.position = new Vector3(0f, cameraHeight, 0f);
				camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
				camera.orthographic = true;
				camera.orthographicSize = plan.DepthMetres * 0.5f;
				camera.aspect = plan.WidthMetres / plan.DepthMetres;
				camera.nearClipPlane = 1f;
				camera.farClipPlane = cameraHeight + 20000f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = Color.black;
				camera.enabled = false;
				camera.targetTexture = target;
				camera.Render();

				RenderTexture.active = target;
				var image = new Texture2D(width, height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
				image.Apply();
				string path = $"{folder}/4 terrain top-down.png";
				File.WriteAllBytes(path, image.EncodeToPNG());
				Object.DestroyImmediate(image);
				return path;
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[Cut snapshot] The overhead picture failed: {ex.Message}");
				return null;
			}
			finally
			{
				RenderTexture.active = previousTarget;
				RenderSettings.fog = previousFog;
				if (previousActive.IsValid())
				{
					SceneManager.SetActiveScene(previousActive);
				}
				target.Release();
				Object.DestroyImmediate(target);
				Object.DestroyImmediate(cameraObject);
				Object.DestroyImmediate(sunObject);
			}
		}

		/// <summary>The four pictures in one image: atlas and cut above, terrain and overhead below.</summary>
		private static string SideBySide(Report report, string folder, int width, int height)
		{
			string[] names = { "1 atlas cut.png", "2 cut heightmap.png", "3 terrain heightmap.png", "4 terrain top-down.png" };
			const int Gap = 12;
			var sheet = new Texture2D(width * 2 + Gap, height * 2 + Gap, TextureFormat.RGB24, false);
			var fill = new Color32[sheet.width * sheet.height];
			for (int i = 0; i < fill.Length; i++)
			{
				fill[i] = new Color32(255, 255, 255, 255);
			}
			sheet.SetPixels32(fill);
			bool any = false;
			for (int i = 0; i < names.Length; i++)
			{
				string path = $"{folder}/{names[i]}";
				if (!File.Exists(path))
				{
					continue;
				}
				var image = new Texture2D(2, 2);
				if (image.LoadImage(File.ReadAllBytes(path)) && image.width == width && image.height == height)
				{
					int x = (i % 2) * (width + Gap);
					int y = (i < 2 ? 1 : 0) * (height + Gap);
					sheet.SetPixels(x, y, width, height, image.GetPixels());
					any = true;
				}
				Object.DestroyImmediate(image);
			}
			string result = null;
			if (any)
			{
				sheet.Apply(false, false);
				result = $"{folder}/side by side.png";
				File.WriteAllBytes(result, sheet.EncodeToPNG());
			}
			Object.DestroyImmediate(sheet);
			return result;
		}
	}
}
#endif
