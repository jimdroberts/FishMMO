#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Forest Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry ForestRuins()
		{
			// Ruins consumed by temperate forest: oak with beech joining it, holly under them, ivy over the stone, woodland
			// flowers and fungi.
			return E("Forest Ruins", L(Ground.ForestFloor, Stand(Was(1f, 6f), 30f, W(0.6f, 0.9f), "Oak", "Beech"), Plants("Fern", 10f), Plants("DebrisForest", 6f, 128), Bush("Bramble", 0.8f), Bush("Laurel", 0.3f), Bush("Box", 0.15f),
					Bush("Holly", 0.2f), Plants("FlowersWoodland", 3f, 180), Plants("MushroomForest", 1f, 128)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Logs("Fallen logs", 0.03f, 10f, Wood("FallenLog_Oak", "Stump_Broken", "Stump_Bracket")))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Moved("main", "Trees: Oak")
				.Accepts(TemperateGeology)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Forest)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
