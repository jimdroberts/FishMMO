#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Geyser Field</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceGeyserField()
		{
			// Enceladus' tiger stripes: flank ridges along the vent fissures, ice vent cones, frost blooms, house-sized ice boulders.
			return E("Ice Geyser Field", L(Ground.Snow, Stones(SmallRocks("Grey"), 0.5f), IceBlocks(0.02f, 16f, "Calved"),
					With(Blocks("Fissure ridges", 0.012f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
				Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f),
						With(IceBlocks(0.004f, 30f, "Calved", "Rounded"), r => { r.Scale = new Vector2(3f, 6f); r.Spacing = 20f; })),
					D(Ground.Frost, 48f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.012f, 10f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				// Alien ground as well: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen)
				/* Its plumes (2026-10-25, I2q-b, decision 53): jets laid a few dozen metres apart along its stored stripes (the TigerStripe
				 * landform, which its round authors; until a scene stores stripes its vents stand on the grid), breathing with the moon's
				 * plume tide; their ice falls back as plume frost (Cryo Tephra stays with the Cryovolcanic Plain and Tidal Fracture). */
				.Emits("Plume Frost", 0.15f)
				.Vents(BiomeVentLayout.Fissures, BiomeVentTiming.PlumeTide)
				// Its sea halls (2026-10-26, phase I2n-b, decision 83): a melt shaft down through the shell to the warm sea under it, the hall
				// inside the Subsurface Ocean Vent's; held only on a world frozen through over a warm interior (its Requires), weighed 0.5.
				.CaveInterior(CaveForm.SeaHall, BiomeCaveInteriors.SubsurfaceOceanVent)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f), (POIType.IceGeyserField, 1f), (POIType.SeaHall, 0.5f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren");
		}
	}
}
#endif
