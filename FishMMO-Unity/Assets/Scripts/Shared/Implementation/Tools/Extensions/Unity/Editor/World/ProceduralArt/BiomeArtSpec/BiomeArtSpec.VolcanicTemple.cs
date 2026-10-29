#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Volcanic Temple</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry VolcanicTemple()
		{
			// A temple inside a volcano: fumarole cones, standing basalt columns, obsidian, sulphur crystals and sparse lava
			// lichen on the cinder.
			return E("Volcanic Temple", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt fallen columns", 0.03f, 16f, F(RockTypes.Basalt, "Fallen", "Block")),
					Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Basalt columns", 0.02f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Obsidian", 0.02f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard"))),
				Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Lava, 96f, 5f),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Plants("LichenLava", 1f, 128))),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Dry)
				/* Its vents (2026-10-25, I2q-b, decision 95): Strombolian bursts on the summit terrace's vent zones (the grid until a
				 * terrace's lines are stored), and a site rate of its own: fumaroles 0.6, mud pots 0.25, hot springs 0.15, no geysers. */
				.Geothermal(0f, 0.15f, 0.6f, 0.25f, 0f, 0f, 0.3f)
				.Vents(BiomeVentLayout.Terrace, BiomeVentTiming.Strombolian)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.LavaTube, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f),
					(POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f),
					(POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.4f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f), (POIType.Hamlet, 0.3f),
					(POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WyvernNest, 1f),
					(POIType.FireTemple, 1.2f), (POIType.FireGiantHall, 1f), (POIType.SalamanderLair, 1.2f), (POIType.AshBuriedCity, 1f),
					(POIType.Keep, 1f), (POIType.Tower, 1f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Hoard", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.4f)
				.Prefers(POIType.StepPyramid, "Rune Steps", 1.4f)
				.Prefers(POIType.FireTemple, "Flame House", 1.5f)
				.Prefers(POIType.AshBuriedCity, "Grey City", 1.4f)
				// (the fantasy wave's variants end here)
				.Gathers("Emberglass Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Volcanic")
				.Emits("Volcanic Ash", 0.15f);
		}
	}
}
#endif
