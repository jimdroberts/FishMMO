#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rille</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Rille()
		{
			// Hadley Rille: layered basalt ledges at the rim, talus down the walls, a dusty boulder-strewn floor, small craters.
			return E("Rille", L(Ground.Regolith, Stones(SmallRocks("Basalt"), 3f),
					With(Blocks("Crater rims", 0.002f, 24f, F(RockTypes.Breccia, "CraterRim"), 15f), r => { r.Spacing = 24f; r.Scale = new Vector2(0.35f, 0.8f); })),
				Ds(D(Ground.Basalt, 48f, 3f, 20f, 90f, Boulders(0.2f, 8f, BasaltRocks), Blocks("Basalt blocks", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
						Outcrops("Basalt ledges", 0.03f, 16f, F(RockTypes.Basalt, "Ledges"), 45f), Blocks("Basalt talus", 0.03f, 14f, F(RockTypes.Basalt, "Scree"))),
					D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.015f, 14f, F(RockTypes.Breccia, "Block", "Boulder"))),
					D(Ground.Dust, 96f, 2f, 0f, 10f, Stones(Pebbles("Basalt"), 2f))),
				C(Ground.Basalt), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
