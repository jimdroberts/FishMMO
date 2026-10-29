#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Wasteland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Wasteland()
		{
			// Atacama-dead land: bleached snags of a lost wood, tumbleweed, varnished desert pavement, wind-faceted ventifacts.
			return E("Wasteland", L(Ground.CrackedEarth, Plants("ShrubDry", 3f), Trees(0.05f, 15f, 25f, "Dead"), Stones(SmallRocks("Grey"), 2f), Plants("ShrubTumbleweed", 0.5f, 128),
					Logs("Bleached logs", 0.01f, 16f, Wood("Driftwood"))),
				Ds(D(Ground.Ash, 64f), D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
					D(Ground.Reg, 96f, 2f, -1f, -1f, Stones(Pebbles("Grey"), 6f), Logs("Petrified logs", 0.01f, 16f, Wood("PetrifiedLog"), 20f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
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
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f),
					(POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f),
					(POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f), (POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1.2f),
					(POIType.RockCutFacade, 0.6f), (POIType.DjinnPrison, 1f), (POIType.CaravanHalt, 1.2f), (POIType.BeastBoneyard, 1.2f),
					(POIType.DreadRoad, 1.6f), (POIType.LichSpire, 0.6f), (POIType.FireTemple, 0.4f), (POIType.Keep, 1f), (POIType.Tower, 1f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 1f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				// (the fantasy wave's variants end here)
				.Gathers("Copper Lode", "Iron Vein", "Sunpetal Patch")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Arid");
		}
	}
}
#endif
