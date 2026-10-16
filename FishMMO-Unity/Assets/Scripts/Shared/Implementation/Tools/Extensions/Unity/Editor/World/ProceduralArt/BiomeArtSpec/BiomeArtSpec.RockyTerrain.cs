#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rocky Terrain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RockyTerrain()
		{
			// Dry ranges (Great Basin, Anti-Atlas): lone bristlecones where it is cold, pinyon where it is mild, spiny
			// cushions and bunch-grass tufts between the quartzite blocks, lichen on the rock.
			return E("Rocky Terrain", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 6f), Bush("MountainPine", 0.1f, 0.9f, 40f), Blocks("Quartzite", 0.06f, 10f, F(RockTypes.Quartzite, "Block", "Wedge")),
					Band(Trees(0.01f, 14f, 35f, "Bristlecone"), -1f, 0.35f), Band(Trees(0.02f, 12f, 30f, "Pinyon"), 0.15f, 0.7f), Plants("CushionSpiny", 1.5f), Grass("GrassTuft", 5f, 200), Plants("LichenClump", 1f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Quartzite scree", 0.03f, 14f, F(RockTypes.Quartzite, "Scree"))), D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(Pebbles("Grey"), 8f))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(RockTypes.Quartzite, RockTypes.Granite, RockTypes.Gneiss, RockTypes.Slate, RockTypes.Schist)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain);
		}
	}
}
#endif
