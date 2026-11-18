#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Bamboo Forest</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry BambooForest()
		{
			// The bamboo hills of the old capital and the eastern shogun's city (biome round, approved 2026-10-18, authored 2026-10-26):
			// towering groves of pale moso and gold-green madake over a floor of straw leaves and culm sheaths, dark sugi on the ridges
			// and red pines on the crests, Japanese maples in the ravines, wild cherry clouding the slopes in spring, hydrangea-filled
			// temple valleys, moss lawns under the maples, red spider lilies along the paddy bunds, andesite screens of cliff and moss-
			// green stone lanterns. Its people are paddy and tea farmers, bamboo-cutters, charcoal-burners, monks and the warriors of
			// hill forts; its fantasy the moon-culm, the fox shrines, the tengu cedar, kodama and firefly glass. Reaches: CAPITAL
			// (warmth below 0.55: madake as well as moso, maple, red pine) and LAUREL (0.55 and up: moso and the shii forest), the humid
			// reach (0.5 and up) laying moss lawns.
			// LATER (I9): every ground and object texture as Bamboo Forest's own variant of its recipe (decision 12).
			// LATER (I9b `Running`): single leptomorph culms spaced through the stand; the clump form stands in for moso and madake.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Sumie + Kaguya (BiomeTemplate.rareLookA/B).
			return E("Bamboo Forest", L(Ground.ForestFloorBambooGrove,
					// The groves: moso everywhere, madake mixed in the cooler reach.
					Band(Stand(Was(3f, 3f), 25f, W(0.7f, 0.5f, metres: 200f), "Moso", "Moso", "Madake"), -1f, 0.55f),
					Band(Stand(Was(3f, 3f), 25f, W(0.7f, 0.5f, metres: 200f), "Moso"), 0.55f, 1f),
					Trees(0.004f, 30f, 30f, "Yamazakura"),
					Grass("GrassSusuki", 6f), Grass("GrassRyunohige", 6f), Plants("FlowersShaga", 8f), Plants("FlowersHiganbana", 1f),
					Plants("Takenoko", 2f), Plants("DebrisCulmSheath", 6f, 128), Plants("DebrisCutCulm", 1f, 128), Plants("FernKurawarabi", 2f),
					Plants("MushroomKinugasa", 0.3f), Bush("KumaZasa", 1.11f), Band(Bush("Yamabuki", 1f), -1f, 0.6f),
					Logs("Fallen logs", 0.02f, 10f, Wood("Culm_Fallen")),
					Stones(Pebbles("Andesite"), 2f),
					Boulders(0.03f, 14f, Boulder("Andesite", "Round"), Boulder("Andesite", "Slab"))),
				Ds(
					// d0: the sugigoke moss lawn of the humid reach round temples, shrines and water: mondo grass, cushion moss, dogtooth
					// violets in the cooler reach, standing stones in the garden.
					D(Ground.MossSugigoke, 40f, 2f, 0f, 20f, Plants("MossKoke", 6f), Grass("GrassRyunohige", 10f), Band(Plants("FlowersKatakuri", 2f), -1f, 0.55f),
						Blocks("Garden stones", 0.01f, 16f, F(RockTypes.Andesite, "Tateishi")), Stones(SmallRocks("Andesite"), 1f))
						.Humid(0.5f, 1f)
						.Ways(path: Ground.PathTobiishi),
					// d1: sugi and red-pine duff on the ridges: cedars, pines, matsutake, asebi and the ridge azalea.
					D(Ground.NeedleLitterSugi, 80f, 2f, 15f, 32f, Stand(Was(1f, 12f), 35f, W(0.6f, 0.8f), "Sugi"), Trees(0.01f, 16f, 32f, "Akamatsu"),
						Plants("MushroomMatsutake", 0.4f), Bush("KumaZasa", 1.11f), Bush("Asebi", 1.5f), Bush("YamaTsutsuji", 1f),
						Logs("Mossy cedars", 0.004f, 30f, Wood("FallenLog_Sugi")), Logs("Pine snags", 0.002f, 40f, Wood("Snag_Akamatsu"))),
					// d2: the black andosol tilth of the banks and villages: autumn seven, spider lilies, susuki, hydrangea and tea.
					D(Ground.SoilKuroboku, 120f, 2f, 0f, 12f, Plants("HerbDokudami", 3f), Plants("FlowersAkiNanakusa", 6f), Plants("FlowersHiganbana", 6f),
						Grass("GrassSusuki", 12f), Plants("HerbSenburi", 1f), Plants("HerbGennosho", 2f), Bush("Ajisai", 1.96f), Bush("Chanoki", 0.2f),
						Bush("Yamabuki", 1.5f)),
					// d3: the andesite talus and the folding-screen ribs of the steep ground.
					D(Ground.RockAndesiteTalus, 96f, 3f, 32f, 55f, Outcrops("Byobu ribs", 0.006f, 40f, F(RockTypes.Andesite, "Byobu"), 60f),
						Outcrops("Iwakura seats", 0.002f, 80f, F(RockTypes.Andesite, "Iwakura"), 45f),
						Blocks("Andesite talus", 0.03f, 12f, F(RockTypes.Andesite, "Talus"), 60f), Boulders(0.1f, 10f, Boulder("Andesite", "Round"), Boulder("Andesite", "Spire")),
						Stones(SmallRocks("Andesite"), 3f), Bush("KumaZasa", 1.11f), Bush("Asebi", 1f), Plants("MossKoke", 2f))
						.Ways(path: Ground.PathIshidan),
					// d4: the laurel ravine of the warm reach: the shii forest, maples, painted fern, Hakone grass and iris.
					D(Ground.ForestFloorLaurel, 70f, 2f, 0f, 30f, Stand(Was(0.5f, 8f), 22f, W(0.6f, 0.8f), "Shii"), Trees(0.01f, 14f, 30f, "IrohaMomiji"),
						Plants("FernKurawarabi", 4f), Grass("GrassHakone", 3f), Plants("FlowersShaga", 4f), Bush("Ajisai", 1f), Plants("FernZenmai", 2f)).Temp(0.55f, 1f)),
				C(Ground.CliffAndesitePlated, 45f), Ground.SiltKurobokuPond, Ground.GravelAndesiteBed)
				// The rules the round retired (the clump bamboo, tree fern, fern, tall grass, rhododendron, dwarf bamboo, bamboo litter and
				// the generic forest mushrooms).
				.Moved("main", "Trees: Bamboo", "Fern", "GrassTall", "Bush: Rhododendron", "Bush: DwarfBamboo", "DebrisBamboo", "MushroomForest")
				.Moved("detail/0", "Trees: TreeFern", "Fern")
				.Ways(path: Ground.PathBambooLeaf, road: Ground.RoadMasa, cobbles: Ground.FlagstoneIshidatami)
				.Bars(sand: Ground.SandMasaGrus, gravel: Ground.GravelArc)
				// Own rock andesite (Byobu, Iwakura, Tateishi, Block, Platy, Talus); approved: accepts granite (masa), schist (green garden
				// stone) and tuff (the green Kamakura stone); gneiss, limestone and basalt dropped. LATER (I9): the Komatsu look.
				.Accepts(RockTypes.Andesite, RockTypes.Granite, RockTypes.Schist, RockTypes.Tuff)
				.CaveShell(CaveForm.Cave, "Andesite").CaveShell(CaveForm.Grotto, "Tuff").CaveShell(CaveForm.Mine, "Andesite")
				.Crystals((BiomeCaveCrystalKind.Twinquartz, 0.4f), (BiomeCaveCrystalKind.Jadeite, 0.3f), (BiomeCaveCrystalKind.Hotarubi, 0.3f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.Landforms("Kofun")
				.Look(BiomeLookClass.Jungle)
				// Approved: streams for the fireflies, macaques on the slopes; no jungle fauna.
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Mountain | BiomeHabitat.FreshWater | BiomeHabitat.Settlement)
				// LATER (AMBIENT-LIFE): Genji fireflies over streams and paddies, macaque troops, sika deer, tanuki, serow, herons and
				// cranes in the paddies, kites, bush warblers, cicada drone; foxfire wisps and kodama motes.
				// LATER (RARE-LOOKS): Sumie and Kaguya.
				.Moat(BiomeMoatWater.Dry)
				.Floats((BiomeFloatingPlantKind.SacredLotus, 0.6f), (BiomeFloatingPlantKind.WaterLily, 0.2f))
				// Gin-clear mountain water tinted yellow-green by the grove overhead.
				.WaterTint(new BiomeWaterLook(WoodsHex("#7a9a52"), WoodsHex("#1e3a2a"), new Vector3(0.30f, 0.16f, 0.24f), 0.1f, WoodsHex("#8a8a6a"), WoodsHex("#f4f6ee")))
				.Blades("GrassSusuki", t => WoodsBlade(t, "#3e5a2a", "#8a9a5a", 0.3f, heightScale: 1.6f, clump: 0.8f, head: GrassBladeHead.Plume, headSize: 0.14f,
					headShare: 0.35f, heads: new[] { "#d8c8c0" }))
				.Blades("GrassHakone", t => WoodsBlade(t, "#4a6a2a", "#b0c050", 0.35f, width: 0.8f, clump: 0.6f, bend: 0.8f))
				.Blades("GrassRyunohige", t => WoodsBlade(t, "#1e3018", "#2e4a24", 0.2f, density: 1.4f, width: 0.4f, heightScale: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.01f, headShare: 0.1f, heads: new[] { "#2a3a8a" }))
				.Blades("FlowersHiganbana", t => WoodsBlade(t, "#3e5a2a", "#557f30", 0.3f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.06f,
					heads: new[] { "#c8141a", "#e83a2a" }))
				.Blades("FlowersAkiNanakusa", t => WoodsBlade(t, "#4a6a34", "#5a7a3a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.03f,
					heads: new[] { "#e8a0c0", "#e8d040", "#c8a0c8", "#6a5ac8" }))
				.Blades("FlowersShaga", t => WoodsBlade(t, "#3a6a2a", "#4e7e34", 0.3f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.04f,
					heads: new[] { "#c8b8e0", "#e8a030" }))
				.Blades("FlowersKatakuri", t => WoodsBlade(t, "#4a6a3a", "#5a7a44", 0.4f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.03f,
					heads: new[] { "#c86aa8" }))
				.Describe("Running bamboo groves on andesite hills — sugi cedars on the ridges, maples in the ravines, terraced paddies and tea, thatched minka, vermilion shrine gates and tiled temples along mossy stone roads")
				.Names(onsets: new[] { "Ka", "Shi", "Mi", "Yo", "Ha", "Ku", "Sa", "Ni", "Ta", "Fu", "Aki", "Ise" },
					nuclei: new[] { "a", "i", "o", "u", "e", "ai" },
					codas: new[] { "yama", "dera", "saka", "mura", "tani", "zawa", "bayashi" },
					syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Yagura", "Ana", "Iwaya", "Okunoin" },
					dungeonPrefixes: new[] { "The Moonlit", "The Foxfire", "The Mossy", "The Hidden", "The Tengu", "The Bamboo" },
					poiSuffixes: new[] { "Jinja", "Tera", "Toge", "Taki", "Mori", "Chaya" },
					adjectives: new[] { "Moonlit", "Mossy", "Vermilion", "Rustling", "Cedar-dark", "Foxfire", "Misted", "Lantern-lined" },
					poiWords: new[]
					{
						("shrine", new[] { "Jinja", "Torii Tunnel" }),
						("temple", new[] { "Tera", "Pagoda" }),
						("hermitage", new[] { "Yamabushi" }),
						("village", new[] { "Mura", "Minka" }),
						("pass", new[] { "Toge" }),
						("waterfall", new[] { "Taki" }),
						("ancienttree", new[] { "Great Ginkgo", "Sacred Cedar" }),
						("crypt", new[] { "Yagura" }),
						("graveyard", new[] { "Gorinto Yard" }),
						("barrow", new[] { "Kofun" }),
						("waystation", new[] { "Chaya" }),
						("castle", new[] { "Yamajiro" }),
						("monsterden", new[] { "Tengu Roost" }),
						("camp", new[] { "Charcoal Kiln" }),
					},
					about: "Japanese-register voice of the bamboo hills (Ka, Shi, Mi, Ku; -yama, -dera, -saka, -zawa; Jinja, Tera, Toge, Taki, Mori, Yagura)")
				.Sites((POIType.Shrine, 1.8f), (POIType.Temple, 1.6f), (POIType.Hermitage, 1.4f), (POIType.AncientTree, 1.4f), (POIType.Waterfall, 1.4f),
					(POIType.Pass, 1.2f), (POIType.Monastery, 1.2f), (POIType.Village, 1.2f), (POIType.Waystation, 1.2f), (POIType.Spring, 1.2f),
					(POIType.HotSpring, 1f), (POIType.HerbGrove, 1f), (POIType.Bridge, 1f), (POIType.Clearing, 1f), (POIType.FeyRing, 1f),
					(POIType.Graveyard, 1f), (POIType.Statue, 1f), (POIType.Barrow, 0.8f), (POIType.Crypt, 0.8f), (POIType.Keep, 1f), (POIType.Cave, 0.8f),
					(POIType.Grotto, 0.8f), (POIType.Gorge, 0.8f), (POIType.LumberCamp, 0.8f), (POIType.MonsterDen, 0.8f), (POIType.Watermill, 0.6f),
					(POIType.Castle, 0.6f), (POIType.Camp, 0.6f), (POIType.Nest, 0.6f), (POIType.Ruins, 0.6f), (POIType.RitualSite, 0.6f),
					(POIType.CrystalFormation, 0.5f), (POIType.AbandonedFarm, 0.5f), (POIType.BanditCamp, 0.5f), (POIType.Town, 0.5f), (POIType.Lake, 0.4f),
					(POIType.River, 0.8f), (POIType.Mine, 0.4f), (POIType.Quarry, 0.4f), (POIType.Monument, 0.4f), (POIType.TradingPost, 0.4f),
					(POIType.Fortress, 0.3f), (POIType.HuntingLodge, 0.3f), (POIType.City, 0.2f), (POIType.Capital, 0.05f),
					// Kept as they were: the landform kinds at the rule's 1.
					(POIType.Rapids, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f),
					(POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.NaturalArch, 1f), (POIType.SeaCave, 1f), (POIType.DungeonEntrance, 1f),
					(POIType.BossLair, 1f), (POIType.Peak, 1f), (POIType.Overhang, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 1.2f), (POIType.GoblinWarren, 1.2f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.5f),
					(POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.WishingStone, 0.8f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.LostLibrary, 0.5f), (POIType.DruidGrove, 1.4f), (POIType.TrollStones, 1f), (POIType.SpiderThicket, 1.2f),
					(POIType.CharcoalClamps, 1.2f), (POIType.GreenwoodHideout, 1.2f), (POIType.GoblinMarket, 0.8f), (POIType.ElfWaystone, 1f),
					(POIType.WolfDen, 1f), (POIType.MossColossus, 0.8f), (POIType.BeeGarden, 0.5f), (POIType.HangingMonastery, 0.8f),
					(POIType.HagsCauldron, 0.5f), (POIType.CovenCircle, 0.6f), (POIType.NymphPool, 0.8f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 2f)
				.Prefers(POIType.Village, "Stone", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.4f)
				.Prefers(POIType.Town, "Stone", 0.8f)
				.Prefers(POIType.Temple, "Court", 1.6f)
				.Prefers(POIType.Monastery, "Cloister", 1.4f)
				.Prefers(POIType.Shrine, "Shrine", 1.8f)
				.Prefers(POIType.Hermitage, "Cottage", 1.8f)
				.Prefers(POIType.Castle, "Stone", 1.6f)
				.Prefers(POIType.Keep, "Stone", 1.6f)
				.Prefers(POIType.Fortress, "Stone", 1.6f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Graveyard, "Earthen", 1f)
				.Prefers(POIType.Waystation, "Inn", 1.6f)
				.Prefers(POIType.Camp, "Crude", 1.4f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.Keep, "Elven Hall", 1f)
				.Prefers(POIType.Tower, "Elven Tower", 1f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.3f)
				.Prefers(POIType.HangingMonastery, "Cliff Chapel", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Bamboo Forest's signature pieces — minka and gassho houses, the shrine with its torii tunnel, the pagoda temple,
				// the mountain hermitage, azekura stores, yagura tombs, the gorinto graveyard, jizo rows, the stone-lantern avenue, the hill fort,
				// the charcoal kiln, the cutters' yard, bamboo flumes, the tea stall, the kofun with haniwa, the moon-culm, the fox shrine and
				// the tengu cedar; until then the kit's variants below stand in.
				.Favours(BiomeStructureStyle.Timber, 1.8f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Earth, 0.4f)
				.Favours(BiomeStructureStyle.Hide, 0.6f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: ochre clay on bamboo lath under silvered sugi boards, deep miscanthus thatch, hinoki-bark shingle, andesite pad
				// stones; white plaster, vermilion lacquer and grey fired tiles on the temples; split-culm fences, indigo noren, white banners.
				// LATER (NEW-SURFACES U11): Tsuchikabe, Itabari, Kaya, Hiwada, Soseki, Itado, Takegaki, Azekura, Noren, Nobori, Shu, Kawara,
				// Kidan, Tsuijibei and Mushiro as Bamboo Forest's own surfaces; the shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, WoodsHex("#b8a07a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, WoodsHex("#8a7a66"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#8a7a52"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#7a4a32"), pitch: BiomeRoofPitch.Pitch40)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, WoodsHex("#6a6a64"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#b89a72"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, WoodsHex("#ece6d4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Timber, WoodsHex("#b89a72"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, WoodsHex("#ece8de"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, WoodsHex("#c8402a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, WoodsHex("#5a5c60"), pitch: BiomeRoofPitch.Pitch40)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate, WoodsHex("#5a5c60"), pitch: BiomeRoofPitch.Pitch40)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, WoodsHex("#6a6a64"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#c8402a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, WoodsHex("#b8a07a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, WoodsHex("#5a5c60"), pitch: BiomeRoofPitch.Pitch40)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Bamboo, WoodsHex("#c8c08a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Bamboo, WoodsHex("#c8c08a"), pitch: BiomeRoofPitch.Pitch30)
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Bamboo, WoodsHex("#c8c08a"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, WoodsHex("#6a6a64"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, WoodsHex("#8a7a66"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Plaster, WoodsHex("#ece8de"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Bamboo, WoodsHex("#c8c08a"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, WoodsHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, WoodsHex("#22304e"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, WoodsHex("#f0eee6"))
				// Moss on every roof, step and lantern.
				.Weathers(BiomeStructureFinish.Mossy, 0.6f, 0.8f)
				// Paddy rice on earth-bunded terraces, tea on stone-faced terraces, soba and hemp on the flat.
				.Crops(("Detail_CropRice", 0.5f), ("Detail_Bush_Chanoki", 0.2f), ("Detail_CropSoba", 0.2f), ("Detail_CropAsa", 0.1f))
				.CropSurface("Detail_CropRice", BiomeFieldSurface.Paddy)
				.CropRiser("Detail_CropRice", BiomeTerraceRiser.EarthBund)
				.CropSurface("Detail_Bush_Chanoki", BiomeFieldSurface.Terraced)
				.CropRiser("Detail_Bush_Chanoki", BiomeTerraceRiser.Drystone)
				// Split-culm fences along the lanes, clipped tea hedges round the farms, rough-piled walls on the terrace risers.
				.Lines(new Entry.Line("Takegaki"), new Entry.Line("Chanoki"), new Entry.Line("Nozura"))
				// Andesite lanterns in pairs along the approaches, paper lanterns in the villages, iron baskets at the forts, foxfire jars.
				.Lights(POIType.Shrine, BiomeWayLightKind.StoneLantern, 10f)
				.Lights(POIType.Temple, BiomeWayLightKind.StoneLantern, 10f)
				.Lights(POIType.Monastery, BiomeWayLightKind.StoneLantern, 10f)
				.Lights(POIType.Village, BiomeWayLightKind.PaperLantern, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.PaperLantern, 16f)
				.Lights(POIType.Waystation, BiomeWayLightKind.PaperLantern, 12f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.Glowjar, 8f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.HangingLantern)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 12f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				// Wisteria rope over the pines, kudzu smothering the edges.
				.GrowsVines(0.1f, 0.45f, 0.12f, 0.25f, ("Fuji", 0.5f), ("Kuzu", 0.5f))
				.Gathers("Bamboo Culm", "Takenoko Shoot", "Matsutake", "Shiitake", "Tea Leaf", "Kuzu Root", "Zenmai Fiddlehead", "Andesite Block",
					"Masa Clay", "Jadeite Nodule", "Twinquartz", "Hotarubi Cluster")
				.Herbs("Detail_HerbDokudami", "Detail_HerbSenburi", "Detail_HerbGennosho", "Detail_FernZenmai")
				.ErodesAs("Rainforest");
		}
	}
}
#endif
