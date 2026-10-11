using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A biome. The asset is the identity — its cached-object ID is what references, biome maps
	/// and race affinities carry — and everything the biome means is data on it: the elevation
	/// tier and climate envelope it is chosen for during generation, the colour that identifies
	/// it on a biome map, the terrain textures and spawn rules that paint it, the climate variants
	/// it can be experienced under, and the naming data dungeons and points of interest are named
	/// from. Registers with <see cref="BiomeRegistry"/> when loaded.
	///
	/// <para>Nothing here restricts where a biome may be used. A cave biome and a grassland are
	/// the same kind of asset; a biome that should never be picked by climate simply has a
	/// <see cref="SelectionWeight"/> of zero and is placed by hand.</para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Biome", menuName = "FishMMO/Biomes/Biome", order = 1)]
	public class BiomeTemplate : CachedScriptableObject<BiomeTemplate>, ICachedObject
	{
		[Header("Identity")]
		[Tooltip("Shown in tools and generated names, e.g. 'Alpine Meadow'. Defaults to the asset name.")]
		public string DisplayName;
		[TextArea]
		public string Description;
		[Tooltip("Colour that identifies this biome on a biome map and in the terrain painter.")]
		public Color BiomeColorId = Color.clear;
		[Tooltip("Colour used for scene gizmos.")]
		public Color GizmoColor = Color.white;

		[Header("Elevation")]
		[Tooltip("Elevation tier this biome belongs to: 0 deep ocean … 8 nival, 9 for biomes that can appear at any elevation.")]
		[Range(0, 9)] public int ElevationTier = 4;
		[Tooltip("Normalised height band the biome occupies (0 sea floor, 1 highest peak).")]
		[Range(0f, 1f)] public float MinHeight = 0f;
		[Range(0f, 1f)] public float MaxHeight = 1f;

		[Header("Climate envelope — where generation chooses this biome")]
		[Range(-1f, 1f)] public float MinTemperature = -1f;
		[Range(-1f, 1f)] public float MaxTemperature = 1f;
		[Range(-1f, 1f)] public float MinHumidity = -1f;
		[Range(-1f, 1f)] public float MaxHumidity = 1f;
		[Tooltip("The warmest season this biome stands, on the temperature scale (0 freezing, 0.30 is 10 °C). Köppen's lines: anything with trees needs a summer of 10 °C or more, tundra and bog anything above freezing, an ice sheet a summer that never thaws. Read only where the body's seasons are known (a placed scene); -1 to 1 asks nothing.")]
		[Range(-1f, 1f)] public float MinWarmestSeason = -1f;
		[Range(-1f, 1f)] public float MaxWarmestSeason = 1f;
		[Tooltip("Relative chance among biomes whose envelopes fit. 0 = never chosen by climate; placed by hand only.")]
		[Min(0f)] public float SelectionWeight = 1f;

		[Header("Where it can exist at all")]
		/// <remarks>
		/// <para>
		/// The temperature and humidity envelope above says what climate a biome LIKES. These two
		/// say what it physically requires, and they are not the same question: the temperature on
		/// an airless moon at the right distance from its star is perfectly temperate, and a jungle
		/// there is still absurd.
		/// </para>
		/// <para>
		/// Kept as requirements on the biome rather than as a list of allowed biomes on each world.
		/// A world states its own conditions once; every biome states its own needs once; the
		/// resolver matches them. A list per world would be one entry per biome per body to
		/// maintain, and every biome added later would have to be threaded back through every world
		/// that ought to have it.
		/// </para>
		/// </remarks>
		[Tooltip("Air this biome needs. Vacuum-tolerant biomes (regolith, impact basin) leave every box ticked.")]
		public BiomeAtmosphereRequirement Atmosphere = BiomeAtmosphereRequirement.Any;

		[Tooltip("Needs liquid water on the surface. Off for deserts, regolith and anything cryogenic — a methane lake is not water.")]
		public bool RequiresLiquidWater;

		/// <remarks>
		/// <para>
		/// What the biome needs of the WORLD, which neither the climate envelope nor the two fields
		/// above can say: an ice moon's vents and a polar sea read the same temperature and the same
		/// humidity, and only "is this world's water frozen through and heated from below" tells
		/// them apart. Every flag is a predicate on <see cref="BiomeWorldConditions"/> derived from
		/// the body itself — see <see cref="BiomeWorldRequirement"/> for what each one tests and why.
		/// </para>
		/// <para>
		/// None, the default, asks nothing. An Earth biome never needs a flag; an alien one carries
		/// the flags that make it alien, and is then never chosen on a world that is not.
		/// </para>
		/// </remarks>
		[Tooltip("What this biome needs of the world itself, every flag ticked must hold: an ice world, cryovolcanism, tidal heating, a methane sky, no liquid water anywhere... Nothing for an Earth biome.")]
		public BiomeWorldRequirement Requires = BiomeWorldRequirement.None;

		[Header("Climate variants")]
		[Tooltip("How this biome reads under different climates. Empty uses the scene's default variants.")]
		public List<BiomeClimateVariant> ClimateVariants = new List<BiomeClimateVariant>();

		[Header("Naming")]
		public BiomeNamingData Naming = new BiomeNamingData();

		[Header("Ground, as the air meets it")]
		[Tooltip("What the wind can lift off this ground once it blows hard enough: sand, dust, regolith. None for rock, soil, vegetation, ice and water. How hard it has to blow comes from the grain and the world's own gravity and air.")]
		public WeatherSubstance LooseGround;
		[Tooltip("What this ground puts into the air by itself — a volcano's ash, a geyser field's ice. None for nearly everything.")]
		public WeatherSubstance Emits;
		[Tooltip("How hard it does, 0..1: a steady trickle at a tenth, a vent that never stops at one. An eruption multiplies it.")]
		[Range(0f, 1f)] public float EmissionRate;

		[Header("How the ground wears")]
		[Tooltip("How erosion, drainage and plateaus shape this biome's ground in a generated scene. Empty wears like temperate soil.")]
		public TerrainProcessProfile TerrainProcess;

		[Header("Main Texture Layer")]
		[Tooltip("Primary base texture that covers the majority of the biome.")]
		[SerializeField] private TerrainTextureLayer mainTextureLayer = new TerrainTextureLayer();

		[Header("Detail Texture Layers")]
		[Tooltip("Additional textures blended with the main texture for variation.")]
		[SerializeField] private List<TerrainTextureLayer> detailTextureLayers = new List<TerrainTextureLayer>();

		[Header("Road and Path Layers")]
		[Tooltip("Textures for roads and small paths within the biome.")]
		[SerializeField] private TerrainTextureLayer roadTextureLayer = new TerrainTextureLayer();
		[SerializeField] private TerrainTextureLayer smallPathTextureLayer = new TerrainTextureLayer();

		[Header("Cliff Texture Layers")]
		[Tooltip("Specialized textures for cliff faces and steep slopes.")]
		[SerializeField] private List<CliffTextureLayer> cliffTextureLayers = new List<CliffTextureLayer>();

		[Header("Riverbed Texture Layer")]
		[Tooltip("Texture for riverbeds and water-adjacent areas.")]
		[SerializeField] private TerrainTextureLayer riverbedTextureLayer = new TerrainTextureLayer();

		[Header("Lakebed Texture Layer")]
		[Tooltip("Texture for lakebeds and water body floors.")]
		[SerializeField] private TerrainTextureLayer lakebedTextureLayer = new TerrainTextureLayer();

		[System.NonSerialized]
		private List<TerrainTextureLayer> cachedTextureLayersInOrder;
		private string key;

		public TerrainTextureLayer MainTextureLayer => mainTextureLayer;
		public List<TerrainTextureLayer> DetailTextureLayers => detailTextureLayers;
		public TerrainTextureLayer RoadTextureLayer => roadTextureLayer;
		public TerrainTextureLayer SmallPathTextureLayer => smallPathTextureLayer;
		public List<CliffTextureLayer> CliffTextureLayers => cliffTextureLayers;
		public TerrainTextureLayer RiverbedTextureLayer => riverbedTextureLayer;
		public TerrainTextureLayer LakebedTextureLayer => lakebedTextureLayer;

		/// <summary>Normalised registry key: the asset name, lowercase letters only ("Alpine Meadow" → "alpinemeadow").</summary>
		public string Key
		{
			get
			{
				if (key == null)
				{
					key = BiomeClimateVariant.Normalize(name);
				}
				return key;
			}
		}

		/// <summary>Display name, falling back to the asset name.</summary>
		/// <summary>How this biome's ground wears: its profile's values, or temperate soil's when it has none.</summary>
		public TerrainProcess ResolvedTerrainProcess => TerrainProcess != null ? TerrainProcess.Values : Biomes.TerrainProcess.Temperate;

		public string ResolvedDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;

		/// <summary>True when climate-driven generation may pick this biome.</summary>
		public bool IsSelectable => SelectionWeight > 0f;

		private static readonly string[] tierNames =
		{
			"Deep Ocean", "Ocean", "Coastal Water", "Beach", "Lowland", "Highland", "Mountain", "Alpine", "Nival", "Nival",
		};

		/// <summary>Human name of an elevation tier, for tools.</summary>
		public static string TierName(int tier)
		{
			return tier >= 0 && tier < tierNames.Length ? tierNames[tier] : tier.ToString();
		}

		// ── Climate ───────────────────────────────────────────────────

		public bool ContainsHeight(float height) => height >= MinHeight && height <= MaxHeight;

		public bool ContainsClimate(float temperature, float humidity)
		{
			return temperature >= MinTemperature && temperature <= MaxTemperature
				&& humidity >= MinHumidity && humidity <= MaxHumidity;
		}

		/// <summary>
		/// The envelope with the summer test: <see cref="ContainsClimate(float, float)"/> and, when the
		/// sample's seasons are known, a warmest season inside <see cref="MinWarmestSeason"/>…<see cref="MaxWarmestSeason"/>.
		/// </summary>
		public bool ContainsClimate(in ClimateSample sample)
		{
			return ContainsClimate(sample.Temperature, sample.Humidity) && WarmestSeasonDistance(sample) <= 0f;
		}

		/// <summary>How far the sample's warmest season lies outside this biome's range, 0 inside or when the seasons are unknown.</summary>
		public float WarmestSeasonDistance(in ClimateSample sample)
		{
			if (!sample.SeasonKnown)
			{
				return 0f;
			}
			float w = sample.WarmestSeason;
			return w < MinWarmestSeason ? MinWarmestSeason - w : w > MaxWarmestSeason ? w - MaxWarmestSeason : 0f;
		}

		/// <summary>
		/// <see cref="ClimateDistance(float, float)"/> with the summer's distance in it too, in the same units.
		/// </summary>
		public float ClimateDistance(in ClimateSample sample)
		{
			float d = ClimateDistance(sample.Temperature, sample.Humidity);
			float w = WarmestSeasonDistance(sample);
			return Mathf.Sqrt(d * d + w * w);
		}

		/// <summary>
		/// How far a climate reading is from this biome's envelope, 0 when inside. Temperature and
		/// humidity each span 2 units, so the distance is in the same units as the ranges.
		/// </summary>
		public float ClimateDistance(float temperature, float humidity)
		{
			float dt = temperature < MinTemperature ? MinTemperature - temperature : temperature > MaxTemperature ? temperature - MaxTemperature : 0f;
			float dh = humidity < MinHumidity ? MinHumidity - humidity : humidity > MaxHumidity ? humidity - MaxHumidity : 0f;
			return Mathf.Sqrt(dt * dt + dh * dh);
		}

		/// <summary>
		/// How central a reading is within the envelope, 1 at the centre falling to 0 at its edge;
		/// used to break ties between biomes that all contain the reading.
		/// </summary>
		public float ClimateCentrality(float temperature, float humidity)
		{
			float halfT = Mathf.Max(0.0001f, (MaxTemperature - MinTemperature) * 0.5f);
			float halfH = Mathf.Max(0.0001f, (MaxHumidity - MinHumidity) * 0.5f);
			float t = Mathf.Abs(temperature - (MinTemperature + halfT)) / halfT;
			float h = Mathf.Abs(humidity - (MinHumidity + halfH)) / halfH;
			return Mathf.Clamp01(1f - Mathf.Max(t, h));
		}

		/// <summary>The first of this template's own variants that fits the reading, or null.</summary>
		public BiomeClimateVariant ResolveOwnVariant(float temperature, float humidity)
		{
			for (int i = 0; i < ClimateVariants.Count; i++)
			{
				BiomeClimateVariant variant = ClimateVariants[i];
				if (variant != null && variant.Matches(temperature, humidity))
				{
					return variant;
				}
			}
			return null;
		}

		/// <summary>The template's own variant with this key, or null.</summary>
		public BiomeClimateVariant FindOwnVariant(string variantKey)
		{
			string normalized = BiomeClimateVariant.Normalize(variantKey);
			if (normalized.Length == 0)
			{
				return null;
			}
			for (int i = 0; i < ClimateVariants.Count; i++)
			{
				if (ClimateVariants[i] != null && ClimateVariants[i].Key == normalized)
				{
					return ClimateVariants[i];
				}
			}
			return null;
		}

		// ── Terrain layers ────────────────────────────────────────────

		/// <summary>All texture layers in painting order: main, details, roads, cliffs, riverbed, lakebed. Cached.</summary>
		public List<TerrainTextureLayer> GetAllTextureLayersInOrder()
		{
			if (cachedTextureLayersInOrder != null)
			{
				return cachedTextureLayersInOrder;
			}

			var layers = new List<TerrainTextureLayer>();
			if (mainTextureLayer != null && mainTextureLayer.HasAlbedo) layers.Add(mainTextureLayer);
			AddWithAlbedo(layers, detailTextureLayers);
			if (roadTextureLayer != null && roadTextureLayer.HasAlbedo) layers.Add(roadTextureLayer);
			if (smallPathTextureLayer != null && smallPathTextureLayer.HasAlbedo) layers.Add(smallPathTextureLayer);
			AddWithAlbedo(layers, cliffTextureLayers);
			if (riverbedTextureLayer != null && riverbedTextureLayer.HasAlbedo) layers.Add(riverbedTextureLayer);
			if (lakebedTextureLayer != null && lakebedTextureLayer.HasAlbedo) layers.Add(lakebedTextureLayer);

			cachedTextureLayersInOrder = layers;
			return layers;
		}

		private static void AddWithAlbedo<T>(List<TerrainTextureLayer> target, List<T> source) where T : TerrainTextureLayer
		{
			if (source == null)
			{
				return;
			}
			for (int i = 0; i < source.Count; i++)
			{
				if (source[i] != null && source[i].HasAlbedo)
				{
					target.Add(source[i]);
				}
			}
		}

		/// <summary>Call after editing layers at runtime.</summary>
		public void InvalidateTextureLayerCache()
		{
			cachedTextureLayersInOrder = null;
		}

		/// <summary>True when at least one layer has a texture to paint with.</summary>
		public bool HasValidTextureLayers()
		{
			InvalidateTextureLayerCache();
			return GetAllTextureLayersInOrder().Count > 0;
		}

		/// <summary>Gives every layer a fresh blend-noise offset so tiled biomes do not repeat.</summary>
		public void RandomizeNoiseOffsets(DeterministicRNG rng = null)
		{
			rng ??= new DeterministicRNG();
			foreach (TerrainTextureLayer layer in EveryLayer())
			{
				layer.blendNoiseOffsetX = rng.Range(-1000f, 1000f);
				layer.blendNoiseOffsetY = rng.Range(-1000f, 1000f);
			}
		}

		private IEnumerable<TerrainTextureLayer> EveryLayer()
		{
			if (mainTextureLayer != null) yield return mainTextureLayer;
			if (detailTextureLayers != null) foreach (TerrainTextureLayer l in detailTextureLayers) if (l != null) yield return l;
			if (roadTextureLayer != null) yield return roadTextureLayer;
			if (smallPathTextureLayer != null) yield return smallPathTextureLayer;
			if (cliffTextureLayers != null) foreach (CliffTextureLayer l in cliffTextureLayers) if (l != null) yield return l;
			if (riverbedTextureLayer != null) yield return riverbedTextureLayer;
			if (lakebedTextureLayer != null) yield return lakebedTextureLayer;
		}

		private void OnValidate()
		{
			mainTextureLayer ??= new TerrainTextureLayer();
			detailTextureLayers ??= new List<TerrainTextureLayer>();
			roadTextureLayer ??= new TerrainTextureLayer();
			smallPathTextureLayer ??= new TerrainTextureLayer();
			cliffTextureLayers ??= new List<CliffTextureLayer>();
			riverbedTextureLayer ??= new TerrainTextureLayer();
			lakebedTextureLayer ??= new TerrainTextureLayer();
			if (MaxHeight < MinHeight) MaxHeight = MinHeight;
			if (MaxTemperature < MinTemperature) MaxTemperature = MinTemperature;
			if (MaxHumidity < MinHumidity) MaxHumidity = MinHumidity;
			key = null;
			InvalidateTextureLayerCache();
		}

		// ── Registration ──────────────────────────────────────────────

		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			key = null;
			BiomeRegistry.Register(this);
		}

		public override void OnUnload(string typeName, string resourceName, int resourceID)
		{
			BiomeRegistry.Unregister(this);
			base.OnUnload(typeName, resourceName, resourceID);
		}
	}
}
