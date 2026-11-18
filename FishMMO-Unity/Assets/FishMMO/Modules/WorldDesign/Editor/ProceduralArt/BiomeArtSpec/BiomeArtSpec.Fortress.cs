#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Fortress</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Fortress()
		{
			// A mountain stronghold: moss cushions and alpine flowers in the stone's cracks, rubble, nettles at the middens,
			// stunted juniper and mountain pine outside the walls.
			return E("Fortress", L(Ground.Flagstone, Plants("CushionMoss", 3f, 128), Plants("FlowersAlpine", 1f, 160)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 2f)),
					D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Plants("HerbRuderal", 1.5f), Bush("Juniper", 0.1f, 0.7f), Bush("MountainPine", 0.08f))),
				C(Ground.CliffRock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Slate, RockTypes.Gneiss, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Settlement)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.RitualSite, 1f),
					(POIType.Temple, 1f), (POIType.Monastery, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 1f),
					(POIType.Hermitage, 1f), (POIType.Village, 1f), (POIType.Town, 1f), (POIType.City, 1f), (POIType.Capital, 1f),
					(POIType.Port, 1f), (POIType.Keep, 1f), (POIType.Castle, 1f), (POIType.Fortress, 1f), (POIType.TradingPost, 1f),
					(POIType.Waystation, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f),
					(POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.HerbGrove, 1f), (POIType.Dockyard, 1f), (POIType.Windmill, 1f), (POIType.Watermill, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 1.4f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 1f),
					(POIType.SiegeCamp, 1.4f), (POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.LostLibrary, 0.8f), (POIType.Commandery, 1.2f), (POIType.LostThrone, 0.6f), (POIType.RoyalMausoleum, 1.2f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.5f, 0.6f, 0.15f, 0.06f, ("Ivy", 0.75f), ("Creeper", 0.25f))
				.Gathers("Iron Vein", "Silverleaf Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Mountain");
		}
	}
}
#endif
