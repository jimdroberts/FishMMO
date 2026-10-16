#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Grassland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Grassland()
		{
			// Lowland pasture mosaic: turf, oak copses, hawthorn scrub among the bramble, gorse and hazel; umbellifers and
			// knapweed among the meadow flowers, bracken on the slopes.
			return E("Grassland", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.15f, 12f), 25f, W(0.1f, 0.6f, open: 0.06f), "Oak"), Bush("Bramble", 0.15f), Bush("Gorse", 0.15f), Bush("Hazel", 0.08f),
					Bush("Hawthorn", 0.1f)),
				Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 6f, 180), Grass("GrassLush", 30f), Plants("FlowersTall", 2f, 180)),
					D(Ground.Soil, 96f, 2f, 15f, 40f, Boulders(0.05f, 10f, GreyRound), Plants("FernBracken", 3f, 160, 40f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Limestone, RockTypes.Shale)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland);
		}
	}
}
#endif
