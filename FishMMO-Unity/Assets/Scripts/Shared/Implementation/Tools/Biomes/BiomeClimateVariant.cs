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

		[Header("Terrain")]
		[Tooltip("Texture layers the terrain painter substitutes under this climate. Empty keeps the biome's own.")]
		public List<TerrainTextureLayer> TextureLayerOverrides = new List<TerrainTextureLayer>();

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
