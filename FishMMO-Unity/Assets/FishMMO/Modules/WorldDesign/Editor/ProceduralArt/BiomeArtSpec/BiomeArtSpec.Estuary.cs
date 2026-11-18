#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Estuary</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Estuary()
		{
			// The tidal Wadden of the Frisian sea-coast (biome round, approved 2026-10-18, authored 2026-10-26): grey schlick bared by
			// the ebb and braided by shining creeks, lugworm casts, green gutweed and blue-black mussel banks; landward the saltings
			// zoned by the TIDE (decision 45: bands in tides, 0 mean sea, 1 mean high water): glasswort and cordgrass on the pioneer
			// mud, the sea-lavender low marsh, the sheep-cropped kwelder with its thrift and dark rush patches, round white pannes.
			// A treeless coast whose free marsh-folk raise their villages on terps and warften, wall the sea out with siel-gated dikes,
			// boil salt from peat and lay cogs aground on the mud. The sea has a real tide on the shared clock, so the flats bare and
			// drown twice a day. Cool band (t <= 0.15): no golden samphire or shrubby sea-blite; warm band (t >= 0.4): both.
			// Fantasy: Rungholt rising from the flats with ghost lights, Ekke Nekkepenn, Fosite's well, Ebbspar glowing while the tide is out.
			// LATER (I9 T1): every ground and object texture as Estuary's own variant of its recipe (decision 12); until then the grounds
			// are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Halliglilac + Rungholt.
			// LATER (SEA-LIFE): harbour seal 1.6, flounder 1.5, brown shrimp 1.5, harbour porpoise 0.4, sturgeon 0.2, Ekke Nekkepenn 0.05;
			// the kit's baitfish and crabs stand in. LATER (AMBIENT-LIFE): wader flocks over the flats, avocets, spoonbills, brent geese in
			// the cool band, salt-marsh sheep, hares on the dike, Ekke Nekkepenn on a sandbank at dusk.
			return E("Estuary", L(Ground.MudSchlickFlat,
					// The flats: gutweed and lugworm casts at low water, the first cordgrass and glasswort at the top of the tide, cockles,
					// and the Wadden's mussel banks (they grow under the sea, dry at the ebb).
					In(BiomeBedWaters.Sea, Plants("Gutweed", 12f, 180, 5f)), Plants("LugwormCasts", 12f, 128, 5f), Plants("Glasswort", 8f, 200, 5f), Grass("GrassCordgrass", 6f, 200, 5f),
					Plants("ShellsCockle", 3f, 128, 5f), Undersea("MusselBank", 4f, 0f, 3f, 160, 10f),
					// Half-sunk erratics wearing collars of wrack, and the rotted oak stakes of a lost revetment.
					Blocks("Findlinge", 0.003f, 60f, F(RegionalRocks.Till, "Findling"), 10f),
					Logs("Dike stakes", 0.01f, 40f, Wood("DikeStakes"), 8f))
					.Tide(-BiomeSeaFloors.OpenDepth, 0.5f).Ways(path: Ground.PathMudWattweg),
				Ds(
					// d0: the firm sand flat, ribbed with ripples and coiled with lugworm casts, shell hash in tide lines.
					D(Ground.SandSandwatt, 90f, 3f, 0f, 2f, Plants("LugwormCasts", 40f, 128, 5f), Plants("ShellsCockle", 6f, 128, 5f), In(BiomeBedWaters.Sea, Plants("Gutweed", 5f, 160, 5f)),
						Undersea("MusselBank", 4f, 0f, 3f, 160, 10f), Blocks("Findlinge", 0.003f, 60f, F(RegionalRocks.Till, "Findling"), 10f),
						Logs("Dike stakes", 0.01f, 40f, Wood("DikeStakes"), 8f))
						.Tide(-1f, 0.2f).Ways(path: Ground.PathMudWattweg),
					// d1: mud combed flat by dwarf eelweed, green gutweed tufts laid one way by the ebb.
					D(Ground.MudZosteraFlat, 64f, 3f, 0f, 2f, In(BiomeBedWaters.Sea, Plants("Gutweed", 6f, 160, 5f)), Plants("ShellsCockle", 2f, 128, 5f), Undersea("MusselBank", 2f, 0f, 3f, 160, 10f))
						.Tide(-0.6f, 0.3f).Ways(path: Ground.PathMudWattweg),
					// d2: the pioneer saltings — glasswort stippled claret in autumn, small cordgrass, annual sea-blite, reed-fringed channels.
					D(Ground.MudPioneerSalting, 72f, 2.5f, 0f, 4f, Plants("Glasswort", 30f, 220, 8f), Plants("SeaBlite", 8f, 200, 8f), Grass("GrassCordgrass", 20f, 220, 8f),
						Grass("GrassSeaClubrush", 6f, 200, 8f), In(BiomeBedWaters.Sea, Plants("Gutweed", 3f, 160, 8f)))
						.Tide(0.3f, 0.8f),
					// d3: the low marsh — the grey-green saltmarsh-grass sward hazed mauve with sea lavender, creek rims of sea purslane.
					D(Ground.GrassLowMarsh, 56f, 2f, 0f, 6f, Grass("GrassSaltmarsh", 36f, 255, 10f), Grass("GrassRushGerard", 5f, 220, 10f),
						Plants("FlowersSeaLavender", 10f, 200, 10f), Plants("FlowersKwelder", 4f, 200, 10f), Plants("SeaBlite", 5f, 200, 10f),
						Plants("SeaPlantain", 4f, 160, 10f), Plants("Glasswort", 4f, 200, 10f), Bush("SeaPurslane", 3f),
						Band(Plants("FlowersGoldenSamphire", 4f, 200, 10f), 0.4f, 1f))
						.Tide(0.7f, 1.15f),
					// d4: the high marsh (kwelder) — sheep-cropped red fescue, dark rush patches, thrift cushions, wool tufts; sea couch on the
					// ungrazed rims, silver sea wormwood, the flood-line teek, and the terps' ash and elder.
					D(Ground.GrassHighMarsh, 48f, 2f, 0f, 10f, Grass("GrassFescueRed", 34f, 255, 14f), Grass("GrassSaltmarsh", 8f, 220, 14f), Grass("GrassRushGerard", 12f, 220, 14f),
						Plants("FlowersThrift", 6f, 200, 14f), Plants("FlowersKwelder", 4f, 200, 14f), Plants("FlowersSeaLavender", 4f, 200, 14f),
						Plants("SeaPlantain", 3f, 160, 14f), Plants("ScurvyGrass", 2f, 160, 14f), Plants("SeaWormwood", 4f, 160, 14f), Grass("GrassSeaCouch", 14f, 200, 14f),
						Plants("Teek", 8f, 128, 14f), Band(Plants("FlowersGoldenSamphire", 3f, 200, 14f), 0.4f, 1f), Band(Bush("ShrubbySeablite", 1.5f), 0.4f, 1f),
						// LATER (I7): the terps' and warften's ash and elder within 120 m of a settlement; they stand on the mounds' crowns here.
						OnLandform(Trees(0.04f, 12f, 12f, "TerpAsh"), BiomeLandformPart.Top, "Terp"), OnLandform(Trees(0.04f, 12f, 12f, "TerpAsh"), BiomeLandformPart.Top, "Warft"),
						OnLandform(Bush("Elder", 0.4f), BiomeLandformPart.Flank, "Terp"))
						.Tide(1.1f, BiomeSeaFloors.OpenDepth),
					// d5: the pannes — bare cracked grey mud, salt bloom, a pink film, a glasswort rim.
					D(Ground.SaltPanneCrust, 110f, 4f, 0f, 1f, Plants("Glasswort", 10f, 200, 5f))
						.Tide(0.8f, 1.4f),
					// d6: the creek cut-banks — laminated tidal clay under an overhung sod lip, till pebbles at the foot.
					D(Ground.SiltCreekBank, 64f, 4f, 18f, 40f, Stones(Pebbles("Till"), 2f), In(BiomeBedWaters.Sea, Plants("Gutweed", 2f, 128, 40f)))),
				C(Ground.TillKliffFace, 40f), Ground.MudFluidSulphide, Ground.MudSubtidal)
				// The sub-tidal bed and the creeks: blue-black mussel valves and ruppia threads under the water.
				.Under(Sea("MusselBank", 4f, 0f, 3f, 160, 10f))
				.UnderTheSea(BiomeSeaFloor.ToLowWater)
				.Tides(4f)
				// The old generic salt-marsh rules this round retired (Phragmites on the saltings, dune grass, tall grass and rush, willow,
				// alder, the wet flowers, samphire, oysters and the driftwood).
				.Moved("main", "Reeds")
				.Moved("detail/0", "GrassDune", "ShrubSamphire", "FlowersWet")
				.Moved("detail/1", "GrassTall", "Bush: Willow", "GrassRush", "ReedsPlume", "Deadwood: Driftwood", "Trees: Alder")
				.Moved("lakebed", "MusselBedOyster")
				.Ways(path: Ground.PathMudSheepTrack, road: Ground.ClayKleiweg, cobbles: Ground.FlagstoneMoppen)
				.Bars(sand: Ground.SandShoalCockle, gravel: Ground.GravelSchill)
				// Own rock Till (Kliff, Slump, Lag, the Findling erratics), accepting the red Heligoland sandstone and the white chalk
				// with its flint bands. LATER (I9): Sandstone "Fositesland" and Chalk "Witte Kliff" as Estuary's own looks.
				.Accepts(RegionalRocks.Till, RockTypes.Sandstone, RockTypes.Chalk)
				.CaveShell(CaveForm.Cave, "Sandstone").CaveShell(CaveForm.SeaCave, "Sandstone").CaveShell(CaveForm.Grotto, "Chalk")
				.CaveShell(CaveForm.Mine, "Till").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				// Vivianite, the blue marsh-clay phosphate, and fantasy Ebbspar. LATER (GLOW): Ebbspar's glow follows the SeaTide (full at
				// low water, dark at high) with a faint 8 s shimmer; it glows steadily here.
				.Crystals((BiomeCaveCrystalKind.Vivianite, 0.6f), (BiomeCaveCrystalKind.Ebbspar, 0.4f))
				// Tidal creek network, terps, warften, the sea dike and the pannes (decision 45).
				.Landforms("TidalCreeks", "Terp", "Warft", "SeaDike", "SaltPannes")
				.Look(BiomeLookClass.Coastal)
				.Water(BiomeWaterRole.Estuary)
				.Wildlife(BiomeHabitat.Wetland | BiomeHabitat.Coast)
				.HomeTo(("Baitfish", 3.5f), ("Crab", 3f), ("Jellyfish", 0.8f))
				.GrowsVines(0.2f, 0.5f, 0.1f, 0.1f, ("Nightshade", 1f))
				.Moat(BiomeMoatWater.Wet)
				// The brackish channels: turbid grey-olive, a cream sea-foam scum, Wadden silt.
				.WaterTint(new BiomeWaterLook(SeaHex("#7a7e5e"), SeaHex("#2a3228"), new Vector3(0.45f, 0.30f, 0.35f), 0.65f, SeaHex("#8a7e62"), SeaHex("#e8e4cc")))
				.SeaTint(new BiomeSeaLook(new Color(0.26f, 0.30f, 0.22f), 2f, 1.8f))
				.Floats((BiomeFloatingPlantKind.Tasselweed, 0.35f), (BiomeFloatingPlantKind.Gutweed, 0.2f))
				.Blades("GrassCordgrass", t => WoodsBlade(t, "#4a6a4a", "#7a947a", 0.25f, stiffness: 1.4f, width: 0.8f, clump: 0.8f,
					head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.3f, heads: new[] { "#8a7a4a" }))
				.Blades("GrassSaltmarsh", t => WoodsBlade(t, "#5a7a6a", "#8aa498", 0.4f, width: 0.5f, bend: 0.7f,
					head: GrassBladeHead.Plume, headSize: 0.04f, headShare: 0.3f, heads: new[] { "#8a6a7a" }))
				.Blades("GrassFescueRed", t => WoodsBlade(t, "#4a5a3a", "#6a7a4a", 0.45f, width: 0.4f, clump: 0.3f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.2f, heads: new[] { "#7a3a3a" }))
				.Blades("GrassRushGerard", t => WoodsBlade(t, "#2e3e2a", "#4a5a34", 0.3f, width: 0.6f, clump: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.4f, heads: new[] { "#5a3a20" }))
				.Blades("GrassSeaCouch", t => WoodsBlade(t, "#6a8a8a", "#9ab0b0", 0.25f, stiffness: 1.5f, width: 1.1f,
					head: GrassBladeHead.SeedHead, headSize: 0.06f, headShare: 0.4f, heads: new[] { "#a8a888" }))
				.Blades("GrassSeaClubrush", t => { WoodsBlade(t, "#4a6a3a", "#7a9a4a", 0.3f, width: 1.4f,
					head: GrassBladeHead.SeedHead, headSize: 0.06f, headShare: 0.5f, heads: new[] { "#7a4a2a" }); t.Wade = 0.8f; })
				.Blades("ReedsBrackish", t => { WoodsBlade(t, "#8a9a5a", "#5e7a4a", 0.2f, stiffness: 1.2f,
					head: GrassBladeHead.Plume, headSize: 0.3f, headShare: 0.6f, heads: new[] { "#4a2a3a", "#c8b898" }); t.Wade = 0.9f; })
				.Blades("FlowersSeaLavender", t => WoodsBlade(t, "#4a6a4a", "#5a7a52", 0.35f, width: 1.2f,
					head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.06f, heads: new[] { "#8a6ac0", "#b8a0e0" }))
				.Blades("FlowersThrift", t => WoodsBlade(t, "#4a6a3a", "#5e7e44", 0.4f, width: 0.7f,
					head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.04f, heads: new[] { "#e890b0", "#f0b8cc" }))
				.Blades("FlowersKwelder", t => WoodsBlade(t, "#4a6a3a", "#6a7a3a", 0.45f,
					head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.025f, heads: new[] { "#e8a8b8", "#e88aa8", "#f4f4ee", "#6a7a3a" }))
				.Blades("FlowersGoldenSamphire", t => WoodsBlade(t, "#6a8a5a", "#7a9a62", 0.35f, width: 1.2f,
					head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.05f, heads: new[] { "#e8c030" }))
				// Sites: the villages are free marsh-folk's, the harbours sielhafens; the drowned town Rungholt is rare (one a scene).
				// LATER (I7): the named pieces — the Wierde round village, the Wohnstallhaus longhouse, Hallig warft, tuff church and
				// klokkenstoel, the Stins tower-house, the siel with its tide-swung flap gates, the tide mill whose wheel turns only on the
				// ebb, salt sieden, wattle weirs and stow nets, the Sielhafen with cogs aground, the cog yard, the Bluese beacon, the
				// Upstalsboom, the Biike, the Denghoog, drowned Rungholt (tide-bared, ghost lights), Fosite's well, the stranded cog and the
				// likedeeler hide; traffic: Frisian cog, Wadden ewer, shrimpers' kahn. The kit's variants below stand in.
				.Sites((POIType.Village, 1.6f), (POIType.FishingCamp, 1.4f), (POIType.RiverMouth, 1.4f), (POIType.Nest, 1.4f), (POIType.Watermill, 1.2f),
					(POIType.Island, 1.2f), (POIType.Delta, 1.2f), (POIType.DrownedVillage, 1f), (POIType.Port, 0.8f), (POIType.Wreck, 0.8f), (POIType.Bay, 0.8f),
					(POIType.Mine, 0.7f), (POIType.Temple, 0.6f), (POIType.Town, 0.6f), (POIType.TradingPost, 0.6f), (POIType.Shrine, 0.6f), (POIType.SaltFlat, 0.6f),
					(POIType.Camp, 0.6f), (POIType.Ruins, 0.6f), (POIType.HerbGrove, 0.6f), (POIType.Keep, 1f), (POIType.Dockyard, 0.5f), (POIType.Lighthouse, 0.5f),
					(POIType.Waystation, 0.5f), (POIType.RitualSite, 0.5f), (POIType.Graveyard, 0.5f), (POIType.Barrow, 0.4f), (POIType.Monastery, 0.4f),
					(POIType.Spring, 0.4f), (POIType.AbandonedFarm, 0.4f), (POIType.SunkenShip, 0.3f), (POIType.PirateCove, 0.3f), (POIType.SmugglersCove, 0.3f),
					(POIType.Monument, 0.3f), (POIType.BanditCamp, 0.3f), (POIType.HuntingLodge, 0.3f), (POIType.MonsterDen, 0.3f), (POIType.Battlefield, 0.3f),
					(POIType.Windmill, 0.2f), (POIType.Hermitage, 0.2f), (POIType.WitchHut, 0.2f), (POIType.Statue, 0.2f), (POIType.City, 0.1f), (POIType.Castle, 0.05f),
					(POIType.Capital, 0.02f), (POIType.River, 1f), (POIType.Lake, 1f), (POIType.Bridge, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HauntedManor, 0.6f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 1f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f),
					(POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GoblinMarket, 0.5f),
					(POIType.SerpentIdol, 0.6f), (POIType.LizardfolkVillage, 1f), (POIType.Eyrie, 0.8f), (POIType.RuneStone, 0.6f),
					(POIType.Crannog, 0.8f), (POIType.HagsCauldron, 1.2f), (POIType.HydraLair, 1.2f), (POIType.ReedVillage, 0.8f),
					(POIType.CovenCircle, 1f), (POIType.LizardfolkHatchery, 1f), (POIType.TidalIslandAbbey, 1f), (POIType.CliffCastle, 1.2f),
					(POIType.SmugglersTunnel, 1f), (POIType.WreckersBeacon, 1f), (POIType.SirenRock, 1f), (POIType.SeaWitchGrotto, 0.8f),
					(POIType.LongshipLanding, 1f), (POIType.KrakenWreck, 0.8f), (POIType.LadyOfTheLake, 0.4f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Longhouse", 1f)
				.Prefers(POIType.Village, "Timber", 0.6f)
				.Prefers(POIType.Village, "Stone", 0.2f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1f)
				.Prefers(POIType.Town, "Stone", 0.6f)
				.Prefers(POIType.Port, "Timber", 1.6f)
				.Prefers(POIType.Port, "Stone", 0.5f)
				.Prefers(POIType.FishingCamp, "Shore", 1.4f)
				.Prefers(POIType.FishingCamp, "Huts", 0.4f)
				.Prefers(POIType.Camp, "Lean-tos", 1f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Keep, "Stone", 1.5f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Temple, "Court", 1.2f)
				.Prefers(POIType.Graveyard, "Earthen", 1.4f)
				.Prefers(POIType.Barrow, "Ringed", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.ReedVillage, "Marsh Town", 1.3f)
				// (the fantasy wave's variants end here)
				.Favours(BiomeStructureStyle.Earth, 1.4f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Timber, 0.8f)
				.Favours(BiomeStructureStyle.Hide, 0.2f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: blue-grey clay-sod walls, wattle-and-daub, dense reed thatch (hipped), tarred oak, Rhenish tuff ashlar and
				// big monastic brick, grey-brown Frisian wool striped madder and woad.
				// LATER (NEW-SURFACES U1/U6): KleiSod, FrisianReet, Tuffstone, Moppen, MonkNun, Fries, Barksail as surfaces of their own;
				// the kit's earth, reed, stone and shingle materials, tinted, stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, SeaHex("#6a6c5c"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, SeaHex("#a8a08a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, SeaHex("#8a8466"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, SeaHex("#5a5c50"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#1e1a14"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#8e8a76"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, SeaHex("#7a3a26"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Adobe, SeaHex("#7a3a26"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, SeaHex("#a8502a"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, SeaHex("#6a6458"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Adobe, SeaHex("#8a4430"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#1e1a14"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, SeaHex("#8a8466"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, SeaHex("#7a4a2a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, SeaHex("#7a4a2a"), pitch: BiomeRoofPitch.Pitch30)
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, SeaHex("#6a6458"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, SeaHex("#8a7a52"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Timber, SeaHex("#3a3024"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, SeaHex("#5a5c50"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, SeaHex("#4a5a3a"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Adobe, SeaHex("#8a4430"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, SeaHex("#8e8a76"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#1e1a14"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#3a2e26"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#6a6052"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#8a2a20"))
				// Flax, gold-of-pleasure and madder in the Grüppen's ridge-and-furrow, water in the furrows, only inside the dikes.
				// LATER (I2i SaltPan field surface): salt gardens beside the salt-peat works in the warm band.
				.Crops(("Detail_CropFlax", 0.4f), ("Detail_CropCamelina", 0.3f), ("Detail_CropMadder", 0.2f))
				.CropSurface("Detail_CropFlax", BiomeFieldSurface.RidgeAndFurrow)
				.CropSurface("Detail_CropCamelina", BiomeFieldSurface.RidgeAndFurrow)
				.CropSurface("Detail_CropMadder", BiomeFieldSurface.RidgeAndFurrow)
				// The summer dike round the fields and the ditches along the lanes.
				.Lines(new Entry.Line("Tungardur"), new Entry.Line(BiomeLinearForm.EarthDitch, BiomeLinearPlace.Lanes))
				// Horn-paned fish-oil lanterns on crooks, wall lanterns in the towns, braziers at a keep, cressets on the dikes; the
				// drowned town and its ghosts burn glowjars. LATER (I7a): non-town site lights are laid once I7a places them.
				.Lights(POIType.Village, BiomeWayLightKind.CrookLantern, 16f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.CrookLantern, 16f)
				.Lights(POIType.Waystation, BiomeWayLightKind.CrookLantern, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.WallLantern, 12f)
				.Lights(POIType.Port, BiomeWayLightKind.WallLantern, 12f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Lighthouse, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Shrine, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.Spring, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.DrownedVillage, BiomeWayLightKind.Glowjar, 8f)
				.Lights(POIType.Ruins, BiomeWayLightKind.Glowjar, 8f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 17f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				.Describe("Tidal marsh of the river mouths — grey mud bared by the ebb, sea-lavender saltings cut by creeks, and villages raised on mounds against the flood")
				.Names(onsets: new[] { "Wyk", "Hus", "Dag", "Fan", "Up", "Sie", "Rung", "Hoog", "Nor", "Lang", "Ham", "Jor", "Bal", "Wan" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ie" },
					codas: new[] { "um", "warf", "wierde", "siel", "büll", "koog", "holt", "sum" },
					middles: new[] { "er", "en", "el", "um" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Siel", "Priel", "Koog", "Warf", "Sump", "Vault", "Undercroft", "Holt-cellar" },
					dungeonPrefixes: new[] { "The Drowned", "The Ebb-bared", "The Salt-grey", "The Tide-held", "The Silted", "The Lamplit" },
					poiSuffixes: new[] { "Wierde", "Warft", "Siel", "Koog", "Priel", "Hallig", "Watt", "Stins", "Fething", "Biike" },
					adjectives: new[] { "Ebb-bared", "Salt-grey", "Tide-held", "Drowned", "Mud-silvered", "Reed-roofed" },
					poiWords: new[]
					{
						("village", new[] { "Wierde", "Warft", "Hus" }),
						("fishingcamp", new[] { "Wattle Weir", "Stow Nets", "Shrimpers' Hythe" }),
						("rivermouth", new[] { "Siel", "Mouth", "Priel" }),
						("island", new[] { "Hallig", "Sandbank", "Eiland" }),
						("drownedvillage", new[] { "Rungholt", "Drowned Warft" }),
						("watermill", new[] { "Tide Mill", "Siel Mill" }),
						("port", new[] { "Sielhafen", "Hythe" }),
						("keep", new[] { "Stins", "Tower-house" }),
						("lighthouse", new[] { "Bluese", "Fire-basket" }),
						("mine", new[] { "Salt Sieden", "Darg Pits" }),
						("ritualsite", new[] { "Upstalsboom", "Biike" }),
						("barrow", new[] { "Denghoog", "Mound-grave" }),
						("spring", new[] { "Fosite's Well", "Fething" }),
					},
					about: "Frisian Wadden: free marsh-folk of the tidal coast (Wyk, Hus, Rung, Hoog; -um, -warf, -wierde, -siel, -koog)")
				.Gathers("Glasswort", "Mussel Bank", "Cockle Bed", "Lugworm", "Salt Peat", "Marsh Clay", "Sea Lavender", "Scurvy-grass", "Sea Wormwood", "Vivianite", "Ebbspar")
				.Herbs("Detail_ScurvyGrass", "Detail_SeaWormwood", "Detail_SeaPlantain", "Detail_Glasswort")
				// Saltbloom: white salt efflorescence and a grey clay splash line, orange lichen on the roofs.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Saltbloom, 0.5f, 0.7f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
