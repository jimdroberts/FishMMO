#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Hills</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Hills()
		{
			// Terraced schist hill country (biome round, approved 2026-10-18, authored 2026-10-26): steep silver-grey hills stepped from foot
			// to shoulder by drystone terraces carrying goblet vines, ancient split-boled olives and almond blossom; above them sheep-bitten
			// tawny pasture starred with white asphodel, cork-oak and downy-oak montado on the gentler shoulders, tree-heath and broom on
			// abandoned terraces, white quartz reefs along the crests and great clearance cairns of flaggy stone. In the cold reach the same
			// hills become bare mat-grass fells under yellow cushion-broom, with snow-wells and corbelled shepherds' huts. Its people are
			// vine-dressers, olive-pressers, shepherds on the drove roads and gold-washers: dark drystone hamlets under heavy schist-slab
			// roofs with ox-blood doors and wooden balconies, granite granaries on stilts round a threshing floor, Iron-Age hill-forts on the
			// summits. Its fantasy is older than its people: the mouras combing their hair at the castro springs, the winged serpent on a
			// dolmen, a far candle procession on a night ridge, heart-red garnets in the old gold galleries. Three climate reaches (approved):
			// warm terraces (t ≥ 0.25), the temperate downy-oak and heath, the cold mat-grass fells. Scrubland, Karst and Woodland keep the
			// maquis, the holm oak and the chalk.
			// LATER (I9): every ground and object texture as the Hills' own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Asphodel V(0.96, 0.16, 0.92) and Olivine V(0.24, 0.22, 0.74).
			return E("Hills", L(Ground.GrassHillPasture,
					// Sheep's fescue turf; mat-grass in the cold reach, silverbeard in the warm; asphodel, the spring bulbs and fell flowers.
					Grass("GrassSheepFescue", 26f), Band(Grass("GrassMatgrass", 20f), -1f, -0.15f), Band(Grass("GrassSilverbeard", 4f), 0.25f, 1f),
					Band(Plants("FlowersAsphodel", 3f, 180), -0.2f, 1f), Plants("FlowersHillSpring", 5f, 180), Band(Plants("FlowersFell", 6f, 180), -1f, 0f),
					Plants("DebrisHill", 4f, 128), Stones(SmallRocks("Schist"), 2f), Stones(SmallRocks("Quartzite"), 0.5f),
					// Cushion broom on the cold fells, prickly juniper in the warm and temperate reach, stone pines on the crests.
					Band(Bush("CushionBroom", 1.5f), -1f, 0.2f), Band(Bush("PricklyJuniper", 0.3f), -0.2f, 1f), Band(Trees(0.002f, 90f, 25f, "StonePine"), 0.3f, 1f),
					// Schist ridges; the foliation upended into knife-edge fins along the strike on the 12–35° slopes. LATER (NEW-ROCK-TYPES):
					// Schist Fins; the slate's cleavage fins stand in.
					Blocks("Schist ridges", 0.004f, 40f, F(RockTypes.Schist, "Ridge")),
					With(Blocks("Schist fins", 0.01f, 30f, F(RockTypes.Slate, "CleavageFin")), s => s.Slope = new Vector2(12f, 35f)),
					// LATER (NEW-ROCK-TYPES): the clapas clearance cairn (a heap formation of the own rock); schist flag stacks stand in.
					With(Blocks("Clapas", 0.003f, 50f, F(RockTypes.Schist, "Flags")), s => s.Slope = new Vector2(0f, 15f)),
					Boulders(0.02f, 10f, Boulder("Schist", "Round"), Boulder("Schist", "Slab"), Boulder("Schist", "Spire"))),
				Ds(
					// d0: terrace tilth (≤ 10°, the terraces' height and every tread): weeds of the field edges, terrace olives and almonds,
					// cut olive stumps, Spanish broom at the edges, giant fennel, rue and the odd wild fig out of a riser.
					With(D(Ground.SoilTerraceTilth, 96f, 3f, 0f, 10f, Plants("FlowersTerraceWeeds", 8f, 180), Grass("GrassSheepFescue", 8f),
						Band(Trees(0.015f, 14f, 15f, "OliveTerrace"), 0.3f, 1f), Band(Trees(0.01f, 20f, 15f, "Almond"), 0.2f, 1f),
						Logs("Olive stumps", 0.02f, 20f, Wood("Stump_Olive")), Band(Bush("SpanishBroom", 0.5f), 0.15f, 1f), Band(Plants("GiantFennel", 0.6f), 0.1f, 1f),
						Plants("HerbRue", 1f), Band(Bush("WildFig", 0.05f), 0.15f, 1f)), l => l.Height = new Vector2(0.6f, 0.68f)),
					// d1: summer-burnt slopes (6–26°, the warm and temperate reach): silverbeard, asphodel, lavender, immortelle, tree-heath
					// and broom on the abandoned terraces, terebinth, the cork-oak montado and the north sides' downy oak, Caesar's mushrooms.
					D(Ground.GrassDryBurntSlope, 96f, 2.5f, 6f, 26f, Band(Grass("GrassSilverbeard", 14f), 0.25f, 1f), Grass("GrassSheepFescue", 6f),
						Plants("FlowersAsphodel", 8f, 180), Plants("FlowersHillSpring", 4f, 180), Plants("FlowersSchist", 3f, 180), Band(Plants("GiantFennel", 1f), 0.1f, 1f),
						Plants("HerbLavender", 2f), Plants("HerbImmortelle", 2f), Plants("DebrisHill", 3f, 128), Plants("MushroomCaesar", 0.3f),
						Bush("TreeHeath", 0.4f), Band(Bush("SpanishBroom", 0.8f), 0.15f, 1f), Band(Bush("Terebinth", 0.3f), 0.2f, 1f),
						Band(Trees(0.015f, 14f, 26f, "OliveTerrace"), 0.3f, 1f),
						Band(Stand(Was(0.12f, 14f), 18f, W(0.25f, 0.45f, open: 0.4f), "CorkOak"), 0.1f, 1f),
						Band(Stand(Was(0.15f, 10f), 26f, W(0.2f, 0.55f, open: 0.15f), "DownyOak"), -0.35f, 0.45f)).Temp(-0.2f, 1f),
					// d2: broom-and-heath fells (10–34°): cushion broom cold, tree-heath and lavender warm, the junipers; the drove roads over them.
					D(Ground.HeathBroomFell, 80f, 2.5f, 10f, 34f, Band(Bush("CushionBroom", 2.49f), -1f, 0.2f), Band(Bush("TreeHeath", 1.2f), -0.3f, 1f),
						Band(Plants("HerbLavender", 5f), -0.3f, 1f), Band(Bush("PricklyJuniper", 0.4f), -0.2f, 1f), Band(Bush("JuniperMat", 1f), -1f, -0.1f),
						Band(Grass("GrassMatgrass", 10f), -1f, -0.15f), Plants("DebrisHill", 2f, 128)).Ways(path: Ground.PathDrove, road: Ground.PathDrove),
					// d3: schist shatter (≥ 30°): lichen, melick at the rock foot, the schist flowers and immortelle, flaggy plates and boulders.
					D(Ground.RockSchistShatter, 48f, 4f, 30f, 90f, Plants("LichenSchist", 3f), Grass("GrassMelick", 6f, 200, 45f), Plants("FlowersSchist", 6f, 180, 45f),
						Plants("HerbImmortelle", 3f, 160, 45f), Band(Bush("Terebinth", 0.3f, 1f, 45f), 0.2f, 1f),
						Blocks("Schist flags", 0.006f, 30f, F(RockTypes.Schist, "Flags"), 70f),
						Boulders(0.08f, 10f, Boulder("Schist", "Round"), Boulder("Schist", "Slab"), Boulder("Schist", "Spire")),
						Stones(SmallRocks("Schist"), 6f), Stones(Pebbles("Schist"), 2f)),
					// d4: crest grus and quartz float on the windswept summits (≤ 20°, the high ground): the fell flowers, lichen, the white
					// quartz reefs. LATER (NEW-ROCK-TYPES): a Quartzite Reef shape (a milky vein wall along the crest); quartzite blocks stand in.
					With(D(Ground.GravelCrestQuartz, 64f, 3f, 0f, 20f, Stones(SmallRocks("Quartzite"), 3f), Plants("FlowersFell", 6f, 180), Plants("FlowersSchist", 4f, 180),
						Plants("LichenSchist", 2f), Blocks("Quartz reefs", 0.002f, 80f, F(RockTypes.Quartzite, "Block")), Band(Bush("JuniperMat", 1f), -1f, -0.1f),
						Band(Trees(0.002f, 90f, 20f, "StonePine"), 0.3f, 1f)), l => l.Height = new Vector2(0.71f, 1f)),
					// d5: the cold fells' frost-heaved turf (approved: ground layers banded by temperature).
					D(Ground.GrassFellTurf, 96f, 2f, 0f, 30f, Grass("GrassMatgrass", 20f), Plants("FlowersFell", 6f, 180), Bush("CushionBroom", 1.5f)).Temp(-1f, -0.15f)),
				C(Ground.CliffSchistCrenulated, 40f), Ground.SiltMica, Ground.PebblesSchistSlab)
				// The rules the round retired (Taiga's granite tors and corestones, Woodland's oak, hawthorn, bramble and bracken, Peat Bog's
				// heather, Tundra's dwarf birch, the gorse, cistus, olive, generic grass, flowers, tufts and grey boulders), switched off where
				// the tool wrote them and nobody tuned them since.
				.Moved("main", "GrassLush", "FlowersMeadow", "Trees: Oak", "Bush: Gorse", "Bush: Juniper", "Bush: Bramble", "Formations: Granite tors",
					"Bush: Hawthorn", "Trees: Olive", "Bush: Cistus", "Bush: DwarfBirch")
				.Moved("detail/0", "FernBracken")
				.Moved("detail/1", "Boulders", "Formations: Granite corestones")
				.Moved("detail/2", "Bush: Heather", "GrassTuft")
				.ClimateGround("Frozen", L(Ground.SnowHillCap, Grass("GrassMatgrass", 4f, 100)))
				.ClimateGround("Scorched", L(Ground.GrassDryBurntSlope, Grass("GrassSilverbeard", 12f), Plants("FlowersAsphodel", 6f, 180)))
				.Ways(path: Ground.PathSchistShard, road: Ground.PathRockCart, cobbles: Ground.FlagstoneSchistEdge)
				.Bars(sand: Ground.SandMicaSchist, gravel: Ground.GravelSchistShingle)
				// Own rock schist (approved; its formations); accepts the lauze slate, the crest quartzite, the causse limestone, the castro
				// knolls' granite. Gneiss dropped, the granite tors gone.
				.Accepts(RockTypes.Schist, RockTypes.Slate, RockTypes.Quartzite, RockTypes.Limestone, RockTypes.Granite)
				// Schist caves and the old gold galleries. LATER (NEW-ROCK-TYPES R56): the sluiced "ruined mountain" red-earth pinnacles.
				.CaveShell(CaveForm.Cave, "Schist")
				.CaveShell(CaveForm.Grotto, "Schist")
				.CaveShell(CaveForm.Mine, "Schist")
				// Almandine garnets, staurolite fairy crosses, the milky gold-flecked quartz, and the moura's heart beating red.
				.Crystals((BiomeCaveCrystalKind.Garnet, 0.45f), (BiomeCaveCrystalKind.Staurolite, 0.2f), (BiomeCaveCrystalKind.ClearQuartz, 0.2f),
					(BiomeCaveCrystalKind.Heartgarnet, 0.15f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.Look(BiomeLookClass.Arid)
				// Approved: scrub, grassland and the crags' eagles and ibex (no woodland).
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Grassland | BiomeHabitat.Mountain)
				// LATER (AMBIENT-LIFE): red-legged partridge coveys off the terraces, Bonelli's eagles, the blue rock thrush on the walls,
				// ocellated lizards basking, ibex on the crags, wild boar rooting the terraces at dusk, wolves in the cold reach; the Serpe.
				// Approved: dry rock-cut moats; gin-clear jade schist water.
				.Moat(BiomeMoatWater.Dry)
				.WaterTint(new BiomeWaterLook(OpenHex("#4c8a6c"), OpenHex("#0a2c28"), new Vector3(0.38f, 0.09f, 0.08f), 0.12f, OpenHex("#8a7e5c"), OpenHex("#f0f2ee")))
				.GrowsVines(0.15f, 0.6f, 0.3f, 0.04f, ("CaperHills", 0.5f), ("PergolaVine", 0.3f), ("EtruscanHoneysuckle", 0.2f))
				.Describe("Terraced schist hills — vines and olives on drystone terraces, slab-roofed hamlets, dolmens and old hill-forts")
				// Approved: a Lusitanian and Occitan voice.
				.Names(onsets: new[] { "Cas", "Pen", "Ser", "Ou", "Pu", "Mam", "Al", "Bar", "Cor", "Fon", "Mir", "Sal", "Vil", "Tor", "Lou", "Mon" },
					nuclei: new[] { "a", "e", "i", "o", "ei", "ou" },
					codas: new[] { "edo", "eira", "ac", "argues", "elo", "inho", "ade", "ol", "às" },
					middles: new[] { "ar", "el", "in", "ou" },
					dungeonSuffixes: new[] { "Mamoa", "Anta", "Gallery", "Fojo", "Cova", "Grotte" },
					poiSuffixes: new[] { "Castro", "Penha", "Serra", "Outeiro", "Puech", "Mamoa", "Fonte", "Mas" },
					adjectives: new[] { "Terraced", "Slab-roofed", "Silver", "Sun-baked", "Asphodel", "Schist-grey", "Vine-hung", "Old" },
					poiWords: new[]
					{
						("ruins", new[] { "Castro", "Old Fort" }), ("barrow", new[] { "Anta", "Mamoa", "Dolmen" }), ("spring", new[] { "Fonte", "Moura's Spring" }),
						("camp", new[] { "Bories", "Shepherds' Huts", "Jasse" }), ("village", new[] { "Aldeia", "Hamlet", "Mas" }),
					},
					about: "A Lusitanian and Occitan voice of the schist hills: invented names only.")
				.Blades("GrassSheepFescue", t => OpenBlade(t, "#4a5a2a", "#8a9048", 0.45f, clump: 0.2f, head: GrassBladeHead.Plume, headSize: 0.02f, headShare: 0.15f,
					heads: new[] { "#8a7a5a" }))
				.Blades("GrassSilverbeard", t => OpenBlade(t, "#6a6a3a", "#b08a4a", 0.3f, clump: 0.75f, head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.4f,
					heads: new[] { "#e8e4dc", "#a05a3a" }))
				.Blades("GrassMatgrass", t => OpenBlade(t, "#5a6a3a", "#b0aa80", 0.35f, stiffness: 1.4f, clump: 0.6f, head: GrassBladeHead.SeedHead, headSize: 0.03f,
					headShare: 0.3f, heads: new[] { "#6a5a6a" }))
				.Blades("GrassMelick", t => OpenBlade(t, "#5a6a40", "#9aa070", 0.3f, head: GrassBladeHead.Plume, headSize: 0.06f, headShare: 0.5f,
					heads: new[] { "#f0ece0" }))
				.Blades("FlowersAsphodel", t => OpenBlade(t, "#5a6a3a", "#6a7a44", 0.35f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#f4f2ec", "#8a5a3a" }))
				.Blades("FlowersHillSpring", t => OpenBlade(t, "#4a6a34", "#5a7a3a", 0.45f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.025f,
					heads: new[] { "#f0d040", "#4a4ab0", "#a080d0", "#d878a8" }))
				.Blades("FlowersSchist", t => OpenBlade(t, "#5a6a4a", "#6a7a54", 0.4f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.03f,
					heads: new[] { "#e8e8e0", "#6a4ab0", "#f0c830", "#e070a0" }))
				.Blades("FlowersTerraceWeeds", t => OpenBlade(t, "#4a6a34", "#5a7a3a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.035f,
					heads: new[] { "#d82818", "#f0d020", "#c03a8a", "#d8a0c0" }))
				.Blades("FlowersFell", t => OpenBlade(t, "#4a6a3a", "#5a7a42", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.02f,
					heads: new[] { "#e090b0", "#8090e0", "#f4f4ee" }))
				// Approved settlement density: hamlets 1.2, towns 0.6, city 0.25, capital 0.1; more keeps, castles and towers; mines, mills.
				.Sites((POIType.Village, 1.2f), (POIType.Town, 0.6f), (POIType.City, 0.25f), (POIType.Capital, 0.1f), (POIType.Port, 0.2f), (POIType.Keep, 1f),
					(POIType.Castle, 1.1f), (POIType.Fortress, 0.4f), (POIType.Tower, 1.3f), (POIType.Waystation, 0.8f), (POIType.TradingPost, 0.5f),
					(POIType.Monastery, 1f), (POIType.Hermitage, 1f), (POIType.Shrine, 1f), (POIType.Spring, 1f), (POIType.Mine, 1.2f), (POIType.Quarry, 0.8f),
					(POIType.Windmill, 0.9f), (POIType.Watermill, 0.7f), (POIType.LumberMill, 0.05f), (POIType.LumberCamp, 0.1f), (POIType.HuntingLodge, 0.4f),
					(POIType.Camp, 1.2f), (POIType.BanditCamp, 0.8f), (POIType.MonsterDen, 0.7f), (POIType.Nest, 0.8f), (POIType.RitualSite, 0.6f),
					(POIType.StoneCircle, 0.2f), (POIType.Barrow, 1.3f), (POIType.Graveyard, 0.8f), (POIType.Battlefield, 0.6f), (POIType.Ruins, 1.1f),
					(POIType.RuinedTower, 0.8f), (POIType.Statue, 0.4f), (POIType.AncientRoad, 0.9f), (POIType.AbandonedFarm, 1f), (POIType.HerbGrove, 0.9f),
					(POIType.AncientTree, 0.6f), (POIType.WitchHut, 0.4f), (POIType.FeyRing, 0.05f), (POIType.CorruptedGrove, 0.1f), (POIType.Clearing, 0.1f),
					(POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Waterfall, 1f), (POIType.Grotto, 1f), (POIType.Overhang, 1f),
					(POIType.Cave, 1f), (POIType.DungeonEntrance, 1f), (POIType.Bridge, 1f), (POIType.River, 1f), (POIType.Rapids, 1f), (POIType.Lake, 1f),
					(POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Mesa, 0.5f), (POIType.Butte, 0.5f), (POIType.NaturalArch, 1f), (POIType.Island, 1f),
					(POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.SeaCave, 0.5f), (POIType.FishingCamp, 0.5f), (POIType.Temple, 0.5f),
					(POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.5f), (POIType.Portal, 0.3f), (POIType.Crypt, 0.6f), (POIType.Ossuary, 0.4f),
					(POIType.Obelisk, 0.2f), (POIType.Monument, 0.5f), (POIType.Lighthouse, 0.3f), (POIType.SmugglersCove, 0.3f), (POIType.PirateCove, 0.2f),
					(POIType.SunkenShip, 0.5f), (POIType.SunkenRuins, 0.5f), (POIType.Dockyard, 0.2f), (POIType.BossLair, 0.8f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 1f), (POIType.GoblinWarren, 1.4f),
					(POIType.HauntedManor, 1f), (POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f),
					(POIType.WreckedCaravan, 0.6f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 0.8f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.8f), (POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 0.5f), (POIType.WishingStone, 0.6f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f), (POIType.KoboldDelving, 1.2f),
					(POIType.DruidGrove, 0.3f), (POIType.TrollStones, 1f), (POIType.ElfWaystone, 0.4f), (POIType.WolfDen, 0.8f), (POIType.Grange, 1.2f),
					(POIType.Dovecote, 1f), (POIType.BeeGarden, 1f), (POIType.VillageGreen, 0.6f), (POIType.ScarecrowField, 1.2f),
					(POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f), (POIType.GallowsHill, 0.8f), (POIType.StoneFigures, 0.4f),
					(POIType.Shieling, 0.8f), (POIType.RockCutFacade, 0.4f), (POIType.CliffPalace, 0.4f), (POIType.DwarvenGate, 1.2f),
					(POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f), (POIType.Eyrie, 1.2f), (POIType.GriffinRoost, 1f),
					(POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f), (POIType.SilverMineTown, 0.6f),
					(POIType.FrostGiantSteading, 0.5f), (POIType.RuneStone, 0.4f), (POIType.LadyOfTheLake, 0.3f), (POIType.NymphPool, 0.4f),
					(POIType.Commandery, 0.5f), (POIType.RoyalMausoleum, 0.4f))
				.Prefers(POIType.Village, "Timber", 0.3f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Town, "Timber", 0.3f)
				.Prefers(POIType.Town, "Palisaded", 0.1f)
				.Prefers(POIType.Town, "Stone", 1.8f)
				.Prefers(POIType.City, "Stone", 1.6f)
				.Prefers(POIType.City, "Timber", 0.2f)
				.Prefers(POIType.Capital, "Stone", 1.6f)
				.Prefers(POIType.Capital, "Timber", 0.2f)
				.Prefers(POIType.Port, "Timber", 0.6f)
				.Prefers(POIType.Port, "Stone", 2f)
				.Prefers(POIType.Waystation, "Stone Inn", 2f)
				.Prefers(POIType.Waystation, "Inn", 0.3f)
				.Prefers(POIType.Windmill, "Post", 1.6f)
				.Prefers(POIType.Watermill, "Wheel", 1.8f)
				.Prefers(POIType.StoneCircle, "Menhirs", 0.6f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1f)
				.Prefers(POIType.Graveyard, "Railed", 0.4f)
				.Prefers(POIType.RitualSite, "Stones", 1.8f)
				.Prefers(POIType.RitualSite, "Bonefire", 0.2f)
				.Prefers(POIType.Monastery, "Cloister", 1.4f)
				.Prefers(POIType.Monastery, "Timber", 0.2f)
				.Prefers(POIType.AbandonedFarm, "Farmstead", 1.4f)
				.Prefers(POIType.Castle, "Highland", 2f)
				.Prefers(POIType.Castle, "Stone", 1f)
				.Prefers(POIType.Keep, "Stone", 1.6f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Stone", 2f)
				.Prefers(POIType.Tower, "Timber", 0.1f)
				.Prefers(POIType.Tower, "Observatory", 0.6f)
				.Prefers(POIType.Hermitage, "Stone", 1.6f)
				.Prefers(POIType.Hermitage, "Cottage", 0.5f)
				.Prefers(POIType.Camp, "Crude", 1.6f)
				.Prefers(POIType.Camp, "Lean-tos", 0.6f)
				.Prefers(POIType.Camp, "Hide Tents", 0.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Barrow, "Ringed", 1.9f)
				.Prefers(POIType.Ruins, "Stone", 2.2f)
				.Prefers(POIType.Ruins, "Burned", 0.3f)
				.Prefers(POIType.Mine, "Adit", 1.2f)
				.Prefers(POIType.Mine, "Shaft Head", 0.6f)
				.Prefers(POIType.Mine, "Abandoned", 1.6f)
				.Prefers(POIType.Mine, "Delving", 0.3f)
				.Prefers(POIType.Quarry, "Cut Stone", 1f)
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
				.Prefers(POIType.MountainKingsHall, "Delving Hall", 1.2f)
				.Prefers(POIType.SilverMineTown, "Timber Camp", 1.3f)
				.Prefers(POIType.DwarvenGate, "Rune Door", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7): the Hills' signature pieces — the terrace flight's through-stone stairs, the two-storey schist hamlet house with
				// its outside stair and balcony, the granite granary on staddles round a threshing floor, the wine-treading tank and beam
				// press, the corbelled shepherds' hut and the drystone fold, the Iron-Age hill-fort with its round houses, the passage dolmen
				// under its cairn, the snow-well, the crest watch-tower, the triangular-sailed tower mill, the horizontal-wheel stream mill,
				// the sluiced gold diggings, the moura's spring, the petroglyph rock, the candle procession on a far ridge (approved); until
				// then the kit's variants below stand in, the palette giving them dark schist, lauze slabs and ox-blood doors.
				// LATER (I7): the corbelled shepherds' huts ("Bories"); crude huts stand in.
				// LATER (I7): the passage dolmen ("Anta") and the hill-fort ("Castro"); the ringed barrow and the stone ruins stand in.
				// LATER (I7): the sluiced gold diggings ("Sluiced").
				// LATER (I7): the tower mill's triangular sails ("Tower") and the horizontal-wheel stream mill ("Rodízio"); the post mill
				// and the wheel stand in.
				// LATER (I7): the petroglyph rock ("Petroglyph"); the standing stones stand in.
				.Favours(BiomeStructureStyle.Stone, 1.8f)
				.Favours(BiomeStructureStyle.Hide, 0.4f)
				.Favours(BiomeStructureStyle.Earth, 0.4f)
				.Favours(BiomeStructureStyle.Timber, 0.3f)
				.Favours(BiomeStructureStyle.Iron, 0.1f)
				// The palette: dark schist drystone in thin flat-bedded plates under heavy lauze slabs (yellow-lichened), granite plinths,
				// quoins and carving, ox-blood doors, dark oak trim, wooden balconies, undyed wool; the warm towns' rammed earth under curved
				// terracotta tiles; the shepherds' conical straw roofs. LATER (NEW-SURFACES U1 / I9): the Lauze (schist-slab) and CanalTile
				// surfaces; the kit's slate and a terracotta-tinted shingle stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, OpenHex("#5a564c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate, OpenHex("#5a564c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, OpenHex("#a8a296"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, OpenHex("#3e2e22"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, OpenHex("#7a2a22"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Stone, OpenHex("#6a6458"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, OpenHex("#605c54"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, OpenHex("#5a4632"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, OpenHex("#5a564c"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate, OpenHex("#5a564c"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, OpenHex("#3e2e22"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, OpenHex("#b08458"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, OpenHex("#e0d8c4"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, OpenHex("#b8643a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, OpenHex("#9a4a2a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, OpenHex("#6a5a3a"), pitch: BiomeRoofPitch.Pitch50)
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, OpenHex("#4e4a40"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, OpenHex("#5a5a30"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, OpenHex("#b0aa9e"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, OpenHex("#b0aa9e"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, OpenHex("#4a3a2a"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, OpenHex("#2a2624"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, OpenHex("#d4ccb8"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, OpenHex("#8a2a22"))
				// Approved: goblet vines and saffron on carved terraces with drystone risers, lavender in rows on the plateau tops, a little
				// hill barley, no wheat; olive and almond orchards on the terraces; old flights of abandoned terraces round the hamlets.
				.Crops(("Detail_CropVineGoblet", 0.5f), ("Detail_FlowersSaffron", 0.25f), ("Detail_HerbLavender", 0.15f), ("Detail_GrassBarleyHill", 0.1f))
				.CropSurface("Detail_CropVineGoblet", BiomeFieldSurface.Terraced)
				.CropRiser("Detail_CropVineGoblet", BiomeTerraceRiser.Drystone)
				.CropSurface("Detail_FlowersSaffron", BiomeFieldSurface.Terraced)
				.CropRiser("Detail_FlowersSaffron", BiomeTerraceRiser.Drystone)
				.CropRows("Detail_HerbLavender", 1.5f)
				.Orchards(new Entry.Orchard("OliveTerrace", 0.6f, 8f, "Detail_GrassSheepFescue", BiomeFieldSurface.Terraced, BiomeTerraceRiser.Drystone),
					new Entry.Orchard("Almond", 0.4f, 7f, "Detail_FlowersTerraceWeeds", BiomeFieldSurface.Terraced, BiomeTerraceRiser.Drystone))
				.AbandonedTerraces(3, BiomeTerraceRiser.Drystone, 8f, 30f, 0.5f)
				// Approved: drystone walls of dark schist plates (field walls, sheepfolds, terrace edges); clapas banks of cleared stone
				// along the field edges.
				.Lines(With(new Entry.Line(BiomeLinearForm.Drystone, BiomeLinearPlace.FieldEdges), l => SchistWall(l)),
					With(new Entry.Line(BiomeLinearForm.Drystone, BiomeLinearPlace.Folds), l => SchistWall(l)),
					With(new Entry.Line(BiomeLinearForm.Drystone, BiomeLinearPlace.TerraceEdges), l => SchistWall(l)),
					new Entry.Line("ClearanceRow"))
				// Soul-niche pillars in the hamlets and along the old roads, lantern posts in the towns, cressets on the ridge strongholds,
				// torches in the shepherds' camps, a lamp stone at the heart of a castro or an anta; heartgarnet crystal posts down a cave's
				// route, cressets at its camps, hanging brackets, lantern stands and wall lanterns in the gold galleries.
				.Lights(POIType.Village, BiomeWayLightKind.Alminha, 22f)
				.Lights(POIType.Waystation, BiomeWayLightKind.Alminha, 30f)
				.Lights(POIType.AncientRoad, BiomeWayLightKind.Alminha, 30f)
				.Lights(POIType.Hermitage, BiomeWayLightKind.Alminha, 30f)
				.Lights(POIType.Town, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.City, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.Capital, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Tower, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Camp, BiomeWayLightKind.TorchPost, 16f)
				.Lights(POIType.Ruins, BiomeWayLightKind.RuneStone, 0f)
				.Lights(POIType.Barrow, BiomeWayLightKind.RuneStone, 0f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Cresset)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.HangingBracket, 17f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.WallLantern)
				.Gathers("Iron Vein", "Gold Vein", "Saffron Crocus Patch", "Cork Bark", "Wild Lavender", "Caper Cluster", "Mica Sheets", "Heath Briar", "Moura's Comb")
				.Herbs("Detail_HerbLavender", "Detail_HerbImmortelle", "Detail_HerbRue", "Detail_HerbSavory")
				// Approved: the Lichen weathering (yellow-orange crustose on the dry sunlit schist; never moss).
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Lichen, 0.25f, 0.8f, OpenHex("#c8b848"))
				.ErodesAs("Temperate");
		}

		/// <summary>The Hills' drystone walls: thin flat-bedded schist plates, no mortar (its linear walls' material).</summary>
		private static void SchistWall(Entry.Line line)
		{
			line.Style.OwnMaterial = true;
			line.Style.Material = BiomeStructureMaterial.Fieldstone;
			line.Style.Tint = OpenHex("#5a564c");
		}
	}
}
#endif
