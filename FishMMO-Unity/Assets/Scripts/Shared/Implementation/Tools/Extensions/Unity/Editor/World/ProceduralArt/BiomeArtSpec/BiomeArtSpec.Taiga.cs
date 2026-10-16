#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Taiga</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Taiga()
		{
			// Boreal forest: larch joins the pine and aspen the birch (one rule each, so they share their woods); bilberry
			// carpets and reindeer lichen under the conifers, needle litter and fungi, fireweed in the clearings.
			return E("Taiga", L(Ground.NeedleLitter, Stand(Was(2.5f, 5f), 30f, W(0.8f, 1f), "Spruce"), Stand(Was(0.8f, 6f), 30f, W(0.8f, 0.45f), "Pine", "Larch"), Plants("Fern", 4f), Grass("GrassTuft", 8f), Bush("Juniper", 0.5f),
					Bush("Bilberry", 5f), Plants("LichenClump", 3f), Plants("DebrisNeedle", 8f, 128), Plants("MushroomForest", 0.4f),
					Logs("Fallen logs", 0.15f, 8f, Wood("FallenLog_Conifer", "FallenLog_Birch", "Stump_Broken", "RootPlate"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.2f, 6f), 25f, W(0.8f, 0.25f, open: 0.05f), "Birch", "Aspen"), Plants("ShrubSmall", 3f), Bush("Willow", 0.3f, 0.6f),
						Plants("FlowersTall", 2f, 180)),
					D(Ground.Snow, 96f)),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Moved("main", "Trees: Pine").Moved("detail/0", "Trees: Birch")
				.Accepts(BorealGeology)
				.Look(BiomeLookClass.Cold)
				.Wildlife(BiomeHabitat.Forest)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
