using UnityEngine;
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
			Shader.SetGlobalVector(Cover, new Vector4(cover.Snow, cover.Wet, cover.Ash, cover.Sand));
			Shader.SetGlobalVector(Misc, new Vector4(frame[WeatherChannel.Aurora], temperature, shelter, time));
			// What is falling, kind by kind. A surface needs to know: rain rings a puddle, hail does
			// not, and snow does neither.
			Shader.SetGlobalVector(Mix, new Vector4(frame[WeatherChannel.RainWeight], frame[WeatherChannel.SnowWeight],
				frame[WeatherChannel.HailWeight], frame[WeatherChannel.AshWeight] + frame[WeatherChannel.SandWeight]));
			Shader.SetGlobalVector(Mix2, new Vector4(frame[WeatherChannel.AshWeight], frame[WeatherChannel.SandWeight], 0f, 0f));
		}

		/// <summary>Calm, dry, clear.</summary>
		public static void Clear()
		{
			Apply(WeatherFrame.Clear, default, 0f, 0f, 0f, 0f);
		}
	}
}
