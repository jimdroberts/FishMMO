#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Forest</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Forest()
		{
			// Beech–oak–birch forest: beech joins the oak and birch stands (one rule, so they share the woods); holly under
			// them, spring flowers, fallen branches and fungi; bilberry and needle litter under the spruce.
			return E("Forest", L(Ground.ForestFloor, Stand(Was(2f, 6f), 30f, W(0.8f, 1f), "Oak", "Birch", "Beech"), Plants("Fern", 12f), Plants("DebrisForest", 10f, 128), Plants("ShrubSmall", 3f),
					Bush("Hazel", 0.5f), Bush("Bramble", 0.6f), Bush("Laurel", 0.2f),
					Bush("Holly", 0.3f), Plants("FlowersWoodland", 5f, 180), Plants("DebrisBranch", 2f, 128), Plants("MushroomForest", 0.6f), Plants("Ivy", 2f, 128),
					Logs("Fallen logs", 0.2f, 8f, Wood("FallenLog_Oak", "FallenLog_Birch", "Stump_Broken", "Stump_Bracket", "RootPlate"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 8f), Bush("Rhododendron", 0.3f)),
					D(Ground.NeedleLitter, 96f, 2f, -1f, -1f, Stand(Was(1.5f, 6f), 30f, W(0.8f, 1f), "Spruce"), Bush("Bilberry", 4f), Plants("DebrisNeedle", 8f, 128)),
					D(Ground.Soil, 64f, 2f, 20f, 45f, Boulders(0.05f, 10f, GreyRound))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Moved("main", "Trees: Oak, Birch")
				.Accepts(TemperateGeology)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Forest);
		}
	}
}
#endif
