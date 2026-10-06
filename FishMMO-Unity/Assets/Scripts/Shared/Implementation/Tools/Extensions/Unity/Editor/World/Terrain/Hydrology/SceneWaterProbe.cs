#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Photographs a REAL saved scene's rivers and falls as the editor draws them (its terrain, its baked props, its
	/// inland water), and measures where the water stands above its banks (water over air, the "floating" pools).
	/// FISHMMO_SCENE: the scene path (default Flo Monolith); FISHMMO_SCENEWATER_OUT: where the PNGs and report go;
	/// FISHMMO_SCENEWATER_FALLS: how many of the largest falls to shoot (default 4). Saves nothing into the project.
	/// </summary>
	public static class SceneWaterProbe
	{
		private const int Width = 1600;

		/// <summary>
		/// How far outside the water's edge the banks are read, metres (FISHMMO_SCENEWATER_OUTSIDE, default 1): at the edge
		/// itself a sample blends the channel's floor with the bank beside it and reads the bank metres low.
		/// </summary>
		private static float ProbeOutside => float.TryParse(Environment.GetEnvironmentVariable("FISHMMO_SCENEWATER_OUTSIDE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 1f;
		private const int Height = 900;

		public static void Run()
		{
			string scenePath = Environment.GetEnvironmentVariable("FISHMMO_SCENE");
			if (string.IsNullOrEmpty(scenePath))
			{
				scenePath = "Assets/Scenes/WorldScene/Arthis/Flo Monolith.unity";
			}
			string outDir = Environment.GetEnvironmentVariable("FISHMMO_SCENEWATER_OUT");
			if (string.IsNullOrEmpty(outDir))
			{
				outDir = Path.Combine(Path.GetTempPath(), "scene-water");
			}
			int fallCount = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_SCENEWATER_FALLS"), out int n) ? n : 4;
			Directory.CreateDirectory(outDir);
			var report = new List<string>();

			EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			var renderer = UnityEngine.Object.FindAnyObjectByType<InlandWaterRenderer>();
			var bodies = renderer != null ? renderer.GetComponent<SceneWaterBodies>() : null;
			if (renderer == null || bodies == null || bodies.Hydrology == null)
			{
				Debug.LogError($"[Scene water probe] {scenePath}: no inland water in the scene.");
				return;
			}
			SceneHydrology hydrology = bodies.Hydrology;
			bodies.Build();
			renderer.Rebuild();
			renderer.SetFlow(RiverFlowBake.SolveInMemory(hydrology));
			report.Add($"scene {scenePath}: {hydrology.Rivers.Count} rivers, {hydrology.Lakes.Count} lakes, {renderer.Falls.Count} falls, {Terrain.activeTerrains.Length} terrains");

			Banks(hydrology, report);
			if (Environment.GetEnvironmentVariable("FISHMMO_SCENEWATER_SHAPE") == "1")
			{
				CompareWithShaped(System.IO.Path.GetFileNameWithoutExtension(scenePath), hydrology, report, Path.Combine(outDir, "banks.csv"));
				File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
				Debug.Log("[Scene water probe] shape comparison written.\n" + string.Join("\n", report));
				return;
			}
			foreach (InlandWaterRenderer.Fall f in renderer.Falls)
			{
				report.Add($"fall river {f.River} lip {f.Lip} plunge {f.Plunge} foot {f.Foot}: drop {f.Drop:0.0} m width {f.Width:0.0} m speed {f.Speed:0.0} landing {f.Landing} foot {f.FootPoint} (landing {Vector3.Distance(new Vector3(f.Landing.x, 0, f.Landing.z), new Vector3(f.FootPoint.x, 0, f.FootPoint.z)):0.0} m from the foot)");
			}

			// Light and a reflection, as the game has them (a batch editor has neither baked).
			Light sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.isActiveAndEnabled);
			if (sun == null)
			{
				sun = new GameObject("Probe Sun").AddComponent<Light>();
				sun.type = LightType.Directional;
				sun.intensity = 1.2f;
				sun.shadows = LightShadows.Soft;
				sun.transform.rotation = Quaternion.Euler(42f, 210f, 0f);
			}
			RenderSettings.sun = sun;

			var camera = new GameObject("Probe Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.fieldOfView = 50f;
			camera.nearClipPlane = 0.2f;
			camera.farClipPlane = 6000f;
			Type cameraData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
			if (cameraData != null)
			{
				Component data = camera.gameObject.AddComponent(cameraData);
				cameraData.GetProperty("requiresDepthTexture")?.SetValue(data, true);
				cameraData.GetProperty("requiresColorTexture")?.SetValue(data, true);
				cameraData.GetProperty("renderShadows")?.SetValue(data, true);
			}
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;

			// The largest falls, and any fall with another close below it (a cascade) once.
			var shots = renderer.Falls.OrderByDescending(f => f.Drop).Take(fallCount).ToList();
			foreach (InlandWaterRenderer.Fall f in renderer.Falls)
			{
				bool cascade = renderer.Falls.Any(o => o.River == f.River && o.Lip > f.Foot && o.Lip - f.Foot <= 12);
				if (cascade && !shots.Any(s => s.River == f.River && Mathf.Abs(s.Lip - f.Lip) <= 30))
				{
					shots.Add(f);
				}
			}
			var probe = new GameObject("Probe Reflection").AddComponent<ReflectionProbe>();
			probe.mode = ReflectionProbeMode.Realtime;
			probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
			probe.size = new Vector3(4000f, 2000f, 4000f);
			probe.resolution = 128;

			int index = 0;
			foreach (InlandWaterRenderer.Fall f in shots)
			{
				Vector3 down = f.FootPoint - f.LipPoint;
				down.y = 0f;
				down = down.sqrMagnitude > 1e-4f ? down.normalized : Vector3.forward;
				var side = new Vector3(-down.z, 0f, down.x);
				Vector3 middle = Vector3.Lerp(f.LipPoint, f.Landing, 0.5f);
				float back = Mathf.Max(14f, f.Drop * 1.3f + f.Width * 1.5f);
				probe.transform.position = middle + Vector3.up * 20f;
				probe.RenderProbe();
				var views = new List<(string, Vector3, Vector3)>
				{
					("front", f.Landing + down * back + Vector3.up * (f.Drop * 0.3f + 2f), middle),
					("side", middle + side * back + Vector3.up * (f.Drop * 0.15f + 2f), middle),
					("high", f.Landing + down * (back * 0.6f) - side * (back * 0.4f) + Vector3.up * (f.Drop + back * 0.7f), f.Landing),
					("top", f.Landing + Vector3.up * (back * 1.4f + f.Drop) + down * 0.1f, f.Landing),
				};
				foreach ((string name, Vector3 from, Vector3 at) in views)
				{
					Vector3 eye = from;
					float ground = GroundAt(eye.x, eye.z);
					if (!float.IsNegativeInfinity(ground) && eye.y < ground + 1.5f)
					{
						eye.y = ground + 1.5f;
					}
					camera.transform.position = eye;
					camera.transform.LookAt(at);
					string file = $"fall{index:00}_r{f.River}_l{f.Lip}_{name}.png";
					Capture(camera, target, Path.Combine(outDir, file));
					report.Add($"{file}: from {eye} to {at}");
				}
				index++;
			}
			File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
			Debug.Log($"[Scene water probe] {index} falls shot into '{outDir}'.\n" + string.Join("\n", report.Take(60)));
		}

		/// <summary>
		/// Where the water stands above its banks: at each river point, the ground at both edges of the water against
		/// the surface. Water whose bank is lower than it hangs over air; ground over the middle of the channel hides it.
		/// </summary>
		private static void Banks(SceneHydrology hydrology, List<string> report)
		{
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null || river.Points.Length < 2)
				{
					continue;
				}
				int floating = 0, buried = 0, n = river.Points.Length;
				var worst = new List<(float gap, int point, float bankLeft, float bankRight)>();
				for (int i = 0; i < n; i++)
				{
					Vector3 p = river.Points[i];
					Vector3 ahead = river.Points[Mathf.Min(n - 1, i + 1)] - river.Points[Mathf.Max(0, i - 1)];
					ahead.y = 0f;
					ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
					var left = new Vector3(-ahead.z, 0f, ahead.x);
					float half = 0.5f * (river.Width != null && i < river.Width.Length ? river.Width[i] : 4f) + ProbeOutside;
					float l = GroundAt(p.x + left.x * half, p.z + left.z * half);
					float r = GroundAt(p.x - left.x * half, p.z - left.z * half);
					float centre = GroundAt(p.x, p.z);
					float gap = p.y - Mathf.Min(l, r);
					if (gap > 0.3f)
					{
						floating++;
						worst.Add((gap, i, p.y - l, p.y - r));
					}
					if (centre > p.y + 0.1f)
					{
						buried++;
					}
				}
				report.Add($"river {river.Id}: {n} points; water above a bank by >0.3 m at {floating} ({100f * floating / n:0.0}%), ground over the water's middle at {buried}");
				foreach (var w in worst.OrderByDescending(w => w.gap).Take(12))
				{
					string reach = river.Reach != null && w.point < river.Reach.Length ? river.Reach[w.point].ToString() : "?";
					report.Add($"   point {w.point} (reach {reach}): water {w.bankLeft:0.00} m over the left bank, {w.bankRight:0.00} m over the right");
				}
			}
		}

		/// <summary>
		/// The scene's ground shaped again in memory, as the generator shaped it before anything after the water touched the
		/// terrain (cliff talus, props, re-paints), against the saved terrain at every river point where the water stands
		/// over a bank: says whether the saved ground was lowered after the carve, or the carve itself left the water high.
		/// </summary>
		private static void CompareWithShaped(string sceneName, SceneHydrology hydrology, List<string> report, string csvPath)
		{
			var csv = new List<string> { "river,point,reach,surface,bed,width,savedLeft,savedRight,shapedLeft,shapedRight,shapedCentre,savedCentre" };
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == sceneName);
			if (entry == null || entry.Body == null)
			{
				report.Add($"shape: no atlas entry '{sceneName}'");
				return;
			}
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
				ErosionStrength = entry.ErosionStrength,
			};
			SolarSystemProfile system = SolarSystemProfile.Resolve(entry.Body);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			SceneHeightField field = SceneGround.Shape(request, plan, system, new List<string>(), out _, out SceneWater water);
			report.Add($"shape: re-shaped {field.Width}x{field.Depth} at {field.Spacing} m; in-memory rivers {water?.Rivers?.Count ?? 0}");
			// The generator against itself: its own rivers over the very ground it carved them into, in this one run.
			var self = new List<string> { "river,point,reach,surface,bed,width,left,right,centre,end,joins" };
			foreach (RiverPath path in water?.Rivers ?? new List<RiverPath>())
			{
				if (!path.Perennial || path.Count < 2)
				{
					continue;
				}
				int floating = 0, hanging = 0;
				float worst = 0f;
				for (int i = 0; i < path.Count; i++)
				{
					float ax = path.X[Mathf.Min(path.Count - 1, i + 1)] - path.X[Mathf.Max(0, i - 1)];
					float az = path.Z[Mathf.Min(path.Count - 1, i + 1)] - path.Z[Mathf.Max(0, i - 1)];
					float len = Mathf.Max(1e-4f, Mathf.Sqrt(ax * ax + az * az));
					float lx = -az / len, lz = ax / len, half = 0.5f * path.Width[i] + ProbeOutside;
					float left = field.MetresAt(path.X[i] + lx * half, path.Z[i] + lz * half);
					float right = field.MetresAt(path.X[i] - lx * half, path.Z[i] - lz * half);
					float centre = field.MetresAt(path.X[i], path.Z[i]);
					float gap = path.Surface[i] - Mathf.Min(left, right);
					if (gap > 0.3f)
					{
						floating++;
						worst = Mathf.Max(worst, gap);
					}
					if (path.Bed[i] - centre > 0.5f)
					{
						hanging++;
					}
					self.Add(string.Join(",", path.Id, i, (int)path.Reach[i], path.Surface[i].ToString("0.00"), path.Bed[i].ToString("0.00"), path.Width[i].ToString("0.0"),
						left.ToString("0.00"), right.ToString("0.00"), centre.ToString("0.00"), path.End, path.JoinsRiver));
				}
				int crossings = 0;
				for (int a = 0; a + 1 < path.Count; a++)
				{
					for (int b = a + 2; b + 1 < path.Count; b++)
					{
						if (Crosses(path.X[a], path.Z[a], path.X[a + 1], path.Z[a + 1], path.X[b], path.Z[b], path.X[b + 1], path.Z[b + 1]))
						{
							crossings++;
						}
					}
				}
				report.Add($"self river {path.Id} (ends {path.End}, joins {path.JoinsRiver}): {path.Count} points, {crossings} self-crossings; water over a bank by >0.3 m at {floating} (worst {worst:0.0} m); bed over the carved ground at {hanging}");
			}
			File.WriteAllLines(Path.ChangeExtension(csvPath, null) + "-self.csv", self);
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null)
				{
					continue;
				}
				int n = river.Points.Length, floating = 0, loweredAfter = 0, highInShape = 0;
				float sumLowered = 0f, maxLowered = 0f, maxShapeGap = 0f;
				for (int i = 0; i < n; i++)
				{
					Vector3 p = river.Points[i];
					Vector3 ahead = river.Points[Mathf.Min(n - 1, i + 1)] - river.Points[Mathf.Max(0, i - 1)];
					ahead.y = 0f;
					ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
					var left = new Vector3(-ahead.z, 0f, ahead.x);
					float half = 0.5f * (river.Width != null && i < river.Width.Length ? river.Width[i] : 4f);
					csv.Add(string.Join(",", river.Id, i, river.Reach != null && i < river.Reach.Length ? river.Reach[i] : 0, p.y.ToString("0.00"),
						(river.Bed != null && i < river.Bed.Length ? river.Bed[i] : 0f).ToString("0.00"), (2f * half).ToString("0.0"),
						GroundAt(p.x + left.x * half, p.z + left.z * half).ToString("0.00"), GroundAt(p.x - left.x * half, p.z - left.z * half).ToString("0.00"),
						field.MetresAt(p.x + left.x * half, p.z + left.z * half).ToString("0.00"), field.MetresAt(p.x - left.x * half, p.z - left.z * half).ToString("0.00"),
						field.MetresAt(p.x, p.z).ToString("0.00"), GroundAt(p.x, p.z).ToString("0.00")));
					foreach (float sideSign in new[] { 1f, -1f })
					{
						float x = p.x + left.x * half * sideSign, z = p.z + left.z * half * sideSign;
						float saved = GroundAt(x, z);
						if (float.IsNegativeInfinity(saved) || p.y - saved <= 0.3f)
						{
							continue;
						}
						floating++;
						float shaped = field.MetresAt(x, z);
						float lowered = shaped - saved;
						if (lowered > 1f)
						{
							loweredAfter++;
							sumLowered += lowered;
							maxLowered = Mathf.Max(maxLowered, lowered);
						}
						if (p.y - shaped > 0.3f)
						{
							highInShape++;
							maxShapeGap = Mathf.Max(maxShapeGap, p.y - shaped);
						}
					}
				}
				File.WriteAllLines(csvPath, csv);
				report.Add($"shape river {river.Id}: {floating} bank samples under the water; {loweredAfter} of them lowered after shaping by >1 m (mean {(loweredAfter > 0 ? sumLowered / loweredAfter : 0f):0.0}, max {maxLowered:0.0}); {highInShape} already under the water in the shaped ground (max {maxShapeGap:0.0} m)");
			}
		}

		private static bool Crosses(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
		{
			float Side(float ox, float oz, float px, float pz, float qx, float qz) => (px - ox) * (qz - oz) - (pz - oz) * (qx - ox);
			return Side(ax, az, bx, bz, cx, cz) * Side(ax, az, bx, bz, dx, dz) < 0f && Side(cx, cz, dx, dz, ax, az) * Side(cx, cz, dx, dz, bx, bz) < 0f;
		}

		private static float GroundAt(float x, float z)
		{
			float best = float.NegativeInfinity;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
				if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
				{
					continue;
				}
				best = Mathf.Max(best, terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y);
			}
			return best;
		}

		private static void Capture(Camera camera, RenderTexture target, string path)
		{
			for (int i = 0; i < 3; i++)
			{
				camera.Render();
			}
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}
	}
}
#endif
