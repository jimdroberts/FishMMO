using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>Fog settings as values.</summary>
	public struct FogState
	{
		public bool Enabled;
		public FogMode Mode;
		public Color Color;
		public float Density;
		public float StartDistance;
		public float EndDistance;

		public static FogState FromRenderSettings() => new FogState
		{
			Enabled = RenderSettings.fog,
			Mode = RenderSettings.fogMode,
			Color = RenderSettings.fogColor,
			Density = RenderSettings.fogDensity,
			StartDistance = RenderSettings.fogStartDistance,
			EndDistance = RenderSettings.fogEndDistance,
		};

		public void WriteToRenderSettings()
		{
			RenderSettings.fog = Enabled;
			RenderSettings.fogMode = Mode;
			RenderSettings.fogColor = Color;
			RenderSettings.fogDensity = Density;
			RenderSettings.fogStartDistance = StartDistance;
			RenderSettings.fogEndDistance = EndDistance;
		}
	}

	/// <summary>
	/// The one writer of the scene's fog: the region's fog is the base, the weather's is laid on
	/// top. Region fog changes go to <see cref="Base"/>; weather goes to <see cref="SetWeather"/>.
	/// </summary>
	public static class FogComposer
	{
		private static FogState baseFog;
		private static bool hasBase;
		private static bool regionColor;
		private static Color skyColor;
		private static bool hasSkyColor;
		private static Color weatherColor = Color.gray;
		private static float weatherAmount;
		private static float weatherDensity;
		private static float weatherEnd = 100f;

		/// <summary>The region fog, before weather. Taken from the scene on first use.</summary>
		public static FogState Base
		{
			get
			{
				if (!hasBase)
				{
					baseFog = FogState.FromRenderSettings();
					hasBase = true;
				}
				return baseFog;
			}
			set
			{
				baseFog = value;
				hasBase = true;
				Apply();
			}
		}

		/// <summary>Sets the region fog, marking its colour as the region's own (it then beats the sky's).</summary>
		public static void SetRegionFog(FogState fog)
		{
			regionColor = true;
			Base = fog;
		}

		/// <summary>
		/// The sky's horizon colour. Fog takes it unless a region set its own colour, so distant
		/// ground and sky meet without a seam.
		/// </summary>
		public static void SetSkyColor(Color color)
		{
			skyColor = color;
			hasSkyColor = true;
			Apply();
		}

		/// <summary>The weather's share (0..1), its colour, the exponential density it adds and the linear end distance it pulls to.</summary>
		public static void SetWeather(float amount, Color color, float addedDensity, float endDistance)
		{
			weatherAmount = Mathf.Clamp01(amount);
			weatherColor = color;
			weatherDensity = Mathf.Max(0f, addedDensity);
			weatherEnd = Mathf.Max(1f, endDistance);
			Apply();
		}

		/// <summary>Forgets the base (a new scene brings its own) and the weather.</summary>
		public static void Reset()
		{
			hasBase = false;
			weatherAmount = 0f;
			regionColor = false;
			hasSkyColor = false;
		}

		/// <summary>The combined fog for a base and a weather contribution.</summary>
		public static FogState Compose(FogState region, float amount, Color color, float addedDensity, float endDistance)
		{
			amount = Mathf.Clamp01(amount);
			if (amount <= 0.001f)
			{
				return region;
			}
			FogState result = region;
			if (!region.Enabled)
			{
				// No region fog: the weather's own, faded in from nothing.
				result.Enabled = true;
				result.Mode = FogMode.ExponentialSquared;
				result.Color = color;
				result.Density = addedDensity * amount;
				result.StartDistance = 0f;
				result.EndDistance = Mathf.Lerp(1000f, endDistance, amount);
				return result;
			}
			result.Color = Color.Lerp(region.Color, color, amount);
			result.Density = region.Density + addedDensity * amount;
			result.EndDistance = Mathf.Min(region.EndDistance, Mathf.Lerp(region.EndDistance, endDistance, amount));
			result.StartDistance = Mathf.Min(region.StartDistance, result.EndDistance * 0.5f);
			return result;
		}

		public static void Apply()
		{
			FogState region = Base;
			if (hasSkyColor && !regionColor)
			{
				region.Color = skyColor;
			}
			// The weather's fog is lit by the same sky as everything else. Its colour comes from the
			// render profile as one pale daytime grey, and used as it stood that grey was the fog's
			// colour at midnight too — so a night mist glowed, on the ground and (since the sky's
			// own fog takes this colour) in a white band right round the horizon. Dimmed to the sky's
			// own brightness, which is the horizon colour the sky hands over every frame.
			Color weather = weatherColor;
			if (hasSkyColor)
			{
				float skyLight = skyColor.r * 0.2126f + skyColor.g * 0.7152f + skyColor.b * 0.0722f;
				float dim = Mathf.Clamp01(skyLight / 0.7f);
				weather = new Color(weatherColor.r * dim, weatherColor.g * dim, weatherColor.b * dim, weatherColor.a);
			}
			Compose(region, weatherAmount, weather, weatherDensity, weatherEnd).WriteToRenderSettings();
		}
	}
}
