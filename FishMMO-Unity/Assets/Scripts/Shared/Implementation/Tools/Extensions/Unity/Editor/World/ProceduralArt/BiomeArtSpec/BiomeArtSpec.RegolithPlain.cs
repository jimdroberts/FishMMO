#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Regolith Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RegolithPlain()
		{
			// Lunar maria and highlands: craters at every scale, breccia ejecta, bright anorthosite blocks, ponded dust.
			return E("Regolith Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 3f), Stones(Pebbles("Grey"), 5f),
					With(Blocks("Crater rims", 0.003f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Breccia", 0.02f, 14f, F(RockTypes.Breccia, "Block", "Boulder"))),
					D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.1f, 10f, GreyRound), Blocks("Anorthosite", 0.02f, 14f, F(RockTypes.Anorthosite, "Boulder"))),
					D(Ground.Dust, 96f)),
				C(Ground.Rock), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
