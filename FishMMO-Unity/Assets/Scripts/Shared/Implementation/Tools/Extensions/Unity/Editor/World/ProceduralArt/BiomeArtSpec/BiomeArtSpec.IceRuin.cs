#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Ruin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceRuin()
		{
			// A collapsed ice city: ice rubble, broken ice pillars, drifted snow in sastrugi, rime on the old flagstones.
			return E("Ice Ruin", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 10f, "Calved", "Slab"), Outcrops("Ice pillars", 0.02f, 8f, F(RockTypes.WaterIce, "Stalagmite"))),
				Ds(D(Ground.Snow, 48f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.04f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f)),
					D(Ground.Flagstone, 32f, 4f, -1f, -1f, Outcrops("Rime crystals", 0.015f, 8f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 0.2f), (POIType.Shrine, 0.2f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 0.2f),
					(POIType.Monument, 0.2f), (POIType.Wreck, 0.2f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.IceCave, 1f), (POIType.BanditCamp, 0.2f), (POIType.FishingCamp, 0.2f),
					(POIType.MonsterDen, 0.2f), (POIType.Nest, 0.2f), (POIType.RitualSite, 0.2f), (POIType.Temple, 0.2f), (POIType.LeyNexus, 1f),
					(POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 0.2f), (POIType.Crypt, 0.2f), (POIType.Ossuary, 0.2f),
					(POIType.Ruins, 0.2f), (POIType.RuinedTower, 0.2f), (POIType.Statue, 0.2f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 0.2f),
					(POIType.Hermitage, 0.2f), (POIType.Mine, 0.2f), (POIType.Quarry, 0.2f), (POIType.Lighthouse, 0.2f),
					(POIType.SmugglersCove, 0.2f), (POIType.PirateCove, 0.2f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 0.2f), (POIType.BossLair, 0.2f), (POIType.OreVein, 0.2f),
					(POIType.CrystalFormation, 0.2f), (POIType.HerbGrove, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.8f), (POIType.GoblinWarren, 0.7f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 1f),
					(POIType.AncientWall, 0.6f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.TitansBones, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.LostLibrary, 1.4f),
					(POIType.WolfDen, 0.8f), (POIType.SummitCairn, 0.4f), (POIType.FrostGiantSteading, 1.2f), (POIType.RuneStone, 1f),
					(POIType.FrozenArmy, 1.2f), (POIType.MammothGraveyard, 0.6f), (POIType.WinterWolfDen, 1f), (POIType.AuroraShrine, 1f),
					(POIType.LichSpire, 1f), (POIType.LostThrone, 0.4f), (POIType.Keep, 1f), (POIType.Tower, 1f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1.4f)
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
				.Gathers("Silver Vein", "Lumen Quartz Cluster", "Frostbloom Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
