#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Plains</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Plains()
		{
			// The High Plains mixed-grass prairie in its medieval centuries (biome round, approved 2026-10-18, authored 2026-10-26): an
			// endless rolling sea of blue-grey wheatgrass and copper little bluestem, curly buffalograss and eyelash-headed grama on the
			// uplands, wine-purple big bluestem in the moist draws, coneflower, blazing star and wild sunflowers, soapweed yucca on the
			// pale sandhills, buffalo wallows and rain-filled playas in the hollows, prairie-dog towns, and in the south the brick-red
			// Gyp Hills under white gypsum caps. Rivers wide, shallow, sand-braided and tawny under giant cottonwoods. Its people are
			// earth-lodge farmers and bison hunters: domed earth lodges in palisaded villages on the river terraces, painted hide tipi
			// camps following the herds, platform-mound towns with plazas and cedar-post sun circles, buffalo jumps. Its fantasy is the
			// sky's: thunder-stones the lightning always finds, the pale spirit herd at moonrise, the horned serpent of the deep pools,
			// sunspoke crystals in the red caves. Reaches by climate band: north (t < 0.2) wheatgrass, bur oak and prairie smoke; south
			// grama, red beds, Osage orange and shinnery oak; east (h ≥ −0.05) the tallgrass draws widen; west (h ≤ −0.15) shortgrass,
			// yucca and prickly pear. Grassland keeps the loess and soda pans, Steppe the feather grass and yurts.
			// LATER (I9): every ground and object texture as the Plains' own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Ghostgrass V(0.11, 0.14, 0.94) and Sunwheel V(0.09, 0.95, 0.98).
			return E("Plains", L(Ground.GrassDryMixedPrairie,
					// The mixed-grass sward: wheatgrass in the north, little bluestem everywhere, grama and buffalograss between.
					Band(Grass("GrassWheatgrass", 26f), -1f, 0.3f), Grass("GrassLittleBluestem", 12f), Grass("GrassGrama", 8f),
					Plants("FlowersConeflower", 6f, 180), Plants("FlowersSunflower", 1f, 180), Band(Plants("FlowersBlazingStar", 3f, 180), -1f, 1f, -0.18f, 1f),
					Band(Plants("FlowersPrairieSmoke", 2f, 180), -1f, 0.22f), Band(Plants("YuccaSoapweed", 0.5f), -1f, 1f, -1f, -0.05f),
					Plants("CompassPlant", 0.6f), Plants("Milkweed", 0.6f), Plants("HerbBergamot", 1f), Plants("HerbPrairieTurnip", 0.6f),
					Plants("MushroomPuffball", 0.2f), Plants("DebrisBison", 3f, 128),
					// Prairie-dog towns gathered into colonies.
					Moded(Plants("DebrisDogtownMound", 0.15f, 128), BiomeScatterMode.ClusteredFields(80f, 0.3f)),
					// Sunflowers crowd the disturbed ground round the villages.
					Moded(Plants("FlowersSunflower", 6f, 180), BiomeScatterMode.NearSites("Village", 30f)),
					Bush("Leadplant", 0.3f),
					// The lone giant cottonwoods (landmarks, eagle nests) and the thunder-stones the lightning finds.
					Trees(0.0008f, 150f, 15f, "PlainsCottonwood"),
					// LATER (I7): the thunder-stone (a glassed red boulder in a fulgurite ring); the round red-bed boulder stands in.
					Boulders(0.0003f, 300f, Boulder("Redbed", "Round"))),
				Ds(
					// d0: shortgrass sod (≤ 12°) of buffalograss and grama, prairie-dog towns, prairie turnip. LATER (GROUND-BANDS): the
					// draft's either-or band (west h ≤ −0.1 OR south t ≥ 0.2) and its upland height; the dry band stands in.
					D(Ground.GrassShortgrassSod, 120f, 2.5f, 0f, 12f, Band(Grass("GrassBuffalo", 40f), 0.1f, 1f), Grass("GrassGrama", 30f), Plants("FlowersPrairieSmoke", 5f, 180),
						Plants("HerbPrairieTurnip", 0.8f), Plants("DebrisBison", 2f, 128), Band(Plants("PricklyPearPlains", 1f), -1f, 1f, -1f, -0.1f), Bush("Leadplant", 0.3f),
						Moded(Plants("DebrisDogtownMound", 0.5f, 128), BiomeScatterMode.ClusteredFields(80f, 0.45f))).Humid(-1f, -0.1f),
					// d1: the tallgrass draws (≤ 8°, the low ground, east of h −0.12): big bluestem and switchgrass, the cottonwood galleries
					// and bur-oak openings, chokecherry, sumac and sand plum. LATER (SCATTER-MODES): the galleries within 40 m of a river and
					// the driftwood on its bars (a near-water anchor); the draws stand in.
					With(D(Ground.GrassMeadowTallgrass, 64f, 2f, 0f, 8f, Band(Grass("GrassBigBluestem", 26f), -1f, 1f, -0.15f, 1f), Grass("GrassSwitchgrass", 10f),
						Band(Grass("GrassWheatgrass", 6f), -1f, 0.3f), Plants("FlowersConeflower", 4f, 180), Plants("FlowersSunflower", 4f, 180),
						Plants("FlowersBlazingStar", 4f, 180), Plants("CompassPlant", 1f), Plants("Milkweed", 1f), Plants("HerbBergamot", 2f),
						Plants("MushroomPuffball", 0.4f), Plants("DebrisCottonwood", 2f, 128),
						Bush("Chokecherry", 0.71f), Bush("SmoothSumac", 0.5f), Band(Bush("SandPlum", 0.6f), 0.1f, 1f), Band(Bush("BuffaloBerry", 0.3f), -1f, 0.25f),
						Stand(Was(0.25f, 12f), 12f, W(0.2f, 0.55f, open: 0.15f), "PlainsCottonwood"),
						Band(Stand(Was(0.04f, 25f), 12f, W(0.04f, 0.5f, open: 0.3f), "BurOak"), -1f, 0.3f, -0.15f, 1f),
						Band(Trees(0.004f, 30f, 12f, "OsageOrange"), 0.22f, 1f),
						Logs("Cottonwood driftwood", 0.004f, 30f, Wood("FallenLog_Cottonwood")), Logs("Cottonwood snags", 0.001f, 100f, Wood("Snag_Cottonwood")))
						.Humid(-0.12f, 1f), l => l.Height = new Vector2(0f, 0.49f)),
					// d2: the sandhills (≤ 20°): sand reed, sandhill flowers, yucca, plum and the southern shinnery oak; a sand trace over them.
					D(Ground.SandSandhills, 180f, 3f, 0f, 20f, Grass("GrassSandReed", 20f), Plants("FlowersSandhill", 6f, 180), Plants("YuccaSoapweed", 3f),
						Plants("PricklyPearPlains", 0.5f), Band(Bush("SandPlum", 0.71f), 0.1f, 1f), Band(Bush("ShinneryOak", 1.02f), 0.25f, 1f))
						.Ways(path: Ground.PathSandTrace, road: Ground.PathSandTrace),
					// d3: buffalo wallows (≤ 4°; the Wallow landform paints them): churned earth, bison litter, the playa flowers at the rims.
					D(Ground.SoilBuffaloWallow, 24f, 4.5f, 0f, 4f, Plants("DebrisBison", 6f, 128), Plants("FlowersPlaya", 4f, 180), Plants("Milkweed", 2f),
						Grass("GrassBuffalo", 10f, 160)),
					// d4: the playa floors (≤ 2°, low ground; the Playa landform paints them): smartweed, coreopsis and water-clover at the rims.
					With(D(Ground.ClayPlayaFloor, 140f, 4f, 0f, 2f, Plants("FlowersPlaya", 12f, 180)), l => l.Height = new Vector2(0f, 0.47f)),
					// d5: the Gyp Hills' red slopes (18–38°, the south only): little bluestem, prickly pear, yucca, sumac, red cedar; buttes
					// and slumps, the red beds' own chips; a red trail over them. LATER (NEW-ROCK-TYPES): the barite rose-rock pebbles; the red
					// beds' pebbles stand in.
					D(Ground.RedbedGypSlope, 64f, 3f, 18f, 38f, Grass("GrassLittleBluestem", 10f), Plants("FlowersConeflower", 3f, 180), Plants("PricklyPearPlains", 2f),
						Plants("YuccaSoapweed", 1.5f), Bush("SmoothSumac", 0.4f), Trees(0.003f, 40f, 38f, "RedCedar"),
						Outcrops("Red buttes", 0.0005f, 250f, F(RegionalRocks.Redbed, "Butte"), 38f),
						Blocks("Caprock slumps", 0.003f, 40f, F(RegionalRocks.Redbed, "Slump")),
						Stones(SmallRocks("Redbed"), 3f), Stones(Pebbles("Redbed"), 2f)).Temp(0.18f, 1f).Ways(path: Ground.PathRedTrail),
					// d6: the rimrock talus (≥ 38°): white caprock ledges over red scree, red-bed boulders.
					D(Ground.RedbedRimrockTalus, 48f, 4f, 38f, 90f, Blocks("Rimrock", 0.004f, 40f, F(RegionalRocks.Redbed, "Rimrock"), 70f),
						Blocks("Red scree", 0.05f, 10f, F(RegionalRocks.Redbed, "Scree"), 70f),
						Boulders(0.15f, 8f, Boulder("Redbed", "Round"), Boulder("Redbed", "Slab"), Boulder("Redbed", "Spire")),
						Stones(SmallRocks("Redbed"), 6f), Trees(0.003f, 40f, 45f, "RedCedar")).Temp(0.18f, 1f).Ways(path: Ground.PathRedTrail)),
				C(Ground.CliffRedbedGypsum, 40f), Ground.ClayOxbowOoze, Ground.SandPlatteBraided)
				// The rules the round retired (Woodland's oak and bramble, Steppe's feather grass, High Desert's rabbitbrush, the generic
				// dry and lush grasses and warm and tall flowers), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "GrassDry", "FlowersWarm", "Trees: Oak", "Bush: Bramble", "FlowersTall", "GrassFeather")
				.Moved("detail/0", "GrassLush")
				.Moved("detail/1", "Bush: Rabbitbrush")
				// Approved: the frozen reach's snow-crusted sward and the scorched reach's dust plain over the main ground.
				.ClimateGround("Frozen", L(Ground.SnowCrustSward, Band(Grass("GrassWheatgrass", 6f, 120), -1f, 0.3f)))
				.ClimateGround("Scorched", L(Ground.CrackedDustPlain, Grass("GrassGrama", 4f, 100)))
				.Ways(path: Ground.PathBisonTrail, road: Ground.PathTravoisTrace, cobbles: Ground.FlagstoneRedbed)
				.Bars(sand: Ground.SandFeldsparBar, gravel: Ground.GravelOgallala)
				// Own rock the red beds (approved); accepts the dark river-bluff shale, the Flint Hills limestone, the Dakota sandstone
				// rimrock and the rare pink quartzite knobs. Open prairie is stoneless.
				.Accepts(RegionalRocks.Redbed, RockTypes.Shale, RockTypes.Limestone, RockTypes.Sandstone, RockTypes.Quartzite)
				// Red-walled gypsum caves with white satin-spar veins; ochre, salt and red-bed copper workings.
				.CaveShell(CaveForm.Cave, "Redbed")
				.CaveShell(CaveForm.Grotto, "Redbed")
				.CaveShell(CaveForm.Mine, "Redbed")
				.Crystals((BiomeCaveCrystalKind.BariteRose, 0.4f), (BiomeCaveCrystalKind.SatinSpar, 0.35f), (BiomeCaveCrystalKind.Sunspoke, 0.25f))
				// The sunspoke's warm gold pulse; the pecked sun-sign rune stones at the shrines warm gold too. LATER (I6i-b G6): the
				// thunder-stone's violet flicker after rain (a second way mode); the warm pulse stands in.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse, warm: true)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse, warm: true)
				.Look(BiomeLookClass.Arid)
				// Approved: rivers, oxbows and playas bring the cranes, ducks, frogs and turtles.
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): bison herds wallowing and rubbing, pronghorn, prairie dogs at their mounds, burrowing owls, booming
				// prairie-chicken leks, sandhill-crane flocks, meadowlarks, swift fox, coyote, wolves trailing the herds, ferruginous hawk,
				// golden eagle, horned lizard, grasshopper clouds, fireflies in the draws; the spirit herd and the horned serpent.
				// Approved: dry moats (the bastioned villages' ditch); the Platte's tawny water, sandbars showing.
				.Moat(BiomeMoatWater.Dry)
				.WaterTint(new BiomeWaterLook(OpenHex("#a08c64"), OpenHex("#4a4030"), new Vector3(0.30f, 0.20f, 0.16f), 0.55f, OpenHex("#b09a70"), OpenHex("#f0ece0")))
				// American lotus on the oxbows and wet playas, water clover in the wallow pools; split-cottonwood landing walks.
				.Floats((BiomeFloatingPlantKind.AmericanLotus, 0.6f), (BiomeFloatingPlantKind.WaterClover, 0.4f))
				.RaisesBoardwalks(BiomeBoardwalks.Paths, BiomeBoardwalkStyle.LandingWalk)
				.GrowsVines(0.08f, 0.35f, 0.05f, 0.15f, ("Bittersweet", 0.5f), ("WildCucumber", 0.3f), ("Groundnut", 0.2f))
				// Approved: buffalo wallows, playas, the mound towns' platform mounds and the conical burial mounds carved at the cut.
				.Landforms("Wallow", "Playa", "PlatformMound", "ConicalMound")
				.Describe("Mixed-grass plains under an endless sky — copper bluestem and bison wallows, cottonwood rivers, earth-lodge towns and red gypsum buttes")
				// Approved: a translated-compound voice; no real tribe names or sacred terms.
				.Names(onsets: new[] { "Red", "Lone", "Elk", "Bull", "Sun", "Thun", "Hawk", "Plum", "Crane", "Ash", "Smoke", "Sand" },
					nuclei: new[] { "a", "e", "o", "i", "u" },
					codas: new[] { "bluff", "ford", "draw", "butte", "lodge", "fork", "bend", "bottom", "wallow", "knoll", "breaks" },
					dungeonSuffixes: new[] { "Cache", "Den", "Wallow", "Sink", "Gyp-cave" },
					poiSuffixes: new[] { "Draw", "Breaks", "Crossing", "Bend", "Bottom", "Ford", "Knoll" },
					adjectives: new[] { "Wind-swept", "Copper-grassed", "Red", "Thunder-struck", "Herd-trodden", "Endless", "Sunlit", "Lonely" },
					poiWords: new[]
					{
						("village", new[] { "Earth Lodges", "Lodge Village" }), ("town", new[] { "Mound Town" }), ("ritualsite", new[] { "Sun Wheel" }),
						("huntinglodge", new[] { "Buffalo Jump" }), ("ruins", new[] { "Tipi Rings" }), ("landmark", new[] { "Thunder-stone", "Lone Cottonwood" }),
						("camp", new[] { "Tipi Ring", "Hunting Camp" }),
					},
					about: "A translated-compound voice of the plains (earth-lodge and bison-hunter register): invented names only.")
				.Blades("GrassWheatgrass", t => OpenBlade(t, "#5a6a5a", "#9aaa9e", 0.35f, clump: 0.2f, head: GrassBladeHead.SeedHead, headSize: 0.05f, headShare: 0.3f,
					heads: new[] { "#8a8a6a" }))
				.Blades("GrassLittleBluestem", t => OpenBlade(t, "#4a6a5a", "#a0603a", 0.3f, clump: 0.7f, head: GrassBladeHead.Plume, headSize: 0.03f, headShare: 0.25f,
					heads: new[] { "#e0dccc" }))
				.Blades("GrassGrama", t => OpenBlade(t, "#5a6a4a", "#8a9070", 0.4f, clump: 0.4f, head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.5f,
					heads: new[] { "#7a4a6a" }))
				.Blades("GrassBuffalo", t => OpenBlade(t, "#5a6a48", "#8a9068", 0.45f, density: 1f, clump: 0.05f))
				.Blades("GrassBigBluestem", t => OpenBlade(t, "#3e5a3a", "#7a6a4a", 0.3f, clump: 0.4f, wade: 0.2f, head: GrassBladeHead.SeedHead, headSize: 0.08f,
					headShare: 0.35f, heads: new[] { "#6a2a4a", "#c89a3a" }))
				.Blades("GrassSwitchgrass", t => OpenBlade(t, "#4a6a3a", "#7a8a5a", 0.35f, clump: 0.5f, head: GrassBladeHead.Plume, headSize: 0.1f, headShare: 0.4f,
					heads: new[] { "#8a4a5a" }))
				.Blades("GrassSandReed", t => OpenBlade(t, "#7a7a5a", "#c8b07a", 0.25f, clump: 0.3f, head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.3f,
					heads: new[] { "#d8c088" }))
				.Blades("FlowersConeflower", t => OpenBlade(t, "#4a6a34", "#5a7a3a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.05f,
					heads: new[] { "#c87ab0", "#8a4a1a", "#f0c020", "#c8301a" }))
				.Blades("FlowersSunflower", t => OpenBlade(t, "#4a6a2a", "#5a7a34", 0.3f, head: GrassBladeHead.Flower, sprinkle: 5f, headSize: 0.1f,
					heads: new[] { "#f0b820", "#3a2a1a" }))
				.Blades("FlowersBlazingStar", t => OpenBlade(t, "#4a6a3a", "#5a7a42", 0.35f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.04f,
					heads: new[] { "#8a3ab0", "#f2f0e6", "#8a3a8a", "#e8c020" }))
				.Blades("FlowersPrairieSmoke", t => OpenBlade(t, "#4a6a3a", "#5a7a42", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f,
					heads: new[] { "#b04a5a", "#e0a0b8" }))
				.Blades("FlowersSandhill", t => OpenBlade(t, "#5a6a4a", "#6a7a54", 0.3f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#f08a20", "#5a5ad8", "#f6f4ea" }))
				.Blades("FlowersPlaya", t => OpenBlade(t, "#5a7a3a", "#6a7a3a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f,
					heads: new[] { "#d87a9a", "#f0c020" }))
				// Approved settlement mix: earth-lodge villages and tipi camps first, mound towns, few castles, no mills.
				.Sites((POIType.Village, 1.6f), (POIType.Camp, 1.6f), (POIType.HuntingLodge, 1.2f), (POIType.RitualSite, 1.2f), (POIType.Landmark, 1.2f),
					(POIType.River, 1.2f), (POIType.TradingPost, 1f), (POIType.Barrow, 1f), (POIType.Ruins, 1f), (POIType.HerbGrove, 1f), (POIType.Graveyard, 0.9f),
					(POIType.Town, 0.8f), (POIType.StoneCircle, 0.8f), (POIType.Shrine, 0.8f), (POIType.Battlefield, 0.8f), (POIType.Nest, 0.8f),
					(POIType.Spring, 0.8f), (POIType.Keep, 1f), (POIType.FishingCamp, 0.6f), (POIType.AncientRoad, 0.6f), (POIType.Lake, 0.6f),
					(POIType.AncientTree, 0.6f), (POIType.MonsterDen, 0.6f), (POIType.Statue, 0.5f), (POIType.Cave, 0.5f), (POIType.Waystation, 0.4f),
					(POIType.Tower, 1f), (POIType.AbandonedFarm, 0.4f), (POIType.Grotto, 0.4f), (POIType.Quarry, 0.4f), (POIType.City, 0.3f),
					(POIType.Fortress, 0.3f), (POIType.Obelisk, 0.3f), (POIType.Bridge, 0.3f), (POIType.Mine, 0.3f), (POIType.OreVein, 0.3f),
					(POIType.Temple, 0.3f), (POIType.Capital, 0.15f), (POIType.Castle, 0.1f), (POIType.BanditCamp, 0.2f), (POIType.Monument, 0.2f),
					(POIType.FallenStar, 0.2f), (POIType.LeyNexus, 0.2f), (POIType.Portal, 0.1f), (POIType.Crypt, 0.1f), (POIType.Ossuary, 0.2f),
					(POIType.RuinedTower, 0.2f), (POIType.Hermitage, 0.2f), (POIType.FeyRing, 0.1f), (POIType.CrystalFormation, 0.2f),
					(POIType.DungeonEntrance, 0.2f), (POIType.BossLair, 0.2f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.RiverMouth, 1f),
					(POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f),
					(POIType.Island, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.4f), (POIType.GoblinWarren, 0.7f),
					(POIType.HauntedManor, 1f), (POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f),
					(POIType.WreckedCaravan, 0.6f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 0.8f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.8f), (POIType.SiegeCamp, 0.6f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.8f), (POIType.TitansBones, 0.5f), (POIType.WishingStone, 1f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.DruidGrove, 0.3f), (POIType.ElfWaystone, 0.4f),
					(POIType.Grange, 1.2f), (POIType.Dovecote, 1f), (POIType.BeeGarden, 1f), (POIType.VillageGreen, 0.6f), (POIType.ScarecrowField, 1.2f),
					(POIType.LongBarrow, 1f), (POIType.Henge, 1f), (POIType.GallowsHill, 0.8f), (POIType.StoneFigures, 0.4f), (POIType.RuneStone, 0.4f),
					(POIType.LadyOfTheLake, 0.3f), (POIType.NymphPool, 0.4f), (POIType.Commandery, 0.5f), (POIType.RoyalMausoleum, 0.4f))
				.Prefers(POIType.Village, "Longhouse", 1.2f)
				.Prefers(POIType.Village, "Timber", 0.1f)
				.Prefers(POIType.Village, "Stone", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Palisaded", 1.6f)
				.Prefers(POIType.Town, "Timber", 0.2f)
				.Prefers(POIType.Town, "Stone", 0f)
				.Prefers(POIType.City, "Timber", 1.6f)
				.Prefers(POIType.City, "Stone", 0.2f)
				.Prefers(POIType.Capital, "Timber", 1.6f)
				.Prefers(POIType.Capital, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 2f)
				.Prefers(POIType.Keep, "Stone", 0.1f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.Camp, "Hide Tents", 2.4f)
				.Prefers(POIType.Camp, "Lean-tos", 0.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 1.8f)
				.Prefers(POIType.HuntingLodge, "Lodge", 0.2f)
				.Prefers(POIType.StoneCircle, "Menhirs", 1.4f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.RitualSite, "Stones", 1.6f)
				.Prefers(POIType.RitualSite, "Bonefire", 0.6f)
				.Prefers(POIType.Ruins, "Excavation", 1.4f)
				.Prefers(POIType.Ruins, "Stone", 0.2f)
				.Prefers(POIType.Graveyard, "Earthen", 1.6f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Statue, "Broken", 1.6f)
				.Prefers(POIType.Barrow, "Ringed", 1.6f)
				.Prefers(POIType.MonsterDen, "Lair", 1.2f)
				.Prefers(POIType.FishingCamp, "Huts", 1.4f)
				.Prefers(POIType.TradingPost, "Market", 1.6f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Tower, "Timber", 1.4f)
				.Prefers(POIType.Tower, "Stone", 0.1f)
				.Prefers(POIType.Temple, "Court", 1.6f)
				.Prefers(POIType.Waystation, "Inn", 1f)
				.Prefers(POIType.Waystation, "Stone Inn", 0f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				// (the fantasy wave's variants end here)
				// LATER (I7): the Plains' signature pieces — the domed earth lodge on four posts with its covered entry (round plan,
				// decisions 31, I28), the bastioned village, the tipi ring, the mound town with its platform-mound temple, plaza and chunkey
				// yard, the cedar-post circle, the sun wheel (dawn glow), the buffalo jump with its cairn drive lanes, old tipi rings, burial
				// scaffolds, stone effigies, the bull-boat landing, corn stages and cache pits, the sweat lodge, the fair ground, the sod
				// watch knoll, the thunder-stone, the serpent pool, the spirit-herd trail (approved); until then the kit's variants below
				// stand in, the palette giving them sod, cottonwood, painted hide and bluestem thatch.
				.Favours(BiomeStructureStyle.Earth, 1.8f)
				.Favours(BiomeStructureStyle.Hide, 1.2f)
				.Favours(BiomeStructureStyle.Timber, 0.9f)
				.Favours(BiomeStructureStyle.Stone, 0.2f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: cut sod over cottonwood stringers domed at 35° (sunflowers and squash on the roof), grey cottonwood trim,
				// painted bison-hide door flaps, palisades of pointed cottonwood posts, red sandstone paving and dressings, rawhide and
				// bone fittings; the mound towns' clay-daub walls under steep bluestem thatch; painted-hide tipis at 60°. LATER (NEW-SURFACES
				// U3 and the rest): SodLodge, Bisonhide, Palisade, Daub, BluestemThatch and Feathered surfaces; the kit's earth, canvas, log,
				// plaster and thatch tinted stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, OpenHex("#6a5a42"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Earth, OpenHex("#6a5a42"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, OpenHex("#7e7a52"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, OpenHex("#7e7a52"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, OpenHex("#a8885a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, OpenHex("#6e5a42"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Fence, BiomeStructureMaterial.Log, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Paving, BiomeStructureMaterial.Sandstone, OpenHex("#9a5038"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, OpenHex("#a88a62"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, OpenHex("#e0d8c4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, OpenHex("#9a6a3a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Thatch, OpenHex("#9a6a3a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Fence, BiomeStructureMaterial.Log, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, OpenHex("#a8885a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, OpenHex("#a8885a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone, OpenHex("#9a4a30"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, OpenHex("#7e7a52"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, OpenHex("#7e7a52"), pitch: BiomeRoofPitch.Pitch35)
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, OpenHex("#8a8478"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, OpenHex("#6a5a42"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, OpenHex("#7e7a52"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Sandstone, OpenHex("#9a4a30"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, OpenHex("#8a2a1a"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, OpenHex("#7a6a52"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.BoneWork, OpenHex("#e0d6c0"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, OpenHex("#a8885a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, OpenHex("#8a2a1a"))
				// Approved: maize and squash in the Three-Sisters hills, sunflowers flat, small fields on the river bottoms.
				.Crops(("Detail_GrassFlintCorn", 0.5f), ("Detail_CropSquash", 0.2f), ("Detail_FlowersFieldSunflower", 0.3f))
				.CropSurface("Detail_GrassFlintCorn", BiomeFieldSurface.CornHills)
				.CropSurface("Detail_CropSquash", BiomeFieldSurface.CornHills)
				.FieldsNearRivers(150f)
				// Palisades of pointed cottonwood posts round the villages; cairn drive lanes along the buffalo-jump ridges.
				.Lines(new Entry.Line(BiomeLinearForm.Palisade, BiomeLinearPlace.SiteBounds),
					With(new Entry.Line(BiomeLinearForm.Cairn, BiomeLinearPlace.Lanes), l =>
					{
						l.Style.Spacing = 40f;
						l.Style.Sides = BiomeLinearSides.Both;
						l.Style.Density = 0.15f;
					}))
				// Cattail-and-tallow torches in the villages and trading fairs, clay fire-bowls round the plazas, cressets on the bastions,
				// rune stones pecked with sun signs at the shrines, ritual sites and landmarks, a brazier at a circle's centre post; the
				// sunspoke crystal posts down a cave's route, wall torches and braziers in the mines (no lanterns). Camps unlit.
				.Lights(POIType.Village, BiomeWayLightKind.TorchPost, 22f)
				.Lights(POIType.Town, BiomeWayLightKind.Brazier, 16f)
				.Lights(POIType.City, BiomeWayLightKind.Brazier, 16f)
				.Lights(POIType.Capital, BiomeWayLightKind.Brazier, 16f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.Shrine, BiomeWayLightKind.RuneStone, 0f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.RuneStone, 0f)
				.Lights(POIType.Landmark, BiomeWayLightKind.RuneStone, 0f)
				.Lights(POIType.StoneCircle, BiomeWayLightKind.Brazier, 0f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.WallTorch)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.WallTorch)
				.Gathers("Prairie Turnip", "Coneflower Root", "Wild Bergamot", "Groundnut Tubers", "Osage Bow-stave", "Cottonwood Driftwood", "Bison Bone Bed",
					"Red Ochre Pit", "Rose Rock Pocket", "Thunder-glass", "Sunspoke Cluster")
				.Herbs("Detail_HerbBergamot", "Detail_HerbPrairieTurnip", "Detail_Milkweed", "Detail_CompassPlant")
				// Approved: the new Dusted weathering (tawny and red dust drifted against the windward feet, sun-bleached timber and hide,
				// a mud-splash band) as a coat in place of moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Dusted, 0.4f, 0.5f)
				.ErodesAs("Open");
		}
	}
}
#endif
