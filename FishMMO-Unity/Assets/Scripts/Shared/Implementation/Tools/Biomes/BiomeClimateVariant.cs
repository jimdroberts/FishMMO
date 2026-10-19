using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A climate reading a biome can be experienced under: the same Forest, frozen or scorched.
	///
	/// <para>Biomes are fixed when the world is generated; what the current climate changes is
	/// the variant — and with it the names a place is given and, for the terrain painter, the
	/// textures it wears. A template lists its own variants; one that lists none takes the
	/// defaults from the scene's <see cref="ClimateSettings"/>.</para>
	/// </summary>
	[Serializable]
	public class BiomeClimateVariant
	{
		[Tooltip("Shown in tools and usable as a request key, e.g. 'Frozen'.")]
		public string Name;

		[Header("Activates when the climate is within")]
		[Range(-1f, 1f)] public float MinTemperature = -1f;
		[Range(-1f, 1f)] public float MaxTemperature = 1f;
		[Range(-1f, 1f)] public float MinHumidity = -1f;
		[Range(-1f, 1f)] public float MaxHumidity = 1f;

		[Header("Naming")]
		[Tooltip("Descriptive words the generator prefers under this climate: Frozen, Frost-bitten, Sun-scorched.")]
		public string[] Adjectives;
		[Tooltip("Leading words for dungeon names under this climate: 'The Frozen', 'The Sun-cracked'.")]
		public string[] DungeonPrefixes;

		/// <remarks>
		/// <para>
		/// <b>Painted since 2026-10-18</b> (decision 29: "ClimateVariants' TextureLayerOverrides actually apply"; read nowhere
		/// before). One entry per GROUND slot of the biome, in order: the first stands in for the main layer, the second for
		/// detail 0, the third for detail 1, and so on; an entry with no art keeps the biome's own layer there, so a variant that
		/// swaps only the main ground lists one. The scene's splat painter adds each override with art to the scene's layers and,
		/// within the variant's envelope (<see cref="TextureWeight"/>, soft over <see cref="TextureFalloff"/>), hands it the share
		/// of its slot the biome's own layer gives up: a Frozen Plains wears its snow-crust sward where the year is that cold and
		/// its own grass elsewhere, cross-fading across the isotherm. Variants overriding the same slot take it in list order
		/// (the first that fits, as <see cref="BiomeTemplate.ResolveOwnVariant"/> chooses for names). An override is a whole
		/// texture layer: its own noise, bands (climate band included) and spawn rules apply where it is painted.
		/// </para>
		/// <para>
		/// The template's OWN variants only: the scene climate's default variants (<see cref="ClimateSettings.DefaultVariants"/>)
		/// stay a naming matter, so what a biome's ground is painted with is decided by its template alone.
		/// </para>
		/// </remarks>
		[Header("Terrain")]
		[Tooltip("Ground layers the terrain painter substitutes under this climate, one per ground slot in order: main, detail 0, detail 1… An entry without art keeps the biome's own there. Empty keeps every layer the biome's own.")]
		public List<TerrainTextureLayer> TextureLayerOverrides = new List<TerrainTextureLayer>();

		[Tooltip("Width of the soft edge, on the climate's scale, over which the texture overrides fade in across each closed end of the envelope, centred on the end (0.1 ≈ 3.3 °C). 0 = a hard line. Naming ignores it.")]
		[Range(0f, 1f)] public float TextureFalloff = BiomeClimateBand.DefaultFalloff;

		/// <summary>
		/// How fully the variant's texture overrides stand in at a reading: its envelope as a climate band
		/// (<see cref="BiomeClimateBand.Weight(Vector2, float, Vector2, float, float, float)"/>, soft over <see cref="TextureFalloff"/>,
		/// ends at ±1 open). 0 for a NaN reading: with no climate known no variant applies.
		/// </summary>
		public float TextureWeight(float temperature, float humidity)
		{
			if (float.IsNaN(temperature) || float.IsNaN(humidity))
			{
				return 0f;
			}
			return BiomeClimateBand.Weight(new Vector2(MinTemperature, MaxTemperature), TextureFalloff,
				new Vector2(MinHumidity, MaxHumidity), TextureFalloff, temperature, humidity);
		}

		/// <summary>The override for one ground slot (0 the main layer, k detail k − 1), or null for none listed.</summary>
		public TerrainTextureLayer TextureOverrideFor(int ground)
		{
			return TextureLayerOverrides != null && ground >= 0 && ground < TextureLayerOverrides.Count ? TextureLayerOverrides[ground] : null;
		}

		/// <summary>Normalised key: lowercase letters only.</summary>
		public string Key => Normalize(Name);

		public bool Matches(float temperature, float humidity)
		{
			return temperature >= MinTemperature && temperature <= MaxTemperature
				&& humidity >= MinHumidity && humidity <= MaxHumidity;
		}

		/// <summary>Lowercase letters only, so "Sun-scorched" and "sunscorched" are the same key.</summary>
		public static string Normalize(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "";
			}
			var chars = new char[value.Length];
			int n = 0;
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (char.IsLetter(c))
				{
					chars[n++] = char.ToLowerInvariant(c);
				}
			}
			return new string(chars, 0, n);
		}
	}
}
