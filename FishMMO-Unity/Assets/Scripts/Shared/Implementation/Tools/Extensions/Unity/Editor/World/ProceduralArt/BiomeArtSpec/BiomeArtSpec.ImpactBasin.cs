#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Impact Basin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry ImpactBasin()
		{
			// Mare basin: fresh small craters, breccia and impact glass in the ejecta, bright anorthosite blocks on the rims.
			return E("Impact Basin", L(Ground.Regolith, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 4f), With(Blocks("Crater rims", 0.004f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)),
				Ds(D(Ground.Rock, 48f, 3f, 15f, 90f, Boulders(0.3f, 8f, GreyRound), Blocks("Mare basalt", 0.04f, 14f, F(RockTypes.Basalt, "Block")), Blocks("Anorthosite", 0.02f, 14f, F(RockTypes.Anorthosite, "Boulder"))),
					D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.03f, 12f, F(RockTypes.Breccia, "Block", "Boulder")), Blocks("Impact glass", 0.01f, 16f, F(RockTypes.Obsidian, "Chunk")))),
				C(Ground.CliffRock), Ground.Regolith, Ground.Regolith)
				// The class the old name reading gave: its name has no keyword, so its dry climate gave Arid.
				.Look(BiomeLookClass.Arid)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f), (POIType.ImpactBasin, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Starsilver Vein", "Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren")
				.Loose("Regolith Dust");
		}
	}
}
#endif
