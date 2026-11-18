#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Tidal Fracture</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry TidalFracture()
		{
			// Europa's lineae: double ridges, rust-brown salts in the fractures, chaos rafts of broken plate, fresh frost
			// crystals, the penitentes predicted on the sunlit ice.
			return E("Tidal Fracture", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Calved", "Slab"),
					With(Blocks("Double ridges", 0.01f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
				Ds(D(Ground.Frost, 64f, 2f, -1f, -1f, With(IceBlocks(0.015f, 16f, "Slab"), r => r.Scale = new Vector2(1.8f, 3.5f)), Aligned(Outcrops("Penitentes", 0.004f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
					D(Ground.Rock, 48f, 3f, 20f, 90f),
					D(Ground.Lineae, 48f, 3f, -1f, -1f, Outcrops("Frost crystals", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice, 38f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Wet)
				// Its vents (2026-10-25, I2q-b, decision 90): Cryo Tephra puffing from its ridge lines with the tidal kneading.
				.Vents(BiomeVentLayout.Ridges, BiomeVentTiming.Kneading)
				// Its sea halls (2026-10-26, phase I2n-b, decision 83): a melt shaft down through the shell to the warm sea under it, the hall
				// inside the Subsurface Ocean Vent's; held only on a world frozen through over a warm interior (its Requires), weighed 0.6.
				.CaveInterior(CaveForm.SeaHall, BiomeCaveInteriors.SubsurfaceOceanVent)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f), (POIType.TidalRift, 1f), (POIType.SeaHall, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren")
				.Emits("Cryo Tephra", 0.08f);
		}
	}
}
#endif
