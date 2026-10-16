#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>River</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry River()
		{
			// Riparian corridor: alder, white willow and black poplar along the banks, sedges, rushes and wet flowers on the
			// mud, driftwood log jams on the gravel bars.
			return E("River", L(Ground.Grass, Grass("GrassLush", 25f), Bush("Willow", 0.4f),
					Stand(Was(0.15f, 10f), 15f, W(0.2f, 0.6f, open: 0.05f), "Alder"), Trees(0.02f, 12f, 15f, "WeepingWillow"), Trees(0.02f, 14f, 15f, "Poplar")),
				Ds(D(Ground.Pebbles, 32f, 2f, -1f, -1f, Logs("Log jams", 0.02f, 8f, Wood("Driftwood", "FallenLog_Birch"), 15f),
						// The gravel bars: rounded cobbles and pebbles the floods sorted.
						Stones(Pebbles("Grey"), 6f), Stones(SmallRocks("Grey"), 1.5f)),
					D(Ground.Mud, 48f, 2f, 0f, 10f, Plants("Reeds", 10f), Grass("GrassSedge", 15f), Grass("GrassRush", 10f), Plants("FlowersWet", 2f, 180))),
				C(Ground.Rock), Ground.Pebbles, Ground.Gravel)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Aquatic)
				.Water(BiomeWaterRole.River)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.FreshWater)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
