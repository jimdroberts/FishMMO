#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Geyser Basin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry GeyserBasin()
		{
			// Yellowstone / El Tatio: sinter flats with geyser cones and terraces, sulphur crystals at the vents, bleached
			// snags where hot water killed the trees, and a lodgepole edge round the basin.
			return E("Geyser Basin", L(Ground.CrackedEarth, Grass("GrassTuft", 4f), Blocks("Tuff", 0.02f, 16f, F(RockTypes.Tuff, "Tafoni")), Blocks("Obsidian", 0.015f, 18f, F(RockTypes.Obsidian, "Chunk")),
					Trees(0.02f, 15f, 20f, "Dead"), Stand(Was(0.1f, 8f), 25f, W(0.15f, 0.6f, open: 0.03f), "Pine")),
				Ds(D(Ground.Sulphur, 32f, 4f, -1f, -1f, Blocks("Sulphur crystals", 0.03f, 10f, F(RockTypes.Sulphur, "Crystals"), 30f)), D(Ground.Mud, 48f),
					D(Ground.Sinter, 64f, 3f, 0f, 20f, Outcrops("Sinter cones", 0.005f, 40f, F(RockTypes.Sinter, "Cone"), 20f), Blocks("Sinter terraces", 0.01f, 24f, F(RockTypes.Sinter, "Terrace"), 20f))),
				C(Ground.Rock), Ground.Mud, Ground.Mud)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
