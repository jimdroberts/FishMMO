#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Proves generated points of interest on a throwaway cut: generates FISHMMO_POI_SCENE's atlas entry (Flo Monolith by
	/// default) as "POI Render Probe" with a capital and FISHMMO_POI_DENSITY (Dense by default), then writes to
	/// FISHMMO_POI_OUT a top-down overview (overview.png), markers.json (every record and its pixel on the overview, for
	/// labelling), one close-up per kind (site_*.png), the record table (records.tsv) and the cut's notes (report.txt).
	/// The scene, its folders, settings and atlas entry are deleted afterwards unless FISHMMO_POI_KEEP=1, which leaves
	/// them for the atlas and map renders; <see cref="Cleanup"/> deletes them later.
	/// </summary>
	public static class PointOfInterestRenderProbe
	{
		public const string ProbeName = "POI Render Probe";
		private const int CloseWidth = 1280;
		private const int CloseHeight = 720;

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_POI_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-poi");
			Directory.CreateDirectory(output);
			string sceneName = Environment.GetEnvironmentVariable("FISHMMO_POI_SCENE") ?? "Flo Monolith";
			bool keep = Environment.GetEnvironmentVariable("FISHMMO_POI_KEEP") == "1";
			int maxShots = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_POI_SHOTS"), out int shots) ? shots : 40;
			var report = new List<string>();
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == sceneName);
			if (entry == null || entry.Body == null)
			{
				Debug.LogError($"[POI probe] no placed atlas entry '{sceneName}'.");
				EditorApplication.Exit(1);
				return;
			}
			bool ok = false;
			try
			{
				Scene scene = EditorSceneManager.GetActiveScene();
				string existing = $"{SceneGenerator.WorldFolder(entry.Body)}/{ProbeName}.unity";
				if (Environment.GetEnvironmentVariable("FISHMMO_POI_REUSE") == "1" && File.Exists(existing))
				{
					scene = EditorSceneManager.OpenScene(existing, OpenSceneMode.Single);
					report.Add($"reused {existing}");
				}
				else
				{
					/* The procedural art first, synchronously, as a build does: a generator change since the art was made
					 * leaves it stale, and the editor's own catch-up runs asynchronously over later updates, which a
					 * batch method that cuts straight away never waits for. */
					if (Environment.GetEnvironmentVariable("FISHMMO_POI_ENSURE_ART") != "0")
					{
						var artLog = new List<string>();
						BiomeArtGenerator.EnsureCurrentForBuild(artLog);
						report.AddRange(artLog.Select(line => "art: " + line));
					}
					Cleanup();
					PointOfInterestSettings settings = ProbeSettings(entry);
					var timer = System.Diagnostics.Stopwatch.StartNew();
					SceneGenerationResult result = SceneGenerator.Generate(new SceneGenerationRequest
					{
						SceneName = ProbeName,
						Body = entry.Body,
						Layer = entry.Layer,
						Latitude = entry.Latitude,
						Longitude = entry.Longitude,
						SizeKm = entry.SizeKm,
						HeadingDegrees = entry.HeadingDegrees,
						FineDetail = true,
						Erosion = true,
						ErosionStrength = entry.ErosionStrength,
						RadiusKm = entry.CutRadiusKm,
						PointsOfInterest = settings,
					});
					report.Add($"cut '{sceneName}' as '{ProbeName}' in {timer.Elapsed.TotalSeconds:0} s: {(result != null && result.Success ? "ok" : result?.Problem)}");
					if (result == null || !result.Success)
					{
						return;
					}
					if (result.Entry != null)
					{
						result.Entry.PointsOfInterest = settings;
						EditorUtility.SetDirty(result.Entry);
					}
					report.Add($"biomes: {result.BiomeSummary}");
					foreach (string note in result.Notes)
					{
						report.Add("note: " + note);
					}
					scene = EditorSceneManager.OpenScene(result.ScenePath, OpenSceneMode.Single);
				}

				List<PointOfInterestRecord> records = PointOfInterestProbe.Records(scene);
				File.WriteAllText(Path.Combine(output, "records.tsv"), PointOfInterestProbe.Table(records));
				report.Add($"{records.Count} point(s) of interest");
				foreach (IGrouping<PointOfInterestGroup, PointOfInterestRecord> group in records.GroupBy(r => r.Info.Group).OrderBy(g => g.Key))
				{
					report.Add($"  {group.Key}: {string.Join(", ", group.GroupBy(r => r.Kind).Select(k => $"{k.Key} x{k.Count()}"))}");
				}
				File.WriteAllLines(Path.Combine(output, "report.txt"), report);

				Rect area = SceneArea(scene);
#if !UNITY_SERVER
				// The ground's texture arrays (baked if stale), or every tile draws plain white, as the world map bake does.
				report.Add($"terrain arrays bound on {TerrainArrayBinder.BindAll(scene)} tile set(s)");
#endif
				using (new ProbeLighting())
				{
					var camera = new GameObject("POI Probe Camera").AddComponent<Camera>();
					try
					{
						AddUrpData(camera);
						Overview(camera, area, records, output, report);
						PathShots(camera, PointOfInterestProbe.Asset(scene), output, report);
						CloseUps(camera, records, maxShots, output, report);
					}
					finally
					{
						UnityEngine.Object.DestroyImmediate(camera.gameObject);
					}
				}
				ok = true;
			}
			catch (Exception ex)
			{
				report.Add("threw: " + ex);
				Debug.LogException(ex);
			}
			finally
			{
				File.WriteAllLines(Path.Combine(output, "report.txt"), report);
				Debug.Log("[POI probe]\n" + string.Join("\n", report));
				if (!keep)
				{
					EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
					Cleanup();
				}
				else
				{
					AssetDatabase.SaveAssets();
				}
				if (Application.isBatchMode)
				{
					EditorApplication.Exit(ok ? 0 : 1);
				}
			}
		}

		/// <summary>
		/// Runs every point-of-interest content tool in order, for one batch launch: naming data, gameplay content
		/// (discovery achievement, NPC table, gathering resources), then the site templates when that tool exists.
		/// Each only adds what is missing.
		/// </summary>
		public static void AuthorContent()
		{
			bool ok = true;
			void Step(string name, Action step)
			{
				try
				{
					step();
					Debug.Log($"[POI content] {name}: done");
				}
				catch (Exception ex)
				{
					ok = false;
					Debug.LogError($"[POI content] {name} threw: {ex}");
				}
			}
			// The procedural art (the structure kit with it) first: the templates measure kit pieces.
			Step("procedural art", () =>
			{
				var artLog = new List<string>();
				BiomeArtGenerator.EnsureCurrentForBuild(artLog);
				foreach (string line in artLog)
				{
					Debug.Log("[POI content] art: " + line);
				}
			});
			Step("place naming", BiomeNamingGenerator.RunPlaceNamingTools);
			Step("gameplay content", PointOfInterestGameplayContent.GenerateAll);
			bool force = Environment.GetEnvironmentVariable("FISHMMO_POI_FORCE_TEMPLATES") == "1";
			Step("site templates", () => Debug.Log("[POI content] templates: " + PointOfInterestTemplateAuthoring.AuthorAll(force)));
			AssetDatabase.SaveAssets();
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(ok ? 0 : 1);
			}
		}

		/// <summary>Deletes the probe's scene, terrain folder, settings, NavMesh entry, atlas entry and terrain arrays.</summary>
		public static void Cleanup()
		{
			foreach (WorldAtlasScene probeEntry in WorldEditorAssets.FindAll<WorldAtlasScene>().Where(e => e != null && e.SceneName == ProbeName).ToList())
			{
				if (probeEntry.Body != null)
				{
					string scenePath = $"{SceneGenerator.WorldFolder(probeEntry.Body)}/{ProbeName}.unity";
					if (File.Exists(scenePath))
					{
						AssetDatabase.DeleteAsset(scenePath);
					}
					string terrainFolder = SceneGenerator.TerrainFolder(probeEntry.Body, ProbeName);
					if (AssetDatabase.IsValidFolder(terrainFolder))
					{
						AssetDatabase.DeleteAsset(terrainFolder);
					}
				}
				AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(probeEntry));
			}
			string settingsPath = SettingsPath();
			if (AssetDatabase.LoadAssetAtPath<PointOfInterestSettings>(settingsPath) != null)
			{
				AssetDatabase.DeleteAsset(settingsPath);
			}
			SceneNavMeshCatalogueEditor.Remove(ProbeName);
			// Its baked spawn table: out of the catalogue and the server addressables group, then deleted.
			string tablePath = SpawnTableBaker.TablePathFor(ProbeName);
			var table = AssetDatabase.LoadAssetAtPath<FishMMO.Server.Implementation.World.SceneServer.Spawner.SceneSpawnTable>(tablePath);
			if (table != null)
			{
				var catalogue = SpawnTableBaker.GetOrCreateCatalogue();
				if (catalogue != null && catalogue.Tables.Remove(table))
				{
					EditorUtility.SetDirty(catalogue);
				}
				UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings?.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(tablePath));
				AssetDatabase.DeleteAsset(tablePath);
				AssetDatabase.SaveAssets();
			}
			string arrays = $"Assets/Prefabs/Client/TerrainArrays/{ProbeName}";
			if (AssetDatabase.IsValidFolder(arrays))
			{
				AssetDatabase.DeleteAsset(arrays);
			}
			WorldAtlasScene.EditorLookup.Invalidate();
		}

		private static string SettingsPath() => $"Assets/Templates/World/Atlas/Scenes/{ProbeName} POI Settings.asset";

		/// <summary>A capital and the requested density, saved so the generator can revive it by path.</summary>
		private static PointOfInterestSettings ProbeSettings(WorldAtlasScene entry)
		{
			var settings = ScriptableObject.CreateInstance<PointOfInterestSettings>();
			settings.Capital = Environment.GetEnvironmentVariable("FISHMMO_POI_CAPITAL") != "0";
			settings.Density = Enum.TryParse(Environment.GetEnvironmentVariable("FISHMMO_POI_DENSITY") ?? "Dense", true, out PointOfInterestDensity density)
				? density : PointOfInterestDensity.Dense;
			AssetDatabase.CreateAsset(settings, SettingsPath());
			AssetDatabase.SaveAssets();
			return settings;
		}

		/// <summary>The scene's ground rectangle (x, z) from its terrain tiles.</summary>
		private static Rect SceneArea(Scene scene)
		{
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					if (terrain.terrainData == null)
					{
						continue;
					}
					Vector3 p = terrain.GetPosition();
					Vector3 s = terrain.terrainData.size;
					minX = Mathf.Min(minX, p.x);
					minZ = Mathf.Min(minZ, p.z);
					maxX = Mathf.Max(maxX, p.x + s.x);
					maxZ = Mathf.Max(maxZ, p.z + s.z);
				}
			}
			return minX < maxX ? Rect.MinMaxRect(minX, minZ, maxX, maxZ) : new Rect(-1000f, -1000f, 2000f, 2000f);
		}

		/// <summary>Straight down over the whole scene, north up; markers.json maps every record to its pixel.</summary>
		private static void Overview(Camera camera, Rect area, List<PointOfInterestRecord> records, string output, List<string> report)
		{
			const int longSide = 2048;
			float aspect = area.width / Mathf.Max(1f, area.height);
			int width = aspect >= 1f ? longSide : Mathf.RoundToInt(longSide * aspect);
			int height = aspect >= 1f ? Mathf.RoundToInt(longSide / aspect) : longSide;
			camera.orthographic = true;
			camera.orthographicSize = area.height * 0.5f;
			camera.aspect = area.width / area.height;
			camera.nearClipPlane = 1f;
			camera.farClipPlane = 12000f;
			camera.transform.SetPositionAndRotation(new Vector3(area.center.x, 5000f, area.center.y), Quaternion.Euler(90f, 0f, 0f));
			Capture(camera, width, height, Path.Combine(output, "overview.png"));

			var json = new StringBuilder();
			json.Append("{\"width\":").Append(width).Append(",\"height\":").Append(height).Append(",\"points\":[");
			bool first = true;
			foreach (PointOfInterestRecord r in records)
			{
				float px = (r.Position.x - area.xMin) / area.width * width;
				float py = (1f - (r.Position.z - area.yMin) / area.height) * height;
				Color c = AtlasPointsOfInterest.GroupColour(r.Info.Group);
				if (!first)
				{
					json.Append(',');
				}
				first = false;
				json.Append("{\"id\":").Append(r.Id)
					.Append(",\"kind\":\"").Append(r.Kind).Append('"')
					.Append(",\"group\":\"").Append(r.Info.Group).Append('"')
					.Append(",\"name\":\"").Append(Escape(r.Name)).Append('"')
					.Append(",\"template\":\"").Append(Escape(r.Template)).Append('"')
					.Append(",\"tier\":").Append(r.DetailTier)
					.Append(",\"x\":").Append(px.ToString("F1", CultureInfo.InvariantCulture))
					.Append(",\"y\":").Append(py.ToString("F1", CultureInfo.InvariantCulture))
					.Append(",\"colour\":\"#").Append(ColorUtility.ToHtmlStringRGB(c)).Append("\"}");
			}
			json.Append("]}");
			File.WriteAllText(Path.Combine(output, "markers.json"), json.ToString());
			report.Add($"overview.png {width}x{height} over {area.width:0} x {area.height:0} m");
		}

		/// <summary>One shot per kind: built and shaped sites first, then the detected ones from further out.</summary>
		private static void CloseUps(Camera camera, List<PointOfInterestRecord> records, int maxShots, string output, List<string> report)
		{
			camera.orthographic = false;
			camera.fieldOfView = 50f;
			camera.nearClipPlane = 0.3f;
			camera.farClipPlane = 6000f;
			camera.aspect = (float)CloseWidth / CloseHeight;
			var chosen = records
				.GroupBy(r => r.Kind)
				.Select(g => g.OrderByDescending(r => r.SizeClass).ThenBy(r => r.Id).First())
				.OrderBy(r => r.Info.Has(PointOfInterestTraits.Detected) ? 1 : 0)
				.ThenBy(r => r.Info.Group)
				.ThenBy(r => r.Kind)
				.Take(maxShots)
				.ToList();
			int shot = 0;
			foreach (PointOfInterestRecord r in chosen)
			{
				bool detected = r.Info.Has(PointOfInterestTraits.Detected);
				bool underwater = r.Info.Has(PointOfInterestTraits.Underwater);
				bool face = r.Info.Has(PointOfInterestTraits.Terrain);
				float distance = detected ? Mathf.Clamp(r.Radius * 3f, 120f, 600f)
					: face ? Mathf.Clamp(r.Radius * 3f, 30f, 80f)
					: Mathf.Clamp(r.Radius * 2.1f, 25f, 320f);
				// Big sites from higher up, so the whole layout reads rather than its nearest wall.
				// Walled towns from well above: an eye beside a curtain wall sees only the wall.
				float elevation = detected ? 35f : face ? 18f : r.Radius > 90f ? 65f : Mathf.Lerp(32f, 55f, Mathf.InverseLerp(15f, 120f, r.Radius));
				// From in front of the site: its yaw faces out of a slope, and a built site's front faces -z rotated by yaw.
				float heading = r.Yaw + 180f;
				Vector3 focus = r.Position + Vector3.up * (detected ? 0f : face ? 3f : Mathf.Min(6f, r.Radius * 0.15f));
				Vector3 eye = Eye(focus, heading, elevation, distance, underwater);
				// Up until nothing of the ground stands between the eye and the site (a ridge hid whole villages).
				for (int tries = 0; tries < 6 && !underwater && Occluded(eye, focus); tries++)
				{
					elevation = Mathf.Min(80f, elevation + 9f);
					eye = Eye(focus, heading, elevation, distance, underwater);
				}
				camera.transform.position = eye;
				camera.transform.LookAt(focus);
				string file = $"site_{shot++:00}_{r.Kind}_{Slug(r.Name)}.png";
				Capture(camera, CloseWidth, CloseHeight, Path.Combine(output, file));
				report.Add($"{file}: {r.Kind} '{r.Name}' r={r.Radius:0} template={(string.IsNullOrEmpty(r.Template) ? "-" : r.Template)} at ({r.Position.x:0}, {r.Position.y:0}, {r.Position.z:0})");

				// A cave also from inside its chamber, looking back up the tunnel, lit by a lamp at the eye.
				ScenePointsOfInterest asset = PointOfInterestProbe.Asset(EditorSceneManager.GetActiveScene());
				if (face && asset != null && CaveShaper.TryChamber(asset, r.Id, out Vector3 floor, out float radius, out float chamberYaw))
				{
					Vector3 towardMouth = r.Position - floor;
					towardMouth.y = 0f;
					towardMouth = towardMouth.sqrMagnitude > 1e-4f ? towardMouth.normalized : Vector3.forward;
					Vector3 inside = floor - towardMouth * radius * 0.7f + Vector3.up * 1.7f;
					var lamp = new GameObject("POI Probe Lamp") { hideFlags = HideFlags.HideAndDontSave };
					try
					{
						Light light = lamp.AddComponent<Light>();
						light.type = LightType.Point;
						light.range = radius * 6f + 10f;
						light.intensity = 3f;
						lamp.transform.position = inside;
						camera.transform.position = inside;
						camera.transform.LookAt(floor + towardMouth * radius + Vector3.up * 1.2f);
						string insideFile = $"site_{shot++:00}_{r.Kind}_{Slug(r.Name)}_chamber.png";
						Capture(camera, CloseWidth, CloseHeight, Path.Combine(output, insideFile));
						report.Add($"{insideFile}: chamber floor ({floor.x:0}, {floor.y:0}, {floor.z:0}) radius {radius:0.0}");
					}
					finally
					{
						UnityEngine.Object.DestroyImmediate(lamp);
					}
				}
			}
		}

		/// <summary>
		/// The ways from a walker's height and from above: one of each class (a stretch 40 % along it), a ford and a bridge,
		/// each looking along the way; and the ways listed in paths.tsv.
		/// </summary>
		private static void PathShots(Camera camera, ScenePointsOfInterest asset, string output, List<string> report)
		{
			if (asset == null || asset.Paths == null || asset.Paths.Count == 0)
			{
				report.Add("paths: none");
				return;
			}
			var table = new StringBuilder("id\tclass\tfrom\tto\tpoints\tlength_m\tfords\tbridged\tmean_wear\n");
			foreach (ScenePath path in asset.Paths)
			{
				int fords = 0, bridged = 0;
				for (int i = 0; i < path.Count; i++)
				{
					fords += (path.FlagsAt(i) & ScenePathPointFlags.Ford) != 0 ? 1 : 0;
					bridged += (path.FlagsAt(i) & ScenePathPointFlags.Bridge) != 0 ? 1 : 0;
				}
				table.Append(string.Join("\t", path.Id, path.Class, path.FromId, path.ToId, path.Count, path.Length.ToString("0", CultureInfo.InvariantCulture),
					fords, bridged, (path.Wear.Length > 0 ? path.Wear.Average() : 0f).ToString("0.00", CultureInfo.InvariantCulture))).Append('\n');
			}
			File.WriteAllText(Path.Combine(output, "paths.tsv"), table.ToString());

			camera.orthographic = false;
			camera.fieldOfView = 55f;
			camera.nearClipPlane = 0.2f;
			camera.farClipPlane = 4000f;
			camera.aspect = (float)CloseWidth / CloseHeight;
			var shots = new List<(string label, ScenePath path, int index)>();
			foreach (ScenePathClass kind in (ScenePathClass[])Enum.GetValues(typeof(ScenePathClass)))
			{
				ScenePath path = asset.Paths.Where(p => p.Class == kind && p.Count > 10).OrderByDescending(p => p.Length).ThenBy(p => p.Id).FirstOrDefault();
				if (path == null)
				{
					continue;
				}
				int index = Mathf.Clamp(Mathf.RoundToInt(path.Count * 0.4f), 1, path.Count - 2);
				for (int k = 0; k < path.Count && (path.FlagsAt(index) & (ScenePathPointFlags.Bridge | ScenePathPointFlags.InSite)) != 0; k++)
				{
					index = Mathf.Clamp(index + 1, 1, path.Count - 2);
				}
				shots.Add((kind.ToString(), path, index));
			}
			foreach (ScenePathPointFlags flag in new[] { ScenePathPointFlags.Ford, ScenePathPointFlags.Bridge })
			{
				foreach (ScenePath path in asset.Paths.OrderBy(p => p.Id))
				{
					int index = Enumerable.Range(1, Mathf.Max(0, path.Count - 2)).FirstOrDefault(i => (path.FlagsAt(i) & flag) != 0);
					if (index > 0)
					{
						// Stand back from the crossing, on the bank, looking over it.
						shots.Add((flag.ToString(), path, Mathf.Max(1, index - 8)));
						break;
					}
				}
			}
			int shot = 0;
			foreach ((string label, ScenePath path, int index) in shots)
			{
				Vector3 at = path.Points[index];
				Vector3 ahead = path.Points[Mathf.Min(path.Count - 1, index + 6)];
				Vector3 along = ahead - at;
				along.y = 0f;
				along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.forward;
				float half = path.HalfWidth[index];
				// A walker's eye a few metres behind, and an eye above and behind for the line of the way.
				var views = new[]
				{
					("walk", at - along * (4f + half * 2f) + Vector3.up * 1.7f, at + along * 14f + Vector3.up * 0.4f),
					("above", at - along * (18f + half * 6f) + Vector3.up * (12f + half * 4f), at + along * 12f),
				};
				foreach ((string view, Vector3 eye, Vector3 look) in views)
				{
					Vector3 e = eye;
					float ground = GroundUnder(e);
					if (e.y < ground + 1.5f)
					{
						e.y = ground + 1.5f;
					}
					camera.transform.position = e;
					camera.transform.LookAt(look);
					string file = $"path_{shot:00}_{label}_{view}.png";
					Capture(camera, CloseWidth, CloseHeight, Path.Combine(output, file));
					report.Add($"{file}: path {path.Id} {path.Class} point {index}/{path.Count} at ({at.x:0}, {at.y:0}, {at.z:0}) half {half:0.00} wear {path.Wear[index]:0.00} surface {(ScenePathSurface)path.Surface[index]}");
				}
				shot++;
			}
		}

		private static Vector3 Eye(Vector3 focus, float heading, float elevation, float distance, bool underwater)
		{
			Vector3 eye = focus + Quaternion.Euler(-elevation, heading, 0f) * Vector3.forward * -distance;
			float ground = GroundUnder(eye);
			if (!underwater && eye.y < ground + 3f)
			{
				eye.y = ground + 3f;
			}
			return eye;
		}

		/// <summary>Whether the terrain rises above the sight line anywhere between the eye and the focus.</summary>
		private static bool Occluded(Vector3 eye, Vector3 focus)
		{
			for (int i = 1; i < 24; i++)
			{
				Vector3 p = Vector3.Lerp(eye, focus, i / 24f);
				if (GroundUnder(p) > p.y + 0.5f)
				{
					return true;
				}
			}
			return false;
		}

		private static float GroundUnder(Vector3 at)
		{
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				Vector3 p = terrain.GetPosition();
				Vector3 s = terrain.terrainData.size;
				if (at.x >= p.x && at.x <= p.x + s.x && at.z >= p.z && at.z <= p.z + s.z)
				{
					return p.y + terrain.SampleHeight(at);
				}
			}
			return float.NegativeInfinity;
		}

		private static void Capture(Camera camera, int width, int height, string path)
		{
			var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			try
			{
				for (int i = 0; i < 3; i++)
				{
					camera.Render();
				}
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = target;
				var image = new Texture2D(width, height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				image.Apply();
				RenderTexture.active = previous;
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				camera.targetTexture = null;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		private static void AddUrpData(Camera camera)
		{
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.55f, 0.68f, 0.82f);
			Type cameraData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
			if (cameraData != null)
			{
				Component data = camera.gameObject.AddComponent(cameraData);
				cameraData.GetProperty("requiresDepthTexture")?.SetValue(data, true);
			}
		}

		private static string Escape(string text) => (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

		private static string Slug(string text)
		{
			var sb = new StringBuilder();
			foreach (char c in text ?? string.Empty)
			{
				sb.Append(char.IsLetterOrDigit(c) ? c : '-');
			}
			return sb.Length > 32 ? sb.ToString(0, 32) : sb.ToString();
		}

		/// <summary>
		/// A sun and flat ambient for edit mode, where nothing lights a generated scene (the sky system makes its lights
		/// at runtime only); everything put back on dispose.
		/// </summary>
		private sealed class ProbeLighting : IDisposable
		{
			private readonly GameObject sun;
			private readonly UnityEngine.Rendering.AmbientMode ambientMode;
			private readonly Color ambient;
			private readonly bool fog;

			public ProbeLighting()
			{
				ambientMode = RenderSettings.ambientMode;
				ambient = RenderSettings.ambientLight;
				fog = RenderSettings.fog;
				RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
				RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.5f);
				RenderSettings.fog = false;
				sun = new GameObject("POI Probe Sun") { hideFlags = HideFlags.HideAndDontSave };
				Light light = sun.AddComponent<Light>();
				light.type = LightType.Directional;
				light.intensity = 1.25f;
				light.color = new Color(1f, 0.96f, 0.9f);
				light.shadows = LightShadows.Soft;
				sun.transform.rotation = Quaternion.Euler(48f, 135f, 0f);
				RenderSettings.sun = light;
			}

			public void Dispose()
			{
				RenderSettings.ambientMode = ambientMode;
				RenderSettings.ambientLight = ambient;
				RenderSettings.fog = fog;
				UnityEngine.Object.DestroyImmediate(sun);
			}
		}
	}
}
#endif
