#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Valley</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Valley()
		{
			// Montane valley: meadow floor with tall herbs, alder, sedge and wet flowers along the wet bottoms, broadleaf
			// woods on the lower ground and larch on the colder upper slopes.
			return E("Valley", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.3f, 10f), 25f, W(0.3f, 0.7f, open: 0.05f), "Oak", "Birch"), Bush("Hazel", 0.3f), Bush("Bramble", 0.3f), Bush("Willow", 0.15f),
					Plants("FlowersTall", 2f, 180)),
				Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 8f, 180)), D(Ground.Pebbles, 32f, 3f, 0f, 8f, Blocks("Conglomerate", 0.03f, 14f, F(RockTypes.Conglomerate, "Boulder", "Block"), 10f)),
					D(Ground.Mud, 48f, 2f, 0f, 10f, Stand(Was(0.3f, 8f), 15f, W(0.4f, 0.7f, open: 0.05f), "Alder"), Grass("GrassSedge", 15f), Plants("FlowersWet", 4f, 180)),
					D(Ground.NeedleLitter, 96f, 2f, 15f, 40f, Band(Stand(Was(0.3f, 8f), 35f, W(0.5f, 0.6f, open: 0.05f), "Larch"), -1f, 0.1f))),
				C(Ground.Rock), Ground.Mud, Ground.Pebbles)
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Shale, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Grassland)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Valley, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f), (POIType.HuntingLodge, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.RitualSite, 1f), (POIType.StoneCircle, 1f), (POIType.Temple, 1f),
					(POIType.Monastery, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 1f),
					(POIType.Hermitage, 1f), (POIType.Village, 1f), (POIType.Town, 1f), (POIType.City, 1f), (POIType.Capital, 1f),
					(POIType.Port, 1f), (POIType.Keep, 1f), (POIType.Castle, 1f), (POIType.Fortress, 1f), (POIType.TradingPost, 1f),
					(POIType.Waystation, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f),
					(POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.HerbGrove, 1f), (POIType.Dockyard, 1f), (POIType.Windmill, 1f), (POIType.Watermill, 1f),
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
				.Prefers(POIType.Grange, "Abbey Grange", 1.4f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.85f, 0.7f, 0.3f, 0.08f, ("Ivy", 0.75f), ("Creeper", 0.25f))
				.Gathers("Copper Lode", "Iron Vein", "Gold Vein", "Silverleaf Patch", "Morel Cluster", "Ancient Oak")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Temperate");
		}
	}
}
#endif
