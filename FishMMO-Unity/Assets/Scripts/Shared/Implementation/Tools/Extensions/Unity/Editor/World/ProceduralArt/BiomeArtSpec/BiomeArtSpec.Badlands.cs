#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Badlands</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Badlands()
		{
			// Bisti / Badlands NP: prickly pear and rabbitbrush in the draws, a lone pinyon on the caprock, cannonball
			// concretions weathering out of the cracked flats.
			return E("Badlands", L(Ground.Clay, Plants("ShrubDry", 1f), Bush("Sagebrush", 0.3f), Bush("Creosote", 0.15f), Stones(SmallRocks("Sandstone"), 2f), Outcrops("Hoodoos", 0.04f, 14f, F(RockTypes.Sandstone, "Pedestal")),
					Blocks("Shale", 0.04f, 12f, F(RockTypes.Shale, "Stack", "Scree")),
					Plants("PadCactus", 0.3f, 128), Bush("Rabbitbrush", 0.2f), Trees(0.01f, 15f, 25f, "Pinyon"), Logs("Petrified logs", 0.01f, 16f, Wood("PetrifiedLog"), 20f)),
				Ds(D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.15f, 8f, Sandstone), Outcrops("Sandstone ledges", 0.03f, 18f, F(RockTypes.Sandstone, "Ledges"), 35f)),
					D(Ground.CrackedEarth, 64f, 2f, -1f, -1f, Blocks("Sandstone concretions", 0.02f, 16f, F(RockTypes.Sandstone, "Concretion"), 20f))),
				C(Ground.Sandstone, 35f), Ground.Clay, Ground.Clay)
				.Accepts(AridGeology)
				.Look(BiomeLookClass.Desert)
				.Wildlife(BiomeHabitat.Desert | BiomeHabitat.Mountain);
		}
	}
}
#endif
