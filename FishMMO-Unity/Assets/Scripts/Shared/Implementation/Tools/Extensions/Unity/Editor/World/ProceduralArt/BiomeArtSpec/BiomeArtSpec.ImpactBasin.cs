#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Impact Basin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry ImpactBasin()
		{
			// Mare basin: fresh small craters, breccia and impact glass in the ejecta, bright anorthosite blocks on the rims.
			return E("Impact Basin", L(Ground.Regolith, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 4f), With(Blocks("Crater rims", 0.004f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)),
				Ds(D(Ground.Rock, 48f, 3f, 15f, 90f, Boulders(0.3f, 8f, GreyRound), Blocks("Mare basalt", 0.04f, 14f, F(RockTypes.Basalt, "Block")), Blocks("Anorthosite", 0.02f, 14f, F(RockTypes.Anorthosite, "Boulder"))),
					D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.03f, 12f, F(RockTypes.Breccia, "Block", "Boulder")), Blocks("Impact glass", 0.01f, 16f, F(RockTypes.Obsidian, "Chunk")))),
				C(Ground.CliffRock), Ground.Regolith, Ground.Regolith)
				// The class the old name reading gave: its name has no keyword, so its dry climate gave Arid.
				.Look(BiomeLookClass.Arid)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
