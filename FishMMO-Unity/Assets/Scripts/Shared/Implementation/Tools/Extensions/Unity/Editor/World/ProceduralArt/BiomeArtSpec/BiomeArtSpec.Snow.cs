#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Snow</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Snow()
		{
			// Polar-desert lowland: sastrugi, granite and gneiss erratics, the odd rounded ice block, frost-shattered stones
			// on the rock. (Granite outnumbers gneiss in the formations, so the cliffs stay granite: CliffRocks.BedrockOf.)
			return E("Snow", L(Ground.Snow, Boulders(0.03f, 12f, GreyRound), Aligned(Blocks("Sastrugi", 0.04f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f), IceBlocks(0.005f, 24f, "Rounded"),
					Blocks("Granite erratics", 0.01f, 24f, F(RockTypes.Granite, "Corestone", "Split")), Blocks("Gneiss erratics", 0.008f, 24f, F(RockTypes.Gneiss, "Boulder"))),
				Ds(D(Ground.Ice, 96f), D(Ground.Rock, 48f, 3f, 30f, 90f, Stones(SmallRocks("Grey"), 3f))),
				C(Ground.Rock), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
