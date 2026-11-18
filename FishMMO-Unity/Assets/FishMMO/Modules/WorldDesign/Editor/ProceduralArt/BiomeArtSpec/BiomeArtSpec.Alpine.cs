#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Alpine</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Alpine()
		{
			// Fellfield above the trees: map lichen on the rock, moss campion cushions and alpine flowers in the gravel,
			// krummholz mountain pine, sastrugi on the snow patches.
			return E("Alpine", L(Ground.Rock, Boulders(0.3f, 6f, GreyRound), Stones(SmallRocks("Grey"), 4f),
					Blocks("Schist", 0.06f, 10f, F(RockTypes.Schist, "Lump", "Ridge", "Flags")), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder")), Plants("LichenClump", 2f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Bush("MountainPine", 0.3f, 0.8f, 40f), Plants("CushionMossCampion", 3f), Plants("FlowersAlpine", 2f, 180)),
					D(Ground.Snow, 64f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Aligned(Blocks("Sastrugi", 0.03f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(MountainGeology)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Frozen)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f),
					(POIType.HuntingLodge, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f),
					(POIType.StoneCircle, 1f), (POIType.Temple, 1f), (POIType.Monastery, 0.2f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f),
					(POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f),
					(POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f),
					(POIType.Hermitage, 1f), (POIType.Village, 0.2f), (POIType.Town, 0.2f), (POIType.City, 0.2f), (POIType.Capital, 0.2f),
					(POIType.Port, 0.2f), (POIType.Keep, 1f), (POIType.Castle, 0.2f), (POIType.Fortress, 0.2f), (POIType.TradingPost, 0.2f),
					(POIType.Waystation, 0.2f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f),
					(POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.HerbGrove, 1f), (POIType.Dockyard, 0.2f), (POIType.Watermill, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 1f), (POIType.GoblinWarren, 1f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 1.2f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f),
					(POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 1f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f),
					(POIType.WishingStone, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f),
					(POIType.KoboldDelving, 1.2f), (POIType.TrollStones, 1f), (POIType.WolfDen, 0.8f), (POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f),
					(POIType.Shieling, 0.8f), (POIType.RockCutFacade, 0.4f), (POIType.CliffPalace, 0.4f), (POIType.DwarvenGate, 1.2f),
					(POIType.MountainKingsHall, 1f), (POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f), (POIType.Eyrie, 1.2f),
					(POIType.GriffinRoost, 1f), (POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f),
					(POIType.PrayerFlagPass, 1.4f), (POIType.SilverMineTown, 1f), (POIType.FrostGiantSteading, 0.5f))
				.Prefers(POIType.Village, "Timber", 0.5f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Town, "Timber", 0.5f)
				.Prefers(POIType.Town, "Stone", 2f)
				.Prefers(POIType.City, "Stone", 1.5f)
				.Prefers(POIType.Capital, "Stone", 1.5f)
				.Prefers(POIType.Port, "Timber", 0.6f)
				.Prefers(POIType.Port, "Stone", 2f)
				.Prefers(POIType.Waystation, "Stone Inn", 2f)
				.Prefers(POIType.Castle, "Highland", 2f)
				.Prefers(POIType.Tower, "Stone", 2f)
				.Prefers(POIType.Tower, "Observatory", 2f)
				.Prefers(POIType.Hermitage, "Stone", 2f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.Keep, "Dwarven Hold", 1.2f)
				.Prefers(POIType.Tower, "Dwarven Tower", 1f)
				.Prefers(POIType.DragonLair, "Hoard", 1.4f)
				.Prefers(POIType.DragonLair, "Bone Roost", 1.2f)
				.Prefers(POIType.GiantsSteading, "Great Hall", 1.3f)
				.Prefers(POIType.GiantsSteading, "Stone Hold", 1.3f)
				.Prefers(POIType.SignalBeacon, "Balefire", 1.3f)
				.Prefers(POIType.TollGate, "Toll House", 1.3f)
				.Prefers(POIType.Hamlet, "Stone Stead", 1.5f)
				.Prefers(POIType.BurnedVillage, "Cinders", 1.4f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.2f)
				.Prefers(POIType.LostLibrary, "Dry Athenaeum", 1.2f)
				.Prefers(POIType.Grange, "Walled Barton", 1.3f)
				.Prefers(POIType.MountainKingsHall, "King's Gate", 1.3f)
				.Prefers(POIType.SilverMineTown, "Stone Delving", 1.3f)
				.Prefers(POIType.DwarvenGate, "Deep Gate", 1.2f)
				// (the fantasy wave's variants end here)
				.Gathers("Silver Vein", "Starsilver Vein", "Frostbloom Patch", "Frostpine Elder")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Mountain");
		}
	}
}
#endif
