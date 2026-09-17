using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>Turns the weather's fog and what is falling into the fog laid over the region's.</summary>
	public static class WeatherFogPresenter
	{
		/// <summary>How strongly the weather fogs the view, 0..1: the fog channel plus heavy precipitation.</summary>
		public static float Amount(in WeatherFrame frame)
		{
			float precipitationHaze = frame[WeatherChannel.Precipitation] *
				(0.25f * frame[WeatherChannel.RainWeight] + 0.6f * frame[WeatherChannel.SnowWeight] +
				 0.2f * frame[WeatherChannel.HailWeight] + 0.7f * frame[WeatherChannel.AshWeight] + 0.9f * frame[WeatherChannel.SandWeight]);
			return Mathf.Clamp01(1f - (1f - frame[WeatherChannel.FogDensity]) * (1f - precipitationHaze));
		}

		/// <summary>The fog colour: mist, pulled toward the colour of what is falling.</summary>
		public static Color ColorOf(in WeatherFrame frame, WeatherRenderProfile profile)
		{
			Color color = profile.MistColor;
			float p = frame[WeatherChannel.Precipitation];
			if (p <= 0.001f)
			{
				return color;
			}
			Color falling = profile.Rain.FogColor * frame[WeatherChannel.RainWeight]
				+ profile.Snow.FogColor * frame[WeatherChannel.SnowWeight]
				+ profile.Hail.FogColor * frame[WeatherChannel.HailWeight]
				+ profile.Ash.FogColor * frame[WeatherChannel.AshWeight]
				+ profile.Sand.FogColor * frame[WeatherChannel.SandWeight];
			falling.a = 1f;
			float share = Mathf.Clamp01(p * 1.5f) * (1f - 0.5f * frame[WeatherChannel.FogDensity]);
			return Color.Lerp(color, falling, share);
		}

		public static void Apply(in WeatherFrame frame, WeatherRenderProfile profile)
		{
			float amount = Amount(frame);
			FogComposer.SetWeather(amount, ColorOf(frame, profile), profile.MaxFogDensity, profile.MinFogEndDistance);
		}
	}
}
