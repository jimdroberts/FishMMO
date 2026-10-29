#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Beach</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Beach()
		{
			// The amber strand of the south-east Baltic (Curonian and Vistula spits; biome round, approved 2026-10-18, authored 2026-10-26):
			// a wide pale quartz strand that squeaks underfoot, a foredune of marram and blue lyme grass, behind it the grey dune of
			// hair-grass, reindeer lichen and crowberry under wind-flagged pines, wandering dunes burying old forests, a black wrack line
			// that glints with amber after the autumn storms, blue-earth clay at the scarps' foot. One culture (the amber-strand folk) with
			// two vegetation bands: the cool grey dune (t <= 0.45) and the warm sea-oats and sea-grape strand (t >= 0.45, coconut palms
			// from t >= 0.65). The ground bands below read the tide (decision 45): swash and beachrock between the tides, the wrack
			// line at the high-water mark, the dunes above it.
			// LATER (I9 T1): every ground and object texture as Beach's own variant of its recipe (decision 12); until then the grounds
			// are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Seaglass (milky jade) + Nacre (pearl pink).
			return E("Beach", L(Ground.SandSinging,
					// The foredune and the upper strand: marram and lyme grass cool, sea-oats warm, strand flowers and runners, sea
					// sandwort, cockles on the wrack, silver drift logs, pink erratics, a windshorn willow or hedge of sea grape.
					Band(Trees(0.03f, 14f, 20f, "StrandCoconut"), 0.65f, 1f),
					Band(Grass("GrassMarram", 18f, 160), -1f, 0.55f), Band(Grass("GrassLyme", 8f, 160), -1f, 0.5f), Band(Grass("GrassGreyHair", 3f, 160), -1f, 0.5f),
					Band(Grass("GrassSeaOats", 14f, 160), 0.45f, 1f),
					Band(Plants("FlowersAmberStrand", 3f, 160, 25f), -1f, 0.5f), Band(Plants("FlowersWarmStrand", 2f, 160, 25f), 0.45f, 1f),
					Band(Plants("BeachVineRailroad", 4f, 160, 25f), 0.45f, 1f), Band(Plants("SeaBindweedRunner", 3f, 160, 25f), -1f, 0.5f),
					Band(Plants("SeaSandwort", 3f, 160, 25f), -1f, 0.55f), Band(Plants("DebrisCoconut", 2f, 128, 20f), 0.65f, 1f),
					Plants("DebrisAmberWrack", 3f, 128, 15f), Band(Plants("AmberDrift", 0.3f, 128, 15f), -1f, 1f),
					Band(Plants("ShellsCockle", 1.5f, 128, 15f), -1f, 0.5f), Band(Plants("ShellsOlive", 1.5f, 128, 15f), 0.5f, 1f),
					Band(Bush("DuneWillow", 0.2f), -1f, 0.45f), Band(Bush("SeaGrape", 0.3f), 0.45f, 1f),
					Logs("Drift logs", 0.02f, 15f, Wood("Driftwood_Silver"), 20f),
					Stones(SmallRocks("Beachrock"), 1f),
					// Pink rapakivi erratics on the upper strand (a few with pecked cup-marks; LATER I7: the cup-marked boulder).
					Outcrops("Erratics", 0.15f, 60f, F(RockTypes.Granite, "Corestone"), 20f)),
				Ds(
					// d0: the swash — wet glassy sand between the tides: swash-line arcs, foam, shell litter, lugworm casts cool and ghost-crab
					// burrows warm.
					D(Ground.SandSwash, 64f, 3f, 0f, 5f, Plants("ShellsCockle", 3f, 128, 15f), Band(Plants("LugwormCasts", 8f, 128, 15f), -1f, 0.55f),
						Band(Plants("CrabBurrows", 6f, 128, 15f), 0.45f, 1f)).Tide(-1f, 0.6f),
					// d1: the amber wrack line at the high-water mark — eelgrass mats, lignite, amber glints and sea beans, sea kale and
					// sandwort on its top edge, silver drift, flint-and-erratic shingle.
					D(Ground.LitterWrackAmber, 20f, 4f, 0f, 8f, Plants("DebrisAmberWrack", 14f, 128, 15f), Plants("AmberDrift", 1.2f, 128, 15f),
						Band(Plants("SeaBeans", 1f, 128, 15f), 0.5f, 1f), Band(Plants("FlowersAmberStrand", 2f, 160, 25f), -1f, 0.5f),
						Band(Plants("SeaKale", 2f, 128, 20f), -1f, 0.55f), Band(Plants("SeaSandwort", 4f, 128, 20f), -1f, 0.55f),
						Band(Grass("GrassLyme", 3f, 160), -1f, 0.5f), Logs("Drift logs", 0.02f, 15f, Wood("Driftwood_Silver"), 20f),
						Stones(Pebbles("Flint"), 3f)).Tide(0.5f, 1.6f),
					// d2: the grey dune (cool) — hair-grass, reindeer lichen, dune star moss, strand flowers, crowberry mats, dune willow
					// and burnet rose, and the dune-pine belt of wind-flagged pines. The brushwood-mat road runs over it.
					D(Ground.LichenGreyDune, 48f, 3f, 0f, 20f, Grass("GrassGreyHair", 16f, 160), Grass("GrassMarram", 6f, 160), Plants("LichenGreyDune", 10f, 160, 20f),
						Plants("MossDuneStar", 8f, 160, 20f), Plants("FlowersAmberStrand", 6f, 160, 25f), Plants("SeaBindweedRunner", 3f, 160, 25f),
						Bush("DuneWillow", 0.6f), Bush("BurnetRose", 0.4f), Bush("Crowberry", 0.8f), Trees(0.03f, 14f, 25f, "WindPine"))
						.Temp(-1f, 0.45f).Tide(1.5f, BiomeSeaFloors.OpenDepth).Ways(road: Ground.PathBrushwoodMat),
					// d3: the warm backdune — sea-oats, railroad vine, beach flowers, a windshorn hedge of sea grape.
					D(Ground.SandWarmBackdune, 48f, 3f, 0f, 20f, Grass("GrassSeaOats", 10f, 160), Plants("FlowersWarmStrand", 6f, 160, 25f),
						Plants("BeachVineRailroad", 5f, 160, 25f), Plants("CrabBurrows", 3f, 128, 15f), Bush("SeaGrape", 0.4f))
						.Temp(0.45f, 1f).Tide(1.5f, BiomeSeaFloors.OpenDepth).Ways(road: Ground.PathBrushwoodMat),
					// d4: slip faces and blowouts — avalanche tongues, marram and sea-oats, the silver snags of a forest a walking dune
					// buried, a lag of flint and pink granite.
					D(Ground.SandSlipTongues, 64f, 3f, 22f, 36f, Band(Grass("GrassMarram", 14f, 160), -1f, 0.55f), Band(Grass("GrassSeaOats", 8f, 160), 0.45f, 1f),
						Band(Logs("Dune snags", 0.006f, 30f, Wood("DuneSnag"), 36f), -1f, 0.55f), Stones(Pebbles("Flint"), 1f)),
					// d5: beachrock — seaward-dipping cemented benches between the tides, with their broken slabs.
					D(Ground.BeachrockPavement, 30f, 4f, 0f, 14f, Stones(SmallRocks("Beachrock"), 4f),
						Outcrops("Beachrock benches", 0.01f, 30f, F(RegionalRocks.Beachrock, "Bench", "Pavement", "Slabs"), 14f)).Tide(-1f, 1f)),
				C(Ground.CliffBlueEarth, 36f), Ground.SandSwashBar, Ground.SandBeachStream)
				// The shallows off the beach: cockles and olive shells, eelgrass drifting in the surge, the warm band's sand dollars.
				.Under(Sea("ShellsCockle", 2f, 0f, 1.5f, 96), Sea("EelgrassDrift", 3f, 0.3f, 3f, 96), Band(Sea("SandDollarStrand", 0.6f, 1f, 4f, 96), 0.45f, 1f))
				// The surf's beds by depth below the water's own surface (decision 35): troughs of megaripples, then offshore sand-silt.
				.BedLayer(Ground.SandTrough, BiomeBedWaters.Sea, 1.5f, 4f, 0f, 0f, Sea("EelgrassDrift", 3f, 0.3f, 4f, 96))
				.BedLayer(Ground.SiltOffshore, BiomeBedWaters.Sea, 4f, 0f, 0f, 0f)
				// The generic dune and shore kit this round retired for its own: dune and tall grass, wrack, driftwood, shells, starfish,
				// pandanus (Mangrove's), the generic palm and morning-glory litter.
				.Moved("main", "DebrisWrack", "DebrisPalm", "Palm", "Driftwood")
				.Moved("detail/0", "GrassTall", "GrassDune", "BeachVine", "Pandanus")
				.Moved("lakebed", "Seagrass", "Shells", "Starfish")
				.Ways(path: Ground.PathDuneTrod, road: Ground.GravelShellRoad, cobbles: Ground.FlagstoneErraticCobbles)
				.Bars(sand: Ground.SandGarnetBar, gravel: Ground.GravelFlintShingle)
				// Own rock beachrock (its benches, pavements and slabs; approved Q2), the pink granite and grey gneiss as erratics only.
				// LATER (I9): Granite's pink rapakivi and Gneiss's grey erratic looks as Beach's own.
				.Accepts(RockTypes.Granite, RockTypes.Gneiss)
				.CaveShell(CaveForm.Cave, "Beachrock").CaveShell(CaveForm.Grotto, "Beachrock").CaveShell(CaveForm.SeaCave, "Beachrock")
				.CaveShell(CaveForm.Mine, "Ground").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Aragonite, 0.6f), (BiomeCaveCrystalKind.Pearlspar, 0.4f))
				// The pearlspar's cold glow pulses, in its caves and on its crystal posts.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				// The foredune along the shore, wandering parabolic dunes (slip face landward) and blowout hollows (approved Q3).
				.Landforms("Foredune", "ParabolicDune", "Blowout")
				.Lines(new Entry.Line("SandFence"))
				.Look(BiomeLookClass.Coastal)
				.Wildlife(BiomeHabitat.Coast)
				// LATER (SEA-LIFE N13/N14 + AMBIENT-LIFE): grey seals hauled out on the bars 0.6 (a new kind); sanderling flocks running
				// with the swash, oystercatchers, tern and gull colonies, cormorants and herons in the dune pines, dune elk at dawn
				// (cool), ghost crabs and turtle tracks (warm), foam-horses at storm dusk. The kit's flatfish stand in as baitfish.
				.HomeTo(("Crab", 2f), ("Baitfish", 2f), ("Jellyfish", 0.8f), ("Jacks", 0.3f))
				// Sea bindweed scrambling over ruins, caves and cliffs; none on trees.
				.GrowsVines(0.3f, 0.5f, 0.05f, 0f, ("SeaBindweed", 1f))
				.NoCrops()
				.Herbs("Detail_SeaKale", "Detail_SeaSandwort", "Detail_FlowersAmberStrand", "Detail_FlowersWarmStrand")
				.Moat(BiomeMoatWater.Wet)
				// The dune lagoons and beach streams: a clear green over tea-brown sand.
				.WaterTint(new BiomeWaterLook(SeaHex("#5a8a72"), SeaHex("#123a3a"), new Vector3(0.42f, 0.10f, 0.08f), 0.3f, SeaHex("#a89a78"), SeaHex("#f2f0e8")))
				// Surf-churned sandy green. LATER (I2k column): the same tint feeds the coastal surface look.
				.SeaTint(new BiomeSeaLook(new Color(0.12f, 0.34f, 0.30f), 9f, 1.3f))
				.Describe("Pale amber strand — singing sand, marram dunes that wander, and a wrack line that glints with amber")
				.Names(onsets: new[] { "Nid", "Juod", "Pree", "Kur", "Gint", "Sam", "Pal", "Rus", "Lab" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ai", "au" },
					codas: new[] { "kranta", "kopa", "keim", "ninken", "gale", "upe" },
					middles: new[] { "al", "ar", "ur", "an" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Ola", "Hole", "Cave", "Grotto", "Undercroft", "Pit", "Hold" },
					dungeonPrefixes: new[] { "The Amber", "The Sea-sung", "The Sand-drowned", "The Wrack-black", "The Wandering", "The Salt-bitten" },
					poiSuffixes: new[] { "Kopa", "Kranta", "Gintaras", "Marios", "Ragas", "Seklius", "Krikstai", "Vetrunge" },
					adjectives: new[] { "Amber", "Sea-sung", "Sand-drowned", "Wrack-black", "Wandering", "Salt-bitten" },
					poiWords: new[]
					{
						("village", new[] { "Kopa", "Nida", "Strand" }),
						("fishingcamp", new[] { "Gintaras", "Amber Fishery", "Seklius" }),
						("lighthouse", new[] { "Fire Beacon", "Ragas" }),
						("graveyard", new[] { "Krikstai", "Grave Boards" }),
						("ruins", new[] { "Buried Village", "Sand-swallowed" }),
						("monument", new[] { "Gallows Dune", "Strand Gallows" }),
						("shrine", new[] { "Cup Stone", "Marios" }),
						("mine", new[] { "Amber Pit", "Gintaras" }),
						("sunkenruins", new[] { "Amber Palace", "Palace Shards" }),
					},
					about: "Baltic: the Curonian and Vistula spits' amber strand (Nid, Juod, Kur, Gint; -kranta, -kopa, -keim, -ninken, -gale, -upe)")
				// The amber-strand folk: villages, fishing camps with their amber fisheries, beacons, grave-board yards, buried villages,
				// the gallows dune, amber pits, cup-stone shrines and (rare) the sea-queen's amber palace in the surf (approved Q6). The
				// rocky coasts' quarries, the woods' lumber camps, bog shrines, stilt villages and witch huts zeroed.
				// LATER (I7): the named pieces — the strand cottage, the weathervane boat (+ sea lane, harbour and Dockyard "Slipway"), the
				// amber fishery, the strand gallows, the grave-board yard, sand fences, the sand-swallowed village, the fire beacon, the
				// upturned-boat hut, flatfish racks, the amber pit, the cup-stone shrine and the amber palace shards. The kit's variants
				// stand in.
				.Sites((POIType.Camp, 0.4f), (POIType.Shrine, 0.4f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f),
					(POIType.Cave, 1f), (POIType.Monument, 0.3f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.NaturalArch, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 0.6f), (POIType.Overhang, 1f), (POIType.BanditCamp, 1f),
					(POIType.FishingCamp, 2f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.StoneCircle, 1f),
					(POIType.Temple, 1f), (POIType.Monastery, 0.6f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f),
					(POIType.Graveyard, 0.6f), (POIType.Barrow, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 0.6f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Village, 1f),
					(POIType.Town, 0.2f), (POIType.City, 0f), (POIType.Capital, 0.6f), (POIType.Port, 0.3f), (POIType.Keep, 1f),
					(POIType.Castle, 0.1f), (POIType.Fortress, 0.6f), (POIType.TradingPost, 0.6f), (POIType.Waystation, 0.6f), (POIType.Mine, 0.4f),
					(POIType.Lighthouse, 0.6f), (POIType.SmugglersCove, 0.6f), (POIType.PirateCove, 0.2f), (POIType.SunkenShip, 0.8f),
					(POIType.SunkenRuins, 0.3f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f), (POIType.Dockyard, 0.5f),
					(POIType.Windmill, 0.2f), (POIType.Watermill, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f),
					(POIType.SignalBeacon, 1f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.Eyrie, 0.8f), (POIType.RuneStone, 0.6f),
					(POIType.TidalIslandAbbey, 1f), (POIType.CliffCastle, 1.2f), (POIType.SmugglersTunnel, 1f), (POIType.WreckersBeacon, 1f),
					(POIType.SirenRock, 1f), (POIType.SeaWitchGrotto, 0.8f), (POIType.LongshipLanding, 1.2f), (POIType.KrakenWreck, 0.8f))
				.Prefers(POIType.FishingCamp, "Shore", 0.5f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				// (the fantasy wave's variants end here)
				.Favours(BiomeStructureStyle.Timber, 2f)
				.Favours(BiomeStructureStyle.Hide, 0.5f)
				.Favours(BiomeStructureStyle.Stone, 0.4f)
				.Favours(BiomeStructureStyle.Earth, 0.2f)
				// The palette: fish-oil ochre plank walls with sea-green gable boards and shutters under weathered grey reed, silver pine
				// shingles, tarred pine net sheds, erratic cobble plinths; strand-reeve towers of galletted erratics with brick quoins;
				// tan-bark sails, amber-and-sea-green banners; sand-clay daub.
				// LATER (NEW-SURFACES U1/U4): StrandBoard, StrandReed, the flint-galletted erratic and the brick quoins as surfaces of their
				// own, the weathervane-board fretwork; tinted kit materials stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#8a4a26"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, SeaHex("#3e7a6e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, SeaHex("#8a8a6a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, SeaHex("#9a948a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, SeaHex("#8a7a72"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, SeaHex("#8a7a72"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, SeaHex("#2a2420"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, SeaHex("#ecebe4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#3e7a6e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, SeaHex("#7a6a64"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#9a4a32"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, SeaHex("#9a948a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, SeaHex("#8a7a72"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, SeaHex("#b8a888"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#2a2420"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#8a4a2a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#d88a20"))
				// Amber lamps on the villages' driftwood posts, hanging lanterns on the net poles, braziers at the harbours and beacons.
				// LATER (I7a): lights at non-town sites are laid once I7a places them; the entries are the template's already.
				.Lights(POIType.Village, BiomeWayLightKind.AmberLamp, 15f)
				.Lights(POIType.Town, BiomeWayLightKind.AmberLamp, 15f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.HangingLantern, 14f)
				.Lights(POIType.Port, BiomeWayLightKind.Brazier, 12f)
				.Lights(POIType.Dockyard, BiomeWayLightKind.Brazier, 12f)
				.Lights(POIType.Lighthouse, BiomeWayLightKind.Brazier, 12f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 10f)
				.Gathers("Strand Amber", "Sea Holly Root", "Marram Bundle", "Drift Timber", "Sea Grape", "Pearlspar")
				// Sandblasted: raised silver grain, paint worn off the edges, sand drifted at the windward foot.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Sandblasted, 0.4f, 0.6f)
				.ErodesAs("Coastal");
		}
	}
}
#endif
