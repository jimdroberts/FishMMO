#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Lava Tube</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry LavaTube()
		{
			// A dead world's tube is cold: a ropy pahoehoe floor (no glowing lava), roof fall, lava dribble spires and benches
			// along the walls, gypsum crusts, ice in the cold traps.
			return E("Lava Tube", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Basalt roof fall", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
					Outcrops("Lava dribbles", 0.03f, 8f, F(RockTypes.Basalt, "Dribble")), Outcrops("Lava benches", 0.015f, 18f, F(RockTypes.Basalt, "Ledges"), 40f)),
				Ds(D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.005f, 20f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Pahoehoe, 96f, 5f, -1f, -1f, Outcrops("Cold-trap ice", 0.004f, 20f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.LavaTube, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f),
					(POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f),
					(POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f))
				.Gathers("Emberglass Cluster", "Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Volcanic");
		}
	}
}
#endif
