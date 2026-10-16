#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>High Desert</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry HighDesert()
		{
			// Great Basin / Colorado Plateau: open pinyon woodland where it is mild, sagebrush and rabbitbrush steppe with
			// feather grass, yucca in the warmer, spiny cushions on the gravel.
			return E("High Desert", L(Ground.Sand, Plants("ShrubDry", 6f), Grass("GrassTuft", 6f), Bush("Sagebrush", 2f), Bush("Juniper", 0.05f, 0.8f),
					Band(Stand(Was(0.1f, 10f), 30f, W(0.15f, 0.3f, open: 0.1f), "Pinyon"), 0.05f, 0.65f), Bush("Rabbitbrush", 1f), Grass("GrassFeather", 10f), Band(Plants("RosetteYucca", 0.5f), 0.25f, 1f)),
				Ds(D(Ground.Gravel, 64f, 2f, -1f, -1f, Plants("CushionSpiny", 1.5f)), D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.1f, 8f, Sandstone),
					Outcrops("Sandstone pedestals", 0.03f, 16f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 35f), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Tilted", "Block")))),
				C(Ground.Sandstone), Ground.Gravel, Ground.Gravel)
				.Accepts(AridGeology)
				.Look(BiomeLookClass.Desert)
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Desert | BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
