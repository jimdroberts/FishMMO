#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Dust Sea</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry DustSea()
		{
			// Lunar dust ponds, Martian dust-mantled basins: fine bright dust (Regolith is now the patches it thins to),
			// boulders sunk half into it, wind-faceted ventifacts where there is air, the odd buried crater rim.
			return E("Dust Sea", L(Ground.Dust, Buried(Boulders(0.05f, 20f, GreyRound), 0.3f, 0.6f), Stones(Pebbles("Grey"), 1f)),
				Ds(D(Ground.Sand, 128f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
					D(Ground.Regolith, 96f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.002f, 30f, F(RockTypes.Breccia, "CraterRim"), 15f), r => r.Spacing = 30f))),
				C(Ground.Rock), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
