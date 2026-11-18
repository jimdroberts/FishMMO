#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Jungle</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Jungle()
		{
			// The living lowland rainforest of the Bight of Benin (biome round, approved 2026-10-18, authored 2026-10-26): iroko, obeche
			// and African mahogany on plank buttresses over a dim, dripping floor of bronze leaf litter on red laterite, oil palms in
			// every gap, raffia swamps along the streams, bare grey granite inselbergs above the canopy and ironstone pavements where
			// the forest thins. Its people are yam-farmers, palm-wine tappers, iron-smelters and brass-casters in red mud-walled
			// compounds inside ditch-and-bank earthworks; its fantasy the speaking heads, the leafwardens, glowing iroko spirit-trees
			// and leafglass. Two reaches: SEMI-DECIDUOUS (humidity below 0.6: obeche, more oil-palm bush) and EVERGREEN (0.6 and up:
			// mahogany, denser understorey). The neotropical kit (kapok, bromeliads, monstera) is Jungle Ruins'; strangler figs are
			// Jungle Temple's.
			// LATER (I9): every ground and object texture as Jungle's own variant of its recipe (decision 12); until then the grounds
			// below are Jungle's own recipes and the stones, barks and leaves the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Camwood + Leafward (BiomeTemplate.rareLookA/B).
			return E("Jungle", L(Ground.JungleFloorBronze,
					// The canopy: mixed leathery trees with mahogany and kola, crowns closing.
					Stand(Was(3f, 5f), 30f, W(0.9f, 1f), "ForestCanopy", "AfricanMahogany", "Kola"),
					// The emergents: iroko always, obeche in the semi-deciduous reach, mahogany in the evergreen.
					Trees(0.006f, 40f, 30f, "Iroko"),
					Band(Trees(0.006f, 40f, 30f, "Obeche"), -1f, 1f, -1f, 0.6f),
					Band(Trees(0.006f, 40f, 30f, "AfricanMahogany"), -1f, 1f, 0.6f, 1f),
					Trees(0.002f, 25f, 30f, "OilPalm"),
					Grass("GrassForestBroad", 6f), Plants("FernRainforest", 8f), Plants("Arrowroot", 6f), Plants("Palisota", 1.5f),
					Plants("FlowersAlligatorPepper", 2f), Plants("FlowersCostus", 1.5f), Plants("FlowersBloodLily", 0.3f), Plants("Selaginella", 3f, 128),
					Plants("TermiteCaps", 1.5f), Plants("MushroomTermite", 0.3f), Plants("DebrisRainforest", 8f, 128), Plants("DebrisPods", 0.5f, 128),
					Bush("Dracaena", 0.75f), Band(Bush("WildCoffee", 0.85f), -1f, 1f, 0.6f, 1f), Band(Bush("WildCoffee", 0.78f), -1f, 1f, -1f, 0.6f),
					Bush("Ixora", 0.78f), Bush("Rattan", 0.55f),
					// LATER (I9b): the buttressed root end, Lampcap clusters on the log; the lightning snag's hornbill holes.
					Logs("Fallen logs", 0.004f, 30f, Wood("FallenLog_Buttressed", "Stump_Bracket")),
					Logs("Iroko snags", 0.001f, 80f, Wood("Snag_Iroko")),
					Stones(SmallRocks("Laterite"), 1f),
					Blocks("Laterite blocks", 0.04f, 10f, F(RegionalRocks.Laterite, "Block"))),
				Ds(
					// d0: red farm-bush soil of the clearings and fallows: spear grass, oil-palm bush, umbrella trees, rattan, termite mounds.
					D(Ground.SoilRedForest, 120f, 2f, 0f, 18f, Grass("GrassSpear", 10f), Plants("FlowersAlligatorPepper", 1.5f),
						Stand(Was(0.5f, 8f), 25f, W(0.4f, 0.3f, open: 0.2f), "OilPalm"),
						Stand(Was(0.4f, 6f), 20f, W(0.3f, 0.3f, open: 0.3f), "UmbrellaTree"),
						Bush("Rattan", 0.71f), Bush("Bitterleaf", 1f), Bush("Dracaena", 1f), Plants("TermiteCaps", 3f), Plants("MushroomTermite", 0.3f),
						Plants("DebrisPods", 0.3f, 128), Stones(Pebbles("Laterite"), 2f)),
					// d1: ravine moss and liverwort on the wet slopes: spikemoss, rainforest ferns, balsam and costus, Lampcaps, granite boulders.
					D(Ground.MossRavine, 48f, 2f, 15f, 38f, Plants("Selaginella", 10f, 128), Plants("FernRainforest", 12f), Plants("FlowersBalsam", 4f),
						Plants("FlowersCostus", 2f), Grass("GrassForestBroad", 4f), Plants("Palisota", 1f), Plants("MushroomLampcap", 0.5f),
						Boulders(0.1f, 10f, Boulder("Grey", "Round"), Boulder("Grey", "Slab")), Stones(SmallRocks("Grey"), 2f)),
					// d2: raffia-swamp mud of the stream flats: raffia palms, arrowroot, balsam at the margins.
					D(Ground.MudRaffiaSwamp, 64f, 2f, 0f, 6f, Stand(Was(0.6f, 6f), 15f, W(0.6f, 0.6f, open: 0.1f), "RaffiaPalm"), Plants("Arrowroot", 12f),
						Plants("FlowersBalsam", 2f), Plants("Selaginella", 3f, 128), Plants("DebrisRainforest", 4f, 128), Plants("DebrisPods", 0.5f, 128))
						.Ways(path: Ground.PathFrondMat),
					// d3: the ironstone pavement (bowal) where the forest thins, on the top of the height band: spear grass, pisolite gravel,
					// breakaways and blocks of laterite.
					With(D(Ground.LateriteIronstone, 200f, 3f, 0f, 12f, Grass("GrassSpear", 8f), Stones(Pebbles("Laterite"), 6f), Stones(SmallRocks("Laterite"), 3f),
						Outcrops("Laterite breakaways", 0.004f, 60f, F(RegionalRocks.Laterite, "Breakaway")),
						Outcrops("Laterite pavement", 0.006f, 40f, F(RegionalRocks.Laterite, "Pavement")),
						Blocks("Laterite rubble", 0.04f, 10f, F(RegionalRocks.Laterite, "Block"))), l => l.Height = new Vector2(0.54f, 0.6f)),
					// d4: the inselberg whaleback: bare granite, rain-streaked, corestones at its feet and the odd dome.
					D(Ground.GraniteWhaleback, 260f, 4f, 18f, 45f, Outcrops("Bornhardt domes", 0.002f, 120f, F(RockTypes.Granite, "Bornhardt"), 45f),
						Blocks("Granite corestones", 0.03f, 14f, F(RockTypes.Granite, "Corestone")), Boulders(0.2f, 10f, Boulder("Grey", "Round"), Boulder("Grey", "Slab")),
						Stones(SmallRocks("Grey"), 2f))),
				C(Ground.CliffGneissGorge, 42f), Ground.MudLeafSilt, Ground.SandQuartzStream)
				// The rules the round retired (the neotropical kit, the generic shrub and tall grass, hibiscus, ivy, bamboo and tree fern).
				.Moved("main", "Trees: Jungle", "Trees: Palm", "Trees: Kapok", "Fern", "ShrubSmall", "GrassTall", "Bush: Hibiscus", "BroadHerb",
					"RosetteBromeliad", "Ivy")
				.Moved("detail/0", "Trees: Bamboo", "Trees: TreeFern", "Trees: Banana")
				.Moved("detail/1", "BroadHerb")
				.Ways(path: Ground.PathRootTrail, road: Ground.LateriteAvenue, cobbles: Ground.PotsherdPavement)
				.Bars(sand: Ground.SandMicaGold, gravel: Ground.GravelPisolite)
				// Own rock laterite (breakaway, pavement, block); approved: accepts granite (the inselbergs) and gneiss (the gorge walls);
				// schist, limestone and basalt dropped.
				.Accepts(RegionalRocks.Laterite, RockTypes.Granite, RockTypes.Gneiss)
				.CaveShell(CaveForm.Cave, "Granite").CaveShell(CaveForm.Grotto, "Laterite").CaveShell(CaveForm.Mine, "Laterite")
				.Crystals((BiomeCaveCrystalKind.WatermelonTourmaline, 0.6f), (BiomeCaveCrystalKind.Leafglass, 0.4f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				// The inselbergs, the laterite breakaways and the iya ditch-and-bank round a town (LATER (I7): the iya sited on its town).
				.Landforms("Inselberg", "LateriteBreakaway", "Iya")
				// The woven liana bridge for the spans a river leaves (ends in the kit's bridge outside its spans).
				.Crosses(BiomeCrossingStyle.Liana)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Jungle)
				// LATER (AMBIENT-LIFE): the fruit-bat dusk exodus, grey parrots roosting, hornbills, colobus and mona troops, forest elephant
				// and bush-cow at the streams, driver-ant columns (client decal), Leafwarden flickers in the sacred groves, Iroko nights.
				// LATER (RARE-LOOKS): Camwood and Leafward.
				.Moat(BiomeMoatWater.Dry)
				.Floats((BiomeFloatingPlantKind.WaterLily, 0.6f), (BiomeFloatingPlantKind.WaterLettuce, 0.4f))
				// Gold-ochre forest river, green-shaded under the canopy.
				.WaterTint(new BiomeWaterLook(WoodsHex("#8a6a30"), WoodsHex("#24180a"), new Vector3(0.25f, 0.38f, 0.55f), 0.6f, WoodsHex("#9a6a2e"), WoodsHex("#e8dcc0")))
				.Blades("GrassForestBroad", t => WoodsBlade(t, "#2a4a1e", "#4a7a2e", 0.3f, density: 0.5f, width: 0.6f, clump: 0.4f))
				.Blades("GrassSpear", t => WoodsBlade(t, "#4a5a2a", "#8a9a4a", 0.4f, stiffness: 1.4f, width: 0.5f, heightScale: 1.2f,
					head: GrassBladeHead.Plume, headSize: 0.12f, headShare: 0.3f, heads: new[] { "#f2f0ea" }))
				.Blades("FlowersAlligatorPepper", t => WoodsBlade(t, "#3a6a2a", "#4a7a34", 0.25f, heightScale: 1.5f, head: GrassBladeHead.Flower, sprinkle: 4f,
					headSize: 0.05f, heads: new[] { "#f0c8d0", "#c8301e" }))
				.Blades("FlowersCostus", t => WoodsBlade(t, "#3a6a2a", "#4a7a34", 0.2f, heightScale: 1.6f, head: GrassBladeHead.Flower, sprinkle: 3f,
					headSize: 0.08f, heads: new[] { "#b8201e", "#f4ecc8" }))
				.Blades("FlowersBalsam", t => WoodsBlade(t, "#3e6a2a", "#557f30", 0.35f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.04f,
					heads: new[] { "#e0507a", "#f08aa8" }))
				.Blades("FlowersBloodLily", t => WoodsBlade(t, "#3a6a2a", "#4a7a34", 0.3f, head: GrassBladeHead.Flower, sprinkle: 2f, headSize: 0.1f,
					heads: new[] { "#e02a1a" }))
				.Describe("Living lowland rainforest — buttressed giants over a dim, dripping floor, oil palms in every gap, grey domes of bare granite above the canopy, and the red-walled, ditch-ringed towns of the brass-casters")
				.Names(onsets: new[] { "O", "I", "E", "Ad", "Ak", "Ob", "Og", "Ol", "Ew", "Uz", "Ig", "Ek", "Il", "Ay", "Ib" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ai", "oa" },
					codas: new[] { "n", "ba", "de", "ro", "mi", "le", "ko", "ye", "wo" },
					syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Hollow", "Root-cave", "Shelter", "Furnace-pit", "Grove-heart", "Iya" },
					dungeonPrefixes: new[] { "The Dripping", "The Red-walled", "The Speaking", "The Leaf-warded", "The Iroko", "The Brass" },
					poiSuffixes: new[] { "Compound", "Quarter", "Grove", "Gate", "Market", "Rock" },
					adjectives: new[] { "Buttressed", "Red-walled", "Brass-cast", "Dripping", "Iroko-shaded", "Leaf-warded", "Palm-wine", "Granite-domed" },
					poiWords: new[]
					{
						("village", new[] { "Compound", "Quarter" }),
						("town", new[] { "Ditch Town", "Compound Quarter" }),
						("city", new[] { "Brass Quarter", "Palace City" }),
						("tradingpost", new[] { "Market", "Tree Market" }),
						("shrine", new[] { "Grove", "Potsherd Shrine" }),
						("herbgrove", new[] { "Sacred Grove", "Leaf Grove" }),
						("ancienttree", new[] { "Iroko", "Spirit Tree" }),
						("mine", new[] { "Furnace", "Iron Pits" }),
						("peak", new[] { "Rock", "Inselberg" }),
						("ritualsite", new[] { "Speaking Heads", "Grove Heart" }),
						("camp", new[] { "Palm-wine Camp", "Hunters' Shelter" }),
						("tower", new[] { "Canopy Lookout" }),
						("bridge", new[] { "Liana Bridge" }),
					},
					about: "Edo-Yoruba-flavoured invented voice of the forest kingdoms (O, Ad, Ak, Ob, Og; -n, -ba, -de, -ro; Compound, Quarter, Grove, Iya)")
				// The forest kingdoms: villages, groves and spirit-trees first, towns and the odd palace city.
				.Sites((POIType.Village, 1.6f), (POIType.AncientTree, 1.6f), (POIType.HerbGrove, 1.6f), (POIType.Town, 1.2f), (POIType.Clearing, 1.2f),
					(POIType.Waterfall, 1.2f), (POIType.Shrine, 1.4f), (POIType.TradingPost, 1.2f), (POIType.Bridge, 1f), (POIType.HuntingLodge, 1f),
					(POIType.Spring, 1f), (POIType.RitualSite, 1f), (POIType.MonsterDen, 1f), (POIType.Peak, 0.8f), (POIType.Overhang, 0.8f),
					(POIType.Gorge, 0.8f), (POIType.Camp, 0.8f), (POIType.Nest, 0.8f), (POIType.DungeonEntrance, 0.8f), (POIType.City, 0.6f),
					(POIType.Mine, 0.6f), (POIType.Cave, 0.6f), (POIType.LumberCamp, 0.6f), (POIType.Waystation, 0.6f), (POIType.FeyRing, 0.6f),
					(POIType.AbandonedFarm, 0.6f), (POIType.OreVein, 0.5f), (POIType.Keep, 1f), (POIType.Ruins, 0.4f), (POIType.Statue, 0.4f),
					(POIType.FishingCamp, 0.4f), (POIType.BanditCamp, 0.4f), (POIType.Fortress, 0.3f), (POIType.CrystalFormation, 0.3f),
					(POIType.Quarry, 0.3f), (POIType.Hermitage, 0.3f), (POIType.Temple, 0.3f), (POIType.CorruptedGrove, 0.3f), (POIType.BossLair, 0.3f),
					(POIType.Monument, 0.3f), (POIType.Capital, 0.2f), (POIType.Graveyard, 0.2f), (POIType.Battlefield, 0.2f), (POIType.Crypt, 0.2f),
					(POIType.LeyNexus, 0.2f), (POIType.FallenStar, 0.2f), (POIType.Portal, 0.1f),
					// Kept as they were: the landform kinds at the rule's 1.
					(POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Mesa, 1f),
					(POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.NaturalArch, 1f), (POIType.SeaCave, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f), (POIType.Hamlet, 0.3f),
					(POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.TreeVillage, 0.8f),
					(POIType.SpiderThicket, 1f), (POIType.DryadOak, 0.4f), (POIType.MossColossus, 1f), (POIType.StepPyramid, 1.4f), (POIType.Cenote, 1f),
					(POIType.JaguarTemple, 1.2f), (POIType.SerpentIdol, 1f), (POIType.LostCity, 1.2f), (POIType.LizardfolkVillage, 0.8f),
					(POIType.IdolVault, 1f), (POIType.LizardfolkHatchery, 0.6f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 1.8f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Stone", 0.2f)
				.Prefers(POIType.Town, "Timber", 1.6f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.Town, "Stone", 0.2f)
				.Prefers(POIType.City, "Timber", 1.4f)
				.Prefers(POIType.City, "Stone", 0.3f)
				.Prefers(POIType.Capital, "Timber", 1.4f)
				.Prefers(POIType.Capital, "Stone", 0.3f)
				.Prefers(POIType.Keep, "Timber", 1.4f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Camp, "Lean-tos", 0.3f)
				.Prefers(POIType.Camp, "Crude", 1f)
				.Prefers(POIType.Ruins, "Burned", 0.4f)
				.Prefers(POIType.Ruins, "Temple", 0f)
				.Prefers(POIType.HuntingLodge, "Lodge", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.3f)
				.Prefers(POIType.TreeVillage, "Jungle Boughs", 1.5f)
				.Prefers(POIType.StepPyramid, "Sun Steps", 1.5f)
				.Prefers(POIType.LostCity, "Fallen Seat", 1.5f)
				.Prefers(POIType.LizardfolkVillage, "Mudhall Wallow", 1.4f)
				// (the fantasy wave's variants end here)
				// LATER (I7 I28): Jungle's signature pieces — the impluvium compound (Village 1.6), the yam-barn hamlet, the walled ditch town,
				// the palace city with brass-bird turrets, the potsherd shrine, the brass-casters' street, the bloomery furnace site, the
				// sacred grove, the tree market, the palm-wine camp, the hunters' shelter, the canopy lookout, the liana bridge, the inselberg
				// refuge, the fallen compound, the speaking heads, the leafward ring and the iroko spirit-tree; until then the kit's variants.
				.Favours(BiomeStructureStyle.Earth, 2f)
				.Favours(BiomeStructureStyle.Timber, 0.8f)
				.Favours(BiomeStructureStyle.Stone, 0.1f)
				.Favours(BiomeStructureStyle.Hide, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: burnished red laterite swish walls with a white kaolin band, stitched raffia-leaf thatch at 50 degrees, dark
				// iroko shingle turrets at 60, carved iroko posts, raffia-rib screens, cast brass and terracotta. LATER (NEW-SURFACES U6/U8):
				// LateriteSwish, KaolinBand, RaffiaLeafThatch, IrokoShingle, CarvedIroko, RaffiaRib, CastBrass, Terracotta, AsoStrip and
				// RedApplique as Jungle's own surfaces; the shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, WoodsHex("#a8502a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, WoodsHex("#ece6da"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#8a7a4a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#3e2a1a"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, WoodsHex("#8a3a1e"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#5a3a22"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, WoodsHex("#5a3a22"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, WoodsHex("#8a3a1e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, WoodsHex("#a8502a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Bamboo, WoodsHex("#b8a070"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#8a7a4a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Thatch, WoodsHex("#6a5a38"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, WoodsHex("#8a3a1e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#5a3a22"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Bamboo, WoodsHex("#b8a070"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, WoodsHex("#9a5a36"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, WoodsHex("#a8502a"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Monolith, WoodsHex("#6a8a5a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Earth, WoodsHex("#a8603a"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, WoodsHex("#5a3a22"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, WoodsHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, WoodsHex("#3e4a7a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, WoodsHex("#b8201e"))
				// The rain-washed walls: algae streaks, fluted and reddened at the foot (LATER (NEW-COATS): Rainwashed; Algae + Streaked stand in).
				.Weathers(BiomeStructureFinish.Algae, 0.6f, 0.7f)
				.Coated(BiomeStructureCoat.Streaked, 0.5f, 0.8f)
				// Yams on mounds, egusi melons under the stumps, African rice on the bottomland; oil-palm, kola and plantain orchards.
				.Crops(("Detail_CropYam", 0.6f), ("Detail_CropEgusi", 0.25f), ("Detail_CropRiceAfrican", 0.15f))
				.CropSurface("Detail_CropYam", BiomeFieldSurface.CornHills)
				.FieldsNearRivers(150f)
				.Orchards(new Entry.Orchard("OilPalm", 0.4f, 9f, "Detail_GrassSpear"), new Entry.Orchard("Kola", 0.3f, 8f, "Detail_FernRainforest"),
					new Entry.Orchard("Plantain", 0.3f, 4f, "Detail_CropEgusi"))
				// The peace-plant line round a compound, the iya's earth bank round a town, raffia-rib goat pens.
				.Lines(new Entry.Line(BiomeLinearForm.Wild, BiomeLinearPlace.SiteBounds, "Dracaena"),
					new Entry.Line(BiomeLinearForm.EarthBank, BiomeLinearPlace.SiteBounds),
					new Entry.Line(BiomeLinearForm.Wattle, BiomeLinearPlace.Folds))
				// Clay palm-oil lamps in the compound niches (LATER (NEW-LIGHTS): ClayLamp; the niche lamp stands in), braziers at the palaces,
				// leafglass posts at the groves.
				.Lights(POIType.Village, BiomeWayLightKind.NicheLamp, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.NicheLamp, 16f)
				.Lights(POIType.City, BiomeWayLightKind.NicheLamp, 16f)
				.Lights(POIType.Capital, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.NicheLamp, 14f)
				.Lights(POIType.HerbGrove, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.CrystalPost, 10f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 12f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				// Lianas curtaining the cliffs and cave mouths, rattan whips and epiphytes in the crowns (LATER (I9b): the span vine).
				.GrowsVines(0.9f, 0.9f, 0.6f, 0.5f, ("Liana", 0.6f), ("RattanWhip", 0.25f), ("Epiphytes", 0.15f))
				.Gathers("Kola Nuts", "Palm Fruit", "Palm Wine", "Alligator Pepper", "Bitter Leaf", "Scent Leaf", "Costus Stem", "Calabar Bean",
					"Camwood Heartwood", "Iroko Timber", "Raffia Fibre", "Laterite Iron Ore", "Watermelon Tourmaline", "Leafglass", "Termite Mushrooms")
				.Herbs("Detail_FlowersAlligatorPepper", "Detail_HerbScentLeaf", "Detail_FlowersCostus", "Detail_FlowersBalsam")
				.ErodesAs("Rainforest");
		}
	}
}
#endif
