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
		public static readonly int WindHold = Shader.PropertyToID("_FishWindHold");

		/// <summary>
		/// How long the wind's pace is held for what travels at it (the gust bands in the grass, the
		/// gusts in the trees), s: long enough that a snapped pace is within a few percent of the wind's,
		/// short enough to follow a front coming through.
		/// </summary>
		public const double WindHoldSeconds = 60.0;
		private static WorldMotion.HeldValue heldWindSpeed;

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
		/// <param name="windToHold">
		/// The wind speed (channel units) to hold for a window that starts now: the weather's own target,
		/// which every player works out alike, rather than the eased one a player who just joined is still
		/// fading in from calm. NaN takes the frame's.
		/// </param>
		public static void Apply(in WeatherFrame frame, in WeatherCover cover, float temperature, float shelter, float time, float lightningFlash, WeatherSubstance substance = null,
			float windToHold = float.NaN)
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
			double into = WorldMotion.Window(WindHoldSeconds, out long window);
			float heldSpeed = heldWindSpeed.Hold(float.IsNaN(windToHold) ? frame[WeatherChannel.WindSpeed] : windToHold, window);
			Shader.SetGlobalVector(WindHold, new Vector4((float)into, (float)WindHoldSeconds, heldSpeed, 0f));
			// What is falling, kind by kind. A surface needs to know: rain rings a puddle, hail does
			// not, and snow does neither.
			Shader.SetGlobalVector(Mix, new Vector4(frame[WeatherChannel.RainWeight], frame[WeatherChannel.SnowWeight],
				frame[WeatherChannel.HailWeight], frame[WeatherChannel.AshWeight] + frame[WeatherChannel.SandWeight]));
			Shader.SetGlobalVector(Mix2, new Vector4(frame[WeatherChannel.AshWeight], frame[WeatherChannel.SandWeight], 0f, 0f));
		}

		/// <summary>
		/// How far into summer a place is, -1 deep winter .. 1 high summer, from the body's year phase
		/// and the latitude.
		/// </summary>
		/// <remarks>
		/// <see cref="FishMMO.Shared.Celestial.CelestialMath.Season01"/> is the northern season (it follows the sun's
		/// declination), so the south has it reversed; and the swing fades toward the equator, where
		/// the year brings wet and dry spells rather than summer and winter. Inverse of the weather
		/// driver's own curve, <c>sin(season · 2π − π/2)</c>, so foliage and weather agree on when
		/// summer is.
		/// </remarks>
		public static float LocalSummer(float season01, float latitudeDegrees)
		{
			float north = Mathf.Sin(season01 * Mathf.PI * 2f - Mathf.PI * 0.5f);
			float hemisphere = latitudeDegrees < 0f ? -1f : 1f;
			return north * hemisphere * SeasonStrength(latitudeDegrees);
		}

		/// <summary>The local year phase: the body's season, turned half a year round in the south.</summary>
		public static float LocalPhase(float season01, float latitudeDegrees)
		{
			return Mathf.Repeat(season01 + (latitudeDegrees < 0f ? 0.5f : 0f), 1f);
		}

		/// <summary>How strongly the year swings here, 0 on the equator .. 1 from the tropics outward.</summary>
		public static float SeasonStrength(float latitudeDegrees) => Mathf.Clamp01(Mathf.Abs(latitudeDegrees) / 23.5f);

		/// <summary>Publishes the season. Season changes over days, so once a climate tick is plenty.</summary>
		/// <remarks>
		/// x the local year phase (0 midwinter, 0.5 midsummer, 0.75 autumn), y local summer -1..1,
		/// z the humidity offset from the climate's mean, w the swing's strength — never quite 0 once
		/// set, so a shader can tell an equatorial scene (no seasons) from no season known.
		/// </remarks>
		public static void ApplySeason(float season01, float latitudeDegrees, float humidityOffset)
		{
			Shader.SetGlobalVector(Season, new Vector4(LocalPhase(season01, latitudeDegrees), LocalSummer(season01, latitudeDegrees),
				Mathf.Clamp(humidityOffset, -1f, 1f), Mathf.Max(0.001f, SeasonStrength(latitudeDegrees))));
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
