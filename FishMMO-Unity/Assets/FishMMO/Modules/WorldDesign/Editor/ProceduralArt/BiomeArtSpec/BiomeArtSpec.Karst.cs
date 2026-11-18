#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Karst</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Karst()
		{
			// Limestone country from Dinaric pavement to Guilin towers: holm oak, and olive and rock-rose where it is warm
			// and drier, tree ferns in the warmest; hart's-tongue fern in the grikes and ivy over the rock.
			return E("Karst", L(Ground.Grass, Grass("GrassLush", 30f), Stand(Was(0.2f, 10f), 25f, W(0.2f, 0.6f, open: 0.05f), "Oak"), Plants("Fern", 4f), Bush("Box", 0.6f), Bush("Juniper", 0.15f), Blocks("Limestone pavement", 0.03f, 18f, F(RockTypes.Limestone, "Pavement", "Block"), 12f),
					Band(Trees(0.03f, 12f, 25f, "HolmOak"), 0.35f, 1f), Band(Trees(0.02f, 12f, 20f, "Olive"), 0.45f, 1f, -1f, 0.3f), Band(Bush("Cistus", 0.3f), 0.4f, 1f, -1f, 0.5f), Band(Trees(0.05f, 8f, 25f, "TreeFern"), 0.6f, 1f)),
				Ds(D(Ground.Limestone, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, LimestoneRocks), Outcrops("Limestone pinnacles", 0.06f, 12f, F(RockTypes.Limestone, "Pinnacle"), 30f),
						Plants("FernHartstongue", 3f, 160, 45f), Plants("Ivy", 2f, 128, 60f)),
					D(Ground.Soil, 64f)),
				C(Ground.Limestone, 35f), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Limestone, RockTypes.Chalk, RockTypes.Marble)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1f), (POIType.Sinkhole, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f),
					(POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f),
					(POIType.Temple, 1f), (POIType.Monastery, 0.4f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f),
					(POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 0.4f), (POIType.Hermitage, 1f), (POIType.Village, 0.4f),
					(POIType.Town, 0.4f), (POIType.City, 0.4f), (POIType.Capital, 0.4f), (POIType.Port, 0.4f), (POIType.Keep, 1f),
					(POIType.Castle, 0.4f), (POIType.Fortress, 0.4f), (POIType.TradingPost, 0.4f), (POIType.Waystation, 0.4f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 0.4f),
					(POIType.Windmill, 0.4f), (POIType.Watermill, 0.4f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f),
					(POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f), (POIType.ElderMonolith, 0.4f),
					(POIType.WaygateRing, 0.3f), (POIType.KoboldDelving, 1f), (POIType.Cenote, 1f), (POIType.DwarvenGate, 0.8f),
					(POIType.HangingMonastery, 0.8f), (POIType.CliffRelief, 0.6f))
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
				.Prefers(POIType.DwarvenGate, "Rune Door", 1.3f)
				.Prefers(POIType.HangingMonastery, "Cliff Chapel", 1.3f)
				// (the fantasy wave's variants end here)
				.GrowsVines(1f, 0.6f, 0.4f, 0.08f, ("Ivy", 0.8f), ("Creeper", 0.2f))
				.Gathers("Silver Vein", "Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Karst");
		}
	}
}
#endif
