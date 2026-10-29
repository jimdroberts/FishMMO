#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Savanna</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Savanna()
		{
			// The Zimbabwe plateau and the Limpopo lowveld in the stone-house centuries (biome round, approved 2026-10-25, authored
			// 2026-10-26): tall-grass parkland on granite, thatching grass head-high and gold in the dry season, red-oat flushing copper,
			// flat-topped umbrella thorns and grey baobabs standing apart, copper-leaved mopane on the grey clay lowlands, lime-barked
			// fever trees round the vleis, red termite islands under their own thickets; the granite kopjes stacked and balanced,
			// lichened orange and lime, low bare dwalas, wide sand rivers dry most of the year; and in the late dry season the veld
			// BURNS (decision 75: the burn mosaic on the shared clock), flushes green and the fire lilies come up. Its people build
			// round daga houses under stepped thatch and curving drystone walls without mortar or corners. Invented names, no rites,
			// gods or real faces. Humidity reaches (decision 29): h < 0.05 the mopane lowveld, h > 0.25 the tall veld.
			// LATER (I9 T1): every ground and object texture as Savanna's own variant (Granite "Matopo", Schist "Greenstone", Sandstone
			// "Mapungubwe", Basalt "Dolerite", Termitaria "Lowveld", the umbrella thorn's and baobab's barks and leaves).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Ashflush V(0.32, 0.92, 0.58) and Birdstone V(0.44, 0.22, 0.58).
			// LATER (I9b P29 / B10): the wet/dry season tints (resurrection bush greening, veld flowers wet only, mopane copper, the baobab
			// leafless with pods in the dry season) and the baobab's flowers opening at dusk.
			return E("Savanna", L(Ground.GrassDryTawnyVeld,
					// The veld's grasses: thatching grass in head-high swathes (thinner in the mopane lowveld), red-oat in patches,
					// Guinea grass in the trees' shade, wire grass round the kraals, star-grass lawns at the homesteads' dancing floors.
					Band(Grass("GrassThatch", 26f, 220), -1f, 1f, 0.05f, 1f), Grass("GrassRedOat", 18f, 200),
					Moded(Grass("GrassGuinea", 16f, 200), BiomeScatterMode.Nursed("UmbrellaThorn,Marula,Jackalberry,Baobab,Leadwood", 6f)),
					Moded(Grass("GrassWire", 8f, 180), BiomeScatterMode.NearSites("Village,Camp,Town", 80f)),
					Moded(Grass("GrassStar", 6f, 200), BiomeScatterMode.NearSites("Village,Town", 40f)),
					// Wet-season veld flowers; the fire lilies grow only where the burn passed one to four weeks before (their blade type's
					// AfterBurn, decision 75); the flame lily scrambling on the termite islands.
					Plants("FlowersVeld", 2f), Plants("FlowersFireLily", 6f),
					OnLandform(Plants("FlowersFlameLily", 1.5f), BiomeLandformPart.Whole, "TermiteIslands"),
					// The parkland's trees: umbrella thorns in open stands, the plateau's baobabs standing apart, marula, leadwood alive and
					// dead for centuries. LATER (I9b P25): the weaver nests on a third of the umbrella thorns.
					Stand(Was(0.06f, 25f), 20f, W(0.2f, 0.12f, open: 0.3f), "UmbrellaThorn"), Trees(0.006f, 60f, 15f, "BaobabMapungubwe"),
					Trees(0.004f, 40f, 18f, "Marula"), Trees(0.003f, 50f, 18f, "Leadwood"), Trees(0.002f, 60f, 20f, "LeadwoodDead"),
					Bush("SickleBush", 0.4f), Plants("HerbWildSage", 0.5f),
					// The termite islands (their landform's zone, decision 55): the island thicket of jackalberry, raisin bush, Guinea
					// grass and bowstring hemp, Podaxis after rain, and the chimney spires at their crowns.
					// LATER (NEW-ROCK-TYPES): Termitaria "Lowveld" with its Spire and Island shapes; the catalogue's cathedral mound stands in.
					OnLandform(Trees(0.02f, 12f, 15f, "Jackalberry"), BiomeLandformPart.Whole, "TermiteIslands"),
					OnLandform(Bush("Grewia", 1f), BiomeLandformPart.Whole, "TermiteIslands"),
					OnLandform(Plants("Sansevieria", 3f), BiomeLandformPart.Whole, "TermiteIslands"),
					OnLandform(Plants("MushroomPodaxis", 2f), BiomeLandformPart.Whole, "TermiteIslands"),
					OnLandform(Outcrops("Termite island spires", 0.01f, 20f, F(RockTypes.Termitaria, "Cathedral"), 20f), BiomeLandformPart.Whole, "TermiteIslands"),
					Outcrops("Termite mounds", 0.003f, 40f, F(RockTypes.Termitaria, "Cathedral"), 20f),
					// The kopjes' castles of rounded corestones (their landform), the granite boulders round them.
					OnLandform(Outcrops("Granite kopjes", 0.01f, 30f, F(RockTypes.Granite, "Matopo", "Tor"), 15f), BiomeLandformPart.Whole, "Kopje"),
					Boulders(0.02f, 15f, Grey),
					// The veld's litter and the silver leadwood logs.
					Plants("DebrisVeld", 2f), Logs("Leadwood logs", 0.02f, 15f, Wood("FallenLog_Leadwood"))),
				Ds(
					// d0: granite sandveld round the kopjes and dwalas: wire and red-oat grass, monkey's tail, red bushwillow on the edges,
					// corestones and granite chips.
					D(Ground.SandGraniteSandveld, 96f, 2f, 0f, 16f, Grass("GrassWire", 14f, 200), Grass("GrassRedOat", 6f, 180), Plants("Xerophyta", 0.5f),
						Bush("Bushwillow", 0.3f), Stones(SmallRocks("Grey"), 1.5f), Blocks("Granite corestones", 0.03f, 10f, F(RockTypes.Granite, "Corestone"))),
					// d1: mopane clay of the lowveld (below 0.52): mopane in stands, elephant-broken stumps, the butterfly leaves' litter.
					// Its ways are hoof-churned game trails.
					With(D(Ground.ClayMopane, 128f, 2f, 0f, 8f, Stand(Was(0.3f, 8f), 15f, W(0.6f, 0.4f), "Mopane"), Grass("GrassRedOat", 8f, 160),
						Plants("DebrisVeld", 3f), Logs("Elephant-broken mopane", 0.01f, 20f, Wood("Stump_ElephantBroken"))), l => l.Height = new Vector2(-1f, 0.52f))
						.Ways(road: Ground.PathGameTrail),
					// d2: greenstone red earth over the schist belts: red-oat, veld flowers, sickle bush, fever-tea, quartz-reef float and
					// the white reefs themselves, greenstone ridges, dolerite blocks.
					// LATER (NEW-ROCK-TYPES): the Quartzite Reef shape (a gold-flecked white ridge 10–60 m) and Pebbles_Reef; quartzite
					// wedges and pebbles stand in.
					D(Ground.SoilGreenstoneRed, 160f, 2.5f, 0f, 18f, Grass("GrassRedOat", 12f, 200), Plants("FlowersVeld", 4f), Bush("SickleBush", 0.8f),
						Plants("HerbWildSage", 1f), Stones(Pebbles("Quartzite"), 2f),
						Blocks("Quartz reefs", 0.002f, 80f, F(RockTypes.Quartzite, "Wedge", "Block")),
						Outcrops("Greenstone ridges", 0.004f, 40f, F(RockTypes.Schist, "Ridge")),
						Blocks("Dolerite blocks", 0.01f, 20f, F(RockTypes.Basalt, "Block"))),
					// d3: dwala slab — the bare exfoliating granite of the dwalas and kopjes (their landforms paint it): resurrection
					// bush and monkey's tail in the cracks, tree aloes, loose sheet spalls, balancing stacks. Ways climb it by pecked steps.
					// LATER (I9 T1): Rocks_Matopo of its own; the granite's grey stones stand in.
					D(Ground.RockDwalaSlab, 96f, 3f, 15f, 45f, Plants("Myrothamnus", 3f), Plants("Xerophyta", 1.5f), Grass("GrassWire", 4f, 140),
						Trees(0.02f, 12f, 45f, "TreeAloe"), Stones(SmallRocks("Grey"), 3f),
						Blocks("Granite sheet spalls", 0.04f, 8f, F(RockTypes.Granite, "SheetSpall")),
						Outcrops("Granite balancing stacks", 0.003f, 60f, F(RockTypes.Granite, "BalancingStack"), 30f),
						Blocks("Granite corestones", 0.03f, 10f, F(RockTypes.Granite, "Corestone"))).Ways(path: Ground.PathSlabSteps),
					// d4: vlei turf round the water and on the pans' floors (their landform paints it): star-grass lawns, elephant grass,
					// fever trees, jackalberry and sausage trees on the banks.
					D(Ground.MudVleiTurf, 64f, 2f, 0f, 4f, Grass("GrassStar", 10f, 220), Plants("ReedsElephant", 10f),
						Stand(Was(0.4f, 10f), 10f, W(0.5f, 0.5f, open: 0.1f), "FeverTree"), Trees(0.02f, 20f, 10f, "Jackalberry"),
						Trees(0.01f, 30f, 10f, "SausageTree"))),
				C(Ground.CliffMatopo, 42f), Ground.MudPanWallow, Ground.SandRiverGruss)
				// The generic kit the round retired (the dry grass and shrub, acacia, the plain baobab, thornbush, euphorbia, the soil
				// layer's thornbush, the clay layer's sandstone boulders).
				.Moved("main", "GrassDry", "Trees: Acacia", "ShrubDry", "GrassSavanna", "Trees: Baobab", "Bush: Thornbush", "Euphorbia")
				.Moved("detail/0", "Bush: Thornbush")
				.Moved("detail/1", "Boulders")
				.Ways(path: Ground.PathSingleFile, road: Ground.PathCattleRoad, cobbles: Ground.FlagstoneDagaFloor)
				.Bars(sand: Ground.SandPinkGruss, gravel: Ground.GravelReefIronstone)
				// The veld burns every late dry season (decision 75, approved Q4): cells ignite at hashed times, fronts run downwind, the
				// grass burns to stubble and flushes green; the burnt veld is drawn by the burn on the GPU, never painted.
				// LATER (I2t LEFT): the flame ribbon and smoke plume at the front, DebrisBurn only on burnt ground, firebreaks at the vleis,
				// dwalas and island thickets.
				.Burns(BiomeBurnRegime.Annual)
				// Own rock granite (its formations; approved Q2), accepting the basement gneiss, the greenstone schist, the reef quartzite,
				// the table hill's sandstone and the dolerite dykes' basalt; conglomerate and shale dropped.
				.Accepts(RockTypes.Gneiss, RockTypes.Schist, RockTypes.Quartzite, RockTypes.Sandstone, RockTypes.Basalt)
				.CaveShell(CaveForm.Cave, "Granite").CaveShell(CaveForm.Grotto, "Granite").CaveShell(CaveForm.Mine, "Schist")
				.CaveShell(CaveForm.LavaTube, "Basalt").CaveShell(CaveForm.SeaCave, BiomeCaveShell.Ground).CaveShell(CaveForm.IceCave, "Ice")
				// Reef gold, emerald in the schist, and rainglass, the fantasy prisms brightening before rain (decision 75's RAIN mode);
				// the Birdstones' eyes and the crystal posts glow by it too.
				.Crystals((BiomeCaveCrystalKind.Reefgold, 0.5f), (BiomeCaveCrystalKind.Emerald, 0.3f), (BiomeCaveCrystalKind.Rainglass, 0.2f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Rain)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Rain)
				// The granite kopjes, low dwala pavements, seasonal pans, the termite islands and one cleft table hill (I2g-b presets).
				.Landforms("Kopje", "Dwala", "Pan", "TermiteIslands", "TableHill")
				.Look(BiomeLookClass.Arid)
				.Blades("GrassThatch", t => AridBlade(t, "#5a5a2a", "#c8a858", 0.35f, head: GrassBladeHead.Plume, headSize: 0.12f, headShare: 0.6f,
					heads: new[] { "#b0703a" }))
				.Blades("GrassRedOat", t => AridBlade(t, "#5a5a2e", "#a0602e", 0.3f, bend: 0.55f, head: GrassBladeHead.Plume, headSize: 0.05f, headShare: 0.5f,
					heads: new[] { "#8a3a1e" }))
				.Blades("GrassWire", t => AridBlade(t, "#6a5a4a", "#c8b8b8", 0.25f, stiffness: 1.6f, width: 0.5f, head: GrassBladeHead.Plume, headSize: 0.06f,
					headShare: 0.7f, heads: new[] { "#d8c8d0" }))
				.Blades("GrassGuinea", t => AridBlade(t, "#3a5a2a", "#7a9a3a", 0.4f, width: 1.6f, head: GrassBladeHead.Plume, headSize: 0.1f, headShare: 0.3f,
					heads: new[] { "#8a6a4a" }))
				.Blades("GrassStar", t => AridBlade(t, "#3e5a2a", "#6a8a3a", 0.45f, density: 1.4f, heightScale: 0.6f, head: GrassBladeHead.SeedHead, headSize: 0.03f,
					headShare: 0.3f, heads: new[] { "#6a4a5a" }))
				.Blades("ReedsElephant", t => AridBlade(t, "#4a6a2a", "#8aa04a", 0.2f, width: 1.4f, head: GrassBladeHead.Plume, headSize: 0.25f, headShare: 0.5f,
					heads: new[] { "#c8a050" }))
				.Blades("FlowersVeld", t => AridBlade(t, "#4a6a2e", "#6a8a3a", 0.3f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.04f,
					heads: new[] { "#9a7ac8", "#e8c830", "#d878a8", "#e0a020" }))
				.Blades("FlowersFireLily", t => AridBlade(t, "#3a5a2a", "#5a7a34", 0.2f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.05f,
					heads: new[] { "#d0281e", "#e070a0", "#f2ece0" }))
				.Blades("FlowersFireLily", t => t.AfterBurning())
				.Blades("FlowersFlameLily", t => AridBlade(t, "#3a5a2a", "#5a7a30", 0.25f, head: GrassBladeHead.Flower, sprinkle: 4f, headSize: 0.05f,
					heads: new[] { "#c8281a", "#f0c020" }))
				.Blades("GrassPearlMillet", t => AridBlade(t, "#5a7a3a", "#8a8a50", 0.3f, width: 1.6f, head: GrassBladeHead.SeedHead, headSize: 0.14f, headShare: 0.8f,
					heads: new[] { "#8a7a5a" }))
				// Grassland, scrub, the homesteads and the fallen enclosures (approved Q8: 278568).
				// LATER (AMBIENT-LIFE): elephant herds, giraffe, zebra and wildebeest, impala, kudu, sable, warthog, buffalo, lion prides,
				// hyena, ground hornbills, secretary birds, vultures, weaver colonies, quelea at dusk, carmine bee-eaters at the burn
				// fronts, hippo and crocodile in the sand-river pools, patched cattle at the kraals, dung beetles on the ways; the fantasy
				// ember-termites round the Embermounds and the stormbird running ahead of the weather driver's storm fronts.
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Scrub | BiomeHabitat.Settlement | BiomeHabitat.Ruins)
				// The rock fig's roots splitting granite and wall courses, the flame lily climbing; the liana retired.
				.GrowsVines(0.1f, 0.25f, 0.05f, 0.05f, ("RockFigRoots", 0.6f), ("Gloriosa", 0.4f))
				.Gathers("Reef Gold", "Alluvial Gold", "Malachite Copper", "Ironstone", "Soapstone", "Baobab Fruit", "Baobab Bast", "Marula Fruit",
					"Mopane Worms", "Thatchgrass", "Bowstring Hemp", "Resurrection Leaf", "Leadwood Charcoal", "Reefgold Crystal", "Emerald Prism", "Rainglass")
				.Herbs(Det("Myrothamnus"), Det("HerbWildSage"))
				// Dry moats (approved Q7); khaki, turbid water in the pans and sand-river pools, no floating plants.
				.Moat(BiomeMoatWater.Dry)
				.WaterTint(new BiomeWaterLook(ScrubHex("#8a7a52"), ScrubHex("#3a3222"), new Vector3(0.30f, 0.22f, 0.16f), 0.7f, ScrubHex("#a08a5a"), ScrubHex("#e8e0cc")))
				.Describe("Head-high golden grass under flat-topped thorns and grey baobabs, granite kopjes stacked like giants' cairns, red termite spires, "
					+ "the dry season's fires running orange across the hills at night — and the curving drystone walls of the stone-house kings")
				.Names(onsets: new[] { "Ma", "Mu", "Chi", "Nya", "Dza", "Ta", "Ru", "Ze", "Go", "Ka", "Sha", "Bu", "Nge", "Do", "Hwe" },
					nuclei: new[] { "a", "e", "i", "o", "u" },
					codas: new[] { "we", "ra", "ngwe", "nde", "mbo", "zi", "re", "ga", "ti", "nhu" },
					middles: new[] { "mb", "ng", "nd", "dz" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Kopje", "Hollow", "Cleft", "Enclosure", "Reef", "Fire-pits" },
					dungeonPrefixes: new[] { "The Burnt", "The Hollow", "The Walled", "The Ringing", "The Thirsting" },
					poiSuffixes: new[] { "Kopje", "Enclosure", "Pan", "Drift", "Kraal", "Rocks" },
					adjectives: new[] { "Red", "Burnt", "Tall-grass", "Thorn", "Ringing", "Golden" },
					poiWords: new[]
					{
						("village", new[] { "Homestead", "Daga Ring" }),
						("camp", new[] { "Cattle Post", "Herd Kraal" }),
						("city", new[] { "Great Enclosure", "Stone Houses" }),
						("capital", new[] { "Great Enclosure", "Stone Houses" }),
						("keep", new[] { "Hill Walls", "Boulder Fort" }),
						("fortress", new[] { "Hill Walls", "Boulder Fort" }),
						("tradingpost", new[] { "Bead Market", "Gold Halt" }),
						("mine", new[] { "Reef", "Fire-pits" }),
						("spring", new[] { "Waterhole", "Pan" }),
						("lake", new[] { "Pan", "Waterhole" }),
						("ruins", new[] { "Fallen Walls", "Old Enclosure" }),
						("statue", new[] { "Birdstones", "Bird Pillars" }),
						("ancienttree", new[] { "Elder Baobab", "Hollow Tree" }),
						("overhang", new[] { "Painted Rock" }),
						("ritualsite", new[] { "Ringing Rocks" }),
					},
					about: "Invented, Shona-flavoured (Ma, Chi, Nya, Dza; -we, -ngwe, -nde, -mbo) with plain English place words; no real place names")
				// Approved §7: homesteads, cattle posts, fallen enclosures, reef workings and the bead market; the plateau's kopjes, pans,
				// waterholes and painted overhangs; no ports or coasts, wetland, mill, lumber, temple, monastery, barrow, ossuary, stone-circle,
				// fey, dune, salt, oasis, glacier, ice or volcanic kinds. The river crossings and falls stay.
				// LATER (I7): the named pieces — the Great Enclosure (an elliptical battered curtain with the chevron frieze, the parallel
				// passage and the solid conical tower; ≤ 1 a scene), the hill complex woven through a kopje's corestones (I24), daga
				// homesteads of round houses under stepped TierThatch (I28) joined by curved screen walls, boulder granaries, cattle kraals,
				// smelting grounds, reef workings, the bead market, the Hill of the Gilt Rhino on the table hill's pad (I23), the Birdstones
				// (eyes glowing on nights before rain, ≤ 2), the painted shelter, the Elder Baobab with a hall in its trunk, the ringing
				// rocks, the waterhole hide, the fallen enclosure and the Embermound (amber Breath glow, ≤ 2). The kit's variants stand in.
				.Sites((POIType.Camp, 1.2f), (POIType.Shrine, 0.4f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Clearing, 0.6f), (POIType.Spring, 1f),
					(POIType.Cave, 0.6f), (POIType.Monument, 0.6f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 0.4f), (POIType.Lake, 0.8f),
					(POIType.Peak, 0.4f), (POIType.Pass, 1f), (POIType.Gorge, 0.3f), (POIType.Mesa, 0.6f), (POIType.Butte, 1f), (POIType.Valley, 0.4f),
					(POIType.Grotto, 0.4f), (POIType.Overhang, 1f), (POIType.BanditCamp, 0.6f), (POIType.HuntingLodge, 0.6f), (POIType.FishingCamp, 0.2f),
					(POIType.MonsterDen, 0.8f), (POIType.Nest, 0.6f), (POIType.RitualSite, 0.6f), (POIType.LeyNexus, 0.1f), (POIType.FallenStar, 0.2f),
					(POIType.CorruptedGrove, 0.2f), (POIType.Portal, 0.1f), (POIType.Graveyard, 0.5f), (POIType.Crypt, 0.2f), (POIType.Battlefield, 0.4f),
					(POIType.Ruins, 1.4f), (POIType.RuinedTower, 0.4f), (POIType.Statue, 0.6f), (POIType.Obelisk, 0.2f), (POIType.AncientRoad, 0.6f),
					(POIType.AbandonedFarm, 0.4f), (POIType.Hermitage, 0.2f), (POIType.Village, 1.6f), (POIType.Town, 0.8f), (POIType.City, 0.5f),
					(POIType.Capital, 0.3f), (POIType.Keep, 1f), (POIType.Castle, 0.3f), (POIType.Fortress, 0.3f), (POIType.TradingPost, 1f),
					(POIType.Waystation, 0.6f), (POIType.Mine, 1.2f), (POIType.Quarry, 0.6f), (POIType.DungeonEntrance, 0.5f), (POIType.BossLair, 0.3f),
					(POIType.OreVein, 0.8f), (POIType.CrystalFormation, 0.3f), (POIType.AncientTree, 1f), (POIType.HerbGrove, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.2f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 1f), (POIType.TitansBones, 1f), (POIType.ElderMonolith, 0.4f),
					(POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.5f), (POIType.LongBarrow, 0.6f), (POIType.Henge, 0.6f), (POIType.KurganField, 0.4f),
					(POIType.NomadCamp, 0.6f), (POIType.Kraal, 1.4f), (POIType.Baobab, 1.4f), (POIType.CaravanHalt, 0.8f), (POIType.BeastBoneyard, 0.8f))
				.Prefers(POIType.Village, "Yurts", 1.8f)
				.Prefers(POIType.Village, "Stone", 0.5f)
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Camp, "Hide Tents", 1.6f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.6f)
				.Prefers(POIType.Statue, "Standing", 1.6f)
				.Prefers(POIType.Ruins, "Stone", 1.6f)
				.Prefers(POIType.Ruins, "Temple", 0f)
				.Prefers(POIType.RitualSite, "Stones", 1.4f)
				.Prefers(POIType.MonsterDen, "Lair", 0.6f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Mine, "Adit", 1.4f)
				.Prefers(POIType.Mine, "Shaft Head", 1f)
				.Prefers(POIType.OreVein, "Spoil", 1.2f)
				.Prefers(POIType.Quarry, "Cut Stone", 0.8f)
				.Prefers(POIType.TradingPost, "Market", 1.8f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Town, "Stone", 1.8f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.City, "Stone", 2f)
				.Prefers(POIType.City, "Timber", 0f)
				.Prefers(POIType.Capital, "Stone", 2f)
				.Prefers(POIType.Capital, "Timber", 0f)
				.Prefers(POIType.Castle, "Stone", 2f)
				.Prefers(POIType.Keep, "Stone", 1.8f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Fortress, "Stone", 1.8f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 1f)
				.Prefers(POIType.Graveyard, "Earthen", 0f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.DragonLair, "Bone Roost", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.5f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.4f)
				.Prefers(POIType.NomadCamp, "Felt Yurts", 1.4f)
				// (the fantasy wave's variants end here)
				// The round-plan villages (the kit's yurt camps) stand in for the daga homesteads, built of daga under thatch by the Hide
				// style's palette below; the stone variants for the walled towns, enclosures and hill walls; no timber, longhouse,
				// palisade, caravanserai, barrow or earthen-mound variants.
				// Approved: Earth 2.0, Stone 1.4, Hide 1.0, Timber 0. LATER (I7): Earth-style templates of their own; until then the
				// kit's Hide (round) and Stone templates carry the homesteads and enclosures, and Timber keeps a trace so no kind is left
				// without a template.
				.Favours(BiomeStructureStyle.Earth, 2f)
				.Favours(BiomeStructureStyle.Stone, 1.4f)
				.Favours(BiomeStructureStyle.Hide, 1f)
				.Favours(BiomeStructureStyle.Timber, 0.1f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: burnished ochre daga walls with moulded bands, stepped grass thatch on round plans at 45°, raised daga
				// platforms and floors, adze-hewn mukwa doors, mopane poles and palisades, forged black iron wound with copper wire,
				// beaten bark cloth; the stone style's fire-split granite in level dry courses with the dark dolerite chevron frieze and
				// soapstone carving; the cattle posts' sewn grass mats and patched hides.
				// LATER (NEW-SURFACES U3/U8 + I6h/I28): Daga, TierThatch, DagaPlatform, MukwaDoor, MopanePalisade, BarkCloth,
				// ZimbabweCourse, DentelleBand, GrassMat and PatchHide as surfaces of their own; the kit's tinted materials stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ScrubHex("#8a5a34"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, ScrubHex("#b89a5a"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, ScrubHex("#6a4024"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.Earth, ScrubHex("#5a3420"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ScrubHex("#8a5a34"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, ScrubHex("#b89a5a"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, ScrubHex("#6a4024"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ScrubHex("#d8ccb8"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, ScrubHex("#8a8076"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Fieldstone, ScrubHex("#8a8076"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, ScrubHex("#7a7068"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, ScrubHex("#2e2a26"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, ScrubHex("#7a8a7a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, ScrubHex("#b89a5a"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, ScrubHex("#8a5a34"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, ScrubHex("#b89a5a"), pitch: BiomeRoofPitch.Pitch45)
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, ScrubHex("#9a6a40"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Earth, ScrubHex("#9a6a40"))
				.BuildsAll(BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ScrubHex("#7a3a24"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, ScrubHex("#5a4a3a"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, ScrubHex("#6a5e52"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, ScrubHex("#2a2420"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ScrubHex("#a88a68"))
				// Termitecast: red-earth termite runways climbing walls and posts, grass seedlings on the wall heads, orange lichen on the
				// courses; the fire-blackened bases where the burn passed as the charred finish.
				.Weathers(BiomeStructureFinish.Charred, 0.2f, 0f)
				.Coated(BiomeStructureCoat.Termitecast, 0.5f, 1f)
				// Field crops: pearl millet and Bambara groundnut in small irregular fields; Bambara in rows.
				// LATER (I2j-b share): the draft's bottomland share (0.4 of fields as vlei gardens); LATER (I7): boulder granaries on half
				// the fields.
				.Crops(("Detail_GrassPearlMillet", 0.6f), ("Detail_Bambara", 0.3f))
				.CropRows("Detail_Bambara", 0.8f)
				// Mopane palisades round the kraals; the battered Zimbabwe wall round the towns, cities and hill complexes.
				// LATER (NEW-SURFACES): the MopanePalisade style's grey barked posts; the kit's palisade at the folds stands in.
				.Lines(new Entry.Line(BiomeLinearForm.Palisade, BiomeLinearPlace.Folds), new Entry.Line("ZimbabweWall"))
				// Soapstone fire-bowls on daga-and-granite pillars in the towns and cities, mopane-resin torches in the villages, braziers at
				// the strongholds; the cattle posts unlit (their hearths). LATER (I7a): crystal posts at the Birdstones and Embermounds.
				.Lights(POIType.Town, BiomeWayLightKind.SoapstoneBowl, 16f)
				.Lights(POIType.City, BiomeWayLightKind.SoapstoneBowl, 14f)
				.Lights(POIType.Capital, BiomeWayLightKind.SoapstoneBowl, 14f)
				.Lights(POIType.Village, BiomeWayLightKind.TorchPost, 20f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Statue, BiomeWayLightKind.CrystalPost, 20f)
				.Lights(POIType.Nest, BiomeWayLightKind.CrystalPost, 20f)
				.Unlit(POIType.Camp)
				.Unlit(POIType.Port)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 10f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.ErodesAs("Open");
		}
	}
}
#endif
