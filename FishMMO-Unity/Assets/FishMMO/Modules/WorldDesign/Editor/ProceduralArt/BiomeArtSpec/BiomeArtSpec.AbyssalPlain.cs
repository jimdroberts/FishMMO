#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Abyssal Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry AbyssalPlain()
		{
			return E("Abyssal Plain", L(Ground.Silt), Ds(D(Ground.Mud, 96f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// Clarion-Clipperton: manganese nodules carpeting the ooze in patches, deposit feeders crawling over it,
				// stalked glass sponges, xenophyophores and sea pens standing very sparse.
				.Under(Sea("Sponge", 0.2f, 0f, 0f, 128), Sea("Starfish", 0.3f, 0f, 0f, 96), Sea("Anemone", 0.15f, 0f, 0f, 128),
					SeaStones(Pebbles("Nodule"), 4f, 0f, 0f), SeaStones(SmallRocks("Nodule"), 1f, 0f, 0f),
					Sea("SeaCucumber", 0.3f, 0f, 0f, 128), Sea("BrittleStar", 0.4f, 0f, 0f, 128), Sea("Xenophyophore", 0.3f, 0f, 0f, 128),
					Sea("GlassSpongeStalked", 0.1f, 0f, 0f, 128), Sea("SeaPen", 0.15f, 0f, 0f, 128, 15f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Gorge, 1f), (POIType.Island, 1f), (POIType.Bay, 1f),
					(POIType.Headland, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f))
				.Gathers("Manganese Nodules")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
