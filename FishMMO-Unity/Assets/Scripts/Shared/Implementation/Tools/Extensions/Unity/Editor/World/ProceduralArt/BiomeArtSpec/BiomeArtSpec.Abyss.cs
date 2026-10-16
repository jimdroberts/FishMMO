#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Abyss</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Abyss()
		{
			return E("Abyss", L(Ground.Silt), Ds(D(Ground.Basalt, 64f, 3f, 20f, 90f)), C(Ground.Basalt, 35f), Ground.Silt, Ground.Silt)
				// A hadal trench: crinoids and glass sponges on the walls, cold-seep microbial mats with tube-worm tufts
				// on the floor, holothurians and xenophyophores on the sediment.
				.Under(Sea("Sponge", 0.15f, 0f, 0f, 128, 60f), Sea("Anemone", 0.15f, 0f, 0f, 128, 60f),
					Sea("BacterialMat", 0.3f, 0f, 0f, 160, 15f), Sea("TubeWorms", 0.3f, 0f, 0f, 160, 20f), Sea("SeaCucumber", 0.2f, 0f, 0f, 128),
					Sea("Xenophyophore", 0.2f, 0f, 0f, 128), Sea("Crinoid", 0.3f, 0f, 0f, 128, 90f),
					// Basalt talus at the foot of the trench walls, manganese nodules on the sediment.
					SeaStones(SmallRocks("Basalt"), 0.8f, 0f, 0f), SeaStones(Pebbles("Nodule"), 1f, 0f, 0f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
