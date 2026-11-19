using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One of a scene's biome overrides (<c>WorldAtlasScene.BiomeOverrides</c>): wherever the planet chose
	/// <see cref="From"/>, the scene is painted <see cref="To"/> instead.
	/// </summary>
	/// <remarks>
	/// Read when the scene is cut, re-cut or repainted; the baked biome map holds the result, so nothing
	/// reads the overrides at runtime. A rule naming its <see cref="From"/> wins over the catch-all.
	/// </remarks>
	[Serializable]
	public struct BiomeOverride
	{
		[Tooltip("The biome the planet chose. Empty: every land biome the climate chose. The sea floor, and the lake, river, estuary and salt-flat biomes laid under water, are kept unless named here.")]
		public BiomeTemplate From;
		[Tooltip("The biome painted in its place. Empty: the rule does nothing.")]
		public BiomeTemplate To;
	}
}
