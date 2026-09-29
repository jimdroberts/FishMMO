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
using FishMMO.Shared.Biomes;
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
			/// <summary>
			/// What the stage adds to the air, on top of a still, ordinary air (half humidity, neutral
			/// pressure, gently unstable). There is no asking for weather by name: a stage that wants
			/// rain asks for air that rains.
			/// </summary>
			public AirOffsets Air;
			/// <summary>The scene's warmth on top of its climate, on the climate scale; added to the air as kelvin.</summary>
			public float Temperature;
			public double Time = 0.39;
			public float Day = 172f;
			public float Latitude = 35f;
			/// <summary>A storm of this kind stood over the camera for the stage.</summary>
			public StormKind? Storm;
			/// <summary>A storm of this kind drifting across the camera.</summary>
			public StormKind? Cell;
			/// <summary>
			/// What the ground under the camera has to give the air for this stage to be possible at
			/// all: sand lifts only off loose ground, ash comes only out of ground that emits it. The
			/// stage is skipped, not failed, where the bed's ground cannot — that is the physics
			/// being right.
			/// </summary>
			public bool NeedsLooseGround, NeedsEmittingGround;
			/// <summary>
			/// The biome (its asset's name) the bed stands on for this stage, instead of its own
			/// ground: the bed's is neither loose nor emitting, and a haboob or an eruption is the
			/// ground's as much as the air's.
			/// </summary>
			public string Ground;
			/// <summary>
			/// How far east of the camera a storm's centre stands, m. Where the weather is not at the
			/// centre — a hurricane's eye is the calm of it — the camera stands in the worst of it.
			/// </summary>
			public float StormOffset;
			/// <summary>
			/// The storm's motion, m/s (world x, z), in place of the steering wind's: to put the camera on a
			/// chosen flank. A storm moving south has the camera, west of it, on its right — a supercell's
			/// inflow side, rain-free, where storm spotters stand.
			/// </summary>
			public Vector2? StormMotion;
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
			/// <summary>
			/// The view must be obscured: a Koschmieder visibility under <see cref="ObscuredVisibilityMetres"/>.
			/// </summary>
			public bool ExpectFog;
			/// <summary>
			/// With <see cref="Expect"/> None: rain no heavier than drizzle counts as none. A fog deep and
			/// dense enough drizzles — its drops grow by collision until some fall out (under 1 mm/h,
			/// AMS glossary) — and a stage that forbade it failed a dense fog for doing what one does.
			/// </summary>
			public bool AllowDrizzle;
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
			/// An air to start in, before <see cref="Stage.Air"/>. The stage then checks that the sky
			/// really changed over to the second one: weather that is asked to hand over to different
			/// weather must not keep the first one falling.
			/// </summary>
			public AirOffsets? AirBefore;
			/// <summary>Runs the cover forward this many seconds before the capture.</summary>
			public float AdvanceCover;
			/// <summary>
			/// The cover map must differ from place to place: a storm passing over one field leaves
			/// that field white and the next one bare. Without this, a scene-wide figure would pass
			/// every other check while the ground told a lie.
			/// </summary>
			public bool ExpectCoverVariation;
		}

		/// <summary>An addition to the air, for a stage.</summary>
		private static AirOffsets Air(float humidity = 0f, float pressure = 0f, float instability = 0f, float wind = 0f, float temperature = 0f)
		{
			return new AirOffsets { Humidity = humidity, Pressure = pressure, Instability = instability, Wind = wind, Temperature = temperature };
		}

		// The recipes. Damp, unsettled air rains; a storm over it pours and thunders; the same air cold
		// enough snows — but cold air holds little water, so a cold stage adds more of it back; air
		// unstable enough under a storm grows hail. Dry settled air is clear. Calm damp air at dawn is
		// mist. Sand and ash are the ground's, and need ground that gives them.
		private static readonly AirOffsets Rainy = Air(humidity: 0.45f, pressure: -0.5f, instability: 0.3f);
		private static readonly AirOffsets Showery = Air(humidity: 0.12f);
		private static readonly AirOffsets ColdDamp = Air(humidity: 0.45f, pressure: -0.3f, instability: 0.2f);
		private static readonly AirOffsets Clear = Air(humidity: -0.4f, pressure: 0.8f);
		private static readonly AirOffsets Fair = Air(humidity: -0.1f, pressure: 0.2f);

		private static readonly Stage[] Stages =
		{
			new Stage { Name = "heavy-rain", Air = Rainy, Storm = StormKind.Thunderstorm, Temperature = 0.3f, Expect = PrecipitationKind.Rain, ExpectFog = true },
			new Stage { Name = "thunderstorm-performant", Air = Rainy, Storm = StormKind.Thunderstorm, Temperature = 0.3f, Quality = 0, Expect = PrecipitationKind.Rain },
			new Stage { Name = "rain-under-pavilion", Air = Rainy, Storm = StormKind.Thunderstorm, Temperature = 0.3f, CameraPosition = new Vector3(-6f, 1.6f, 4.2f), CameraEuler = new Vector3(-5f, 10f, 0f), Expect = PrecipitationKind.Rain },
			new Stage { Name = "same-storm-frozen", Air = ColdDamp, Storm = StormKind.Thunderstorm, Temperature = -1f, Expect = PrecipitationKind.Snow },
			// Heavy snow just below freezing (the stage temperature is an offset on the bed's own climate,
			// 33 K a unit): the only air where flakes clump into the big wet
			// aggregates (Hobbs, Chang & Locatelli 1974). It reads about −0.10 on the climate scale (−3 °C);
			// the frozen storm reads about −0.20 (−7 °C).
			new Stage { Name = "wet-snow", Air = ColdDamp, Storm = StormKind.Thunderstorm, Temperature = -0.9f, Expect = PrecipitationKind.Snow },
			new Stage { Name = "blizzard", Air = Air(humidity: 0.45f, pressure: -0.4f, instability: 0.2f, wind: 15f), Storm = StormKind.SquallLine, Temperature = -1f, Quality = 2, Expect = PrecipitationKind.Snow, ExpectFog = true },
			new Stage { Name = "hailstorm", Air = Air(humidity: 0.35f, pressure: -0.4f, instability: 0.45f), Storm = StormKind.Supercell, Temperature = 0.1f, Expect = PrecipitationKind.Hail },
			new Stage { Name = "sandstorm", Air = Air(humidity: -0.3f, instability: 0.2f), Storm = StormKind.Haboob, Temperature = 0.7f, Time = 0.35, Expect = PrecipitationKind.Sand, ExpectFog = true, NeedsLooseGround = true, Ground = "Desert" },
			new Stage { Name = "ashfall", Air = Air(), Storm = StormKind.Eruption, Temperature = 0.2f, Time = 0.32, Expect = PrecipitationKind.Ash, ExpectFog = true, NeedsEmittingGround = true, Ground = "Volcanic" },
			new Stage { Name = "mist", Air = Air(humidity: -0.03f, pressure: 0.9f, wind: -6f), Temperature = -0.3f, Time = 0.27, Expect = PrecipitationKind.None, ExpectFog = true },
			new Stage { Name = "clear", Air = Clear, Temperature = 0.2f, Expect = PrecipitationKind.None },
			new Stage
			{
				// Rain landing: faint glints off the ground, rings spreading in the puddles. Damp rather
				// than soaked: fully wet, every level square metre is a puddle and no glint is ever drawn.
				Name = "rain-splashes", Air = Rainy, Storm = StormKind.Thunderstorm, Temperature = 0.4f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Rain,
				CameraPosition = new Vector3(-2f, 1.6f, -6f), CameraEuler = new Vector3(24f, 20f, 0f),
				Cover = new WeatherCover { Wet = 0.6f },
			},
			new Stage
			{
				// The ground after rain: darker, shinier, with standing water in the hollows.
				Name = "wet-ground", Air = Showery, Temperature = 0.4f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Rain,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(28f, 20f, 0f),
				Cover = new WeatherCover { Wet = 1f }, ExpectSurface = true,
			},
			new Stage
			{
				// Snow lying on everything level, and on nothing under the pavilion roof.
				Name = "snow-cover", Air = ColdDamp, Temperature = -1f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Snow,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(24f, 20f, 0f),
				Cover = new WeatherCover { Snow = 1f }, ExpectSurface = true,
			},
			new Stage
			{
				// Ash: the same settling, a different colour, and it does not shine.
				Name = "ash-cover", Air = Air(), Storm = StormKind.Eruption, Temperature = 0.3f, Time = 0.5, Sun = 1, Expect = PrecipitationKind.Ash, ExpectFog = true,
				CameraPosition = new Vector3(-2f, 1.4f, -6f), CameraEuler = new Vector3(24f, 20f, 0f),
				Cover = new WeatherCover { Ash = 1f }, ExpectSurface = true, NeedsEmittingGround = true, Ground = "Volcanic",
			},
			new Stage
			{
				// Rain, then the same storm with the air chilled forty kelvin: what falls has to turn
				// to snow, not keep raining.
				Name = "rain-then-snow", AirBefore = Rainy, Air = ColdDamp + Air(temperature: -40f), Storm = StormKind.Thunderstorm, Temperature = 0.4f, Time = 0.5, Sun = 1,
				Settle = 9f, Expect = PrecipitationKind.Snow,
			},
			new Stage
			{
				// A squall line in freezing air stands over one side of the field — as large as the air
				// makes it, standing west with its leading edge a kilometre short of the camera, inside
				// the four-kilometre cover map. After a quarter of an
				// hour of it, the ground under it is white and the ground outside it is not.
				Name = "cover-under-cell", Air = Air(humidity: 0.02f), Storm = StormKind.SquallLine, StormOffset = -2400f, Temperature = -1f, Time = 0.5, Sun = 1,
				CameraPosition = new Vector3(-2f, 12f, -26f), CameraEuler = new Vector3(22f, 12f, 0f),
				AdvanceCover = 1200f, ExpectCoverVariation = true, Expect = PrecipitationKind.None,
			},
			new Stage
			{
				// The hill is real terrain on the forked terrain shader, and High is the tier where
				// deep snow lifts the ground it lies on.
				Name = "terrain-snow", Air = ColdDamp, Temperature = -1f, Time = 0.5, Sun = 1, Quality = 2, Expect = PrecipitationKind.Snow,
				CameraPosition = new Vector3(26f, 7f, 18f), CameraEuler = new Vector3(6f, 16f, 0f),
				Cover = new WeatherCover { Snow = 1f }, ExpectSurface = true,
			},
			new Stage { Name = "sky-dawn", Air = Clear, Temperature = 0.2f, Time = 0.255, CameraEuler = new Vector3(-6f, 90f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-noon", Air = Fair, Temperature = 0.3f, Time = 0.5, CameraEuler = new Vector3(-35f, 180f, 0f), Sun = 1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-dusk", Air = Clear, Temperature = 0.2f, Time = 0.745, CameraEuler = new Vector3(-6f, 270f, 0f), Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night", Air = Clear, Temperature = 0.1f, Time = 0.0, Moonlit = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-night-performant", Air = Clear, Temperature = 0.1f, Time = 0.0, Moonlit = true, Quality = 0, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lunar-eclipse", Air = Clear, Temperature = 0.1f, Time = 0.0, Moonlit = true, Eclipse = true, CameraEuler = new Vector3(-40f, 160f, 0f), Sun = -1, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-storm-cell", Air = Air(humidity: -0.1f, instability: 0.3f), Storm = StormKind.Thunderstorm, StormOffset = 5000f, Temperature = 0.3f, Time = 0.62, CameraEuler = new Vector3(-8f, 90f, 0f), Sun = 1, ExpectCurtain = true, Expect = PrecipitationKind.None },
			new Stage { Name = "sky-lightning-night", Settle = 30f, Air = Rainy, Storm = StormKind.Thunderstorm, Temperature = 0.3f, Time = 0.95, CameraEuler = new Vector3(-20f, 30f, 0f), Sun = -1, ExpectStrikes = true, Expect = PrecipitationKind.Rain },
			new Stage { Name = "sky-aurora", Air = Clear, Temperature = -0.6f, Time = 0.02, Day = 355f, Latitude = 72f, CameraEuler = new Vector3(-30f, 0f, 0f), Sun = -1, ExpectAurora = true, Expect = PrecipitationKind.None },

			// ── Every kind of storm, from the air it grows in ──
			// The ones seen from afar stand a set distance off at the size their air gives them, the
			// camera facing them. A standing front faces east and runs north and south, so a squall
			// line or a haboob stands to the west with its steep leading face toward the camera.
			// Radiation fog, three ways: calm air under a high on a cool night, a little damper each
			// time — a mist of a few kilometres, a fog of a few hundred metres, a dense fog of tens.
			// The fog channel goes as the humidity past its dew point, so a few hundredths of humidity
			// is the difference; the high's lid leaves no depth for drizzle.
			new Stage { Name = "fog", Air = Air(humidity: 0.15f, pressure: 0.9f, wind: -6f), Temperature = -0.3f, Time = 0.27, Expect = PrecipitationKind.None, ExpectFog = true },
			new Stage { Name = "dense-fog", Air = Air(humidity: 0.3f, pressure: 0.9f, wind: -6f), Temperature = -0.3f, Time = 0.27, Expect = PrecipitationKind.None, AllowDrizzle = true, ExpectFog = true },
			// A supercell's rotating core over the camera in a sultry afternoon: the tornado, and the
			// hail core its spinning updraught holds stones up in.
			new Stage { Name = "tornado", Air = Air(humidity: 0.35f, pressure: -0.5f, instability: 0.45f), Storm = StormKind.Supercell, Temperature = 0.45f, Time = 0.62, Sun = 1, CameraEuler = new Vector3(-12f, 20f, 0f), Expect = PrecipitationKind.Hail },
			// The same storm a kilometre off, coming: the curtain under it, the sky dark behind.
			new Stage { Name = "tornado-approach", Air = Air(humidity: 0.12f, pressure: -0.1f, instability: 0.45f, wind: 4f), Storm = StormKind.Supercell, StormOffset = 4000f, Temperature = 0.45f, Time = 0.62, Sun = 1, CameraEuler = new Vector3(-6f, 90f, 0f), Expect = PrecipitationKind.Rain },
			// In a hurricane's eyewall over warm air: rain driven flat by the wind.
			new Stage { Name = "hurricane-eyewall", Air = Air(pressure: -0.2f, instability: 0.2f, wind: 10f), Storm = StormKind.TropicalCyclone, StormOffset = 800f, Temperature = 0.6f, Time = 0.45, Sun = 1, Latitude = 18f, Expect = PrecipitationKind.Rain, ExpectFog = true },
			// And in its eye, the calm at the middle, looking straight up: the eye is a few hundred
			// metres across in a storm this size, and the wall stands round it.
			new Stage { Name = "hurricane-eye", Air = Air(humidity: -0.1f, pressure: -0.2f, instability: 0.2f, wind: 10f), Storm = StormKind.TropicalCyclone, Temperature = 0.6f, Time = 0.45, Sun = 1, Latitude = 18f, CameraEuler = new Vector3(-80f, 20f, 0f), Expect = PrecipitationKind.None },
			// A squall line on the horizon, marching in.
			new Stage { Name = "squall-line-approach", Air = Air(humidity: -0.1f, pressure: -0.1f, instability: 0.35f, wind: 8f), Storm = StormKind.SquallLine, StormOffset = -6000f, Temperature = 0.35f, Time = 0.58, Sun = 1, CameraEuler = new Vector3(-6f, 270f, 0f), ExpectCurtain = true, Expect = PrecipitationKind.None },
			// A dust devil over hot desert at midday: loose ground, dry air, the sun heating it.
			new Stage { Name = "dust-devil", Air = Air(humidity: -0.35f, pressure: 0.3f, instability: 0.3f), Storm = StormKind.DustDevil, Temperature = 0.8f, Time = 0.55, Sun = 1, Expect = PrecipitationKind.Sand, NeedsLooseGround = true, Ground = "Desert" },
			// A haboob's wall a kilometre off over the desert.
			new Stage { Name = "haboob-approach", Air = Air(humidity: -0.3f, instability: 0.2f, wind: -4.5f), Storm = StormKind.Haboob, StormOffset = -6000f, Temperature = 0.7f, Time = 0.6, Sun = 1, CameraEuler = new Vector3(-3f, 270f, 0f), Expect = PrecipitationKind.None, NeedsLooseGround = true, Ground = "Desert" },

			// ── The storms whole ──
			// A tornado wants a low cloud base — a low condensation level is one of the best
			// predictors there is — so the supercells stand in damp inflow air.
			// A cumulonimbus is seen whole from tens of kilometres: its flat dark base, its tower to
			// the tropopause, its anvil streaming downwind. Close up, a supercell's tornado hangs from
			// the wall cloud under its rotating updraught, the rain and hail falling ahead of it.
			new Stage { Name = "thunderstorm-distant", Air = Air(humidity: -0.1f, pressure: -0.1f, instability: 0.35f, wind: 4f), Storm = StormKind.Thunderstorm, StormOffset = 20000f, Temperature = 0.35f, Time = 0.6, Sun = 1, CameraEuler = new Vector3(-20f, 90f, 0f), ExpectCurtain = true, Expect = PrecipitationKind.None },
			new Stage { Name = "supercell-distant", Air = Air(humidity: 0.12f, pressure: -0.1f, instability: 0.45f, wind: 4f), Storm = StormKind.Supercell, StormOffset = 15000f, Temperature = 0.45f, Time = 0.62, Sun = 1, CameraEuler = new Vector3(-15f, 90f, 0f), Expect = PrecipitationKind.Rain },
			new Stage { Name = "tornado-close", Air = Air(humidity: 0.12f, pressure: -0.1f, instability: 0.45f, wind: 4f), Storm = StormKind.Supercell, StormOffset = 3000f, Temperature = 0.45f, Time = 0.62, Sun = 1, CameraEuler = new Vector3(-12f, 90f, 0f), Expect = PrecipitationKind.Rain },
			// The same tornado seen as a spotter sees one: from its inflow side, clear of the storm's own
			// rain and hail, with the afternoon sun behind the camera on the funnel. The air this sultry
			// still showers lightly everywhere (about 2 mm/h), so rain is expected here too.
			new Stage { Name = "tornado-inflow", Air = Air(humidity: 0.12f, pressure: -0.1f, instability: 0.45f, wind: 4f), Storm = StormKind.Supercell, StormOffset = 3000f, StormMotion = new Vector2(0f, -12f), Temperature = 0.45f, Time = 0.62, Sun = 1, CameraEuler = new Vector3(-12f, 90f, 0f), Expect = PrecipitationKind.Rain },
			new Stage { Name = "squall-line-distant", Air = Air(humidity: -0.1f, pressure: -0.1f, instability: 0.35f, wind: 8f), Storm = StormKind.SquallLine, StormOffset = -25000f, Temperature = 0.35f, Time = 0.58, Sun = 1, CameraEuler = new Vector3(-10f, 270f, 0f), ExpectCurtain = true, Expect = PrecipitationKind.None },
		};

		private static string outputDirectory;
		private static int stageIndex;
		private static double stageStarted;
		private static int failures;
		private static int strikesAtStart;
		private static int scheduledStrikes;
		private static readonly List<string> report = new List<string>();

		/// <summary>
		/// The visibility an obscured view must be under, m: ten kilometres, the line past which a
		/// weather report calls the view unrestricted (METAR's "9999"). A fog amount of about 0.09.
		/// </summary>
		private const float ObscuredVisibilityMetres = 10000f;

		/// <summary>
		/// Renders at the capture's size before a still, as the sky probe's (SkySimRender.CaptureWarmFrames):
		/// every pixel of the clouds is marched again once in sixteen frames. <c>FISHMMO_CAPTURE_WARM_FRAMES</c>
		/// overrides it (<see cref="WarmFrames"/>).
		/// </summary>
		private const int CaptureWarmFrames = 24;

		/// <summary>The most warm frames the override may ask for: 1024 is the cloud feature's whole frame cycle (64 × 16).</summary>
		private const int MaxWarmFrames = 1024;

		/// <summary>
		/// The warm frames a capture renders: <c>FISHMMO_CAPTURE_WARM_FRAMES</c> when it is a whole number
		/// (0 to <see cref="MaxWarmFrames"/>), else <see cref="CaptureWarmFrames"/>. Twenty-four frames is
		/// a cycle and a half of the clouds' sixteen places — about three samples' weight a pixel on
		/// Balanced — so a still settled over more says how much of what it shows is settling.
		/// </summary>
		private static int WarmFrames()
		{
			string asked = Environment.GetEnvironmentVariable("FISHMMO_CAPTURE_WARM_FRAMES");
			if (!string.IsNullOrEmpty(asked) && int.TryParse(asked.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int frames))
			{
				return Mathf.Clamp(frames, 0, MaxWarmFrames);
			}
			return CaptureWarmFrames;
		}

		/// <summary>What the last stage set the cloud options to, so the log says it once and not every stage.</summary>
		private static string cloudOptionsLogged;

		/// <summary>
		/// The cloud reconstruction options under trial (<see cref="CloudOptions"/>), from
		/// <c>FISHMMO_CLOUD_OPTIONS</c> — the probe's to read, never the game's. Unset is every option off.
		/// </summary>
		private static void ApplyCloudOptions()
		{
			FishCloudsFeature.Options = CloudOptions.Parse(Environment.GetEnvironmentVariable("FISHMMO_CLOUD_OPTIONS"), out string ignored);
			string said = FishCloudsFeature.Options + "|" + ignored;
			if (said != cloudOptionsLogged)
			{
				cloudOptionsLogged = said;
				Debug.Log($"[WeatherSimRender] cloud options: {FishCloudsFeature.Options}{(string.IsNullOrEmpty(ignored) ? "" : $" (not understood, ignored: {ignored})")}");
			}
		}

		/// <summary>
		/// The precipitation channel at drizzle's ceiling, 1 mm/h: the channel's full scale is about
		/// 50 mm/h and goes as its square (AirPhysics.PrecipitationExtinction), so √(1/50).
		/// </summary>
		private static readonly float DrizzleChannel = Mathf.Sqrt(1f / 50f);
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

		private static WorldSimController Controller() => UnityEngine.Object.FindAnyObjectByType<WorldSimController>();

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
				if (WeatherSimProfile.Enabled)
				{
					settle = Mathf.Max(settle, WeatherSimProfile.SettleSeconds);
				}
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
			// FISHMMO_WEATHER_VARIANT's quality= renders every stage at one level (the cloud options' E).
			QualitySettings.SetQualityLevel(Mathf.Clamp(WeatherSimProfile.VariantQuality(stage.Quality), 0, QualitySettings.names.Length - 1), true);
			ApplyCloudOptions();
			// These stages check that one air does what it should, so the drifting weather field is
			// held off: left running it would add its own weather on top, and every stage would then
			// depend on the moment it happened to be rendered at.
			controller.FieldDriven = false;
			controller.Camera.transform.position = stage.CameraPosition;
			controller.Camera.transform.rotation = Quaternion.Euler(stage.CameraEuler);
			if (stage.Moonlit)
			{
				MoonlitNight(controller, stage.Latitude, stage.Eclipse);
				controller.Camera.transform.rotation = Quaternion.Euler(-Mathf.Min(moonAltitude, 60f) + 8f, moonAzimuth, 0f);
			}
			controller.DayOfYear = stage.Moonlit ? MoonlitNight(controller, stage.Latitude, stage.Eclipse) : stage.Day;
			// JumpTo, not TimeOfDay: the weather runs on the world's clock, which TimeOfDay alone
			// leaves at the day's start.
			controller.JumpTo(stage.Time);
			controller.Latitude = stage.Latitude;
			controller.ClearAll(0f);
			controller.ResetCover();
			controller.CoverOverride = stage.Cover;
			StandOn(controller, stage.Ground);
			stageSkipped = null;
			if (stage.NeedsLooseGround || stage.NeedsEmittingGround)
			{
				WeatherSample ground = WeatherField.Sample(controller.Timeline, controller.Settings, controller.gameObject.scene, controller.Camera.transform.position, controller.Tick);
				if (stage.NeedsLooseGround && ground.Ground.Loose == null)
				{
					stageSkipped = "the ground under the bed has nothing loose on it, and a haboob lifts only what the ground lets go";
				}
				else if (stage.NeedsEmittingGround && ground.Ground.Emits == null)
				{
					stageSkipped = "the ground under the bed emits nothing, and ash comes only out of ground that does";
				}
			}
			float kelvin = stage.Temperature * (float)FishMMO.Shared.Biomes.ClimateModel.KelvinPerUnit;
			if (stage.AirBefore.HasValue)
			{
				AirOffsets before = stage.AirBefore.Value;
				before.Temperature += kelvin;
				controller.SetAir(before, 0f);
				if (stage.Storm.HasValue)
				{
					controller.SpawnCell(stage.Storm.Value, 0f, 0f, overhead: true);
				}
				// Let it settle into the first weather, then hand over the way a player would.
				controller.ForcePresent();
			}
			AirOffsets air = stage.Air;
			air.Temperature += kelvin;
			// A stage that hands over from another air does it the way a player would, over a couple
			// of seconds; the rest start where they mean to be.
			controller.SetAir(air, stage.AirBefore.HasValue ? 2f : 0f);
			scheduledStrikes = 0;
			strikesAtStart = SkySystem.Instance != null && SkySystem.Instance.Lightning != null ? SkySystem.Instance.Lightning.TotalStrikes : 0;
			if (stage.Storm.HasValue && !stage.AirBefore.HasValue)
			{
				controller.SpawnCell(stage.Storm.Value, 0f, 0f, overhead: true);
				if (stage.StormOffset != 0f)
				{
					// Stood off, and — all but a front, whose facing is its motion — moving as the
					// director moves a storm, with the steering wind. One stood still in a 15 m/s wind
					// had its rain streaming out kilometres downwind of it.
					WeatherTimeline timeline = controller.Timeline;
					WeatherSample steeringAir = WeatherField.Sample(timeline, controller.Settings, controller.gameObject.scene, controller.Camera.transform.position, controller.Tick);
					Vector2 steering = stage.StormMotion ?? steeringAir.OpenAir.Wind * 0.55f;
					bool front = StormPhysics.ShapeOf(stage.Storm.Value) == StormCellShape.Front;
					for (int i = 0; i < timeline.Cells.Count; i++)
					{
						StormCell cell = timeline.Cells[i];
						cell.OriginX += stage.StormOffset;
						if (!front)
						{
							cell.VelocityX = steering.x;
							cell.VelocityZ = steering.y;
						}
						timeline.UpsertCell(cell);
					}
				}
			}
			if (stage.Cell.HasValue)
			{
				controller.SpawnCell(stage.Cell.Value, 400f, 0.5f);
				// The cell comes from wherever the clock put it; turn to face it, keeping the pitch,
				// so the picture is of the storm and not of the sky behind it.
				WeatherTimeline timeline = controller.Timeline;
				if (timeline.Cells.Count > 0)
				{
					StormCell cell = timeline.Cells[timeline.Cells.Count - 1];
					Vector2 centre = cell.CentreAt((uint)controller.Tick, timeline.TickDelta);
					Vector3 from = controller.Camera.transform.position;
					float yaw = Mathf.Atan2(centre.x - from.x, centre.y - from.z) * Mathf.Rad2Deg;
					controller.Camera.transform.rotation = Quaternion.Euler(stage.CameraEuler.x, yaw, 0f);
				}
			}
			stageStarted = EditorApplication.timeSinceStartup;
			// The variant scales the profile for any run, profiled or not: a still under res= is how a
			// denser march (the cloud options' E) is seen.
			WeatherSimProfile.ApplyVariant(controller.Profile != null ? controller.Profile : WeatherRenderProfile.Active);
			if (WeatherSimProfile.Enabled)
			{
				WeatherSimProfile.Begin();
			}
		}

		/// <summary>Why the stage running now cannot happen in the bed, or null when it can.</summary>
		private static string stageSkipped;

		private static SceneBiomeMap groundMap;
		private static SceneBiomeMap bedGround;
		private static bool bedGroundKept;

		/// <summary>
		/// Stands the bed on one biome for a stage — a map of that biome alone, a hundred kilometres
		/// round the camera — or back on its own ground when <paramref name="biomeName"/> is empty.
		/// </summary>
		private static void StandOn(WorldSimController controller, string biomeName)
		{
			WorldSceneSettings settings = controller.Settings;
			if (settings == null)
			{
				return;
			}
			if (!bedGroundKept)
			{
				bedGround = settings.BiomeMap;
				bedGroundKept = true;
			}
			if (string.IsNullOrEmpty(biomeName))
			{
				settings.BiomeMap = bedGround;
				return;
			}
			var biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>($"Assets/Templates/Entity/Biomes/{biomeName}.asset");
			if (biome == null)
			{
				Debug.LogWarning($"[WeatherSimRender] no biome named {biomeName}; the stage stands on the bed's own ground.");
				settings.BiomeMap = bedGround;
				return;
			}
			int id = BiomeRegistry.IDOf(biome);
			if (!BiomeRegistry.TryGetByID(id, out _))
			{
				BiomeRegistry.Register(biome);
			}
			if (groundMap == null)
			{
				groundMap = ScriptableObject.CreateInstance<SceneBiomeMap>();
				groundMap.hideFlags = HideFlags.DontSave;
			}
			Vector3 at = controller.Camera.transform.position;
			groundMap.Set(1, 1, new[] { id }, new Vector2(at.x - 50000f, at.z - 50000f), new Vector2(100000f, 100000f));
			settings.BiomeMap = groundMap;
		}

		private static string Lin(Color c) => $"({c.r:0.000},{c.g:0.000},{c.b:0.000})";
		private static string Lin(Vector4 v) => $"({v.x:0.000},{v.y:0.000},{v.z:0.000},{v.w:0.000})";

		private static void Finish(WorldSimController controller, Stage stage)
		{
			if (stageSkipped != null)
			{
				report.Add($"{stage.Name}: SKIP — {stageSkipped}");
				return;
			}
			if (WeatherSimProfile.Enabled)
			{
				// Before the capture, which renders extra frames of its own at another size.
				WeatherSimProfile.Write(outputDirectory, stage.Name);
			}
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
			bool drizzle = stage.AllowDrizzle && kind == PrecipitationKind.Rain && shown[WeatherChannel.Precipitation] <= DrizzleChannel;
			bool kindOk = stage.Expect == PrecipitationKind.None
				? kind == PrecipitationKind.None || shown[WeatherChannel.Precipitation] < 0.05f || drizzle
				: kind == stage.Expect || (stage.Expect == PrecipitationKind.Snow && kind == PrecipitationKind.Sleet);
			if (!kindOk) problems.Add($"expected {stage.Expect}, shows {kind}");
			if (stage.Expect != PrecipitationKind.None && drawn == 0) problems.Add("nothing drawn");
			// The amount is the contrast lost over 250 m, so it IS a visibility: Koschmieder's
			// 3.912/β with β = −ln(1 − amount)/250. It was a flat 0.2 of the amount — a visibility of
			// 4.4 km — which failed a mist of 5.3 km for being a mist.
			float visibility = fog < 0.9999f ? 3.912f * 250f / Mathf.Max(1e-6f, -Mathf.Log(1f - fog)) : 0f;
			if (stage.ExpectFog && visibility >= ObscuredVisibilityMetres) problems.Add($"visibility {visibility / 1000f:0.0} km (fog {fog:0.00})");
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
			// What a storm is to the sky: its cell, what the weather map holds at its heart, and the
			// column band it is drawn in.
			string storms = string.Empty;
			if (controller.Timeline.Cells.Count > 0)
			{
				StormCell cell = controller.Timeline.Cells[0];
				Vector2 centre = cell.CentreAt((uint)controller.Tick, controller.Timeline.TickDelta);
				Color heart = WeatherMap.Sample(controller.Timeline, new StormFrames(controller.LastSample), new Vector3(centre.x, 0f, centre.y), (uint)controller.Tick);
				storms = $"; cell {cell.Kind} {cell.Shape} r {cell.RadiusMeters:0} m x {cell.ExtentMeters:0} m at {Vector2.Distance(centre, new Vector2(controller.Camera.transform.position.x, controller.Camera.transform.position.z)):0} m, map heart r {heart.r:0.00} g {heart.g:0.00} b {heart.b:0.00} a {heart.a:0.00}";
				SkySystem skyNow = SkySystem.Instance;
				if (skyNow != null)
				{
					for (int b = 0; b < skyNow.CloudBandCount && b < skyNow.CloudBands.Count; b++)
					{
						CloudBand band = skyNow.CloudBands[b];
						if (band.Column)
						{
							storms += $", column band {band.Bottom:0}-{band.Top:0} m";
						}
					}
					AirColumn open = skyNow.CloudViewerColumn;
					storms += $", open air base {open.Base:0} top {open.Top:0} towers {open.TowerCeiling:0} CAPE {open.Cape:0}";
				}
			}
			if (problems.Count > 0)
			{
				failures++;
			}
			// The local temperature on the climate scale, which types what falls (WeatherFrame.RetypeForTemperature:
			// all rain above 0.05, all snow below −0.15).
			WeatherSample here = WeatherField.Sample(controller.Timeline, controller.Settings, controller.gameObject.scene, controller.Camera.transform.position, controller.Tick);
			// The light everything in the frame is lit by, as the engine holds it: what a snowflake or a
			// wall gets (the trilight and the sun) against what the fog gets.
			Light sunLight = RenderSettings.sun;
			string light = $"ambient sky {Lin(RenderSettings.ambientSkyColor)} equator {Lin(RenderSettings.ambientEquatorColor)} ground {Lin(RenderSettings.ambientGroundColor)}, sun {(sunLight != null ? sunLight.intensity : 0f):0.000}×{(sunLight != null ? Lin(sunLight.color) : "-")} cookie {(sunLight != null && sunLight.cookie != null ? "on" : "off")}, fog light {Lin(Shader.GetGlobalVector("_FishFogLightColor"))} ambient {Lin(Shader.GetGlobalVector("_FishFogAmbient"))}";
			report.Add($"{stage.Name}: {(problems.Count == 0 ? "PASS" : "FAIL " + string.Join("; ", problems))} — {kind} {shown[WeatherChannel.Precipitation]:0.00}, temperature {here.Temperature:0.00}, {light}, fog {fog:0.00}, {drawn} drawn, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, map {(map ? "ready" : "missing")}, {sky}{storms} → {Path.GetFileName(path)}");
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
					controller.Camera != null ? controller.Camera.transform.position : Vector3.zero, window, controller.LastSample);
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
				// The clouds' history is thrown away when the buffer changes size, which rendering into
				// this target does: one render here photographed a single sparse frame of samples —
				// the rows of white dots in every storm sky — never what the game shows. Settle first,
				// as the sky probe does.
				int warmFrames = WarmFrames();
				for (int warm = 0; warm < warmFrames; warm++)
				{
					camera.Render();
				}
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
