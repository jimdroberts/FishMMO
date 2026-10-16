#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Lake</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Lake()
		{
			// Lakeshore zonation: reeds and cattail, then sedges and rushes on the mud, wet-meadow flowers, alder carr and
			// (where it is mild) weeping willow behind; driftwood on the pebble beaches. (Water lilies need a float placement.)
			return E("Lake", L(Ground.Grass, Grass("GrassLush", 30f), Bush("Willow", 0.3f),
					Stand(Was(0.1f, 10f), 15f, W(0.15f, 0.5f, open: 0.05f), "Alder"), Band(Trees(0.02f, 12f, 15f, "WeepingWillow"), 0.3f, 1f)),
				Ds(D(Ground.Mud, 32f, 2f, 0f, 10f, Plants("Reeds", 15f), Plants("ReedsPlume", 10f, 200), Grass("GrassSedge", 15f), Grass("GrassRush", 10f), Plants("FlowersWet", 2f, 180)),
					D(Ground.Pebbles, 48f, 2f, -1f, -1f, Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 15f),
						// The beach itself: rounded pebbles and the stones the waves have not moved.
						Stones(Pebbles("Grey"), 5f), Stones(SmallRocks("Grey"), 1.2f))),
				C(Ground.Rock), Ground.Mud, Ground.Pebbles)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Aquatic)
				.Water(BiomeWaterRole.Lake)
				.Wildlife(BiomeHabitat.FreshWater);
		}
	}
}
#endif
