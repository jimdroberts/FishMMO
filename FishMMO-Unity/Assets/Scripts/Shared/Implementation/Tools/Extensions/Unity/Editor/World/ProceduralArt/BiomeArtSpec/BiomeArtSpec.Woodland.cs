#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Woodland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Woodland()
		{
			// Oak–birch wood pasture: hawthorn and holly among the hazel, bracken and foxgloves in the glades, bluebells and
			// fungi under the trees.
			return E("Woodland", L(Ground.Grass, Stand(Was(0.8f, 8f), 30f, W(0.55f, 0.8f, open: 0.05f), "Oak", "Birch"), Grass("GrassLush", 25f), Plants("FlowersMeadow", 3f), Bush("Hazel", 0.8f), Bush("Bramble", 1f),
					Bush("Hawthorn", 0.2f), Bush("Holly", 0.15f), Plants("FernBracken", 3f), Plants("FlowersTall", 1f, 180),
					Logs("Fallen logs", 0.08f, 10f, Wood("FallenLog_Oak", "FallenLog_Birch", "Stump_Broken", "Stump_Bracket"))),
				Ds(D(Ground.ForestFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Plants("DebrisForest", 6f, 128), Plants("FlowersWoodland", 5f, 180), Plants("MushroomForest", 0.5f)), D(Ground.Moss, 64f)),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(TemperateGeology)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Woodland);
		}
	}
}
#endif
