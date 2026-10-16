#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Lava Tube</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry LavaTube()
		{
			// A dead world's tube is cold: a ropy pahoehoe floor (no glowing lava), roof fall, lava dribble spires and benches
			// along the walls, gypsum crusts, ice in the cold traps.
			return E("Lava Tube", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Basalt roof fall", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
					Outcrops("Lava dribbles", 0.03f, 8f, F(RockTypes.Basalt, "Dribble")), Outcrops("Lava benches", 0.015f, 18f, F(RockTypes.Basalt, "Ledges"), 40f)),
				Ds(D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.005f, 20f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Pahoehoe, 96f, 5f, -1f, -1f, Outcrops("Cold-trap ice", 0.004f, 20f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
