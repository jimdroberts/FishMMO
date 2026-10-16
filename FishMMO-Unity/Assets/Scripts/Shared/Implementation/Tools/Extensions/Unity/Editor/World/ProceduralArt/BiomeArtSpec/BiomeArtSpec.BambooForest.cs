#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Bamboo Forest</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry BambooForest()
		{
			// Moso bamboo over a Sasa understory and culm litter; tree ferns in the warmest groves.
			return E("Bamboo Forest", L(Ground.ForestFloor, Stand(Was(3f, 3f), 25f, W(0.7f, 0.5f, metres: 200f), "Bamboo"), Plants("Fern", 8f), Grass("GrassTall", 6f), Bush("Rhododendron", 0.6f),
					Bush("DwarfBamboo", 1.5f), Plants("DebrisBamboo", 6f, 128), Plants("MushroomForest", 0.4f)),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Band(Trees(0.08f, 8f, 30f, "TreeFern"), 0.55f, 1f), Plants("Fern", 6f)), D(Ground.Soil, 64f, 2f, 20f, 45f)),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
