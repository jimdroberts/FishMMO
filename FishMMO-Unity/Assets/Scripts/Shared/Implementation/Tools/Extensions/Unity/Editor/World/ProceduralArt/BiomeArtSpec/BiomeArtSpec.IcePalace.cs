#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Palace</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IcePalace()
		{
			// Frozen architecture: large prismatic ice crystals and ice columns round the structures, drifts in sastrugi, a few blocks.
			return E("Ice Palace", L(Ground.Ice, With(Outcrops("Ice crystals", 0.02f, 10f, F(RockTypes.WaterIce, "Crystal")), r => r.Scale = new Vector2(1.5f, 3f)),
					Outcrops("Ice columns", 0.015f, 10f, F(RockTypes.WaterIce, "Stalagmite"))),
				Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.03f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f), IceBlocks(0.01f, 14f, "Rounded")),
					D(Ground.Flagstone, 32f, 4f)),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
