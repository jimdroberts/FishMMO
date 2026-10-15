using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The air's temperature at a place and a moment, in kelvin: its climate (the year's mean, the season, what is authored
	/// and added), the air mass the weather has brought, and the hour of the day. The one sum the weather sample and the
	/// sky's air map make, on the server and every client.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>In kelvin, unclamped (2026-10-15).</b> The climate scale stops at ±33 °C, and a weather sample used to be read on
	/// it and turned into kelvin through the world's mean — so every place past the scale's end read exactly its world's
	/// mean, and a frozen moon or a hot desert world had no warmer or colder place anywhere on it. The scale is now derived
	/// from this, never the other way round.
	/// </para>
	/// <para>Pure functions of the place, the clock and the body: the same on every machine.</para>
	/// </remarks>
	public static class SurfaceTemperature
	{
		/// <summary>The air mass anomaly's typical extreme: what <see cref="WeatherDriver.Synoptic.Temperature"/> reaches in a strong warm or cold spell.</summary>
		public const float AirMassExtreme = 0.55f;

		/// <summary>Degrees of latitude a strong air mass is carried from: about what a deep low's flow moves air in its life.</summary>
		public const float AirMassReachDegrees = 9f;

		/// <summary>The smallest swing an air mass brings, K: the tropics' day-to-day.</summary>
		public const float AirMassFloorKelvin = 1f;

		/// <summary>The largest, K: a polar outbreak over a winter continent.</summary>
		public const float AirMassCeilingKelvin = 15f;

		/// <summary>The day's range over dry open land under clear skies on an Earth-like world, K.</summary>
		public const float LandDayRangeKelvin = 15f;

		/// <summary>The day's range over the open sea, K.</summary>
		public const float SeaDayRangeKelvin = 1.5f;

		/// <summary>The fraction of the day at which the air is warmest: mid-afternoon, when the ground has given back the most.</summary>
		public const float WarmestHour01 = 0.6f;

		/// <summary>
		/// The air's temperature at a place, K: the climate's reading there, the air mass over it and the hour of its day.
		/// </summary>
		/// <param name="climate">The climate at the place, runtime offsets included (its <see cref="ClimateSample.KelvinOrScale"/>).</param>
		/// <param name="air">The open air over the place: its air mass and its humidity.</param>
		/// <param name="latitude">The place's latitude, degrees.</param>
		/// <param name="localTime01">The hour of the place's own day, 0 midnight … 0.5 noon.</param>
		/// <param name="daylightShare">How much of the day the sun is up there today, 0 … 1; -1 when the body has no day.</param>
		public static float AirKelvin(in ClimateSample climate, in WeatherDriver.Synoptic air, SolarSystemProfile system, WorldBody body,
			double latitude, double worldHours, float localTime01, float daylightShare)
		{
			float kelvin = climate.KelvinOrScale;
			PlanetClimateTable table = system != null && body != null ? PlanetClimateTable.For(system, body) : null;
			float gradient = table != null ? GradientAt(table, latitude, worldHours, climate.Continentality) : 0.6f;
			kelvin += AirMassKelvin(air.Temperature, gradient, climate.Continentality);
			AtmosphereKind atmosphere = CloudWorld.AtmosphereOf(body);
			double dayHours = system != null && body != null ? CelestialMath.SolarDayHours(system, body) : 24.0;
			kelvin += DayKelvin(atmosphere, climate.Continentality, air.Humidity, localTime01, daylightShare, dayHours);
			return kelvin;
		}

		/// <summary>How fast the temperature falls poleward at a latitude now, K per degree: the year's own gradient there.</summary>
		public static float GradientAt(PlanetClimateTable table, double latitude, double worldHours, float continentality)
		{
			float phase = table.PhaseAt(worldHours);
			float north = table.KelvinAt(latitude + 2.0, phase, continentality);
			float south = table.KelvinAt(latitude - 2.0, phase, continentality);
			return Mathf.Abs(north - south) / 4f;
		}

		/// <summary>
		/// The warmth an air mass brings, K: the weather field's anomaly (−<see cref="AirMassExtreme"/> … +) sized by how
		/// much warmer or colder the air it carries came from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// An air mass is air carried from somewhere else, so how far it can swing a place's temperature is how much the
		/// temperature changes over the distance it travels: about <see cref="AirMassReachDegrees"/> of latitude times the
		/// gradient there. That is ±1 K in the tropics, where the gradient is nothing, and ±9 K in a mid-latitude winter —
		/// where it was a flat ±6 K everywhere, so the equator had cold snaps and the steppe's winters had none. Inland it
		/// swings further: nothing tempers an air mass over a continent.
		/// </para>
		/// </remarks>
		public static float AirMassKelvin(float airMassAnomaly, float gradientKelvinPerDegree, float continentality)
		{
			float swing = Mathf.Clamp(gradientKelvinPerDegree * AirMassReachDegrees, AirMassFloorKelvin, AirMassCeilingKelvin)
				* Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(continentality));
			return Mathf.Clamp(airMassAnomaly / AirMassExtreme, -1.2f, 1.2f) * swing;
		}

		/// <summary>The day's range of temperature at a place, K: warmest afternoon less coldest night.</summary>
		/// <remarks>
		/// <para>
		/// Over land under a clear sky, about <see cref="LandDayRangeKelvin"/>; over the sea, whose surface barely warms in a
		/// day, about <see cref="SeaDayRangeKelvin"/>. Damp air and cloud hold the night's heat in and the day's out, so an
		/// overcast humid day ranges a third as far. Where the sun neither rises nor sets — polar day and polar night — there
		/// is no day to range over. A short day gives the ground less time to warm and cool: the range goes as the root of
		/// its length. Thin air holds little heat and lets the ground's swing through; a thick one smooths it nearly away;
		/// with none at all the ground goes from scorching to frozen, as the Moon's does.
		/// </para>
		/// </remarks>
		public static float DayRangeKelvin(AtmosphereKind atmosphere, float continentality, float humidity, float daylightShare, double dayHours)
		{
			if (daylightShare < 0f || double.IsInfinity(dayHours) || dayHours <= 1e-6)
			{
				return 0f;
			}
			float land = LandDayRangeKelvin * (1f - 0.5f * Mathf.Clamp01(humidity));
			float range = Mathf.Lerp(SeaDayRangeKelvin, land, Mathf.Clamp01(continentality));
			float cloud = Mathf.Clamp01((humidity - 0.5f) / 0.4f);
			range *= 1f - 0.6f * cloud;
			float f = Mathf.Clamp01(daylightShare);
			range *= 4f * f * (1f - f);
			range *= Mathf.Clamp(Mathf.Sqrt((float)(dayHours / 24.0)), 0.3f, 3f);
			switch (atmosphere)
			{
				case AtmosphereKind.None: return range * 10f;
				case AtmosphereKind.Thin: return range * 2.5f;
				case AtmosphereKind.Thick: return range * 0.1f;
				default: return range;
			}
		}

		/// <summary>The hour's share of the day's range, K: warmest at <see cref="WarmestHour01"/>, coldest half a day before.</summary>
		public static float DayKelvin(AtmosphereKind atmosphere, float continentality, float humidity, float localTime01, float daylightShare, double dayHours)
		{
			float range = DayRangeKelvin(atmosphere, continentality, humidity, daylightShare, dayHours);
			return 0.5f * range * Mathf.Cos(2f * Mathf.PI * (localTime01 - WarmestHour01));
		}
	}
}
