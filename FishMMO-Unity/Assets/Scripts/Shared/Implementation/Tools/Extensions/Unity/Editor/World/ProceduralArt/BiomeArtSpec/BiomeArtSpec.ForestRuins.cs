#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Forest Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry ForestRuins()
		{
			// Ruins consumed by temperate forest: oak with beech joining it, holly under them, ivy over the stone, woodland
			// flowers and fungi.
			return E("Forest Ruins", L(Ground.ForestFloor, Stand(Was(1f, 6f), 30f, W(0.6f, 0.9f), "Oak", "Beech"), Plants("Fern", 10f), Plants("DebrisForest", 6f, 128), Bush("Bramble", 0.8f), Bush("Laurel", 0.3f), Bush("Box", 0.15f),
					Bush("Holly", 0.2f), Plants("FlowersWoodland", 3f, 180), Plants("MushroomForest", 1f, 128)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Logs("Fallen logs", 0.03f, 10f, Wood("FallenLog_Oak", "Stump_Broken", "Stump_Bracket")))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Moved("main", "Trees: Oak")
				.Accepts(TemperateGeology)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Forest)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.WitchHut, 0.5f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 0.5f), (POIType.BanditCamp, 1f),
					(POIType.HuntingLodge, 1.5f), (POIType.LumberCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f),
					(POIType.Nest, 0.4f), (POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.Monastery, 0.3f), (POIType.FeyRing, 1f),
					(POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f),
					(POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Village, 0.3f),
					(POIType.Town, 0.3f), (POIType.City, 0.3f), (POIType.Capital, 0.3f), (POIType.Port, 0.3f), (POIType.Keep, 1f),
					(POIType.Castle, 0.3f), (POIType.Fortress, 0.3f), (POIType.TradingPost, 0.3f), (POIType.Waystation, 0.3f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.AncientTree, 1f), (POIType.HerbGrove, 1.5f),
					(POIType.Dockyard, 0.3f), (POIType.Watermill, 0.3f), (POIType.LumberMill, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 1.2f), (POIType.GoblinWarren, 1.2f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.ManorHouse, 0.5f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f),
					(POIType.CultistHideout, 1f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.WishingStone, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.LostLibrary, 0.5f),
					(POIType.DruidGrove, 1.4f), (POIType.TrollStones, 1f), (POIType.SpiderThicket, 1.4f), (POIType.CharcoalClamps, 1.2f),
					(POIType.GreenwoodHideout, 1.2f), (POIType.GoblinMarket, 0.8f), (POIType.ElfWaystone, 1f), (POIType.WolfDen, 1f),
					(POIType.MossColossus, 0.8f), (POIType.BeeGarden, 0.5f), (POIType.HagsCauldron, 0.5f), (POIType.CovenCircle, 0.6f),
					(POIType.NymphPool, 0.8f), (POIType.LostThrone, 1.2f))
				.Prefers(POIType.Village, "Longhouse", 0.4f)
				.Prefers(POIType.Town, "Palisaded", 1.2f)
				.Prefers(POIType.City, "Timber", 1.5f)
				.Prefers(POIType.Capital, "Timber", 1.5f)
				.Prefers(POIType.Keep, "Palisade", 1.5f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.Tower, "Wizard", 1.4f)
				.Prefers(POIType.Camp, "Lean-tos", 1.5f)
				.Prefers(POIType.BanditCamp, "Palisade", 1.5f)
				.Prefers(POIType.Monastery, "Timber", 1.5f)
				.Prefers(POIType.Ruins, "Burned", 1.5f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1.4f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Keep, "Elven Hall", 1f)
				.Prefers(POIType.Tower, "Elven Tower", 1f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.3f)
				.Prefers(POIType.SpiderThicket, "Brood Nest", 1.5f)
				.Prefers(POIType.StepPyramid, "Fallen Fane", 1.6f)
				.Prefers(POIType.LostCity, "Ruined Forum", 1.3f)
				// (the fantasy wave's variants end here)
				.GrowsVines(1f, 1f, 0.45f, 0.22f, ("Ivy", 0.85f), ("Creeper", 0.15f))
				.Gathers("Silverleaf Patch", "Morel Cluster", "Ancient Oak")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Temperate");
		}
	}
}
#endif
