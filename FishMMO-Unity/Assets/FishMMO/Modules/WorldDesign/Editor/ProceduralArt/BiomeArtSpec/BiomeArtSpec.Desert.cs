#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Desert</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Desert()
		{
			// The great sand sea and its gravel plains, West Saharan, in the caravan centuries (biome round, approved 2026-10-18, authored
			// 2026-10-26): ochre-gold seas of transverse and barchan dunes rising to star dunes, long seif ridges with pale gassi corridors
			// between, and round them the black reg, a varnished pebble pavement to every horizon; wind-fluted yardangs, Nubian-sandstone
			// mesas streaked with black varnish, a guelta at the head of a dry gorge, a lone desert-date tree every caravan steers by. Its
			// people are veiled caravaneers: red-leather tents, camel trains on braided pistes marked by slab pillars, deep draw-wells, and
			// dune-edge ksour of dry-laid rose slab the dunes are slowly drowning; beneath the sand the tombs of an older people. Its fantasy
			// is the emptiness: the spirits of the void dancing as noon dust-devils, sunglass that drinks the day and glows at night, dunes
			// that boom at dusk, the ribs of a sand-wyrm arching out of a dune. Three reaches by humidity (decision 29): the HYPERARID reg
			// (h < −0.8: yardangs, glass, almost no plants), the ERG core, and the FRINGE (h ≥ −0.6: red sand sheets, taman grass, wadi
			// trees, ephemeral flowers, most settlement). The American cactus kit left Desert (decision 43; offered to High Desert/Scrubland).
			// LATER (I9 T1): every ground and object texture as Desert's own variant (the Nubian sandstone, the varnished basalt ventifacts);
			// until then the grounds are its own recipes and the barks, leaves and stones the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Djinnfire V(0.88, 0.85, 0.72) and Tagelmust V(0.665, 0.80, 0.40).
			return E("Desert", L(Ground.SandErg,
					// The erg: drinn tussocks building their mounds, a little taman, had cushions, arta switches over the sand.
					Band(Grass("GrassDrinn", 3f, 180), -1f, 1f, -0.8f, 1f), Band(Grass("GrassTaman", 1f, 170), -1f, 1f, -0.7f, 1f),
					Plants("Had", 2f), Band(Bush("Arta", 0.4f), -1f, 1f, -0.85f, 1f),
					// Sand lilies and colocynth runners at the dune feet; the desert hyacinth beside the shrubs it lives on.
					With(Band(Plants("FlowersSandLily", 1.5f), -1f, 1f, -0.75f, 1f), s => s.Slope = new Vector2(4f, 12f)),
					With(Plants("Colocynth", 1f), s => s.Slope = new Vector2(4f, 12f)),
					Moded(Plants("Cistanche", 0.5f), BiomeScatterMode.UnderShrub("Arta,Markh,Irak", 3f)),
					// Bleached bones, the toll of the crossing, thickest by the wells and halts. LATER (a way-side anchor): along the pistes.
					Plants("DebrisBones", 0.1f, 128), Moded(Plants("DebrisBones", 1f, 128), BiomeScatterMode.NearSites("Waystation,Spring,Camp", 40f)),
					Moded(Plants("DebrisCamel", 6f, 128), BiomeScatterMode.NearSites("Waystation,Spring,Camp,TradingPost", 50f)),
					// The desert date by the wells and in the wadis (the Steering Tree), the toothbrush tree at the wells. LATER (SCATTER-MODES
					// near-water anchor): within 20 m of the wadi beds; the wells stand in.
					Moded(Trees(0.006f, 30f, 25f, "Balanites"), BiomeScatterMode.NearSites("Waystation,Spring,Oasis,Camp", 50f)),
					Moded(Bush("Irak", 0.4f), BiomeScatterMode.NearSites("Waystation,Spring,Oasis", 40f)),
					// A sand-scoured dead desert date on its deflated mound, here and there.
					With(Logs("Balanites snags", 0.002f, 60f, Wood("Snag_Balanites")), r => r.AlignToNormal = false),
					Stones(SmallRocks("Sandstone"), 0.3f)),
				Ds(
					// d0: the slip faces: avalanched sand at its angle of repose (24–36°), drinn only on their lower third.
					D(Ground.SandSlipFace, 48f, 3f, 24f, 36f, With(Band(Grass("GrassDrinn", 2f, 160), -1f, 1f, -0.8f, 1f), s => s.Slope = new Vector2(24f, 30f))),
					// d1: the reg, varnished desert pavement to the horizon, wider in the hyperarid reach: silver-awn tufts on its margins,
					// fagonia mats, zilla tangles, resurrection balls lodged against the stones, wind-faceted pebbles, basalt ventifacts,
					// Ténéré fossil wood broken in rounds, the odd lone atil, the green Sahara's litter where the old lake beds are near.
					// The road across it is the reg track, its pebbles swept aside into two pale sand lanes.
					// LATER (I6f): the faceted dreikanter pebbles (Pebbles_Reg) and the pale sunglass lumps (Pebbles_Sunglass) as stone sets of
					// their own; basalt and quartzite pebbles stand in.
					D(Ground.RegPavement, 120f, 2f, 0f, 8f, Band(Grass("GrassSilverAwn", 10f, 160), -1f, 1f, -0.85f, 1f), Plants("FlowersFagonia", 3f),
						Plants("Zilla", 1f), Plants("RoseOfJericho", 1f), Stones(Pebbles("Basalt"), 4f), Stones(Pebbles("Quartzite"), 2f),
						Band(Stones(Pebbles("Quartzite"), 0.2f), -1f, 1f, -1f, -0.8f),
						Blocks("Ventifacts", 0.03f, 12f, F(RockTypes.Basalt, "Ventifact"), 15f),
						Logs("Petrified logs", 0.006f, 25f, Wood("PetrifiedLog_Tenere"), 20f),
						Band(Trees(0.0008f, 80f, 15f, "Atil"), -1f, 1f, -0.85f, 1f), Plants("DebrisNeolithic", 0.5f, 128),
						Moded(Outcrops("Yardangs", 0.004f, 60f, F(RockTypes.Sandstone, "Yardang"), 20f), YardangWind))
						.Ways(road: Ground.PathRegTrack),
					// d2: the gassi corridors between the seifs (the Seif landform paints them) and the interdune flats: granule ripples,
					// silver-awn and taman, had, markh; after rain the acheb carpets in the rain cells.
					D(Ground.GravelGassi, 96f, 2f, 0f, 6f, Band(Grass("GrassSilverAwn", 8f, 160), -1f, 1f, -0.85f, 1f), Band(Grass("GrassTaman", 3f, 160), -1f, 1f, -0.7f, 1f),
						Plants("Had", 3f), Band(Bush("Markh", 0.3f), -1f, 1f, -0.75f, 1f), AchebCarpet(),
						Stones(Pebbles("Basalt"), 2f)),
					// d3: varnished sandstone where the dune sand is thin and on the steeps: cross-bedded blocks, gour mushroom rocks, the big
					// yardangs along the wind, varnished boulders at the feet, fagonia and resurrection balls against them; the fringe's
					// granite inselbergs.
					D(Ground.SandstoneVarnished, 48f, 3f, 20f, 90f, Blocks("Cross-bedded sandstone", 0.02f, 14f, F(RockTypes.Sandstone, "CrossBedded"), 40f),
						Outcrops("Gour mushroom rocks", 0.006f, 40f, F(RockTypes.Sandstone, "Gour"), 30f),
						Moded(Outcrops("Great yardangs", 0.004f, 60f, F(RockTypes.Sandstone, "Yardang"), 25f), YardangWind),
						Outcrops("Sandstone ledges", 0.01f, 24f, F(RockTypes.Sandstone, "Ledges"), 40f),
						Boulders(0.05f, 10f, VarnishedBoulders), Stones(SmallRocks("Sandstone"), 2f),
						Moded(Plants("FlowersFagonia", 2f), BiomeScatterMode.CliffFoot(8f)), Moded(Plants("RoseOfJericho", 2f), BiomeScatterMode.CliffFoot(6f)),
						Band(Outcrops("Granite inselbergs", 0.002f, 120f, F(RockTypes.Granite, "Tor", "Corestone"), 30f), -1f, 1f, -0.6f, 1f)),
					// d4: the palaeolake marl of the green Sahara (the Paleolake landform paints it): diatomite, the Neolithic surface scatter of
					// querns, axes, comb-marked sherds and ostrich shell; acheb carpets after rain.
					With(D(Ground.ClayPaleolakeMarl, 160f, 2f, 0f, 3f, Plants("DebrisNeolithic", 3f, 128), AchebCarpet()), l => l.Height = new Vector2(0.45f, 0.47f)),
					// d5: the fringe's red sand sheets, grass-held: taman, drinn and silver-awn, had, arta and markh on their mounds, sand
					// lilies, colocynth, the rain cells' acheb, lone atil and the wadi trees.
					D(Ground.SandRedSheet, 80f, 2f, 0f, 12f, Grass("GrassTaman", 6f, 200), Grass("GrassDrinn", 4f, 180), Grass("GrassSilverAwn", 6f, 160),
						Plants("Had", 1f), Bush("Arta", 0.8f), Bush("Markh", 0.5f), Plants("FlowersSandLily", 1f), Plants("Colocynth", 1.5f), AchebCarpet(),
						Moded(Plants("Cistanche", 0.5f), BiomeScatterMode.UnderShrub("Arta,Markh,Irak", 3f)), Plants("Zilla", 1f),
						Trees(0.002f, 50f, 15f, "Atil"), Trees(0.003f, 30f, 15f, "Balanites"), Bush("Irak", 0.3f)).Humid(-0.6f, 1f)),
				C(Ground.CliffNubian, 40f), Ground.ClayGueltaOoze, Ground.GravelWadi)
				// The American kit the round retired (saguaro, Joshua tree, cholla, ocotillo, creosote, barrel cactus, the generic dune grass
				// and desert flowers, the sandstone layer's pedestals and boulders, the reg's basalt chips), switched off where the tool wrote
				// them and nobody tuned them since.
				.Moved("main", "Trees: Saguaro", "CactusBarrel", "ShrubDry", "Bush: Creosote", "Trees: JoshuaTree", "Cholla", "Bush: Ocotillo", "GrassDune",
					"FlowersDesert")
				.Moved("detail/0", "Boulders", "Formations: Sandstone pedestals")
				.Moved("detail/2", "Detail_Rocks_Basalt", "Formations: Ventifacts")
				.Ways(path: Ground.PathCamelPad, road: Ground.PathCaravanTrack, cobbles: Ground.FlagstoneSlabPaving)
				.Bars(sand: Ground.SandWadiFlood, gravel: Ground.GravelWadiBar)
				// The Frozen variant (the template's): a frost-glazed erg, white hoar in the ripples.
				.ClimateGround("Frozen", L(Ground.SandFrostErg, Band(Grass("GrassDrinn", 1f, 140), -1f, 1f, -0.8f, 1f)))
				// Own rock the Nubian sandstone (its formations); approved: accepts the reg's quartzite, the inselbergs' granite and the
				// ventifacts' basalt. Shale, conglomerate and limestone dropped: canyon walls fall back to sandstone.
				.Accepts(RockTypes.Sandstone, RockTypes.Quartzite, RockTypes.Granite, RockTypes.Basalt)
				// Sandstone caves, rock shelters and copper mines; lava tubes in basalt. LATER (I9 S3): sandstone dripstone at 0.1× (dry),
				// honeycomb tafoni and sand drifts on the cave floors; the copper-green seams of the mines.
				.CaveShell(CaveForm.Cave, "Sandstone").CaveShell(CaveForm.Grotto, "Sandstone").CaveShell(CaveForm.Mine, "Sandstone")
				.CaveShell(CaveForm.LavaTube, "Basalt")
				// Malachite crusts in the copper mines; sunglass, the desert glass that drinks the day, glowing warm after dusk and lining
				// the ways it lights.
				.Crystals((BiomeCaveCrystalKind.Malachite, 0.45f), (BiomeCaveCrystalKind.Sunglass, 0.55f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.AfterSunset, warm: true)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.AfterSunset, warm: true)
				// The dune mix (star dunes in the deepest sand, seifs with their gassi corridors, the aeolian transverse and barchan
				// fields), the nebkha mounds of the fringe, the gueltas under dry falls and the palaeolake floor.
				.Landforms("StarDune", "Seif", "TransverseDunes", "Barchans", "Nebkha", "Guelta", "Paleolake")
				.Look(BiomeLookClass.Desert)
				// Approved: the buried towns' and tombs' life (owls, snakes, scorpions, bats in the tomb halls) beside the desert's.
				.Wildlife(BiomeHabitat.Desert | BiomeHabitat.Ruins)
				// LATER (AMBIENT-LIFE): dromedary caravans strung along the pistes on the shared clock (I18), addax and dorcas gazelle, the
				// fennec at dusk, jerboas, desert monitor, horned vipers' sidewinder tracks, deathstalkers, darkling-beetle tracks on the dawn
				// dunes, ostrich pairs, lappet-faced vultures, ravens, sandgrouse at the wells at dawn; the void-spirits' noon whirls (WEATHER
				// O7), stronger in their stone rings, and very rarely a ripple-wave running against the wind across a dune field (the wyrm).
				// LATER (I7 I26): the booming dune's dusk roar on the shared clock.
				.Moat(BiomeMoatWater.Dry)
				// The guelta's water: clear olive-jade over black silt, rock-shadowed.
				.WaterTint(new BiomeWaterLook(ErgHex("#8c8c60"), ErgHex("#1e281e"), new Vector3(0.30f, 0.22f, 0.26f), 0.15f, ErgHex("#b09a70"), ErgHex("#f0ece0")))
				.Blades("GrassDrinn", t => UplandBlade(t, "#6a7a68", "#c8bc94", 0.2f, stiffness: 2f, width: 0.6f, clump: 0.8f,
					head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.3f, heads: new[] { "#ece6d6" }))
				.Blades("GrassTaman", t => UplandBlade(t, "#5a6a5a", "#8aa08a", 0.25f, stiffness: 1.5f, width: 0.8f, clump: 0.6f,
					head: GrassBladeHead.Plume, headSize: 0.06f, headShare: 0.4f, heads: new[] { "#9a7a8a" }))
				.Blades("GrassSilverAwn", t => UplandBlade(t, "#8a8a6a", "#c8c09a", 0.35f, width: 0.3f, clump: 0.3f,
					head: GrassBladeHead.Plume, headSize: 0.05f, headShare: 0.6f, heads: new[] { "#f2ece0" }))
				.Blades("FlowersSchouwia", t => UplandBlade(t, "#5a7a6a", "#6a8a78", 0.35f, head: GrassBladeHead.Flower, sprinkle: 16f, headSize: 0.04f,
					heads: new[] { "#8a3aa8", "#b070c8" }))
				.Blades("FlowersFagonia", t => UplandBlade(t, "#4a6a3a", "#5a7a44", 0.4f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.025f,
					heads: new[] { "#c060a8" }))
				.Blades("FlowersSandLily", t => UplandBlade(t, "#7a8a78", "#8a9a88", 0.3f, head: GrassBladeHead.Flower, sprinkle: 3f, headSize: 0.07f,
					heads: new[] { "#f6f2ea" }))
				.Describe("The great sand sea — star dunes and black gravel plains, slab-stone towns the dunes are drowning, leather-tent caravans steering by wells and cairns, and the tombs of an older people beneath the sand")
				.Names(onsets: new[] { "Tin", "Ta", "Ti", "In", "Ag", "Az", "Tad", "Tiz", "Ten", "Ou", "Chin", "Am", "Ig", "Ar", "Ezz" },
					nuclei: new[] { "a", "e", "i", "ou", "aa" },
					codas: new[] { "ert", "ane", "akh", "ouft", "ess", "ar", "ezzaf", "et" },
					middles: new[] { "ad", "er", "il", "ag" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Tomb", "Tumulus", "Buried Hall", "Well", "Shaft", "Gallery", "Pyramid" },
					dungeonPrefixes: new[] { "The Drowned", "The Empty", "The Booming", "The Veiled", "The Sun-glassed", "The Wyrm's" },
					poiSuffixes: new[] { "Erg", "Reg", "Ksar", "Guelta", "Alam", "Ehan", "Ribat", "Draa" },
					adjectives: new[] { "Sand-drowned", "Varnished", "Empty", "Booming", "Veiled", "Sun-glassed", "Star-duned", "Wyrm-ribbed" },
					poiWords: new[]
					{
						("dunesea", new[] { "Erg", "Draa", "Sand Sea" }),
						("camp", new[] { "Ehan", "Tent Camp", "Halt" }),
						("waystation", new[] { "Draw Well", "Ribat", "Well-halt" }),
						("village", new[] { "Ksar", "Slab Town" }),
						("town", new[] { "Ksar", "Walled Ksar" }),
						("keep", new[] { "Ribat" }),
						("fortress", new[] { "Ribat", "Great Ribat" }),
						("tradingpost", new[] { "Salt Market", "Caravan Market" }),
						("spring", new[] { "Guelta", "Well" }),
						("barrow", new[] { "Antenna Tumulus", "Keyhole Tumulus" }),
						("crypt", new[] { "Buried Tomb", "Sand Pyramid" }),
						("ruins", new[] { "Drowned Ksar", "Dune-swallowed Town" }),
						("ancientroad", new[] { "Alam Trace", "Cairn Road" }),
						("monument", new[] { "Rock-art Wall", "Engraved Face" }),
						("shrine", new[] { "Saint's Dome", "White Dome" }),
						("feyring", new[] { "Ring of the Empty" }),
						("fallenstar", new[] { "Glass Field" }),
						("monsterden", new[] { "Wyrm Ribs" }),
					},
					about: "Saharan caravaneers' voice (Tin, Ta, Ag, Tad, Tiz, Ezz; -ert, -ane, -akh, -ouft, -ezzaf): the erg and the reg")
				// Caravan country: camps, wells and halts everywhere, tombs and drowned ruins, a ksar here and there, ribats on the routes;
				// no mills, timber, coasts, wetlands or sunken kinds (approved as drafted). The landform kinds keep the rule's 1.
				.Sites((POIType.DuneSea, 2f), (POIType.Camp, 1.6f), (POIType.Waystation, 1.6f), (POIType.Crypt, 1.4f), (POIType.Ruins, 1.4f),
					(POIType.Barrow, 1.4f), (POIType.AncientRoad, 1.4f), (POIType.Landmark, 1.4f), (POIType.TradingPost, 1.2f), (POIType.Overhang, 1.2f),
					(POIType.DungeonEntrance, 1.2f), (POIType.Monument, 1f), (POIType.Shrine, 1f), (POIType.BanditCamp, 1f), (POIType.Graveyard, 0.8f),
					(POIType.FallenStar, 0.8f), (POIType.Keep, 1f), (POIType.Village, 0.8f), (POIType.Town, 0.8f), (POIType.MonsterDen, 0.8f),
					(POIType.Mesa, 0.6f), (POIType.Butte, 0.6f), (POIType.Statue, 0.6f), (POIType.Battlefield, 0.6f), (POIType.RitualSite, 0.6f),
					(POIType.FeyRing, 0.6f), (POIType.BossLair, 0.6f), (POIType.Hermitage, 0.6f), (POIType.Oasis, 0.5f), (POIType.StoneCircle, 0.5f),
					(POIType.Fortress, 0.4f), (POIType.Obelisk, 0.4f), (POIType.NaturalArch, 0.4f), (POIType.Gorge, 0.4f), (POIType.Nest, 0.4f),
					(POIType.Quarry, 0.4f), (POIType.Mine, 0.4f), (POIType.City, 0.3f), (POIType.OreVein, 0.3f), (POIType.CrystalFormation, 0.3f),
					(POIType.Castle, 0.2f), (POIType.Spring, 0.2f), (POIType.HerbGrove, 0.2f), (POIType.Portal, 0.2f), (POIType.LeyNexus, 0.2f),
					(POIType.Temple, 0.2f), (POIType.Capital, 0.1f), (POIType.River, 0.1f), (POIType.Lake, 0.05f),
					(POIType.Cave, 1f), (POIType.Grotto, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.Bridge, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f),
					(POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f),
					(POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f), (POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1.2f),
					(POIType.Pyramid, 1.4f), (POIType.Sphinx, 1.2f), (POIType.RockCutFacade, 0.6f), (POIType.SandBuriedTemple, 1.4f),
					(POIType.DjinnPrison, 1f), (POIType.SandwormPit, 1.2f), (POIType.CaravanHalt, 1.2f), (POIType.BeastBoneyard, 1.2f),
					(POIType.FireTemple, 0.4f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Town, "Stone", 2f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.City, "Stone", 1f)
				.Prefers(POIType.City, "Timber", 0f)
				.Prefers(POIType.Capital, "Stone", 1f)
				.Prefers(POIType.Capital, "Timber", 0f)
				.Prefers(POIType.Keep, "Stone", 2f)
				.Prefers(POIType.TradingPost, "Caravanserai", 2f)
				.Prefers(POIType.TradingPost, "Market", 0.3f)
				.Prefers(POIType.Camp, "Hide Tents", 2f)
				.Prefers(POIType.Camp, "Crude", 0.6f)
				.Prefers(POIType.Camp, "Lean-tos", 0f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.BanditCamp, "Open", 1f)
				.Prefers(POIType.BanditCamp, "Palisade", 0f)
				.Prefers(POIType.Barrow, "Ringed", 2f)
				.Prefers(POIType.Barrow, "Excavated", 0.2f)
				.Prefers(POIType.Graveyard, "Earthen", 1.6f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Crypt, "Tomb", 2f)
				.Prefers(POIType.Waystation, "Stone Inn", 2f)
				.Prefers(POIType.Waystation, "Inn", 0f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Fortress, "Stone", 1.2f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Castle, "Stone", 0.4f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Ruins, "Burned", 0.2f)
				.Prefers(POIType.Ruins, "Temple", 0.5f)
				.Prefers(POIType.Ruins, "Excavation", 0.6f)
				.Prefers(POIType.AncientRoad, "Colonnade", 0.2f)
				.Prefers(POIType.Monument, "Obelisk", 1.4f)
				.Prefers(POIType.Monument, "Statue", 0.3f)
				.Prefers(POIType.Shrine, "Shrine", 1.6f)
				.Prefers(POIType.Shrine, "Statue", 0.3f)
				.Prefers(POIType.Hermitage, "Stone", 1.6f)
				.Prefers(POIType.Hermitage, "Cottage", 0f)
				.Prefers(POIType.FeyRing, "Ring", 1.4f)
				.Prefers(POIType.FallenStar, "Crater", 1.6f)
				.Prefers(POIType.Mine, "Adit", 1.4f)
				.Prefers(POIType.Mine, "Shaft Head", 0.2f)
				.Prefers(POIType.MonsterDen, "Bones", 1.2f)
				.Prefers(POIType.MonsterDen, "Lair", 0.6f)
				.Prefers(POIType.Temple, "Court", 0.6f)
				.Prefers(POIType.Temple, "Colonnade", 0.4f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.6f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0.3f)
				.Prefers(POIType.Ruins, "Stone", 2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Hoard", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.4f)
				.Prefers(POIType.TollGate, "Toll House", 1.2f)
				.Prefers(POIType.Hamlet, "Stone Stead", 1.4f)
				.Prefers(POIType.BurnedVillage, "Cinders", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.4f)
				.Prefers(POIType.LostLibrary, "Dry Athenaeum", 1.4f)
				.Prefers(POIType.FireTemple, "Colonnade", 1.3f)
				.Prefers(POIType.NomadCamp, "Black Tents", 1.4f)
				.Prefers(POIType.Necropolis, "City of the Dead", 1.3f)
				.Prefers(POIType.Pyramid, "Smooth-sided", 1.3f)
				.Prefers(POIType.CaravanHalt, "Well Camp", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Desert's signature pieces and their variants — the slab ksar with its ostrich-egg tower (Village/Town "Slab Ksar"
				// 2.0/1.6, "Red Banco" 1.0), the ribat (Keep 1.8, Fortress 1.2, Waystation 0.8), the ehan camp and the azalai halt (Camp 2.0/1.2),
				// the draw well and its draw-track (Waystation 2.0), the alam pillars (AncientRoad "Alam Trace" 2.0), the antenna tumuli
				// (Barrow 2.0, Graveyard 1.0), the buried tomb half-drowned by a dune (Crypt 2.0, DungeonEntrance 1.6), the dune-swallowed ksar
				// (Ruins 2.0), the rock-art panel with its glowing dot-and-line glyphs (Monument/Landmark 1.4; RuneStone light), the library
				// house, the saint's dome with its rag-tied tree (Shrine 1.4, Hermitage 1.0), the salt market (TradingPost 1.6), the copper
				// furnaces (Mine 1.4), the Ring of the Empty (FeyRing 1.4), the booming dune (I26 audio cue), the glass field (FallenStar 1.6)
				// and the wyrm ribs (MonsterDen 0.8). Until then the kit's variants below stand in at the draft's weights.
				.Favours(BiomeStructureStyle.Stone, 1.6f)
				.Favours(BiomeStructureStyle.Hide, 1.4f)
				.Favours(BiomeStructureStyle.Earth, 1f)
				.Favours(BiomeStructureStyle.Timber, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: dry-laid rose sandstone slabs under red-ochre clay render, flat clay roofs on acacia beams with stepped
				// parapets, single-slab lintels, studded acacia doors, beaten clay floors, slab drystone fences, black iron with brass,
				// glazed indigo cloth and indigo banners striped white and saffron; the caravan towns' red banco walls; red-leather tents
				// on arched frames over woven grass screens. LATER (NEW-SURFACES U3/U6/U8): SlabStone, RedBanco, MatClay, AcaciaStudded,
				// NicheCourse, BancoRelief, IndigoCloth, TentLeather and EsaberMat as Desert's own surfaces; the shared ones tinted stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone, ErgHex("#b0703e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, ErgHex("#9a3a22"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ErgHex("#8a5a3a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, ErgHex("#8a5a3a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Sandstone, ErgHex("#a0603a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Sandstone, ErgHex("#c08a5a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ErgHex("#6a4228"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, ErgHex("#a07a50"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ErgHex("#9a3a22"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, ErgHex("#a8482a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ErgHex("#8a5a3a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, ErgHex("#8a5a3a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Sandstone, ErgHex("#a0603a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ErgHex("#6a4228"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed, ErgHex("#d8c08a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, ErgHex("#8a3420"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Canvas, ErgHex("#a8482a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, ErgHex("#c49c6c"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, ErgHex("#8a6a4a"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Sandstone, ErgHex("#c08a5a"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Adobe, ErgHex("#9a3a22"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Sandstone, ErgHex("#a0603a"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Adobe, ErgHex("#8a5a3a"), pitch: BiomeRoofPitch.Flat)
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Plaster, ErgHex("#ece6da"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, ErgHex("#8a6a48"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, ErgHex("#8a6a48"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, ErgHex("#2a2622"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ErgHex("#1e2a4a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, ErgHex("#1e2a4a"))
				// Drifted: sand banked to the sills on the windward side, tongues through the doorways, the lower courses scoured pale.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Drifted, 0.6f, 1f)
				// No fields (approved: the gardens are the Oasis's).
				.NoCrops()
				// LATER (I7): the alam pillars of stacked slabs every 300–600 m along the caravan ways, a camel skull on some; a cairn
				// marker line at that spacing stands in.
				.Lines(AlamLine())
				// Pierced terracotta lamp-jars on the street walls of the ksour, cressets on the ribats, one brazier at the well, a jar at
				// the shrine door, sunglass posts at the rings, glass fields and landmarks; the camps go unlit (the hearth only).
				.Lights(POIType.Village, BiomeWayLightKind.PiercedJar, 18f)
				.Lights(POIType.Town, BiomeWayLightKind.PiercedJar, 18f)
				.Lights(POIType.City, BiomeWayLightKind.PiercedJar, 18f)
				.Lights(POIType.Capital, BiomeWayLightKind.PiercedJar, 18f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.PiercedJar, 16f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Waystation, BiomeWayLightKind.Brazier, 40f)
				.Lights(POIType.Shrine, BiomeWayLightKind.PiercedJar, 40f)
				.Lights(POIType.Hermitage, BiomeWayLightKind.PiercedJar, 40f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.CrystalPost, 12f)
				.Lights(POIType.FallenStar, BiomeWayLightKind.CrystalPost, 12f)
				.Lights(POIType.Landmark, BiomeWayLightKind.CrystalPost, 16f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.WallTorch)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 14f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.WallTorch)
				// Twining desert milkweed on the ruins, the odd cave mouth and the desert dates; no ivy or creeper.
				.GrowsVines(0.05f, 0.15f, 0f, 0.08f, ("Pergularia", 1f))
				.Gathers("Copper Lode", "Sunglass Lump", "Malachite Crust", "Petrified Wood Round", "Ostrich Eggshell", "Colocynth Gourd", "Cistanche Spike",
					"Rose of Jericho", "Terfez Truffle", "Balanites Date", "Markh Fibre", "Irak Stick", "Camel Bone")
				// LATER (I9b P29): the rose of Jericho's opened green state inside the rain cells and the truffles' cracked mounds after rain.
				.Herbs("Detail_Cistanche", "Detail_Colocynth", "Detail_RoseOfJericho", "Detail_Had")
				.ErodesAs("Erg")
				.Loose("Silicate Sand");
		}

		/// <summary>The yardangs' wind alignment: their long axes down the scene's wind (Desert's ridges of both sizes).</summary>
		private static BiomeScatterMode YardangWind => default(BiomeScatterMode).Faced(BiomeScatterFacing.Wind, 0f, 6f);

		/// <summary>
		/// The acheb: Schouwia's violet carpets, only inside the rain cells (decision 43; SCATTER-MODES Z18), none outside them.
		/// LATER (I9b P29): the bloom's timing after the weather driver's rain on the shared clock; the cells stand bloomed.
		/// </summary>
		private static Scatter AchebCarpet() => Moded(Band(Plants("FlowersSchouwia", 10f, 200), -1f, 1f, -0.75f, 1f), BiomeScatterMode.RainCellClusters(200f, 0.3f));

		/// <summary>The varnished sandstone boulders (Desert's; LATER (I9 T3): their black varnish tops): round and slab.</summary>
		private static string[] VarnishedBoulders => new[] { Boulder("Sandstone", "Round"), Boulder("Sandstone", "Slab") };

		/// <summary>The alam stand-in: a cairn marker every 400 m along the ways, one side.</summary>
		private static Entry.Line AlamLine()
		{
			var line = new Entry.Line(BiomeLinearForm.Cairn, BiomeLinearPlace.Lanes);
			line.Style.Spacing = 400f;
			line.Style.Height = 2f;
			line.Style.Sides = BiomeLinearSides.One;
			return line;
		}

		/// <summary>An sRGB colour from #rrggbb, for the Desert, Badlands and Salt Flat files.</summary>
		private static Color ErgHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
