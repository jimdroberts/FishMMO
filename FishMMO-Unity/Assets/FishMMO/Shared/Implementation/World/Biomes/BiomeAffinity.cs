using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How strongly a race belongs in a biome. A race's affinities say where its settlements
	/// are named from, where spawners should favour it, and — through the biome's climate
	/// variants — how its places read under the current weather.
	/// </summary>
	[Serializable]
	public class BiomeAffinity
	{
		[Tooltip("The biome this race is at home in.")]
		[TemplateReference("BiomeTemplate")]
		public int BiomeID;

		[Tooltip("Relative weight against the race's other affinities; 1 is typical, higher is favoured.")]
		[Min(0f)] public float Weight = 1f;
	}
}
