#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Cryovolcanic Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CryovolcanicPlain()
		{
			// Smooth cryolava with lobate flow fronts, ice vent cones in fields, frost blooms, salt stains, a few penitentes.
			return E("Cryovolcanic Plain", L(Ground.Frost, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.02f, 16f, "Rounded", "Slab"), Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f)),
				Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Blocks("Cryolava lobes", 0.015f, 16f, F(RockTypes.WaterIce, "Lobe"), 15f)),
					D(Ground.Snow, 96f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")), Aligned(Outcrops("Penitentes", 0.003f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
					D(Ground.Lineae, 64f)),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Frozen)
				// Its sea halls (2026-10-26, phase I2n-b, decision 83): a melt shaft down through the shell to the warm sea under it, the hall
				// inside the Subsurface Ocean Vent's; held only on a world frozen through over a warm interior (its Requires), weighed 0.3.
				.CaveInterior(CaveForm.SeaHall, BiomeCaveInteriors.SubsurfaceOceanVent)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f), (POIType.Cryovolcano, 1f), (POIType.SeaHall, 0.3f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Barren")
				.Emits("Cryo Tephra", 0.08f);
		}
	}
}
#endif
