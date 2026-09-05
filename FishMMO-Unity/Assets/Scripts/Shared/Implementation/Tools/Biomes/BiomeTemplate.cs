using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.NameGeneration;

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
		[Tooltip("Relative chance among biomes whose envelopes fit. 0 = never chosen by climate; placed by hand only.")]
		[Min(0f)] public float SelectionWeight = 1f;

		[Header("Climate variants")]
		[Tooltip("How this biome reads under different climates. Empty uses the scene's default variants.")]
		public List<BiomeClimateVariant> ClimateVariants = new List<BiomeClimateVariant>();

		[Header("Naming")]
		public BiomeNamingData Naming = new BiomeNamingData();

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
