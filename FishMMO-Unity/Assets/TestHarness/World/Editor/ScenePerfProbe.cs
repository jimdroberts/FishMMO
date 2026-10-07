using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using FishMMO.Water;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Where a world scene's frame goes: plays the scene (its World Sim bed drives the sky and weather), renders it
	/// from a few viewpoints at the player's resolution, and times it. Each viewpoint gets its frame time (renders
	/// back to back, waited on), and the GPU and CPU time of every profiler marker over a run of natural frames; the
	/// first viewpoint also gets each system switched off in turn, so its cost is the difference.
	/// </summary>
	/// <remarks>
	/// Launched by <c>memory/tools/perfrun.sh</c> as a GUI editor under xvfb (play mode needs presented frames).
	/// Environment: FISHMMO_PERF_OUT (report folder), FISHMMO_SCENE (scene path; Flo Monolith), FISHMMO_PERF_VIEWS
	/// (meadow,fall,overview), FISHMMO_PERF_VARIANTS ("all", "none" or a comma list), FISHMMO_PERF_RES (2560x1440),
	/// FISHMMO_PERF_SETTLE (seconds before the first measurement; 45). Writes ScenePerf-report.txt and a PNG per view.
	/// </remarks>
	public static class ScenePerfProbe
	{
		private const string StateKey = "FishMMO.ScenePerfProbe.Out";
		private const string DefaultScene = "Assets/Scenes/WorldScene/Arthis/Flo Monolith.unity";
		private const int TimedFrames = 60;
		private const int WaitFrames = 60;
		private const int SampledFrames = 60;

		private static string output;
		private static readonly List<string> report = new List<string>();
		private static readonly List<Step> steps = new List<Step>();
		private static int stepIndex;
		private static int frame;
		private static double settleUntil;
		private static Camera camera;
		private static RenderTexture target;
		private static readonly Dictionary<string, Recorder> recorders = new Dictionary<string, Recorder>();
		private static readonly Dictionary<string, double> gpuSum = new Dictionary<string, double>();
		private static readonly Dictionary<string, double> cpuSum = new Dictionary<string, double>();
		private static int sampled;
		private static float baseMilliseconds;
		private static float baseNatural;
		private static bool weatherApplied;
		private static string weatherName = "scene";

		/// <summary>
		/// FISHMMO_PERF_WEATHER: the weather every measurement is taken in, set through the scene's World Sim bed before the
		/// settle (clear, fair, overcast, rain, storm overhead, storm-far 5 km off); unset, the scene's own.
		/// </summary>
		private static void ApplyWeather()
		{
			string asked = Environment.GetEnvironmentVariable("FISHMMO_PERF_WEATHER");
			WorldSimController controller = UnityEngine.Object.FindAnyObjectByType<WorldSimController>();
			if (string.IsNullOrEmpty(asked) || controller == null)
			{
				return;
			}
			weatherName = asked;
			AirOffsets Air(float humidity = 0f, float pressure = 0f, float instability = 0f, float wind = 0f) =>
				new AirOffsets { Humidity = humidity, Pressure = pressure, Instability = instability, Wind = wind };
			controller.ClearAll(0f);
			controller.FieldDriven = false;
			switch (asked)
			{
				case "clear": controller.SetAir(Air(humidity: -0.4f, pressure: 0.8f), 0f); break;
				case "fair": controller.SetAir(Air(humidity: -0.1f, pressure: 0.2f), 0f); break;
				case "overcast": controller.SetAir(Air(humidity: 0.25f, pressure: -0.1f), 0f); break;
				case "rain": controller.SetAir(Air(humidity: 0.45f, pressure: -0.5f, instability: 0.3f), 0f); break;
				case "storm":
					controller.SetAir(Air(humidity: 0.45f, pressure: -0.5f, instability: 0.3f), 0f);
					controller.SpawnCell(StormKind.Thunderstorm, 0f, 0f, overhead: true);
					break;
				case "storm-far":
				{
					controller.SetAir(Air(humidity: -0.1f, instability: 0.3f), 0f);
					controller.SpawnCell(StormKind.Thunderstorm, 0f, 0f, overhead: true);
					WeatherTimeline timeline = controller.Timeline;
					for (int i = 0; i < timeline.Cells.Count; i++)
					{
						StormCell cell = timeline.Cells[i];
						cell.OriginX += 5000f;
						timeline.UpsertCell(cell);
					}
					break;
				}
			}
			controller.ForcePresent();
		}
		private static Action revert;

		private struct Step
		{
			public string View;
			public string Variant;
			public bool Breakdown;
		}

		public static void Run()
		{
			output = Environment.GetEnvironmentVariable("FISHMMO_PERF_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-perf");
			}
			Directory.CreateDirectory(output);
			SessionState.SetString(StateKey, output);
			string scene = Environment.GetEnvironmentVariable("FISHMMO_SCENE");
			EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? DefaultScene : scene, OpenSceneMode.Single);
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
			EditorApplication.EnterPlaymode();
		}

		/// <summary>Play mode reloads the domain; the session key carries the run across.</summary>
		[InitializeOnLoadMethod]
		private static void Resume()
		{
			if (string.IsNullOrEmpty(SessionState.GetString(StateKey, string.Empty)))
			{
				return;
			}
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
			if (EditorApplication.isPlaying)
			{
				Begin();
			}
		}

		private static void OnPlayMode(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.EnteredPlayMode)
			{
				Begin();
			}
		}

		private static void Begin()
		{
			output = SessionState.GetString(StateKey, output);
			report.Clear();
			steps.Clear();
			weatherApplied = false;
			weatherName = "scene";
			stepIndex = -1;
			frame = 0;
			float settle = float.TryParse(Environment.GetEnvironmentVariable("FISHMMO_PERF_SETTLE"), out float s) ? s : 45f;
			settleUntil = EditorApplication.timeSinceStartup + settle;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static void Pump()
		{
			try
			{
				if (!EditorApplication.isPlaying)
				{
					return;
				}
				if (stepIndex < 0)
				{
					if (!weatherApplied)
					{
						weatherApplied = true;
						ApplyWeather();
					}
					if (EditorApplication.timeSinceStartup < settleUntil)
					{
						return;
					}
					Prepare();
					stepIndex = 0;
					frame = 0;
					Enter(steps[0]);
					return;
				}
				Step step = steps[stepIndex];
				frame++;
				if (step.Breakdown && frame == WaitFrames - SampledFrames / 2)
				{
					// Unity's profiler over the same natural frames: the CPU's time by marker, scripts included.
					ProfilerDriver.ClearAllFrames();
					ProfilerDriver.profileEditor = false;
					ProfilerDriver.enabled = true;
					Profiler.enabled = true;
					WrapHooks();
				}
				if (frame > WaitFrames - SampledFrames / 2 && frame <= WaitFrames - SampledFrames / 2 + SampledFrames)
				{
					Sample();
				}
				if (frame < WaitFrames + SampledFrames / 2)
				{
					return;
				}
				Measure(step);
				revert?.Invoke();
				revert = null;
				stepIndex++;
				frame = 0;
				if (stepIndex >= steps.Count)
				{
					Finish(0);
					return;
				}
				Enter(steps[stepIndex]);
			}
			catch (Exception e)
			{
				report.Add($"FAILED: {e}");
				Finish(1);
			}
		}

		// ── Setting up ──────────────────────────────────────────────

		private static void Prepare()
		{
			WorldSimController controller = UnityEngine.Object.FindAnyObjectByType<WorldSimController>();
			camera = controller != null && controller.Camera != null ? controller.Camera : Camera.main;
			if (camera == null)
			{
				throw new InvalidOperationException("no camera in the scene");
			}
			if (controller != null)
			{
				// Noon, the clock held: every viewpoint and variant sees the same sky.
				controller.Paused = true;
				controller.JumpTo(0.5);
			}
			string res = Environment.GetEnvironmentVariable("FISHMMO_PERF_RES") ?? "2560x1440";
			string[] wh = res.Split('x');
			target = new RenderTexture(int.Parse(wh[0]), int.Parse(wh[1]), 24, RenderTextureFormat.ARGB32);
			camera.targetTexture = target;

			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			report.Add($"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {target.width}x{target.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, " +
				$"pipeline {(pipeline != null ? pipeline.name : "none")} (render scale {pipeline?.renderScale:0.00}, shadow distance {pipeline?.shadowDistance:0}, cascades {pipeline?.shadowCascadeCount}, MSAA {pipeline?.msaaSampleCount}, HDR {pipeline?.supportsHDR}), " +
				$"GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), GPU recorder {(SystemInfo.supportsGpuRecorder ? "yes" : "NO")}");
			report.Add($"camera far {camera.farClipPlane:0}, fov {camera.fieldOfView:0}, lodBias {QualitySettings.lodBias:0.00}; weather {weatherName}");
			report.Add($"render-pipeline hooks: {Hooks()}");

			var names = new List<string>();
			Sampler.GetNames(names);
			foreach (string name in names)
			{
				Recorder recorder = Recorder.Get(name);
				if (recorder != null && recorder.isValid)
				{
					recorder.enabled = true;
					recorders[name] = recorder;
				}
			}

			string views = Environment.GetEnvironmentVariable("FISHMMO_PERF_VIEWS") ?? "meadow,fall,overview";
			string variants = Environment.GetEnvironmentVariable("FISHMMO_PERF_VARIANTS") ?? "all";
			string[] viewList = views.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
			for (int v = 0; v < viewList.Length; v++)
			{
				steps.Add(new Step { View = viewList[v], Variant = "base", Breakdown = true });
				if (v > 0 && Environment.GetEnvironmentVariable("FISHMMO_PERF_VARIANTS_EVERY_VIEW") == "1")
				{
					// Looks, not timings: every view in every variant (the grass angles before and after a change).
					foreach (string variant in Variants(variants))
					{
						steps.Add(new Step { View = viewList[v], Variant = variant });
					}
				}
				if (v == 0)
				{
					// Each variant beside a fresh base: the frame drifts as a run warms up (5–6 ms over a run in a storm), so
					// a variant is only comparable with the base measured just before it.
					foreach (string variant in Variants(variants))
					{
						steps.Add(new Step { View = viewList[v], Variant = "rebase" });
						steps.Add(new Step { View = viewList[v], Variant = variant });
					}
					// The base again, settled: the first measurement of a run reads high while the scene warms up.
					steps.Add(new Step { View = viewList[v], Variant = "rebase" });
				}
			}
		}

		private static IEnumerable<string> Variants(string asked)
		{
			if (asked == "none")
			{
				yield break;
			}
			if (asked != "all")
			{
				foreach (string v in asked.Split(','))
				{
					yield return v.Trim();
				}
				yield break;
			}
			foreach (ScriptableRendererFeature feature in Features())
			{
				if (feature != null && feature.isActive)
				{
					yield return "feature:" + feature.name;
				}
			}
			foreach (string v in new[] { "godrays", "cloudshadows", "details", "shadows", "post", "scale50", "terrain", "water" })
			{
				yield return v;
			}
		}

		private static List<ScriptableRendererFeature> Features()
		{
			var all = new List<ScriptableRendererFeature>();
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (pipeline == null)
			{
				return all;
			}
			FieldInfo list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
			if (list?.GetValue(pipeline) is ScriptableRendererData[] data)
			{
				foreach (ScriptableRendererData d in data)
				{
					if (d != null)
					{
						all.AddRange(d.rendererFeatures);
					}
				}
			}
			return all;
		}

		// ── A step ──────────────────────────────────────────────────

		private static void Enter(Step step)
		{
			Place(step.View);
			revert = Apply(step.Variant);
			gpuSum.Clear();
			cpuSum.Clear();
			sampled = 0;
		}

		private static void Place(string view)
		{
			Vector3 eye, look;
			switch (view)
			{
				case "grass-eye":
				case "grass-third":
				case "grass-high":
				case "grass-top":
					GrassView(view, out eye, out look, out Vector3 up);
					camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, up));
					return;
				case "fall":
					if (Fall(out eye, out look))
					{
						break;
					}
					goto default;
				case "overview":
					eye = new Vector3(0f, GroundAt(0f, 0f) + 350f, -600f);
					look = new Vector3(0f, GroundAt(0f, 0f), 0f);
					break;
				default:
				{
					// A player standing on open land nearest the middle of the scene (its middle may be sea), looking
					// out level across the longest way over land.
					Vector2 at = Land();
					eye = new Vector3(at.x, GroundAt(at.x, at.y) + 1.8f, at.y);
					Vector3 best = Vector3.forward;
					float longest = -1f;
					for (int k = 0; k < 16; k++)
					{
						float angle = k * Mathf.PI / 8f;
						var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
						float run = 0f;
						while (run < 1500f && GroundAt(at.x + dir.x * (run + 25f), at.y + dir.z * (run + 25f)) > SeaLevel() + 2f)
						{
							run += 25f;
						}
						if (run > longest)
						{
							longest = run;
							best = dir;
						}
					}
					look = eye + (best + new Vector3(0f, -0.03f, 0f)) * 100f;
					break;
				}
			}
			camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
		}

		/// <summary>In front of the scene's biggest fall, as a player would stand to look at it.</summary>
		private static bool Fall(out Vector3 eye, out Vector3 look)
		{
			eye = look = default;
			SceneWaterBodies bodies = UnityEngine.Object.FindAnyObjectByType<SceneWaterBodies>();
			if (bodies == null || bodies.Hydrology == null)
			{
				return false;
			}
			List<InlandWaterRenderer.Fall> falls = InlandWaterRenderer.FindFalls(bodies.Hydrology, 1.5f);
			if (falls.Count == 0)
			{
				return false;
			}
			InlandWaterRenderer.Fall f = falls.OrderByDescending(x => x.Drop).First();
			Vector3 down = f.FootPoint - f.LipPoint;
			down.y = 0f;
			down = down.sqrMagnitude > 1e-4f ? down.normalized : Vector3.forward;
			float back = Mathf.Max(18f, f.Drop * 1.8f + f.Width * 1.5f);
			Vector3 at = f.FootPoint + down * back;
			eye = new Vector3(at.x, Mathf.Max(GroundAt(at.x, at.z) + 1.8f, f.FootPoint.y + 1.8f), at.z);
			look = Vector3.Lerp(f.LipPoint, f.FootPoint, 0.5f);
			return true;
		}

		private static float SeaLevel() => SurfaceWater.TryGetLevel(out float sea) ? sea : 0f;

		private static Vector2? grassSpot;
		private static Vector3 grassDirection;

		/// <summary>
		/// The grass views, round the densest blade grass near the scene's middle: a player's eye (1.7 m, level), a
		/// third-person camera (about 3 m up, 20 degrees down), a high oblique (25 m up, 45 degrees) and straight down from 8 m.
		/// </summary>
		private static void GrassView(string view, out Vector3 eye, out Vector3 look, out Vector3 up)
		{
			Vector2 at2 = GrassSpot();
			Vector3 dir = grassDirection;
			float ground = GroundAt(at2.x, at2.y);
			var at = new Vector3(at2.x, ground, at2.y);
			up = Vector3.up;
			switch (view)
			{
				case "grass-eye":
					eye = at + Vector3.up * 1.7f;
					look = eye + (dir + Vector3.down * 0.05f) * 50f;
					break;
				case "grass-third":
					eye = at - dir * 6f;
					eye.y = GroundAt(eye.x, eye.z) + 3.2f;
					look = at + dir * 4f + Vector3.up * 0.4f;
					look.y = GroundAt(look.x, look.z) + 0.4f;
					break;
				case "grass-high":
					eye = at - dir * 25f;
					eye.y = ground + 25f;
					look = at;
					break;
				default:
					eye = at + Vector3.up * 8f;
					look = at;
					up = dir;
					break;
			}
		}

		/// <summary>The densest blade grass (Detail_Grass* detail layers, summed over 9 x 9 cells) on a 25 m grid within 800 m of the middle, on gentle land.</summary>
		private static Vector2 GrassSpot()
		{
			if (grassSpot.HasValue)
			{
				return grassSpot.Value;
			}
			float sea = SeaLevel();
			Vector2 best = Land();
			float bestScore = -1f;
			for (float z = -800f; z <= 800f; z += 25f)
			{
				for (float x = -800f; x <= 800f; x += 25f)
				{
					float h = GroundAt(x, z);
					float slope = Mathf.Max(Mathf.Abs(GroundAt(x + 4f, z) - h), Mathf.Abs(GroundAt(x, z + 4f) - h)) / 4f;
					if (h < sea + 3f || slope > 0.2f)
					{
						continue;
					}
					float score = GrassAt(x, z) - Mathf.Sqrt(x * x + z * z) * 0.01f;
					if (score > bestScore)
					{
						bestScore = score;
						best = new Vector2(x, z);
					}
				}
			}
			// Look along the grass that runs furthest from there.
			float longest = -1f;
			grassDirection = Vector3.forward;
			for (int k = 0; k < 16; k++)
			{
				float angle = k * Mathf.PI / 8f;
				var d = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
				float run = 0f;
				while (run < 400f && GrassAt(best.x + d.x * (run + 10f), best.y + d.z * (run + 10f)) > 0f)
				{
					run += 10f;
				}
				if (run > longest)
				{
					longest = run;
					grassDirection = d;
				}
			}
			grassSpot = best;
			report.Add($"grass views round ({best.x:0}, {best.y:0}), grass score {bestScore:0}, looking ({grassDirection.x:0.00}, {grassDirection.z:0.00}) along {longest:0} m of grass");
			return best;
		}

		private static float GrassAt(float x, float z)
		{
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				TerrainData data = terrain.terrainData;
				Vector3 p = terrain.transform.position, size = data.size;
				if (x < p.x || z < p.z || x > p.x + size.x || z > p.z + size.z)
				{
					continue;
				}
				int res = data.detailResolution;
				int cx = Mathf.Clamp(Mathf.FloorToInt((x - p.x) / size.x * res) - 4, 0, Mathf.Max(0, res - 9));
				int cz = Mathf.Clamp(Mathf.FloorToInt((z - p.z) / size.z * res) - 4, 0, Mathf.Max(0, res - 9));
				DetailPrototype[] prototypes = data.detailPrototypes;
				float sum = 0f;
				for (int i = 0; i < prototypes.Length; i++)
				{
					GameObject prefab = prototypes[i]?.prototype;
					if (prefab == null || !prefab.name.StartsWith("Detail_Grass", StringComparison.Ordinal))
					{
						continue;
					}
					int[,] cells = data.GetDetailLayer(cx, cz, Mathf.Min(9, res), Mathf.Min(9, res), i);
					foreach (int c in cells)
					{
						sum += c;
					}
				}
				return sum;
			}
			return 0f;
		}

		/// <summary>The open land nearest the scene's middle: 10 m or more above the sea and gentle (under 15°).</summary>
		private static Vector2 Land()
		{
			float sea = SeaLevel();
			for (float r = 0f; r < 3000f; r += 25f)
			{
				int around = Mathf.Max(1, Mathf.CeilToInt(2f * Mathf.PI * r / 25f));
				for (int k = 0; k < around; k++)
				{
					float angle = k * 2f * Mathf.PI / around;
					float x = Mathf.Sin(angle) * r, z = Mathf.Cos(angle) * r;
					float h = GroundAt(x, z);
					float slope = Mathf.Max(Mathf.Abs(GroundAt(x + 4f, z) - h), Mathf.Abs(GroundAt(x, z + 4f) - h)) / 4f;
					if (h > sea + 10f && slope < 0.27f)
					{
						return new Vector2(x, z);
					}
				}
			}
			return Vector2.zero;
		}

		private static float GroundAt(float x, float z)
		{
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
				if (x >= p.x && z >= p.z && x <= p.x + size.x && z <= p.z + size.z)
				{
					return terrain.SampleHeight(new Vector3(x, 0f, z)) + p.y;
				}
			}
			return 0f;
		}

		/// <summary>Switches one system off; returns what switches it back on.</summary>
		private static Action Apply(string variant)
		{
			// Several at once: "a+b".
			if (variant.Contains('+'))
			{
				var undo = variant.Split('+').Select(part => Apply(part.Trim())).Where(a => a != null).ToList();
				return () => { for (int i = undo.Count - 1; i >= 0; i--) undo[i](); };
			}
			if (variant.StartsWith("cloud:"))
			{
				return Cloud(variant.Substring("cloud:".Length));
			}
			if (variant.StartsWith("hook:"))
			{
				return Unhook(variant.Substring("hook:".Length));
			}
			if (variant.StartsWith("quality:"))
			{
				int was = QualitySettings.GetQualityLevel();
				QualitySettings.SetQualityLevel(int.Parse(variant.Substring("quality:".Length)), true);
				return () => QualitySettings.SetQualityLevel(was, true);
			}
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (variant.StartsWith("feature:"))
			{
				string name = variant.Substring("feature:".Length);
				var off = Features().Where(f => f != null && f.name == name && f.isActive).ToList();
				off.ForEach(f => f.SetActive(false));
				return () => off.ForEach(f => f.SetActive(true));
			}
			switch (variant)
			{
				case "grassflat":
				case "grassold":
				{
					// The grass as it was: "grassflat" without the top-down lay-over and edge-on thickening (2026-10-07),
					// "grassold" also with the old rings and metre-only widening.
					GrassBladeSettings g = WeatherRenderProfile.Active != null ? WeatherRenderProfile.Active.Grass : null;
					if (g == null)
					{
						return () => { };
					}
					float lay = g.TopDownLayDegrees, thicken = g.EdgeOnThicken, exponent = g.WidenExponent, widen = g.MaxWiden, pixels = g.MaxBladePixels;
					Vector2[] rings = g.Rings;
					int cap = g.BladeCap;
					g.TopDownLayDegrees = 0f;
					g.EdgeOnThicken = 0f;
					if (variant == "grassold")
					{
						g.WidenExponent = 0.5f;
						g.MaxWiden = 6f;
						g.MaxBladePixels = 8f;
						g.Rings = new[] { new Vector2(8f, 1600f), new Vector2(20f, 480f), new Vector2(50f, 110f), new Vector2(120f, 22f), new Vector2(300f, 4.5f) };
						g.BladeCap = 2500000;
					}
					return () =>
					{
						g.TopDownLayDegrees = lay;
						g.EdgeOnThicken = thicken;
						g.WidenExponent = exponent;
						g.MaxWiden = widen;
						g.MaxBladePixels = pixels;
						g.Rings = rings;
						g.BladeCap = cap;
					};
				}
				case "cpugather":
					GrassBladeRenderer.GpuGather = false;
					return () => GrassBladeRenderer.GpuGather = true;
				case "regather":
					GrassBladeRenderer.AlwaysRegather = true;
					return () => GrassBladeRenderer.AlwaysRegather = false;
				case "godrays":
					SkySystem.DrawGodRays = false;
					return () => SkySystem.DrawGodRays = true;
				case "cloudshadows":
					SkySystem.DrawCloudShadows = false;
					return () => SkySystem.DrawCloudShadows = true;
				case "details":
					TerrainInstancingShared.SetDrawsDetails(camera, false);
					return () => TerrainInstancingShared.SetDrawsDetails(camera, true);
				case "shadows":
				{
					float was = pipeline.shadowDistance;
					pipeline.shadowDistance = 0f;
					return () => pipeline.shadowDistance = was;
				}
				case "post":
				{
					var data = camera.GetUniversalAdditionalCameraData();
					bool was = data.renderPostProcessing;
					data.renderPostProcessing = false;
					return () => data.renderPostProcessing = was;
				}
				case "falls":
				{
					// Every waterfall's curtain and spray (InlandWaterRenderer's "Fall"/"Spray" children).
					var on = new List<Renderer>();
					foreach (InlandWaterRenderer inland in UnityEngine.Object.FindObjectsByType<InlandWaterRenderer>(FindObjectsInactive.Exclude))
					{
						foreach (Renderer part in inland.GetComponentsInChildren<Renderer>())
						{
							if (part.enabled && (part.name.StartsWith("Fall ") || part.name.StartsWith("Spray ")))
							{
								on.Add(part);
							}
						}
					}
					on.ForEach(r => r.enabled = false);
					return () => on.ForEach(r => r.enabled = true);
				}
				case "nocamera":
					// The editor's own GPU time alone in the natural frames (the timed renders still draw the camera).
					camera.enabled = false;
					return () => camera.enabled = true;
				case "msaa1":
				{
					int was = pipeline.msaaSampleCount;
					pipeline.msaaSampleCount = 1;
					return () => pipeline.msaaSampleCount = was;
				}
				case "scale50":
				{
					float was = pipeline.renderScale;
					pipeline.renderScale = 0.5f;
					return () => pipeline.renderScale = was;
				}
				case "terrain":
				{
					var on = Terrain.activeTerrains.Where(t => t.enabled).ToList();
					on.ForEach(t => t.enabled = false);
					return () => on.ForEach(t => t.enabled = true);
				}
				case "water":
				{
					var on = new List<Behaviour>();
					on.AddRange(UnityEngine.Object.FindObjectsByType<WaterSurface>(FindObjectsInactive.Exclude));
					on.AddRange(UnityEngine.Object.FindObjectsByType<InlandWaterRenderer>(FindObjectsInactive.Exclude));
					on.AddRange(UnityEngine.Object.FindObjectsByType<WaterShore>(FindObjectsInactive.Exclude));
					on = on.Where(b => b.enabled).ToList();
					on.ForEach(b => b.enabled = false);
					return () => on.ForEach(b => b.enabled = true);
				}
				default:
					return null;
			}
		}

		/// <summary>
		/// Takes a system's handlers off the render pipeline's frame and camera events (RenderPipelineManager's
		/// begin/end callbacks), by the name of the type that declares them (closures nested in it included): its whole
		/// per-camera work, compute dispatches without markers and the draws it submits, is then missing from the frame.
		/// Returns what puts them back.
		/// </summary>
		private static Action Unhook(string system)
		{
			var removed = new List<(FieldInfo field, Delegate handler)>();
			foreach (FieldInfo field in typeof(RenderPipelineManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (!typeof(Delegate).IsAssignableFrom(field.FieldType) || !(field.GetValue(null) is Delegate current))
				{
					continue;
				}
				foreach (Delegate handler in current.GetInvocationList())
				{
					Type declaring = handler.Method.DeclaringType;
					string name = declaring != null ? declaring.FullName ?? declaring.Name : "";
					if (name.Contains(system))
					{
						field.SetValue(null, Delegate.Remove((Delegate)field.GetValue(null), handler));
						removed.Add((field, handler));
					}
				}
			}
			if (removed.Count == 0)
			{
				report.Add($"  (no render-pipeline handler declared by a type named like '{system}')");
			}
			return () =>
			{
				foreach ((FieldInfo field, Delegate handler) in removed)
				{
					field.SetValue(null, Delegate.Combine((Delegate)field.GetValue(null), handler));
				}
			};
		}

		private static readonly List<(FieldInfo field, Delegate original)> wrappedHooks = new List<(FieldInfo, Delegate)>();

		/// <summary>
		/// For the profiled frames: every camera and context handler on the render pipeline's events wrapped in a
		/// profiler marker named for the system that declared it ("Hook.GrassBladeSystem.beginCameraRendering"), so the
		/// CPU table can tell them apart (Unity marks only their sum). The originals go back before any variant runs.
		/// </summary>
		private static void WrapHooks()
		{
			wrappedHooks.Clear();
			foreach (FieldInfo field in typeof(RenderPipelineManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (!(field.GetValue(null) is Delegate current))
				{
					continue;
				}
				Delegate wrapped = null;
				foreach (Delegate handler in current.GetInvocationList())
				{
					Type t = handler.Method.DeclaringType;
					while (t != null && t.IsNested && t.Name.StartsWith("<"))
					{
						t = t.DeclaringType;
					}
					var marker = new Unity.Profiling.ProfilerMarker($"Hook.{t?.Name}.{field.Name}");
					Delegate one = handler;
					if (handler is Action<ScriptableRenderContext, Camera> perCamera)
					{
						one = (Action<ScriptableRenderContext, Camera>)((c, cam) => { marker.Begin(); try { perCamera(c, cam); } finally { marker.End(); } });
					}
					else if (handler is Action<ScriptableRenderContext, List<Camera>> perContext)
					{
						one = (Action<ScriptableRenderContext, List<Camera>>)((c, cams) => { marker.Begin(); try { perContext(c, cams); } finally { marker.End(); } });
					}
					wrapped = Delegate.Combine(wrapped, one);
				}
				wrappedHooks.Add((field, current));
				field.SetValue(null, wrapped);
			}
		}

		private static void UnwrapHooks()
		{
			foreach ((FieldInfo field, Delegate original) in wrappedHooks)
			{
				field.SetValue(null, original);
			}
			wrappedHooks.Clear();
		}

		/// <summary>The declaring types of every render-pipeline event handler now subscribed, for the report.</summary>
		private static string Hooks()
		{
			var names = new SortedSet<string>();
			foreach (FieldInfo field in typeof(RenderPipelineManager).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
			{
				if (typeof(Delegate).IsAssignableFrom(field.FieldType) && field.GetValue(null) is Delegate current)
				{
					foreach (Delegate handler in current.GetInvocationList())
					{
						Type t = handler.Method.DeclaringType;
						while (t != null && t.IsNested && t.Name.StartsWith("<"))
						{
							t = t.DeclaringType;
						}
						names.Add($"{field.Name}:{t?.Name}");
					}
				}
			}
			return string.Join(", ", names);
		}

		/// <summary>
		/// One of the clouds' own settings changed on the live render profile, in memory (put back afterwards, never
		/// saved): res=× (march resolution), steps=× (march steps), light=n (light-march steps), lightmarch=0,
		/// maxdist=metres, stepscale=×, detail=0…1 (the tier's), growth=0 (no distance step growth), exit=0.001…0.5.
		/// </summary>
		private static Action Cloud(string setting)
		{
			SkySystem sky = UnityEngine.Object.FindAnyObjectByType<SkySystem>();
			WeatherRenderProfile profile = sky != null ? sky.Profile : null;
			if (profile == null)
			{
				report.Add($"  (no sky profile for cloud:{setting})");
				return null;
			}
			string[] kv = setting.Split('=');
			float value = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
			WeatherTierSettings tier = profile.TierFor(QualitySettings.GetQualityLevel());
			VolumetricCloudSettings clouds = profile.Clouds;
			VolumetricCloudDiagnostics d = clouds.Diagnostics;
			switch (kv[0])
			{
				case "res": { float was = tier.CloudResolution; tier.CloudResolution = Mathf.Clamp(was * value, 0.05f, 1f); return () => tier.CloudResolution = was; }
				case "steps": { int was = tier.CloudSteps; tier.CloudSteps = Mathf.Max(4, Mathf.RoundToInt(was * value)); return () => tier.CloudSteps = was; }
				case "detail": { float was = tier.CloudDetail; tier.CloudDetail = value; return () => tier.CloudDetail = was; }
				case "light": { int was = clouds.LightSteps; clouds.LightSteps = Mathf.Max(1, (int)value); return () => clouds.LightSteps = was; }
				case "maxdist": { float was = clouds.MaxDistance; clouds.MaxDistance = value; return () => clouds.MaxDistance = was; }
				case "lightmarch": { bool was = d.LightMarch; d.LightMarch = value > 0.5f; return () => d.LightMarch = was; }
				case "stepscale": { float was = d.StepScale; d.StepScale = value; return () => d.StepScale = was; }
				case "growth": { bool was = d.DistanceStepGrowth; d.DistanceStepGrowth = value > 0.5f; return () => d.DistanceStepGrowth = was; }
				case "exit": { float was = d.EarlyExit; d.EarlyExit = value; return () => d.EarlyExit = was; }
				case "tail": { int was = clouds.FarTailSteps; clouds.FarTailSteps = (int)value; return () => clouds.FarTailSteps = was; }
				case "lightvol": { bool was = CloudLightVolume.Enabled; CloudLightVolume.Enabled = value > 0.5f; return () => CloudLightVolume.Enabled = was; }
				case "debug": { CloudDebugView was = d.DebugView; d.DebugView = (CloudDebugView)(int)value; return () => d.DebugView = was; }
				default:
					report.Add($"  (unknown cloud setting {kv[0]})");
					return null;
			}
		}

		// ── Measuring ──────────────────────────────────────────────

		/// <summary>One natural frame's time per marker (the recorders hold the frame before).</summary>
		private static void Sample()
		{
			sampled++;
			foreach (KeyValuePair<string, Recorder> pair in recorders)
			{
				Recorder r = pair.Value;
				if (r.gpuSampleBlockCount > 0)
				{
					gpuSum[pair.Key] = (gpuSum.TryGetValue(pair.Key, out double g) ? g : 0.0) + r.gpuElapsedNanoseconds;
				}
				if (r.sampleBlockCount > 0)
				{
					cpuSum[pair.Key] = (cpuSum.TryGetValue(pair.Key, out double c) ? c : 0.0) + r.elapsedNanoseconds;
				}
			}
		}

		private static void Measure(Step step)
		{
			List<string> cpu = null;
			if (step.Breakdown)
			{
				UnwrapHooks();
				cpu = CpuTable();
				ProfilerDriver.enabled = false;
				Profiler.enabled = false;
			}
			// Renders back to back, the GPU waited on before and after: the frame's whole cost, CPU or GPU, whichever is longer.
			var sync = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			RenderTexture.active = target;
			sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
			GrassBladeRenderer.ResetTimes();
			var watch = System.Diagnostics.Stopwatch.StartNew();
			double submit = 0.0;
			for (int i = 0; i < TimedFrames; i++)
			{
				var one = System.Diagnostics.Stopwatch.StartNew();
				camera.Render();
				submit += one.Elapsed.TotalMilliseconds;
			}
			RenderTexture.active = target;
			sync.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
			watch.Stop();
			UnityEngine.Object.DestroyImmediate(sync);
			float ms = (float)(watch.Elapsed.TotalMilliseconds / TimedFrames);
			if ((step.Variant == "base" || step.Variant == "rebase") && step.View == steps[0].View)
			{
				baseMilliseconds = ms;
			}
			// The natural frames' own GPU time: every system's once-a-frame work counted once, as a player's frame has it
			// (the timed renders share one editor frame, so work a system does once per frame number is spread over all
			// sixty of them). It includes the editor's own drawing, the same in every variant: compare differences.
			int n = Mathf.Max(1, sampled);
			float natural = (float)((gpuSum.TryGetValue("FrameTime.GPU", out double g) ? g : 0.0) / n / 1e6);
			float cameraGpu = (float)((gpuSum.TryGetValue("UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera", out double c) ? c : 0.0) / n / 1e6);
			if ((step.Variant == "base" || step.Variant == "rebase") && step.View == steps[0].View)
			{
				baseNatural = natural;
			}
			string delta = step.Variant == "base" || step.Variant == "rebase" ? "" : $" (saves {baseMilliseconds - ms:+0.00;-0.00} ms timed, {baseNatural - natural:+0.00;-0.00} ms natural)";
			report.Add($"{step.View} {step.Variant}: natural GPU frame {natural:0.00} ms (camera {cameraGpu:0.00}); timed {ms:0.00} ms ({1000f / ms:0} fps), CPU in Render() {submit / TimedFrames:0.00} ms{delta}");
			{
				// The light volume the cloud march's long light segments read: whether it is published, and what is in it.
				Vector4 on = Shader.GetGlobalVector("_FishCloudLightVolumeC");
				var volume = Shader.GetGlobalTexture("_FishCloudLightVolume") as RenderTexture;
				if (on.x > 0.5f && volume != null)
				{
					var read = new Texture2D(volume.width, volume.height, TextureFormat.RGBAFloat, false, true);
					RenderTexture was = RenderTexture.active;
					RenderTexture.active = volume;
					read.ReadPixels(new Rect(0, 0, volume.width, volume.height), 0, 0);
					RenderTexture.active = was;
					Color[] texels = read.GetPixels();
					UnityEngine.Object.DestroyImmediate(read);
					double sum = 0.0;
					float max = 0f;
					int cloudy = 0;
					foreach (Color texel in texels)
					{
						sum += texel.r;
						max = Mathf.Max(max, texel.r);
						cloudy += texel.r > 1e-5f ? 1 : 0;
					}
					Vector4 a = Shader.GetGlobalVector("_FishCloudLightVolumeA"), b = Shader.GetGlobalVector("_FishCloudLightVolumeB");
					report.Add($"  cloud light volume: on, texel {a.z:0} m, slice {b.y:0} m from {b.x:0} m; density mean {sum / texels.Length:0.000000}, max {max:0.0000}, {100.0 * cloudy / texels.Length:0.0}% cloudy");
				}
				else
				{
					report.Add($"  cloud light volume: off (C.x {on.x:0}, texture {(volume != null ? "set" : "none")})");
				}
			}
			if (GrassBladeRenderer.TimedCalls > 0)
			{
				float calls = GrassBladeRenderer.TimedCalls;
				report.Add($"  grass per render: atlas {GrassBladeRenderer.TimeAtlas / calls:0.000} ms, gather {GrassBladeRenderer.TimeGather / calls:0.000} ms, record {GrassBladeRenderer.TimeRecord / calls:0.000} ms, draws {GrassBladeRenderer.TimeDraws / calls:0.000} ms; {GrassBladeRenderer.TimedTiles} tiles, {GrassBladeRenderer.TimedItems} items ({calls / TimedFrames:0.0} calls a render; {(GrassBladeRenderer.GpuGather ? "GPU" : "CPU")} gather)");
			}

			{
				// What the variant looks like (after the timed renders, so a temporal history has settled).
				var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
				RenderTexture.active = target;
				image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
				string tag = new string((step.Variant).Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '=' ? ch : '_').ToArray());
				File.WriteAllBytes(Path.Combine(output, $"perf-{step.View}-{tag}.png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			if (step.Breakdown)
			{
				int frames = Mathf.Max(1, sampled);
				report.Add($"  GPU by marker (mean of {frames} natural frames; nested markers overlap their parents):");
				foreach (KeyValuePair<string, double> pair in gpuSum.OrderByDescending(p => p.Value).Take(45))
				{
					report.Add($"    {pair.Value / frames / 1e6:0.000} ms  {pair.Key}");
				}
				report.AddRange(cpu);
			}
			File.WriteAllLines(Path.Combine(output, "ScenePerf-report.txt"), report);
		}

		/// <summary>
		/// The recorded natural frames' CPU time, flattened: main-thread self time per marker per frame (and calls), the
		/// render thread's after it. Draws and dispatches are keyed by the marker that called them.
		/// </summary>
		private static List<string> CpuTable()
		{
			var lines = new List<string>();
			int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
			if (first < 0 || last < first)
			{
				lines.Add("  (no profiler frames)");
				return lines;
			}
			var mainSelf = new Dictionary<string, double>();
			var mainCalls = new Dictionary<string, double>();
			var renderSelf = new Dictionary<string, double>();
			var children = new List<int>();
			var stack = new Stack<(int id, string parent)>();
			int frames = 0;
			double frameMs = 0.0;
			for (int f = first + (last - first) / 4; f <= last; f++)
			{
				using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
				{
					if (view == null || !view.valid)
					{
						continue;
					}
					frames++;
					frameMs += view.frameTimeMs;
					Flatten(view, mainSelf, mainCalls, children, stack);
				}
				int renderThread = RenderThreadIndex(f);
				if (renderThread > 0)
				{
					using (HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, renderThread, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
					{
						if (view != null && view.valid)
						{
							Flatten(view, renderSelf, null, children, stack);
						}
					}
				}
			}
			if (frames == 0)
			{
				lines.Add("  (no valid profiler frames)");
				return lines;
			}
			lines.Add($"  CPU main thread, {frames} profiled frames of {frameMs / frames:0.0} ms (self ms a frame, calls a frame):");
			foreach (KeyValuePair<string, double> item in mainSelf.OrderByDescending(p => p.Value).Take(45))
			{
				double calls = mainCalls.TryGetValue(item.Key, out double c) ? c / frames : 0.0;
				lines.Add($"    {item.Value / frames,8:0.000}  {calls,7:0.#}  {item.Key}");
			}
			lines.Add("  CPU render thread:");
			foreach (KeyValuePair<string, double> item in renderSelf.OrderByDescending(p => p.Value).Take(15))
			{
				lines.Add($"    {item.Value / frames,8:0.000}  {item.Key}");
			}
			return lines;
		}

		/// <summary>Markers that say what was done but not for whom, keyed by the marker that called them.</summary>
		private static readonly HashSet<string> Generic = new HashSet<string>
		{
			"Graphics.DrawProcedural", "Graphics.Blit", "Render.Mesh", "DrawBuffersBatchMode", "RenderLoop.Draw",
			"Graphics.DrawMesh", "Graphics.DrawMeshInstanced", "Graphics.DrawProceduralIndirect", "ComputeShader.Dispatch",
			"GC.Alloc", "Graphics.RenderMeshIndirect", "Graphics.RenderMeshInstanced", "GraphicsBuffer.SetData", "Texture2D.Apply",
		};

		private static void Flatten(HierarchyFrameDataView view, Dictionary<string, double> self, Dictionary<string, double> calls, List<int> children, Stack<(int id, string parent)> stack)
		{
			stack.Clear();
			int root = view.GetRootItemID();
			stack.Push((root, "(root)"));
			while (stack.Count > 0)
			{
				(int id, string parent) = stack.Pop();
				string name = id == root ? "(root)" : view.GetItemName(id);
				if (id != root)
				{
					string key = Generic.Contains(name) ? $"{parent} > {name}" : name;
					self.TryGetValue(key, out double ms);
					self[key] = ms + view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
					if (calls != null)
					{
						calls.TryGetValue(key, out double n);
						calls[key] = n + view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnCalls);
					}
				}
				view.GetItemChildren(id, children);
				for (int i = 0; i < children.Count; i++)
				{
					stack.Push((children[i], name));
				}
			}
		}

		private static int RenderThreadIndex(int frame)
		{
			for (int thread = 1; thread < 64; thread++)
			{
				using (RawFrameDataView raw = ProfilerDriver.GetRawFrameDataView(frame, thread))
				{
					if (raw == null || !raw.valid)
					{
						return -1;
					}
					if (raw.threadName == "Render Thread")
					{
						return thread;
					}
				}
			}
			return -1;
		}

		private static void Finish(int code)
		{
			EditorApplication.update -= Pump;
			SessionState.EraseString(StateKey);
			if (camera != null)
			{
				camera.targetTexture = null;
			}
			File.WriteAllLines(Path.Combine(output ?? ".", "ScenePerf-report.txt"), report.Append(code == 0 ? "DONE" : "FAILED").ToList());
			EditorAutomation.Finish(code);
		}
	}
}
