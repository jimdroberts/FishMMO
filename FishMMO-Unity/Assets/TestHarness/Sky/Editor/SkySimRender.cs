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
using FishMMO.Shared.WorldDesign;

namespace FishMMO.TestHarness.Sky.Editor
{
	/// <summary>
	/// Plays the Sky Sim scene through a series of places, dates and times, saves a PNG of each with
	/// the panel over it, and checks that the sky really shows what the place should: the sun up or
	/// down, stars at night, a moon where one was asked for, a black airless sky, and an aurora.
	/// </summary>
	/// <remarks>
	/// Headless: <c>-executeMethod FishMMO.TestHarness.Sky.Editor.SkySimRender.Render</c> under
	/// xvfb, without <c>-batchmode</c>, so play mode presents frames. PNGs go to
	/// <c>FISHMMO_SKY_RENDER_DIR</c>, defaulting to <c>PanelRenders/</c>.
	/// </remarks>
	public static class SkySimRender
	{
		private const int Width = 1600;
		private const int Height = 900;
		private const float SettleSeconds = 2.5f;
		/// <summary>
		/// Renders taken at the capture size before the pixels are read, so the clouds' temporal
		/// history has something in it. Eight is a little past where the blend stops changing.
		/// </summary>
		private const int CaptureWarmFrames = 8;
		private const string StateKey = "FishMMO.SkySimRender.Active";
		private const string ReportKey = "FishMMO.SkySimRender.Report";
		private const string FailuresKey = "FishMMO.SkySimRender.Failures";

		private sealed class Stage
		{
			public string Name;
			/// <summary>The body to stand on, by name. Empty: the home world.</summary>
			public string Body;
			public float Latitude = 20f;
			public double Time = 0.5;
			public float Day = 172f;
			public Vector3 CameraEuler = new Vector3(-12f, 180f, 0f);
			public int Quality = 1;
			/// <summary>+1 the sun must be up, -1 down, 0 either.</summary>
			public int Sun;
			public bool ExpectStars;
			public bool ExpectMoon;
			public bool ExpectAurora;
			public bool ExpectBlackSky;
			public bool ExpectTextured;
			public bool ExpectComet;
			public bool ExpectAsteroids;
			public bool ExpectMeteors;
			public bool ExpectEclipse;
			public bool ExpectRainbow;
			/// <summary>The frame must be brighter around the sun with the light shafts than without.</summary>
			public bool ExpectGodRays;
			/// <summary>The ground must be more mottled with the clouds' shadow on it than without.</summary>
			public bool ExpectCloudShadow;
			/// <summary>The share of the frame the clouds must cover, judged from the capture itself.</summary>
			public float ExpectCloudCover;
			/// <summary>The most of the frame the clouds may cover. 0 means no ceiling.</summary>
			public float CloudCoverCeiling;
			/// <summary>
			/// Runs the weather driver instead of the stage's channels: the sky is whatever the
			/// drifting field says it is at a moment chosen so that the camera stands under a
			/// half-cloudy sky. This is the stage that sees formations, since with the channels
			/// driven straight there are none.
			/// </summary>
			public bool Driver;
			/// <summary>Applies this preset (by name) once the stage is set up, with no transition.</summary>
			public string Preset;
			/// <summary>
			/// How unevenly the cloud must be spread across the sky: the spread of the cover between
			/// the blocks of the frame. A noise field cut at one level is even everywhere and scores
			/// low; a sky with banks and gaps scores high. 0 only reports the figure.
			/// </summary>
			public float ExpectFormationContrast;
			/// <summary>
			/// How far the moon's disc must stand out from the sky around it. Above zero the moon has
			/// to be visible; <see cref="MoonDiscCeiling"/> says it has to be hidden. The pair exists
			/// because a body and a cloud can go wrong in both directions: the bodies are drawn in the
			/// transparent queue, after the cloud volume is composited, so a moon can float in front of
			/// an overcast — and the fix for that reads the cloud buffer's transmittance, which with no
			/// clouds drawn samples as zero and would take every body out of the sky instead.
			/// </summary>
			public float MoonDiscContrast;
			/// <summary>The most the moon's disc may stand out. 0 means no ceiling.</summary>
			public float MoonDiscCeiling;
			/// <summary>Puts the camera this far above the ground: the decks are a place you can fly to.</summary>
			public float CameraHeight;
			/// <summary>Aims the camera at this body once the moment is found.</summary>
			public string LookAt;
			/// <summary>
			/// The body this stage needs by name. When the system has no such body the stage is
			/// skipped, not failed: the comet, the companion and the belt are example content a
			/// project may not have (Solar System page → Add example sky objects).
			/// </summary>
			public string Requires;
			/// <summary>As <see cref="Requires"/>, for content that is not a body.</summary>
			public Func<bool> Available;
			/// <summary>When set, the time of day is searched for so the sun stands about here.</summary>
			public float? SunAltitudeWanted;
			/// <summary>Aims the camera at the antisolar point: where a rainbow stands.</summary>
			public bool LookAwayFromSun;
			/// <summary>
			/// When set, the probe searches the calendar for the first day whose <see cref="Time"/>
			/// satisfies this, and uses that day: the moments an eclipse or a bright comet needs
			/// cannot be written down in advance.
			/// </summary>
			public Func<CelestialState, bool> Want;
			public WeatherFrame Weather = WeatherFrame.Clear;
			public float Temperature = 0.2f;
		}

		private static readonly Stage[] Stages =
		{
			new Stage { Name = "home-noon", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-30f, 180f, 0f) },
			new Stage { Name = "home-dawn", Time = 0.26, CameraEuler = new Vector3(-6f, 90f, 0f) },
			new Stage { Name = "home-night", Time = 0.0, Sun = -1, ExpectStars = true, CameraEuler = new Vector3(-35f, 160f, 0f) },
			new Stage
			{
				// Polar night: the day is searched for, because the solstice depends on the orbit.
				Name = "home-pole-winter", Latitude = 82f, Time = 0.5, Sun = -1, ExpectStars = true,
				// Deep enough that the stars are out: a sun just under the horizon is still twilight.
				CameraEuler = new Vector3(-20f, 180f, 0f), Want = state => state.SunAltitude < -12f,
			},
			new Stage { Name = "home-aurora", Latitude = 72f, Time = 0.0, Day = 355f, Sun = -1, ExpectAurora = true, ExpectStars = true, Temperature = -0.7f, CameraEuler = new Vector3(-30f, 0f, 0f), Weather = Frame((WeatherChannel.Aurora, 1f)) },
			new Stage { Name = "home-storm-noon", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-20f, 180f, 0f), Weather = Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 1f), (WeatherChannel.Precipitation, 0.8f), (WeatherChannel.RainWeight, 1f), (WeatherChannel.LightningRate, 1f), (WeatherChannel.FogDensity, 0.3f)) },
			new Stage { Name = "moon-day", Body = "Moon 1", Time = 0.5, Sun = 1, ExpectBlackSky = true, ExpectStars = true, CameraEuler = new Vector3(-25f, 180f, 0f) },
			new Stage { Name = "moon-night", Body = "Moon 1", Time = 0.0, Sun = -1, ExpectBlackSky = true, ExpectStars = true, CameraEuler = new Vector3(-30f, 0f, 0f) },
			new Stage { Name = "home-moonlit", Time = 0.0, Sun = -1, ExpectMoon = true, ExpectStars = true, MoonDiscContrast = 0.004f },
			new Stage
			{
				// The same moon under a closed sky. It must not be there: the bodies are drawn after the
				// cloud volume is composited, so without something to put them back behind it a moon
				// hangs in front of an overcast. Paired with the contrast asked of home-moonlit above,
				// which fails the other way if the bodies are hidden when there is no cloud at all.
				Name = "moon-behind-cloud", Time = 0.0, Sun = -1, ExpectMoon = true, MoonDiscCeiling = 0.002f,
				Weather = Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 1f)),
			},
			new Stage { Name = "home-noon-performant", Time = 0.5, Sun = 1, Quality = 0, CameraEuler = new Vector3(-30f, 180f, 0f) },
			new Stage
			{
				// A cloudy noon: the volume must really be in front of the sky.
				// The sun is high, so the clouds' shadow on the ground has shape to check.
				Name = "clouds-noon", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-22f, 150f, 0f), ExpectCloudCover = 0.15f, ExpectCloudShadow = true,
				Weather = Frame((WeatherChannel.CloudCover, 0.7f), (WeatherChannel.CloudDensity, 0.6f)),
			},
			new Stage
			{
				// Looking up under a storm: the weather map's own square must not show as an edge.
				Name = "night-storm-overhead", Time = 0.0, Sun = -1, CameraEuler = new Vector3(-62f, 25f, 0f), Temperature = 0.2f,
				Weather = Frame((WeatherChannel.CloudCover, 0.75f), (WeatherChannel.CloudDensity, 0.8f), (WeatherChannel.Precipitation, 0.7f),
					(WeatherChannel.RainWeight, 1f), (WeatherChannel.LightningRate, 0.8f)),
			},
			new Stage
			{
				Name = "companion-textured", Time = 0.02, Sun = -1, ExpectTextured = true, LookAt = "Companion", Requires = "Companion",
				Want = state => Up(state, "Companion", 25f) && Lit(state, "Companion", 0.55f),
			},
			new Stage
			{
				Name = "comet", Time = 0.85, Sun = -1, ExpectComet = true, LookAt = "Wanderer", Requires = "Wanderer",
				Want = state => Up(state, "Wanderer", 8f) && Bright(state, "Wanderer", 0.25f),
			},
			new Stage
			{
				Name = "meteor-shower", Time = 0.0, Day = 199f, Sun = -1, ExpectMeteors = true, ExpectStars = true,
				Available = () => SolarSystemProfile.Active != null && SolarSystemProfile.Active.MeteorShowers.Count > 0,
				CameraEuler = new Vector3(-45f, 40f, 0f),
			},
			new Stage
			{
				Name = "asteroids-high", Time = 0.0, Day = 199f, Sun = -1, Quality = 2, ExpectAsteroids = true, ExpectStars = true,
				Available = () => SolarSystemProfile.Active != null && SolarSystemProfile.Active.AsteroidBelts.Count > 0,
				CameraEuler = new Vector3(-30f, 200f, 0f),
			},
			new Stage
			{
				// An eclipse: the rays rake out around the body covering the sun.
				Name = "solar-eclipse", Time = 0.5, Sun = 1, ExpectEclipse = true, ExpectGodRays = true, LookAt = "Sun", Requires = "Companion",
				Want = state => state.SolarEclipse > 0.5f,
			},
			new Stage
			{
				// A quarter-covered sky must look like a quarter-covered sky. This is the stage that
				// notices when a coverage setting quietly fills the whole frame.
				Name = "clouds-scattered", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-28f, 150f, 0f),
				ExpectCloudCover = 0.05f, CloudCoverCeiling = 0.42f,
				Weather = Frame((WeatherChannel.CloudCover, 0.25f), (WeatherChannel.CloudDensity, 0.5f)),
			},
			new Stage
			{
				// Half cloudy: the middle of the range, where a wrong curve hides best.
				Name = "clouds-broken", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-28f, 150f, 0f),
				ExpectCloudCover = 0.3f, CloudCoverCeiling = 0.85f,
				Weather = Frame((WeatherChannel.CloudCover, 0.6f), (WeatherChannel.CloudDensity, 0.6f)),
			},
			new Stage
			{
				// And an overcast must actually close over.
				Name = "clouds-overcast", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-28f, 150f, 0f),
				ExpectCloudCover = 0.7f,
				Weather = Frame((WeatherChannel.CloudCover, 1f), (WeatherChannel.CloudDensity, 0.9f)),
			},
			new Stage
			{
				// The Clear preset over the driver's own half-cloudy sky. A preset is an override:
				// the field steps back by the preset's strength, so Clear has to actually clear it.
				// It used to be a Clouds layer at zero, skipped as nothing, over a field left at
				// full weight — so it cleared nothing and the two systems fought.
				Name = "preset-clear-over-driver", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-28f, 150f, 0f),
				Driver = true, Preset = "Clear", CloudCoverCeiling = 0.02f,
			},
			new Stage
			{
				// The driver's own sky, on a half-cloudy day: the one stage that shows formations.
				// Every other cloud stage drives the channels straight, and a sky asked for one
				// cover has the same cover everywhere by construction — which is fine for checking
				// the cut and useless for checking whether the sky has banks and gaps in it.
				Name = "clouds-formations", Time = 0.5, Sun = 1, CameraEuler = new Vector3(-28f, 150f, 0f),
				Driver = true, ExpectCloudCover = 0.03f,
			},
			new Stage
			{
				// Cloud at dawn, which is where a sky that only knows one colour gives itself away:
				// the tops take the sun's red long before anything on the ground does.
				Name = "clouds-dawn", SunAltitudeWanted = 3f, CameraEuler = new Vector3(-8f, 90f, 0f), LookAt = "Sun",
				ExpectCloudCover = 0.05f,
				Weather = Frame((WeatherChannel.CloudCover, 0.55f), (WeatherChannel.CloudDensity, 0.6f)),
			},
			new Stage
			{
				// Above the low deck, looking down on it: the clouds are a place you can fly to, not a
				// ceiling painted on the sky. From up here their tops are below the horizon.
				Name = "clouds-from-above", Time = 0.5, Sun = 1, CameraEuler = new Vector3(22f, 150f, 0f), CameraHeight = 3400f,
				ExpectCloudCover = 0.05f,
				Weather = Frame((WeatherChannel.CloudCover, 0.6f), (WeatherChannel.CloudDensity, 0.6f)),
			},
			new Stage
			{
				// Broken cloud with the sun low: shafts, and the clouds' own shadow on the ground.
				Name = "god-rays", SunAltitudeWanted = 14f, Sun = 1, LookAt = "Sun", ExpectGodRays = true,
				CameraEuler = new Vector3(-4f, 90f, 0f),
				Weather = Frame((WeatherChannel.CloudCover, 0.7f), (WeatherChannel.CloudDensity, 0.65f)),
			},
			new Stage
			{
				Name = "rainbow", SunAltitudeWanted = 20f, ExpectRainbow = true, LookAwayFromSun = true,
				Weather = Frame((WeatherChannel.Precipitation, 0.35f), (WeatherChannel.RainWeight, 1f), (WeatherChannel.CloudCover, 0.35f)),
			},
		};

		// ── Moment searches ──

		private static SkyBodyState? Of(CelestialState state, string name)
		{
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				CelestialBody body = state.Bodies[i].Body;
				if (body != null && (body.ResolvedName == name || body.name == name))
				{
					return state.Bodies[i];
				}
			}
			return null;
		}

		private static bool Up(CelestialState state, string name, float degrees)
		{
			SkyBodyState? body = Of(state, name);
			return body.HasValue && body.Value.AltitudeDegrees > degrees;
		}

		private static bool Lit(CelestialState state, string name, float illumination)
		{
			SkyBodyState? body = Of(state, name);
			return body.HasValue && body.Value.Illumination > illumination && body.Value.Shadowed < 0.05f;
		}

		private static bool Bright(CelestialState state, string name, float brightness)
		{
			SkyBodyState? body = Of(state, name);
			return body.HasValue && body.Value.Brightness > brightness;
		}

		/// <summary>
		/// The first day (within four years) whose local time <paramref name="time"/> satisfies the
		/// stage's wish, or the stage's own day when nothing does.
		/// </summary>
		private static float FindDay(WorldSimController controller, Stage stage)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = controller.Body;
			if (system == null || body == null || stage.Want == null)
			{
				return stage.Day;
			}
			double day = CelestialMath.HomeSolarDayHours(system);
			double solarDay = CelestialMath.SolarDayHours(system, body);
			var state = new CelestialState();
			for (float d = 0f; d < 1460f; d += 0.2f)
			{
				double hours = d * day;
				double shift = stage.Time - CelestialMath.LocalTime01(system, body, hours, 0.0);
				hours += (shift - Math.Round(shift)) * solarDay;
				state.Compute(system, body, hours, stage.Latitude, 0.0, 0f);
				if (stage.Want(state))
				{
					return d;
				}
			}
			Debug.LogWarning($"[SkySimRender] {stage.Name}: no day in four years matched; using day {stage.Day}.");
			return stage.Day;
		}

		/// <summary>
		/// The first day (within a year of the stage's) on which the weather driver puts between
		/// two fifths and seven tenths cover over the camera at the stage's hour. The driver is a
		/// pure function of the clock, so this is a lookup and not a simulation.
		/// </summary>
		private static float FindCloudyDay(WorldSimController controller, Stage stage)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system == null)
			{
				return stage.Day;
			}
			double dayHours = CelestialMath.HomeSolarDayHours(system);
			double yearHours = Math.Max(1e-6, CelestialMath.YearHours(system));
			Vector3 at = controller.Camera.transform.position;
			for (float d = 0f; d < 365f; d += 0.5f)
			{
				float day = stage.Day + d;
				double hours = (day + stage.Time) * dayHours;
				float season = Mathf.Repeat((float)(hours / yearHours), 1f);
				float cover = WeatherDriver.CoverAt(WeatherDriver.WorldSeed, new Vector2(at.x, at.z), hours * 3600.0,
					stage.Latitude, season, (float)stage.Time);
				if (cover >= 0.4f && cover <= 0.7f)
				{
					return day;
				}
			}
			Debug.LogWarning($"[SkySimRender] {stage.Name}: no half-cloudy day within a year; using day {stage.Day}.");
			return stage.Day;
		}

		/// <summary>The time of day whose sun sits nearest the wanted altitude, on the stage's day.</summary>
		private static double FindTime(WorldSimController controller, Stage stage)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = controller.Body;
			if (system == null || body == null || !stage.SunAltitudeWanted.HasValue)
			{
				return stage.Time;
			}
			double solarDay = CelestialMath.SolarDayHours(system, body);
			double dayStart = controller.DayOfYear * CelestialMath.HomeSolarDayHours(system);
			var state = new CelestialState();
			double best = stage.Time, closest = double.MaxValue;
			for (int i = 0; i < 480; i++)
			{
				double hours = dayStart + solarDay * i / 480.0;
				state.Compute(system, body, hours, stage.Latitude, 0.0, 0f);
				double distance = Math.Abs(state.SunAltitude - stage.SunAltitudeWanted.Value);
				if (distance < closest)
				{
					closest = distance;
					best = state.LocalTime01;
				}
			}
			return best;
		}

		private static string outputDirectory;
		private static int stageIndex;
		private static double stageStarted;
		private static int failures;
		private static readonly List<string> report = new List<string>();
		private static PanelSettings uiSettings;
		private static RenderTexture uiTarget;

		private static WeatherFrame Frame(params (WeatherChannel channel, float value)[] values)
		{
			var frame = new WeatherFrame();
			foreach ((WeatherChannel channel, float value) in values)
			{
				frame[channel] = value;
			}
			return frame;
		}

		[DashboardTool(DashboardToolAttribute.UITests, "Render Sky Sim", Section = "Renders", Order = 22,
			Tooltip = "Plays the Sky Sim scene through ten skies (home, poles, aurora, storm, an airless moon) and writes a PNG of each to PanelRenders/.")]
		public static void Render()
		{
			outputDirectory = Environment.GetEnvironmentVariable("FISHMMO_SKY_RENDER_DIR");
			if (string.IsNullOrEmpty(outputDirectory))
			{
				outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "PanelRenders"));
			}
			Directory.CreateDirectory(outputDirectory);
			SessionState.SetString(StateKey, outputDirectory);

			if (!File.Exists(WorldSimSceneGenerator.ScenePath) || Environment.GetEnvironmentVariable("FISHMMO_SKY_REGENERATE") == "1")
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
				string summary = SessionState.GetString(ReportKey, string.Empty);
				failures = SessionState.GetInt(FailuresKey, 1);
				if (string.IsNullOrEmpty(summary))
				{
					summary = "No stage reported.";
					failures = Mathf.Max(failures, 1);
				}
				File.WriteAllText(Path.Combine(outputDirectory ?? ".", "SkySim-report.txt"), summary);
				Debug.Log($"[SkySimRender] {(failures == 0 ? "PASS" : $"FAIL ({failures})")}\n{summary}");
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

		/// <summary>
		/// True when <c>FISHMMO_SKY_RENDER_ONLY</c> names other stages: a comma-separated list, for
		/// working on one sky without waiting for the rest.
		/// </summary>
		private static bool Skipped(Stage stage)
		{
			string only = Environment.GetEnvironmentVariable("FISHMMO_SKY_RENDER_ONLY");
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

		private static void Pump()
		{
			try
			{
				WorldSimController controller = Controller();
				if (controller == null)
				{
					return;
				}
				if (stageIndex >= 0 && EditorApplication.timeSinceStartup - stageStarted < SettleSeconds)
				{
					return;
				}
				if (stageIndex >= 0 && !skipped)
				{
					Finish(controller, Stages[stageIndex]);
				}
				stageIndex++;
				while (stageIndex < Stages.Length && Skipped(Stages[stageIndex]))
				{
					stageIndex++;
				}
				while (stageIndex < Stages.Length && Missing(controller, Stages[stageIndex], out string why))
				{
					report.Add($"{Stages[stageIndex].Name}: SKIP — {why}");
					stageIndex++;
				}
				skipped = false;
				if (stageIndex >= Stages.Length)
				{
					EditorApplication.update -= Pump;
					MeasureCost(controller);
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

		private static void Start(WorldSimController controller, Stage stage)
		{
			QualitySettings.SetQualityLevel(Mathf.Clamp(stage.Quality, 0, QualitySettings.names.Length - 1), true);
			controller.Paused = true;
			controller.Body = BodyNamed(controller, stage.Body);
			controller.Latitude = stage.Latitude;
			controller.DayOfYear = stage.Want != null ? FindDay(controller, stage) : stage.Day;
			controller.TimeOfDay = stage.SunAltitudeWanted.HasValue ? FindTime(controller, stage) : stage.Time;
			// Every stage starts from an empty timeline. A layer left by the stage before used to
			// blend in unseen; now that a layer overrides the drifting field, a leftover Clear from
			// the preset stage silenced the driver stage that followed it and it drew no cloud.
			controller.ClearAll(0f);
			controller.SkyWeather = stage.Weather;
			if (stage.Driver)
			{
				// After the channels, which switch the driver off: the driver decides now, on a day
				// the field puts a half-cloudy sky over the camera.
				controller.DayOfYear = FindCloudyDay(controller, stage);
				controller.DriveWeatherDirectly = false;
			}
			if (!string.IsNullOrEmpty(stage.Preset))
			{
				WeatherPreset preset = controller.Presets.Find(p => p != null && p.ResolvedName == stage.Preset);
				if (preset == null)
				{
					Debug.LogWarning($"[SkySimRender] {stage.Name}: no preset named {stage.Preset} on the bed.");
				}
				else
				{
					controller.ApplyPreset(preset, 0f);
				}
			}
			controller.Temperature = stage.Temperature;
			controller.Camera.transform.rotation = Quaternion.Euler(stage.CameraEuler);
			if (stage.CameraHeight > 0f)
			{
				Vector3 seat = controller.Camera.transform.position;
				controller.Camera.transform.position = new Vector3(seat.x, stage.CameraHeight, seat.z);
			}
			if (stage.ExpectMoon)
			{
				controller.DayOfYear = MoonlitNight(controller, stage.Latitude);
				controller.TimeOfDay = 0.0;
			}
			stageStarted = EditorApplication.timeSinceStartup;
		}

		private static WorldBody BodyNamed(WorldSimController controller, string name)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (string.IsNullOrEmpty(name))
			{
				return system != null ? system.HomeWorld : null;
			}
			foreach (WorldBody body in controller.Bodies)
			{
				if (body.ResolvedName == name || body.name == name)
				{
					return body;
				}
			}
			return system != null ? system.HomeWorld : null;
		}

		/// <summary>The first day whose local midnight has a lit, unshadowed moon well up.</summary>
		private static float MoonlitNight(WorldSimController controller, float latitude)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			WorldBody body = controller.Body;
			if (system == null || body == null)
			{
				return 0f;
			}
			double day = CelestialMath.HomeSolarDayHours(system);
			double solarDay = CelestialMath.SolarDayHours(system, body);
			var state = new CelestialState();
			for (float d = 0f; d < 400f; d += 0.25f)
			{
				double hours = d * day;
				double shift = 0.0 - CelestialMath.LocalTime01(system, body, hours, 0.0);
				hours += (shift - Math.Round(shift)) * solarDay;
				state.Compute(system, body, hours, latitude, 0.0, 0f);
				if (state.Moon >= 0 && state.Bodies[state.Moon].AltitudeDegrees > 30f
					&& state.Bodies[state.Moon].Illumination > 0.6f && state.Bodies[state.Moon].Shadowed < 0.01f)
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

		private static void Finish(WorldSimController controller, Stage stage)
		{
			string path = Path.Combine(outputDirectory, $"SkySim-{stageIndex:00}-{stage.Name}.png");
			if (stage.ExpectMoon)
			{
				// Look at the moon we picked the night for.
				controller.Camera.transform.rotation = Quaternion.Euler(-Mathf.Min(moonAltitude, 60f) + 8f, moonAzimuth, 0f);
			}
			if (stage.LookAwayFromSun && controller.State != null)
			{
				// A rainbow is a circle about the antisolar point, 42° out: look there.
				Vector3 away = -controller.State.SunDirection;
				controller.LookAt(new Vector3(away.x, Mathf.Max(0.25f, away.y + 0.6f), away.z));
			}
			if (!string.IsNullOrEmpty(stage.LookAt) && controller.State != null)
			{
				SkyBodyState? aim = Of(controller.State, stage.LookAt);
				if (aim.HasValue)
				{
					controller.LookAt(aim.Value.Direction);
				}
			}
			Capture(controller, path);
			// Per stage, so a stage that does not measure it cannot report the last one's number.
			moonDisc = 0f;

			var problems = new List<string>();
			SkySystem sky = controller.Sky;
			CelestialState state = controller.State;
			if (sky == null || state == null)
			{
				failures++;
				report.Add($"{stage.Name}: FAIL no sky system or celestial state");
				return;
			}
			if (controller.Body == null) problems.Add("no body");
			if (sky.Profile != null && sky.Profile.SkyMaterial != null && (RenderSettings.skybox == null || RenderSettings.skybox.shader != sky.Profile.SkyMaterial.shader))
			{
				problems.Add($"skybox is {(RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "none")}");
			}
			if (RenderSettings.sun != sky.Sun && RenderSettings.sun != sky.Moon) problems.Add("RenderSettings.sun is neither light");
			float altitude = state.SunAltitude;
			if (stage.Sun > 0 && altitude <= 0f) problems.Add($"sun should be up, is at {altitude:0.0}°");
			if (stage.Sun < 0 && altitude >= 0f) problems.Add($"sun should be down, is at {altitude:0.0}°");
			float stars = sky.Current.StarVisibility;
			if (stage.ExpectStars && stars < 0.5f) problems.Add($"stars only {stars:0.00}");
			float aurora = Shader.GetGlobalVector("_FishAuroraParams").x;
			if (stage.ExpectAurora && aurora < 0.05f) problems.Add($"aurora only {aurora:0.00}");
			if (stage.ExpectMoon && (state.Moon < 0 || state.Bodies[state.Moon].AltitudeDegrees <= 0f)) problems.Add("no moon up");
			if (stage.ExpectBlackSky)
			{
				if (controller.Body != null && controller.Body.HasWeather) problems.Add($"{controller.Body.ResolvedName} has air, so its sky is not black");
				if (sky.Current.Zenith.maxColorComponent > 0.05f) problems.Add($"the airless zenith is not black ({sky.Current.Zenith})");
			}
			int textured = sky.Bodies != null ? sky.Bodies.Textured.Count : 0;
			int quads = sky.Bodies != null ? sky.Bodies.QuadCount : 0;
			if (stage.ExpectTextured && textured == 0) problems.Add("no body was drawn with its surface texture");
			if (stage.ExpectComet && !HasKind(state, SkyBodyKind.Comet, 0f)) problems.Add("no comet above the horizon");
			if (stage.ExpectMeteors && state.MeteorRate < 40f) problems.Add($"the shower should be falling, rate {state.MeteorRate:0}/h");
			if (stage.ExpectAsteroids && quads < 50) problems.Add($"the belt should add points, only {quads} quads");
			if (stage.ExpectEclipse && state.SolarEclipse < 0.5f) problems.Add($"eclipse only {state.SolarEclipse:0.00}");
			// The floor counts any cloud at all; the ceiling counts solid cloud. Cloud is translucent
			// at its real extinction, and a fair-weather sky is mostly the thin kind: judged on solid
			// cloud alone a quarter-covered sky measured two percent and looked exactly right.
			if (stage.ExpectCloudCover > 0f && lastCloudAny < stage.ExpectCloudCover)
			{
				problems.Add($"the clouds should cover the sky, only {lastCloudAny * 100f:0}% of it has any cloud in it");
			}
			if (stage.MoonDiscContrast > 0f || stage.MoonDiscCeiling > 0f)
			{
				moonDisc = MeasureMoonDisc(controller);
				if (stage.MoonDiscContrast > 0f && moonDisc < stage.MoonDiscContrast)
				{
					problems.Add($"the moon does not stand out from the sky ({moonDisc:0.0000} < {stage.MoonDiscContrast:0.0000}); the clouds may be hiding bodies that nothing is in front of");
				}
				if (stage.MoonDiscCeiling > 0f && moonDisc > stage.MoonDiscCeiling)
				{
					problems.Add($"the moon shows through the cloud ({moonDisc:0.0000} > {stage.MoonDiscCeiling:0.0000}); a body is being drawn over the volume instead of behind it");
				}
			}
			if (stage.CloudCoverCeiling > 0f && lastCloudCover > stage.CloudCoverCeiling)
			{
				problems.Add($"the clouds cover {lastCloudCover * 100f:0}% of the sky, which is more than this weather asked for");
			}
			if (stage.ExpectFormationContrast > 0f && lastCloudContrast < stage.ExpectFormationContrast)
			{
				problems.Add($"the cloud is spread evenly across the sky (block spread {lastCloudContrast:0.000} < {stage.ExpectFormationContrast:0.000}): noise, not formations");
			}
			if (stage.ExpectGodRays)
			{
				// The same frame twice, with the shafts and without: what they add is the difference.
				float with = MeasureSunGlow(controller);
				SkySystem.DrawGodRays = false;
				float without = MeasureSunGlow(controller);
				SkySystem.DrawGodRays = true;
				if (sky.GodRayIntensity <= 0.001f)
				{
					problems.Add("the sky asks for no light shafts here");
				}
				else if (with <= without * 1.02f)
				{
					problems.Add($"the light shafts add nothing around the sun ({without:0.0000} → {with:0.0000})");
				}
			}
			if (stage.ExpectCloudShadow)
			{
				// The shadow is a cookie on the light: a flat one is no shadow at all, however the
				// ground happens to be lit.
				Light lit = sky.Sun != null && sky.Sun.cookie != null ? sky.Sun : sky.Moon;
				Texture cookie = lit != null ? lit.cookie : null;
				if (cookie == null)
				{
					problems.Add("the light carries no cloud-shadow cookie");
				}
				else
				{
					float spread = MeasureCookieSpread(cookie);
					if (spread < 0.02f)
					{
						problems.Add($"the cloud-shadow cookie is flat ({spread:0.0000} spread), so the shadow has no shape");
					}
				}
			}
			float rainbow = Shader.GetGlobalVector("_FishRainbow").x;
			if (stage.ExpectRainbow && rainbow < 0.05f) problems.Add($"rainbow only {rainbow:0.00}");
			if (problems.Count > 0)
			{
				failures++;
			}
			string moon = state.Moon >= 0 ? $"moon {state.Bodies[state.Moon].AltitudeDegrees:0}° {(state.Bodies[state.Moon].Illumination * 100f):0}%" : "no moon";
			report.Add($"{stage.Name}: {(problems.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", problems))} — {controller.Body?.ResolvedName} at {controller.Latitude:0}°, shown aurora {controller.Presentation?.Shown[WeatherChannel.Aurora] ?? 0f:0.00}/rain {controller.Presentation?.Shown[WeatherChannel.Precipitation] ?? 0f:0.00}, day {Mathf.FloorToInt(controller.DayOfYear)}, {SceneTime.Format(state.LocalTime01)}, sun {altitude:0.0}°, stars {stars:0.00}, aurora {aurora:0.00}, eclipse {state.SolarEclipse:0.00}, rainbow {rainbow:0.00}, {moon}, {state.Bodies.Count} bodies, {quads} quads, {textured} textured, cloud cover {lastCloudCover * 100f:0}% solid / {lastCloudAny * 100f:0}% any (spread {lastCloudContrast:0.000}), moon disc {moonDisc:0.0000}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} → {Path.GetFileName(path)}");
		}

		/// <summary>
		/// Times the sky pass on each tier under the busiest sky the example system can show, and
		/// writes it for the Solar System page's Cost panel. The camera renders nothing but sky and
		/// the test bed's few landmarks, so the number is the sky's own cost on this renderer.
		/// </summary>
		private static void MeasureCost(WorldSimController controller)
		{
			const int WarmUp = 10;
			const int Frames = 60;
			SkySystem sky = controller.Sky;
			if (sky == null || controller.Camera == null)
			{
				return;
			}
			// A night sky with everything in it: stars, the moons, the comet and the belt.
			controller.Paused = true;
			controller.Body = SolarSystemProfile.Active != null ? SolarSystemProfile.Active.HomeWorld : controller.Body;
			controller.Latitude = 20f;
			controller.DayOfYear = 199f;
			controller.TimeOfDay = 0.0;
			controller.SkyWeather = WeatherFrame.Clear;
			controller.Camera.transform.rotation = Quaternion.Euler(-30f, 180f, 0f);

			var report = new SkyCostReport();
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
			RenderTexture previousTarget = controller.Camera.targetTexture;
			try
			{
				controller.Camera.targetTexture = target;
				for (int level = 0; level < Mathf.Min(3, QualitySettings.names.Length); level++)
				{
					QualitySettings.SetQualityLevel(level, true);
					// Let the tier's star map, reflection and body mesh rebuild before timing.
					for (int i = 0; i < WarmUp; i++)
					{
						controller.Camera.Render();
					}
					var watch = System.Diagnostics.Stopwatch.StartNew();
					for (int i = 0; i < Frames; i++)
					{
						controller.Camera.Render();
					}
					watch.Stop();
					report.Entries.Add(new SkyCostReport.Entry
					{
						Tier = QualitySettings.names[level],
						Milliseconds = (float)(watch.Elapsed.TotalMilliseconds / Frames),
						Quads = sky.Bodies != null ? sky.Bodies.QuadCount : 0,
						Textured = sky.Bodies != null ? sky.Bodies.Textured.Count : 0,
						StarCubemapSize = controller.Profile != null ? controller.Profile.TierFor(level).StarCubemapSize : 0,
						Frames = Frames,
					});
				}
			}
			finally
			{
				controller.Camera.targetTexture = previousTarget;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
			report.Save();
			var parts = new List<string>();
			foreach (SkyCostReport.Entry entry in report.Entries)
			{
				parts.Add($"{entry.Tier} {entry.Milliseconds:0.00} ms");
			}
			report0 = string.Join(" · ", parts);
			SkySimRender.report.Add($"cost: {report0} (on {SystemInfo.graphicsDeviceName})");
		}

		private static string report0 = string.Empty;
		private static bool skipped;

		/// <summary>Whether a stage's content is absent, so it is skipped rather than failed.</summary>
		private static bool Missing(WorldSimController controller, Stage stage, out string why)
		{
			why = null;
			if (!string.IsNullOrEmpty(stage.Requires) && NamedBody(controller, stage.Requires) == null)
			{
				why = $"the system has no '{stage.Requires}' (Solar System page → Add example sky objects)";
				return true;
			}
			if (stage.Available != null && !stage.Available())
			{
				why = "the system has no such sky objects (Solar System page → Add example sky objects)";
				return true;
			}
			return false;
		}

		/// <summary>A body anywhere in the system by name: comets and stars are not stood on, so the controller does not list them.</summary>
		private static CelestialBody NamedBody(WorldSimController controller, string name)
		{
			SolarSystemProfile system = SolarSystemProfile.Active;
			if (system == null)
			{
				return null;
			}
			foreach (CelestialBody body in system.Bodies)
			{
				if (body != null && (body.ResolvedName == name || body.name == name))
				{
					return body;
				}
			}
			return null;
		}

		private static bool HasKind(CelestialState state, SkyBodyKind kind, float minimumAltitude)
		{
			for (int i = 0; i < state.Bodies.Count; i++)
			{
				if (state.Bodies[i].Kind == kind && state.Bodies[i].AltitudeDegrees > minimumAltitude)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The share of the sky the clouds cover in a capture. A cloud is brighter than the sky
		/// behind it and much less blue, which separates the two without needing the buffer itself.
		/// </summary>
		/// <summary>
		/// How bright the sky is in a ring around the sun: where shafts live, but outside the sun's
		/// own disc and halo, so the measure is of the rays and not of the sun.
		/// </summary>
		/// <summary>
		/// How far the moon's disc stands out from the sky immediately around it, in luminance.
		/// </summary>
		/// <remarks>
		/// A moon on a clear night is far brighter than the sky beside it; a moon behind an overcast
		/// is not there at all. Comparing the disc against its own surroundings rather than against
		/// an absolute brightness is what makes the same measure work for both, at any time of night
		/// and whatever the cloud happens to be lit to.
		/// </remarks>
		private static float MeasureMoonDisc(WorldSimController controller)
		{
			Camera camera = controller.Camera;
			CelestialState state = controller.State;
			if (state == null || state.Moon < 0)
			{
				return 0f;
			}
			SkyBodyState moon = state.Bodies[state.Moon];
			// Viewport, not screen. WorldToScreenPoint answers in the pixels the camera is currently
			// sized to, which here is the game view, while the image below is Width x Height — so a
			// screen-space answer lands a fifth of the frame away from where the moon actually is,
			// and a disc this small is then measured against empty sky.
			Vector3 view = camera.WorldToViewportPoint(camera.transform.position + moon.Direction * 10000f);
			if (view.z <= 0f)
			{
				return 0f;
			}
			var screen = new Vector2(view.x * Width, view.y * Height);
			// The disc, and a ring of sky well clear of it to compare against.
			float radius = Mathf.Max(6f, moon.AngularRadius * Mathf.Rad2Deg / camera.fieldOfView * Height);
			float inner = radius * 2.5f, outer = radius * 6f;
			Texture2D image = Shoot(controller);
			try
			{
				Color[] pixels = image.GetPixels();
				float disc = 0f, sky = 0f;
				int discCount = 0, skyCount = 0;
				for (int y = 0; y < Height; y++)
				{
					for (int x = 0; x < Width; x++)
					{
						float distance = new Vector2(x - screen.x, y - screen.y).magnitude;
						if (distance > outer)
						{
							continue;
						}
						Color p = pixels[y * Width + x];
						float luminance = p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f;
						if (distance <= radius)
						{
							disc += luminance;
							discCount++;
						}
						else if (distance >= inner)
						{
							sky += luminance;
							skyCount++;
						}
					}
				}
				if (discCount == 0 || skyCount == 0)
				{
					return 0f;
				}
				return disc / discCount - sky / skyCount;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(image);
			}
		}

		private static float MeasureSunGlow(WorldSimController controller)
		{
			Camera camera = controller.Camera;
			CelestialState state = controller.State;
			if (state == null || state.Sun < 0)
			{
				return 0f;
			}
			Vector3 screen = camera.WorldToScreenPoint(camera.transform.position + state.SunDirection * 10000f);
			if (screen.z <= 0f)
			{
				return 0f;
			}
			Texture2D image = Shoot(controller);
			try
			{
				Color[] pixels = image.GetPixels();
				float sum = 0f;
				int counted = 0;
				float inner = Height * 0.12f, outer = Height * 0.4f;
				for (int y = 0; y < Height; y += 2)
				{
					for (int x = 0; x < Width; x += 2)
					{
						float distance = new Vector2(x - screen.x, y - screen.y).magnitude;
						if (distance < inner || distance > outer)
						{
							continue;
						}
						Color p = pixels[y * Width + x];
						sum += p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f;
						counted++;
					}
				}
				return counted > 0 ? sum / counted : 0f;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(image);
			}
		}

		/// <summary>How much shape a light cookie has: the spread of its values.</summary>
		private static float MeasureCookieSpread(Texture cookie)
		{
			var readable = new Texture2D(cookie.width, cookie.height, TextureFormat.RGBA32, false);
			RenderTexture previousActive = RenderTexture.active;
			var copy = RenderTexture.GetTemporary(cookie.width, cookie.height, 0, RenderTextureFormat.ARGB32);
			try
			{
				Graphics.Blit(cookie, copy);
				RenderTexture.active = copy;
				readable.ReadPixels(new Rect(0, 0, cookie.width, cookie.height), 0, 0);
				readable.Apply();
				Color[] pixels = readable.GetPixels();
				float sum = 0f, squares = 0f;
				foreach (Color p in pixels)
				{
					sum += p.r;
					squares += p.r * p.r;
				}
				float mean = sum / pixels.Length;
				return Mathf.Sqrt(Mathf.Max(0f, squares / pixels.Length - mean * mean));
			}
			finally
			{
				RenderTexture.active = previousActive;
				RenderTexture.ReleaseTemporary(copy);
				UnityEngine.Object.DestroyImmediate(readable);
			}
		}

		/// <summary>How mottled the ground is: the spread of brightness across the lower frame.</summary>
		private static float MeasureGroundVariance(WorldSimController controller)
		{
			Texture2D image = Shoot(controller);
			try
			{
				Color[] pixels = image.GetPixels();
				float sum = 0f, squares = 0f;
				int counted = 0;
				for (int y = 0; y < Height / 4; y += 2)
				{
					for (int x = Width / 3; x < Width; x += 2)
					{
						Color p = pixels[y * Width + x];
						float luminance = p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f;
						sum += luminance;
						squares += luminance * luminance;
						counted++;
					}
				}
				if (counted == 0)
				{
					return 0f;
				}
				float mean = sum / counted;
				return Mathf.Sqrt(Mathf.Max(0f, squares / counted - mean * mean));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(image);
			}
		}

		/// <summary>One frame from the sim camera, read back for measuring. The caller destroys it.</summary>
		private static Texture2D Shoot(WorldSimController controller)
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
				return image;
			}
			finally
			{
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
		}

		/// <summary>
		/// How much of the upper half of the frame the clouds cover: the share of pixels whose
		/// transmittance in the cloud buffer is under a half. Read from the buffer itself, because
		/// the sun's glare and a sunlit cloud top are the same white to a colour test.
		/// </summary>
		private static float MeasureCloudCover(Texture2D image)
		{
			Texture buffer = FishCloudsFeature.LastCloudBuffer;
			if (buffer == null)
			{
				return 0f;
			}
			var readable = new Texture2D(buffer.width, buffer.height, TextureFormat.RGBAHalf, false);
			RenderTexture previousActive = RenderTexture.active;
			var copy = RenderTexture.GetTemporary(buffer.width, buffer.height, 0, RenderTextureFormat.ARGBHalf);
			try
			{
				Graphics.Blit(buffer, copy);
				RenderTexture.active = copy;
				readable.ReadPixels(new Rect(0, 0, buffer.width, buffer.height), 0, 0);
				readable.Apply();
				Color[] pixels = readable.GetPixels();
				int counted = 0, cloudy = 0, any = 0;
				// The upper half, and not the left third where the panel sits — and the same region
				// cut into blocks, whose cover is compared block to block: a sky with banks and gaps
				// has some blocks solid and some clear, a noise field cut at one level has every
				// block about the same.
				const int Columns = 6, Rows = 3;
				int x0 = buffer.width / 3, y0 = buffer.height / 2;
				int blockWidth = Mathf.Max(1, (buffer.width - x0) / Columns);
				int blockHeight = Mathf.Max(1, (buffer.height - y0) / Rows);
				var blockCloudy = new int[Columns * Rows];
				var blockCounted = new int[Columns * Rows];
				for (int y = y0; y < buffer.height; y++)
				{
					int row = Mathf.Min(Rows - 1, (y - y0) / blockHeight);
					for (int x = x0; x < buffer.width; x++)
					{
						int column = Mathf.Min(Columns - 1, (x - x0) / blockWidth);
						int block = row * Columns + column;
						counted++;
						blockCounted[block]++;
						float through = pixels[y * buffer.width + x].a;
						if (through < 0.6f)
						{
							cloudy++;
							blockCloudy[block]++;
						}
						if (through < 0.95f)
						{
							any++;
						}
					}
				}
				float mean = 0f;
				for (int b = 0; b < blockCloudy.Length; b++)
				{
					mean += blockCounted[b] > 0 ? blockCloudy[b] / (float)blockCounted[b] : 0f;
				}
				mean /= blockCloudy.Length;
				float spread = 0f;
				for (int b = 0; b < blockCloudy.Length; b++)
				{
					float share = blockCounted[b] > 0 ? blockCloudy[b] / (float)blockCounted[b] : 0f;
					spread += (share - mean) * (share - mean);
				}
				lastCloudContrast = Mathf.Sqrt(spread / blockCloudy.Length);
				lastCloudAny = counted > 0 ? any / (float)counted : 0f;
				return counted > 0 ? cloudy / (float)counted : 0f;
			}
			finally
			{
				RenderTexture.active = previousActive;
				RenderTexture.ReleaseTemporary(copy);
				UnityEngine.Object.DestroyImmediate(readable);
			}
		}

		private static float lastCloudCover;
		/// <summary>The share of the last capture with any cloud in it at all, however thin.</summary>
		private static float lastCloudAny;
		/// <summary>The block-to-block spread of the cover in the last capture: banks and gaps, or an even scatter.</summary>
		private static float lastCloudContrast;
		/// <summary>How far the moon stood out from the sky last time a stage asked.</summary>
		private static float moonDisc;

		private static void Capture(WorldSimController controller, string path)
		{
			Camera camera = controller.Camera;
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
			RenderTexture previousTarget = camera.targetTexture;
			RenderTexture previousActive = RenderTexture.active;
			try
			{
				camera.targetTexture = target;
				// The clouds are steadied against the last frame, and their history is thrown away
				// whenever the buffer changes size — which is exactly what rendering into this
				// capture target does. A single render here would therefore photograph a frame the
				// game never shows: one un-averaged march, with every ray's jitter still in it.
				// Render a few times at this size first, so the history converges and the still is
				// the sky as it is actually seen.
				for (int warm = 0; warm < CaptureWarmFrames; warm++)
				{
					camera.Render();
				}
				RenderTexture.active = target;
				var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				image.Apply();
				lastCloudCover = MeasureCloudCover(image);
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
			if (document == null || document.panelSettings == null)
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
