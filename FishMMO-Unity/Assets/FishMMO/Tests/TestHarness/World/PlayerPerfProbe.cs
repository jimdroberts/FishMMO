using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishMMO.TestHarness.World
{
	/// <summary>Opt-in standalone rendering benchmark. Added to build copies only by PlayerPerfBuild.</summary>
	[DefaultExecutionOrder(-100)]
	public sealed class PlayerPerfProbe : MonoBehaviour
	{
		[Serializable] public sealed class Summary
		{
			public string scene, view, gpu, api, unity, quality, clock, feature, variant, grassLooks, weather;
			public int width, height, samples, gpuSamples, gcCollections, msaa;
			public float fieldOfView;
			public bool moving, hlod, hlodAvailable, development, waterFixture;
			public float renderScale, fps, frameMedian, frameP95, frameP99, gpuMedian, gpuP95, cpuMedian, renderMedian, drawMedian, setPassMedian;
			public long allocatedBytes, reservedBytes;
			public Vector3 eye, target;
			public bool weatherFieldDriven, cloudsReady, cloudsEnabled, coarseGrass, preparedGrassColour, foamShortcut;
			public int cloudBufferWidth, cloudBufferHeight, precipitationKinds;
			public double worldSecondsStart, worldSecondsEnd;
			public Vector2 cloudDriftStart, cloudDriftEnd;
			public Vector4 foamWindow;
			public float foamTargetCoverage;
			public float cloudCover, precipitation, wind, measuredCloudCover, measuredAnyCloud;
		}
		private struct Sample { public float frame, gpu, cpu, render, draws, setPass; }
		private struct View { public string Name; public Vector3 Eye, Target; }
		private readonly FrameTiming[] timings = new FrameTiming[1];
		private readonly List<Sample> samples = new List<Sample>(4096);
		private readonly List<Summary> results = new List<Summary>();
		private ProfilerRecorder draws, setPass;
		private readonly List<ProfilerRecorder> drawPaths = new List<ProfilerRecorder>();
		private int errors;
		private readonly Dictionary<string, Recorder> profile = new Dictionary<string, Recorder>();
		private readonly Dictionary<string, double> gpuProfile = new Dictionary<string, double>(), cpuProfile = new Dictionary<string, double>();
		private Camera camera;
		private WorldSimController world;
		private MeshRenderer waterFixture;
		private string output, weatherCase;
		public TerrainArraySet TerrainArrays;
		private void Awake()
		{
			if (!Application.isPlaying) return;
			output = Arg("out", Path.Combine(Application.persistentDataPath, "RenderingBenchmark")); Directory.CreateDirectory(output); Application.logMessageReceived += LogFailure;
			foreach (var binder in FindObjectsByType<TerrainArrayBinder>()) binder.Bind(TerrainArrays);
		}

		private static string Arg(string name, string fallback)
		{
			string prefix = "--" + name + "=";
			foreach (string a in Environment.GetCommandLineArgs()) if (a.StartsWith(prefix, StringComparison.Ordinal)) return a.Substring(prefix.Length);
			return fallback;
		}
		private static int Number(string name, int fallback) => int.TryParse(Arg(name, ""), out int n) ? n : fallback;
		private static float Percentile(List<float> values, float p)
		{
			if (values.Count == 0) return -1;
			values.Sort(); return values[Mathf.Clamp(Mathf.CeilToInt(p * values.Count) - 1, 0, values.Count - 1)];
		}
		private static bool SetHlod(bool on)
		{
			WorldHlod.Enabled = on;
			return WorldHlod.HasData;
		}
		private static string HlodDescription() => WorldHlod.Describe();
		private IEnumerator Start()
		{
			yield return null; // Let the world controller finish its authored startup before applying benchmark conditions.
			
			string wanted = Arg("scene", SceneManager.GetActiveScene().name);
			if (wanted != SceneManager.GetActiveScene().name) { SceneManager.LoadScene(wanted); yield break; }
			world = FindAnyObjectByType<WorldSimController>();
			camera = world != null ? world.Camera : Camera.main;
			if (world == null || camera == null) { Fail("Benchmark scene has no WorldSim controller/camera"); yield break; }
			QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
			QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
			Screen.SetResolution(2560, 1440, FullScreenMode.FullScreenWindow);
			var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (pipeline == null) { Fail("URP is required"); yield break; }
			int aa = Number("msaa",4); if (aa != 2 && aa != 4) { Fail("Use --msaa=2 or 4"); yield break; }
			pipeline.renderScale = 1f; pipeline.msaaSampleCount = aa; pipeline.upscalingFilter = UpscalingFilterSelection.Linear;
			camera.allowMSAA = true; camera.targetTexture = null; camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
			foreach (var panel in FindObjectsByType<WorldSimPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) panel.enabled = false;
			foreach (var document in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None)) document.enabled = false;
			foreach (var controls in FindObjectsByType<WorldSimCamera>(FindObjectsSortMode.None)) controls.enabled = false;
			world.Paused = true; world.JumpTo(double.Parse(Arg("day", "0.5"), CultureInfo.InvariantCulture)); world.ClearAll(0);
			weatherCase = Arg("weather", "scene");
			world.FieldDriven = weatherCase == "scene";
			switch (weatherCase)
			{
				case "scene": break;
				case "clear": world.SetAir(new AirOffsets { Humidity = -.4f, Pressure = .8f }, 0); break;
				case "overcast": world.SetAir(new AirOffsets { Humidity = .25f, Pressure = -.1f, Wind = 6f }, 0); break;
				case "rain":
				case "storm": world.SetAir(new AirOffsets { Humidity = .45f, Pressure = -.5f, Instability = .3f, Wind = 6f }, 0); break;
				default: Fail("Use --weather=scene|clear|overcast|rain|storm"); yield break;
			}
			SkySystem.EditorCloudsOff = false;
			if (!SkySystem.DrawClouds) { Fail("Enable clouds in client graphics settings before a weather benchmark"); yield break; }
			world.ForcePresent();
			bool live = Arg("clock", "live") == "live";
			world.RunRate = 1; world.Paused = !live;
			if (!live) Time.timeScale = 0;
			Application.runInBackground = true;
			TerrainGpuRenderer.CountFamilies = false;
			StartCounters();
			Debug.Log($"[PlayerPerf] {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, {Screen.width}x{Screen.height}, native {aa}x MSAA, development={Debug.isDebugBuild}, frame timing={FrameTimingManager.IsFeatureEnabled()}");
			// Allow asynchronous terrain/grass adoption, then require a quiet build window.
			float start = Time.realtimeSinceStartup, ready = start;
			while (Time.realtimeSinceStartup - start < Number("settle", 35) || GrassBladeSystem.Building || DetailScatterSystem.Building || Time.realtimeSinceStartup - ready < 3)
			{
				if (GrassBladeSystem.Building || DetailScatterSystem.Building) ready = Time.realtimeSinceStartup;
				if (Time.realtimeSinceStartup - start > 180) { Fail("World adoption did not settle within 180 seconds"); yield break; }
				FrameTimingManager.CaptureFrameTimings(); yield return null;
			}
			if (Screen.width != 2560 || Screen.height != 1440) { Fail($"Required 2560x1440, got {Screen.width}x{Screen.height}"); yield break; }
			if (WeatherRenderProfile.Active == null || GrassBladeSystem.Renderer == null || DetailScatterSystem.Renderer == null || GrassBladeSystem.LastDrawCount == 0) { Fail("Missing grass/detail renderer or active render profile"); yield break; }
			foreach (var binder in FindObjectsByType<TerrainArrayBinder>()) if (binder.BoundSet == null || !binder.BoundSet.IsUsable) { Fail("Terrain arrays are not bound"); yield break; }
			StartCounters();
			List<View> views = Views();
			string at = Arg("at", ""); if (at.Length > 0) { var parts = at.Split(','); if (parts.Length != 6) { Fail("--at=x,y,z,targetX,targetY,targetZ"); yield break; } var values = Array.ConvertAll(parts, x => float.Parse(x, CultureInfo.InvariantCulture)); views.Add(new View { Name = "custom", Eye = new Vector3(values[0],values[1],values[2]), Target = new Vector3(values[3],values[4],values[5]) }); }
			string requested = Arg("views", "all");
			if (requested != "all") views.RemoveAll(v => Array.IndexOf(requested.Split(','), v.Name) < 0);
			if (views.Count == 0) { Fail("No benchmark views selected"); yield break; }
			bool indexAbba = Arg("mode", "baseline") == "terrain-index";
			bool grassAbba = Arg("mode", "baseline") == "grass-looks";
			string mode = Arg("mode", "baseline");
			bool three = mode == "three", coarse = mode == "grass-coarse", colour = mode == "grass-colour", foam = mode == "foam";
			bool extraAbba = three || coarse || colour || foam;
			// "settings": HarnessSettings lists, --set-a in the off phases and --set-b in the on phases (the browser probe's names).
			bool settingsAbba = mode == "settings";
			HarnessSettings.ApplyList(Arg("set", ""));
			bool abba = mode == "abba" || indexAbba || grassAbba || extraAbba || settingsAbba;
			bool[] phases = abba ? new[] { false, true, true, false } : new[] { false };
			int count = Mathf.Clamp(Number("samples", 600), 1, 3600), warm = Mathf.Clamp(Number("warm", 180), 30, 1200);
			bool moving = Arg("moving", "true") == "true";
			for (int v = 0; v < views.Count; v++)
			{
				for (int phase = 0; phase < phases.Length; phase++)
				{
					bool hlodOn = mode == "abba" && phases[phase];
					if (settingsAbba) HarnessSettings.ApplyList(phases[phase] ? Arg("set-b", "") : Arg("set-a", ""));
					GrassBladeRenderer.CoarseOcclusion = (three || coarse) ? phases[phase] : Arg("grass-coarse","on") != "off";
					GrassBladeRenderer.PrepareTerrainColour = (three || colour) ? phases[phase] : Arg("grass-colour","prepared") != "full";
					bool shortcut = (three || foam) ? phases[phase] : Arg("foam","shortcut") != "full";
					Shader.SetGlobalFloat("_FishFoamFullFallback",shortcut ? 0 : 1);
					GrassBladeRenderer.CacheFloraLooks = grassAbba ? phases[phase] : Arg("grass-looks","cached") != "per-vertex";
					bool available = SetHlod(hlodOn);
					Shader.SetGlobalFloat("_FishIndexFullFilter", indexAbba ? (phases[phase] ? 0 : 1) : Arg("index-filter","fast") == "full" ? 1 : 0);
					if (mode == "abba" && !available) { Fail("ABBA requested, but HLOD control is unavailable"); yield break; }
					View view = views[v]; camera.transform.SetPositionAndRotation(view.Eye, Quaternion.LookRotation(view.Target - view.Eye));
					// Populate the near foam simulation at this camera before holding time for image pairs.
					if (!live && phase == 0 && view.Name == "river-foam")
					{
						world.Paused = false; Time.timeScale = 1;
						float until = Time.realtimeSinceStartup + 5;
						while (Time.realtimeSinceStartup < until) { FrameTimingManager.CaptureFrameTimings(); yield return null; }
						world.Paused = true; Time.timeScale = 0; world.ForcePresent();
					}
					if (weatherCase == "storm" && phase == 0) { world.RemoveAllStorms(); world.SpawnCell(StormKind.Thunderstorm, speed: 0f, overhead: true); world.ForcePresent(); }
					for (int f = 0; f < warm; f++) { if (f < warm - 30 && f % 30 == 0) CloudStats.Poll(); FrameTimingManager.CaptureFrameTimings(); yield return null; }
					if (!SkySystem.CloudsReady || FishCloudsFeature.LastCloudBuffer == null) { Fail("Cloud renderer/buffer is unavailable"); yield break; }
					double worldStart = world.WorldSeconds; Vector2 driftStart = SkySystem.Instance.CloudDrift;
					if (Arg("profile", "false") == "true") EnableProfile();
					gpuProfile.Clear(); cpuProfile.Clear();
					samples.Clear(); int gcStart = GC.CollectionCount(0); ulong lastTiming = 0;
					for (int f = 0; f < count; f++)
					{
						float t = count > 1 ? (float)f / (count - 1) : 0;
						Vector3 offset = moving ? Vector3.right * (Mathf.Sin(t * Mathf.PI * 2) * 20) : Vector3.zero;
						Vector3 eye = view.Eye + offset; if (view.Name != "river-foam") eye.y = Mathf.Max(eye.y, Ground(eye.x, eye.z) + 2);
						camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(view.Target - eye));
						FrameTimingManager.CaptureFrameTimings(); yield return null;
						var sample = new Sample { frame = Time.unscaledDeltaTime * 1000, gpu = -1, cpu = -1, render = -1, draws = DrawCount(), setPass = setPass.Valid ? setPass.LastValue : -1 };
						if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].frameStartTimestamp != lastTiming)
						{
							lastTiming = timings[0].frameStartTimestamp;
							if (timings[0].gpuFrameTime > 0) sample.gpu = (float)timings[0].gpuFrameTime;
							sample.cpu = (float)timings[0].cpuMainThreadFrameTime; sample.render = (float)timings[0].cpuRenderThreadFrameTime;
						}
						samples.Add(sample);
						foreach (var pair in profile) { if (pair.Value.gpuSampleBlockCount > 0) gpuProfile[pair.Key] = gpuProfile.TryGetValue(pair.Key, out double g) ? g + pair.Value.gpuElapsedNanoseconds : pair.Value.gpuElapsedNanoseconds; if (pair.Value.sampleBlockCount > 0) cpuProfile[pair.Key] = cpuProfile.TryGetValue(pair.Key, out double c) ? c + pair.Value.elapsedNanoseconds : pair.Value.elapsedNanoseconds; }
					}
					WriteProfile(Path.Combine(output, $"{v:D2}-{phase}-{view.Name}-profile.csv"), count);
					var row = Summarize(view, hlodOn, available, moving, live, pipeline, GC.CollectionCount(0) - gcStart);
					if (row.gpuSamples < samples.Count / 2) { Fail("Insufficient GPU timing samples"); yield break; }
					row.coarseGrass = GrassBladeRenderer.CoarseOcclusion; row.preparedGrassColour = GrassBladeRenderer.PrepareTerrainColour; row.foamShortcut = shortcut;
					row.foamWindow = Shader.GetGlobalVector("_FishFoamFieldRect0");
					Vector2 foamUv = (new Vector2(view.Target.x,view.Target.z)-new Vector2(row.foamWindow.x,row.foamWindow.y))/Mathf.Max(1,row.foamWindow.z);
					row.foamTargetCoverage = row.foamWindow.w < .5f ? 0 : Mathf.Clamp01(Mathf.Min(foamUv.x,1-foamUv.x,foamUv.y,1-foamUv.y)*12);
					if (view.Name == "river-foam" && row.foamTargetCoverage < .999f) { Fail("River view has no full carried-foam coverage"); yield break; }
					row.waterFixture = Arg("water-fixture","false") == "true";
					row.weather = weatherCase; row.weatherFieldDriven = world.FieldDriven;
					row.cloudsReady = SkySystem.CloudsReady; row.cloudsEnabled = SkySystem.DrawClouds;
					row.cloudBufferWidth = FishCloudsFeature.LastCloudBuffer.width; row.cloudBufferHeight = FishCloudsFeature.LastCloudBuffer.height;
					row.worldSecondsStart = worldStart; row.worldSecondsEnd = world.WorldSeconds;
					row.cloudDriftStart = driftStart; row.cloudDriftEnd = SkySystem.Instance.CloudDrift;
					row.cloudCover = SkySystem.Instance.CloudCover; row.measuredCloudCover = CloudStats.MeasuredCover; row.measuredAnyCloud = CloudStats.MeasuredAnyCloud;
					WeatherFrame shown = world.Presentation.Shown; row.precipitation = shown[WeatherChannel.Precipitation]; row.wind = shown[WeatherChannel.WindSpeed];
					row.precipitationKinds = world.Presentation.Precipitation.Drawn.Count;
					if (live && row.worldSecondsEnd <= worldStart) { Fail("World clock did not advance during measurement"); yield break; }
					if (live && row.wind > .01f && Vector2.Distance(driftStart, row.cloudDriftEnd) < 1e-5f) { Fail("Wind is active but cloud drift did not advance"); yield break; }
					if ((weatherCase == "rain" || weatherCase == "storm") && (row.precipitation < .01f || row.precipitationKinds == 0)) { Fail("Rain/storm benchmark did not draw precipitation"); yield break; }
					row.grassLooks = GrassBladeRenderer.CacheFloraLooks ? "cached" : "per-vertex";
					row.variant = settingsAbba ? (phases[phase] ? "b:" + Arg("set-b", "") : "a:" + Arg("set-a", "")) : extraAbba ? mode + (phases[phase] ? "-on" : "-off") : grassAbba ? (phases[phase] ? "grass-looks-cached" : "grass-looks-per-vertex") : indexAbba ? (phases[phase] ? "index-fast" : "index-full") : "index-"+Arg("index-filter","fast");
					results.Add(row); File.WriteAllText(Path.Combine(output, $"{v:D2}-{phase}-{view.Name}.json"), JsonUtility.ToJson(row, true));
					WriteSamples(Path.Combine(output, $"{v:D2}-{phase}-{view.Name}.csv"));
					Debug.Log($"[PlayerPerf] weather={weatherCase}, field={row.weatherFieldDriven}, cover={row.cloudCover:F3}, measured={row.measuredCloudCover:F3}, rain={row.precipitation:F3}, kinds={row.precipitationKinds}, clock advanced={row.worldSecondsEnd-worldStart:F3}s, drift={Vector2.Distance(driftStart,row.cloudDriftEnd):F3}m");
					Debug.Log($"[PlayerPerf] {view.Name} HLOD={hlodOn} {row.variant} FPS={row.fps:F1}, frame median/p95={row.frameMedian:F2}/{row.frameP95:F2}, GPU={row.gpuMedian:F2} ({row.gpuSamples} samples), {HlodDescription()}");
					// Screenshots and encoding are outside the timed window, followed by a new warm-up.
					if (Arg("screenshots", "true") == "true")
					{
						yield return new WaitForEndOfFrame(); var image = ScreenCapture.CaptureScreenshotAsTexture();
						File.WriteAllBytes(Path.Combine(output, $"{v:D2}-{phase}-{view.Name}.png"), image.EncodeToPNG()); Destroy(image);
					}
				}
			}
			if (errors > 0) { Fail($"Benchmark logged {errors} rendering/runtime errors; see errors.log"); yield break; }
			File.WriteAllText(Path.Combine(output, "DONE"), "Completed\n"); Application.Quit(0);
		}
		private void EnableProfile()
		{
			var names = new List<string>(); Sampler.GetNames(names); foreach (string name in names) { if (profile.ContainsKey(name)) continue; var recorder = Recorder.Get(name); if (recorder != null && recorder.isValid) { recorder.enabled = true; profile.Add(name, recorder); } }
		}
		private void WriteProfile(string path, int count)
		{
			if (profile.Count == 0) return; using var writer = new StreamWriter(path); writer.WriteLine("marker,gpu_mean_ms,cpu_mean_ms");
			foreach (var pair in profile) { gpuProfile.TryGetValue(pair.Key, out double g); cpuProfile.TryGetValue(pair.Key, out double c); if (g > 0 || c > 0) writer.WriteLine("\"" + pair.Key.Replace("\"", "\"\"") + "\"," + (g / count / 1e6).ToString("R", CultureInfo.InvariantCulture) + "," + (c / count / 1e6).ToString("R", CultureInfo.InvariantCulture)); }
		}
		private void StartCounters()
		{
			var handles = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(handles);
			var counters = new List<string>();
			foreach (var handle in handles)
			{
				var info = ProfilerRecorderHandle.GetDescription(handle); counters.Add(info.Category + ": " + info.Name);
				if (!draws.Valid && info.Name == "Draw Calls Count") draws = ProfilerRecorder.StartNew(info.Category, info.Name, 1);
				if (!setPass.Valid && info.Name == "SetPass Calls Count") setPass = ProfilerRecorder.StartNew(info.Category, info.Name, 1);
			}
			if (!draws.Valid && drawPaths.Count == 0) foreach (string name in new[] { "Standard Draw Calls Count", "Standard Indirect Draw Calls Count", "Standard Instanced Draw Calls Count", "SRP Batcher Draw Calls Count", "BRG Draw Calls Count", "BRG Indirect Draw Calls Count", "Null Geometry Draw Calls Count", "Null Geometry Indirect Draw Calls Count", "Dynamic Batched Draw Calls Count", "Static Batched Draw Calls Count", "Instanced Batched Draw Calls Count" })
			{ var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1); if (recorder.Valid) drawPaths.Add(recorder); else recorder.Dispose(); }
			File.WriteAllLines(Path.Combine(output, "counters.txt"), counters);
		}
		private float DrawCount() { if (draws.Valid) return draws.LastValue; if (drawPaths.Count == 0) return -1; long total = 0; foreach (var path in drawPaths) total += path.LastValue; return total; }
		private Summary Summarize(View view, bool hlod, bool available, bool moving, bool live, UniversalRenderPipelineAsset pipeline, int collections)
		{
			var frame = new List<float>(); var gpu = new List<float>(); var cpu = new List<float>(); var render = new List<float>(); var draw = new List<float>(); var pass = new List<float>(); double total = 0;
			foreach (var s in samples) { frame.Add(s.frame); total += s.frame; if (s.gpu >= 0) gpu.Add(s.gpu); if (s.cpu >= 0) cpu.Add(s.cpu); if (s.render >= 0) render.Add(s.render); if (s.draws >= 0) draw.Add(s.draws); if (s.setPass >= 0) pass.Add(s.setPass); }
			return new Summary { scene = SceneManager.GetActiveScene().name, view = view.Name, gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), unity = Application.unityVersion, quality = QualitySettings.names[QualitySettings.GetQualityLevel()], clock = live ? "live 1x" : "frozen", feature = HlodDescription(), width = Screen.width, height = Screen.height, samples = samples.Count, gpuSamples = gpu.Count, moving = moving, hlod = hlod, hlodAvailable = available, development = Debug.isDebugBuild, renderScale = pipeline.renderScale, msaa = pipeline.msaaSampleCount, fieldOfView = camera.fieldOfView, fps = total > 0 ? (float)(1000 * samples.Count / total) : 0, frameMedian = Percentile(frame, .5f), frameP95 = Percentile(frame, .95f), frameP99 = Percentile(frame, .99f), gpuMedian = Percentile(gpu, .5f), gpuP95 = Percentile(gpu, .95f), cpuMedian = Percentile(cpu, .5f), renderMedian = Percentile(render, .5f), drawMedian = Percentile(draw, .5f), setPassMedian = Percentile(pass, .5f), allocatedBytes = Profiler.GetTotalAllocatedMemoryLong(), reservedBytes = Profiler.GetTotalReservedMemoryLong(), gcCollections = collections, eye = view.Eye, target = view.Target };
		}
		private void WriteSamples(string path)
		{
			using var writer = new StreamWriter(path); writer.WriteLine("frame_ms,gpu_ms,cpu_main_ms,cpu_render_ms,draws,setpass");
			foreach (var s in samples) writer.WriteLine(string.Join(",", s.frame.ToString("R", CultureInfo.InvariantCulture), s.gpu.ToString("R", CultureInfo.InvariantCulture), s.cpu.ToString("R", CultureInfo.InvariantCulture), s.render.ToString("R", CultureInfo.InvariantCulture), s.draws.ToString("R", CultureInfo.InvariantCulture), s.setPass.ToString("R", CultureInfo.InvariantCulture)));
		}
		private List<View> Views()
		{
			var anchors = new Dictionary<string, Dictionary<Vector2Int, (int Count, Vector3 Sum)>>();
			foreach (string kind in new[] { "forest", "rocks", "settlement" }) anchors[kind] = new Dictionary<Vector2Int, (int, Vector3)>();
			foreach (var holder in FindObjectsByType<SceneProps>(FindObjectsSortMode.None)) foreach (var set in holder.Sets)
			{
				if (set == null) continue;
				foreach (var prop in set.Props)
				{
					if (prop.Prototype < 0 || prop.Prototype >= set.Prototypes.Length) continue;
					string name = set.Prototypes[prop.Prototype].Prefab != null ? set.Prototypes[prop.Prototype].Prefab.name : "";
					string kind = name.StartsWith("Tree", StringComparison.OrdinalIgnoreCase) ? "forest" : name.StartsWith("Structure", StringComparison.OrdinalIgnoreCase) ? "settlement" : name.StartsWith("Rock", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Cliff", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Boulder", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Formation", StringComparison.OrdinalIgnoreCase) ? "rocks" : null;
					if (kind == null) continue;
					var cell = new Vector2Int(Mathf.FloorToInt(prop.Position.x / 128), Mathf.FloorToInt(prop.Position.z / 128));
					anchors[kind].TryGetValue(cell, out var a); anchors[kind][cell] = (a.Count + 1, a.Sum + prop.Position);
				}
			}
			var views = new List<View>(); Vector3 spawn = new Vector3(0, Ground(0, 0) + 2, 0); views.Add(new View { Name = "ground", Eye = spawn, Target = spawn + Vector3.forward * 100 });
			views.Add(new View { Name = "sky-up", Eye = spawn, Target = spawn + new Vector3(0,80,100) });
			foreach (var kind in anchors)
			{
				Vector3 centre = Vector3.zero; int most = 0;
				foreach (var a in kind.Value.Values) if (a.Count > most) { most = a.Count; centre = a.Sum / a.Count; }
				if (most == 0) continue;
				centre.y = Ground(centre.x, centre.z) + 4;
				Vector3 eye = centre + new Vector3(120, 65, -120); eye.y = Mathf.Max(eye.y, Ground(eye.x, eye.z) + 8);
				views.Add(new View { Name = kind.Key + "-vista", Eye = eye, Target = centre });
				if (kind.Key == "settlement") { var near = centre + new Vector3(28, 14, -28); near.y = Mathf.Max(near.y, Ground(near.x, near.z) + 6); views.Add(new View { Name = "settlement-close", Eye = near, Target = centre }); }
				if (kind.Key == "settlement") { var far = centre + new Vector3(600,220,-600); far.y = Mathf.Max(far.y, Ground(far.x,far.z) + 8); views.Add(new View { Name = "settlement-horizon",Eye = far,Target = centre }); }
				if (kind.Key == "forest") { eye = centre + new Vector3(0, 0, -16); eye.y = Ground(eye.x, eye.z) + 2; views.Add(new View { Name = "forest-ground", Eye = eye, Target = new Vector3(centre.x, eye.y, centre.z) }); }
			}
			// Controlled shader diagnostic. Authored Moron 2 water is underground and obstructed at these automatic anchors.
			// Never create this fixture in the general-world FPS comparisons.
			if (Arg("water-fixture","false") == "true")
			{
				if (Arg("views","") != "river-foam") throw new InvalidOperationException("Water fixture must be measured separately from world views");
				Vector3 at = spawn + Vector3.up * 6;
				CreateWaterFixture(at);
				views.Add(new View { Name = "river-foam",Eye = at + new Vector3(0,3,-5),Target = at });
			}
			WorldHlodData.Cluster best = null; float bestDistance = 0; int bestScore = 0;
			float factor = TerrainTreeMath.ScreenFactor(camera.fieldOfView,false,0,QualitySettings.lodBias);
			foreach (var owner in FindObjectsByType<WorldHlod>()) if (owner.Data != null) foreach (var group in owner.Data.Clusters)
			{
				float low = Mathf.Max(20,group.MinDistanceFactor*factor), high = Mathf.Min(CliffRockInstancing.DrawDistanceFor(group.MinSourceSize)*(1-CliffRockInstancing.ThinShare),group.MaxDistanceFactor*factor);
				int score = group.OriginalParts - group.Parts.Length;
				if (high > low + group.Bounds.extents.magnitude*2 + 4 && score > bestScore) { best = group; bestDistance = (low+high)*.5f; bestScore = score; }
			}
			if (best != null) { Vector3 eye = best.Bounds.center + new Vector3(.15f,1,.1f).normalized*bestDistance; eye.y = Mathf.Max(eye.y,Ground(eye.x,eye.z)+8); views.Add(new View { Name = "hlod-diagnostic",Eye = eye,Target = best.Bounds.center }); }

			return views;
		}
		private void CreateWaterFixture(Vector3 centre)
		{
			MeshRenderer source = null;
			foreach (var owner in FindObjectsByType<FishMMO.Shared.Biomes.SceneWaterBodies>())
			foreach (var r in owner.GetComponentsInChildren<MeshRenderer>())
				if (r.sharedMaterial != null && r.sharedMaterial.shader.name == "FishMMO/Water/Inland Water") { source = r; break; }
			if (source == null) throw new InvalidOperationException("No authored inland-water material for the foam fixture");
			const int side = 97; const float cell = .5f;
			var positions = new Vector3[side*side]; var normals = new Vector3[positions.Length]; var colours = new Color32[positions.Length];
			var uv = new Vector2[positions.Length]; var flow = new Vector2[positions.Length]; var state = new Vector2[positions.Length]; var tide = new Vector2[positions.Length]; var bank = new Vector4[positions.Length]; var look = new Vector2[positions.Length];
			for (int z=0;z<side;z++) for(int x=0;x<side;x++)
			{
				int i=z*side+x; float px=(x-(side-1)*.5f)*cell, pz=(z-(side-1)*.5f)*cell;
				positions[i]=new Vector3(px,0,pz); normals[i]=Vector3.up; colours[i]=new Color32(192,255,128,255);
				uv[i]=new Vector2(pz,px); flow[i]=new Vector2(0,2.5f); state[i]=new Vector2(.6f,1); bank[i]=new Vector4(24,2.5f,0,1);
			}
			var indices = new int[(side-1)*(side-1)*6]; int n=0;
			for(int z=0;z<side-1;z++) for(int x=0;x<side-1;x++) { int i=z*side+x; indices[n++]=i;indices[n++]=i+side;indices[n++]=i+1;indices[n++]=i+1;indices[n++]=i+side;indices[n++]=i+side+1; }
			var mesh=new Mesh { name="Controlled rapid foam diagnostic", vertices=positions,normals=normals,colors32=colours,uv=uv,triangles=indices };
			mesh.SetUVs(1,flow);mesh.SetUVs(2,state);mesh.SetUVs(3,tide);mesh.SetUVs(4,bank);mesh.SetUVs(5,look);mesh.RecalculateBounds();
			var host=new GameObject("Controlled water shader diagnostic");host.transform.position=centre;host.AddComponent<MeshFilter>().sharedMesh=mesh;
			var renderer=host.AddComponent<MeshRenderer>(); waterFixture=renderer; renderer.sharedMaterial=new Material(source.sharedMaterial); renderer.sharedMaterial.SetFloat("_RiverFallCount",0);
			var properties=new MaterialPropertyBlock();source.GetPropertyBlock(properties);renderer.SetPropertyBlock(properties);
			renderer.shadowCastingMode=ShadowCastingMode.Off; FishMMO.Water.WaterFoamField.AddInland(renderer);
			Debug.Log("[PlayerPerf] Controlled 48 m rapid fixture; synthetic geometry, authored inland-water material and live foam simulation. Excluded from world FPS claims.");
		}

		private static float Ground(float x, float z)
		{
			foreach (var t in Terrain.activeTerrains) { Vector3 p = t.GetPosition(), size = t.terrainData.size; if (x >= p.x && x <= p.x + size.x && z >= p.z && z <= p.z + size.z) return t.SampleHeight(new Vector3(x, 0, z)) + p.y; } return 0;
		}
		private void LogFailure(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; File.AppendAllText(Path.Combine(output, "errors.log"), message + "\n" + stack + "\n"); } }
		private void Fail(string reason) { Debug.LogError("[PlayerPerf] " + reason); File.WriteAllText(Path.Combine(output, "FAILED"), reason); Application.Quit(2); }
		private void OnDestroy() { if (waterFixture != null) { FishMMO.Water.WaterFoamField.RemoveInland(waterFixture); Destroy(waterFixture.sharedMaterial); Destroy(waterFixture.GetComponent<MeshFilter>().sharedMesh); Destroy(waterFixture.gameObject); } Application.logMessageReceived -= LogFailure; draws.Dispose(); setPass.Dispose(); foreach (var recorder in drawPaths) recorder.Dispose(); foreach (var recorder in profile.Values) recorder.enabled = false; }
	}
}
