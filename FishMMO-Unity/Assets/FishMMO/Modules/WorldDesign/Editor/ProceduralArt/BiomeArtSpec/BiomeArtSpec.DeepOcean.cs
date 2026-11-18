#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Deep Ocean</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry DeepOcean()
		{
			return E("Deep Ocean", L(Ground.Silt), Ds(D(Ground.Mud, 128f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// The continental slope: Lophelia cold-water coral thickets on the rock, sea pens in the mud, crinoids and
				// glass sponges, brittle-star beds and the odd nodule.
				.Under(Sea("Sponge", 0.3f, 0f, 0f, 128), Sea("Starfish", 0.2f, 0f, 0f, 96), Sea("Anemone", 0.2f, 0f, 0f, 128),
					Sea("ColdCoral", 0.3f, 0f, 0f, 160, 60f), Sea("GlassSponge", 0.3f, 0f, 0f, 128), Sea("SeaPen", 1f, 0f, 0f, 160, 15f),
					Sea("Crinoid", 0.3f, 0f, 0f, 128, 60f), Sea("BrittleStar", 0.5f, 0f, 0f, 128), SeaStones(Pebbles("Nodule"), 0.5f, 0f, 0f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.HomeTo("Whale")
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Gorge, 1f), (POIType.Island, 1f), (POIType.Bay, 1f),
					(POIType.Headland, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f), (POIType.KrakenGrounds, 1f))
				.Gathers("Manganese Nodules", "Glass Sponge")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
