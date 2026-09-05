using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>A climate reading at one point: what the world feels like there right now.</summary>
	public struct ClimateSample
	{
		/// <summary>-1 coldest … 1 hottest.</summary>
		public float Temperature;
		/// <summary>-1 driest … 1 wettest.</summary>
		public float Humidity;
		/// <summary>Elevation tier 0-8 of the height the sample was taken at.</summary>
		public int ElevationTier;
	}

	/// <summary>
	/// The climate model, as data. Every constant WorldEditor's biome generation hard-codes —
	/// the lapse rate, the humidity curve, the latitude gradient, the elevation-tier boundaries
	/// — is a field here, so a scene can be a frozen north or a tropical coast by asset, and a
	/// weather system can push the offsets at runtime through <see cref="WorldSceneSettings"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "New Climate", menuName = "FishMMO/Biomes/Climate Settings", order = 2)]
	public class ClimateSettings : CachedScriptableObject<ClimateSettings>, ICachedObject
	{
		/// <summary>Elevation-tier boundaries WorldEditor generates with: 0, 8 cut-offs, 1.</summary>
		public static readonly float[] DefaultElevationBoundaries = { 0f, 0.2f, 0.35f, 0.42f, 0.45f, 0.6f, 0.75f, 0.9f, 0.95f, 1f };

		[Header("Global climate")]
		[Tooltip("Shifts every temperature reading: -1 ice age … +1 hothouse.")]
		[Range(-1f, 1f)] public float GlobalTemperatureOffset = 0f;
		[Tooltip("Shifts every humidity reading: -1 drought … +1 monsoon.")]
		[Range(-1f, 1f)] public float GlobalHumidityOffset = 0f;
		[Tooltip("When on, temperature falls with distance from the equator (the map's centre line).")]
		public bool UsePlanetTemperature = false;

		[Header("Temperature model")]
		[Tooltip("How much temperature drops from sea floor to the highest peak.")]
		[Range(0f, 2f)] public float ElevationLapseRate = 0.8f;

		[Header("Humidity model")]
		[Tooltip("Extra humidity at the lowest elevations, fading to none at the highest.")]
		[Range(0f, 1f)] public float LowlandHumidityBonus = 0.3f;
		[Tooltip("Temperature above which air dries out.")]
		[Range(-1f, 1f)] public float HeatDryingThreshold = 0.5f;
		[Tooltip("Humidity lost per unit of temperature above the threshold.")]
		[Range(0f, 2f)] public float HeatDryingRate = 0.5f;
		[Tooltip("Temperature below which cold air holds less moisture.")]
		[Range(-1f, 1f)] public float ColdDryingThreshold = -0.3f;
		[Tooltip("Humidity lost per unit of temperature below the threshold.")]
		[Range(0f, 2f)] public float ColdDryingRate = 0.3f;

		[Header("Elevation tiers")]
		[Tooltip("Ten ascending values: 0, eight tier cut-offs, 1. Tier n spans [boundary n, boundary n+1).")]
		public float[] ElevationBoundaries = (float[])DefaultElevationBoundaries.Clone();
		[Tooltip("Normalised height of the water surface.")]
		[Range(0f, 1f)] public float WaterSurfaceHeight = 0.42f;

		[Header("Default climate variants")]
		[Tooltip("Variants applied to any biome that lists none of its own, matched in order.")]
		public List<BiomeClimateVariant> DefaultVariants = new List<BiomeClimateVariant>();

		/// <summary>Temperature and humidity at a normalised height and latitude (0 south edge … 1 north edge).</summary>
		public ClimateSample Evaluate(float height, float latitude01)
		{
			float temperature = GlobalTemperatureOffset;
			if (UsePlanetTemperature)
			{
				temperature -= Mathf.Abs(latitude01 - 0.5f) * 2f;
			}
			temperature -= height * ElevationLapseRate;
			temperature = Mathf.Clamp(temperature, -1f, 1f);

			float humidity = (1f - height) * LowlandHumidityBonus;
			if (temperature > HeatDryingThreshold)
			{
				humidity -= (temperature - HeatDryingThreshold) * HeatDryingRate;
			}
			else if (temperature < ColdDryingThreshold)
			{
				humidity += (temperature - ColdDryingThreshold) * ColdDryingRate;
			}
			humidity = Mathf.Clamp(humidity + GlobalHumidityOffset, -1f, 1f);

			return new ClimateSample
			{
				Temperature = temperature,
				Humidity = humidity,
				ElevationTier = TierForHeight(height),
			};
		}

		/// <summary>The elevation tier (0-8) a normalised height falls in.</summary>
		public int TierForHeight(float height) => TierForHeight(height, ElevationBoundaries);

		public static int TierForHeight(float height, float[] boundaries)
		{
			if (boundaries == null || boundaries.Length != 10)
			{
				boundaries = DefaultElevationBoundaries;
			}
			for (int tier = 0; tier < 8; tier++)
			{
				if (height >= boundaries[tier] && height < boundaries[tier + 1])
				{
					return tier;
				}
			}
			return height < boundaries[0] ? 0 : 8;
		}

		/// <summary>The variant a biome shows under this reading: the biome's own first, else the defaults, else null.</summary>
		public BiomeClimateVariant ResolveVariant(BiomeTemplate biome, ClimateSample sample)
		{
			if (biome == null)
			{
				return null;
			}
			if (biome.ClimateVariants != null && biome.ClimateVariants.Count > 0)
			{
				return biome.ResolveOwnVariant(sample.Temperature, sample.Humidity);
			}
			for (int i = 0; i < DefaultVariants.Count; i++)
			{
				BiomeClimateVariant variant = DefaultVariants[i];
				if (variant != null && variant.Matches(sample.Temperature, sample.Humidity))
				{
					return variant;
				}
			}
			return null;
		}

		/// <summary>A default variant by key, for requests that name one.</summary>
		public BiomeClimateVariant FindDefaultVariant(string variantKey)
		{
			string normalized = BiomeClimateVariant.Normalize(variantKey);
			for (int i = 0; i < DefaultVariants.Count; i++)
			{
				if (DefaultVariants[i] != null && DefaultVariants[i].Key == normalized)
				{
					return DefaultVariants[i];
				}
			}
			return null;
		}

		private void OnValidate()
		{
			if (ElevationBoundaries == null || ElevationBoundaries.Length != 10)
			{
				ElevationBoundaries = (float[])DefaultElevationBoundaries.Clone();
			}
			for (int i = 1; i < ElevationBoundaries.Length; i++)
			{
				if (ElevationBoundaries[i] < ElevationBoundaries[i - 1])
				{
					ElevationBoundaries[i] = ElevationBoundaries[i - 1];
				}
			}
		}
	}
}
