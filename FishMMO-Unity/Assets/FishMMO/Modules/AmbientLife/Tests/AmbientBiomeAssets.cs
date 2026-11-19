using System.Collections.Generic;
using UnityEditor;
using FishMMO.Shared.Biomes;

namespace FishMMO.UnitTests
{
	/// <summary>Every biome template under the biome folder (core data the World Design module authors), by name.</summary>
	public static class AmbientBiomeAssets
	{
		public const string BiomeFolder = "Assets/FishMMO/Modules/WorldSimulation/Templates/Biomes";

		public static List<BiomeTemplate> All()
		{
			var biomes = new List<BiomeTemplate>();
			foreach (string guid in AssetDatabase.FindAssets("t:BiomeTemplate", new[] { BiomeFolder }))
			{
				BiomeTemplate biome = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (biome != null)
				{
					biomes.Add(biome);
				}
			}
			biomes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return biomes;
		}
	}
}
