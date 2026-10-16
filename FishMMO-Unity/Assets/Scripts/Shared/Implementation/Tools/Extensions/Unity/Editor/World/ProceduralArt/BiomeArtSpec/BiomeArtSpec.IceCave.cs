#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceCave()
		{
			// Eisriesenwelt, floor only: ice stalagmites and columns, giant hoar-frost crystals, ice pebbles.
			return E("Ice Cave", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.04f, 10f, "Calved", "Rounded"), Outcrops("Ice stalagmites", 0.05f, 6f, F(RockTypes.WaterIce, "Stalagmite")), Stones(Pebbles("Ice"), 3f)),
				Ds(D(Ground.Snow, 48f), D(Ground.Frost, 64f, 2f, -1f, -1f, Outcrops("Hoar crystals", 0.03f, 6f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice, 35f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
