#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>How far out of a settlement Woodland's lanes are hedged, metres: the village's own lanes, not every way through the wood.</summary>
		public const float LaneHedgeReach = 300f;

		/// <summary>The <c>Woodland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Woodland()
		{
			// The open, managed, lived-in lowland wood (biome round, approved 2026-10-16, authored 2026-10-26): coppice-with-standards
			// of hazel and sweet chestnut under spaced oak standards, sunny rides and glades of foxglove and bracken, a blue haze of
			// bluebells under the hazel, grazed wood-pasture of squat oak pollards and crab apples on short turf, all on white chalk
			// and black flint, edged by laid hedges round the assarts. Red peg-tile roofs, white daub, flint walls. Its fantasy is
			// romance, not the fey: the white hart, knights' pavilions, an enchanted fountain, a blasted oak, golden mistletoe.
			// Forest keeps the closed beech, fir, yew, holly, ivy and fey; this wood keeps hazel, bramble, hawthorn and the open oak.
			// LATER (I9): every ground and object texture as Woodland's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Gilded + Copperleaf (BiomeTemplate.rareLookA/B).
			// LATER (I9b P27): pollards (a swollen bolling of straight poles) and coppice stools on the scatter's trees; the oaks
			// below stand whole until the generator grows them.
			return E("Woodland", L(Ground.GrassWoodPasture,
					// Wood-pasture: wide-spaced veteran oaks and crab apples on the turf.
					Stand(Was(0.6f, 12f), 30f, W(0.35f, 0.5f, open: 0.35f), "OakWoodland", "CrabApple"),
					Trees(0.03f, 30f, 30f, "WildCherry"), Trees(0.01f, 60f, 30f, "SweetChestnut"),
					Grass("GrassPasture", 22f), Grass("GrassYorkshireFog", 3f),
					Plants("FlowersGlade", 3f, 180), Plants("FlowersHedgerow", 2f, 180), Plants("FernBrackenWoodland", 2f), Plants("HerbNettle", 1f),
					Plants("DebrisOakLeaf", 1f, 128), Plants("MushroomParasol", 0.4f),
					Bush("HawthornWoodland", 0.22f), Bush("BrambleWoodland", 0.43f), Bush("HazelCoppice", 0.28f), Bush("Blackthorn", 0.28f), Bush("DogRose", 0.28f),
					// LATER (I9b): the shed pollard limb's crooked lie; a light fallen limb stands in.
					Logs("Shed limbs", 0.04f, 14f, Wood("FallenLimb_Oak", "Stump_Beefsteak")),
					// LATER (I7/I9b): warren spoil fans (WarrenSpoil, a flat chalk-and-burrow mesh); none until built.
					Stones(SmallRocks("Flint"), 0.8f),
					// The grey wethers: sarsens lying in the turf like a flock, in groups of four to twelve.
					With(Blocks("Grey wethers", 0.006f, 30f, F(RegionalRocks.Sarsen, "Wether")), s => { s.ClusterMetres = 30f; s.ClusterSize = 8f; }),
					Blocks("Puddingstone", 0.004f, 40f, F(RockTypes.Conglomerate, "Boulder"))),
				Ds(
					// d0: the coppice floor — standards over the cut hazel and chestnut stools, the cut coupes' brash.
					D(Ground.ForestFloorCopse, 48f, 2f, 0f, 28f, Stand(Was(1.4f, 6f), 30f, W(0.6f, 0.7f), "OakWoodland", "SweetChestnut", "FieldMaple", "BirchWoodland"),
						Bush("HazelCoppice", 0.4f), Bush("ChestnutStool", 0.4f), Bush("HazelStoolCut", 0.8f),
						Logs("Coppice stools", 0.3f, 6f, Wood("Stump_Coppice")),
						Plants("DebrisOakLeaf", 8f, 128), Plants("DebrisCoppice", 1.5f, 128), Grass("GrassFalseBrome", 8f),
						Plants("FlowersPrimrose", 5f, 180), Plants("FlowersFoxglove", 3f, 180), Plants("FlowersBluebell", 3f, 180),
						Plants("WoodSpurge", 4f), Plants("LordsAndLadies", 2f), Plants("MushroomChanterelle", 1f), Plants("FernBrackenWoodland", 1f)),
					// d1: the bluebell haze under the hazel.
					D(Ground.ForestFloorBluebell, 96f, 3f, 0f, 22f, Plants("FlowersBluebell", 24f, 220), Bush("HazelCoppice", 0.4f), Plants("DebrisOakLeaf", 3f, 128)),
					// d2: bracken glades under birch.
					D(Ground.GrassDryBracken, 80f, 2.5f, 4f, 30f, Plants("FernBrackenWoodland", 14f), Plants("FlowersFoxglove", 6f, 180), Grass("GrassFalseBrome", 4f),
						Stand(Was(0.5f, 6f), 30f, W(0.5f, 0.4f, open: 0.3f), "BirchWoodland"), Bush("BrambleWoodland", 0.62f),
						Plants("MushroomChanterelle", 0.6f), Plants("DebrisOakLeaf", 3f, 128)),
					// d3: flowery rides and glade swards. LATER (WATER): the tufted hair-grass held within 20 m of water.
					D(Ground.GrassMeadowGlade, 64f, 2.5f, 0f, 14f, Grass("GrassYorkshireFog", 14f), Plants("FlowersGlade", 10f, 180), Grass("GrassHairgrass", 4f),
						Plants("FlowersHedgerow", 2f, 180), Plants("MushroomParasol", 0.6f),
						With(Blocks("Glade wethers", 0.004f, 30f, F(RegionalRocks.Sarsen, "Wether")), s => { s.ClusterMetres = 30f; s.ClusterSize = 6f; })),
					// d4: chalk rendzina on the hanger slopes.
					D(Ground.SoilRendzina, 40f, 2f, 20f, 38f, Grass("GrassPasture", 6f), Plants("FlowersPrimrose", 3f, 180), Plants("WoodSpurge", 2f),
						Bush("Dogwood", 1f), Bush("WayfaringTree", 0.6f), Stones(SmallRocks("Flint"), 4f),
						Blocks("Chalk lumps", 0.01f, 20f, F(RockTypes.Chalk, "Lump"))),
					// d5: grassy chalk rock and cuttings.
					D(Ground.ChalkWoodland, 64f, 4f, 34f, 90f, Stones(SmallRocks("Flint"), 3f), Blocks("Chalk blocks", 0.008f, 24f, F(RockTypes.Chalk, "Block")))),
				C(Ground.ChalkFace, 40f), Ground.MudMarl, Ground.GravelChalkStream)
				// The rules the round retired (Forest's holly, fern, ivy, fly agaric and logs; the generic lawn, meadow flowers and moss).
				.Moved("main", "Trees: Oak, Birch", "GrassLush", "FlowersMeadow", "Bush: Hazel", "Bush: Bramble", "Bush: Hawthorn", "Bush: Holly", "FernBracken",
					"FlowersTall", "Deadwood: Fallen logs")
				.Moved("detail/0", "Fern", "DebrisForest", "FlowersWoodland", "MushroomForest")
				.Ways(path: Ground.PathGreenLane, road: Ground.PathChalkTrack, cobbles: Ground.FlagstoneFlint)
				.Bars(sand: Ground.SandChalky, gravel: Ground.GravelFlint)
				// Own rock chalk with flint (its formations); approved: accepts Limestone, Shale, Conglomerate (puddingstone).
				.Accepts(RockTypes.Chalk, RockTypes.Limestone, RockTypes.Shale, RockTypes.Conglomerate)
				.Crystals((BiomeCaveCrystalKind.FlintGeode, 0.7f), (BiomeCaveCrystalKind.Sunstone, 0.3f))
				// LATER (I7): the denehole mine form (narrow shaft, bell chambers in the chalk).
				.Look(BiomeLookClass.Temperate)
				// Approved: wood-pasture and glades add the grassland's hares, skylarks and voles; chipmunks, hummingbirds and parrots
				// dropped creature-side. LATER (AMBIENT-LIFE): fallow deer, badger, fox, green woodpecker, nightingale, dormouse, the
				// rare White Hart at dawn and dusk.
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Grassland)
				.Moat(BiomeMoatWater.Wet)
				.Blades("GrassPasture", t => WoodsBlade(t, "#3e5a24", "#7a9a44", 0.45f, density: 1f, stiffness: 0.7f, width: 0.5f, clump: 0.15f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.15f, heads: new[] { "#7a5a5a" }))
				.Blades("GrassYorkshireFog", t => WoodsBlade(t, "#6a7a5a", "#9aa880", 0.4f, width: 0.9f, clump: 0.4f,
					head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.4f, heads: new[] { "#c8a0a8" }))
				.Blades("GrassFalseBrome", t => WoodsBlade(t, "#5a7a2a", "#a0b044", 0.35f, width: 1.1f, clump: 0.5f, bend: 0.6f,
					head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.25f, heads: new[] { "#8a9a4a" }))
				.Blades("GrassHairgrass", t => WoodsBlade(t, "#3e5a24", "#7a8a4a", 0.3f, width: 0.6f, clump: 0.85f,
					head: GrassBladeHead.Plume, headSize: 0.14f, headShare: 0.45f, heads: new[] { "#a89aa8" }))
				.Blades("FlowersBluebell", t => WoodsBlade(t, "#2e5a22", "#4a7a2c", 0.3f, head: GrassBladeHead.Flower, sprinkle: 26f, headSize: 0.03f,
					heads: new[] { "#4a4ac0", "#5a5ad0", "#6a5ac8", "#f0f0f4" }))
				.Blades("FlowersPrimrose", t => WoodsBlade(t, "#3e6a28", "#557f30", 0.45f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.025f,
					heads: new[] { "#f2e890", "#8a70c8", "#a04a9a", "#f6f6f2" }))
				.Blades("FlowersFoxglove", t => WoodsBlade(t, "#3e6a2a", "#5a8034", 0.3f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.04f,
					heads: new[] { "#c0508a", "#d070a0", "#e8c0d8", "#f4f0ee" }))
				.Blades("FlowersGlade", t => WoodsBlade(t, "#43622a", "#6a8a3a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.035f,
					heads: new[] { "#d04a7a", "#4a5ac0", "#f0c020", "#f6f4ee" }))
				.Blades("FlowersHedgerow", t => WoodsBlade(t, "#4a6e2c", "#64843a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.06f,
					heads: new[] { "#f4f2e8", "#f0ece0", "#f2f2ec", "#c080a8" }))
				.Describe("Open lowland woodland — coppice and wood-pasture, sunlit glades and bluebells, hedged assarts on chalk and flint")
				.Sites((POIType.Camp, 0.9f), (POIType.Shrine, 1.2f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1.2f), (POIType.HolyWell, 0.8f),
					(POIType.Clearing, 1.2f), (POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.WitchHut, 0.4f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 0.5f), (POIType.BanditCamp, 0.6f),
					(POIType.HuntingLodge, 1.2f), (POIType.LumberCamp, 1.2f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 0.5f),
					(POIType.Nest, 0.2f), (POIType.RitualSite, 0.3f), (POIType.StoneCircle, 0.7f), (POIType.Temple, 1f), (POIType.Monastery, 1.3f),
					(POIType.FeyRing, 0.2f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 0.2f), (POIType.Portal, 1f),
					(POIType.Graveyard, 1f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Battlefield, 0.4f), (POIType.Ossuary, 1f),
					(POIType.Ruins, 0.7f), (POIType.RuinedTower, 0.4f), (POIType.Statue, 0.3f), (POIType.Obelisk, 0.1f), (POIType.AncientRoad, 0.6f),
					(POIType.AbandonedFarm, 0.9f), (POIType.Hermitage, 0.8f), (POIType.Village, 1.3f), (POIType.Town, 0.9f), (POIType.City, 0.5f),
					(POIType.Capital, 0.3f), (POIType.Port, 0.9f), (POIType.Keep, 1f), (POIType.Castle, 0.7f), (POIType.Fortress, 0.3f),
					(POIType.TradingPost, 0.5f), (POIType.Waystation, 1f), (POIType.Mine, 0.5f), (POIType.Quarry, 0.7f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.AncientTree, 1f), (POIType.HerbGrove, 1.2f), (POIType.Dockyard, 0.9f), (POIType.Windmill, 0.3f),
					(POIType.Watermill, 1.2f), (POIType.LumberMill, 0.5f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 1.2f), (POIType.GoblinWarren, 1.2f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.5f),
					(POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.WishingStone, 0.8f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.LostLibrary, 0.5f), (POIType.DruidGrove, 1.4f), (POIType.TreeVillage, 1f), (POIType.TrollStones, 1f),
					(POIType.SpiderThicket, 1.2f), (POIType.CharcoalClamps, 1.2f), (POIType.GreenwoodHideout, 1.2f), (POIType.DryadOak, 1.2f),
					(POIType.GoblinMarket, 0.8f), (POIType.ElfWaystone, 1f), (POIType.WolfDen, 1f), (POIType.MossColossus, 0.8f),
					(POIType.BeeGarden, 0.5f), (POIType.HagsCauldron, 0.5f), (POIType.CovenCircle, 0.6f), (POIType.NymphPool, 0.8f))
				.Prefers(POIType.Village, "Timber", 1.6f)
				.Prefers(POIType.Village, "Stone", 0.9f)
				.Prefers(POIType.Village, "Longhouse", 0.1f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.3f)
				.Prefers(POIType.Town, "Stone", 1.1f)
				.Prefers(POIType.Town, "Palisaded", 0.3f)
				.Prefers(POIType.City, "Stone", 1.2f)
				.Prefers(POIType.City, "Timber", 1f)
				.Prefers(POIType.Capital, "Stone", 1.3f)
				.Prefers(POIType.Capital, "Timber", 0.8f)
				.Prefers(POIType.Keep, "Stone", 1f)
				.Prefers(POIType.Keep, "Palisade", 0.8f)
				.Prefers(POIType.Castle, "Stone", 1.5f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.Monastery, "Cloister", 1.6f)
				.Prefers(POIType.Tower, "Wizard", 1.4f)
				.Prefers(POIType.StoneCircle, "Menhirs", 1.4f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0.2f)
				.Prefers(POIType.Barrow, "Ringed", 1f)
				.Prefers(POIType.Camp, "Lean-tos", 1f)
				.Prefers(POIType.Camp, "Crude", 0.3f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.BanditCamp, "Palisade", 1.5f)
				.Prefers(POIType.Hermitage, "Cottage", 1f)
				.Prefers(POIType.Hermitage, "Stone", 1f)
				.Prefers(POIType.Graveyard, "Railed", 0.6f)
				.Prefers(POIType.Graveyard, "Earthen", 1f)
				.Prefers(POIType.Ruins, "Stone", 1f)
				.Prefers(POIType.Monastery, "Timber", 0.4f)
				.Prefers(POIType.HuntingLodge, "Lodge", 1.8f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 0.1f)
				.Prefers(POIType.Camp, "Hide Tents", 1.4f)
				.Prefers(POIType.Ruins, "Burned", 0.6f)
				.Prefers(POIType.AbandonedFarm, "Farmstead", 1.2f)
				.Prefers(POIType.AbandonedFarm, "Burned", 0.6f)
				.Prefers(POIType.Waystation, "Inn", 1.6f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.6f)
				.Prefers(POIType.Tower, "Stone", 1f)
				.Prefers(POIType.Tower, "Timber", 0.6f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Elven Hall", 1.2f)
				.Prefers(POIType.Tower, "Elven Tower", 1f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.2f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.3f)
				.Prefers(POIType.TreeVillage, "Talan Town", 1.4f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Woodland's signature pieces — Wealden hall house, round colombier, moated manor, flint round-tower church
				// and lychgate, coppice yard, chalk pit, deer-park pale and leap, striped pavilion camp and quintain, lavoir, wayside
				// calvary, bee garden, enchanted fountain, Herne's oak, sarsen avenue; the kit's variants below stand in.
				.Favours(BiomeStructureStyle.Timber, 1.5f)
				.Favours(BiomeStructureStyle.Stone, 1.1f)
				.Favours(BiomeStructureStyle.Hide, 0.5f)
				.Favours(BiomeStructureStyle.Earth, 0.4f)
				.Favours(BiomeStructureStyle.Iron, 0.2f)
				// The palette: white limewash in large square panels, silvered oak, long-straw thatch and red plain tiles, flint plinths
				// and pitching, clunch dressings, heraldic banners. LATER (NEW-SURFACES U11): PegTile (clay plain tile, roof and
				// tile-hanging, 50°) and Flint (knapped flushwork, pitching) surfaces, weatherboard and chalk cob; red-tinted shingle and
				// dark fieldstone stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, WoodsHex("#eeeae0"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Shingle, WoodsHex("#9a4a2e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#6a5e50"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#b8984e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#9a4a2a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, WoodsHex("#4a4a4c"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, WoodsHex("#5a4a38"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, WoodsHex("#8a8274"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, WoodsHex("#5a5a5c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, WoodsHex("#3e3e40"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, WoodsHex("#eeeae0"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, WoodsHex("#9a4a2a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#9a4a2a"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, WoodsHex("#4a4a4c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, WoodsHex("#5a5a5c"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, WoodsHex("#c8bca0"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#b8984e"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Timber, WoodsHex("#8a7a66"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, WoodsHex("#4a4a4c"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, WoodsHex("#5a7a34"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, WoodsHex("#d8d2c0"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, WoodsHex("#d8d2c0"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, WoodsHex("#6a5440"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, WoodsHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, WoodsHex("#d8d0bc"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, WoodsHex("#2a4a8a"))
				// Buckwheat, woad, field peas and Woodland's own rye; apple and cherry orchards (approved).
				.Crops(("Detail_FlowersBuckwheat", 0.3f), ("Detail_FlowersWoad", 0.25f), ("Detail_FlowersFieldPea", 0.25f), ("Detail_GrassRye", 0.3f))
				// LATER (I9b): orchard apple and cherry as fruiting orchard trees; the crab apple and gean stand in.
				.Orchards(new Entry.Orchard("CrabApple", 0.25f, 7f, "Detail_GrassPasture"), new Entry.Orchard("WildCherry", 0.12f, 8f, "Detail_GrassPasture"))
				// Approved: laid hedges of hawthorn and hazel (blackthorn, dogwood, field maple and dog rose in them, oak standards)
				// along the clearing edges, the lanes and the assarts' fields; a cleft-oak pale round the parks and manors.
				// The hedged lanes are the settlements' own (2026-10-27, owner: "limit Woodland hedges near settlements"): the cart tracks
				// and roads within LaneHedgeReach of a settlement; footpaths, trails and the ways between villages run unhedged.
				.Lines(
					new Entry.Line(BiomeLinearForm.Laid, BiomeLinearPlace.ClearingEdges, "HawthornWoodland", "HazelCoppice", "Blackthorn", "Dogwood") { Standards = new[] { "OakWoodland", "FieldMaple" } },
					With(new Entry.Line(BiomeLinearForm.Laid, BiomeLinearPlace.Lanes, "HawthornWoodland", "HazelCoppice", "DogRose", "WayfaringTree") { Standards = new[] { "OakWoodland" } }, l =>
					{
						l.Style.Reach = LaneHedgeReach;
						l.Style.Ways = BiomeLinearWays.Tracks | BiomeLinearWays.Roads;
					}),
					new Entry.Line(BiomeLinearForm.Laid, BiomeLinearPlace.FieldEdges, "HawthornWoodland", "Blackthorn", "HazelCoppice", "Dogwood") { Standards = new[] { "OakWoodland", "FieldMaple" } },
					new Entry.Line(BiomeLinearForm.Picket, BiomeLinearPlace.SiteBounds))
				// A prosperous wood: lanterns in the villages too, braziers at a stronghold's gates, the green lanes' wayside lanterns and
				// the graveyards' lanterns of the dead; candles in niches cut in the chalk down the mines.
				.Lights(POIType.Village, BiomeWayLightKind.LanternPost, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.City, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.Capital, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Castle, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Waystation, BiomeWayLightKind.WaysideLantern, 16f)
				.Lights(POIType.Monastery, BiomeWayLightKind.WaysideLantern, 16f)
				.Lights(POIType.Graveyard, BiomeWayLightKind.LanterneDesMorts, 30f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.LanternPost)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.CandleNiche, 8f)
				.GrowsVines(0.5f, 0.65f, 0.4f, 0.08f, ("TravellersJoy", 0.55f), ("Hop", 0.25f), ("BlackBryony", 0.2f))
				.Gathers("Silverleaf Patch", "King Oak", "Chanterelle Patch", "Hazelnut Cluster", "Sweet Chestnut Fall", "Flint Nodules", "Woad Patch", "Golden Mistletoe")
				.Herbs("Detail_HerbAgrimony", "Detail_HerbWildMarjoram", "Detail_WoodSpurge", "Detail_LordsAndLadies")
				// Dry chalk country: lichen-gold tiles, little moss.
				.Weathers(BiomeStructureFinish.Mossy, 0.12f, 1f)
				.ErodesAs("Temperate");
		}
	}
}
#endif
