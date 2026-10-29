#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Crater</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Crater()
		{
			// A volcanic crater: fumarole cones with sulphur rims on the floor, sulphur crystals and scoria bombs on the cinder,
			// pioneer tufts, lava lichen on the walls.
			return E("Crater", L(Ground.Gravel, Stones(SmallRocks("Grey"), 4f), Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Grass("GrassTuft", 4f, 160)),
				Ds(D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, GreyRound), Blocks("Andesite talus", 0.03f, 14f, F(RockTypes.Andesite, "Talus")), Plants("LichenLava", 3f, 128, 60f)),
					D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Andesite", 0.05f, 12f, F(RockTypes.Andesite, "Block", "Platy")), Blocks("Pumice", 0.04f, 12f, F(RockTypes.Pumice, "Lump"))),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Blocks("Scoria bombs", 0.02f, 10f, F(RockTypes.Scoria, "Bomb", "Lump")))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(VolcanicGeology)
				// The class the old name reading gave: its name has no keyword, so its climate's middle (warm, middling humidity) gave Temperate; a per-biome round may make it Volcanic.
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Wet)
				// An impact crater gives the air no ash (decision 39, the Crater round: the weather content generator's volcanic row is gone).
				.Emits(null)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Crater, 1f),
					(POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f),
					(POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f),
					(POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f),
					(POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f), (POIType.Hamlet, 0.3f),
					(POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.Keep, 1f), (POIType.Tower, 1f))
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
				// (the fantasy wave's variants end here)
				.Gathers("Starsilver Vein", "Emberglass Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Mountain");
		}
	}
}
#endif
