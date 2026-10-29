#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rocky Coast</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RockyCoast()
		{
			// North Atlantic rocky shore (−33…5 °C): sea stacks, wrack and driftwood on the pebbles, orange and black
			// lichen above the splash zone, sea-thrift cushions and heather on the clifftop, gorse only where it is mild
			// and dwarf birch where it is arctic; mussel and barnacle bands in the intertidal.
			return E("Rocky Coast", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 5f), Outcrops("Sea stacks", 0.002f, 60f, F(RockTypes.Granite, "Tor"), 20f)),
				Ds(D(Ground.Pebbles, 32f, 3f, 0f, 25f, Stones(Pebbles("Grey"), 10f), Blocks("Granite", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split")),
						Plants("DebrisWrack", 2f, 128, 20f), Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 20f)),
					D(Ground.GrassDry, 64f, 2f, 0f, 20f, Grass("GrassTuft", 12f), Band(Bush("Gorse", 0.5f), 0f, 1f),
						Plants("CushionThrift", 3f, 160, 45f), Bush("Heather", 1f), Band(Bush("DwarfBirch", 0.4f), -1f, -0.3f)),
					D(Ground.Lichen, 48f, 3f, 0f, 60f)),
				C(Ground.CliffRock, 38f), Ground.Pebbles, Ground.Gravel)
				.Under(Sea("MusselBed", 4f, 0f, 3f, 180, 60f), Sea("Barnacles", 4f, 0f, 3f, 180, 70f))
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Basalt, RockTypes.Slate, RockTypes.Schist, RockTypes.Quartzite)
				.Look(BiomeLookClass.Coastal)
				.Wildlife(BiomeHabitat.Coast | BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.StoneCircle, 1f),
					(POIType.Temple, 1f), (POIType.Monastery, 0.5f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Village, 0.5f),
					(POIType.Town, 0.5f), (POIType.City, 0.5f), (POIType.Capital, 0.5f), (POIType.Port, 0.5f), (POIType.Keep, 1f),
					(POIType.Castle, 0.5f), (POIType.Fortress, 0.5f), (POIType.TradingPost, 0.5f), (POIType.Waystation, 0.5f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 0.5f),
					(POIType.Windmill, 0.5f), (POIType.Watermill, 0.5f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f),
					(POIType.SignalBeacon, 1f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.Eyrie, 0.8f), (POIType.RuneStone, 0.6f),
					(POIType.TidalIslandAbbey, 1f), (POIType.CliffCastle, 1.6f), (POIType.SmugglersTunnel, 1f), (POIType.WreckersBeacon, 1f),
					(POIType.SirenRock, 1.4f), (POIType.SeaWitchGrotto, 0.8f), (POIType.LongshipLanding, 1f), (POIType.KrakenWreck, 0.8f))
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
				// (the fantasy wave's variants end here)
				.GrowsVines(0.6f, 0.55f, 0.25f, 0.04f, ("Ivy", 1f))
				.Gathers("Iron Vein", "Samphire Patch")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Coastal");
		}
	}
}
#endif
