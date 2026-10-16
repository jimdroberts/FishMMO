#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Tholin Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry TholinPlain()
		{
			// Titan's dune seas and Huygens' cobble plain: organic dunes, water-ice cobbles, bladed penitentes on the frosted highs.
			return E("Tholin Plain", L(Ground.Tholin, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Rounded"), Stones(SmallRocks("Ice"), 3f), Stones(Pebbles("Ice"), 4f)),
				Ds(D(Ground.Frost, 96f, 2f, -1f, -1f, Aligned(Outcrops("Bladed terrain", 0.006f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f)), D(Ground.Gravel, 64f), D(Ground.TholinDune, 128f)),
				C(Ground.Rock), Ground.Tholin, Ground.Tholin)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Alien);
		}
	}
}
#endif
