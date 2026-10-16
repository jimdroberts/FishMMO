#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Desert</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Desert()
		{
			// Sonoran / Mojave / erg: Joshua trees in the cooler desert, cholla and ocotillo in the warm, dune tufts on the
			// sand, the odd bloom after rain, and varnished desert pavement with wind-cut ventifacts.
			return E("Desert", L(Ground.Sand, Trees(0.04f, 15f, 15f, "Saguaro"), Plants("CactusBarrel", 0.5f, 96), Plants("ShrubDry", 0.5f), Bush("Creosote", 0.5f),
					Band(Trees(0.02f, 15f, 15f, "JoshuaTree"), 0.25f, 0.6f), Band(Plants("Cholla", 0.4f, 96), 0.4f, 1f), Band(Bush("Ocotillo", 0.15f), 0.4f, 1f), Grass("GrassDune", 4f, 200), Plants("FlowersDesert", 0.5f, 160)),
				Ds(D(Ground.Sandstone, 48f, 3f, 15f, 90f, Boulders(0.03f, 12f, Sandstone), Outcrops("Sandstone pedestals", 0.01f, 30f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 30f)), D(Ground.Gravel, 64f),
					D(Ground.Reg, 96f, 2f, 0f, 12f, Stones(SmallRocks("Basalt"), 2f), Blocks("Ventifacts", 0.02f, 14f, F(RockTypes.Basalt, "Ventifact"), 15f))),
				C(Ground.Sandstone), Ground.Sand, Ground.Sand)
				.Accepts(AridGeology)
				.Look(BiomeLookClass.Desert)
				.Wildlife(BiomeHabitat.Desert)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
