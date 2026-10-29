#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Taiga</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Taiga()
		{
			// Boreal forest: larch joins the pine and aspen the birch (one rule each, so they share their woods); bilberry
			// carpets and reindeer lichen under the conifers, needle litter and fungi, fireweed in the clearings.
			return E("Taiga", L(Ground.NeedleLitter, Stand(Was(2.5f, 5f), 30f, W(0.8f, 1f), "Spruce"), Stand(Was(0.8f, 6f), 30f, W(0.8f, 0.45f), "Pine", "Larch"), Plants("Fern", 4f), Grass("GrassTuft", 8f), Bush("Juniper", 0.5f),
					Bush("Bilberry", 5f), Plants("LichenClump", 3f), Plants("DebrisNeedle", 8f, 128), Plants("MushroomForest", 0.4f),
					Logs("Fallen logs", 0.15f, 8f, Wood("FallenLog_Conifer", "FallenLog_Birch", "Stump_Broken", "RootPlate"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Stand(Was(0.2f, 6f), 25f, W(0.8f, 0.25f, open: 0.05f), "Birch", "Aspen"), Plants("ShrubSmall", 3f), Bush("Willow", 0.3f, 0.6f),
						Plants("FlowersTall", 2f, 180)),
					D(Ground.Snow, 96f)),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Moved("main", "Trees: Pine").Moved("detail/0", "Trees: Birch")
				.Accepts(BorealGeology)
				.Look(BiomeLookClass.Cold)
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
					(POIType.Nest, 0.4f), (POIType.RitualSite, 1f), (POIType.StoneCircle, 1f), (POIType.Temple, 1f), (POIType.Monastery, 0.6f),
					(POIType.FeyRing, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f),
					(POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 0.6f),
					(POIType.Hermitage, 1f), (POIType.Village, 0.6f), (POIType.Town, 0.6f), (POIType.City, 0.6f), (POIType.Capital, 0.6f),
					(POIType.Port, 0.6f), (POIType.Keep, 1f), (POIType.Castle, 0.6f), (POIType.Fortress, 0.6f), (POIType.TradingPost, 0.6f),
					(POIType.Waystation, 0.6f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f),
					(POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.AncientTree, 1f), (POIType.HerbGrove, 1.5f), (POIType.Dockyard, 0.6f), (POIType.Watermill, 0.6f),
					(POIType.LumberMill, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 1.2f), (POIType.GoblinWarren, 1.2f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.5f),
					(POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.WishingStone, 0.8f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.LostLibrary, 0.5f), (POIType.DruidGrove, 1.4f), (POIType.TreeVillage, 0.4f), (POIType.TrollStones, 1f),
					(POIType.SpiderThicket, 1.2f), (POIType.CharcoalClamps, 1.2f), (POIType.GreenwoodHideout, 1.2f), (POIType.GoblinMarket, 0.8f),
					(POIType.ElfWaystone, 1f), (POIType.WolfDen, 1f), (POIType.MossColossus, 0.8f), (POIType.BeeGarden, 0.5f), (POIType.RuneStone, 1f),
					(POIType.WinterWolfDen, 0.8f), (POIType.HagsCauldron, 0.5f), (POIType.CovenCircle, 0.6f), (POIType.NymphPool, 0.8f))
				.Prefers(POIType.Village, "Timber", 0.5f)
				.Prefers(POIType.Village, "Longhouse", 0.4f)
				.Prefers(POIType.Village, "Yurts", 1f)
				.Prefers(POIType.Town, "Timber", 0.5f)
				.Prefers(POIType.Town, "Palisaded", 1.2f)
				.Prefers(POIType.City, "Timber", 1f)
				.Prefers(POIType.Capital, "Timber", 1f)
				.Prefers(POIType.Keep, "Palisade", 1.5f)
				.Prefers(POIType.Castle, "Highland", 1.5f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.Tower, "Wizard", 1.4f)
				.Prefers(POIType.Camp, "Lean-tos", 1.5f)
				.Prefers(POIType.Camp, "Yurts", 2f)
				.Prefers(POIType.BanditCamp, "Palisade", 1.5f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 2f)
				.Prefers(POIType.FishingCamp, "Huts", 1.5f)
				.Prefers(POIType.Monastery, "Timber", 1.5f)
				.Prefers(POIType.Graveyard, "Earthen", 1.5f)
				.Prefers(POIType.Ruins, "Burned", 1.5f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Elven Hall", 1f)
				.Prefers(POIType.Tower, "Elven Tower", 1f)
				.Prefers(POIType.DragonLair, "Bone Roost", 1.4f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.GiantsSteading, "Great Hall", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.3f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.2f)
				.Prefers(POIType.TreeVillage, "Taiga Lodge", 1.5f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.35f, 0.25f, 0.06f, 0.02f, ("Creeper", 0.8f), ("Ivy", 0.2f))
				.Gathers("Iron Vein", "Frostbloom Patch", "Morel Cluster", "Frostpine Elder")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
