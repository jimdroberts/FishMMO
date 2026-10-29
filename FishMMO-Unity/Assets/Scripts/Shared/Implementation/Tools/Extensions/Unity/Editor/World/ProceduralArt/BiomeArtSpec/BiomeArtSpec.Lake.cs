#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Lake</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Lake()
		{
			// The great clear lakes of the Alpine foreland (biome round, approved 2026-10-18, authored 2026-10-26): green-glass water over a
			// pale wave-cut shelf that drops at the halde into blue-black depth; furrow-stones crusted blue-green, quillwort lawns and
			// pondweed forests under the water, lake-balls rolling on the sand; on the strand a wave-washed shoreweed turf with the lake's
			// own sky-blue forget-me-not; dark lake-bulrush standing far out; grey-green molasse cliffs honeycombed at the waterline. Its
			// people are the pile-folk: bark-roofed houses on oak pile fields over the shallows, plank causeways, pile breakwaters, square-
			// sail barges, logboats, wattle fish-garths, island minsters. Fantasy: the lake keeps what it drowned (dim lit pile-towns,
			// nixie stairs, a hand rising from the mere, the lake wyrm), kept rare. Lake grows no trees: the shore biomes plant the banks.
			// Where it shows: the bed (shelf, halde, deeps by depth below the lake's own level, decision 35) and the wave-washed strand.
			// LATER (I9 T1): every ground and object texture as Lake's own variant of its recipe (decision 12); until then the grounds
			// are Lake's own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Kingfisher + Nixdeep.
			return E("Lake", L(Ground.PebblesStrandShingle,
					// The strand: wave-sorted shingle with shoreweed on its low edge, the strand turf, the lake's drift litter, skimming
					// stones and furrow-stones, the odd molasse chunk, purple-willow scrub on its top edge.
					Grass("GrassShoreweed", 6f, 160), Plants("FlowersStrand", 3f, 160, 25f), Plants("DebrisDriftLine", 3f, 128, 15f),
					Stones(Pebbles("Strand"), 10f), Stones(Pebbles("Furrow"), 6f), Stones(SmallRocks("Molasse"), 1f),
					Bush("LakeWillowScrub", 0.4f)),
				Ds(
					// d0: the isoetid flat — clean sand under a shoreweed and needle-spike turf, bulrush on its wet edge, water horsetail.
					With(D(Ground.SandIsoetidFlat, 72f, 3f, 0f, 6f, Grass("GrassShoreweed", 22f, 200), Grass("GrassNeedleSpike", 10f, 160),
						Plants("LakeBulrush", 2f, 128, 15f), Plants("FlowersStrand", 8f, 160, 25f), Plants("HorsetailWater", 5f, 128, 15f)),
						l => l.Height = new Vector2(0f, 0.47f)),
					// d1: the drift line — a thin band along the waterline.
					D(Ground.LitterGeschwemmsel, 24f, 4f, 0f, 8f, Plants("DebrisDriftLine", 18f, 160, 15f), Plants("FlowersStrand", 2f, 128, 25f)),
					// d2: molasse ledges on the steeper ground, waterline tafoni under the cliff foot.
					D(Ground.MolasseLedge, 64f, 4f, 22f, 90f, Stones(SmallRocks("Molasse"), 3f),
						Outcrops("Molasse ledges", 0.01f, 24f, F(RegionalRocks.Molasse, "Ledges"), 60f),
						Outcrops("Molasse tafoni", 0.004f, 40f, F(RegionalRocks.Molasse, "Tafoni"), 60f)),
					// d3: the cold lakes' ice-push ramparts (temperature band, decision 29): shoved gravel, scrub on the top edge.
					D(Ground.GravelPushRampart, 40f, 3f, 2f, 18f, Bush("LakeWillowScrub", 0.4f), Stones(Pebbles("Strand"), 4f))
						.Temp(-1f, -0.2f)),
				C(Ground.MolasseTafoniCliff, 40f), Ground.LimestoneFurrowShelf, Ground.SandDeltaGlacialFlour)
				// The shelf (0-3 m below the lake's own level, decision 35): the bulrush stands, lobelia, quillwort lawns, willow moss on the
				// cobbles, lake-balls, sponges, the furrow-stones, water horsetail, the drowned pile-fields, sunk logs, the lone erratic.
				.Under(In(BiomeBedWaters.Lake, Sea("LakeBulrush", 8f, 0.3f, 2.5f, 160, 20f)), In(BiomeBedWaters.Lake, Sea("WaterLobelia", 6f, 0.2f, 1.5f, 160, 20f)),
					In(BiomeBedWaters.Lake, Sea("Quillwort", 14f, 1f, 3f, 200, 25f)), In(BiomeBedWaters.Lake, Sea("WillowMoss", 4f, 0.3f, 3f, 160, 25f)),
					In(BiomeBedWaters.Lake, Sea("LakeBall", 3f, 2f, 3f, 128, 15f)), In(BiomeBedWaters.Lake, Sea("LakeSponge", 2f, 0.3f, 3f, 128, 25f)),
					In(BiomeBedWaters.Lake, Sea("HorsetailWater", 4f, 0f, 0.8f, 128, 20f)), In(BiomeBedWaters.Lake, SeaStones(Pebbles("Furrow"), 8f, 0f, 3f)),
					In(BiomeBedWaters.Lake, Sea("PileStumps", 0.3f, 0.3f, 3f, 96, 20f)), In(BiomeBedWaters.Lake, Sea("NetSinkers", 1f, 0.3f, 3f, 96, 20f)),
					With(In(BiomeBedWaters.Lake, Logs("Sunken logs", 0.02f, 20f, Wood("Log_Sunken"), 25f)), s => s.Depth = new Vector2(1f, 3f)),
					With(In(BiomeBedWaters.Lake, Logs("Logboat wrecks", 0.004f, 60f, Wood("Logboat_Wreck"), 25f)), s => s.Depth = new Vector2(0.5f, 3f)),
					With(In(BiomeBedWaters.Lake, Blocks("Lake erratics", 0.003f, 60f, F(RockTypes.Gneiss, "Boulder"), 25f)), s => s.Depth = new Vector2(0.2f, 2f)))
				// The halde (3-9 m): pondweed forests the pike hide in, milfoil plumes, the quillwort's deeper lawns, lake-balls drifting in the
				// hollows, the drowned forest's stumps.
				.BedLayer(Ground.SiltPondweedHalde, BiomeBedWaters.Lake, 3f, 9f, 0f, 0f,
					Sea("Pondweed", 8f, 1.5f, 6f, 160, 25f), Sea("Milfoil", 5f, 1f, 4f, 160, 25f), Sea("Quillwort", 10f, 3f, 6f, 200, 25f),
					Sea("LakeBall", 3f, 2f, 8f, 128, 15f),
					With(Logs("Drowned stumps", 0.01f, 20f, Wood("DrownedStump"), 25f), s => s.Depth = new Vector2(3f, 9f)))
				// The deeps (below 9 m): varved gyttja, nothing grows.
				.BedLayer(Ground.MudVarvedGyttja, BiomeBedWaters.Lake, 9f, 0f, 0f, 0f)
				// The generic lakeshore kit this round retired (GrassLush, willow, alder and weeping-willow wood, reed beds, sedge, rush,
				// wet flowers, driftwood logs and the grey pebbles): the shore biomes and River own them.
				.Moved("main", "GrassLush", "Bush: Willow", "Trees: Alder", "Trees: WeepingWillow")
				.Moved("detail/0", "Reeds", "ReedsPlume", "GrassSedge", "GrassRush", "FlowersWet")
				.Moved("detail/1", "Driftwood", Pebbles("Grey"), SmallRocks("Grey"))
				.Ways(path: Ground.PathStrandTrod, road: Ground.GravelTowpath, cobbles: Ground.FlagstoneBollenstein)
				.Bars(sand: Ground.SandDeltaFlourBar, gravel: Ground.GravelErraticBar)
				// Own rock molasse (ledges, tafoni; approved Q2), accepting the limestone walls and the gneiss of the glacial basins.
				// LATER (I9): Molasse's tafoni honeycomb as Lake's own look.
				.Accepts(RockTypes.Limestone, RockTypes.Gneiss)
				.CaveShell(CaveForm.Cave, "Molasse").CaveShell(CaveForm.Grotto, "Limestone").CaveShell(CaveForm.Mine, "Limestone")
				.CaveShell(CaveForm.SeaCave, "Molasse").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.CavePearl, 0.6f), (BiomeCaveCrystalKind.Nixglass, 0.4f))
				// Nixglass's slow ripple of light, in its caves and on its crystal posts.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Caustic)
				.Look(BiomeLookClass.Aquatic)
				.Water(BiomeWaterRole.Lake)
				.Wildlife(BiomeHabitat.FreshWater)
				// LATER (SEA-LIFE N11 / AMBIENT-LIFE): great crested grebes, loons, gull colonies on the erratics, whitefish rises at dusk,
				// pike in the pondweed; the lake wyrm's V-wake and the nixies on their stair (fantasy, rare).
				.GrowsVines(0.4f, 0.6f, 0.25f, 0f, ("Bittersweet", 1f))
				.NoCrops()
				.Herbs("Detail_FlowersStrand", "Detail_GrassShoreweed", "Detail_HorsetailWater")
				.Moat(BiomeMoatWater.Wet)
				// Pile-town lanes and the plank Steg causeways are boardwalks over the water (decision 27).
				.RaisesBoardwalks(BiomeBoardwalks.Paths | BiomeBoardwalks.Streets)
				.Blades("GrassShoreweed", t => { WoodsBlade(t, "#3e5a2e", "#6a8a44", 0.3f, density: 1.2f, stiffness: 1.2f, width: 0.8f, clump: 0.6f); t.Wade = 1f; })
				.Blades("GrassNeedleSpike", t => { WoodsBlade(t, "#4a6a34", "#7a9a4a", 0.35f, density: 1.4f, width: 0.4f); t.Wade = 1f; })
				.Blades("FlowersStrand", t => WoodsBlade(t, "#3e6a28", "#557f30", 0.35f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.04f,
					heads: new[] { "#6aa8e8", "#e8d040", "#e09ab8", "#d8c8ec" }))
				// The lake's fallback water look (the shore biome decides): glass-clear green.
				.WaterTint(new BiomeWaterLook(SeaHex("#4a9a8a"), SeaHex("#08304a"), new Vector3(0.40f, 0.09f, 0.05f), 0.08f, SeaHex("#8a8670"), SeaHex("#eef2f2")))
				.Describe("Clear cold lake — wave-washed strands, quillwort lawns under green water, and pile-towns on the shallows")
				.Names(onsets: new[] { "Ueber", "Lind", "Hall", "Mond", "Atter", "Zell", "Rein", "Moos" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ei", "au" },
					codas: new[] { "see", "au", "horn", "wil", "egg", "stad" },
					middles: new[] { "al", "er", "en", "ing" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Heidenhoehle", "Grotte", "Halde", "Tiefe", "Undercroft", "Hold", "Water-door" },
					dungeonPrefixes: new[] { "The Drowned", "The Nixie-haunted", "The Green-glass", "The Pile-built", "The Lake-kept", "The Hand-held" },
					poiSuffixes: new[] { "Halde", "Fach", "Steg", "Woerth", "Spitz", "Seegfroerni", "Pfahl", "Nix" },
					adjectives: new[] { "Drowned", "Nixie-haunted", "Green-glass", "Pile-built", "Lake-kept", "Wave-washed" },
					poiWords: new[]
					{
						("stiltvillage", new[] { "Pfahlbau", "Pile-town", "Steg" }),
						("fishingcamp", new[] { "Fach", "Garth", "Net-rack" }),
						("island", new[] { "Woerth", "Insel", "Holm" }),
						("monastery", new[] { "Island Minster", "Woerth" }),
						("headland", new[] { "Spitz", "Horn" }),
						("drownedvillage", new[] { "Pile Field", "Drowned Pile-town" }),
						("sunkenruins", new[] { "Nixie Stair", "Halde" }),
						("ossuary", new[] { "Charnel House", "Skull House" }),
						("statue", new[] { "Mere Hand", "Sword Hand" }),
					},
					about: "Alemannic: the Alpine foreland's clear lakes (Ueber-, Lind-, Hall-, Mond-, Atter-, Zell-; -see, -au, -horn, -wil, -egg)")
				// The pile-folk's sites: pile-towns and fish-garths over the shallows, yards and a port, island minsters, shrines and
				// charnel houses, hermitages in the molasse caves, the mere hand, a harbour light, the lake wyrm's den, and under the water the
				// drowned pile-field, ruins, sunk barges and (rare) a sunk city (approved Q4, Q7: all kept rare). The shore biomes build
				// villages and towns; the mines, quarries and camps of the land zeroed.
				// LATER (I21/I22): over-water pile placement with a Steg causeway for StiltVillage/FishingCamp, and the Sunken* kinds under the
				// lake's own level; Placed sites weigh 0 in water-role biomes until then. LATER (I7): the named pieces — pile house, pile-town,
				// Steg causeway, Laedine barge and logboats (I17 lake traffic), fish-garth, net racks, whitefish smokehouse, water-door
				// boathouse, island minster, painted-skull charnel house, ice-cross shrine (cold, Frozen), nixie stair, mere hand; the variants
				// "Lake piles", "Garth", "Island", "Painted skulls", "Pile field". The kit's variants stand in.
				.Sites((POIType.Spring, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1f),
					(POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f),
					(POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.StiltVillage, 2f), (POIType.FishingCamp, 2f), (POIType.Dockyard, 0.5f), (POIType.Port, 0.3f), (POIType.Monastery, 0.8f),
					(POIType.Shrine, 0.6f), (POIType.Ossuary, 0.3f), (POIType.Hermitage, 0.4f), (POIType.Statue, 0.15f), (POIType.Lighthouse, 0.15f),
					(POIType.MonsterDen, 0.3f), (POIType.DrownedVillage, 1f), (POIType.SunkenRuins, 0.5f), (POIType.SunkenShip, 0.8f),
					(POIType.SunkenCity, 0.05f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.Crannog, 1.2f), (POIType.LadyOfTheLake, 1f), (POIType.NymphPool, 0.8f), (POIType.LakeMonsterLair, 1f))
				.Favours(BiomeStructureStyle.Timber, 2f)
				.Favours(BiomeStructureStyle.Stone, 0.4f)
				.Favours(BiomeStructureStyle.Earth, 0.2f)
				// The palette: lake-chalk daub, silvered split fir, bark roofs, black waterlogged oak piles, molasse ashlar, bulrush matting,
				// lake-blue doors and banners.
				// LATER (NEW-SURFACES U8): the Bark roof surface (red-brown fir/elm bark slabs weighted with ridge poles) and lake-chalk daub;
				// riven shingle in the bark's colour stands in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, SeaHex("#dcd8c8"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, SeaHex("#8a8680"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, SeaHex("#5e3a2a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, SeaHex("#8a8a84"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Timber, SeaHex("#241e18"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#3a6a8a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#747c6c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#747c6c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, SeaHex("#8a8a84"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#dcd8c8"))
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, SeaHex("#7a5e44"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Reed, SeaHex("#6a6a4a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber)
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron)
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#8a6a4a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#3a6a8a"))
				// Cressets on the pile heads double as the night torch-fishing light; the harbour's hanging lanterns; the drowned sites' cold
				// lamps (a dim green Glow, decision 28); the minsters' lantern posts. The shore biomes light the towns.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital)
				.Lights(POIType.StiltVillage, BiomeWayLightKind.Cresset, 14f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.Cresset, 14f)
				.Lights(POIType.Port, BiomeWayLightKind.HangingLantern, 12f)
				.Lights(POIType.Dockyard, BiomeWayLightKind.HangingLantern, 12f)
				.Lights(POIType.Monastery, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.DrownedVillage, BiomeWayLightKind.DrownedLamp, 10f)
				.Lights(POIType.SunkenRuins, BiomeWayLightKind.DrownedLamp, 10f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 10f)
				.Gathers("Lake Ore", "Lake Ball", "Spongilla", "Bulrush Bundle", "Nixglass")
				// The green waterline and the grey silvering above it.
				.Weathers(BiomeStructureFinish.Algae, 0.35f, 0.8f)
				.ErodesAs("Temperate");
		}
	}
}
#endif
