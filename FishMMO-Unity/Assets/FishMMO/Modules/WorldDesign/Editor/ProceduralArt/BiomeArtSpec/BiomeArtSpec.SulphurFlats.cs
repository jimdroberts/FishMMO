#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Sulphur Flats</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SulphurFlats()
		{
			// Io's sulphur plains and Dallol: sulphur chimneys and fumarole cones in vent fields, needle crystals, salt
			// terraces and polygons on the crust, SO₂ frost.
			return E("Sulphur Flats", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f), Outcrops("Sulphur chimneys", 0.02f, 10f, F(RockTypes.Sulphur, "Chimney"), 20f),
					Outcrops("Fumarole cones", 0.008f, 18f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Sulphur crystals", 0.03f, 8f, F(RockTypes.Sulphur, "Crystals"), 30f)),
				Ds(D(Ground.CrackedEarth, 64f, 2f, -1f, -1f, Blocks("Salt terraces", 0.006f, 20f, F(RockTypes.Halite, "Terrace"), 12f), Blocks("Salt polygons", 0.02f, 8f, F(RockTypes.Halite, "Polygons"), 8f)),
					D(Ground.Ash, 96f), D(Ground.Frost, 96f)),
				C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				/* Its vents (2026-10-25, I2q-b, decision 84): its sulphur dust is a deposit, not a vapour or a vented ash, so no site stood
				 * here; a site rate of its own gives it fumaroles (seven in ten, fuming yellow-white) and ponds of molten sulphur (three in
				 * ten), which wear an opaque melt look: amber in the shallows, dark red in the deep, drifting on the shared clock. */
				.Geothermal(0f, 0f, 0.7f, 0f, 0f, 0.3f, 0.35f, new UnityEngine.Color(0.925f, 0.902f, 0.753f, 1f))
				.WaterTint(new BiomeWaterLook(new UnityEngine.Color(0.941f, 0.753f, 0.251f, 1f), new UnityEngine.Color(0.627f, 0.251f, 0.055f, 1f),
					new UnityEngine.Vector3(0.9f, 0.5f, 0.1f), 1f, new UnityEngine.Color(0.784f, 0.541f, 0.188f, 1f), new UnityEngine.Color(0.784f, 0.541f, 0.188f, 1f)))
				.HotWater()
				.MeltWater()
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.HotSpring, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f),
					(POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f),
					(POIType.Headland, 1f), (POIType.FumaroleField, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.LavaTube, 1f),
					(POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f),
					(POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f),
					(POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f),
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
				.Prefers(POIType.FireTemple, "Colonnade", 1.3f)
				// (the fantasy wave's variants end here)
				.Gathers("Emberglass Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Volcanic")
				.Emits("Sulphur Dust", 0.08f);
		}
	}
}
#endif
