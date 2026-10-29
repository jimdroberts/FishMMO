#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Permanent Ice</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry PermanentIce()
		{
			// Ice cap and summit firn: dense sastrugi and penitente fields on the snow, rime frost-flowers, nunatak rock on the steeps.
			return E("Permanent Ice", L(Ground.Ice, IceBlocks(0.02f, 16f, "Rounded", "Calved")),
				Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.05f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f),
						Aligned(Outcrops("Penitentes", 0.008f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f)),
					D(Ground.Frost, 96f, 2f, -1f, -1f, Blocks("Rime", 0.02f, 10f, F(RockTypes.WaterIce, "FrostFlowers"), 30f)),
					D(Ground.Rock, 48f, 3f, 30f, 90f, Stones(SmallRocks("Grey"), 1f))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 0.2f), (POIType.Shrine, 0.2f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 0.2f),
					(POIType.Monument, 0.2f), (POIType.Wreck, 0.2f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Glacier, 1f),
					(POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.IceCave, 1f), (POIType.BanditCamp, 0.2f), (POIType.FishingCamp, 0.2f),
					(POIType.MonsterDen, 0.2f), (POIType.Nest, 0.2f), (POIType.RitualSite, 0.2f), (POIType.Temple, 0.2f), (POIType.LeyNexus, 1f),
					(POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 0.2f), (POIType.Crypt, 0.2f), (POIType.Ossuary, 0.2f),
					(POIType.Ruins, 0.2f), (POIType.RuinedTower, 0.2f), (POIType.Statue, 0.2f), (POIType.AncientRoad, 0.2f),
					(POIType.Hermitage, 0.2f), (POIType.Mine, 0.2f), (POIType.Quarry, 0.2f), (POIType.Lighthouse, 0.2f),
					(POIType.SmugglersCove, 0.2f), (POIType.PirateCove, 0.2f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 0.2f), (POIType.BossLair, 0.2f), (POIType.OreVein, 0.2f),
					(POIType.CrystalFormation, 0.2f), (POIType.HerbGrove, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.8f), (POIType.GoblinWarren, 0.7f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 0.6f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.TitansBones, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.8f), (POIType.SummitCairn, 0.4f),
					(POIType.FrostGiantSteading, 1.2f), (POIType.RuneStone, 1f), (POIType.FrozenArmy, 1.2f), (POIType.DragonInTheIce, 1f),
					(POIType.MammothGraveyard, 0.6f), (POIType.WinterWolfDen, 1f), (POIType.AuroraShrine, 1f), (POIType.LichSpire, 1f),
					(POIType.LostThrone, 0.4f), (POIType.Keep, 1f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 0.5f)
				.Prefers(POIType.Village, "Longhouse", 2.5f)
				.Prefers(POIType.Village, "Yurts", 1f)
				.Prefers(POIType.Town, "Timber", 0.5f)
				.Prefers(POIType.City, "Timber", 1f)
				.Prefers(POIType.Capital, "Timber", 1f)
				.Prefers(POIType.Keep, "Palisade", 1.5f)
				.Prefers(POIType.Castle, "Highland", 1.5f)
				.Prefers(POIType.Camp, "Yurts", 2f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 2f)
				.Prefers(POIType.FishingCamp, "Huts", 1.5f)
				.Prefers(POIType.Monastery, "Timber", 1.5f)
				.Prefers(POIType.Graveyard, "Earthen", 1.5f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.6f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Bone Roost", 1.4f)
				.Prefers(POIType.GiantsSteading, "Great Hall", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.5f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.2f)
				// (the fantasy wave's variants end here)
				.Gathers("Frostbloom Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
