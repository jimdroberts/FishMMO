#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>River</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry River()
		{
			// The great working river of the Middle Rhine and Moselle (biome round, approved 2026-10-18, authored 2026-10-26): a strong
			// grey-green current over multicoloured Rhine gravel armoured into riffles, dark scour-pools under the bends, fennel-weed and
			// ribbon-weed combed downstream, olive-black torrent algae on the reef ribs, golden point-bar sand, whole uprooted trees stranded
			// on the bars, and a wet margin of galingale, water pepper and the tall Stromtal flowers under crack-willows; dark greywacke
			// cliffs at the narrows. Its people are the river-folk: ship mills moored in rows, toll keeps on the rocks, treadwheel cranes,
			// lift-net platforms, rope ferries, paved fords, stone bridges with a chapel on the middle pier, log rafts. Fantasy: the river
			// keeps a hoard (Rhinegold glowing in the deepest pool), a siren on the high rock, the Old Wels under the weir, the soul ferry.
			// Where it shows: the bed (riffle, run, pool and reef by depth and current, decisions 35 and h), the bars, the scoured wet margin
			// and the mid-channel islets. The banks' trees and grasses are the shore biomes'.
			// LATER (I9 T1): every ground and object texture as River's own variant of its recipe (decision 12); the wet margin's tracks
			// motif (a granular stand-in today).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Rhinegold + Salmonrun.
			return E("River", L(Ground.MudFloodLoam,
					// The islets and exposed margin: crack-willow pollards (LATER I9b P27: the pollard and leaning forms), the Stromtal
					// flowers out of the cold band, dewberry, flood brash, Rhine pebbles, greywacke chips, beaver stumps.
					Trees(0.02f, 10f, 15f, "CrackWillow"), Band(Plants("FlowersStromtal", 4f, 160, 25f), -0.25f, 1f),
					Plants("DebrisBrash", 3f, 128, 15f), Stones(Pebbles("Rhine"), 4f), Stones(SmallRocks("Greywacke"), 1f),
					Logs("Beaver stumps", 0.01f, 12f, Wood("Stump_Beaver"), 20f)),
				Ds(
					// d0: the levee sand — flood drape on the banks, the Stromtal flowers and the bar pioneers, dewberry, brash.
					D(Ground.SandLeveeDrape, 60f, 3f, 0f, 10f, Band(Plants("FlowersStromtal", 6f, 160, 25f), -0.25f, 1f), Plants("FlowersFloodbank", 5f, 160, 25f),
						Bush("Dewberry", 0.6f), Plants("DebrisBrash", 4f, 128, 15f), Stones(Pebbles("Rhine"), 6f),
						Logs("Beaver stumps", 0.01f, 12f, Wood("Stump_Beaver"), 20f), Trees(0.008f, 14f, 15f, "CrackWillow")),
					// d1: the wet margin — glistening dark mud, galingale, bur-reed in the slow water.
					With(D(Ground.MudWetMargin, 28f, 4f, 0f, 8f, Grass("GrassGalingale", 18f, 200), Plants("BurReed", 10f, 128, 15f)),
						l => l.Height = new Vector2(0f, 0.5f)),
					// d2: the bank ledge — greywacke ribs on the steeper ground.
					D(Ground.GreywackeBankLedge, 64f, 4f, 24f, 90f, Stones(SmallRocks("Greywacke"), 3f),
						Outcrops("Greywacke ribs", 0.01f, 20f, F(RegionalRocks.Greywacke, "Ribs"), 60f)),
					// d3: the cold river's scour banks — frost-shoved cobbles (temperature band, decision 29), the bar's gravel head.
					D(Ground.GravelScourBank, 40f, 3f, 2f, 20f, Plants("FlowersFloodbank", 8f, 160, 25f), Plants("DebrisBrash", 10f, 128, 15f),
						Stones(Pebbles("Rhine"), 10f), Stones(Pebbles("Imbricated"), 4f)).Temp(-1f, -0.25f)),
				C(Ground.GreywackeNarrowsCliff, 42f), Ground.GravelArmourRiffle, Ground.GravelRhineRun)
				// The run (0.6-2 m, the riverbed slot): fennel-weed and ribbon-weed combed downstream, starwort in the slack, Rhine pebbles, the
				// waterlogged trunks, the midstream stacks and the bedrock reefs. Speeds are the river's own (decision 35 (k): flow sway).
				// LATER (I9b/I7): the eel baskets near villages and the sunk millstone (a boulder piece).
				.UnderRiver(Flowing(Sea("FennelWeed", 10f, 0.6f, 2f, 160, 25f), 0f, 1.2f), Sea("RibbonWeed", 8f, 0.3f, 1.5f, 160, 25f),
					Flowing(Sea("Starwort", 6f, 0f, 0.8f, 128, 20f), 0f, 0.4f), SeaStones(Pebbles("Rhine"), 10f, 0f, 0f),
					Sea("EelBaskets", 0.05f, 0.3f, 1.5f, 96, 20f),
					With(Logs("Sunk logs", 0.01f, 30f, Wood("Log_Sunk_River"), 20f), s => s.Depth = new Vector2(0.6f, 0f)),
					Outcrops("Midstream stacks", 0.002f, 80f, F(RegionalRocks.Greywacke, "Midstream"), 20f))
				// The riffle (to 0.6 m): the armour layer with its torrent algae and shingled cobbles.
				.BedLayer(Ground.GravelArmourRiffle, BiomeBedWaters.River, 0f, 0.6f, 0f, 0f,
					Flowing(Sea("Lemanea", 14f, 0f, 0.6f, 200, 25f), 0.8f), SeaStones(Pebbles("Imbricated"), 12f, 0f, 0.6f),
					Sea("BurReed", 4f, 0f, 0.5f, 128, 20f))
				// The Kolk (below 2 m): the scour-pools' dark silt-sand; sunk wood lies in it.
				.BedLayer(Ground.SandScourPool, BiomeBedWaters.River, 2f, 0f, 0f, 0f,
					With(Logs("Pool logs", 0.01f, 30f, Wood("Log_Sunk_River"), 20f), s => s.Depth = new Vector2(2f, 0f)))
				// The reef: bedrock ribs across the channel where the current is quick and shallow (speed >= 1.6 m/s, depth <= 1.2 m).
				.BedLayer(Ground.GreywackeReefRibs, BiomeBedWaters.River, 0f, 1.2f, 1.6f, 0f,
					Flowing(Sea("Lemanea", 10f, 0f, 1.2f, 200, 25f), 1.6f), SeaStones(SmallRocks("Greywacke"), 2f, 0f, 1.2f),
					Flowing(Outcrops("River reefs", 0.004f, 40f, F(RegionalRocks.Greywacke, "Reef"), 20f), 1.6f))
				// The generic riparian kit this round retired (alder, white-willow and poplar banks, lush grass and willow scrub, the
				// reed beds, sedge, rush, wet flowers, driftwood log jams and the grey pebbles): the bank biomes and the marshes own them.
				.Moved("main", "GrassLush", "Bush: Willow", "Trees: Alder", "Trees: WeepingWillow", "Trees: Poplar")
				.Moved("detail/0", "Log jams", Pebbles("Grey"), SmallRocks("Grey"))
				.Moved("detail/1", "Reeds", "GrassSedge", "GrassRush", "FlowersWet")
				.Ways(path: Ground.PathBarTrack, road: Ground.GreywackeFordPaving, cobbles: Ground.BasaltRhenishSetts)
				.Bars(sand: Ground.SandPointBar, gravel: Ground.GravelBleachedBar)
				// Own rock greywacke (ribs, reefs, midstream stacks; approved Q2), accepting the roofing slate, the reef-crest quartzite and
				// the Eifel basalt.
				.Accepts(RockTypes.Slate, RockTypes.Quartzite, RockTypes.Basalt)
				.CaveShell(CaveForm.Cave, "Greywacke").CaveShell(CaveForm.Grotto, "Basalt").CaveShell(CaveForm.Mine, "Slate")
				.CaveShell(CaveForm.SeaCave, "Greywacke").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Agate, 0.6f), (BiomeCaveCrystalKind.RhinegoldSeam, 0.4f))
				// The seam's warm shimmer like light through moving water, in its caves and on its crystal posts.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Caustic, warm: true)
				.Look(BiomeLookClass.Aquatic)
				.Water(BiomeWaterRole.River)
				// The bank biome brings the land animals; the river has fresh water only (the grassland habitat dropped).
				// LATER (AMBIENT-LIFE / SEA-LIFE N11): salmon at the weirs and the autumn run, sturgeon, barbel shoals, beavers, otters,
				// sandpipers and plovers on the bars, herons, dippers on cold riffles; the siren, the Old Wels, the Mouse Tower rats, the soul ferry.
				.Wildlife(BiomeHabitat.FreshWater)
				.GrowsVines(0.3f, 0.4f, 0.2f, 0.3f, ("Dodder", 1f))
				.NoCrops()
				.Herbs("Detail_FlowersFloodbank", "Detail_FlowersStromtal", "Detail_GrassGalingale")
				.Moat(BiomeMoatWater.Wet)
				.Blades("GrassGalingale", t => { WoodsBlade(t, "#4a5a2e", "#7a8a44", 0.35f, width: 0.5f, clump: 0.5f,
					head: GrassBladeHead.Plume, headSize: 0.04f, headShare: 0.5f, heads: new[] { "#2e2218" }); t.Wade = 0.3f; })
				.Blades("FlowersStromtal", t => WoodsBlade(t, "#3e6a28", "#557f30", 0.3f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.05f,
					heads: new[] { "#5a6ad0", "#e8e090", "#f0b020", "#c8d040" }))
				.Blades("FlowersFloodbank", t => WoodsBlade(t, "#5a7a3a", "#6e8e44", 0.4f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.04f,
					heads: new[] { "#f0c8d0", "#e8c020", "#f0e040", "#c88a8a" }))
				// The river's fallback water look (the bank biome decides): grey-green Rhine.
				.WaterTint(new BiomeWaterLook(SeaHex("#4a7a62"), SeaHex("#12302e"), new Vector3(0.42f, 0.11f, 0.07f), 0.28f, SeaHex("#8a7a58"), SeaHex("#eaece6")))
				.Describe("Strong grey-green river — gravel riffles and scour-pools, ship mills moored in the current, toll keeps on the rocks")
				.Names(onsets: new[] { "Bach", "Kaub", "Bing", "Bopp", "Wes", "Ander", "Lahn", "Gaar", "Rhens", "Mos", "Nah", "Sieg", "Linz" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ei", "au" },
					codas: new[] { "ach", "bach", "furt", "stein", "fels", "lei", "ern", "heim" },
					middles: new[] { "al", "er", "en", "ing" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Kolk", "Hoehle", "Stollen", "Undercroft", "Hold", "Weir-hole" },
					dungeonPrefixes: new[] { "The Drowned", "The Rhinegold", "The Siren's", "The Mouse-haunted", "The Toll-held", "The Weir-dark" },
					poiSuffixes: new[] { "Furt", "Fahr", "Lei", "Grien", "Kolk", "Zoll", "Krahnen", "Pfalz", "Muehle" },
					adjectives: new[] { "Drowned", "Rhinegold", "Siren-sung", "Toll-held", "Weir-dark", "Grey-green" },
					poiWords: new[]
					{
						("watermill", new[] { "Schiffmuehle", "Ship Mill", "Muehle" }),
						("fishingcamp", new[] { "Daubel", "Lift-net", "Salmon Stage" }),
						("keep", new[] { "Zoll", "Pfalz", "Toll Keep" }),
						("tower", new[] { "Mouse Tower", "Mauseturm" }),
						("port", new[] { "Krahnen", "Staple", "Treadwheel Quay" }),
						("shrine", new[] { "Nicholas Pillar", "Boatmen's Pillar" }),
						("bridge", new[] { "Furt", "Stone Arches", "Fahr" }),
						("monsterden", new[] { "Wels Hole", "Kolk" }),
						("landmark", new[] { "Lei", "Siren Rock" }),
					},
					about: "Rhenish: the Middle Rhine and Moselle's river-folk (Bach-, Kaub-, Bing-, Bopp-, Wes-, Ander-; -ach, -furt, -stein, -fels, -lei, -heim)")
				// The river-folk's sites: ship mills and lift-nets in the current, toll keeps and the Mouse Tower on rocks, river staples, the
				// boatmen's pillars, stone bridges, the Old Wels's hole, sunk wine barges and Roman piers, the siren rock (approved Q3, Q8:
				// all kept rare). The bank biomes build villages and towns; the stilt, drowned-village, mine, quarry and camp kinds zeroed.
				// LATER (I20/I19): channel and bank-straddling placement for the ship mills, lift-nets, toll keeps and staples (Placed sites
				// weigh 0 in River cells until then), rope ferries (FerryOver) and river traffic (I17). LATER (I7): the named pieces — ship mill,
				// toll keep, Mouse Tower, treadwheel crane, lift-net platform, rope ferry, stone bridge with a pier chapel, paved ford, log
				// raft, Oberlaender barge, Nicholas pillar, siren rock, hoard pool, Wels hole; the variants "Ship mill", "Lift-net", "Toll
				// island", "Mouse tower", "River staple", "Stone arches", "Ferry", "Ford". The kit's variants stand in.
				.Sites((POIType.Spring, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1f),
					(POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f),
					(POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.Watermill, 1.5f), (POIType.FishingCamp, 1.2f), (POIType.Keep, 0.4f), (POIType.Tower, 0.2f), (POIType.Port, 0.4f),
					(POIType.Shrine, 0.5f), (POIType.Bridge, 1f), (POIType.MonsterDen, 0.2f), (POIType.SunkenShip, 0.4f), (POIType.SunkenRuins, 0.35f),
					(POIType.Landmark, 0.08f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.Crannog, 1f), (POIType.LadyOfTheLake, 1f), (POIType.NymphPool, 0.8f), (POIType.LakeMonsterLair, 1f))
				.Favours(BiomeStructureStyle.Timber, 1.6f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				// The palette: ochre limewash in black-oak frames, slate-hung gables under altdeutsch slate, white-harled greywacke rubble on
				// tar-black footings, basalt setts, oak-blood trim, boat-green doors, tarred canvas, wheel-and-keys banners.
				// LATER (NEW-SURFACES): SlateHung, the Altdeutsch slate cut, harled greywacke and Trachyte as surfaces of their own; plain
				// slate, stone and tinted fieldstone stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, SeaHex("#d4b070"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Slate, SeaHex("#2e3238"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, SeaHex("#34383e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate, SeaHex("#34383e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, SeaHex("#e2ded4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, SeaHex("#6a2a22"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#3a5a3a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, SeaHex("#e6e2d8"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#b8b4a8"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#2e2e30"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, SeaHex("#34383e"))
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, SeaHex("#3c3c40"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Stone, SeaHex("#e2ded4"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Slate, SeaHex("#34383e"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron)
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#3a3028"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#a8282a"))
				// Hanging lanterns at the mills and staples, wall lanterns on the keeps and the tower, lantern posts on the bridges; the
				// slate galleries' frog lamps. The bank biomes light the towns.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital)
				.Lights(POIType.Watermill, BiomeWayLightKind.HangingLantern, 12f)
				.Lights(POIType.Port, BiomeWayLightKind.HangingLantern, 12f)
				.Lights(POIType.Keep, BiomeWayLightKind.WallLantern, 10f)
				.Lights(POIType.Tower, BiomeWayLightKind.WallLantern, 10f)
				.Lights(POIType.Bridge, BiomeWayLightKind.LanternPost, 13f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.FrogLamp, 10f)
				.Gathers("River Gold", "Agate Nodule", "Noble Crayfish", "Water Pepper", "Soapwort", "Rhinegold Seam")
				// Boat-tar black-brown on timber near the water and hulls, a green-black waterline on stone.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Tarred, 0.4f, 0.8f)
				.ErodesAs("Temperate");
		}
	}
}
#endif
