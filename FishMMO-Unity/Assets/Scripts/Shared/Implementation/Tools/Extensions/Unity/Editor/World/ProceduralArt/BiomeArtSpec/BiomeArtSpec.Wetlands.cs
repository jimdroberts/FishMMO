#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Wetlands</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Wetlands()
		{
			// Freshwater marsh and fen: Phragmites reedbeds and cattail on the mud, sedge tussocks and rushes, flag iris,
			// loosestrife and marigold, birch-and-alder carr (one wood: the alder joins the birch).
			return E("Wetlands", L(Ground.Grass, Grass("GrassTall", 30f), Stand(Was(0.08f, 10f), 15f, W(0.15f, 0.4f, open: 0.03f), "Birch", "Alder"), Bush("Willow", 0.8f)),
				Ds(D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 25f, 220), Plants("ReedsPlume", 15f, 220), Grass("GrassSedge", 20f)),
					D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 3f), Grass("GrassRush", 12f), Plants("FlowersWet", 3f, 180))),
				C(Ground.Soil), Ground.Mud, Ground.Mud)
				.Moved("main", "Trees: Birch")
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
