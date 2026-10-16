#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Permanent Ice</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry PermanentIce()
		{
			// Ice cap and summit firn: dense sastrugi and penitente fields on the snow, rime frost-flowers, nunatak rock on the steeps.
			return E("Permanent Ice", L(Ground.Ice, IceBlocks(0.02f, 16f, "Rounded", "Calved")),
				Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.05f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f),
						Aligned(Outcrops("Penitentes", 0.008f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f)),
					D(Ground.Frost, 96f, 2f, -1f, -1f, Blocks("Rime", 0.02f, 10f, F(RockTypes.WaterIce, "FrostFlowers"), 30f)),
					D(Ground.Rock, 48f, 3f, 30f, 90f, Stones(SmallRocks("Grey"), 1f))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice);
		}
	}
}
#endif
