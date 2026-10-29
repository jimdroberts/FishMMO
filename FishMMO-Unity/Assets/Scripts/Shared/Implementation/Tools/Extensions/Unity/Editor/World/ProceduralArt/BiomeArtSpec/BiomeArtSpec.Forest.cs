#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Forest</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Forest()
		{
			// Primeval temperate broadleaf-and-fir forest (biome round, approved 2026-10-16, authored 2026-10-26): a closed vault of
			// tall beech on grey pillar boles over a bare copper leaf floor, veteran stag-headed oaks and small-leaved limes, dark
			// silver fir and yew in the cool hollows and toward the top of its height band, ramsons and butterbur in the stream
			// ravines, ash and alder by the water, mossy sandstone blocks heaped under it all, and deadwood everywhere. Its people
			// are foresters, charcoal burners, bodgers and outlaws; its fantasy the green man and the fey (foxfire on rotten wood,
			// faerie lights, wyrdstone). Taiga keeps the spruce, birch and fly agaric; Woodland the hazel, bramble and open oak.
			// LATER (I9): every ground and object texture as Forest's own variant of its recipe (decision 12); until then the
			// grounds below are Forest's own recipes and the stones, barks and leaves the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Faerie + Witchmoss (BiomeTemplate.rareLookA/B), Moonbell only in them.
			return E("Forest", L(Ground.ForestFloorBeech,
					// The closed vault: beech dominant with veteran oak and lime, crowns touching, few lone trees (stand of three equal
					// shares: the stand field's species patches give beech its broad swathes).
					Stand(Was(2.5f, 6f), 30f, W(0.92f, 1f, open: 0.02f), "BeechForest", "OakForest", "Lime"),
					// Silver fir rising toward the top of the biome's height band (the cool hollows carry their own, detail 2).
					With(Stand(Was(1.5f, 6f), 30f, W(0.5f, 0.9f), "SilverFir", "BeechForest"), s => s.Height = new Vector2(0.54f, 1f)),
					Grass("GrassWoodMelick", 3f), Grass("GrassWoodrush", 1.5f),
					Plants("FlowersBeechwood", 4f, 180), Plants("FernShuttlecock", 8f), Plants("DogsMercury", 6f), Plants("CushionMossPincushion", 1f),
					Plants("IvyForest", 3f, 128), Plants("SerpentRoots", 0.8f),
					Plants("DebrisBeechMast", 10f, 128), Plants("DebrisBranchMossy", 3f, 128), Plants("MushroomCep", 0.8f), Plants("MushroomCluster", 0.6f),
					Bush("HollyForest", 0.5f), Bush("Elder", 0.2f), Bush("Spindle", 0.15f),
					Logs("Fallen logs", 0.35f, 8f, Wood("FallenLog_Oak", "FallenLog_Beech", "Snag_Beech", "Stump_Bracket", "RootPlate")),
					// LATER (I9b): a walk-in hollow oak (DeadwoodForm Hollow); the hollow veteran stump stands in.
					Logs("Hollow oaks", 0.01f, 60f, Wood("HollowOak")),
					// LATER (I9): Forest's own sandstone and flint stones (decision 6/12); the catalogue's sandstone stands in.
					Stones(SmallRocks("Sandstone"), 0.5f),
					Blocks("Sandstone blocks", 0.02f, 16f, F(RockTypes.Sandstone, "Block")),
					// LATER (NEW-ROCK-TYPES): a "Chaos" shape (4–9 weathered cuboids heaped and tilted, Fontainebleau); tight heaps of
					// blocks and tilted slabs stand in.
					With(Blocks("Sandstone chaos", 0.004f, 50f, F(RockTypes.Sandstone, "Block", "Tilted")), s => { s.ClusterMetres = 14f; s.ClusterSize = 7f; }),
					Outcrops("Sandstone pedestals", 0.004f, 40f, F(RockTypes.Sandstone, "Pedestal")),
					Blocks("Gneiss boulders", 0.006f, 24f, F(RockTypes.Gneiss, "Boulder"))),
				Ds(
					// d0: cushion moss over roots and boulders.
					D(Ground.MossCushion, 48f, 2f, 0f, 38f, Plants("FernShuttlecock", 10f), Grass("GrassWoodrush", 6f), Plants("FlowersBeechwood", 3f, 180),
						Plants("CushionMossPincushion", 8f), Plants("MushroomPorcelain", 0.8f), Plants("MushroomFoxfire", 0.4f), Plants("DebrisBranchMossy", 2f, 128),
						Stand(Was(0.6f, 5f), 30f, W(0.7f, 0.9f), "Hornbeam"),
						Logs("Mossy logs", 0.25f, 10f, Wood("FallenLog_Beech", "FallenLog_Oak", "Snag_Beech", "Stump_Bracket")),
						Blocks("Mossy sandstone blocks", 0.02f, 16f, F(RockTypes.Sandstone, "Block"))),
					// d1: the ramsons carpet — its own low, flat, damp layer of the valley floors and stream ravines (approved).
					With(D(Ground.ForestFloorRamsons, 96f, 3f, 0f, 15f, Plants("FlowersRamsons", 22f, 220), Grass("GrassSedgePendulous", 8f), Plants("Butterbur", 5f),
						Plants("FernHartstongue", 4f), Stand(Was(0.5f, 6f), 30f, W(0.6f, 0.8f), "Ash", "Alder")), l => l.Height = new Vector2(0.45f, 0.52f)),
					// d2: silver-fir duff in the cool hollows, yew under the firs.
					D(Ground.NeedleLitterFir, 120f, 2.5f, 0f, 30f, Stand(Was(1.5f, 6f), 30f, W(0.85f, 1f), "SilverFir", "Yew", "BeechForest"),
						Bush("ButchersBroom", 1f), Grass("GrassWoodMelick", 4f), Plants("DogsMercury", 4f), Plants("DebrisBeechMast", 6f, 128), Bush("HollyForest", 0.3f)),
					// d3: root-laced humus banks.
					D(Ground.SoilRootHumus, 64f, 2f, 22f, 40f, Grass("GrassWoodrush", 5f), Plants("FernShuttlecock", 6f), Plants("FernHartstongue", 4f),
						Plants("SerpentRoots", 2f), Plants("IvyForest", 2f, 128), Plants("MushroomCluster", 1.2f), Plants("MushroomFoxfire", 1.5f),
						Stand(Was(0.6f, 5f), 30f, W(0.7f, 0.9f), "Hornbeam"), Stones(SmallRocks("Sandstone"), 2f),
						Blocks("Sandstone concretions", 0.01f, 12f, F(RockTypes.Sandstone, "Concretion")),
						With(Blocks("Shale scree", 0.02f, 12f, F(RockTypes.Shale, "Scree", "Ledges")), s => s.Slope = new Vector2(30f, 40f))),
					// d4: mossy forest sandstone on the steep ground, ivy and hart's-tongue on its ledges, the odd lone yew.
					D(Ground.SandstoneForest, 64f, 4f, 32f, 90f, Plants("IvyForest", 8f, 128, 60f), Plants("FernHartstongue", 6f, 160, 60f),
						Plants("CushionMossPincushion", 3f, 160, 60f), Stones(SmallRocks("Sandstone"), 3f),
						Outcrops("Sandstone ledges", 0.015f, 20f, F(RockTypes.Sandstone, "Ledges")), Trees(0.04f, 30f, 45f, "Yew"))),
				C(Ground.SandstoneForestFace, 42f), Ground.MudLeafMould, Ground.GravelBrook)
				.Moved("main", "Trees: Oak, Birch")
				// The rules the round retired (Taiga's spruce, bilberry and needles, Woodland's hazel and bramble, the generic shrub and
				// grey boulders), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "Trees: Oak, Birch, Beech", "Fern", "DebrisForest", "ShrubSmall", "Bush: Hazel", "Bush: Bramble", "Bush: Laurel", "Bush: Holly",
					"FlowersWoodland", "DebrisBranch", "MushroomForest", "Ivy")
				.Moved("detail/0", "Fern", "Bush: Rhododendron")
				.Moved("detail/1", "Trees: Spruce", "Bush: Bilberry", "DebrisNeedle")
				.Moved("detail/2", "Boulders")
				.Ways(path: Ground.PathBeechLitter, road: Ground.PathHolloway, cobbles: Ground.FlagstoneSandstone)
				.Bars(sand: Ground.SandHumic, gravel: Ground.GravelSandstoneFlint)
				// Own rock sandstone (its formations); approved: accepts Shale, Limestone, Schist, Gneiss; no granite or slate.
				.Accepts(RockTypes.Sandstone, RockTypes.Shale, RockTypes.Limestone, RockTypes.Schist, RockTypes.Gneiss)
				.Crystals((BiomeCaveCrystalKind.GreenFluorite, 0.6f), (BiomeCaveCrystalKind.Wyrdstone, 0.4f))
				.Look(BiomeLookClass.Forest)
				// Approved: the streams' dippers, newts and dragonflies (FreshWater); chipmunks dropped creature-side.
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): woodpecker, jay, wood pigeon, stag beetle, hedgehog, roe deer, faerie lights at dusk.
				.Moat(BiomeMoatWater.Wet)
				.Blades("GrassWoodrush", t => WoodsBlade(t, "#2a4418", "#4e7028", 0.3f, density: 1f, stiffness: 0.8f, width: 1.2f, clump: 0.6f,
					head: GrassBladeHead.Plume, headSize: 0.05f, headShare: 0.3f, heads: new[] { "#6a4a2a" }))
				.Blades("GrassWoodMelick", t => WoodsBlade(t, "#3a5a24", "#6a8a3a", 0.4f, density: 0.8f, width: 0.6f, clump: 0.25f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.25f, heads: new[] { "#7a4a5a" }))
				.Blades("GrassSedgePendulous", t => WoodsBlade(t, "#3a5a2a", "#6a7e44", 0.35f, width: 1.3f, bend: 0.7f,
					head: GrassBladeHead.SeedHead, headSize: 0.06f, headShare: 0.35f, heads: new[] { "#5a4a2a" }))
				.Blades("FlowersBeechwood", t => WoodsBlade(t, "#3e6a28", "#557f30", 0.45f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.025f,
					heads: new[] { "#f6f4f0", "#f2d020", "#f4ecf0", "#f8f8f4" }))
				.Blades("FlowersRamsons", t => WoodsBlade(t, "#3a6a22", "#5a8a30", 0.3f, width: 1.6f, head: GrassBladeHead.Flower, sprinkle: 24f, headSize: 0.03f,
					headShare: 0.5f, heads: new[] { "#f6f6f0" }))
				.Sites((POIType.Camp, 1.3f), (POIType.Shrine, 1.4f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1.4f), (POIType.HolyWell, 1f),
					(POIType.Clearing, 1.5f), (POIType.Cave, 1f), (POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.WitchHut, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.Overhang, 0.5f), (POIType.BanditCamp, 1.8f),
					(POIType.HuntingLodge, 2.2f), (POIType.LumberCamp, 2f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1.3f),
					(POIType.Nest, 0.6f), (POIType.RitualSite, 0.6f), (POIType.StoneCircle, 0.3f), (POIType.Temple, 1f), (POIType.Monastery, 0.6f),
					(POIType.FeyRing, 1.8f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 0.6f), (POIType.Barrow, 0.8f), (POIType.Crypt, 1f), (POIType.Battlefield, 1f), (POIType.Ossuary, 1f),
					(POIType.Ruins, 1f), (POIType.RuinedTower, 0.7f), (POIType.Statue, 0.2f), (POIType.Obelisk, 0.2f), (POIType.AncientRoad, 1f),
					(POIType.AbandonedFarm, 0.6f), (POIType.Hermitage, 1.6f), (POIType.Village, 0.9f), (POIType.Town, 0.6f), (POIType.City, 0.3f),
					(POIType.Capital, 0.1f), (POIType.Port, 0.6f), (POIType.Keep, 1f), (POIType.Castle, 0.4f), (POIType.Fortress, 0.3f),
					(POIType.TradingPost, 0.3f), (POIType.Waystation, 0.6f), (POIType.Mine, 0.3f), (POIType.Quarry, 0.5f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.AncientTree, 2.2f), (POIType.HerbGrove, 1.6f), (POIType.Dockyard, 0.6f), (POIType.Watermill, 0.6f),
					(POIType.LumberMill, 1.5f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 1.2f), (POIType.GoblinWarren, 1.2f), (POIType.HauntedManor, 0.8f),
					(POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.5f),
					(POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.WishingStone, 0.8f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.LostLibrary, 0.5f), (POIType.DruidGrove, 1.4f), (POIType.TreeVillage, 1.2f), (POIType.TrollStones, 1f),
					(POIType.SpiderThicket, 1.2f), (POIType.CharcoalClamps, 1.2f), (POIType.GreenwoodHideout, 1.2f), (POIType.DryadOak, 1f),
					(POIType.GoblinMarket, 0.8f), (POIType.ElfWaystone, 1f), (POIType.WolfDen, 1f), (POIType.MossColossus, 0.8f),
					(POIType.BeeGarden, 0.5f), (POIType.HagsCauldron, 0.5f), (POIType.CovenCircle, 0.6f), (POIType.NymphPool, 0.8f))
				.Prefers(POIType.Village, "Timber", 1.8f)
				.Prefers(POIType.Village, "Longhouse", 0.2f)
				.Prefers(POIType.Village, "Stone", 0.4f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.4f)
				.Prefers(POIType.Town, "Palisaded", 1.2f)
				.Prefers(POIType.Town, "Stone", 0.4f)
				.Prefers(POIType.City, "Timber", 1.2f)
				.Prefers(POIType.City, "Stone", 0.8f)
				.Prefers(POIType.Capital, "Timber", 1.2f)
				.Prefers(POIType.Capital, "Stone", 0.8f)
				.Prefers(POIType.Keep, "Palisade", 1.2f)
				.Prefers(POIType.Keep, "Stone", 0.8f)
				.Prefers(POIType.Castle, "Stone", 1f)
				.Prefers(POIType.Castle, "Highland", 0.2f)
				.Prefers(POIType.Fortress, "Palisade", 1.5f)
				.Prefers(POIType.HuntingLodge, "Lodge", 2f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 0.4f)
				.Prefers(POIType.BanditCamp, "Open", 1.5f)
				.Prefers(POIType.Tower, "Wizard", 1.4f)
				.Prefers(POIType.Waystation, "Inn", 1.5f)
				.Prefers(POIType.Camp, "Lean-tos", 1.5f)
				.Prefers(POIType.Camp, "Crude", 0.8f)
				.Prefers(POIType.Camp, "Hide Tents", 0.4f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Hermitage, "Cottage", 1.2f)
				.Prefers(POIType.Hermitage, "Stone", 0.8f)
				.Prefers(POIType.WitchHut, "Cottage", 1.5f)
				.Prefers(POIType.WitchHut, "Stilt Hut", 0f)
				.Prefers(POIType.Monastery, "Cloister", 1f)
				.Prefers(POIType.BanditCamp, "Palisade", 1f)
				.Prefers(POIType.Monastery, "Timber", 1f)
				.Prefers(POIType.Ruins, "Stone", 1.5f)
				.Prefers(POIType.Ruins, "Burned", 0.3f)
				.Prefers(POIType.Graveyard, "Railed", 1f)
				.Prefers(POIType.Graveyard, "Earthen", 0.8f)
				.Prefers(POIType.Tower, "Timber", 1.2f)
				.Prefers(POIType.Tower, "Stone", 0.8f)
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
				// LATER (I7): Forest's signature pieces — the treehouse hall (a Village variant and at Ancient Tree / Fey Ring sites,
				// approved), charcoal-burner's camp, cruck lodge, bodger's shelter, pannage pen, high seat, holy well, hollow-tree
				// cell, sawpit, green-man stone; until then the kit's variants below stand in.
				.Favours(BiomeStructureStyle.Timber, 1.6f)
				.Favours(BiomeStructureStyle.Stone, 0.8f)
				.Favours(BiomeStructureStyle.Earth, 0.6f)
				.Favours(BiomeStructureStyle.Hide, 0.4f)
				.Favours(BiomeStructureStyle.Iron, 0.2f)
				// The palette: ochre wattle-and-daub in dark oak close-studding, bracken-brown thatch and riven-oak shingles, buff
				// sandstone plinths and setts, Lincoln green cloth. LATER (NEW-SURFACES U11): split-oak slab logs, riven-oak shingle
				// and sandstone stone-slate surfaces as Forest's own; the shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, WoodsHex("#d6c49a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, WoodsHex("#d6c49a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, WoodsHex("#3a2a1a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, WoodsHex("#6a5030"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#7a6a56"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Sandstone, WoodsHex("#8a8270"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, WoodsHex("#4a3622"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Paving, BiomeStructureMaterial.Sandstone, WoodsHex("#8e8670"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone, WoodsHex("#9a927c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, WoodsHex("#d6c49a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, WoodsHex("#7a7262"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, WoodsHex("#7a6a56"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Sandstone, WoodsHex("#8e8670"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, WoodsHex("#6a5440"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Sandstone, WoodsHex("#8a8270"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, WoodsHex("#4a5a28"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, WoodsHex("#8a8270"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, WoodsHex("#5a4430"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, WoodsHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, WoodsHex("#3e5a2e"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, WoodsHex("#2a4e26"))
				// Oats, spelt and field beans in the assarts (LATER (I7): stumps and brash at the field edges).
				.Crops(("Detail_GrassOats", 0.45f), ("Detail_GrassSpelt", 0.3f), ("Detail_FlowersFieldBean", 0.25f))
				// The assarts' woodbanks: an earth bank with a laid hedge of the understorey's shrubs along its top.
				.Lines(new Entry.Line("Clawdd", "HollyForest", "Elder", "Spindle"))
				// Dark lanes by torchlight, lanterns in the towns, braziers at a stronghold's gates, the forest roads' crook lanterns
				// and the fey sites' glowjars.
				.Lights(POIType.Village, BiomeWayLightKind.TorchPost, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.City, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.Capital, BiomeWayLightKind.LanternPost, 12f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Castle, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Waystation, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.HuntingLodge, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.Monastery, BiomeWayLightKind.CrookLantern, 14f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.Glowjar, 8f)
				.Lights(POIType.AncientTree, BiomeWayLightKind.Glowjar, 8f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.LanternPost)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 10f)
				// LATER (I2f-b vine kinds / I9b strand leaf): HangingRoots root curtains at cave mouths and overhangs; ivy stands in.
				.GrowsVines(0.9f, 0.95f, 0.35f, 0.3f, ("Ivy", 0.75f), ("Honeysuckle", 0.25f))
				.Gathers("Iron Vein", "Silverleaf Patch", "Morel Cluster", "Ancient Oak", "Yew Heartwood", "Wild Garlic", "Truffle Patch", "Foxfire Wood")
				.Herbs("Detail_FlowersRamsons", "Detail_Butterbur", "Detail_FernHartstongue", "Detail_HerbWoodBetony")
				// The dampest, shadiest moss of the temperate set.
				.Weathers(BiomeStructureFinish.Mossy, 0.35f, 1f)
				.ErodesAs("Temperate");
		}

		/// <summary>
		/// A blade type's look as a round's draft gives it (<see cref="Entry.Blades"/>; shared by the Forest and Woodland files): root and tip colours over the mesh's, the ground
		/// tint, and the shape and head values that are set (null or omitted keeps the type's own).
		/// </summary>
		private static void WoodsBlade(BiomeGrassType t, string root, string tip, float groundTint, float? density = null, float? stiffness = null,
			float? width = null, float? clump = null, float? bend = null, float? heightScale = null, GrassBladeHead? head = null, float? sprinkle = null,
			float? headSize = null, float? headShare = null, string[] heads = null)
		{
			t.RootColour = WoodsHex(root);
			t.TipColour = WoodsHex(tip);
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
				t.HeadColours = System.Array.ConvertAll(heads, WoodsHex);
				t.HeadColoursOverMesh = true;
			}
		}

		/// <summary>An sRGB colour from #rrggbb, for the Forest and Woodland files.</summary>
		private static Color WoodsHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
