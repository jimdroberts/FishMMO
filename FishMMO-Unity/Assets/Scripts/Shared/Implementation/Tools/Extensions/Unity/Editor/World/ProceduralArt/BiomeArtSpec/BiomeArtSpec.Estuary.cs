#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Estuary</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Estuary()
		{
			// Salt-marsh zonation: bare mudflat, cordgrass on the silt, samphire and sea lavender, then rushes and
			// Phragmites, willow and alder at the upland edge; oyster banks and driftwood below the tide line.
			return E("Estuary", L(Ground.Mud, Plants("Reeds", 30f, 220)),
				Ds(D(Ground.Silt, 48f, 2f, -1f, -1f, Grass("GrassDune", 20f, 200, 3f), Plants("ShrubSamphire", 4f, 160, 5f), Plants("FlowersWet", 3f, 180, 8f)),
					D(Ground.Grass, 48f, 2f, 0f, 10f, Grass("GrassTall", 20f), Bush("Willow", 0.2f),
						Grass("GrassRush", 15f), Plants("ReedsPlume", 10f, 200), Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 10f),
						Stand(Was(0.05f, 12f), 15f, W(0.08f, 0.6f, open: 0.03f), "Alder"))),
				C(Ground.Soil), Ground.Silt, Ground.Silt)
				.Under(Sea("MusselBedOyster", 2f, 0f, 3f, 160, 10f))
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Water(BiomeWaterRole.Estuary)
				.Wildlife(BiomeHabitat.Wetland | BiomeHabitat.Coast)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
