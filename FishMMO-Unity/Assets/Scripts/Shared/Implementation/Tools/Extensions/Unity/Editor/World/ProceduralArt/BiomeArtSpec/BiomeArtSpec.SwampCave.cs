#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SwampCave()
		{
			// A flooded cave, dark inside: pale fungi and slime-mould mats on the mud, dripstone and flowstone where it is drier.
			return E("Swamp Cave", L(Ground.Mud, Plants("Reeds", 6f), Plants("MushroomCave", 2f, 140), Plants("SlimeMat", 1.5f, 128), Logs("Washed-in timber", 0.02f, 10f, Wood("FallenLog_Oak", "Driftwood"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Outcrops("Stalagmites", 0.03f, 8f, F(RockTypes.Limestone, "Stalagmite"))),
					D(Ground.Rock, 48f, 3f, 20f, 90f, Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone")))),
				C(Ground.CliffRock, 38f), Ground.Mud, Ground.Mud)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Cave)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet)
				// 2026-10-25 (decision 86, phase I2n-c): the inside of the swamps' caves cut in the soluble rocks (the Cave's set), so
				// a link to it never holds in granite or sandstone.
				.InteriorRocks(BiomeCaveInteriors.SolubleRocks);
		}
	}
}
#endif
