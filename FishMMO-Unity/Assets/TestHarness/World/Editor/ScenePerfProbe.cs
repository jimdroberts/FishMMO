using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
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
			report.Add($"camera far {camera.farClipPlane:0}, fov {camera.fieldOfView:0}, lodBias {QualitySettings.lodBias:0.00}");
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
				if (v == 0)
				{
					foreach (string variant in Variants(variants))
					{
						steps.Add(new Step { View = viewList[v], Variant = variant });
					}
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
			if (step.Variant == "base" && step.View == steps[0].View)
			{
				baseMilliseconds = ms;
			}
			// The natural frames' own GPU time: every system's once-a-frame work counted once, as a player's frame has it
			// (the timed renders share one editor frame, so work a system does once per frame number is spread over all
			// sixty of them). It includes the editor's own drawing, the same in every variant: compare differences.
			int n = Mathf.Max(1, sampled);
			float natural = (float)((gpuSum.TryGetValue("FrameTime.GPU", out double g) ? g : 0.0) / n / 1e6);
			float cameraGpu = (float)((gpuSum.TryGetValue("UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera", out double c) ? c : 0.0) / n / 1e6);
			if (step.Variant == "base" && step.View == steps[0].View)
			{
				baseNatural = natural;
			}
			string delta = step.Variant == "base" ? "" : $" (saves {baseMilliseconds - ms:+0.00;-0.00} ms timed, {baseNatural - natural:+0.00;-0.00} ms natural)";
			report.Add($"{step.View} {step.Variant}: natural GPU frame {natural:0.00} ms (camera {cameraGpu:0.00}); timed {ms:0.00} ms ({1000f / ms:0} fps), CPU in Render() {submit / TimedFrames:0.00} ms{delta}");
			if (GrassBladeRenderer.TimedCalls > 0)
			{
				float calls = GrassBladeRenderer.TimedCalls;
				report.Add($"  grass per render: atlas {GrassBladeRenderer.TimeAtlas / calls:0.000} ms, gather {GrassBladeRenderer.TimeGather / calls:0.000} ms, record {GrassBladeRenderer.TimeRecord / calls:0.000} ms, draws {GrassBladeRenderer.TimeDraws / calls:0.000} ms; {GrassBladeRenderer.TimedTiles} tiles, {GrassBladeRenderer.TimedItems} items ({calls / TimedFrames:0.0} calls a render)");
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
				report.Add("  CPU by marker (main thread and render thread, same caveat):");
				foreach (KeyValuePair<string, double> pair in cpuSum.OrderByDescending(p => p.Value).Take(30))
				{
					report.Add($"    {pair.Value / frames / 1e6:0.000} ms  {pair.Key}");
				}
			}
			File.WriteAllLines(Path.Combine(output, "ScenePerf-report.txt"), report);
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
