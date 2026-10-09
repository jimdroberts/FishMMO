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
			// Before anything is measured or its markers listed: the clouds and fog as shipped, whatever the editor's own switches say
			// (FISHMMO_PERF_CLOUDS=0 holds the clouds off instead, to price everything else).
			SkySystem.EditorCloudsOff = CloudsHeldOff;
			SkySystem.EditorFogOff = false;
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
			report.Add(DescribeBackdrop());

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
					// FISHMMO_PERF_BREAKDOWN_ALL=1: every step's GPU-by-marker and CPU tables, so a pair shows which passes moved.
					bool all = Environment.GetEnvironmentVariable("FISHMMO_PERF_BREAKDOWN_ALL") == "1";
					foreach (string variant in Variants(variants))
					{
						steps.Add(new Step { View = viewList[v], Variant = "rebase", Breakdown = all });
						steps.Add(new Step { View = viewList[v], Variant = variant, Breakdown = all });
					}
					// The base again, settled: the first measurement of a run reads high while the scene warms up.
					steps.Add(new Step { View = viewList[v], Variant = "rebase" });
				}
			}
		}

		/// <summary>FISHMMO_PERF_CLOUDS=0: the whole run with the clouds off, as a player with the option off sees it.</summary>
		private static bool CloudsHeldOff => Environment.GetEnvironmentVariable("FISHMMO_PERF_CLOUDS") == "0";

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
			// The editor's cloud switch (CloudToggle) is a person's preference, restored after every domain reload: the
			// probe measures the clouds as shipped, so it is held on here, for this session only (nothing is saved). The fog
			// switch (FogToggle) likewise. FISHMMO_PERF_CLOUDS=0 holds them off for the whole run instead.
			SkySystem.EditorCloudsOff = CloudsHeldOff;
			SkySystem.EditorFogOff = false;
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
				case "grass-back":
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
				case "edge":
				case "edge-high":
				{
					// On land just inside the scene's edge, looking out over the backdrop: level at a player's eye (edge), or
					// from 150 m up tilted 12 degrees down (edge-high), where the backdrop fills most of the frame.
					Edge(out Vector3 at, out Vector3 outward);
					bool high = view == "edge-high";
					eye = new Vector3(at.x, GroundAt(at.x, at.z) + (high ? 150f : 1.8f), at.z);
					Vector3 dir = high ? Quaternion.AngleAxis(12f, Vector3.Cross(Vector3.up, outward)) * outward : outward + new Vector3(0f, -0.03f, 0f);
					look = eye + dir * 100f;
					break;
				}
				case "skyline":
				case "skyup":
				{
					// The meadow's spot and direction, above the trees: level, half the screen sky (skyline), or tilted 25
					// degrees up into it (skyup) — the clouds' cost follows how much sky is in view, and the meadow's trees
					// hide most of it.
					Place("meadow");
					Vector3 forward = camera.transform.forward;
					forward.y = 0f;
					forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
					Vector3 standing = camera.transform.position + Vector3.up * 30f;
					Vector3 dir = view == "skyup" ? Quaternion.AngleAxis(-25f, Vector3.Cross(Vector3.up, forward)) * forward : forward;
					camera.transform.SetPositionAndRotation(standing, Quaternion.LookRotation(dir, Vector3.up));
					return;
				}
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

		private static (Vector3 At, Vector3 Out)? edgeSpot;

		/// <summary>
		/// The land point 40 m inside the scene's terrain edge nearest the middle of an edge, and the way out across it.
		/// </summary>
		private static void Edge(out Vector3 at, out Vector3 outward)
		{
			if (edgeSpot.HasValue)
			{
				at = edgeSpot.Value.At;
				outward = edgeSpot.Value.Out;
				return;
			}
			float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
				minX = Mathf.Min(minX, p.x);
				minZ = Mathf.Min(minZ, p.z);
				maxX = Mathf.Max(maxX, p.x + size.x);
				maxZ = Mathf.Max(maxZ, p.z + size.z);
			}
			const float inset = 40f;
			float sea = SeaLevel();
			at = new Vector3((minX + maxX) * 0.5f, 0f, maxZ - inset);
			outward = Vector3.forward;
			float nearest = float.MaxValue;
			// North, south, east, west: a point on each edge's inset line, walked out from the edge's middle.
			var edges = new (Vector3 Mid, Vector3 Along, float Half, Vector3 Out)[]
			{
				(new Vector3((minX + maxX) * 0.5f, 0f, maxZ - inset), Vector3.right, (maxX - minX) * 0.5f - inset, Vector3.forward),
				(new Vector3((minX + maxX) * 0.5f, 0f, minZ + inset), Vector3.right, (maxX - minX) * 0.5f - inset, Vector3.back),
				(new Vector3(maxX - inset, 0f, (minZ + maxZ) * 0.5f), Vector3.forward, (maxZ - minZ) * 0.5f - inset, Vector3.right),
				(new Vector3(minX + inset, 0f, (minZ + maxZ) * 0.5f), Vector3.forward, (maxZ - minZ) * 0.5f - inset, Vector3.left),
			};
			foreach (var e in edges)
			{
				for (float d = 0f; d < e.Half && d < nearest; d += 50f)
				{
					foreach (float sign in new[] { 1f, -1f })
					{
						Vector3 p = e.Mid + e.Along * (d * sign);
						if (GroundAt(p.x, p.z) > sea + 2f)
						{
							nearest = d;
							at = p;
							outward = e.Out;
							break;
						}
					}
				}
			}
			edgeSpot = (at, outward);
			report.Add($"edge view at ({at.x:0}, {at.z:0}), looking ({outward.x:0}, {outward.z:0}), {(nearest < float.MaxValue ? $"{nearest:0} m from the edge's middle" : "NO land on any edge")}");
		}

		/// <summary>Every renderer under the scene's backdrops: the ground rings, or (water) only the lakes and rivers drawn on them.</summary>
		private static List<Renderer> BackdropRenderers(bool water)
		{
			var found = new List<Renderer>();
			foreach (SceneBackdrop backdrop in UnityEngine.Object.FindObjectsByType<SceneBackdrop>(FindObjectsInactive.Exclude))
			{
				foreach (Renderer r in backdrop.GetComponentsInChildren<Renderer>())
				{
					// The rings are named "Ring <n> <side>" by SceneBackdropBuilder; everything else under it is water.
					if (r.enabled && (!water || !r.name.StartsWith("Ring ", StringComparison.Ordinal)))
					{
						found.Add(r);
					}
				}
			}
			return found;
		}

		/// <summary>What the backdrop draws, for the report: meshes, vertices, triangles and shaders, ground and water apart.</summary>
		private static string DescribeBackdrop()
		{
			List<Renderer> all = BackdropRenderers(false);
			if (all.Count == 0)
			{
				return "backdrop: none in this scene";
			}
			var sb = new StringBuilder("backdrop:");
			foreach (var group in all.GroupBy(r => r.name.StartsWith("Ring ", StringComparison.Ordinal) ? "ground" : "water"))
			{
				long verts = 0, tris = 0;
				foreach (Renderer r in group)
				{
					Mesh mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
					if (mesh != null)
					{
						verts += mesh.vertexCount;
						for (int s = 0; s < mesh.subMeshCount; s++)
						{
							tris += mesh.GetIndexCount(s) / 3;
						}
					}
				}
				string shaders = string.Join("/", group.Select(r => r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none").Distinct());
				sb.Append($" {group.Key} {group.Count()} meshes, {verts:N0} verts, {tris:N0} tris ({shaders});");
			}
			return sb.ToString();
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
				case "grass-back":
					// The same eye turned round: at Flo Monolith grass-eye stands in a bush, which fills most of its frame.
					eye = at + Vector3.up * 1.7f;
					look = eye + (-dir + Vector3.down * 0.05f) * 50f;
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
			if (variant.StartsWith("backdropfade:"))
			{
				// "backdropfade:<from>:<to>": the distances (m) the backdrop's arrays fade over (FishMMO/Backdrop Ground's
				// _FishBackdropDetail), on its shared material for this step; 0:0 draws the colour bake alone.
				string[] parts = variant.Substring("backdropfade:".Length).Split(':');
				var vector = new Vector4(float.Parse(parts[0]), float.Parse(parts[1]), 0f, 0f);
				var changed = new List<(Material Material, Vector4 Was)>();
				foreach (Renderer r in BackdropRenderers(false))
				{
					Material m = r.sharedMaterial;
					if (m != null && m.HasProperty("_FishBackdropDetail") && !changed.Exists(c => c.Material == m))
					{
						changed.Add((m, m.GetVector("_FishBackdropDetail")));
						m.SetVector("_FishBackdropDetail", vector);
					}
				}
				return () => changed.ForEach(c => c.Material.SetVector("_FishBackdropDetail", c.Was));
			}
			if (variant.StartsWith("grass:") || variant.StartsWith("grassrings="))
			{
				// The grass profile, live (restored after): `grass:Field=value` sets a number field of GrassBladeSettings,
				// `grassrings=k` scales every ring's blades per square metre by k (fewer blades, same reach).
				GrassBladeSettings g = WeatherRenderProfile.Active != null ? WeatherRenderProfile.Active.Grass : null;
				if (g == null)
				{
					return () => { };
				}
				if (variant.StartsWith("grassrings="))
				{
					float k = float.Parse(variant.Substring("grassrings=".Length), System.Globalization.CultureInfo.InvariantCulture);
					Vector2[] rings = g.Rings;
					g.Rings = rings.Select(r => new Vector2(r.x, r.y * k)).ToArray();
					return () => g.Rings = rings;
				}
				string[] kv = variant.Substring("grass:".Length).Split('=');
				FieldInfo field = typeof(GrassBladeSettings).GetField(kv[0]);
				if (field == null)
				{
					return null;
				}
				object was = field.GetValue(g);
				object value = field.FieldType == typeof(int) ? (object)int.Parse(kv[1]) : float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
				field.SetValue(g, value);
				return () => field.SetValue(g, was);
			}
			if (variant.StartsWith("shadowdist=") || variant.StartsWith("cascades=") || variant.StartsWith("shadowres=") || variant.StartsWith("stp="))
			{
				// The pipeline asset, live (restored after): main-light shadow distance (m), cascade count, shadow map
				// resolution, or render scale with the STP upscaler.
				var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
				string number = variant.Substring(variant.IndexOf('=') + 1);
				if (variant.StartsWith("shadowdist="))
				{
					float was = asset.shadowDistance;
					asset.shadowDistance = float.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
					return () => asset.shadowDistance = was;
				}
				if (variant.StartsWith("cascades="))
				{
					int was = asset.shadowCascadeCount;
					asset.shadowCascadeCount = int.Parse(number);
					return () => asset.shadowCascadeCount = was;
				}
				if (variant.StartsWith("shadowres="))
				{
					int was = asset.mainLightShadowmapResolution;
					asset.mainLightShadowmapResolution = int.Parse(number);
					return () => asset.mainLightShadowmapResolution = was;
				}
				float scaleWas = asset.renderScale;
				UpscalingFilterSelection filterWas = asset.upscalingFilter;
				asset.renderScale = float.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
				asset.upscalingFilter = UpscalingFilterSelection.STP;
				return () =>
				{
					asset.renderScale = scaleWas;
					asset.upscalingFilter = filterWas;
				};
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
				case "scatterregen":
					// The detail scatter generated on every render, as before its reuse (DetailScatterRenderer.RegatherMetres).
					DetailScatterRenderer.AlwaysRegenerate = true;
					return () => DetailScatterRenderer.AlwaysRegenerate = false;
				case "godrays":
					SkySystem.DrawGodRays = false;
					return () => SkySystem.DrawGodRays = true;
				case "cloudshadows":
					SkySystem.DrawCloudShadows = false;
					return () => SkySystem.DrawCloudShadows = true;
				case "fog":
					// Every fog at once (FogToggle): the layer's passes, the ground mist and the distance fog.
					SkySystem.EditorFogOff = true;
					return () => SkySystem.EditorFogOff = false;
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
				case "vegold":
					Shader.SetGlobalFloat("_FishVegetationDiag", 1f);
					return () => Shader.SetGlobalFloat("_FishVegetationDiag", 0f);
				case "vegtrace":
					Shader.SetGlobalFloat("_FishVegContactAlways", 1f);
					return () => Shader.SetGlobalFloat("_FishVegContactAlways", 0f);
				case "nocascadecull":
					// Every tree drawn into every shadow cascade again (ShadowCascadeCulling).
					ShadowCascadeCulling.Enabled = false;
					return () => ShadowCascadeCulling.Enabled = true;
				case "vegplantconst":
					// Every per-plant value as a constant (FishVegetationPasses.hlsl _FishVegPlantConst): the most a per-instance
					// precompute of them could save. Draws every plant alike.
					Shader.SetGlobalFloat("_FishVegPlantConst", 1f);
					return () => Shader.SetGlobalFloat("_FishVegPlantConst", 0f);
				case "castercullall":
					Shader.SetGlobalFloat("_FishCasterCullAll", 1f);
					return () => Shader.SetGlobalFloat("_FishCasterCullAll", 0f);
				case "noprime":
					// The trees' alpha-tested lit pass drawn alone again, writing its own depth (TerrainGpuRenderer.PrimeAlphaTested).
					TerrainGpuRenderer.PrimeAlphaTested = false;
					return () => TerrainGpuRenderer.PrimeAlphaTested = true;
				case "noocclusion":
					FishDepthPyramid.Enabled = false;
					return () => FishDepthPyramid.Enabled = true;
				case "nocamera":
					// The editor's own GPU time alone in the natural frames (the timed renders still draw the camera).
					camera.enabled = false;
					return () => camera.enabled = true;
				case "depthprime":
				{
					// Depth priming forced on every renderer (URP leaves it off under MSAA: pair it with msaa1).
					var changed = new List<(UniversalRendererData Data, DepthPrimingMode Was)>();
					FieldInfo list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
					if (list?.GetValue(pipeline) is ScriptableRendererData[] all)
					{
						foreach (ScriptableRendererData d in all)
						{
							if (d is UniversalRendererData universal)
							{
								changed.Add((universal, universal.depthPrimingMode));
								universal.depthPrimingMode = DepthPrimingMode.Forced;
							}
						}
					}
					return () => changed.ForEach(c => c.Data.depthPrimingMode = c.Was);
				}
				case "msaa2":
				{
					int was = pipeline.msaaSampleCount;
					pipeline.msaaSampleCount = 2;
					return () => pipeline.msaaSampleCount = was;
				}
				case "ssaoafter":
				{
					// URP's SSAO applied after the opaques (no depth prepass needed for it). Its settings are internal.
					var changed = new List<(object Settings, FieldInfo Field, object Was)>();
					foreach (ScriptableRendererFeature feature in Features())
					{
						if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion")
						{
							continue;
						}
						object settings = feature.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(feature);
						FieldInfo after = settings?.GetType().GetField("AfterOpaque", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
						if (after != null)
						{
							changed.Add((settings, after, after.GetValue(settings)));
							after.SetValue(settings, true);
						}
					}
					return () => changed.ForEach(c => c.Field.SetValue(c.Settings, c.Was));
				}
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
				case "backdrop":
				case "backdropwater":
				{
					// The renderers only: SceneBackdrop itself stays on, so the camera's far plane it raised (and everything
					// else drawn to that distance) is unchanged and the difference is the backdrop's own draws.
					List<Renderer> on = BackdropRenderers(variant == "backdropwater");
					on.ForEach(r => r.enabled = false);
					return () => on.ForEach(r => r.enabled = true);
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
				case "maxmarch": { float was = d.MaxMarchDistance; d.MaxMarchDistance = value; return () => d.MaxMarchDistance = was; }
				case "stepscale": { float was = d.StepScale; d.StepScale = value; return () => d.StepScale = was; }
				case "growth": { bool was = d.DistanceStepGrowth; d.DistanceStepGrowth = value > 0.5f; return () => d.DistanceStepGrowth = was; }
				case "exit": { float was = d.EarlyExit; d.EarlyExit = value; return () => d.EarlyExit = was; }
				case "fieldskip": { Shader.SetGlobalFloat("_FishCloudFieldSkip", value); return () => Shader.SetGlobalFloat("_FishCloudFieldSkip", 0f); }
				case "fieldreuse": { Shader.SetGlobalFloat("_FishCloudFieldReuse", value); return () => Shader.SetGlobalFloat("_FishCloudFieldReuse", 0f); }
				case "tail": { int was = clouds.FarTailSteps; clouds.FarTailSteps = (int)value; return () => clouds.FarTailSteps = was; }
				case "splita": { float was = FishCloudsFeature.FarSplitA; FishCloudsFeature.FarSplitA = value; return () => FishCloudsFeature.FarSplitA = was; }
				case "splitb": { float was = FishCloudsFeature.FarSplitB; FishCloudsFeature.FarSplitB = value; return () => FishCloudsFeature.FarSplitB = was; }
				case "farslice": { int was = FishCloudsFeature.FarSliceOverride; FishCloudsFeature.FarSliceOverride = (int)value; return () => FishCloudsFeature.FarSliceOverride = was; }
				case "farband": { float was = clouds.FarBandMetres; clouds.FarBandMetres = value; return () => clouds.FarBandMetres = was; }
				case "tiles": { bool was = CloudFieldTiles.Enabled; CloudFieldTiles.Enabled = value > 0.5f; return () => CloudFieldTiles.Enabled = was; }
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
				// The cloud passes by themselves (natural frames), and what resolution the march really ran at: a variant
				// that moves the resolution must be seen to have moved it.
				int frames = Mathf.Max(1, sampled);
				string passes = string.Join(", ", gpuSum.Where(p => p.Key.StartsWith("Fish Clouds"))
					.OrderByDescending(p => p.Value)
					.Select(p => $"{p.Key.Substring("Fish Clouds".Length).Trim(' ', '(', ')')} {p.Value / frames / 1e6:0.00}"));
				SkySystem sky = SkySystem.Instance;
				float resolution = sky != null ? sky.CloudTier.Resolution : 0f;
				report.Add($"  clouds: {(passes.Length > 0 ? passes : "no passes")} ms; march {resolution:0.000} of the screen, {FishCloudsFeature.PixelsPerMarchedTexel:0.0} rebuilt px a texel");
			}
			{
				// The field's tiles: whether the march reads them, and what is in them (a tile of zeros was never drawn).
				Vector4 tilesOn = Shader.GetGlobalVector("_FishCloudFieldTiles");
				string Mean(string name)
				{
					var tile = Shader.GetGlobalTexture(name) as RenderTexture;
					if (tile == null)
					{
						return "none";
					}
					var read = new Texture2D(tile.width, tile.height, TextureFormat.RGBAFloat, false, true);
					RenderTexture before = RenderTexture.active;
					RenderTexture.active = tile;
					read.ReadPixels(new Rect(0, 0, tile.width, tile.height), 0, 0);
					RenderTexture.active = before;
					Color[] texels = read.GetPixels();
					UnityEngine.Object.DestroyImmediate(read);
					double total = 0.0;
					float most = 0f;
					foreach (Color texel in texels)
					{
						total += texel.r;
						most = Mathf.Max(most, texel.r);
					}
					return $"{tile.width}² mean {total / texels.Length:0.000} max {most:0.000}";
				}
				report.Add($"  cloud field tiles: {(tilesOn.x > 0.5f ? "on" : "off")}; tower {Mean("_FishCloudTowerTile")}; formation {Mean("_FishCloudMesoTile")}");
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
			report.Add($"  props drawn: {CliffRockInstancing.DescribeDraws()}");
			report.Add($"  shadow cascades: {ShadowCascadeCulling.LastReport}");
			report.Add($"  indirect draws a camera: props {CliffRockInstancing.LastDrawCount}, trees {TerrainTreeInstancing.LastDrawCount}, details {TerrainDetailInstancing.LastDrawCount}, scatter {DetailScatterSystem.LastDrawCount}, grass {GrassBladeSystem.LastDrawCount}");
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
				// A long combination (every hook at once) overflows the file name limit and failed the run: cut it, kept apart by its hash.
				if (tag.Length > 96)
				{
					tag = tag.Substring(0, 80) + "_" + ((uint)step.Variant.GetHashCode()).ToString("x8");
				}
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
