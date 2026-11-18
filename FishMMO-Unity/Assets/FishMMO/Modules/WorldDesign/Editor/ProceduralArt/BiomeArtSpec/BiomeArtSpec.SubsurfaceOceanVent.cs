#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Subsurface Ocean Vent</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SubsurfaceOceanVent()
		{
			return E("Subsurface Ocean Vent", L(Ground.Basalt), Ds(D(Ground.Silt, 48f), D(Ground.Sulphur, 24f, 4f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// Black smokers in vent fields and white Lost City carbonate towers; round them tube worms, vent clams and
				// bacterial mats on sulphide rubble. No corals: nothing at a vent builds a reef.
				.Under(Sea("TubeWorms", 6f, 0f, 0f, 200, 50f), Sea("Anemone", 1f, 0f, 0f, 128),
					Sea("MusselBedVentClam", 3f, 0f, 0f, 180, 40f), Sea("BacterialMat", 2f, 0f, 0f, 160, 30f), SeaStones(SmallRocks("Basalt"), 2f, 0f, 0f),
					Outcrops("Sulphide smokers", 0.01f, 16f, F(RockTypes.Sulphide, "Smoker"), 30f), Outcrops("Carbonate towers", 0.004f, 30f, F(RockTypes.Chalk, "Pinnacle"), 30f))
				.Moved("main", "Coral").Moved("lakebed", "Coral")
				.Look(BiomeLookClass.Aquatic)
				// An INTERIOR biome (2026-10-26, phase I2n-b, decision 83): the sea halls of Ice Shelf, Tidal Fracture, Ice Geyser Field and
				// Cryovolcanic Plain link it (CaveForm.SeaHall), on a world frozen through over a warm interior; it is never selected on any
				// surface (BiomeSpecTable row weight 0). Its life is a sea's and a cave's (Q7: not the airless ice's Barren bit).
				.Wildlife(BiomeHabitat.Sea | BiomeHabitat.Cave)
				.Moat(BiomeMoatWater.Frozen)
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
				.Gathers("Voidstone Cluster", "Vent Clam Bed")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
