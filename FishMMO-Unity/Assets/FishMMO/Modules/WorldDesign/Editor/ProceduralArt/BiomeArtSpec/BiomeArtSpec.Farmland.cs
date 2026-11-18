#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Farmland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Farmland()
		{
			// Mixed farming: standard oaks and poplar rows, hawthorn hedgerow thickets with hazel and bramble (scatter makes
			// thickets, not lines), cow parsley on the verges, poppies at the field margins. (Wheat fields need hand-made masks.)
			return E("Farmland", L(Ground.Soil, Stand(Was(0.02f, 20f), 15f, W(0.03f, 0.5f, open: 0.06f), "Oak"), Bush("Bramble", 0.3f), Bush("Hazel", 0.15f),
					Bush("Hawthorn", 0.3f), Trees(0.01f, 8f, 15f, "LombardyPoplar")),
				Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 30f), Plants("FlowersTall", 1f, 160)), D(Ground.GrassDry, 64f, 2f, -1f, -1f, Grass("GrassDry", 20f), Plants("FlowersWarm", 1f, 160))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(LowlandGeology)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Farmland)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1.2f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f),
					(POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.Monastery, 1.2f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f),
					(POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f),
					(POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f),
					(POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 1.2f), (POIType.Hermitage, 1f), (POIType.Village, 1.2f), (POIType.Town, 1.2f),
					(POIType.City, 1.2f), (POIType.Capital, 1.2f), (POIType.Port, 1.2f), (POIType.Keep, 1.2f), (POIType.Castle, 1.2f),
					(POIType.Fortress, 1.2f), (POIType.TradingPost, 1.2f), (POIType.Waystation, 1.2f), (POIType.Mine, 1f), (POIType.Quarry, 1f),
					(POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 1.2f),
					(POIType.Windmill, 1.2f), (POIType.Watermill, 1.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.4f), (POIType.GoblinWarren, 0.7f),
					(POIType.HauntedManor, 1f), (POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f),
					(POIType.WreckedCaravan, 0.6f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 0.8f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.8f), (POIType.SiegeCamp, 0.6f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.8f), (POIType.TitansBones, 0.5f), (POIType.WishingStone, 1f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.DruidGrove, 0.3f), (POIType.ElfWaystone, 0.4f),
					(POIType.Grange, 1.2f), (POIType.Dovecote, 1f), (POIType.BeeGarden, 1f), (POIType.VillageGreen, 0.6f), (POIType.ScarecrowField, 1.2f),
					(POIType.LongBarrow, 1f), (POIType.Henge, 1f), (POIType.GallowsHill, 0.8f), (POIType.StoneFigures, 0.4f), (POIType.RuneStone, 0.4f),
					(POIType.LadyOfTheLake, 0.3f), (POIType.NymphPool, 0.4f), (POIType.Commandery, 0.5f), (POIType.RoyalMausoleum, 0.4f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.6f, 0.65f, 0.15f, 0.06f, ("Ivy", 0.75f), ("Creeper", 0.25f))
				.Gathers("Silverleaf Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Open");
		}
	}
}
#endif
