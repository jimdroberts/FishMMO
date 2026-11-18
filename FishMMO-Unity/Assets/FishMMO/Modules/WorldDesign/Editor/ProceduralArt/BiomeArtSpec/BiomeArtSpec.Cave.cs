#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Cave()
		{
			// A limestone show cave, floor only (no ceiling to hang stalactites from): stalagmites and columns, flowstone,
			// rimstone gours in the wet hollows, breakdown, pale fungi, rare gypsum. The pinnacles were a surface karst spire.
			return E("Cave", L(Ground.Rock, Stones(SmallRocks("Grey"), 4f), Stones(Pebbles("Grey"), 6f), Blocks("Limestone", 0.05f, 10f, F(RockTypes.Limestone, "Block")),
					Outcrops("Stalagmites", 0.05f, 6f, F(RockTypes.Limestone, "Stalagmite")), Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone"), 35f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Gypsum", 0.003f, 20f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Mud, 64f, 2f, -1f, -1f, Blocks("Rimstone gours", 0.015f, 14f, F(RockTypes.Limestone, "Gours"), 12f), Plants("MushroomCave", 1.5f, 128))),
				C(Ground.CliffRock, 35f), Ground.Mud, Ground.Gravel)
				.Moved("main", "Formations: Limestone pinnacles")
				.Accepts(RockTypes.Limestone, RockTypes.Chalk, RockTypes.Marble, RockTypes.Granite)
				.Look(BiomeLookClass.Cave)
				.Wildlife(BiomeHabitat.Cave)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.RitualSite, 1f),
					(POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f),
					(POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f))
				.GrowsVines(1f, 0.55f, 0.35f, 0.06f, ("Ivy", 1f))
				.Gathers("Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Karst");
		}
	}
}
#endif
