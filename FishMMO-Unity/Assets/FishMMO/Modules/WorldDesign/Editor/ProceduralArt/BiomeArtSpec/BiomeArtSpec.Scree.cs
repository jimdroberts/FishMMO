#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Scree</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Scree()
		{
			// The moving stone of the summits (biome round, approved 2026-10-25, authored 2026-10-26), in the register of the old
			// Diamond Mountains' travel records: pale granodiorite pinnacle ridges shedding blocks into every gully, a talus cone at
			// the angle of rest under each (fine chip at the apex, cart-sized blocks at the toe), stone rivers of lichen-grey blocks
			// down the valley sides, rock glaciers and protalus ramparts in the cold cirques, fell-field nets between, fresh white
			// rockfall scars, parsley fern and clubmoss in the gaps, bellflowers on the shaded talus, stonecrop on the sunned blocks,
			// wind-sheared fir, silver ghost-firs and thousand-year yews; slopes that breathe ice in high summer. Its people live at
			// the cone toes (fire-field farmers in clay-chinked log steadings under bark roofs, stone-cutters, hermits) and on the
			// ridges (fortress walls snaking over the slides, five-fire beacons); its fantasy the walking rocks still creeping toward
			// the mountains that called them, goblin-fire on rainy nights, the goblins' unfinished bridge and the iron-eater's den.
			// On thin-aired and airless worlds (decisions 41, 44) the summits are bare falling talus under a rust or black sky: the
			// main ground gives way to the rust scree (Thin) and the vacuum talus (Airless), nothing grows, nobody lives there.
			// Reaches: COLD t ≤ −0.3, CORE, WARM t ≥ 0.3, DRY h ≤ −0.3, WET h ≥ 0.3.
			// LATER (I9): every ground and object texture as Scree's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Bisaek (kiln celadon) + Jindallae (azalea magenta).
			// LATER (MOVEMENT M2): loose footing on the steep scree (own predicted phase).
			return E("Scree", L(Ground.GravelFineScree,
					// The fines between the talus: ridge reed and rock sedge, never off the living air.
					OnWorlds(Band(Grass("GrassRidgeReed", 0.6f, 255, 28f), -0.3f, 1f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Grass("GrassRockSedge", 0.4f), BiomeAtmosphereRequirement.Living),
					Stones(SmallRocks("Granodiorite"), 4f), Stones(Pebbles("Granodiorite"), 6f),
					// Blocks graded down every cone: fine at the apex, coarse at the toe (I2u size grading).
					TalusGraded(Blocks("Granodiorite blocks", 0.01f, 14f, F(RegionalRocks.Granodiorite, "Block"))),
					TalusGraded(Boulders(0.1f, 8f, GranodioriteBoulders)),
					// The odd rocking stone on a ledge.
					With(Outcrops("Rocking stones", 0.001f, 80f, F(RegionalRocks.Granodiorite, "Rocker"), 35f), s => s.MaxPerChunk = 2),
					Plants("DebrisRockfallScar", 0.3f, 128)).Air(BiomeAtmosphereRequirement.Living),
				Ds(
					// d0: openwork talus at the cone toes and the chutes' runouts: coral lichen, black-dot lichen, parsley fern, bells.
					D(Ground.RockOpenworkTalus, 64f, 3f, 30f, 45f, OnWorlds(Band(Plants("LichenCoral", 2f, 160, 50f), -1f, 1f, -0.3f, 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("LichenBlackdot", 3f, 160, 50f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Band(Plants("FernParsley", 1.5f, 160, 40f), -1f, 0.3f, -0.3f, 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Faced(Band(Plants("FlowersDiamondBell", 0.5f, 160, 50f), -0.4f, 1f), BiomeFacing.Poleward), BiomeAtmosphereRequirement.Living),
						OnWorlds(Faced(Band(Plants("FlowersStonecrop", 0.6f, 160, 50f), -0.3f, 1f), BiomeFacing.Equatorward), BiomeAtmosphereRequirement.Living),
						Plants("DebrisRockfallScar", 2f, 128, 50f), TalusGraded(Blocks("Granodiorite talus", 0.03f, 10f, F(RegionalRocks.Granodiorite, "Talus"), 50f)),
						TalusGraded(Boulders(0.3f, 6f, GranodioriteBoulders)), Stones(SmallRocks("Granodiorite"), 6f)).Ways(path: Ground.PathSteppingBlocks),
					// d1: stone rivers down the valley axes (the ScreeStoneRiver landform paints it whole; elsewhere gentle block fields).
					D(Ground.RockStoneRiver, 96f, 3f, 5f, 20f, OnWorlds(Band(Plants("LichenCoral", 1.5f), -1f, 1f, -0.3f, 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("LichenBlackdot", 3f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Faced(Plants("ClubmossFir", 0.8f), BiomeFacing.Poleward), BiomeAtmosphereRequirement.Living),
						OnWorlds(Band(Plants("FlowersDiamondBell", 0.5f), -0.4f, 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Band(Plants("FlowersStonecrop", 0.6f), -0.3f, 1f), BiomeAtmosphereRequirement.Living),
						Blocks("Stream blocks", 0.04f, 8f, F(RegionalRocks.Granodiorite, "StreamBlocks")), Stones(SmallRocks("Granodiorite"), 3f))
						.Ways(path: Ground.PathSteppingBlocks),
					// d2: the rock glaciers' furrowed skins, only on the cold ScreeRockGlacier landforms.
					D(Ground.GravelRockGlacierSkin, 64f, 3f, 0f, 30f, Stones(SmallRocks("Granodiorite"), 3f)).Temp(-1f, -0.3f)
						.Zone(BiomeLandformPart.Whole, "ScreeRockGlacier"),
					// d3: frost-sorted fell-field nets on the flats of the cold and core reaches, cushions in the fines.
					D(Ground.SoilFellNets, 80f, 2.5f, 0f, 14f, OnWorlds(Plants("CushionNetwort", 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Grass("GrassRockSedge", 0.6f), BiomeAtmosphereRequirement.Living)).Temp(-1f, 0.2f),
					// d4: late snow patches in the nivation hollows and below the ramparts (tapering into the rock round them).
					D(Ground.SnowPatchScree, 48f, 3f, 0f, 35f).Temp(-1f, -0.3f),
					// d5: cone-toe loam of the steadings and fire-fields under the fir stands, azalea on the warm cones.
					D(Ground.SoilConeToe, 80f, 2.5f, 0f, 15f, Band(Bush("Jindallae", 0.3f, 1f, 30f), -0.3f, 1f),
						With(Band(Stand(Was(0.004f, 18f), 30f, W(0.35f, 0.7f, open: 0.05f, scale: 0.75f), "KoreanFir"), -0.3f, 1f), s => s.Height = new Vector2(0f, 0.95f)),
						Band(Trees(0.004f, 40f, 30f, "GhostFir"), -0.3f, 1f), Band(Trees(0.0005f, 80f, 35f, "ThousandYew"), -0.3f, 1f),
						Plants("DebrisFirDrift", 1f, 128), Band(Grass("GrassRidgeReed", 0.6f), -0.3f, 1f),
						Faced(Band(Plants("HerbMountainRoot", 0.05f), -0.3f, 1f), BiomeFacing.Poleward)).Air(BiomeAtmosphereRequirement.Living),
					// d6: breathing talus round the ice caves' mouths (I2u: summer ice and winter steam from the shared clock).
					D(Ground.RockBreathingTalus, 40f, 3f, 20f, 50f, OnWorlds(Plants("ClubmossFir", 0.8f), BiomeAtmosphereRequirement.Living),
						Stones(SmallRocks("Granodiorite"), 6f)).NearSites("IceCave", 60f),
					// d7: wedge-split spoil round the quarries and the fortress walls.
					D(Ground.GravelWedgeSpoil, 30f, 3f, 0f, 30f, Plants("DebrisRockfallScar", 1f, 128), Stones(SmallRocks("Granodiorite"), 4f)).NearSites("Quarry,Fortress", 60f),
					// d8: the thin-aired worlds' rust scree in the main ground's place.
					D(Ground.GravelRustScree, 64f, 2f, 0f, 40f, Stones(SmallRocks("Granodiorite"), 4f), Stones(Pebbles("Granodiorite"), 6f),
						Plants("DebrisRockfallScar", 0.6f, 128), TalusGraded(Blocks("Rust talus", 0.02f, 12f, F(RegionalRocks.Granodiorite, "Talus", "Block"))))
						.Air(BiomeAtmosphereRequirement.Thin),
					// d9: the airless worlds' vacuum talus in the main ground's place.
					D(Ground.RegolithVacuumTalus, 64f, 2f, 0f, 45f, Stones(SmallRocks("Granodiorite"), 5f), Stones(Pebbles("Granodiorite"), 4f),
						Plants("DebrisRockfallScar", 0.6f, 128), TalusGraded(Blocks("Vacuum talus", 0.03f, 10f, F(RegionalRocks.Granodiorite, "Talus", "Block"))))
						.Air(BiomeAtmosphereRequirement.Airless)),
				C(Ground.CliffGranodiorite), Ground.SiltRampartTarn, Ground.GravelStepPool)
				// The rules the round retired (the generic grey stones, slate and quartzite scree, dwarf pine, alpine flowers, moss
				// campion and lichen clumps).
				.Moved("main", "Detail_Rocks_Grey", "Detail_Pebbles_Grey", "Bush: MountainPine", "Formations: Slate scree", "Formations: Quartzite scree",
					"FlowersAlpine", "CushionMossCampion")
				.Moved("detail/0", "LichenClump")
				.Ways(path: Ground.PathTalusTrace, road: Ground.PathFortressCauseway, cobbles: Ground.FlagstoneWedge)
				.Bars(sand: Ground.SandGrus, gravel: Ground.GravelTorrentShingle)
				// Own rock granodiorite; approved: accepts the pink granite pinnacles and the gorges' old gneiss.
				.Accepts(RegionalRocks.Granodiorite, RockTypes.Granite, RockTypes.Gneiss)
				.CaveShell(CaveForm.Cave, "Granodiorite").CaveShell(CaveForm.Grotto, "Granodiorite").CaveShell(CaveForm.Mine, "Granodiorite")
				.CaveShell(CaveForm.SeaCave, "Granodiorite").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Granodiorite")
				// Pale lilac amethyst points in the pockets (the Scree look of amethyst), clear quartz, goblin-fire hopping point to point.
				.Crystals((BiomeCaveCrystalKind.Amethyst, 0.6f), (BiomeCaveCrystalKind.ClearQuartz, 0.2f), (BiomeCaveCrystalKind.Goblinfire, 0.2f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Flicker)
				// The moving stone (I2g-b/c/f presets): cones under the cliffs (their coarse toe rims the openwork talus), the chutes
				// above them (stored lines the falling stone runs down), protalus ramparts of blocks, nivation hollows floored with
				// snow, the pinnacle ridges, the walking rock's tracks, and the re-tuned rock glaciers (cold), stone rivers, mudflow
				// fans (wet) and boulder tracks (airless). LATER (I7): the Walking Rock itself (≤ 1 a scene) and the Ten Thousand Forms
				// landmark on the pinnacle ridge's pad.
				.Landforms(BiomeLandformPresets.Named("ScreeCone").AtDensity(3f).Painted(1, BiomeLandformPart.Rim),
					BiomeLandformPresets.Named("ScreeChutes"),
					BiomeLandformPresets.Named("ProtalusRampart").Painted(1, BiomeLandformPart.Whole),
					BiomeLandformPresets.Named("NivationHollow").Painted(5, BiomeLandformPart.Floor),
					BiomeLandformPresets.Named("PinnacleRidge"),
					BiomeLandformPresets.Named("WalkingRock"),
					BiomeLandformPresets.Named("ScreeRockGlacier"),
					BiomeLandformPresets.Named("ScreeStoneRiver"),
					BiomeLandformPresets.Named("ScreeMudflowFan"),
					BiomeLandformPresets.Named("ScreeBoulderTracks"))
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain)
				// LATER (AMBIENT-LIFE): long-tailed goral bounding across the scree, musk deer at dusk by the fir edges, chipmunks
				// popping from the block gaps, the eagle-owl's dusk hoots; goblin-fire wisps over the talus on rainy nights (×2 wet).
				// All need air. LATER (WEATHER/sound): the falling stone's clatter.
				.Moat(BiomeMoatWater.Dry)
				// The tarns and talus springs: clear and cold.
				.WaterTint(new BiomeWaterLook(UplandHex("#4e7a78"), UplandHex("#0e2228"), new Vector3(0.42f, 0.10f, 0.07f), 0.05f, UplandHex("#6a685e"), UplandHex("#f0f2f2")))
				.Blades("GrassRidgeReed", t => UplandBlade(t, "#4a5a2a", "#a8a060", 0.35f, width: 1.2f, heightScale: 1.4f,
					head: GrassBladeHead.Plume, headSize: 0.06f, headShare: 0.6f, heads: new[] { "#9a7a6a" }))
				.Blades("GrassRockSedge", t => UplandBlade(t, "#3a4a24", "#8a9a5a", 0.45f, width: 1.1f, clump: 0.6f))
				.Blades("FlowersDiamondBell", t => UplandBlade(t, "#4a6a3a", "#5a7a44", 0.2f, bend: 0.7f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#9a8ad8", "#ece8f8" }))
				.Blades("FlowersStonecrop", t => UplandBlade(t, "#4a6a2a", "#8a3a2a", 0.25f, heightScale: 0.5f, head: GrassBladeHead.Flower, sprinkle: 16f, headSize: 0.02f,
					heads: new[] { "#f0c830" }))
				.Describe("Loose stone of the Diamond Mountains: talus cones under twelve thousand granite peaks, stone rivers of lichen-grey blocks, rock glaciers in the cold cirques and slopes that breathe ice in high summer. Bark-roofed log steadings shelter at the cone toes, ridge fortresses snake across the slides, and on rainy nights goblin-fire dances over the scree where the walking rocks still creep towards the mountains that called them.")
				.Names(onsets: new[] { "Geum", "Seol", "Dae", "Cheon", "Ma", "Bi", "Ul", "Myo", "Hwa", "Dol", "Bong", "Sam", "Baek", "Yeong", "Mun", "Ok", "Hyang" },
					nuclei: new[] { "a", "eo", "o", "u", "i", "ae", "ye" },
					codas: new[] { "san", "bong", "bawi", "gol", "ryeong", "seong", "dae", "dol", "ji", "am", "won" },
					middles: new[] { "n", "ng", "l", "m" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Goblin Hollow", "Iron-eater's Den", "Ice Valley", "Talus Vault", "Walking-stone Gap", "Fortress Postern", "Chute Cave" },
					dungeonPrefixes: new[] { "The Walking", "The Goblin-lit", "The Ice-breathing", "The Stone-summoned", "The Wedge-split", "The Thousand-year" },
					poiSuffixes: new[] { "Ridge Fortress", "Five-fire Beacon", "Pass Won", "Ledge Hut", "Wedge Quarry", "Talus Shelter", "Summer-ice Slope",
						"Walking Rock", "Ten Thousand Forms", "Wrestling Stone", "Goblins' Bridge", "Bird-pole Gate", "Bark-roof Steading", "Fire-field" },
					adjectives: new[] { "Walking", "Twelve-thousand-peaked", "Goblin-lit", "Bark-roofed", "Ice-breathing", "Stone-summoned", "Wedge-split",
						"Five-fired", "Thousand-year", "Mist-figured" },
					poiWords: new[]
					{
						("fortress", new[] { "Ridge Fortress", "Sanseong" }),
						("tower", new[] { "Five-fire Beacon", "Beacon Peak" }),
						("waystation", new[] { "Pass Won", "Travellers' Lodge" }),
						("hermitage", new[] { "Ledge Hut", "Hermit's Ledge" }),
						("quarry", new[] { "Wedge Quarry" }),
						("overhang", new[] { "Talus Shelter" }),
						("icecave", new[] { "Summer-ice Slope", "Ice Valley" }),
						("landmark", new[] { "Walking Rock", "Ten Thousand Forms", "Wrestling Stone", "Bird-pole Gate" }),
						("bridge", new[] { "Goblins' Bridge" }),
						("bosslair", new[] { "Iron-eater's Den" }),
						("monsterden", new[] { "Goblin Hollow" }),
						("village", new[] { "Bark-roof Steading", "Cone-toe Hamlet" }),
						("abandonedfarm", new[] { "Fallow Fire-field" }),
						("peak", new[] { "Bong", "San" }),
						("pass", new[] { "Ryeong", "Gap" }),
					},
					about: "Old mountain-kingdom voice of the Diamond Mountains (Geum, Seol, Dae, Cheon; -san, -bong, -bawi, -ryeong, -seong): invented names")
				// The summits' sites (approved): peaks, passes, landmarks and caves; a few hamlets, beacons and ridge fortresses where the
				// air is breathable; no town, port, temple or coastal kind. The lived-in, worked and kept kinds need breathable air; the
				// talus springs (a detected kind, which never needs air) need liquid water.
				.Sites((POIType.Peak, 1.3f), (POIType.Pass, 1f), (POIType.Gorge, 0.8f), (POIType.Landmark, 1.2f), (POIType.Overhang, 0.8f),
					(POIType.Village, 0.4f), (POIType.Fortress, 0.5f), (POIType.Ruins, 0.4f), (POIType.Tower, 1f), (POIType.Waystation, 0.4f),
					(POIType.Hermitage, 0.5f), (POIType.Quarry, 0.6f), (POIType.Camp, 0.4f), (POIType.Cave, 0.6f), (POIType.IceCave, 0.4f),
					(POIType.Bridge, 0.3f), (POIType.Spring, 0.5f), (POIType.Waterfall, 0.3f), (POIType.Lake, 0.2f), (POIType.Glacier, 0.2f),
					(POIType.NaturalArch, 0.3f), (POIType.Nest, 0.4f), (POIType.MonsterDen, 0.3f), (POIType.BossLair, 0.12f),
					(POIType.CrystalFormation, 0.3f), (POIType.OreVein, 0.4f), (POIType.Mine, 0.2f), (POIType.DungeonEntrance, 0.4f),
					(POIType.AncientRoad, 0.3f), (POIType.Graveyard, 0.2f), (POIType.HerbGrove, 0.3f), (POIType.AbandonedFarm, 0.2f), (POIType.FallenStar, 0.1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 1f), (POIType.GoblinWarren, 1f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.SignalBeacon, 1.2f), (POIType.Hamlet, 0.3f), (POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.3f),
					(POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 1f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.PetrifiedBattle, 0.6f), (POIType.WishingStone, 0.6f), (POIType.ElderMonolith, 0.4f),
					(POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f), (POIType.KoboldDelving, 1.2f), (POIType.TrollStones, 1f),
					(POIType.WolfDen, 0.8f), (POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f), (POIType.Shieling, 0.8f), (POIType.RockCutFacade, 0.4f),
					(POIType.CliffPalace, 0.4f), (POIType.DwarvenGate, 1.2f), (POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f),
					(POIType.Eyrie, 1.2f), (POIType.GriffinRoost, 1f), (POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f),
					(POIType.SilverMineTown, 1f), (POIType.FrostGiantSteading, 0.5f), (POIType.Keep, 1f))
				.Prefers(POIType.Village, "Timber", 2f)
				.Prefers(POIType.Village, "Stone", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Fortress, "Stone", 2f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Waystation, "Stone Inn", 2f)
				.Prefers(POIType.Waystation, "Inn", 0f)
				.Prefers(POIType.Hermitage, "Cottage", 2f)
				.Prefers(POIType.Tower, "Stone", 1.6f)
				.Prefers(POIType.Tower, "Timber", 0f)
				.Prefers(POIType.Tower, "Observatory", 0f)
				.Prefers(POIType.Tower, "Wizard", 0f)
				.Prefers(POIType.Hermitage, "Stone", 0f)
				.Prefers(POIType.Quarry, "Cut Stone", 2f)
				.Prefers(POIType.Ruins, "Stone", 1.4f)
				.Prefers(POIType.Ruins, "Burned", 0f)
				.Prefers(POIType.Ruins, "Excavation", 0f)
				.Prefers(POIType.Ruins, "Temple", 0f)
				.Prefers(POIType.Camp, "Crude", 1.4f)
				.Prefers(POIType.Camp, "Lean-tos", 0f)
				.Prefers(POIType.Camp, "Hide Tents", 0f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1.6f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.AbandonedFarm, "Burned", 1.6f)
				.Prefers(POIType.AbandonedFarm, "Farmstead", 0f)
				.Prefers(POIType.MonsterDen, "Lair", 1.4f)
				.Prefers(POIType.MonsterDen, "Bones", 0f)
				.Prefers(POIType.BossLair, "Stones", 1.6f)
				.Prefers(POIType.BossLair, "Warcamp", 0f)
				.Prefers(POIType.Nest, "Bones", 1.2f)
				.Prefers(POIType.Bridge, "Goblins Bridge", 0.8f)
				.Prefers(POIType.Bridge, "Timber", 0.4f)
				.Prefers(POIType.DungeonEntrance, "Door", 0.6f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
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
				.Prefers(POIType.Grange, "Walled Barton", 1.3f)
				.Prefers(POIType.MountainKingsHall, "Delving Hall", 1.2f)
				.Prefers(POIType.DwarvenGate, "Rune Door", 1.3f)
				// (the fantasy wave's variants end here)
				.Air(POIType.Landmark, true).Air(POIType.Village, true).Air(POIType.Fortress, true).Air(POIType.Ruins, true).Air(POIType.Tower, true)
				.Air(POIType.Waystation, true).Air(POIType.Hermitage, true).Air(POIType.Quarry, true).Air(POIType.Camp, true).Air(POIType.Bridge, true)
				.Air(POIType.Nest, true).Air(POIType.MonsterDen, true).Air(POIType.BossLair, true).Air(POIType.Mine, true)
				.Air(POIType.AncientRoad, true).Air(POIType.Graveyard, true).Air(POIType.HerbGrove, true).Air(POIType.AbandonedFarm, true)
				.Water(POIType.Spring)
				// LATER (I7): Scree's signature pieces and their variants — the bark-roof steading with its jar terrace and pine-knot niche
				// lamp (Village "Bark-roof Steading" 2.0), the bird-pole gate (Landmark "Sotdae Gate" 0.8), the ridge fortress on its
				// SanseongWall stretch with chi bastions and a half-moon gate (Fortress "Ridge Fortress" 2.0, ≤ 2), the five-fire beacon
				// lighting one to five pits by the shared clock (Tower "Five-fire Beacon" 1.6), the pass lodge (Waystation "Pass Won"
				// 2.0), the ledge hut (Hermitage "Ledge Hut" 2.0), the wedge quarry (Quarry "Wedge Quarry" 2.0), the talus shelter
				// (Overhang/Cave 1.4), the summer-ice slope with its ice-store hut (IceCave 1.6), the walking rock (Landmark ≤ 1), the
				// ten thousand forms (Landmark ≤ 2), the wrestling stone with the goblin's broom (Landmark 1.0), the iron-eater's den (BossLair
				// ≤ 1), the goblin hollow (MonsterDen 1.4), ridge mounds (Graveyard 1.6), the fallow fire-field (AbandonedFarm 1.6), the
				// stone-cutters' camp (Camp 1.4), the owl crag (Nest 1.2) and the fallen ridge wall (Ruins 1.4). Until then the kit's
				// nearest variants stand in at those weights, the generic rest at 0 (approved).
				.Favours(BiomeStructureStyle.Timber, 1.3f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				.Favours(BiomeStructureStyle.Hide, 0f)
				// The palette: round red-pine logs with thick grey-white clay chinking under thick grey-brown bark-slab roofs held by
				// poles and stones (pitch 30), granodiorite footings, paper-backed lattice doors, wrought iron; the fortresses of dry
				// wedge ashlar with wedge-split flags. LATER (NEW-SURFACES U8/U11): ClayChinkLog, the Gulpi bark roof, GrusStone, Ttisal
				// lattice, Hanji paper, OilPaper floors and WedgeAshlar as Scree's own surfaces; the shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Log, UplandHex("#8a5a3a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Log, UplandHex("#8a5a3a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, UplandHex("#6a5a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, UplandHex("#6a5a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#8a8880"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Plaster, UplandHex("#d8d4c8"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, UplandHex("#a8845a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Timber, UplandHex("#b8904a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, UplandHex("#9a9890"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Stone, UplandHex("#9a9890"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, UplandHex("#6a5a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, UplandHex("#6a5a4a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, UplandHex("#8a8880"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, UplandHex("#7a5a3a"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, UplandHex("#8a8882"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, UplandHex("#8a5a3a"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, UplandHex("#8a8880"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, UplandHex("#9a9890"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, UplandHex("#a8a69c"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, UplandHex("#8a4a2a"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, UplandHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, UplandHex("#f0e8d4"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, UplandHex("#2a3a6a"))
				// Stoneshot: uphill faces pocked by falling stones, spalled corners, a skirt of stones against the uphill wall (approved).
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Stoneshot, 0.65f, 1f)
				// Perilla on the burned fire-field clearings only (no wheat, no orchards).
				.Crops(("Detail_CropPerilla", 1f))
				// The ridge fortresses' wedge-ashlar curtain following the crests.
				.Lines(new Entry.Line("SanseongWall"))
				// The goblins' bridges of unmortared boulders where a span fits (I2l-b).
				.Crosses(BiomeCrossingStyle.GoblinsBridge)
				// Pine-knot niche lamps on the steading walls, pine-knot cressets at the lodges and fortresses, the five-pit beacons, a
				// brazier at the stone-cutters' camp. Off the breathable air nothing burns (Glow only, decision 41: the authoring's rule).
				.Lights(POIType.Village, BiomeWayLightKind.Kokeul, 16f)
				.Lights(POIType.Waystation, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Tower, BiomeWayLightKind.FivePits, 12f)
				.Lights(POIType.Camp, BiomeWayLightKind.Brazier, 10f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 24f)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Cresset)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 17f)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.Kokeul)
				.NoVines()
				// LATER (I7): Goblin's Club only at a Wrestling Stone (≤ 1 node a scene), Mountain Root ≤ 3 nodes, Summer Ice at the
				// Summer-ice Slopes; the catalogue rows stand in wherever the site kinds gather them.
				.Gathers("Mountain Root", "Summer Ice", "Eonyang Amethyst", "Fir Resin", "Goblin's Club")
				.Herbs("Detail_HerbMountainRoot", "Detail_FlowersDiamondBell")
				.ErodesAs("Mountain");
		}

		/// <summary>The granodiorite boulders (I6b stones of the crust rock): round, slab and spire (the Scree file's).</summary>
		/// <remarks>LATER (I6f): the draft's own Block, Sheet and Enclave boulder shapes.</remarks>
		private static string[] GranodioriteBoulders => new[] { Boulder("Granodiorite", "Round"), Boulder("Granodiorite", "Slab"), Boulder("Granodiorite", "Spire") };
	}
}
#endif
