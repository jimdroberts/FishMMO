#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Methane Lake</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MethaneLake()
		{
			// Titan's shores (Huygens): rounded water-ice cobbles, bright evaporite rings on the flats the lake left, tholin dunes.
			return E("Methane Lake", L(Ground.Frost, Stones(SmallRocks("Ice"), 4f), Stones(Pebbles("Ice"), 6f), IceBlocks(0.01f, 18f, "Rounded")),
				Ds(D(Ground.Ice, 64f), D(Ground.SaltCrust, 48f, 2f, 0f, 5f), D(Ground.TholinDune, 96f, 2f, -1f, -1f, Stones(Pebbles("Ice"), 2f))), C(Ground.Ice), Ground.Tholin, Ground.Tholin)
				.Under(SeaStones(Pebbles("Ice"), 4f, 0f, 8f))
				.Look(BiomeLookClass.Alien)
				.Water(BiomeWaterRole.MethaneLake)
				.Wildlife(BiomeHabitat.Alien);
		}
	}
}
#endif
