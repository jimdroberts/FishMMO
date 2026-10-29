#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Underwater Canyon</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry UnderwaterCanyon()
		{
			return E("Underwater Canyon", L(Ground.Silt), Ds(D(Ground.Rock, 48f, 3f, 25f, 90f), D(Ground.Gravel, 64f)), C(Ground.CliffRock, 38f), Ground.Silt, Ground.Silt)
				// Monterey Canyon: cold-water coral and crinoids on the sheer walls, sea pens in the soft floor, dense
				// brittle-star beds, a rare seep mat.
				.Under(Sea("SeaFan", 1.5f, 4f, 0f, 160, 70f), Sea("Sponge", 1.5f, 4f, 0f, 160, 70f), Sea("Anemone", 0.5f, 2f, 0f, 128, 70f),
					Sea("ColdCoral", 1f, 50f, 0f, 160, 90f), Sea("Crinoid", 0.5f, 30f, 0f, 160, 90f), Sea("SeaPen", 1f, 20f, 0f, 160, 15f),
					Sea("BrittleStar", 2f, 20f, 0f, 160, 20f), Sea("BacterialMat", 0.05f, 200f, 0f, 160, 15f),
					// Rock fallen from the walls, and gravel the turbidity currents drop on the floor.
					SeaStones(SmallRocks("Grey"), 2f, 4f, 0f), SeaStones(Pebbles("Grey"), 3f, 4f, 0f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Gorge, 1f), (POIType.Island, 1f), (POIType.Bay, 1f),
					(POIType.Headland, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f))
				.Gathers("Kelp Holdfast", "Glass Sponge")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
