#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Cryovolcanic Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CryovolcanicPlain()
		{
			// Smooth cryolava with lobate flow fronts, ice vent cones in fields, frost blooms, salt stains, a few penitentes.
			return E("Cryovolcanic Plain", L(Ground.Frost, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.02f, 16f, "Rounded", "Slab"), Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f)),
				Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Blocks("Cryolava lobes", 0.015f, 16f, F(RockTypes.WaterIce, "Lobe"), 15f)),
					D(Ground.Snow, 96f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")), Aligned(Outcrops("Penitentes", 0.003f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
					D(Ground.Lineae, 64f)),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Alien);
		}
	}
}
#endif
