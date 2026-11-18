#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Oasis</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Oasis()
		{
			// A Saharan / Arabian spring oasis: date-palm groves (no coconut palm grows at a desert oasis) with their frond
			// litter, tamarisk and oleander (in place of hibiscus), Phragmites and sedges at the spring.
			return E("Oasis", L(Ground.Grass, Grass("GrassTall", 25f), Stand(Was(1.5f, 6f), 15f, W(0.6f, 0.9f, open: 0.05f, metres: 160f), "DatePalm"),
					Bush("Tamarisk", 0.3f), Bush("Oleander", 0.3f), Plants("DebrisPalm", 2f, 128, 20f)),
				Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Bush("Tamarisk", 0.15f)), D(Ground.Mud, 32f, 2f, 0f, 8f, Plants("Reeds", 12f), Plants("ReedsPlume", 10f, 200), Grass("GrassSedge", 15f))),
				C(Ground.Sandstone), Ground.Mud, Ground.Sand)
				.Moved("main", "Trees: Palm", "Bush: Hibiscus")
				.Accepts(RockTypes.Sandstone, RockTypes.Limestone, RockTypes.Conglomerate)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Desert | BiomeHabitat.FreshWater)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1.5f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f),
					(POIType.Monastery, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 1f),
					(POIType.Hermitage, 1f), (POIType.Oasis, 1f), (POIType.Village, 1f), (POIType.Town, 1f), (POIType.City, 1f),
					(POIType.Capital, 1f), (POIType.Port, 1f), (POIType.Keep, 1f), (POIType.Castle, 1f), (POIType.Fortress, 1f),
					(POIType.TradingPost, 1f), (POIType.Waystation, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 1f), (POIType.Windmill, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f),
					(POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f), (POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f),
					(POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f), (POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f),
					(POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1.2f), (POIType.Sphinx, 0.6f), (POIType.RockCutFacade, 0.6f),
					(POIType.DjinnPrison, 1f), (POIType.CaravanHalt, 1.2f), (POIType.BeastBoneyard, 1.2f), (POIType.FireTemple, 0.4f))
				.Prefers(POIType.Village, "Timber", 0.1f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Village, "Yurts", 0.6f)
				.Prefers(POIType.Town, "Timber", 0.1f)
				.Prefers(POIType.Town, "Stone", 2f)
				.Prefers(POIType.Town, "Palisaded", 0.2f)
				.Prefers(POIType.City, "Stone", 1.5f)
				.Prefers(POIType.City, "Timber", 0.05f)
				.Prefers(POIType.Capital, "Stone", 1.5f)
				.Prefers(POIType.Capital, "Timber", 0.05f)
				.Prefers(POIType.Port, "Timber", 0.2f)
				.Prefers(POIType.Port, "Stone", 2f)
				.Prefers(POIType.TradingPost, "Caravanserai", 2f)
				.Prefers(POIType.Waystation, "Stone Inn", 2f)
				.Prefers(POIType.Keep, "Palisade", 0.1f)
				.Prefers(POIType.Tower, "Stone", 2f)
				.Prefers(POIType.Tower, "Observatory", 1.6f)
				.Prefers(POIType.HuntingLodge, "Lodge", 0.3f)
				.Prefers(POIType.Ruins, "Burned", 0.3f)
				.Prefers(POIType.Ruins, "Temple", 1.5f)
				.Prefers(POIType.Ruins, "Excavation", 1.3f)
				.Prefers(POIType.Hermitage, "Stone", 2f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.3f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.DragonLair, "Hoard", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.4f)
				.Prefers(POIType.TollGate, "Toll House", 1.2f)
				.Prefers(POIType.Hamlet, "Stone Stead", 1.4f)
				.Prefers(POIType.BurnedVillage, "Cinders", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.4f)
				.Prefers(POIType.LostLibrary, "Dry Athenaeum", 1.4f)
				.Prefers(POIType.Pyramid, "Smooth-sided", 1.3f)
				.Prefers(POIType.CaravanHalt, "Well Camp", 1.3f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.4f, 0.4f, 0.1f, 0.04f, ("Liana", 1f))
				.Gathers("Sunpetal Patch", "Elder Ironbark")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
