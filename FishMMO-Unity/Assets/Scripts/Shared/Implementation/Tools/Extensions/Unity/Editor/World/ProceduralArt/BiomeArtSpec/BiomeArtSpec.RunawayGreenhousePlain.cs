#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Runaway Greenhouse Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RunawayGreenhousePlain()
		{
			// The Venera landing sites: platy layered basalt slabs over dark soil, lobate pahoehoe flows, cinder.
			return E("Runaway Greenhouse Plain", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt blocks", 0.03f, 16f, F(RockTypes.Basalt, "Block")),
					Blocks("Platy slabs", 0.08f, 8f, F(RockTypes.Basalt, "Slabs"), 20f)),
				Ds(D(Ground.Sulphur, 96f), D(Ground.Ash, 64f), D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Stones(Pebbles("Basalt"), 2f)), D(Ground.Cinder, 96f)),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				// Kiln haze (2026-10-25, I2q-b, decision 89), replacing the sulphur dust.
				.Emits("Kiln Haze", 0.06f)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Volcanic");
		}
	}
}
#endif
