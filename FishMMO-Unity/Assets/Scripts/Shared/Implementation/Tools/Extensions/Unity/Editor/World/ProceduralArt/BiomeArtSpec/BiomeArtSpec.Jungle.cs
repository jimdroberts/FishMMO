#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Jungle</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Jungle()
		{
			// Layered rainforest: kapok emergents over the canopy, tree ferns and bananas in the gaps, monstera and
			// bromeliads on the shaded floor, elephant-ears in the wet hollows.
			return E("Jungle", L(Ground.JungleFloor, Stand(Was(3f, 5f), 30f, W(0.9f, 1f), "Jungle"), Stand(Was(0.3f, 8f), 20f, W(0.9f, 0.25f, open: 0.3f), "Palm"), Plants("Fern", 25f), Plants("ShrubSmall", 10f), Grass("GrassTall", 10f), Bush("Hibiscus", 0.6f),
					Trees(0.01f, 40f, 30f, "Kapok"), Plants("BroadHerb", 3f), Plants("RosetteBromeliad", 1f), Plants("Ivy", 1.5f, 128),
					Logs("Fallen logs", 0.1f, 10f, Wood("FallenLog_Oak", "Stump_Bracket"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.4f, 4f), 25f, W(0.4f, 0.25f, open: 0.02f, metres: 200f), "Bamboo"), Trees(0.15f, 8f, 30f, "TreeFern"), Trees(0.1f, 8f, 25f, "Banana")),
					D(Ground.Mud, 64f, 2f, 0f, 10f, Plants("BroadHerb", 4f))),
				C(Ground.Rock), Ground.Mud, Ground.Mud)
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle);
		}
	}
}
#endif
