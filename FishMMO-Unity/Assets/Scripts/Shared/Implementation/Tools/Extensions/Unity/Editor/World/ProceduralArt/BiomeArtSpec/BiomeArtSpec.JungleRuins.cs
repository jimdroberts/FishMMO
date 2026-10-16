#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Jungle Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry JungleRuins()
		{
			// A collapsed jungle city: canopy with the odd kapok emergent, vines over the stone, broad herbs and bromeliads,
			// tree ferns and bananas in the gaps the fallen buildings left.
			return E("Jungle Ruins", L(Ground.JungleFloor, Stand(Was(1.2f, 5f), 25f, W(0.7f, 0.8f), "Jungle"), Plants("Fern", 15f), Plants("ShrubSmall", 6f), Bush("Hibiscus", 0.5f),
					With(Trees(0.004f, 28f, 20f, "Kapok"), r => { r.Spacing = 28f; r.ClusterSize = 2f; }), Plants("BroadHerb", 3f), Plants("RosetteBromeliad", 1f, 128)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Trees(0.15f, 8f, 25f, "TreeFern"), Trees(0.1f, 8f, 25f, "Banana"), Logs("Fallen logs", 0.05f, 10f, Wood("FallenLog_Oak", "Stump_Broken")))),
				C(Ground.Rock), Ground.Mud, Ground.Mud)
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle);
		}
	}
}
#endif
