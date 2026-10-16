#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Glacier</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Glacier()
		{
			// Valley glacier: icefall seracs, glacier tables and dirt cones on the ablation ice, sastrugi on the snow and
			// penitentes where the air is dry, moraine stones and boulders in stripes of gravel.
			return E("Glacier", L(Ground.Ice, Stones(SmallRocks("Grey"), 0.5f), Seracs(0.02f, 25f), IceBlocks(0.05f, 12f, "Calved", "Slab"),
					Outcrops("Glacier tables", 0.005f, 30f, F(RockTypes.WaterIce, "GlacierTable"), 15f), Outcrops("Dirt cones", 0.005f, 30f, F(RockTypes.WaterIce, "DirtCone"), 15f)),
				Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.04f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f),
						Band(Aligned(Outcrops("Penitentes", 0.01f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f), -1f, 1f, -1f, -0.2f)),
					D(Ground.Rock, 48f, 3f, 30f, 90f),
					D(Ground.Gravel, 32f, 4f, 0f, 25f, Stones(SmallRocks("Grey"), 4f), Boulders(0.1f, 8f, GreyRound))),
				C(Ground.Ice, 38f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice);
		}
	}
}
#endif
