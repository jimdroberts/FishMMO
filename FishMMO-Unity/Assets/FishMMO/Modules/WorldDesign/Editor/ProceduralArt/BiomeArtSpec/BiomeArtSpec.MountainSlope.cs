#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Mountain Slope</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MountainSlope()
		{
			// The Afromontane cloud forest of the highlands (biome round, approved 2026-10-18, authored 2026-10-26): steep red-earth
			// slopes lost in cloud, a dripping forest of moss-sleeved yellowwoods and pink-tasselled kosso hung with old-man's-beard,
			// gold hypericum in the glades; above it the frost-moor of giant heathers, giant groundsels and blue lobelia spires; below
			// it the warm coffee forest under strangler-fig giants and tree ferns in the ravines; grey-green phonolite ledges, red
			// rain-gullied mule tracks and red-brown falls into the gorges. Its people keep round thatched tukuls in enset gardens,
			// round churches in islands of sacred forest, churches hewn down into red tuff and monasteries on sheer-sided ambas; its
			// fantasy is the Moon's: moonstone glowing blue in the caves and on the pilgrim road, clinkstone that rings when cloud
			// pours over it, a horned drake the colour of moss. Four reaches: the FROST-MOOR (t −0.45…0.05), the CLOUD FOREST core,
			// the COFFEE FOREST (t ≥ 0.55) and a DRY reach (h ≤ 0.15) of pencil cedar and candelabra euphorbia.
			// LATER (I9): every ground and object texture as Mountain Slope's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Jadeglow (luminous emerald) + Lapisveil (lapis-blue mist).
			return E("Mountain Slope", L(Ground.ForestFloorCloudHumus,
					// The cloud forest: kosso with the emergent yellowwoods over it, crowns closing; the yellowwoods also alone.
					// LATER (I9b P2): Hagenia's pink-red flower tassels (a TreeSpecies tassel field); its leaves stand alone.
					Band(Stand(Was(0.15f, 10f), 40f, W(0.85f, 0.9f, open: 0.04f), "Hagenia", "Podocarpus"), 0.05f, 0.55f),
					Trees(0.02f, 18f, 40f, "Podocarpus"),
					// The dry reach's pencil cedar woods and candelabra euphorbia.
					Band(Stand(Was(0.1f, 12f), 35f, W(0.5f, 0.6f), "PencilCedar"), 0.05f, 1f, -1f, 0.15f),
					// LATER (I9b P3): the candelabra's many upturned arms (cactus form, Branches > 3); three arms stand in.
					Band(Trees(0.01f, 16f, 45f, "Candelabra"), 0.3f, 1f, -1f, 0.2f),
					Band(Grass("GrassBasket", 6f), 0.05f, 1f), Plants("FernMontane", 10f), Plants("CushionCloudMoss", 3f), Plants("DebrisCloudForest", 10f, 128),
					Band(Plants("MushroomTermitomyces", 0.2f), 0.2f, 1f),
					Band(Bush("TreeHypericum", 0.6f), -0.3f, 0.5f), Band(Bush("Coffee", 0.2f), 0.5f, 1f, 0.1f, 1f), Bush("Gesho", 0.2f),
					Logs("Nurse logs", 0.15f, 12f, Wood("FallenLog_Podocarp")),
					// Phonolite: volcanic plugs on the slopes, rind boulders, moss-capped ledges.
					With(Outcrops("Phonolite plugs", 0.003f, 60f, F(RegionalRocks.Phonolite, "Plug"), 30f), s => s.Slope = new Vector2(10f, 30f)),
					Blocks("Phonolite rind", 0.05f, 10f, F(RegionalRocks.Phonolite, "Rind")),
					With(Outcrops("Phonolite ledges", 0.02f, 16f, F(RegionalRocks.Phonolite, "Ledges"), 36f), s => s.Slope = new Vector2(20f, 36f)),
					Boulders(0.2f, 8f, PhonoliteBoulders), Stones(SmallRocks("Phonolite"), 3f)),
				Ds(
					// d0: the moss blanket over roots and stones (moss clings steeper than the humus), tree ferns by the wet ravines.
					D(Ground.MossBlanket, 48f, 2.5f, 0f, 45f, Plants("FernMontane", 8f), Plants("CushionCloudMoss", 6f), Band(Grass("GrassBasket", 4f), 0.05f, 1f),
						Band(Plants("FlowersImpatiens", 2f, 180), 0f, 1f, 0.2f, 1f), Plants("DebrisCloudForest", 4f, 128),
						Band(Trees(0.12f, 6f, 42f, "CloudTreeFern"), 0.35f, 1f, 0.3f, 1f),
						Blocks("Mossy rind", 0.05f, 10f, F(RegionalRocks.Phonolite, "Rind")), Stones(SmallRocks("Phonolite"), 2f))
						.Ways(path: Ground.PathPhonoliteSteps),
					// d1: the kikuyu-grazed glades: Meskel gold after the rains, red-hot pokers, alchemilla, the frost-moor's tussocks above.
					D(Ground.GrassMeadowKikuyu, 64f, 2f, 0f, 28f, Band(Grass("GrassKikuyu", 24f), 0f, 1f), Band(Grass("GrassAfroTussock", 5f), -0.45f, 0.25f),
						Band(Plants("FlowersMeskel", 6f, 180), -0.1f, 0.8f), Band(Plants("FlowersTorchLily", 1.5f), -0.3f, 0.5f), Plants("HerbAlchemilla", 6f),
						Plants("HerbKoseret", 0.5f), Band(Plants("MushroomTermitomyces", 0.3f), 0.2f, 1f), Band(Plants("GiantLobelia", 0.05f, 160, 35f), -0.45f, 0.1f),
						Band(Trees(0.01f, 30f, 35f, "GiantGroundsel"), -0.45f, 0.05f), Bush("Gesho", 0.2f)),
					// d2: red nitisol of the tilled terraces and road cuts.
					D(Ground.SoilNitisol, 80f, 3f, 0f, 16f, Band(Grass("GrassKikuyu", 6f), 0f, 1f), Band(Plants("FlowersMeskel", 2f, 180), -0.1f, 0.8f),
						Stones(Pebbles("Phonolite"), 2f)),
					// d3: the ericaceous frost-moor of giant heathers, giant groundsels and lobelia spires.
					D(Ground.HeathEricaMoor, 72f, 2.5f, 0f, 35f, Grass("GrassAfroTussock", 10f), Plants("FlowersEverlasting", 4f), Plants("FlowersTorchLily", 1f),
						Plants("HerbAlchemilla", 4f), Plants("GiantLobelia", 0.15f, 160, 35f), Bush("SilverAlchemilla", 1.5f), Bush("TreeHypericum", 0.5f),
						Stand(Was(0.3f, 5f), 38f, W(0.6f, 0.8f, metres: 120f), "GiantHeather"), Trees(0.03f, 8f, 35f, "GiantGroundsel"),
						// LATER (I9b): a standing-snag deadwood form; a contorted dead heather as a tree stands in.
						Trees(0.02f, 20f, 35f, "SnagHeather"), Plants("DebrisCloudForest", 3f, 128)).Temp(-0.45f, 0.05f)
						.Ways(path: Ground.PathBundleCauseway, road: Ground.PathBundleCauseway),
					// d4: mossy phonolite ledges and platy scree on the steep ground; organ columns and ringing slab heaps.
					D(Ground.RockPhonoliteLedges, 32f, 4f, 36f, 90f, Plants("FernMontane", 4f, 160, 50f), Plants("CushionCloudMoss", 2f, 160, 60f),
						Band(Plants("FlowersEverlasting", 2f, 160, 50f), -0.45f, 0.2f), Outcrops("Phonolite organs", 0.01f, 30f, F(RegionalRocks.Phonolite, "Organ"), 60f),
						Blocks("Clinkstone slabs", 0.03f, 12f, F(RegionalRocks.Phonolite, "Clinkslabs"), 60f), Blocks("Phonolite scree", 0.05f, 10f, F(RegionalRocks.Phonolite, "Scree"), 60f),
						Band(Trees(0.01f, 16f, 60f, "Candelabra"), 0.3f, 1f, -1f, 0.2f), Boulders(0.4f, 6f, PhonoliteBoulders), Stones(SmallRocks("Phonolite"), 8f)),
					// d5: the warm coffee-forest floor under the strangler figs, tree ferns in the ravines.
					D(Ground.ForestFloorCoffee, 64f, 2f, 0f, 34f, Band(Bush("Coffee", 0.8f), 0.5f, 1f, 0.1f, 1f), Band(Grass("GrassBasket", 3f), 0.05f, 1f),
						Plants("FernMontane", 6f), Plants("DebrisFig", 6f, 128), Band(Trees(0.003f, 40f, 25f, "Warka"), 0.4f, 1f),
						Band(Trees(0.12f, 6f, 42f, "CloudTreeFern"), 0.35f, 1f, 0.3f, 1f)).Temp(0.55f, 1f)),
				C(Ground.CliffPhonolite, 40f), Ground.MudRedClay, Ground.PebblesRedSilted)
				// The rules the round retired (Alpine's dwarf pine and alpine flowers, Taiga's pine, larch, juniper and needles, Bamboo
				// Forest's rhododendron, the generic slate, grey stones and dry grass).
				.Moved("main", "Trees: Pine", "Bush: MountainPine", "Bush: Juniper", "Formations: Slate upright", "Formations: Slate", "Trees: Bristlecone",
					"Bush: Rhododendron", "DebrisNeedle", "Detail_Rocks_Grey")
				.Moved("detail/0", "Formations: Slate scree")
				.Moved("detail/1", "GrassTuft", "Trees: Larch", "FlowersAlpine")
				.Ways(path: Ground.PathRootStair, road: Ground.PathMuleGully, cobbles: Ground.FlagstoneSlabStair)
				.Bars(sand: Ground.SandPhonolite, gravel: Ground.GravelRedBlack)
				// Own rock phonolite; approved: accepts the trap basalt, the crystalline core's gneiss, andesite and the red tuff of the
				// rock-hewn churches. LATER (I9): the red tuff texture variant of the churches' trenches.
				.Accepts(RegionalRocks.Phonolite, RockTypes.Basalt, RockTypes.Gneiss, RockTypes.Andesite, RockTypes.Tuff)
				.CaveShell(CaveForm.Cave, "Phonolite").CaveShell(CaveForm.Grotto, "Phonolite").CaveShell(CaveForm.Mine, "Phonolite")
				.CaveShell(CaveForm.SeaCave, "Phonolite").CaveShell(CaveForm.LavaTube, "Basalt")
				// Opal nodules in the cliff bands, moonstone glowing blue, amethyst in the basalt geodes.
				.Crystals((BiomeCaveCrystalKind.Hydrophane, 0.5f), (BiomeCaveCrystalKind.Moonstone, 0.35f), (BiomeCaveCrystalKind.Amethyst, 0.15f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				// The ambas: sheer flat-topped mesas (approved as carved landforms). LATER (I7 I23): the amba monastery on its pad,
				// reached by a hauling rope up the cliff.
				.Landforms("Amba")
				.Look(BiomeLookClass.Forest)
				// Approved: the canopy's birds and monkeys (Forest) and the falls' and streams' life (FreshWater) beside the crag life.
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Forest | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): colobus troops, gelada herds at the cliff edges, walia ibex, crimson-winged turacos, malachite
				// sunbirds on the lobelias, tree hyrax screaming at night, three-horned chameleons, duikers, the frost-moor's wolf, honey-
				// guides by the hives; the colour-shifting drake on a mossy trunk (rare). LATER (WEATHER/sound): clinkstones ringing as
				// the cloud flows over them.
				.Moat(BiomeMoatWater.Wet)
				// The red-brown laterite water of the highland rivers; the falls take it (red smoking water into the gorges).
				.WaterTint(new BiomeWaterLook(UplandHex("#8a5a38"), UplandHex("#3a2416"), new Vector3(0.22f, 0.32f, 0.45f), 0.8f, UplandHex("#a8643a"), UplandHex("#e8dccc")))
				.Blades("GrassKikuyu", t => UplandBlade(t, "#3a5a24", "#6a9a3a", 0.45f, density: 1.5f, clump: 0.1f, width: 1.2f, heightScale: 0.6f))
				.Blades("GrassBasket", t => UplandBlade(t, "#2e4a1e", "#5a8a32", 0.5f, width: 2f, bend: 0.5f))
				.Blades("GrassAfroTussock", t => UplandBlade(t, "#5a5a32", "#b0a070", 0.35f, stiffness: 1.6f, width: 0.6f, clump: 0.85f, heightScale: 1.4f,
					head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.1f, heads: new[] { "#a89a7a" }))
				.Blades("FlowersMeskel", t => UplandBlade(t, "#4a6a2a", "#66843a", 0.35f, heightScale: 1.5f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.035f,
					heads: new[] { "#f0c018", "#e8b818" }))
				.Blades("FlowersImpatiens", t => UplandBlade(t, "#4a7a3a", "#5a8a44", 0.3f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f, headShare: 0.25f,
					heads: new[] { "#d8302a", "#e870a8", "#f4f0f0" }))
				.Blades("FlowersEverlasting", t => UplandBlade(t, "#8a9a88", "#a8b0a0", 0.35f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f,
					heads: new[] { "#f4f2ec", "#e0a0b0", "#d8b860" }))
				.Blades("FlowersTorchLily", t => UplandBlade(t, "#4a6a3a", "#5a7a44", 0.3f, heightScale: 1.8f, head: GrassBladeHead.Flower, sprinkle: 3f, headSize: 0.12f,
					heads: new[] { "#d8401c", "#f0c020" }))
				.Blades("GrassTeff", t => UplandBlade(t, "#6a8a3a", "#c8b080", 0.3f, width: 0.5f, head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.7f,
					heads: new[] { "#a85a3a", "#e8dcc0" }))
				.Blades("GrassEmmer", t => UplandBlade(t, "#5a7a5a", "#a88a5a", 0.3f, head: GrassBladeHead.SeedHead, headSize: 0.07f, headShare: 0.7f,
					heads: new[] { "#4a3a2a" }))
				.Describe("Cloud-wrapped mountainsides — moss-hung forest, giant heathers and lobelias, red-earth tracks, gorges and waterfalls, round churches and cliff monasteries")
				.Names(onsets: new[] { "Ab", "Ge", "Tse", "Me", "Zag", "Sem", "Wol", "La", "Deb", "Am", "Ak", "Lal", "Gon", "Ta" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ai" },
					codas: new[] { "wenz", "ber", "ra", "bo", "lo", "ma", "ge", "sa", "re", "nat" },
					middles: new[] { "ar", "en", "ir", "al" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Cliff Cells", "Bone Cave", "Drake Hollow", "Tuff Trench", "Moon Vault", "Gorge Cave" },
					dungeonPrefixes: new[] { "The Cloud-wrapped", "The Moss-hung", "The Moonlit", "The Red-water", "The Ringing", "The Watching" },
					poiSuffixes: new[] { "Debre", "Amba", "Gedam", "Tsebel", "Ber", "Wenz" },
					adjectives: new[] { "Cloud-wrapped", "Moss-hung", "Moon-blessed", "Red-earth", "Clinkstone", "Lobelia-crowned", "Ringing", "Honey-hived" },
					poiWords: new[]
					{
						("monastery", new[] { "Gedam", "Debre", "Amba" }),
						("hermitage", new[] { "Cliff Cell", "Hermit Ledge" }),
						("spring", new[] { "Tsebel", "Holy Spring" }),
						("shrine", new[] { "Holy Spring", "Cross-stone" }),
						("temple", new[] { "Rock-hewn Church", "Round Church" }),
						("obelisk", new[] { "Stelae", "Stele Field" }),
						("waterfall", new[] { "Smoking Water", "Red Falls" }),
						("gorge", new[] { "Wenz Gorge", "Red Gorge" }),
						("ancienttree", new[] { "Warka", "Meeting Fig" }),
						("stonecircle", new[] { "Singing Stones", "Clinkstones" }),
						("monsterden", new[] { "Drake Hollow" }),
						("castle", new[] { "Domed Castle" }),
						("village", new[] { "Mender", "Tukul Ring" }),
						("waystation", new[] { "Mead House" }),
					},
					about: "Highland voice of the cloud-forest mountains (Ab, Ge, Tse, Zag, Sem; -wenz river, -ber gate; Debre mount, Amba mesa, Gedam monastery)")
				// Monasteries, hermitages, shrines and churches up; villages, a town or two, a rare city (approved as drafted).
				.Sites((POIType.Monastery, 1.8f), (POIType.Hermitage, 1.4f), (POIType.Shrine, 1.4f), (POIType.Temple, 1.2f), (POIType.Obelisk, 1f),
					(POIType.Spring, 1.4f), (POIType.Waterfall, 1.6f), (POIType.Gorge, 1.5f), (POIType.Rapids, 1f), (POIType.Bridge, 1.4f),
					(POIType.AncientTree, 1f), (POIType.LavaTube, 0.4f), (POIType.HotSpring, 0.4f), (POIType.HerbGrove, 1.2f), (POIType.Cave, 1f),
					(POIType.Grotto, 1f), (POIType.Overhang, 1f), (POIType.Pass, 0.8f), (POIType.Peak, 0.6f), (POIType.Village, 0.8f), (POIType.Town, 0.4f),
					(POIType.City, 0.1f), (POIType.Capital, 0.05f), (POIType.Keep, 1f), (POIType.Castle, 0.5f), (POIType.Fortress, 0.3f),
					(POIType.Tower, 1f), (POIType.TradingPost, 0.6f), (POIType.Waystation, 0.8f), (POIType.Camp, 0.6f), (POIType.HuntingLodge, 0.4f),
					(POIType.Watermill, 0.4f), (POIType.Mine, 0.6f), (POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.6f), (POIType.Quarry, 0.3f),
					(POIType.MonsterDen, 0.8f), (POIType.Nest, 0.8f), (POIType.RitualSite, 0.6f), (POIType.StoneCircle, 0.5f), (POIType.LeyNexus, 0.5f),
					(POIType.Portal, 0.2f), (POIType.FallenStar, 0.3f), (POIType.BanditCamp, 0.6f), (POIType.Ruins, 0.8f), (POIType.RuinedTower, 0.4f),
					(POIType.Statue, 0.4f), (POIType.Graveyard, 0.6f), (POIType.Ossuary, 0.5f), (POIType.Crypt, 0.3f), (POIType.Barrow, 0.1f),
					(POIType.Battlefield, 0.4f), (POIType.AbandonedFarm, 0.4f), (POIType.AncientRoad, 0.8f),
					// Kept as they were: the landform kinds at the rule's 1, the dungeons and monuments.
					(POIType.Monument, 1f), (POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Mesa, 1f),
					(POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.NaturalArch, 1f), (POIType.SeaCave, 1f),
					(POIType.FishingCamp, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 1f), (POIType.GoblinWarren, 1f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 1.2f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f),
					(POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 1f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f),
					(POIType.WishingStone, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f),
					(POIType.KoboldDelving, 1.2f), (POIType.TrollStones, 1f), (POIType.WolfDen, 0.8f), (POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f),
					(POIType.Shieling, 0.8f), (POIType.RockCutFacade, 0.4f), (POIType.CliffPalace, 0.4f), (POIType.DwarvenGate, 1.2f),
					(POIType.MountainKingsHall, 1.2f), (POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f), (POIType.Eyrie, 1.2f),
					(POIType.GriffinRoost, 1f), (POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f),
					(POIType.PrayerFlagPass, 0.8f), (POIType.SilverMineTown, 1f), (POIType.FrostGiantSteading, 0.5f))
				.Prefers(POIType.Village, "Timber", 2f)
				.Prefers(POIType.Village, "Stone", 0.6f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.2f)
				.Prefers(POIType.Town, "Stone", 1.2f)
				.Prefers(POIType.Town, "Palisaded", 0.3f)
				.Prefers(POIType.City, "Stone", 1.5f)
				.Prefers(POIType.City, "Timber", 0.5f)
				.Prefers(POIType.Capital, "Stone", 1.5f)
				.Prefers(POIType.Capital, "Timber", 0.5f)
				.Prefers(POIType.Monastery, "Cloister", 0.3f)
				.Prefers(POIType.Monastery, "Timber", 0f)
				.Prefers(POIType.Temple, "Court", 0.2f)
				.Prefers(POIType.Temple, "Colonnade", 0f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.3f)
				.Prefers(POIType.TradingPost, "Market", 1.4f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0.2f)
				.Prefers(POIType.StoneCircle, "Menhirs", 0.2f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.Ossuary, "Tomb", 0.3f)
				.Prefers(POIType.MonsterDen, "Lair", 0.5f)
				.Prefers(POIType.MonsterDen, "Bones", 0.3f)
				.Prefers(POIType.AncientTree, "Shrine", 0.4f)
				.Prefers(POIType.Watermill, "Wheel", 0.2f)
				.Prefers(POIType.Camp, "Lean-tos", 0.8f)
				.Prefers(POIType.Camp, "Crude", 0.6f)
				.Prefers(POIType.Camp, "Hide Tents", 0.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Castle, "Highland", 0.4f)
				.Prefers(POIType.Castle, "Stone", 0.3f)
				.Prefers(POIType.Shrine, "Shrine", 0.6f)
				.Prefers(POIType.Shrine, "Statue", 0.1f)
				.Prefers(POIType.Bridge, "Timber", 0.3f)
				.Prefers(POIType.Waystation, "Inn", 0.3f)
				.Prefers(POIType.Tower, "Stone", 1f)
				.Prefers(POIType.Hermitage, "Stone", 0.6f)
				.Prefers(POIType.Hermitage, "Cottage", 0f)
				.Prefers(POIType.Obelisk, "Obelisk", 0.3f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.Keep, "Dwarven Hold", 1.2f)
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
				.Prefers(POIType.Grange, "Walled Barton", 1.3f)
				.Prefers(POIType.MountainKingsHall, "King's Gate", 1.3f)
				.Prefers(POIType.MountainKingsHall, "Kings Before the Gate", 1.2f)
				.Prefers(POIType.SilverMineTown, "Stone Delving", 1.3f)
				.Prefers(POIType.DwarvenGate, "Deep Gate", 1.2f)
				// (the fantasy wave's variants end here)
				// LATER (I7 I28): Mountain Slope's signature pieces — the round wattle-daub tukul under a conical thatch with a pot finial
				// (Village/Town "Tukul" 2.0/1.2; the kit's Timber village, daub and thatch in this palette, stands in at the tukul's
				// weight), the hidmo (the kit's Stone village), the round church in its sacred grove (Monastery "Round Church" 1.4), the
				// rock-hewn church in its tuff trench (Temple "Rock-hewn" 1.8), the amba monastery (Monastery "Amba" 1.6), cliff cells
				// with pole ladders (Hermitage "Cliff Cell" 1.6), the stelae field (Obelisk "Stelae" 1.8), the domed-tower castle (Castle
				// "Gondarine" 1.6), the holy spring chapel (Shrine "Holy Spring" 1.2), the gorge arch bridge (Bridge "Stone Arch" 1.4), the
				// mead house (Waystation "Tej House" 1.4), the singing stones (StoneCircle "Singing Stones" 1.4), the bone cave (Ossuary
				// "Bone Cave" 1.4), the drake hollow (MonsterDen "Drake Hollow" 1.2), the meeting fig (AncientTree "Warka" 1.6), the
				// horizontal mill (Watermill "Horizontal" 1.2), stone bell-frames, tree hives, threshing floors, enset gardens and the
				// moon sanctum. Until then the kit's variants below.
				.Favours(BiomeStructureStyle.Timber, 1.4f)
				.Favours(BiomeStructureStyle.Stone, 1.4f)
				.Favours(BiomeStructureStyle.Earth, 0.8f)
				.Favours(BiomeStructureStyle.Hide, 0.3f)
				.Favours(BiomeStructureStyle.Iron, 0.2f)
				// The palette: red-ochre straw daub under dark conical grass thatch, phonolite rubble with timber beam courses (the
				// "monkey-head" walls), flat earth roofs on the stone houses, carved juniper, brass-inlaid iron, white cotton cloth and
				// woven banded banners. LATER (NEW-SURFACES U11): ChikaDaub, HighlandThatch, AksumiteCourse, DungPlaster, Shamma and Tibeb
				// as Mountain Slope's own surfaces, the red tuff carving as its own; the shared surfaces tinted stand in.
				// LATER (I7 I28): round-plan walls under conical roofs for the tukuls and round churches.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, UplandHex("#9a5a36"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, UplandHex("#9a5a36"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, UplandHex("#5a4e3a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Thatch, UplandHex("#5a4e3a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, UplandHex("#6a4a30"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, UplandHex("#a8703a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, UplandHex("#5a4a34"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, UplandHex("#5a4e3a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, UplandHex("#6a5a40"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, UplandHex("#7a8070"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, UplandHex("#3e2a1c"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, UplandHex("#8a4a2e"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, UplandHex("#5a4e3a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#5a5e50"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, UplandHex("#7a6a4a"))
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, UplandHex("#7a5a3a"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, UplandHex("#646a5c"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, UplandHex("#6a705e"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, UplandHex("#4a3a26"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, UplandHex("#7a8070"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, UplandHex("#8a4a34"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, UplandHex("#3e2a1c"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, UplandHex("#4a3e2a"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, UplandHex("#f0ece0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, UplandHex("#2a3a7a"))
				// The cloud forest greens every wall: moss sleeves, beard-lichen tufts and ferns on the sills (approved: Epiphytic).
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Epiphytic, 0.6f, 1f)
				// Teff on earth-bunded terraces, enset gardens round the homesteads (a non-grass crop), emmer in carved broad beds.
				.Crops(("Detail_GrassTeff", 0.45f), ("Detail_Enset", 0.3f), ("Detail_GrassEmmer", 0.25f))
				.CropSurface("Detail_GrassTeff", BiomeFieldSurface.Terraced)
				.CropRiser("Detail_GrassTeff", BiomeTerraceRiser.EarthBund)
				.CropSurface("Detail_GrassEmmer", BiomeFieldSurface.RidgeAndFurrow)
				// The euphorbia hedges round homesteads and church compounds: gesho and thorn with candelabra standards.
				.Lines(new Entry.Line(BiomeLinearForm.Wild, BiomeLinearPlace.SiteBounds, "Gesho") { Standards = new[] { "Candelabra" } })
				// Oil lamps under the eaves, crook lanterns in the towns, jars of glow-worms on the cliff stairs, glowing cross-stones,
				// the pilgrim road shining blue with moonstone, cressets at the strongholds; the herders' camps unlit.
				.Lights(POIType.Village, BiomeWayLightKind.HangingBracket, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.City, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.Capital, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.Monastery, BiomeWayLightKind.Glowjar, 20f)
				.Lights(POIType.Temple, BiomeWayLightKind.Glowjar, 20f)
				.Lights(POIType.Shrine, BiomeWayLightKind.RuneStone, 10f)
				.Lights(POIType.AncientRoad, BiomeWayLightKind.CrystalPost, 28f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Waystation, BiomeWayLightKind.HangingBracket, 12f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.CrookLantern)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				// Moss sleeves on trunks, limbs and walls, and the white-starred highland jasmine.
				.GrowsVines(0.5f, 0.6f, 0.4f, 0.35f, ("MossSleeve", 0.6f), ("AbyssinianJasmine", 0.4f))
				// LATER (I7): the Wild Honey Hive node at the tree hives only.
				.Gathers("Laterite Ironstone", "Hydrophane Seam", "Moonstone Cluster", "Kosso Blossom", "Wild Coffee", "Gesho", "Termite Mushroom Ring",
					"Yellowwood Elder", "Wild Honey Hive")
				.Herbs("Detail_HerbKoseret", "Detail_FlowersEverlasting", "Detail_MushroomTermitomyces", "Detail_HerbAlchemilla")
				.ErodesAs("Mountain");
		}

		/// <summary>The phonolite boulders (I6b stones of the crust rock): round, slab and spire (the Mountain Slope file's).</summary>
		private static string[] PhonoliteBoulders => new[] { Boulder("Phonolite", "Round"), Boulder("Phonolite", "Slab"), Boulder("Phonolite", "Spire") };
	}
}
#endif
