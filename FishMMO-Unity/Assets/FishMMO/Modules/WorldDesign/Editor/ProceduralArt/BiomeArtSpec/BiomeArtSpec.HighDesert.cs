#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>High Desert</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry HighDesert()
		{
			// Great Basin / Colorado Plateau: open pinyon woodland where it is mild, sagebrush and rabbitbrush steppe with
			// feather grass, yucca in the warmer, spiny cushions on the gravel.
			return E("High Desert", L(Ground.Sand, Plants("ShrubDry", 6f), Grass("GrassTuft", 6f), Bush("Sagebrush", 2f), Bush("Juniper", 0.05f, 0.8f),
					Band(Stand(Was(0.1f, 10f), 30f, W(0.15f, 0.3f, open: 0.1f), "Pinyon"), 0.05f, 0.65f), Bush("Rabbitbrush", 1f), Grass("GrassFeather", 10f), Band(Plants("RosetteYucca", 0.5f), 0.25f, 1f)),
				Ds(D(Ground.Gravel, 64f, 2f, -1f, -1f, Plants("CushionSpiny", 1.5f)), D(Ground.Sandstone, 48f, 3f, 20f, 90f, Boulders(0.1f, 8f, Sandstone),
					Outcrops("Sandstone pedestals", 0.03f, 16f, F(RockTypes.Sandstone, "Pedestal", "Ledges"), 35f), Blocks("Sandstone beds", 0.03f, 14f, F(RockTypes.Sandstone, "Tilted", "Block")))),
				C(Ground.Sandstone), Ground.Gravel, Ground.Gravel)
				.Accepts(AridGeology)
				.Look(BiomeLookClass.Desert)
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Desert | BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1.5f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f),
					(POIType.Monastery, 0.3f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.AbandonedFarm, 0.3f),
					(POIType.Hermitage, 1f), (POIType.Oasis, 1f), (POIType.Village, 0.3f), (POIType.Town, 0.3f), (POIType.City, 0.3f),
					(POIType.Capital, 0.3f), (POIType.Port, 0.3f), (POIType.Keep, 1f), (POIType.Castle, 0.3f), (POIType.Fortress, 0.3f),
					(POIType.TradingPost, 0.3f), (POIType.Waystation, 0.3f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 0.3f), (POIType.Windmill, 0.3f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f),
					(POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f), (POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f),
					(POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f), (POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f),
					(POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1.2f), (POIType.Pyramid, 0.8f), (POIType.Sphinx, 0.6f), (POIType.RockCutFacade, 0.6f),
					(POIType.SandBuriedTemple, 0.8f), (POIType.DjinnPrison, 1f), (POIType.SandwormPit, 0.5f), (POIType.CaravanHalt, 1.2f),
					(POIType.BeastBoneyard, 1.2f), (POIType.CliffPalace, 1f), (POIType.FireTemple, 0.4f))
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
				.Prefers(POIType.NomadCamp, "Black Tents", 1.4f)
				.Prefers(POIType.Necropolis, "City of the Dead", 1.3f)
				.Prefers(POIType.Pyramid, "Smooth-sided", 1.3f)
				.Prefers(POIType.Pyramid, "Stepped", 1.2f)
				.Prefers(POIType.CaravanHalt, "Well Camp", 1.3f)
				// (the fantasy wave's variants end here)
				.Gathers("Copper Lode", "Gold Vein", "Sunpetal Patch")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Arid")
				.Loose("Silicate Sand");
		}
	}
}
#endif
