using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.TestHarness.World;
using FishMMO.TestHarness.World.Editor;
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
			/// <summary>Snow, wet, ash and sand held at this depth instead of accumulating.</summary>
			public WeatherCover? Cover;
			/// <summary>
			/// The ground must look different with that cover than without it. The frame is rendered
			/// twice, so a surface shader that quietly does nothing fails here.
			/// </summary>
			public bool ExpectSurface;
			/// <summary>Longer than the usual settle, for anything that has to wait for an event.</summary>
			public float Settle;
			/// <summary>
			/// A preset to apply first, before <see cref="Stage.Preset"/>. The stage then checks
			/// that the sky really changed over to the second one: weather that is asked to hand
			/// over to different weather must not keep the first one falling.
			/// </summary>
			public string PresetBefore;
			/// <summary>Runs the cover forward this many seconds before the capture.</summary>
			public float AdvanceCover;
			/// <summary>
			/// The cover map must differ from place to place: a storm passing over one field leaves
			/// that field white and the next one bare. Without this, a scene-wide figure would pass
			/// every other check while the ground told a lie.
			/// </summary>
			public bool ExpectCoverVariation;
		}

		private static readonly Stage[] Stages =
		{
			new Stage { Name = "heavy-rain", Preset = "Heavy Rain", Temperature = 0.3f, Expect = PrecipitationKind.Rain, ExpectFog = true },
			new Stage { Name = "thunderstorm-performant", Preset = "Thunderstorm", Temperature = 0.3f, Quality = 0, Expect = PrecipitationKind.Rain },
			new Stage { Name = "rain-under-pavilion", Preset = "Heavy Rain", Temperature = 0.3f, CameraPosition = new Vector3(-6f, 1.6f, 4.2f), CameraEuler = new Vector3(-5f, 10f, 0f), Expect = PrecipitationKind.Rain },
			new Stage { Name = "same-storm-frozen", Preset = "Heavy Rain", Temperature = -0.8f, Expect = PrecipitationKind.Snow },
			new Stage { Name = "blizzard", Preset = "Blizzard", Temperature = -1f, Quality = 2, Expect = PrecipitationKind.Snow, ExpectFog = true },
			new Stage { Name = "hailstorm", Preset = "Hailstorm", Temperature = 0.1f, Expect = PrecipitationKind.Hail },
			new Stage { Name = "sandstorm", Preset = "Sandstorm", Temperature = 0.7f, Time = 0.35, Expect = PrecipitationKind.Sand, ExpectFog = true },
			new Stage { Name = "ashfall", Preset = "Ashfall", Temperature = 0.2f, Time = 0.32, Expect = PrecipitationKind.Ash, ExpectFog = true },
			new Stage { Name = "mist", Preset = "Mist", Temperature = 0.1f, Time = 0.28, Expect = PrecipitationKind.None, ExpectFog = true },
			new Stage { Name = "clear", Preset = "Clear", Temperature = 0.2f, Expect = PrecipitationKind.None },
			new Stage
			{
				// The ground after rain: darker, shinier, with standing water in the hollows.
				Name = "wet-ground", Preset = "Light Rain", Temperature = 0.4f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Rain,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(28f, 20f, 0f),
				Cover = new WeatherCover { Wet = 1f }, ExpectSurface = true,
			},
			new Stage
			{
				// Snow lying on everything level, and on nothing under the pavilion roof.
				Name = "snow-cover", Preset = "Light Snow", Temperature = -1f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Snow,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(24f, 20f, 0f),
				Cover = new WeatherCover { Snow = 1f }, ExpectSurface = true,
			},
			new Stage
			{
				// Ash: the same settling, a different colour, and it does not shine.
				Name = "ash-cover", Preset = "Ashfall", Temperature = 0.3f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Ash, ExpectFog = true,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(24f, 20f, 0f),
				Cover = new WeatherCover { Ash = 1f }, ExpectSurface = true,
			},
			new Stage
			{
				// Rain, then snow. The complaint this pins: the sky kept raining and no snow fell,
				// because falling snow turns to rain above freezing and the bed was warm.
				Name = "rain-then-snow", PresetBefore = "Heavy Rain", Preset = "Heavy Snow", Temperature = 0.4f, Time = 0.5, Sun = 1,
				Settle = 9f, Expect = PrecipitationKind.Snow,
			},
			new Stage
			{
				// A blizzard cell drifts across one corner of the field. After a quarter of an hour
				// of it, the ground under the cell is white and the ground outside it is not.
				Name = "cover-under-cell", Preset = "Clear", Cell = "Blizzard", Temperature = -1f, Time = 0.5, Sun = 1,
				CameraPosition = new Vector3(-2f, 12f, -26f), CameraEuler = new Vector3(22f, 12f, 0f),
				AdvanceCover = 1200f, ExpectCoverVariation = true, Expect = PrecipitationKind.None,
			},
			new Stage
			{
				// The hill is real terrain on the forked terrain shader, and High is the tier where
				// deep snow lifts the ground it lies on.
				Name = "terrain-snow", Preset = "Light Snow", Temperature = -1f, Time = 0.5, Sun = 1, Quality = 2, Expect = PrecipitationKind.Snow,
				CameraPosition = new Vector3(26f, 7f, 18f), CameraEuler = new Vector3(6f, 16f, 0f),
				Cover = new WeatherCover { Snow = 1f }, ExpectSurface = true,
			},
			new Stage { Name = "sky-dawn", Preset = "Clear", Temperature = 0.2f, Time = 0.255, CameraEuler = new Vector3(-6f, 90f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-noon", Preset = "Fair", Temperature = 0.3f, Time = 0.5, CameraEuler = new Vector3(-35f, 180f, 0f), Sun = 1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-dusk", Preset = "Clear", Temperature = 0.2f, Time = 0.745, CameraEuler = new Vector3(-6f, 270f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night-performant", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, Quality = 0, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lunar-eclipse", Preset = "Clear", Temperature = 0.1f, Time = 0.0, Moonlit = true, Eclipse = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-storm-cell", Preset = "Clear", Cell = "Thunderstorm", Temperature = 0.3f, Time = 0.62, CameraEuler = new Vector3(-8f, 0f, 0f), Sun = 1, ExpectCurtain = true, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lightning-night", Settle = 14f, Preset = "Thunderstorm", Temperature = 0.3f, Time = 0.95, CameraEuler = new Vector3(-20f, 30f, 0f), Sun = -1, ExpectStrikes = true, Expect = PrecipitationKind.Rain },
			new Stage { Name = "sky-aurora", Preset = "Aurora Night", Temperature = -0.6f, Time = 0.02, Day = 355f, Latitude = 72f, CameraEuler = new Vector3(-30f, 0f, 0f), Sun = -1, ExpectAurora = true, Expect = PrecipitationKind.None },
		};

		private static string outputDirectory;
		private static int stageIndex;
		private static double stageStarted;
		private static int failures;
		private static int strikesAtStart;
		private static int scheduledStrikes;
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

			if (!File.Exists(WorldSimSceneGenerator.ScenePath) || Environment.GetEnvironmentVariable("FISHMMO_WEATHER_REGENERATE") == "1")
			{
				WorldSimSceneGenerator.Generate();
			}
			EditorSceneManager.OpenScene(WorldSimSceneGenerator.ScenePath, OpenSceneMode.Single);
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

		private static WorldSimController Controller() => UnityEngine.Object.FindFirstObjectByType<WorldSimController>();

		private static void Pump()
		{
			try
			{
				WorldSimController controller = Controller();
				if (controller == null)
				{
					return;
				}
				float settle = stageIndex >= 0 && Stages[stageIndex].Settle > 0f ? Stages[stageIndex].Settle : SettleSeconds;
				if (stageIndex >= 0 && EditorApplication.timeSinceStartup - stageStarted < settle)
				{
					return;
				}
				if (stageIndex >= 0)
				{
					Finish(controller, Stages[stageIndex]);
				}
				stageIndex++;
				while (stageIndex < Stages.Length && Skipped(Stages[stageIndex]))
				{
					stageIndex++;
				}
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

		/// <summary>
		/// True when <c>FISHMMO_WEATHER_RENDER_ONLY</c> names other stages: a comma-separated list,
		/// for working on one weather without waiting for the rest.
		/// </summary>
		private static bool Skipped(Stage stage)
		{
			string only = Environment.GetEnvironmentVariable("FISHMMO_WEATHER_RENDER_ONLY");
			if (string.IsNullOrEmpty(only))
			{
				return false;
			}
			foreach (string name in only.Split(','))
			{
				if (string.Equals(name.Trim(), stage.Name, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			return true;
		}

		private static void Start(WorldSimController controller, Stage stage)
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
			controller.ResetCover();
			controller.CoverOverride = stage.Cover;
			if (!string.IsNullOrEmpty(stage.PresetBefore))
			{
				WeatherPreset before = controller.Presets.Find(p => p != null && p.ResolvedName == stage.PresetBefore);
				controller.ApplyPreset(before, 0f);
				// Let it settle into the first weather, then hand over the way a player would.
				controller.ForcePresent();
			}
			WeatherPreset preset = controller.Presets.Find(p => p != null && p.ResolvedName == stage.Preset);
			// A stage that hands over from another weather does it the way a player would, over a
			// couple of seconds; the rest start where they mean to be.
			controller.ApplyPreset(preset, string.IsNullOrEmpty(stage.PresetBefore) ? 0f : 2f);
			scheduledStrikes = 0;
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

		private static void Finish(WorldSimController controller, Stage stage)
		{
			string path = Path.Combine(outputDirectory, $"WeatherSim-{stageIndex:00}-{stage.Name}.png");
			if (stage.AdvanceCover > 0f)
			{
				controller.AdvanceCover(stage.AdvanceCover);
			}
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
			if (stage.ExpectSurface)
			{
				// The same frame with the cover and without it: what the surface shaders do is the
				// difference. A shader that ignores the weather shows none.
				float covered = MeasureGround(controller);
				WeatherCover? held = controller.CoverOverride;
				controller.CoverOverride = default(WeatherCover);
				controller.ForcePresent();
				float bare = MeasureGround(controller);
				controller.CoverOverride = held;
				controller.ForcePresent();
				float change = Mathf.Abs(covered - bare);
				if (change < 0.01f)
				{
					problems.Add($"the ground looks the same covered and bare ({bare:0.000} → {covered:0.000}); are the world materials on FishMMO/Weather Lit?");
				}
			}
			if (stage.ExpectCoverVariation)
			{
				float spread = MeasureCoverSpread(controller, out float highest);
				if (highest < 0.15f)
				{
					problems.Add($"nothing settled anywhere: the deepest cover on the map is {highest:0.00}");
				}
				else if (spread < 0.03f)
				{
					problems.Add($"the cover map is flat ({spread:0.000} spread), so the ground does not follow the storm");
				}
			}
			string sky = CheckSky(controller, stage, problems);
			if (problems.Count > 0)
			{
				failures++;
			}
			report.Add($"{stage.Name}: {(problems.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", problems))} — {kind} {shown[WeatherChannel.Precipitation]:0.00}, fog {fog:0.00}, {drawn} drawn, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, map {(map ? "ready" : "missing")}, {sky} → {Path.GetFileName(path)}");
		}

		/// <summary>The first day whose midnight has the moon high and mostly lit, looking along the stage camera.</summary>
		private static float MoonlitNight(WorldSimController controller, float latitude, bool eclipse)
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
		private static string CheckSky(WorldSimController controller, Stage stage, List<string> problems)
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
			if (stage.ExpectStrikes && strikes == 0)
			{
				// A strike is scheduled into a slot of world time, so a short stage may simply fall
				// between two of them. What must never happen is a storm that schedules none at all:
				// ask the schedule over a minute of world time and fail on that instead of on luck.
				var window = new List<LightningStrike>();
				double now = SkySystem.Instance != null ? SkySystem.Instance.WorldSeconds : 0.0;
				SkySchedule.Lightning(controller.Timeline, (uint)controller.Tick, now, now + 60.0,
					controller.Camera != null ? controller.Camera.transform.position : Vector3.zero, window);
				scheduledStrikes = window.Count;
				if (window.Count == 0)
				{
					problems.Add("the storm schedules no lightning at all in the next minute of world time");
				}
			}
			int curtains = sky.Curtains != null ? sky.Curtains.Drawn : 0;
			if (stage.ExpectCurtain && curtains == 0) problems.Add("no rain curtain drawn");
			float aurora = Shader.GetGlobalVector("_FishAuroraParams").x;
			if (stage.ExpectAurora && aurora < 0.05f) problems.Add($"aurora only {aurora:0.00}");
			if (stage.Moonlit && (sky.State.Moon < 0 || sky.State.Bodies[sky.State.Moon].AltitudeDegrees <= 0f)) problems.Add("no moon up on a moonlit night");
			float shadow = sky.State.LunarEclipse;
			if (stage.Moonlit && stage.Eclipse && shadow < 0.5f) problems.Add($"the moon should be eclipsed, shadow {shadow:0.00}");
			if (stage.Moonlit && !stage.Eclipse && shadow > 0.05f) problems.Add($"the moon should be clear, shadow {shadow:0.00}");
			return $"sun {altitude:0.0}°, {strikes} strikes ({scheduledStrikes} scheduled), {curtains} curtains, aurora {aurora:0.00}, lunar shadow {sky.State.LunarEclipse:0.00}, {sky.Bodies?.QuadCount ?? 0} body quads";
		}

		/// <summary>
		/// How much the cover varies across the map, and how deep it gets anywhere on it. A map that
		/// is merely the scene's average repeated has no spread.
		/// </summary>
		private static float MeasureCoverSpread(WorldSimController controller, out float highest)
		{
			highest = 0f;
			WeatherCoverMap map = controller.Presentation != null ? controller.Presentation.CoverMap : null;
			if (map == null || map.Texture == null || !map.IsValid)
			{
				return 0f;
			}
			Color[] pixels = map.Texture.GetPixels();
			float sum = 0f, squares = 0f;
			foreach (Color p in pixels)
			{
				// Snow is what this stage lays down; the other channels ride on the same map.
				float value = p.r;
				highest = Mathf.Max(highest, value);
				sum += value;
				squares += value * value;
			}
			float mean = sum / pixels.Length;
			return Mathf.Sqrt(Mathf.Max(0f, squares / pixels.Length - mean * mean));
		}

		/// <summary>How bright the lower half of the frame is: the ground the cover settles on.</summary>
		private static float MeasureGround(WorldSimController controller)
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
				Color[] pixels = image.GetPixels();
				float sum = 0f;
				int counted = 0;
				for (int y = 0; y < Height / 2; y += 2)
				{
					for (int x = 0; x < Width; x += 2)
					{
						Color p = pixels[y * Width + x];
						sum += p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f;
						counted++;
					}
				}
				UnityEngine.Object.DestroyImmediate(image);
				return counted > 0 ? sum / counted : 0f;
			}
			finally
			{
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		private static void Capture(WorldSimController controller, string path)
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
		private static void OverlayPanel(WorldSimController controller, Texture2D image)
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
