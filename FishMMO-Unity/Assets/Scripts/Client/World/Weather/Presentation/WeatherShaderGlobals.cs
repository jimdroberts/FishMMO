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

		/// <summary>The wind's ground-plane direction (world x, z) for a heading in degrees.</summary>
		public static Vector2 WindDirection(float headingDegrees)
		{
			float h = headingDegrees * Mathf.Deg2Rad;
			return new Vector2(Mathf.Sin(h), Mathf.Cos(h));
		}

		public static void Apply(in WeatherFrame frame, in WeatherCover cover, float temperature, float shelter, float time, float lightningFlash)
		{
			Shader.SetGlobalVector(Cloud, new Vector4(frame[WeatherChannel.CloudCover], frame[WeatherChannel.CloudDensity], frame[WeatherChannel.CloudBase], lightningFlash));
			Shader.SetGlobalVector(Precip, new Vector4(frame[WeatherChannel.Precipitation], frame[WeatherChannel.DropSize], frame[WeatherChannel.SnowWeight], frame.StormSeverity));
			Vector2 wind = WindDirection(frame[WeatherChannel.WindHeading]);
			Shader.SetGlobalVector(Wind, new Vector4(wind.x, wind.y, frame[WeatherChannel.WindSpeed], frame[WeatherChannel.WindGust]));
			Shader.SetGlobalVector(Fog, new Vector4(frame[WeatherChannel.FogDensity], frame[WeatherChannel.FogHeight], frame[WeatherChannel.VolumetricFog], 0f));
			Shader.SetGlobalVector(Cover, new Vector4(cover.Snow, cover.Wet, cover.Ash, cover.Sand));
			Shader.SetGlobalVector(Misc, new Vector4(frame[WeatherChannel.Aurora], temperature, shelter, time));
		}

		/// <summary>Calm, dry, clear.</summary>
		public static void Clear()
		{
			Apply(WeatherFrame.Clear, default, 0f, 0f, 0f, 0f);
		}
	}
}
