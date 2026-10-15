using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// Writes the weather into the shader globals every weather effect reads (FishWeather.hlsl).
	/// The only writer of those globals.
	/// </summary>
	public static class WeatherShaderGlobals
	{
		public static readonly int Cloud = Shader.PropertyToID("_FishWeatherCloud");
		public static readonly int Precip = Shader.PropertyToID("_FishWeatherPrecip");
		public static readonly int Wind = Shader.PropertyToID("_FishWeatherWind");
		public static readonly int Fog = Shader.PropertyToID("_FishWeatherFog");
		public static readonly int Cover = Shader.PropertyToID("_FishWeatherCover");
		public static readonly int Misc = Shader.PropertyToID("_FishWeatherMisc");
		public static readonly int Tier = Shader.PropertyToID("_FishWeatherTier");
		public static readonly int Mix = Shader.PropertyToID("_FishWeatherMix");
		/// <summary>What is falling, as a colour: rgb the substance's tint, a how harsh it is.</summary>
		public static readonly int Substance = Shader.PropertyToID("_FishWeatherSubstance");
		/// <summary>
		/// Ash and sand apart: x ash, y sand. <see cref="Mix"/> adds them together, which is enough
		/// for a surface that only needs "something dry is settling" but not for anything that has
		/// to tell a greasy ashfall from a dry scouring sandstorm.
		/// </summary>
		public static readonly int Mix2 = Shader.PropertyToID("_FishWeatherMix2");
		/// <summary>
		/// The season where the camera is: x the local year phase (<see cref="LocalPhase"/>), y local
		/// summer -1..1 (<see cref="LocalSummer"/>), z the climate's humidity offset from its mean
		/// (negative is drought), w how strongly the year swings here (0 when unknown). Foliage reads
		/// it to dry, brown, turn and drop its leaves.
		/// </summary>
		public static readonly int Season = Shader.PropertyToID("_FishSeason");
		/// <summary>The wind held for each window of the shared motion clock (FishWeather.hlsl's _FishWindHold).</summary>
		private static readonly int WindWay = Shader.PropertyToID("_FishWindWay");
		/// <summary>Where the scene air's carried distance wraps before a shader's float sees it, m: a hundred kilometres, to a centimetre.</summary>
		private const double WindWayWrapMetres = 100000.0;
		public static readonly int WindHold = Shader.PropertyToID("_FishWindHold");

		/// <summary>
		/// How long the wind's pace is held for what travels at it (the gust bands in the grass, the
		/// gusts in the trees), s: long enough that a snapped pace is within a few percent of the wind's,
		/// short enough to follow a front coming through.
		/// </summary>
		public const double WindHoldSeconds = 60.0;

		/// <summary>The wind's ground-plane direction (world x, z) for a heading in degrees.</summary>
		public static Vector2 WindDirection(float headingDegrees)
		{
			float h = headingDegrees * Mathf.Deg2Rad;
			return new Vector2(Mathf.Sin(h), Mathf.Cos(h));
		}

		/// <summary>
		/// What the quality tier allows a surface to do. Set apart from the weather itself, because
		/// it changes when the player changes quality, not when the weather turns.
		/// </summary>
		public static void ApplyTier(bool terrainSnowDisplacement)
		{
			Shader.SetGlobalVector(Tier, new Vector4(terrainSnowDisplacement ? 1f : 0f, 0f, 0f, 0f));
		}

		/// <param name="substance">
		/// What the precipitation is made of, or null for the kinds' own defaults. Lets a surface or
		/// an overlay show nitrogen snow and water snow as the different things they are, without
		/// anything downstream having to know what a substance is.
		/// </param>
		public static void Apply(in WeatherFrame frame, in WeatherCover cover, float temperature, float shelter, float time, float lightningFlash, WeatherSubstance substance = null)
		{
			Color tint = substance != null ? substance.Tint : Color.white;
			float harshness = substance != null ? substance.Harshness : 0f;
			Shader.SetGlobalVector(Substance, new Vector4(tint.r, tint.g, tint.b, harshness));
			Shader.SetGlobalVector(Cloud, new Vector4(frame[WeatherChannel.CloudCover], frame[WeatherChannel.CloudDensity], frame[WeatherChannel.CloudBase], lightningFlash));
			Shader.SetGlobalVector(Precip, new Vector4(frame[WeatherChannel.Precipitation], frame[WeatherChannel.DropSize], frame[WeatherChannel.SnowWeight], frame.StormSeverity));
			Vector2 wind = WindDirection(frame[WeatherChannel.WindHeading]);
			Shader.SetGlobalVector(Wind, new Vector4(wind.x, wind.y, frame[WeatherChannel.WindSpeed], frame[WeatherChannel.WindGust]));
			Shader.SetGlobalVector(Fog, new Vector4(frame[WeatherChannel.FogDensity], frame[WeatherChannel.FogHeight], frame[WeatherChannel.VolumetricFog], 0f));
			// And the fog as the layer the fog passes draw, in metres and per metre: how thick, how deep,
			// how lifted, and its structure carried along on the wind by this same clock.
			FogLayerView.Publish(frame, time);
			Shader.SetGlobalVector(Cover, new Vector4(cover.Snow, cover.Wet, cover.Ash, cover.Sand));
			Shader.SetGlobalVector(Misc, new Vector4(frame[WeatherChannel.Aurora], temperature, shelter, time));
			// The wind's pace for what travels at it, held still for each window of the shared clock: every
			// player switches window at the same moment, and the shaders snap the pace so a window holds
			// whole bands (FishWindTravel), so nothing scrubs as the wind eases and the bands agree.
			// The pace is the scene's wind at the window's start (AirDrift: the wind the weather reports, with what the
			// scene's author and its runtime air add), the same number on every machine wherever its player stands
			// (2026-10-15: it was the wind at each player's own camera, sampled on that client's own cadence, so two
			// players saw a meadow's gust bands in different places, and one who joined mid-window another again).
			double into = WorldMotion.Window(WindHoldSeconds, out long window);
			double windowStart = window * WindHoldSeconds;
			AirDrift.Air sceneAir = SkySystem.SceneAir(windowStart);
			float heldSpeed = Mathf.Clamp01(AirDrift.SpeedAt(sceneAir, windowStart) / AirDrift.SteeringGain / 30f);
			Shader.SetGlobalVector(WindHold, new Vector4((float)into, (float)WindHoldSeconds, heldSpeed, 0f));
			// And the scene's air for what is laid out across the world on the wind (FishWindWay): its way, and how far
			// its ten-metre wind has carried it by now (FishHeatShimmer), from the shared clock.
			double now = WorldMotion.Seconds;
			AirDrift.Air nowAir = SkySystem.SceneAir(now);
			Vector2 way = WeatherDriver.PrevailingWind(nowAir.Latitude, nowAir.Belts);
			double carried = AirDrift.GroundCarried(nowAir, now);
			Shader.SetGlobalVector(WindWay, new Vector4(way.x, way.y, (float)WorldMotion.Repeat(carried, WindWayWrapMetres), (float)WindWayWrapMetres));
			// What is falling, kind by kind. A surface needs to know: rain rings a puddle, hail does
			// not, and snow does neither.
			Shader.SetGlobalVector(Mix, new Vector4(frame[WeatherChannel.RainWeight], frame[WeatherChannel.SnowWeight],
				frame[WeatherChannel.HailWeight], frame[WeatherChannel.AshWeight] + frame[WeatherChannel.SandWeight]));
			Shader.SetGlobalVector(Mix2, new Vector4(frame[WeatherChannel.AshWeight], frame[WeatherChannel.SandWeight], 0f, 0f));
		}

		/// <summary>Publishes the season. Season changes over days, so once a climate tick is plenty.</summary>
		/// <remarks>
		/// x how far through its thermal year the place is (0 its coldest point, 0.5 its warmest, 0.75 autumn), y how
		/// warm it is for growing (−1 dormant … 1 growing), z the humidity offset from the climate's mean, w how hard its
		/// winter is (0 none … 1 one that strips the trees) — never quite 0 once set, so a shader can tell a place with no
		/// winter from no season known. All from the climate (<see cref="FishMMO.Shared.Weather.SceneClimate.Foliage"/>), not the calendar.
		/// </remarks>
		public static void ApplySeason(float yearPhase01, float growing, float humidityOffset, float winter)
		{
			Shader.SetGlobalVector(Season, new Vector4(Mathf.Repeat(yearPhase01, 1f), Mathf.Clamp(growing, -1f, 1f),
				Mathf.Clamp(humidityOffset, -1f, 1f), Mathf.Max(0.001f, Mathf.Clamp01(winter))));
		}

		/// <summary>No season known: foliage shows its healthy colours.</summary>
		public static void ClearSeason()
		{
			Shader.SetGlobalVector(Season, Vector4.zero);
		}

		/// <summary>Calm, dry, clear.</summary>
		public static void Clear()
		{
			Apply(WeatherFrame.Clear, default, 0f, 0f, 0f, 0f);
			ClearSeason();
		}
	}
}
