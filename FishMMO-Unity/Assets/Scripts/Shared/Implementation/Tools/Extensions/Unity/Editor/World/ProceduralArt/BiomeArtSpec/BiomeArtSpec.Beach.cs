#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Beach</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Beach()
		{
			// A sandy coast from 3 to 33 °C: coconut palms, palm litter, pandanus and morning-glory runners only where it is
			// tropical; marram / sea-oats tussocks on the dunes, wrack and driftwood along the strandline everywhere.
			return E("Beach", L(Ground.SandBeach, Band(Trees(0.03f, 14f, 20f, "Palm"), 0.55f, 1f),
					Plants("DebrisWrack", 3f, 128, 15f), Logs("Driftwood", 0.02f, 10f, Wood("Driftwood"), 15f), Band(Plants("DebrisPalm", 1f, 128, 20f), 0.55f, 1f)),
				Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Grass("GrassDune", 10f, 160), Band(Plants("BeachVine", 2f, 160, 25f), 0.5f, 1f), Band(Trees(0.01f, 10f, 25f, "Pandanus"), 0.6f, 1f)),
					D(Ground.Pebbles, 24f, 3f, -1f, -1f, Boulders(0.3f, 6f, GreyRound))),
				C(Ground.Rock), Ground.Sand, Ground.Pebbles)
				// The shallows off the beach.
				.Under(Sea("Shells", 2f, 0.5f, 15f, 96), Sea("Starfish", 0.5f, 0.5f, 20f, 96))
				// Dune grass replaces the generic tall grass on the sand.
				.Moved("lakebed", "Seagrass").Moved("detail/0", "GrassTall")
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Basalt, RockTypes.Slate, RockTypes.Limestone, RockTypes.Chalk, RockTypes.Sandstone)
				.Look(BiomeLookClass.Coastal)
				.Wildlife(BiomeHabitat.Coast)
				.HomeTo("Crab");
		}
	}
}
#endif
