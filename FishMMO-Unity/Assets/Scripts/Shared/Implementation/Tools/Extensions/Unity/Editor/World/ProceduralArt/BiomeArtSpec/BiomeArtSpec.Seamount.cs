#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Seamount</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Seamount()
		{
			return E("Seamount", L(Ground.Basalt), Ds(D(Ground.Silt, 48f, 2f, 0f, 20f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// A seamount's flanks: kelp only on a shallow summit in cold water, soft corals there in warm water,
				// cold-water and black coral gardens, crinoids and glass sponges down the current-swept basalt.
				.Under(Band(Sea("Kelp", 4f, 4f, 30f, 160), -1f, 0.6f), Sea("SeaFan", 3f, 4f, 0f, 160, 60f), Sea("Sponge", 2f, 4f, 0f, 160, 60f), Sea("Anemone", 1f, 2f, 0f, 128, 60f),
					Sea("ColdCoral", 1.5f, 150f, 1000f, 180, 70f), Sea("Crinoid", 1f, 40f, 0f, 160, 70f), Sea("GlassSponge", 1f, 100f, 0f, 160, 70f),
					Band(Sea("SoftCoral", 2f, 5f, 40f, 180, 60f), 0.55f, 1f), Sea("BrittleStar", 0.5f, 20f, 0f, 128),
					// Pillow-lava rubble and pebbles shed down the basalt flanks.
					SeaStones(SmallRocks("Basalt"), 1.5f, 4f, 0f), SeaStones(Pebbles("Basalt"), 2f, 4f, 0f))
				.Moved("detail/0", "Kelp")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
