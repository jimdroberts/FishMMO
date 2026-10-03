using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

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
		/// <remarks>
		/// On a body these are normalised heights, and each stands for the altitude in
		/// <see cref="TierEdgeMetres"/> at the same index (<see cref="PlanetClimateField.HeightOfAltitude"/>):
		/// tier 0 abyssal and hadal floor, 1 slope and rise, 2 continental shelf, 3 shore, 4 lowland and
		/// plateau, 5 highland, 6 mountain, 7 alpine, 8 nival. A biome's height band is its tier's
		/// span of these numbers, so they stay the scale every biome is authored on.
		/// </remarks>
		public static readonly float[] DefaultElevationBoundaries = { 0f, 0.2f, 0.35f, 0.42f, 0.45f, 0.6f, 0.75f, 0.9f, 0.95f, 1f };

		/// <summary>
		/// The altitude each of the ten tier boundaries stands at on a body, in metres on Earth's scale
		/// (a body's own metres divided by its relief over Earth's): the deepest trench, then the edges
		/// of the abyssal zone, the shelf, the water line, the shore, the lowland, the highland, the
		/// mountains and the alpine band, then the summit.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Landform bands, not life zones.</b> Treeline and snowline move with latitude by thousands
		/// of metres — the treeline stands at 4 km in the Andes and at sea level in the Arctic — and the
		/// climate field already does that by cooling with altitude at the body's own lapse rate. So
		/// the tiers say only where the ground stands, and every edge is a landform edge:
		/// </para>
		/// <list type="bullet">
		/// <item><b>−4000 / −200:</b> the abyssal zone and the shelf break (ETOPO1; see
		/// <see cref="PlanetClimateField.AbyssalMetres"/>, <see cref="PlanetClimateField.ShelfEdgeMetres"/>).</item>
		/// <item><b>30, the shore:</b> beaches, dune ridges, salt marsh, mangrove and estuary flats.
		/// The low-elevation coastal zone is usually drawn at 10 m (McGranahan, Balk and Anderson 2007,
		/// 2.2% of Earth's land); storm ridges and foredunes stand to a few tens of metres. The
		/// generated land rises from the sea more steeply than Earth's coastal plains (0.6% of it is
		/// under 10 m), so 30 m is where the same 2% of land falls in the band.</item>
		/// <item><b>1500, lowland and plateau:</b> below about 1.5 km, ground counts as mountain only
		/// for its local relief (the UNEP-WCMC mountain classes 5 and 6 need steep slopes or 300 m of
		/// local relief; Kapos et al. 2000), and a globe sample is tens of kilometres across with
		/// no relief to judge. So everything below is plain, plateau or low hill, whose cover the
		/// climate decides: the African and Brazilian plateaus are savanna at 1–1.5 km.</item>
		/// <item><b>2500, highland:</b> classes 4 and 5, 1.5–2.5 km, mountainous with any slope at
		/// all: high plateaus, basins and the foothills.</item>
		/// <item><b>3500, mountain; 4500, alpine; summit, nival:</b> classes 3, 2 and 1, which are
		/// mountain by altitude alone.</item>
		/// </list>
		/// <para>
		/// Measured on an Earth-like body (200,000 points): shore 2.0% of land, lowland 77%,
		/// highland 15%, mountain 4.2%, alpine 1.0%, nival 1.0%. Under the old fractions of the summit
		/// the same body was 33% coast, 62% tier 4 and 4% highland.
		/// </para>
		/// <para>
		/// The two ends are the deepest knot of <see cref="PlanetSurface.OceanKnotDepthMetres"/> and
		/// Earth's summit (<see cref="PlanetSurface.EarthSummitFraction"/>); on a body the summit is its
		/// own highest ground.
		/// </para>
		/// </remarks>
		public static readonly float[] TierEdgeMetres = { -10911f, -4000f, -200f, 0f, 30f, 1500f, 2500f, 3500f, 4500f, 8848f };

		private static ClimateSettings derived;

		/// <summary>
		/// The climate every scene gets when none is authored: this asset's own field defaults, held
		/// in memory and never written to disk.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A world's climate is derived, not authored.</b> <c>CelestialMath.ClimateOffsets</c>
		/// already works out what a body's distance from its star, its atmosphere, its water and its
		/// latitude do to every reading on it — all of it relative to the home world. So this asset
		/// is not "a climate": it is the <em>reference</em> model those offsets move, and there is
		/// exactly one of it however many planets a system has. Writing one per body would apply the
		/// orbit twice and make a cold world colder than anything could live on.
		/// </para>
		/// <para>
		/// Which leaves nothing for a per-world asset to say, so the defaults answer for every scene
		/// and an authored asset becomes what it should always have been: an override for a scene
		/// that wants to differ. The numbers here are the calibrated ones — sea level 0.8, the lapse
		/// rate 0.8, a 20° span across the map — measured so the whole biome range is reachable
		/// between the equator and the poles.
		/// </para>
		/// <para>
		/// Not registered in the cached-object table: it has no asset name, and an unnamed entry
		/// would take the ID 0 slot that a template referenced only by a prefab already collides on.
		/// </para>
		/// </remarks>
		public static ClimateSettings Default
		{
			get
			{
				if (derived == null)
				{
					derived = CreateInstance<ClimateSettings>();
					derived.name = "Derived Climate";
					derived.hideFlags = HideFlags.HideAndDontSave;
				}
				return derived;
			}
		}

		[Header("Global climate")]
		[Tooltip("Shifts every temperature reading: -1 ice age … +1 hothouse.")]
		[Range(-1f, 1f)] public float GlobalTemperatureOffset = 0f;
		[Tooltip("Shifts every humidity reading: -1 drought … +1 monsoon.")]
		[Range(-1f, 1f)] public float GlobalHumidityOffset = 0f;
		[Tooltip("SUPERSEDED by Map latitude span, which does this properly from the body's tilt and orbit. Left so existing assets keep loading; it adds a second, cruder gradient on top when on.")]
		public bool UsePlanetTemperature = false;

		[Tooltip("How many degrees of latitude the scene's biome map spans, north edge to south. The scene's own latitude sits at the middle, so one map can run from forest to tundra. 0 gives the whole scene one climate.")]
		[Range(0f, 120f)] public float MapLatitudeSpanDegrees = 20f;

		[Header("Temperature model")]
		[Tooltip("Temperature at the water line ON THE SUB-SOLAR EQUATOR (-1 frozen … +1 scorching). Latitude cools it from there and the lapse rate cools it with height, so this is the hottest the sea-level ground ever gets, not its average. 0.8 puts true tropics at the equator and ice caps at the poles.")]
		[Range(-1f, 1f)] public float SeaLevelTemperature = 0.8f;
		/// <remarks>
		/// 1.5, not 0.8. The lapse rate has to carry the whole drop from the water line to the
		/// highest peak on its own, and at 0.8 it removed only 0.464 across a scene's entire height
		/// range — so with the sea level anchored at 0.8, the highest peak in any scene read +0.336
		/// and **nothing froze from elevation at all**. Snow could only come from latitude or from
		/// a weather cell, which is not how a mountain looks. At 1.5 the peak reads −0.07 and the
		/// water line is untouched at 0.80.
		/// </remarks>
		[Tooltip("How much temperature drops from sea floor to the highest peak. 1.5 puts the highest ground just below freezing while the water line stays mild.")]
		[Range(0f, 2f)] public float ElevationLapseRate = 1.5f;

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
			// Anchored at the water line: before this, sea level read -0.34 and almost every
			// scene above it was below freezing, so most weather fell as snow.
			temperature += SeaLevelTemperature - (height - WaterSurfaceHeight) * ElevationLapseRate;
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
