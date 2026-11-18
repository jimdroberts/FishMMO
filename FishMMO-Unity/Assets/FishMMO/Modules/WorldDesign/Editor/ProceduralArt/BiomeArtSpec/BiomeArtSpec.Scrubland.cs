#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Scrubland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Scrubland()
		{
			// The Sonoran desert scrub of the canal builders (biome round, approved 2026-10-25, authored 2026-10-26): green bajadas of
			// rotted pink granite under banded mylonite fronts, many-armed saguaros among green-barked palo verdes, braided washes lined
			// with ironwood, desert willow and mesquite bosques, white caliche benches at every terrace edge, evenly spaced creosote on
			// the pale flats, black malpaís and one great maar. Two reaches by the climate bands (decision 29): the UPLAND (h ≥ −0.3,
			// t < 0.8: saguaro–palo verde, grasses, canals, towns) and the LOWER COLORADO (the drier flats: creosote–white bursage, sand
			// verbena); organ pipe and elephant tree only where it never frosts (t ≥ 0.75). The Sahel kit and the maquis it once grew
			// (acacia stands, olive, cistus, thornbush, sagebrush, yucca, sandstone beds) are declined back to the catalogue (decision 77).
			// LATER (I9 T1): every ground and object texture as Scrubland's own variant (Granite "Grus", Gneiss "Mylonite", Basalt
			// "Malpaís", Creosote, Ocotillo and Saguaro as its own looks); until then the grounds are its own recipes, the rest the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Pitahaya V(0.98, 0.45, 0.48) and Chubasco V(0.28, 0.35, 0.30).
			// LATER (the draft's LOWER reach is h < −0.3 OR t ≥ 0.8): its rules ride the dry band only; the hottest arm's species
			// (organ pipe, elephant tree) carry their own temperature band.
			return E("Scrubland", L(Ground.SoilBajadaLoam,
					// The bajada's grasses under and between the shrubs: bush muhly growing up inside the bursage and jojoba (SCATTER-MODES
					// shelter), three-awn and cottontop in the open, six-weeks grama in the swales.
					Moded(SonoranUpland(Grass("GrassBushMuhly", 4f, 160)), BiomeScatterMode.UnderShrub("Bursage,Jojoba,Creosote")),
					SonoranUpland(Grass("GrassThreeAwn", 6f, 180)), SonoranUpland(Grass("GrassCottontop", 5f, 180)), Grass("GrassSixWeeks", 4f, 160),
					// The spring look: gold poppy in broad patches (counted flowers, grouped 8 m) with lupine through them, desert marigold at
					// the roadsides, chia.
					SonoranUpland(Plants("FlowersGoldPoppy", 6f)),
					SonoranUpland(Plants("FlowersLupine", 4f)), Plants("FlowersDesertMarigold", 2f), Plants("HerbChia", 1f),
					// The matrix shrubs: bursage everywhere on the bajada, creosote evenly spaced (decision 77: its root zones keep the
					// others off), ocotillo.
					SonoranUpland(Bush("Bursage", 8f)), Moded(Bush("Creosote", 0.6f), BiomeScatterMode.EvenSpacing(4f)), Bush("Ocotillo", 0.3f),
					// The cacti: teddybear cholla, the fishhook barrel leaning to the sun (decision 77), the Engelmann pear.
					Plants("ChollaTeddybear", 0.5f), Moded(Plants("CactusFishhook", 0.3f), BiomeScatterMode.LeanSunward(14f)), Plants("PadCactusEngelmann", 0.8f),
					// The saguaro forest above the lowest flats, foothill palo verde and whitethorn. LATER (I9b P3): candelabra arms by age;
					// the Warden saguaro is an AncientTree site (I7).
					Band(Trees(0.04f, 14f, 30f, "Saguaro"), -1f, 1f, -0.38f, 1f), SonoranUpland(Trees(0.03f, 10f, 30f, "PaloVerde")),
					Trees(0.01f, 14f, 25f, "Whitethorn"),
					// Saguaro skeletons standing for years, their fallen ribs within a few metres.
					With(Logs("Saguaro skeletons", 0.004f, 40f, Wood("Snag_Saguaro")), r => r.AlignToNormal = false), Plants("DebrisSaguaroRibs", 0.3f),
					// The builders' scatter of red-on-buff sherds and shell, thick round villages and ruins, a trace everywhere.
					Moded(Plants("DebrisSherdsRedBuff", 3f), BiomeScatterMode.NearSites("Village,Town,Ruins,TradingPost", 80f, 0.03f)),
					Stones(Pebbles("Caliche"), 1f)),
				Ds(
					// d0: the creosote flat — pale silty loam of the lower reach and the upland basins, creosote at its even spacing over
					// white bursage, six-weeks grama in the swales, sand verbena, a thin dark pebble lag (not a pavement).
					D(Ground.SiltCreosoteFlat, 96f, 2f, 0f, 6f, Moded(Bush("Creosote", 1.32f), BiomeScatterMode.EvenSpacing(4f)), SonoranLower(Bush("BursageWhite", 6f)),
						Grass("GrassSixWeeks", 8f, 200), SonoranLower(Plants("FlowersSandVerbena", 3f)), Plants("FlowersDesertMarigold", 1f),
						Stones(Pebbles("Basalt"), 1.5f)),
					// d1: caliche benches breaking out of the terrace and wash edges: nodules, bench lips and broken hardpan plates, the
					// Engelmann pear and three-awn on them. The causeways across the benches are paved in caliche setts.
					D(Ground.CalicheBench, 140f, 3f, 6f, 30f, Stones(Pebbles("Caliche"), 3f), Plants("PadCactusEngelmann", 0.6f), Grass("GrassThreeAwn", 4f, 160),
						Trees(0.01f, 14f, 25f, "Whitethorn"),
						Blocks("Caliche benches", 0.02f, 16f, F(RegionalRocks.Caliche, "Bench", "Plate")),
						Blocks("Caliche scree", 0.01f, 12f, F(RegionalRocks.Caliche, "Scree"))).Ways(road: Ground.FlagstoneCalicheSetts),
					// d2: the grus slopes, the saguaro ground: young saguaros only under nurse trees (decision 77), palo verde, the
					// fishhook barrels and cholla of the south slopes, pincushions under the shrubs, the frost-free reach's organ pipe,
					// spikemoss in the north-facing crevices, brittlebush, jojoba and ocotillo; rotted corestones and split boulders.
					D(Ground.GravelGrusBajada, 64f, 2.5f, 12f, 38f,
						Moded(Trees(0.02f, 8f, 38f, "Saguaro"), BiomeScatterMode.Nursed("PaloVerde,Ironwood,Mesquite", 2f)), SonoranUpland(Trees(0.03f, 10f, 35f, "PaloVerde")),
						Moded(Plants("CactusFishhook", 0.6f), BiomeScatterMode.LeanSunward(14f)), Plants("ChollaTeddybear", 1.5f),
						Moded(Plants("CactusPincushion", 1.2f), BiomeScatterMode.UnderShrub("Bursage,Jojoba,Creosote,Brittlebush", 1.5f)),
						Plants("AgaveHohokam", 0.3f), Band(Plants("OrganPipeSonoran", 0.3f), 0.75f, 1f), Faced(Plants("Spikemoss", 3f), BiomeFacing.Poleward),
						Bush("Brittlebush", 3f), SonoranUpland(Bush("Jojoba", 0.8f)), Bush("Ocotillo", 0.94f),
						SonoranUpland(Grass("GrassCottontop", 2f, 140)), SonoranUpland(Plants("FlowersGoldPoppy", 3f)), Plants("DebrisPackratMidden", 0.15f),
						Band(Trees(0.001f, 60f, 30f, "ElephantTree"), 0.75f, 1f),
						Stones(SmallRocks("Grey"), 2f),
						Blocks("Granite corestones", 0.008f, 30f, F(RockTypes.Granite, "Corestone")),
						Blocks("Granite split boulders", 0.03f, 12f, F(RockTypes.Granite, "Split"))),
					// d3: malpaís — black vesicular flows of the lower reach (the Malpais landform), brittlebush and ocotillo on the hot
					// black rock, the odd elephant tree. Ways across it are lava trails, the stones lifted off and stacked in kerbs.
					D(Ground.BasaltMalpais, 320f, 4f, 0f, 30f, Stones(SmallRocks("Basalt"), 4f), Bush("Brittlebush", 2f), Bush("Ocotillo", 0.94f),
						Blocks("Malpaís blocks", 0.03f, 10f, F(RockTypes.Basalt, "Block", "Knob")), Band(Trees(0.001f, 60f, 30f, "ElephantTree"), 0.75f, 1f))
						.Ways(path: Ground.PathMalpaisTrail),
					// d4: the mesquite bosque by the rivers and washes: velvet mesquite closing over pod litter, ironwood and desert
					// willow on the wash lines, Goodding's willow and carrizo by the water, quailbush, chuparosa and fairy duster.
					// LATER (a river-distance band): the draft's ≤ 120 m from rivers; the layer's noise patches stand in.
					D(Ground.LitterMesquiteBosque, 120f, 2.5f, 0f, 8f, Stand(Was(0.2f, 9f), 15f, W(0.6f, 0.6f, open: 0.15f), "MesquiteVelvet"),
						Plants("DebrisMesquitePods", 6f), Trees(0.01f, 18f, 20f, "Ironwood"), Trees(0.004f, 20f, 15f, "DesertWillow"),
						Trees(0.004f, 25f, 10f, "GooddingWillow"), Plants("ReedsCarrizo", 2f), Bush("Quailbush", 0.94f), Bush("Chuparosa", 0.6f),
						SonoranUpland(Bush("FairyDuster", 0.8f)), Logs("Fallen ironwood", 0.002f, 30f, Wood("FallenLog_Ironwood")))),
				C(Ground.CliffMylonite, 40f), Ground.ClayTank, Ground.SandWashArkose)
				// The old generic kit the round retired (thorn and maquis shrubs, tufts, acacia stands, olive, the soil layer's saltbush,
				// agave and yucca, the rock layer's sandstone boulders and beds), switched off where the tool wrote them and nobody tuned
				// them since.
				.Moved("main", "ShrubDry", "ShrubSmall", "Bush: Sagebrush", "GrassTuft", "Trees: Acacia", "Trees: Mesquite", "Trees: Olive", "Bush: Thornbush",
					"Bush: Cistus", "PadCactus")
				.Moved("detail/0", "Bush: Saltbush", "RosetteAgave", "RosetteYucca")
				.Moved("detail/1", "Boulders", "Formations: Sandstone beds")
				.Ways(path: Ground.PathClearedTrail, road: Ground.PathCanalBank, cobbles: Ground.FlagstoneCalicheSetts)
				.Bars(sand: Ground.SandWashBar, gravel: Ground.GravelWashCobble)
				// Own rock caliche (its formations, approved Q4: a crust rock, not soluble-linked), accepting the bajadas' granite, the
				// mylonite fronts' gneiss, the malpaís basalt and the grottoes' limestone; sandstone, shale and conglomerate dropped.
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Basalt, RockTypes.Limestone)
				.CaveShell(CaveForm.Cave, "Granite").CaveShell(CaveForm.Grotto, "Limestone").CaveShell(CaveForm.Mine, "Gneiss")
				.CaveShell(CaveForm.LavaTube, "Basalt").CaveShell(CaveForm.IceCave, "Ice")
				// Peridot in the basalt bombs; sunshell, the fantasy shell-crystal keeping the day's light until midnight (decision 58's
				// AfterSunset, warm), lining the ways it lights.
				.Crystals((BiomeCaveCrystalKind.Peridot, 0.6f), (BiomeCaveCrystalKind.Sunshell, 0.4f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.AfterSunset, warm: true)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.AfterSunset, warm: true)
				// The coalescing fans off the mountain fronts, the planed pediment below them, the braided benched washes, the black
				// flows and one great maar; the oval courts' sunken pads; the platform mounds (Plains' carve, the walled caliche
				// dressing its own: LATER (I7)).
				.Landforms("Bajada", "Pediment", "BraidedWash", "Malpais", "Maar", "OvalCourt", "PlatformMound")
				.Look(BiomeLookClass.Arid)
				// LATER (I9 T13): the intaglios as ground paint on the lower reach's lag (≤ 2 a scene).
				.Blades("GrassBushMuhly", t => AridBlade(t, "#6a6a4a", "#a8a080", 0.35f, width: 0.5f, clump: 0.5f, head: GrassBladeHead.Plume, headSize: 0.12f,
					headShare: 0.7f, heads: new[] { "#c8a0a8" }))
				.Blades("GrassThreeAwn", t => AridBlade(t, "#6a5a4a", "#c0a888", 0.4f, width: 0.6f, head: GrassBladeHead.Plume, headSize: 0.06f, headShare: 0.5f,
					heads: new[] { "#a07a8a" }))
				.Blades("GrassCottontop", t => AridBlade(t, "#5a6a48", "#a8a888", 0.35f, head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.5f,
					heads: new[] { "#f0ece4" }))
				.Blades("GrassSixWeeks", t => AridBlade(t, "#6a7a4a", "#b8b088", 0.5f, heightScale: 0.5f, head: GrassBladeHead.SeedHead, headSize: 0.03f,
					headShare: 0.6f, heads: new[] { "#b8a888" }))
				.Blades("ReedsCarrizo", t => AridBlade(t, "#6a7a5a", "#8a9a6a", 0.3f, head: GrassBladeHead.Plume, headSize: 0.3f, headShare: 1f,
					heads: new[] { "#8a6a6a" }))
				.Blades("FlowersGoldPoppy", t => AridBlade(t, "#6a8a7a", "#7a9a8a", 0.25f, head: GrassBladeHead.Flower, sprinkle: 24f, headSize: 0.035f,
					heads: new[] { "#f0a020", "#f8c838", "#e88a18", "#f4b830" }))
				.Blades("FlowersLupine", t => AridBlade(t, "#5a7a4a", "#6a8a5a", 0.3f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.03f,
					heads: new[] { "#5a5ad0", "#8a7ae0", "#6a6ad8", "#7a5ac8" }))
				.Blades("FlowersDesertMarigold", t => AridBlade(t, "#a8b0a0", "#c0c6b8", 0.3f, head: GrassBladeHead.Flower, sprinkle: 3f, headSize: 0.035f,
					heads: new[] { "#f0d838", "#e8cc30" }))
				.Blades("FlowersSandVerbena", t => AridBlade(t, "#5a7a4a", "#6a8a52", 0.35f, head: GrassBladeHead.Flower, sprinkle: 4f, headSize: 0.04f,
					heads: new[] { "#c060b0", "#d070c0" }))
				.Blades("GrassMaizeCanal", t => AridBlade(t, "#4a6a2a", "#8a9a4a", 0.3f, width: 2f, head: GrassBladeHead.SeedHead, headSize: 0.12f, headShare: 0.6f,
					heads: new[] { "#d8c070" }))
				.Blades("FlowersCottonCanal", t => AridBlade(t, "#4a6a3a", "#5a7a44", 0.3f, head: GrassBladeHead.Flower, sprinkle: 20f, headSize: 0.04f,
					heads: new[] { "#f4f2ec", "#ece8e0" }))
				.Blades("GrassLittleBarley", t => AridBlade(t, "#8a9a5a", "#d0b870", 0.3f, head: GrassBladeHead.SeedHead, headSize: 0.05f, headShare: 0.7f,
					heads: new[] { "#d8b860" }))
				// The lower flats' desert creatures as well as the scrub's (approved: Scrub | Desert).
				// LATER (AMBIENT-LIFE): Gila woodpecker and gilded flicker at the saguaro holes, elf owl at dusk, cactus wren (football
				// nests as cholla dressing), Gambel's quail coveys, roadrunner, Harris's hawks atop saguaros, white-winged doves, long-nosed
				// bats, sphinx moths on the bloom night (N34), peccary herds, desert tortoise, Gila monster, desert iguana, tarantula and
				// tarantula hawk, coyote; the NIGHT OF THE QUEEN's moth motes (client-only, a fixed day of the shared clock's year).
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Desert)
				// Desert mistletoe in a third of the legume trees' crowns; no vines on its walls, cliffs or cave mouths (Ivy never).
				.GrowsVines(0f, 0f, 0f, 0.3f, ("DesertMistletoe", 1f))
				.Gathers("Copper Lode", "Elder Ironbark", "Saguaro Fruit", "Mesquite Pods", "Cholla Buds", "Agave Heart", "Chia Patch", "Creosote Resin",
					"Desert Tobacco", "Moonbloom Tuber", "Agave Fibre", "Carrizo Cane", "Cotton Boll", "Desert Ironwood", "Red Ochre", "Caliche Nodule",
					"Peridot Bomb", "Sunshell Scatter")
				.Herbs(Det("HerbChia"), Det("HerbDesertTobacco"), Det("Spikemoss"), Det("QueenOfTheNight"))
				// Moats are canal-fed ditches round the walled compounds, in the biome's own water.
				.Moat(BiomeMoatWater.Wet)
				// "Salt River olive": green-olive rivers, silty tan canals.
				.WaterTint(new BiomeWaterLook(ScrubHex("#6a7a52"), ScrubHex("#1e2a1e"), new Vector3(0.30f, 0.22f, 0.35f), 0.4f, ScrubHex("#8a7a58"), ScrubHex("#ece6d6")))
				// LATER (WEATHER O9): the late-summer monsoon storm cells and dust walls.
				.Describe("Sonoran thorn scrub of saguaro bajadas, palo verde washes and creosote flats under banded mountains — and the walled caliche "
					+ "towns and great canals of the canal builders")
				.Names(onsets: new[] { "Ha", "Ke", "Ma", "Ou", "Sa", "To", "Va", "Wi" },
					nuclei: new[] { "a", "o", "u", "ee", "ai" },
					codas: new[] { "t", "m", "sh", "l", "n" },
					middles: new[] { "ak", "ov", "il", "uk" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Wash-cave", "Tank", "Ochre Pit", "Boulder Shelter", "Mound-room", "Lava Hollow" },
					dungeonPrefixes: new[] { "The Thorned", "The Dry", "The Sunken", "The Burnt", "The Caliche", "The Silent" },
					poiSuffixes: new[] { "Compound", "Mound", "Court", "Canal", "Wash", "Tank", "Bajada", "Pediment", "Pass", "Narrows" },
					adjectives: new[] { "Red", "White", "Thorn", "Dry", "Sun-baked", "Green-barked", "Broken", "Black" },
					poiWords: new[]
					{
						("village", new[] { "Courtyards", "Compound" }),
						("town", new[] { "Compound", "Mound" }),
						("city", new[] { "Great House", "Mound Town" }),
						("capital", new[] { "Great House" }),
						("temple", new[] { "Mound", "Court" }),
						("ritualsite", new[] { "Court", "Sunken Court" }),
						("shrine", new[] { "Trail Shrine", "Cairn" }),
						("spring", new[] { "Tank", "Canal Head" }),
						("ruins", new[] { "Broken Canal", "Melted Compound" }),
						("keep", new[] { "Hill of Terraces" }),
						("fortress", new[] { "Hill of Terraces", "Walled Hill" }),
						("camp", new[] { "Harvest Camp", "Roasting Pits" }),
						("tradingpost", new[] { "Shell Works" }),
						("mine", new[] { "Ochre Pit" }),
						("monument", new[] { "Ground Figures" }),
						("landmark", new[] { "Ground Figures", "Warden" }),
						("ancienttree", new[] { "Warden", "King Ring" }),
						("gorge", new[] { "Narrows" }),
					},
					about: "Invented, soft (Ha, Ke, Ma, Ou; -t, -m, -sh) with plain English compounds: \"<Tree> Wash\", \"Broken Canal\", \"Hill of Terraces\"; no living-language words")
				// Approved §7: the canal builders' villages, compounds, mounds and courts, many ruins, springs, shrines and camps; no castles,
				// monasteries, crypts, barrows, ossuaries, graveyards, oases, mills, lumber camps, hunting lodges, overhangs, mesas, buttes,
				// coasts or sunken kinds. The river and mountain detections stay.
				// LATER (I7): the named pieces — courtyard groups of rectangular wattle houses in shallow pits, walled caliche compounds,
				// compound mounds, the four-storey great house with its sun and moon ports, sunken oval courts, the great canal's weir and
				// headgates, agave rock-pile fields, roasting pits, terraced fortified hills, shell workshops, saguaro harvest camps, trail
				// shrines, the ground figures, ochre pits, melted compounds, the Warden saguaro, the cereus garden of the Night of the
				// Queen and the sunshell scatter. The kit's variants below stand in.
				.Sites((POIType.Landmark, 1.2f), (POIType.Camp, 1.2f), (POIType.Shrine, 1.2f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1.2f),
					(POIType.Cave, 0.6f), (POIType.Monument, 0.8f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1f),
					(POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 0.8f), (POIType.Volcano, 0.3f), (POIType.NaturalArch, 0.2f), (POIType.Grotto, 0.6f),
					(POIType.BanditCamp, 0.4f), (POIType.MonsterDen, 0.6f), (POIType.Nest, 0.6f), (POIType.RitualSite, 1f), (POIType.StoneCircle, 0.2f),
					(POIType.Temple, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.5f), (POIType.Portal, 0.3f), (POIType.Battlefield, 0.6f),
					(POIType.Ruins, 1.4f), (POIType.RuinedTower, 0.4f), (POIType.Statue, 0.4f), (POIType.Obelisk, 0.2f), (POIType.AncientRoad, 0.6f),
					(POIType.AbandonedFarm, 0.6f), (POIType.Hermitage, 0.3f), (POIType.Village, 1.6f), (POIType.Town, 1f), (POIType.City, 0.5f),
					(POIType.Capital, 0.15f), (POIType.Keep, 1f), (POIType.Fortress, 0.6f), (POIType.TradingPost, 0.8f), (POIType.Waystation, 0.5f),
					(POIType.Mine, 0.6f), (POIType.Quarry, 0.4f), (POIType.DungeonEntrance, 0.6f), (POIType.BossLair, 0.4f), (POIType.OreVein, 0.5f),
					(POIType.CrystalFormation, 0.3f), (POIType.AncientTree, 1f), (POIType.HerbGrove, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.2f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1f), (POIType.ElderMonolith, 0.8f),
					(POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f), (POIType.LostLibrary, 0.5f),
					(POIType.WolfDen, 0.5f), (POIType.IdolVault, 0.4f), (POIType.LongBarrow, 0.6f), (POIType.Henge, 0.6f), (POIType.NomadCamp, 0.6f),
					(POIType.Kraal, 0.6f), (POIType.Baobab, 0.5f), (POIType.Necropolis, 1.2f), (POIType.RockCutFacade, 0.6f), (POIType.DjinnPrison, 1f),
					(POIType.CaravanHalt, 0.8f), (POIType.BeastBoneyard, 0.8f), (POIType.FireTemple, 0.4f))
				.Prefers(POIType.Village, "Timber", 2f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Stone", 1.2f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0.6f)
				.Prefers(POIType.Town, "Stone", 1.6f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.City, "Stone", 1.6f)
				.Prefers(POIType.City, "Timber", 0.1f)
				.Prefers(POIType.Capital, "Stone", 1.6f)
				.Prefers(POIType.Capital, "Timber", 0.05f)
				.Prefers(POIType.Temple, "Court", 1.6f)
				.Prefers(POIType.Temple, "Colonnade", 0f)
				.Prefers(POIType.RitualSite, "Stones", 0.6f)
				.Prefers(POIType.RitualSite, "Bonefire", 0.6f)
				.Prefers(POIType.Keep, "Stone", 1.2f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Waystation, "Inn", 1f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.4f)
				.Prefers(POIType.Shrine, "Shrine", 1.6f)
				.Prefers(POIType.Monument, "Obelisk", 0.3f)
				.Prefers(POIType.Mine, "Adit", 1.4f)
				.Prefers(POIType.Ruins, "Stone", 1.6f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Fortress, "Stone", 1.6f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Camp, "Lean-tos", 1.6f)
				.Prefers(POIType.Camp, "Crude", 0.8f)
				.Prefers(POIType.Camp, "Hide Tents", 0f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.TradingPost, "Market", 1.4f)
				.Prefers(POIType.Tower, "Stone", 1f)
				.Prefers(POIType.Tower, "Observatory", 1.4f)
				.Prefers(POIType.Tower, "Timber", 0f)
				.Prefers(POIType.Ruins, "Burned", 0.3f)
				.Prefers(POIType.Ruins, "Temple", 0.6f)
				.Prefers(POIType.Ruins, "Excavation", 0.4f)
				.Prefers(POIType.FallenStar, "Crater", 1.2f)
				.Prefers(POIType.Hermitage, "Stone", 1f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.3f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
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
				.Prefers(POIType.CaravanHalt, "Well Camp", 1.3f)
				// (the fantasy wave's variants end here)
				// Walled caliche compounds as the kit's stone villages (their walls the palette's puddled caliche), courtyard groups as its
				// timber villages (rib wattle); nothing of the north (longhouses, yurts, palisades, caravanserais, barrows).
				// Approved: Adobe 1.6, Earth 1.4, Hide 0.2, Stone and Timber 0. The kit has no adobe or earth templates for most kinds yet,
				// so its stone and timber templates stand in (built of puddled caliche and rib wattle by the palette below) rather than
				// leave the kinds without a template. LATER (I7): Earth-style templates of their own, then Stone and Timber at 0.
				.Favours(BiomeStructureStyle.Earth, 1.6f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Timber, 0.6f)
				.Favours(BiomeStructureStyle.Hide, 0.2f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: puddled caliche walls in coursed lifts, flat rib-and-mud roofs on mesquite vigas, caliche footings and setts,
				// dark twisted mesquite posts, carrizo-mat doors, red-on-buff painted carving; the courtyard houses' rib wattle under low
				// hipped brush-and-mud roofs at 30°; agave cord in place of iron, agave netting and strings of white shell.
				// LATER (NEW-SURFACES): PuddledCaliche, RibMudRoof, MesquitePost, CarrizoMat, CompoundWall, RedOnBuff, AgaveCord, AgaveNet,
				// ShellStrand, RibWattle and BrushMudRoof as surfaces of their own; the kit's tinted materials stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ScrubHex("#c8b494"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Adobe, ScrubHex("#c09a80"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ScrubHex("#a08a6a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, ScrubHex("#d4c8ae"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, ScrubHex("#cebea2"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Dressing, BiomeStructureMaterial.Adobe, ScrubHex("#c8b494"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Plaster, ScrubHex("#d8b890"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ScrubHex("#c8b494"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ScrubHex("#a08a6a"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, ScrubHex("#d4c8ae"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, ScrubHex("#a07a58"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Earth, ScrubHex("#a07a58"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, ScrubHex("#8a6a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, ScrubHex("#8a6a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, ScrubHex("#8a6a4a"))
				.BuildsAll(BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, ScrubHex("#4a3424"))
				.BuildsAll(BiomeStructureRole.Door, BiomeStructureMaterial.Reed, ScrubHex("#c8b078"))
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, ScrubHex("#d4c8ae"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, ScrubHex("#4a3424"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Reed, ScrubHex("#8a7a5a"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ScrubHex("#d8c8a0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, ScrubHex("#f0e8dc"))
				// Rainmelt: puddled-caliche wall tops rounded to domes, splash coving at the foot, mud fans, runnel flutes, a white
				// caliche skin surviving on the caps (decision 77); no moss in this country.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Rainmelt, 0.5f, 1f)
				// Field crops: flood-irrigated maize, canal cotton and little barley in the canal basins, the field agaves on the bajada,
				// kept near the rivers the great canal draws on. LATER (I2j-c J22): the CanalBasins (bunded basins off the stored channel
				// lines) and RockPileField (cobble heaps with an agave at each foot, a roasting pit at the edge) field surfaces; flat fields
				// and agave rows stand in.
				.Crops(("Detail_GrassMaizeCanal", 0.5f), ("Detail_FlowersCottonCanal", 0.3f), ("Detail_GrassLittleBarley", 0.2f), ("Detail_AgaveHohokam", 0.4f))
				.CropRows("Detail_AgaveHohokam", 5f)
				.FieldsNearRivers(600f)
				// Ocotillo living fences round the courtyard groups, saguaro-rib fences, one great canal off a river weir, the field edges'
				// clearance heaps.
				.Lines(new Entry.Line("OcotilloFence"), new Entry.Line("RibFence"), new Entry.Line("GreatCanal"), new Entry.Line("ClearanceRow"))
				// Tray fires on mesquite tripods along the streets and the trading posts', carrizo torches at the strongholds; trail shrines
				// and camps unlit. LATER (I7a): sunshell crystal posts at the fallen-star scatters, ritual courts and cereus gardens.
				.Lights(POIType.Village, BiomeWayLightKind.TrayFire, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.TrayFire, 16f)
				.Lights(POIType.City, BiomeWayLightKind.TrayFire, 16f)
				.Lights(POIType.Capital, BiomeWayLightKind.TrayFire, 16f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.TrayFire, 14f)
				.Lights(POIType.Keep, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.FallenStar, BiomeWayLightKind.CrystalPost, 20f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.CrystalPost, 20f)
				.Lights(POIType.HerbGrove, BiomeWayLightKind.CrystalPost, 20f)
				.Unlit(POIType.Port)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 10f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.ErodesAs("Arid");
		}

		/// <summary>A Scrubland rule kept to the upland reach (approved Q2: h ≥ −0.3 and t &lt; 0.8, the saguaro–palo verde country).</summary>
		private static Scatter SonoranUpland(Scatter rule) => Band(rule, -1f, 0.8f, -0.3f, 1f);

		/// <summary>A Scrubland rule kept to the lower reach's dry band (h &lt; −0.3: the creosote–bursage flats).</summary>
		private static Scatter SonoranLower(Scatter rule) => Band(rule, -1f, 1f, -1f, -0.3f);

		/// <summary>
		/// One dry-country blade type's look (the Scrubland and Savanna files): its own root and tip colours over the mesh's, how much of
		/// the ground's colour it takes, and its shape, heads and sprinkle where given.
		/// </summary>
		private static void AridBlade(BiomeGrassType t, string root, string tip, float groundTint, float? density = null, float? stiffness = null,
			float? width = null, float? clump = null, float? bend = null, float? heightScale = null, GrassBladeHead? head = null, float? sprinkle = null,
			float? headSize = null, float? headShare = null, string[] heads = null)
		{
			t.RootColour = ScrubHex(root);
			t.TipColour = ScrubHex(tip);
			t.ColoursOverMesh = true;
			t.GroundTint = groundTint;
			t.Density = density ?? t.Density;
			t.Stiffness = stiffness ?? t.Stiffness;
			t.Width = width ?? t.Width;
			t.ClumpPull = clump ?? t.ClumpPull;
			t.Bend = bend ?? t.Bend;
			t.HeightScale = heightScale ?? t.HeightScale;
			t.Head = head ?? t.Head;
			t.Sprinkle = sprinkle ?? t.Sprinkle;
			t.HeadSize = headSize ?? t.HeadSize;
			t.HeadShare = headShare ?? t.HeadShare;
			if (heads != null)
			{
				t.HeadColours = System.Array.ConvertAll(heads, ScrubHex);
				t.HeadColoursOverMesh = true;
			}
		}

		/// <summary>An sRGB colour from #rrggbb, for the Scrubland and Savanna files.</summary>
		private static Color ScrubHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
