#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Ruin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceRuin()
		{
			// A collapsed ice city: ice rubble, broken ice pillars, drifted snow in sastrugi, rime on the old flagstones.
			return E("Ice Ruin", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 10f, "Calved", "Slab"), Outcrops("Ice pillars", 0.02f, 8f, F(RockTypes.WaterIce, "Stalagmite"))),
				Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.04f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f)),
					D(Ground.Flagstone, 32f, 4f, -1f, -1f, Outcrops("Rime crystals", 0.015f, 8f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice);
		}
	}
}
#endif
