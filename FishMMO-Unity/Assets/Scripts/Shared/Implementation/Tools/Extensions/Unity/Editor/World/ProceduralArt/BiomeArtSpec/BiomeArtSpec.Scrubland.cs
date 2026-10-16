#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Scrubland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Scrubland()
		{
			// Thorn scrub, maquis and chaparral (15–33 °C): open acacia, mesquite groves and the odd wild olive, creosote,
			// thornbush, cistus at the wet end, prickly pear; sagebrush only where it is cool (it is a cold-desert shrub);
			// saltbush, agave and yucca on the bare soil.
			return E("Scrubland", L(Ground.GrassDry, Plants("ShrubDry", 8f), Plants("ShrubSmall", 4f), Band(Bush("Sagebrush", 1.5f), -1f, 0.55f), Bush("Creosote", 0.6f), Grass("GrassTuft", 15f), Stand(Was(0.05f, 15f), 25f, W(0.15f, 0.1f, open: 0.2f), "Acacia"),
					Trees(0.03f, 12f, 25f, "Mesquite"), Band(Trees(0.01f, 14f, 25f, "Olive"), -1f, 1f, -0.25f, 1f), Bush("Thornbush", 0.4f), Band(Bush("Cistus", 0.6f), -1f, 1f, -0.25f, 1f),
					Plants("PadCactus", 1f, 128)),
				Ds(D(Ground.Soil, 64f, 2f, -1f, -1f, Bush("Saltbush", 0.3f), Plants("RosetteAgave", 0.3f, 128), Plants("RosetteYucca", 0.2f, 128)),
					D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, Sandstone), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Block", "Tilted")))),
				C(Ground.Rock), Ground.Gravel, Ground.Gravel)
				.Accepts(AridGeology)
				.Look(BiomeLookClass.Arid)
				.Wildlife(BiomeHabitat.Scrub)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
