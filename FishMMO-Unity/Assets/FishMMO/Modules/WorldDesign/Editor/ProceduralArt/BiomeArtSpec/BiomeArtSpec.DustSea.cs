#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Dust Sea</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry DustSea()
		{
			// Lunar dust ponds, Martian dust-mantled basins: fine bright dust (Regolith is now the patches it thins to),
			// boulders sunk half into it, wind-faceted ventifacts where there is air, the odd buried crater rim.
			return E("Dust Sea", L(Ground.Dust, Buried(Boulders(0.05f, 20f, GreyRound), 0.3f, 0.6f), Stones(Pebbles("Grey"), 1f)),
				Ds(D(Ground.Sand, 128f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
					D(Ground.Regolith, 96f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.002f, 30f, F(RockTypes.Breccia, "CraterRim"), 15f), r => r.Spacing = 30f))),
				C(Ground.Rock), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.DuneSea, 1f), (POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f),
					(POIType.Ruins, 0.5f), (POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Barren")
				.Loose("Regolith Dust");
		}
	}
}
#endif
