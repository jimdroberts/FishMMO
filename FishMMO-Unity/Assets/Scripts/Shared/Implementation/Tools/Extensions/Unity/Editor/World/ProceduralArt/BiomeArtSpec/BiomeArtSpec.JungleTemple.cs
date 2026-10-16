#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Jungle Temple</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry JungleTemple()
		{
			// Ta Prohm: silk-cotton giants standing alone over the stone, vines over the flagstones, broad herbs, bromeliads, bananas.
			return E("Jungle Temple", L(Ground.JungleFloor, Stand(Was(0.8f, 5f), 25f, W(0.55f, 0.7f), "Jungle"), Plants("Fern", 12f), Bush("Hibiscus", 0.4f), Bush("Rhododendron", 0.2f),
					With(Trees(0.006f, 28f, 20f, "Kapok"), r => { r.Spacing = 28f; r.ClusterSize = 2f; }), Plants("BroadHerb", 2f)),
				Ds(D(Ground.Flagstone, 24f, 4f, -1f, -1f, Plants("Ivy", 10f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("RosetteBromeliad", 1f, 128), Trees(0.1f, 8f, 25f, "Banana"))),
				C(Ground.Rock), Ground.Mud, Ground.Mud)
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
