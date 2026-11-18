#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>How far out of a settlement Alpine Meadow's drove lanes are fenced, metres: through its hay meadows, not up the pasture.</summary>
		public const float DroveLaneReach = 300f;

		/// <summary>The <c>Alpine Meadow</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry AlpineMeadow()
		{
			// The Carpathian high pastures (biome round, approved 2026-10-18, authored 2026-10-26): long bald grass ridges rising
			// out of the spruce, a mauve haze of bent-grass over gold arnica, bellflower and rampion, white false hellebore standing
			// ungrazed, gentian spires, seas of dwarf mugo pine and crimson rhododendron on the north faces, crocus carpets at the
			// last snow, rust-brown rush on the wind ridges, grey-green flysch ribs and slab fields where the ridge breaks, old stone
			// pines on the knolls and wind-flagged spruce at the forest edge. Its people are shepherds of the summer folds, with
			// painted hewn-log houses under steep fir shakes; its fantasy the mountains' weather and spirits (the dancers' burnt
			// rings, the storm-wizards' black tarns, the stone flock on the ridge, stormglass in the caves). Three reaches: the
			// MELTLINE (t ≤ −0.35: snow patches, crocus melt-turf), the DRY LIMESTONE (h ≤ 0.1: blue moor-grass, edelweiss, karren)
			// and the WET springs (h ≥ 0.3: flushes, tall herbs).
			// LATER (I9): every ground and object texture as Alpine Meadow's own variant of its recipe (decision 12); until then the
			// grounds below are Alpine Meadow's own recipes and the barks, leaves and stones the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Sanziene (midsummer honey-lemon) + Ielebloom (the dancers' hot
			// magenta) (BiomeTemplate.rareLookA/B).
			return E("Alpine Meadow", L(Ground.GrassMeadowPolonyna,
					// The polonyna turf: bent and red fescue under a mauve haze, sweet vernal grass, gold arnica and blue bells.
					Grass("GrassBent", 28f), Grass("GrassSweetVernal", 8f),
					// The rusty rush only on the high wind ridges.
					With(Grass("GrassRushTrifid", 6f), s => s.Height = new Vector2(0.71f, 1f)),
					Plants("FlowersPolonyna", 10f, 180),
					// Crocus at the snow's edge, only where the year is cold enough to keep snow into summer.
					Band(Plants("FlowersMeltCrocus", 4f, 200), -1f, 0f),
					// The ungrazed: white false hellebore in its pleated ranks, yellow gentian spires, carline stars.
					Plants("Veratrum", 0.8f), Plants("GentianYellow", 0.4f), Plants("CarlineThistle", 1.2f),
					Plants("HerbArnica", 2f), Plants("HerbMeum", 2f),
					// The dock ring round every fold and byre, and the litter of a grazed summer thickest there.
					Moded(Plants("DockAlpine", 3f), BiomeScatterMode.NearSites("Camp,Village,Hermitage", 40f)),
					Plants("DebrisPasture", 3f, 128),
					// Fairy rings (and the dancers' burnt ones). LATER (I9b P29): one ring in six a scorched dead ring; all live here.
					Moded(Plants("FairyRing", 1.5f), BiomeScatterMode.FairyRings(7f, 0.35f)),
					// Marmot burrows in the stony slopes of the drier side.
					With(Band(Plants("DebrisMarmotSpoil", 0.3f, 128), -1f, 1f, -1f, 0.5f), s => s.Slope = new Vector2(10f, 30f)),
					// Mugo on the slopes of the wetter side, the rhododendron on the north faces.
					With(Band(Bush("Knieholz", 0.31f), -1f, 1f, -0.2f, 1f), s => s.Slope = new Vector2(15f, 35f)),
					Faced(Bush("CarpathianRhododendron", 0.3f), BiomeFacing.Poleward),
					// Old stone pines on the knolls (lone ones in the open), wind-flagged spruce at the forest edge.
					With(Stand(Was(0.03f, 16f), 30f, W(0.06f, 0.4f, open: 0.05f), "Cembra"), s => s.Height = new Vector2(0.64f, 0.74f)),
					// LATER (I9b P26): the FlagSpruce's Flag (windward branches stripped) and Skirt (a ring of layered offshoots) fields;
					// a leaning spruce crowned to the ground stands in.
					With(Trees(0.006f, 35f, 30f, "FlagSpruce"), s => s.Height = new Vector2(0.6f, 0.68f)),
					// LATER (I9b): a standing-snag deadwood form; a dead stone pine as a tree stands in.
					Trees(0.003f, 50f, 35f, "SnagCembra"),
					// The flysch: rib rows on the slopes, the high slab seas, sandstone tors, the stone crone on the ridge with her flock.
					With(Outcrops("Flysch ribs", 0.008f, 30f, F(RegionalRocks.Flysch, "Ribs"), 28f), s => s.Slope = new Vector2(10f, 28f)),
					With(Blocks("Flysch slab fields", 0.004f, 60f, F(RegionalRocks.Flysch, "SlabField"), 30f), s => { s.Slope = new Vector2(8f, 30f); s.Height = new Vector2(0.68f, 1f); }),
					Outcrops("Flysch tors", 0.003f, 50f, F(RegionalRocks.Flysch, "Tor"), 30f),
					With(Outcrops("Stone crones", 0.0008f, 120f, F(RockTypes.Conglomerate, "Crone"), 20f), s => s.Height = new Vector2(0.7f, 1f)),
					With(Blocks("Stone flocks", 0.001f, 120f, F(RockTypes.Conglomerate, "StoneSheep"), 25f), s => { s.ClusterMetres = 25f; s.ClusterSize = 18f; }),
					Boulders(0.08f, 10f, FlyschBoulders),
					Stones(SmallRocks("Flysch"), 3f)),
				Ds(
					// d0: the uncut flower hay-meadow (fânaț) of the lower slopes, below the meltline.
					With(D(Ground.GrassFlowerHay, 120f, 2f, 0f, 20f, Grass("GrassSweetVernal", 14f), Grass("GrassBent", 10f), Plants("FlowersPolonyna", 14f, 200),
						Band(Plants("FlowersTallHerb", 3f, 180), -0.5f, 0.2f, 0f, 1f), Plants("Veratrum", 1.5f), Plants("HerbMeum", 2f),
						Moded(Plants("FairyRing", 1.5f), BiomeScatterMode.FairyRings(7f, 0.35f)), Stones(SmallRocks("Flysch"), 1f)).Temp(-0.35f, 1f),
						l => l.Height = new Vector2(0.6f, 0.69f)),
					// d1: the mugo-and-rhododendron dwarf-shrub heath of the north faces, on the wetter side.
					D(Ground.HeathMugo, 88f, 2.5f, 12f, 34f, With(Bush("Knieholz", 0.31f), s => { s.ClusterMetres = 25f; s.ClusterSize = 14f; }),
						Bush("CarpathianRhododendron", 4f), Plants("LichenIceland", 3f), Plants("HerbArnica", 1f),
						Logs("Mugo stumps", 0.05f, 8f, Wood("Stump_Mugo"))).Humid(-0.2f, 1f).Facing(BiomeFacing.Poleward, 100f)
						.Ways(path: Ground.PathKnieholzCut, road: Ground.PathKnieholzCut),
					// d2: flysch ribs and slab rubble on the steep ground, rush on the tops, scree below.
					D(Ground.RockFlyschRibs, 40f, 4f, 28f, 90f, Grass("GrassRushTrifid", 10f), Band(Grass("GrassBlueMoor", 3f), -1f, 1f, -1f, 0.2f),
						Band(Plants("FlowersCrag", 3f, 160, 60f), -1f, 1f, -1f, 0.3f), Plants("LichenIceland", 1f, 160, 60f),
						Blocks("Flysch scree", 0.05f, 10f, F(RegionalRocks.Flysch, "Scree")), Trees(0.003f, 50f, 40f, "SnagCembra"),
						Boulders(0.3f, 6f, FlyschBoulders), Stones(SmallRocks("Flysch"), 8f), Stones(Pebbles("Flysch"), 2f)),
					// d3: late snow patches in the lee hollows of the meltline (tapering into the rock round them).
					D(Ground.SnowLatePatch, 72f, 3f, 0f, 30f).Temp(-1f, -0.35f),
					// d4: the melt-turf ringing the patches: matted straw just out of the snow, crocus and snowbells.
					D(Ground.GrassDryMeltTurf, 72f, 2f, 0f, 25f, Plants("FlowersMeltCrocus", 24f, 220), Grass("GrassRushTrifid", 4f)).Temp(-1f, -0.15f),
					// d5: the spring flushes of the wet side, bright moss cushions, tall herbs round them, dock near the folds.
					D(Ground.MossSpringFlush, 40f, 4f, 0f, 10f, Plants("MossFlush", 4f), Band(Plants("FlowersTallHerb", 6f, 180), -0.5f, 0.2f, 0f, 1f),
						Plants("Veratrum", 2f), Plants("DockAlpine", 0.2f), Bush("SilesianWillow", 0.4f)).Humid(0.3f, 1f),
					// d6: karren crag pasture of the dry limestone reach: blue moor-grass, edelweiss and pinks, marmots.
					D(Ground.LimestoneKarren, 160f, 3f, 12f, 40f, Grass("GrassBlueMoor", 18f), Plants("FlowersCrag", 8f), Plants("GentianYellow", 0.6f),
						Plants("CarlineThistle", 2f), Plants("DebrisMarmotSpoil", 0.4f, 128),
						Outcrops("Karren crags", 0.01f, 20f, F(RockTypes.Limestone, "Pavement", "Blade"), 40f)).Humid(-1f, 0.1f)
						.Ways(road: Ground.PathKarrenSteps)),
				C(Ground.CliffFlyschRib), Ground.SiltTarn, Ground.PebblesFlyschSlab)
				.Moved("main", "Trees: Spruce")
				// The rules the round retired (Taiga's spruce, larch and juniper, Bamboo Forest's rhododendron, the generic turf,
				// flowers and grey boulders, Tundra's gneiss), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "Trees: Spruce, Larch", "GrassLush", "FlowersMeadow", "FlowersAlpine", "FlowersTall", "Bush: MountainPine", "Bush: Juniper",
					"Bush: Rhododendron")
				.Moved("detail/0", "Boulders", "Formations: Gneiss")
				.Moved("detail/1", "GrassSedge")
				.Ways(path: Ground.PathSheepTrod, road: Ground.PathSledgeTrack, cobbles: Ground.FlagstoneFlyschCrazy)
				.Bars(sand: Ground.SandFlysch, gravel: Ground.GravelFlyschShingle)
				// Own rock flysch (its formations); approved: accepts the mining hills' andesite, the crags' limestone, the conglomerate
				// of the stone flock and the granite of the high core. Gneiss, schist, slate, quartzite and marble dropped.
				.Accepts(RegionalRocks.Flysch, RockTypes.Andesite, RockTypes.Limestone, RockTypes.Conglomerate, RockTypes.Granite)
				// Limestone caves and grottoes, andesite mines; the ice caves hosted (the great cave glaciers of the limestone).
				.CaveShell(CaveForm.Cave, "Limestone").CaveShell(CaveForm.Grotto, "Limestone").CaveShell(CaveForm.Mine, "Andesite")
				.CaveShell(CaveForm.IceCave, "Ice")
				// Stibnite needles and rhodochrosite of the mining hills; stormglass, its storm flickering inside it.
				.Crystals((BiomeCaveCrystalKind.Stibnite, 0.4f), (BiomeCaveCrystalKind.Rhodochrosite, 0.25f), (BiomeCaveCrystalKind.Stormglass, 0.35f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Flicker)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				.Look(BiomeLookClass.Alpine)
				// Approved: the tarns' and springs' dippers, newts and frogs (FreshWater) beside the grassland and mountain life.
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Mountain | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): chamois on the crags, whistling marmots, a brown bear at dusk by a fold, wolf howls, golden eagle,
				// water pipit, alpine accentor, nutcracker, Apollo butterflies, newts in the tarns; sheep flocks with white sheepdogs,
				// belled cattle, mountain ponies; the veiled dancers circling at night and a dragon in the hail clouds (storms only).
				// LATER (WEATHER/sound): the long horn's call across the ridges, sheep and cow bells.
				.Moat(BiomeMoatWater.Wet)
				// The "eye of the sea" tarns: gin-clear, sapphire-black in depth.
				.WaterTint(new BiomeWaterLook(UplandHex("#3a7a8a"), UplandHex("#0a2244"), new Vector3(0.42f, 0.14f, 0.06f), 0.06f, UplandHex("#4a4a40"), UplandHex("#f2f6f8")))
				.Blades("GrassBent", t => UplandBlade(t, "#4a5a2a", "#8a9a50", 0.4f, density: 1.3f, clump: 0.25f, width: 0.6f,
					head: GrassBladeHead.Plume, headSize: 0.04f, headShare: 0.5f, heads: new[] { "#8a5a7a" }))
				.Blades("GrassSweetVernal", t => UplandBlade(t, "#5a6a30", "#a8a050", 0.4f, clump: 0.3f, width: 0.9f,
					head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.35f, heads: new[] { "#b8903a" }))
				.Blades("GrassRushTrifid", t => UplandBlade(t, "#5a4a2a", "#a86a3a", 0.3f, stiffness: 2f, width: 0.4f, clump: 0.7f,
					head: GrassBladeHead.SeedHead, headSize: 0.015f, headShare: 0.1f, heads: new[] { "#3a2a1e" }))
				.Blades("GrassBlueMoor", t => UplandBlade(t, "#4a5a50", "#8a9a90", 0.3f, width: 0.9f, clump: 0.6f,
					head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.4f, heads: new[] { "#5a5a8a" }))
				.Blades("FlowersPolonyna", t => UplandBlade(t, "#4a6a2a", "#66843a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.035f,
					heads: new[] { "#f0a020", "#5a6ad0", "#6a3aa0", "#f4f2e8" }))
				.Blades("FlowersMeltCrocus", t => UplandBlade(t, "#4a6a2a", "#5e7e34", 0.35f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.04f,
					heads: new[] { "#7a4ac0", "#f4f0f8", "#9a80d8", "#e070a8" }))
				.Blades("FlowersTallHerb", t => UplandBlade(t, "#3e6a28", "#5a8a34", 0.35f, heightScale: 1.6f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.06f,
					heads: new[] { "#c06a8a", "#3a3a9a", "#2a4ab0", "#f0b020" }))
				.Blades("FlowersCrag", t => UplandBlade(t, "#5a6a4a", "#8a9a7a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#ecece4", "#e05a90", "#f0d030", "#f4f2ea" }))
				.Blades("GrassMountainOats", t => UplandBlade(t, "#5a7a5a", "#c8c090", 0.3f, head: GrassBladeHead.SeedHead, headSize: 0.06f, headShare: 0.6f,
					heads: new[] { "#d8c890" }))
				.Blades("FlowersFlax", t => UplandBlade(t, "#5a7a4a", "#7a9a5a", 0.3f, head: GrassBladeHead.Flower, sprinkle: 16f, headSize: 0.03f,
					heads: new[] { "#6a8ae0", "#b89a5a" }))
				.Blades("GrassHayMeadow", t => UplandBlade(t, "#4e6a2c", "#a8a860", 0.35f, density: 1.2f, heightScale: 1.4f,
					head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.3f, heads: new[] { "#a88a5a" }))
				.Describe("High summer pastures above the forest — bent-grass ridges, crocus at the snow's edge, mugo-pine seas, shepherds' summer folds and painted log villages")
				.Names(onsets: new[] { "Pol", "Hal", "Plai", "Stan", "Var", "Bor", "Gor", "Hov", "Chor", "Svid", "Rod", "Ceah", "Mar", "Ob", "Brat", "Dz" },
					nuclei: new[] { "a", "o", "i", "u", "e", "ea", "ia" },
					codas: new[] { "ul", "eni", "ita", "iv", "ova", "owka", "esti", "an", "ina" },
					middles: new[] { "on", "ar", "or", "ud" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Pestera", "Scorbura", "Ice Cave", "Hole", "Den", "Cleft", "Iron Door" },
					dungeonPrefixes: new[] { "The Hail-black", "The Sleeping", "The Dancers'", "The Storm-bound", "The Shepherd's", "The Lightning-split" },
					poiSuffixes: new[] { "Polonyna", "Hala", "Plai", "Tau", "Varf", "Stana", "Obcina", "Izvor" },
					adjectives: new[] { "Dancer-ringed", "Hail-black", "Gentian-blue", "Bell-loud", "Crocus-white", "Lightning-split", "Stone-flocked", "Shepherd's" },
					poiWords: new[]
					{
						("camp", new[] { "Stana", "Koliba", "Fold" }),
						("peak", new[] { "Varf", "Pip", "Crest" }),
						("pass", new[] { "Plai", "Saddle", "Shepherds' Way" }),
						("lake", new[] { "Tau", "Eye of the Sea" }),
						("spring", new[] { "Izvor", "Fount" }),
						("shrine", new[] { "Troita", "Wayside Cross" }),
						("temple", new[] { "Needle Church" }),
						("monastery", new[] { "Painted Monastery", "Schit" }),
						("feyring", new[] { "Dancers' Ring", "Hora" }),
						("ritualsite", new[] { "Black Tarn", "Storm Lectern" }),
						("stonecircle", new[] { "Stone Flock", "Crone and Sheep" }),
						("monsterden", new[] { "Dragon's Hole" }),
						("crypt", new[] { "Sleeping Knights", "Iron Door" }),
						("banditcamp", new[] { "Outlaws' Cave" }),
						("village", new[] { "Sat", "Painted Village" }),
					},
					about: "Carpathian shepherds' voice (Pol, Hal, Plai, Stan, Var; -ul, -eni, -ita, -iv, -ova, -owka): the high summer pastures")
				// Shepherds' country: summer folds everywhere, a village here and there, a small town, no city, capital or port; shrines,
				// springs, herb groves and fey rings up (approved as drafted).
				.Sites((POIType.Camp, 1.8f), (POIType.Village, 0.6f), (POIType.Town, 0.15f), (POIType.Keep, 1f), (POIType.Castle, 0.4f),
					(POIType.Fortress, 0.2f), (POIType.Tower, 1f), (POIType.Waystation, 0.6f), (POIType.TradingPost, 0.4f), (POIType.Monastery, 0.5f),
					(POIType.Hermitage, 0.8f), (POIType.Shrine, 1.4f), (POIType.Temple, 0.4f), (POIType.Spring, 1.4f), (POIType.Waterfall, 1f),
					(POIType.Peak, 1.2f), (POIType.Pass, 1.2f), (POIType.Gorge, 0.8f), (POIType.IceCave, 0.6f), (POIType.Cave, 1f), (POIType.HerbGrove, 1.4f),
					(POIType.FeyRing, 1.2f), (POIType.StoneCircle, 0.7f), (POIType.RitualSite, 0.8f), (POIType.LeyNexus, 0.5f), (POIType.MonsterDen, 0.8f),
					(POIType.Nest, 0.8f), (POIType.BanditCamp, 1f), (POIType.HuntingLodge, 0.6f), (POIType.LumberCamp, 0.3f), (POIType.LumberMill, 0.2f),
					(POIType.Watermill, 0.5f), (POIType.Mine, 0.5f), (POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.5f), (POIType.Quarry, 0.2f),
					(POIType.Graveyard, 0.5f), (POIType.Crypt, 0.4f), (POIType.Barrow, 0.2f), (POIType.Battlefield, 0.3f), (POIType.Ruins, 0.5f),
					(POIType.RuinedTower, 0.5f), (POIType.Statue, 0.4f), (POIType.Obelisk, 0.1f), (POIType.AncientRoad, 0.3f), (POIType.AbandonedFarm, 0.5f),
					(POIType.AncientTree, 0.4f), (POIType.WitchHut, 0.4f), (POIType.Clearing, 0.2f), (POIType.Portal, 0.2f), (POIType.FallenStar, 0.2f),
					// Kept as they were: the landform kinds at the rule's 1, and the dungeons, ossuaries, fishing camps and monuments.
					(POIType.Bridge, 1f), (POIType.Monument, 1f), (POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f),
					(POIType.Delta, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 1f), (POIType.FishingCamp, 1f),
					(POIType.Ossuary, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 0.4f), (POIType.GoblinWarren, 1f),
					(POIType.HauntedManor, 1f), (POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f),
					(POIType.WreckedCaravan, 0.6f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 0.8f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.8f), (POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 0.5f), (POIType.WishingStone, 0.6f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f), (POIType.KoboldDelving, 1.2f),
					(POIType.DruidGrove, 0.3f), (POIType.TrollStones, 1f), (POIType.ElfWaystone, 0.4f), (POIType.WolfDen, 0.8f),
					(POIType.BeeGarden, 0.6f), (POIType.VillageGreen, 0.6f), (POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f), (POIType.GallowsHill, 1f),
					(POIType.StoneFigures, 0.4f), (POIType.Shieling, 1.4f), (POIType.RockCutFacade, 0.4f), (POIType.CliffPalace, 0.4f),
					(POIType.DwarvenGate, 1.2f), (POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f), (POIType.Eyrie, 1.2f),
					(POIType.GriffinRoost, 1f), (POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f),
					(POIType.SilverMineTown, 1f), (POIType.FrostGiantSteading, 0.5f), (POIType.RuneStone, 0.4f), (POIType.LadyOfTheLake, 0.3f),
					(POIType.NymphPool, 0.4f), (POIType.RoyalMausoleum, 0.4f))
				.Prefers(POIType.Camp, "Lean-tos", 0.3f)
				.Prefers(POIType.Camp, "Crude", 0.3f)
				.Prefers(POIType.Camp, "Hide Tents", 0f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Village, "Timber", 2f)
				.Prefers(POIType.Village, "Stone", 0.3f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.8f)
				.Prefers(POIType.Town, "Stone", 0.6f)
				.Prefers(POIType.Town, "Palisaded", 0.3f)
				.Prefers(POIType.Temple, "Court", 0.1f)
				.Prefers(POIType.Temple, "Colonnade", 0f)
				.Prefers(POIType.Monastery, "Cloister", 0.3f)
				.Prefers(POIType.Monastery, "Timber", 0.3f)
				.Prefers(POIType.Shrine, "Shrine", 0.3f)
				.Prefers(POIType.Shrine, "Statue", 0.2f)
				.Prefers(POIType.Watermill, "Wheel", 0.4f)
				.Prefers(POIType.Graveyard, "Earthen", 0.4f)
				.Prefers(POIType.Graveyard, "Railed", 0.2f)
				.Prefers(POIType.BanditCamp, "Open", 0.4f)
				.Prefers(POIType.BanditCamp, "Palisade", 0.2f)
				.Prefers(POIType.FeyRing, "Ring", 0.4f)
				.Prefers(POIType.StoneCircle, "Menhirs", 0.2f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.RitualSite, "Stones", 0.3f)
				.Prefers(POIType.RitualSite, "Bonefire", 0.1f)
				.Prefers(POIType.MonsterDen, "Lair", 0.5f)
				.Prefers(POIType.MonsterDen, "Bones", 0.4f)
				.Prefers(POIType.Crypt, "Tomb", 0.4f)
				.Prefers(POIType.Tower, "Timber", 1.2f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.4f)
				.Prefers(POIType.TradingPost, "Market", 1.4f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Castle, "Highland", 1.6f)
				.Prefers(POIType.Castle, "Stone", 0.6f)
				.Prefers(POIType.Keep, "Stone", 1.2f)
				.Prefers(POIType.Keep, "Palisade", 0.4f)
				.Prefers(POIType.Tower, "Stone", 0.8f)
				.Prefers(POIType.Hermitage, "Stone", 1f)
				.Prefers(POIType.Hermitage, "Cottage", 0.4f)
				.Prefers(POIType.Waystation, "Inn", 1.2f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.Keep, "Dwarven Hold", 1f)
				.Prefers(POIType.Tower, "Dwarven Tower", 1f)
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
				.Prefers(POIType.Grange, "Abbey Grange", 1.4f)
				.Prefers(POIType.Grange, "Walled Barton", 1.3f)
				.Prefers(POIType.Henge, "Sun Ring", 1.3f)
				.Prefers(POIType.SilverMineTown, "Timber Camp", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Alpine Meadow's signature pieces and their variants — the summer fold with its cheese hut, milking gate and
				// leaning-pole fold (Camp "Stana" 2.0) and the herdsman's hut (Camp/Hermitage "Koliba" 0.8/0.6), the painted log house
				// with its gable hood and houseleeks on the ridge, the carved oak gate, the needle-spire church (Temple 1.6, Monastery
				// 0.8 "Needle Church"), the painted fresco monastery (Monastery "Painted" 1.2), the roofed wayside cross (Shrine "Troita"
				// 1.8), hay barns and pole stacks, the felting whirlpool tub (Watermill "Valtoare" 1.4), painted grave crosses (Graveyard
				// "Painted Crosses" 1.6), the outlaws' crag cave (BanditCamp "Haiduc Cave" 1.4), the detached bell tower, sheep-washes
				// and salt licks, the ridge-top fair, the dancers' burnt ring with its cold lights (FeyRing "Iele Ring" 1.4), the stone
				// flock (StoneCircle "Stone Flock" 1.4), the storm-wizard's chained black tarn (RitualSite "Solomonar Tarn" 1.4), the
				// dragon's scorched den (MonsterDen "Balaur Den" 1.0) and the iron door of the sleeping knights (Crypt "Sleeping Knights"
				// 1.0). Until then the kit's variants below stand in at the draft's weights.
				.Favours(BiomeStructureStyle.Timber, 2f)
				.Favours(BiomeStructureStyle.Hide, 0.8f)
				.Favours(BiomeStructureStyle.Stone, 0.6f)
				.Favours(BiomeStructureStyle.Iron, 0.2f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				// The palette: dark hewn logs with white-limed chinks under steep fir shakes at the 60° Goral pitch (approved), dry-laid
				// flysch plinths, carved oak, leaning-pole fences, black iron with brass inlay, white-black-red woollen cloth and
				// woven banded banners. LATER (NEW-SURFACES U11): HewnLog (squared beams, dovetails, painted joint-ends), Shake (riven
				// fir shakes; church scale pattern), Cerga (long-pile blanket) and Kilim (woven bands) as Alpine Meadow's own surfaces;
				// LATER (NEW-SURFACES U6): the painted monastery's exterior Fresco (plain lime stands in). The shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Log, UplandHex("#6a4a2e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Log, UplandHex("#5a3e28"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, UplandHex("#5a524a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, UplandHex("#5a524a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#6a6c5e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, UplandHex("#3a2a1e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, UplandHex("#5a3e28"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Timber, UplandHex("#7a6a58"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#6a6c5e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, UplandHex("#e6e0d2"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, UplandHex("#5a524a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, UplandHex("#5a524a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, UplandHex("#8a8a7c"))
				// The folds and herdsmen's huts: split vertical boards smoke-blackened inside, shake or pitched sod roofs.
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, UplandHex("#5a4a3a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, UplandHex("#5a6232"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, UplandHex("#5a524a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#6a6c5e"))
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Wall, BiomeStructureMaterial.Log, UplandHex("#6a4a2e"))
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#6a6c5e"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, UplandHex("#7a6a58"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, UplandHex("#6e7264"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, UplandHex("#6a4a2e"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, UplandHex("#6a6c5e"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, UplandHex("#5a6232"), pitch: BiomeRoofPitch.Pitch35)
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, UplandHex("#6e7062"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, UplandHex("#6a6e5e"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, UplandHex("#5a3e28"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, UplandHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, UplandHex("#ece6d8"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, UplandHex("#a8282a"))
				// Sunburnt timber: chocolate-black on the sun faces, silver on the shade, moss on the north shakes (approved finish).
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Sunburnt, 0.55f, 1f)
				// Small village fields of pasture country, no terraces or orchards: mountain oats in downslope plough strips (carved),
				// a little flax, and uncut hay meadow.
				.Crops(("Detail_GrassMountainOats", 0.45f), ("Detail_FlowersFlax", 0.2f), ("Detail_GrassHayMeadow", 0.35f))
				.CropSurface("Detail_GrassMountainOats", BiomeFieldSurface.RidgeAndFurrow)
				// Leaning-pole fences along the field and meadow edges and the drove lanes; turfed flysch clearance rows on the fields.
				// The drove lanes are the village's own (2026-10-27, Moron 2: "weird fencing along the roads higher up in the snowy
				// hills"): the cart tracks and roads out through its hay meadows, fenced within DroveLaneReach of a settlement; the
				// high pasture above is open, its footpaths and trails never fenced.
				.Lines(new Entry.Line(BiomeLinearForm.LeaningPole, BiomeLinearPlace.FieldEdges),
					With(new Entry.Line(BiomeLinearForm.LeaningPole, BiomeLinearPlace.Lanes), l =>
					{
						l.Style.Reach = DroveLaneReach;
						l.Style.Ways = BiomeLinearWays.Tracks | BiomeLinearWays.Roads;
					}),
					new Entry.Line("ClearanceRow"))
				// Tin lanterns on the log walls, carved oak lantern posts in the towns, wall torches at the inns, iron brackets on the
				// church walls, a candle lantern on the wayside cross, the dancers' cold lights, stormglass on the wizards' posts, cressets
				// at the strongholds; the folds go unlit (the fold fire only).
				.Lights(POIType.Village, BiomeWayLightKind.WallLantern, 18f)
				.Lights(POIType.Town, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.Waystation, BiomeWayLightKind.WallTorch, 14f)
				.Lights(POIType.Monastery, BiomeWayLightKind.HangingBracket, 15f)
				.Lights(POIType.Temple, BiomeWayLightKind.HangingBracket, 15f)
				.Lights(POIType.Shrine, BiomeWayLightKind.CrookLantern, 10f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.Glowjar, 8f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Tower, BiomeWayLightKind.Cresset, 16f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.WallTorch)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 16f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				// Houseleek mats on the ruins and crags (and LATER (I7) on every roof ridge, the lightning charm).
				// LATER (I9b): a rosette-mat leaf for the houseleek; its own species on the palmate leaf stands in.
				.GrowsVines(0f, 0.5f, 0.2f, 0f, ("Houseleek", 1f))
				.Gathers("Arnica Bloom", "Gentian Root", "Iceland Moss", "Carline Star", "Fairy-ring Champignon", "Cembra Heartwood", "Stibnite Vein", "Stormglass Cluster")
				.Herbs("Detail_HerbArnica", "Detail_GentianYellow", "Detail_HerbMeum", "Detail_LichenIceland")
				.ErodesAs("Mountain");
		}

		/// <summary>The flysch boulders (I6b stones of the crust rock): round, slab and spire (the Alpine Meadow file's).</summary>
		private static string[] FlyschBoulders => new[] { Boulder("Flysch", "Round"), Boulder("Flysch", "Slab"), Boulder("Flysch", "Spire") };

		/// <summary>
		/// A blade type's look as a round's draft gives it (<see cref="Entry.Blades"/>; shared by the Alpine Meadow, Mountain Slope and Scree
		/// files): root and tip colours over the mesh's, the ground tint, and the shape and head values that are set (null keeps the type's own).
		/// </summary>
		private static void UplandBlade(BiomeGrassType t, string root, string tip, float groundTint, float? density = null, float? stiffness = null,
			float? width = null, float? clump = null, float? bend = null, float? heightScale = null, GrassBladeHead? head = null, float? sprinkle = null,
			float? headSize = null, float? headShare = null, string[] heads = null)
		{
			t.RootColour = UplandHex(root);
			t.TipColour = UplandHex(tip);
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
				t.HeadColours = System.Array.ConvertAll(heads, UplandHex);
				t.HeadColoursOverMesh = true;
			}
		}

		/// <summary>An sRGB colour from #rrggbb, for the Alpine Meadow, Mountain Slope and Scree files.</summary>
		private static Color UplandHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
