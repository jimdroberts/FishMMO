using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.TestHarness.Weather.Editor
{
	/// <summary>
	/// Plays the Weather Sim scene and saves what the camera sees in a series of weathers, with
	/// the control panel laid over it. Also checks that each weather really produced what it
	/// should (something falling, the right kind, fog, a sky map), and fails the run if not.
	/// </summary>
	/// <remarks>
	/// Headless: <c>-executeMethod FishMMO.TestHarness.Weather.Editor.WeatherSimRender.Render</c>
	/// under xvfb, without <c>-batchmode</c>, so play mode presents frames. PNGs go to
	/// <c>FISHMMO_WEATHER_RENDER_DIR</c>, defaulting to <c>PanelRenders/</c>.
	/// </remarks>
	public static class WeatherSimRender
	{
		private const int Width = 1600;
		private const int Height = 900;
		private const float SettleSeconds = 5f;
		private const string StateKey = "FishMMO.WeatherSimRender.Active";
		private const string ReportKey = "FishMMO.WeatherSimRender.Report";
		private const string FailuresKey = "FishMMO.WeatherSimRender.Failures";

		private sealed class Stage
		{
			public string Name;
			public string Preset;
			public float Temperature;
			public double Time = 0.39;
			public float Day = 172f;
			public float Latitude = 35f;
			public string Cell;
			/// <summary>+1 sun must be up, -1 down, 0 either.</summary>
			public int Sun;
			public bool ExpectStrikes;
			public bool ExpectCurtain;
			public bool ExpectAurora;
			/// <summary>Pick the first night with a high, mostly lit moon (Day is ignored).</summary>
			public bool Moonlit;
			/// <summary>With <see cref="Moonlit"/>: pick a night whose moon is in the planet's shadow instead.</summary>
			public bool Eclipse;
			public int Quality = 1;
			public Vector3 CameraPosition = new Vector3(-2f, 1.8f, -6f);
			public Vector3 CameraEuler = new Vector3(4f, 20f, 0f);
			public PrecipitationKind Expect;
			public bool ExpectFog;
		}

		private static readonly Stage[] Stages =
		{
			new Stage { Name = "heavy-rain", Preset = "Heavy Rain", Temperature = 0.3f, Expect = PrecipitationKind.Rain, ExpectFog = true },
			new Stage { Name = "thunderstorm-performant", Preset = "Thunderstorm", Temperature = 0.3f, Quality = 0, Expect = PrecipitationKind.Rain },
			new Stage { Name = "rain-under-pavilion", Preset = "Heavy Rain", Temperature = 0.3f, CameraPosition = new Vector3(-6f, 1.6f, 4.2f), CameraEuler = new Vector3(-5f, 10f, 0f), Expect = PrecipitationKind.Rain },
			new Stage { Name = "same-storm-frozen", Preset = "Heavy Rain", Temperature = -0.8f, Expect = PrecipitationKind.Snow },
			new Stage { Name = "blizzard", Preset = "Blizzard", Temperature = -0.6f, Quality = 2, Expect = PrecipitationKind.Snow, ExpectFog = true },
			new Stage { Name = "hailstorm", Preset = "Hailstorm", Temperature = 0.1f, Expect = PrecipitationKind.Hail },
			new Stage { Name = "sandstorm", Preset = "Sandstorm", Temperature = 0.7f, Time = 0.35, Expect = PrecipitationKind.Sand, ExpectFog = true },
			new Stage { Name = "ashfall", Preset = "Ashfall", Temperature = 0.2f, Time = 0.32, Expect = PrecipitationKind.Ash, ExpectFog = true },
			new Stage { Name = "mist", Preset = "Mist", Temperature = 0.1f, Time = 0.28, Expect = PrecipitationKind.None, ExpectFog = true },
			new Stage { Name = "clear", Preset = "Clear", Temperature = 0.2f, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-dawn", Preset = "Clear", Temperature = 0.2f, Time = 0.255, CameraEuler = new Vector3(-6f, 90f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-noon", Preset = "Fair", Temperature = 0.3f, Time = 0.5, CameraEuler = new Vector3(-35f, 180f, 0f), Sun = 1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-dusk", Preset = "Clear", Temperature = 0.2f, Time = 0.745, CameraEuler = new Vector3(-6f, 270f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night-performant", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, Quality = 0, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lunar-eclipse", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, Eclipse = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-storm-cell", Preset = "Clear", Cell = "Thunderstorm", Temperature = 0.3f, Time = 0.62, CameraEuler = new Vector3(-8f, 0f, 0f), Sun = 1, ExpectCurtain = true, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lightning-night", Preset = "Thunderstorm", Temperature = 0.3f, Time = 0.95, CameraEuler = new Vector3(-20f, 30f, 0f), Sun = -1, ExpectStrikes = true, Expect = PrecipitationKind.Rain },
			new Stage { Name = "sky-aurora", Preset = "Aurora Night", Temperature = -0.6f, Time = 0.02, Day = 355f, Latitude = 72f, CameraEuler = new Vector3(-30f, 0f, 0f), Sun = -1, ExpectAurora = true, Expect = PrecipitationKind.None },
		};

		private static string outputDirectory;
		private static int stageIndex;
		private static double stageStarted;
		private static int failures;
		private static int strikesAtStart;
		private static readonly List<string> report = new List<string>();
		private static PanelSettings uiSettings;
		private static RenderTexture uiTarget;

		[DashboardTool(DashboardToolAttribute.UITests, "Render Weather Sim", Section = "Renders", Order = 21,
			Tooltip = "Plays the Weather Sim scene through its weather and sky stages and writes a PNG of each to PanelRenders/.")]
		public static void Render()
		{
			outputDirectory = Environment.GetEnvironmentVariable("FISHMMO_WEATHER_RENDER_DIR");
			if (string.IsNullOrEmpty(outputDirectory))
			{
				outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "PanelRenders"));
			}
			Directory.CreateDirectory(outputDirectory);
			SessionState.SetString(StateKey, outputDirectory);

			if (!File.Exists(WeatherSimSceneGenerator.ScenePath) || Environment.GetEnvironmentVariable("FISHMMO_WEATHER_REGENERATE") == "1")
			{
				WeatherSimSceneGenerator.Generate();
			}
			EditorSceneManager.OpenScene(WeatherSimSceneGenerator.ScenePath, OpenSceneMode.Single);
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
			EditorApplication.EnterPlaymode();
		}

		/// <summary>Play mode reloads the domain unless it is disabled; the session key carries the run across.</summary>
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
			else if (change == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(StateKey, string.Empty)))
			{
				SessionState.EraseString(StateKey);
				EditorApplication.playModeStateChanged -= OnPlayMode;
				ReleaseUi();
				// Statics may not survive leaving play mode; the session copy does.
				string summary = SessionState.GetString(ReportKey, string.Empty);
				failures = SessionState.GetInt(FailuresKey, 1);
				if (string.IsNullOrEmpty(summary))
				{
					summary = "No stage reported.";
					failures = Mathf.Max(failures, 1);
				}
				File.WriteAllText(Path.Combine(outputDirectory ?? ".", "WeatherSim-report.txt"), summary);
				Debug.Log($"[WeatherSimRender] {(failures == 0 ? "PASS" : $"FAIL ({failures})")}\n{summary}");
				EditorAutomation.Finish(failures == 0 ? 0 : 1);
			}
		}

		private static void Begin()
		{
			outputDirectory = SessionState.GetString(StateKey, outputDirectory);
			stageIndex = -1;
			failures = 0;
			report.Clear();
			SessionState.EraseString(ReportKey);
			SessionState.SetInt(FailuresKey, 0);
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static WeatherSimController Controller() => UnityEngine.Object.FindFirstObjectByType<WeatherSimController>();

		private static void Pump()
		{
			try
			{
				WeatherSimController controller = Controller();
				if (controller == null)
				{
					return;
				}
				if (stageIndex >= 0 && EditorApplication.timeSinceStartup - stageStarted < SettleSeconds)
				{
					return;
				}
				if (stageIndex >= 0)
				{
					Finish(controller, Stages[stageIndex]);
				}
				stageIndex++;
				if (stageIndex >= Stages.Length)
				{
					EditorApplication.update -= Pump;
					Save();
					EditorApplication.ExitPlaymode();
					return;
				}
				Start(controller, Stages[stageIndex]);
			}
			catch (Exception ex)
			{
				failures++;
				report.Add($"ERROR {ex}");
				EditorApplication.update -= Pump;
				Save();
				EditorApplication.ExitPlaymode();
			}
		}

		private static void Start(WeatherSimController controller, Stage stage)
		{
			QualitySettings.SetQualityLevel(Mathf.Clamp(stage.Quality, 0, QualitySettings.names.Length - 1), true);
			controller.Camera.transform.position = stage.CameraPosition;
			controller.Camera.transform.rotation = Quaternion.Euler(stage.CameraEuler);
			if (stage.Moonlit)
			{
				MoonlitNight(controller, stage.Latitude, stage.Eclipse);
				controller.Camera.transform.rotation = Quaternion.Euler(-Mathf.Min(moonAltitude, 60f) + 8f, moonAzimuth, 0f);
			}
			controller.Temperature = stage.Temperature;
			controller.DayOfYear = stage.Moonlit ? MoonlitNight(controller, stage.Latitude, stage.Eclipse) : stage.Day;
			controller.TimeOfDay = stage.Time;
			controller.Latitude = stage.Latitude;
			controller.ClearAll(0f);
			WeatherPreset preset = controller.Presets.Find(p => p != null && p.ResolvedName == stage.Preset);
			controller.ApplyPreset(preset, 0f);
			strikesAtStart = SkySystem.Instance != null && SkySystem.Instance.Lightning != null ? SkySystem.Instance.Lightning.TotalStrikes : 0;
			if (!string.IsNullOrEmpty(stage.Cell))
			{
				WeatherPreset cell = controller.Presets.Find(p => p != null && p.ResolvedName == stage.Cell);
				if (cell == null)
				{
					failures++;
					report.Add($"{stage.Name}: FAIL no preset named {stage.Cell}");
				}
				controller.SpawnCell(cell, 400f, 0.5f);
			}
			if (preset == null)
			{
				failures++;
				report.Add($"{stage.Name}: FAIL no preset named {stage.Preset}");
			}
			stageStarted = EditorApplication.timeSinceStartup;
		}

		private static void Finish(WeatherSimController controller, Stage stage)
		{
			string path = Path.Combine(outputDirectory, $"WeatherSim-{stageIndex:00}-{stage.Name}.png");
			Capture(controller, path);

			WeatherFrame shown = controller.Presentation.Shown;
			PrecipitationKind kind = shown.DominantPrecipitation;
			float fog = WeatherFogPresenter.Amount(shown);
			int drawn = controller.Presentation.Precipitation.Drawn.Count;
			bool map = controller.Presentation.Occlusion.IsValid;
			var problems = new List<string>();
			bool kindOk = stage.Expect == PrecipitationKind.None
				? kind == PrecipitationKind.None || shown[WeatherChannel.Precipitation] < 0.05f
				: kind == stage.Expect || (stage.Expect == PrecipitationKind.Snow && kind == PrecipitationKind.Sleet);
			if (!kindOk) problems.Add($"expected {stage.Expect}, shows {kind}");
			if (stage.Expect != PrecipitationKind.None && drawn == 0) problems.Add("nothing drawn");
			if (stage.ExpectFog && fog < 0.2f) problems.Add($"fog only {fog:0.00}");
			if (!map) problems.Add("sky occlusion map never finished");
			if (stage.Name == "rain-under-pavilion" && !controller.Presentation.Occlusion.IsCovered(controller.Camera.transform.position))
			{
				problems.Add("the pavilion roof is not in the sky map");
			}
			string sky = CheckSky(stage, problems);
			if (problems.Count > 0)
			{
				failures++;
			}
			report.Add($"{stage.Name}: {(problems.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", problems))} — {kind} {shown[WeatherChannel.Precipitation]:0.00}, fog {fog:0.00}, {drawn} drawn, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, map {(map ? "ready" : "missing")}, {sky} → {Path.GetFileName(path)}");
		}

		/// <summary>The first day whose midnight has the moon high and mostly lit, looking along the stage camera.</summary>
		private static float MoonlitNight(WeatherSimController controller, float latitude, bool eclipse)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = system != null ? system.HomeWorld : null;
			if (body == null)
			{
				return 0f;
			}
			double longitude = controller.Settings != null ? controller.Settings.Longitude : 0.0;
			double day = CelestialMath.HomeSolarDayHours(system);
			double solarDay = CelestialMath.SolarDayHours(system, body);
			var state = new CelestialState();
			for (float d = 0f; d < 400f; d += 0.25f)
			{
				double hours = d * day;
				double shift = 0.0 - CelestialMath.LocalTime01(system, body, hours, longitude);
				hours += (shift - Math.Round(shift)) * solarDay;
				state.Compute(system, body, hours, latitude, longitude, 0f);
				if (state.Moon >= 0 && state.Bodies[state.Moon].AltitudeDegrees > 35f && state.Bodies[state.Moon].Illumination > 0.6f
					&& (eclipse ? state.Bodies[state.Moon].Shadowed > 0.6f : state.Bodies[state.Moon].Shadowed < 0.01f))
				{
					moonAzimuth = Mathf.Atan2(state.Bodies[state.Moon].Direction.x, state.Bodies[state.Moon].Direction.z) * Mathf.Rad2Deg;
					moonAltitude = state.Bodies[state.Moon].AltitudeDegrees;
					return d;
				}
			}
			return 0f;
		}

		private static float moonAzimuth;
		private static float moonAltitude;

		/// <summary>Checks the sky owns the render settings and shows what the stage asked for.</summary>
		private static string CheckSky(Stage stage, List<string> problems)
		{
			SkySystem sky = SkySystem.Instance;
			if (sky == null || sky.State == null)
			{
				problems.Add("no sky system or no celestial state");
				return "no sky";
			}
			if (sky.Profile != null && sky.Profile.SkyMaterial != null && (RenderSettings.skybox == null || RenderSettings.skybox.shader != sky.Profile.SkyMaterial.shader))
			{
				problems.Add($"skybox is {(RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "none")}");
			}
			if (RenderSettings.sun != sky.Sun && RenderSettings.sun != sky.Moon)
			{
				problems.Add("RenderSettings.sun is neither the sun nor the moon light");
			}
			float altitude = sky.State.SunAltitude;
			if (stage.Sun > 0 && altitude <= 0f) problems.Add($"sun should be up, is at {altitude:0.0}°");
			if (stage.Sun < 0 && altitude >= 0f) problems.Add($"sun should be down, is at {altitude:0.0}°");
			int strikes = sky.Lightning != null ? sky.Lightning.TotalStrikes - strikesAtStart : 0;
			if (stage.ExpectStrikes && strikes == 0) problems.Add("no lightning");
			int curtains = sky.Curtains != null ? sky.Curtains.Drawn : 0;
			if (stage.ExpectCurtain && curtains == 0) problems.Add("no rain curtain drawn");
			float aurora = Shader.GetGlobalVector("_FishAuroraParams").x;
			if (stage.ExpectAurora && aurora < 0.05f) problems.Add($"aurora only {aurora:0.00}");
			if (stage.Moonlit && (sky.State.Moon < 0 || sky.State.Bodies[sky.State.Moon].AltitudeDegrees <= 0f)) problems.Add("no moon up on a moonlit night");
			float shadow = sky.State.LunarEclipse;
			if (stage.Moonlit && stage.Eclipse && shadow < 0.5f) problems.Add($"the moon should be eclipsed, shadow {shadow:0.00}");
			if (stage.Moonlit && !stage.Eclipse && shadow > 0.05f) problems.Add($"the moon should be clear, shadow {shadow:0.00}");
			return $"sun {altitude:0.0}°, {strikes} strikes, {curtains} curtains, aurora {aurora:0.00}, lunar shadow {sky.State.LunarEclipse:0.00}, {sky.Bodies?.QuadCount ?? 0} body quads";
		}

		private static void Capture(WeatherSimController controller, string path)
		{
			Camera camera = controller.Camera;
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
			RenderTexture previousTarget = camera.targetTexture;
			RenderTexture previousActive = RenderTexture.active;
			try
			{
				camera.targetTexture = target;
				camera.Render();
				RenderTexture.active = target;
				var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				image.Apply();
				OverlayPanel(controller, image);
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		/// <summary>Redirects the panel into a texture (once) and blends the last panel frame over the image.</summary>
		private static void OverlayPanel(WeatherSimController controller, Texture2D image)
		{
			UIDocument document = controller.GetComponent<UIDocument>();
			if (document == null)
			{
				return;
			}
			if (uiTarget == null)
			{
				uiTarget = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32);
				uiSettings = UnityEngine.Object.Instantiate(document.panelSettings);
				uiSettings.targetTexture = uiTarget;
				uiSettings.clearColor = true;
				uiSettings.colorClearValue = new Color(0, 0, 0, 0);
				uiSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
				document.panelSettings = uiSettings;
				// The first capture has no panel yet; later ones do.
				return;
			}
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = uiTarget;
			var ui = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
			ui.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			ui.Apply();
			RenderTexture.active = previous;
			Color[] under = image.GetPixels();
			Color[] over = ui.GetPixels();
			for (int i = 0; i < under.Length; i++)
			{
				Color o = over[i];
				if (o.a > 0f)
				{
					under[i] = Color.Lerp(under[i], new Color(o.r, o.g, o.b, 1f), o.a);
				}
			}
			image.SetPixels(under);
			image.Apply();
			UnityEngine.Object.DestroyImmediate(ui);
		}

		private static void Save()
		{
			SessionState.SetString(ReportKey, string.Join("\n", report));
			SessionState.SetInt(FailuresKey, failures);
		}

		private static void ReleaseUi()
		{
			if (uiTarget != null)
			{
				uiTarget.Release();
				UnityEngine.Object.DestroyImmediate(uiTarget);
				uiTarget = null;
			}
			if (uiSettings != null)
			{
				UnityEngine.Object.DestroyImmediate(uiSettings);
				uiSettings = null;
			}
		}
	}
}
