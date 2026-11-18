#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Geyser Basin</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry GeyserBasin()
		{
			// The geothermal highland of the Icelandic interior in the Commonwealth age (biome round, approved 2026-10-18, authored
			// 2026-10-26): a cold, dry fell-field of grey gravel and cream tephra, snow lying late in the lee, and in its hollows the hot
			// ground — white geyserite aprons beaded like coral, sapphire pools, orange-ochre-olive thermophile mats fanning down the
			// runoff, grey mud pots, steaming rose-and-yellow clay hills and spouts that throw boiling water on their own clock; round
			// every vent a ring of startling green where the warm ground keeps a lawn all winter. Free farmers under no king: herringbone
			// turf passage farms, rye bread baked in the steaming ground, a bathing pool fed from the spring, the Thing in summer round
			// its Law Rock, cairned bridle-ways; its fantasy the hidden folk in the great boulders, trolls caught by the dawn, the
			// water-horse of the cold lakes and the sun-spar that shows where the hidden sun is.
			// Reaches (the heat mask of the geothermal sites): HOT CORE heat ≥ 0.65 (bare geyserite), WARM RING 0.35–0.65 (mats, clay),
			// HALO 0.1–0.35 (green lawn, moss), COLD MELUR < 0.1 (fell-field, tephra, late snow). By temperature: COLD t ≤ −0.15, MILD.
			// LATER (I9): every ground and object texture as Geyser Basin's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Hverfire + Huldusheen.
			// LATER (I2q-c): the HotPoolBasin and RunoffChannels landform presets (the cut already digs the pools and traces the runoff),
			// the Thufur field surface, the Sintered coat's per-decay chance; Snag_SinterSock deadwood and the Móberg/Raven-flint variants.
			return E("Geyser Basin", L(Ground.GravelMelurFell,
					// The melur's only grass, a campion, the odd woolly willow in a gully.
					Grass("GrassFellRush", 2.5f, 255, 28f),
					Plants("FlowersHolurt", 1.5f, 160, 28f),
					Plants("LichenCrottle", 0.3f),
					Bush("WoollyWillow", 0.2f, 1f, 25f),
					Plants("DebrisWool", 0.3f, 128),
					Stones(SmallRocks("Rhyolite"), 4f), Stones(Pebbles("Rhyolite"), 6f), Stones(Pebbles("Obsidian"), 1f),
					Boulders(0.03f, 12f, BasaltRocks),
					Outcrops("Rhyolite spines", 0.003f, 80f, F(RegionalRocks.Rhyolite, "Spine"), 40f),
					Blocks("Moberg tafoni", 0.006f, 30f, F(RockTypes.Tuff, "Tafoni"), 35f)),
				Ds(
					// d0: the geyserite apron of the hot core, splash sinter near the geysers.
					D(Ground.SinterGeyseriteApron, 64f, 3f, 0f, 14f, Plants("SinterSpicules", 10f), Plants("DebrisHorse", 0.2f, 128))
						.Heat(0.65f, 1f).Ways(path: Ground.FlagstoneHella),
					// d1: the thermophile mat down the runoff.
					D(Ground.SinterThermophileMat, 48f, 3f, 0f, 8f, Plants("MatHverasly", 8f), Plants("FlowersHverMonkey", 6f, 160, 20f),
						Grass("GrassHotPanic", 2f, 255, 20f)).Heat(0.35f, 0.8f).Ways(path: Ground.FlagstoneHella),
					// d2: solfatara clay, the painted hills' altered rhyolite.
					D(Ground.ClaySolfataraClay, 64f, 3f, 0f, 35f, Grass("GrassHotPanic", 6f, 255, 30f), Plants("SulphurFilm", 3f),
						Plants("AdderTongue", 2f), Blocks("Altered knolls", 0.004f, 50f, F(RegionalRocks.Rhyolite, "AlteredKnoll"), 30f))
						.Heat(0.4f, 0.9f).Ways(path: Ground.FlagstoneHella),
					// d3: the mud-pot flats.
					D(Ground.MudPotFlat, 48f, 2f, 0f, 6f, Plants("MudSpatter", 4f)).Heat(0.3f, 1f),
					// d4: the warm halo, the green lawn and emerald moss; flocks graze it.
					D(Ground.MossWarmHalo, 96f, 2f, 0f, 25f, Grass("GrassThermalBent", 40f, 255, 25f), Plants("MossThermal", 15f),
						Plants("AdderTongue", 1f), Band(Plants("FlowersThermalGentian", 3f, 160, 20f), -0.15f, 1f), Plants("Moonwort", 0.6f),
						Band(Bush("WoollyWillow", 0.2f, 1f, 25f), -1f, 1f), Plants("DebrisWool", 3f, 128), Band(Bush("TeaLeafWillow", 0.2f, 1f, 25f), -0.15f, 1f))
						.Heat(0.1f, 0.4f).Ways(road: Ground.PathReidgataRuts),
					// d5: the rhyolite tephra sheet on the cold melur.
					D(Ground.AshTephraSheet, 140f, 2f, 0f, 20f, Stones(Pebbles("Rhyolite"), 2f), Plants("FlowersHolurt", 1f, 160, 20f)).Heat(0f, 0.1f),
					// d6: the fonn, a late snow patch in the lee (tapering into the rock round it).
					D(Ground.SnowFonnPatch, 64f, 2f, 10f, 30f).Temp(-1f, -0.15f).Heat(0f, 0.05f),
					// d7: painted scree under the rhyolite walls.
					D(Ground.GravelPaintedScree, 64f, 2f, 28f, 45f, Plants("LichenCrottle", 3f), Stones(Pebbles("Rhyolite"), 3f),
						Blocks("Flow-banded blocks", 0.02f, 14f, F(RegionalRocks.Rhyolite, "FlowBanded")), Blocks("Raven-flint", 0.01f, 18f, F(RockTypes.Obsidian, "Chunk"), 45f),
						Boulders(0.1f, 8f, RhyoliteBoulders))),
				C(Ground.CliffRhyolitePainted, 38f), Ground.SinterHotPoolOoze, Ground.GravelWarmStream)
				.AddCliff(C(Ground.CliffRhyolitePainted, 38f))
				// The rules the round retired (grass tufts, sulphur crystals, obsidian chunks and sinter cones scattered at random, the
				// dead trees and the pine stand: the cones and terraces belong to the geothermal sites now).
				.Moved("main", "GrassTuft", "Formations: Tuff", "Formations: Obsidian", "Trees: Dead", "Stand: Pine")
				.Moved("detail/0", "Formations: Sulphur crystals")
				.Moved("detail/2", "Formations: Sinter cones", "Formations: Sinter terraces")
				.Ways(path: Ground.PathPonyTrod, road: Ground.PathReidgataRuts, cobbles: Ground.FlagstoneHella)
				.Bars(sand: Ground.SandBlackSandur, gravel: Ground.GravelRhyoliteObsidian)
				// Own rock rhyolite; approved: accepts the raven-flint obsidian, the erratic basalt and the palagonite tuff; andesite dropped.
				.Accepts(RegionalRocks.Rhyolite, RockTypes.Obsidian, RockTypes.Basalt, RockTypes.Tuff)
				.CaveShell(CaveForm.Cave, "Rhyolite").CaveShell(CaveForm.Grotto, "Basalt").CaveShell(CaveForm.Mine, "Basalt")
				.CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				// Clear spar, peach stilbite sheaves in the vugs, and the sun-spar.
				.Crystals((BiomeCaveCrystalKind.IcelandSpar, 0.5f), (BiomeCaveCrystalKind.Stilbite, 0.2f), (BiomeCaveCrystalKind.Solarsteinn, 0.3f))
				// LATER (glow mode): the sun-spar brightens on the faces turned toward the sun; the sun-height mode stands in.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Sun)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				// The hot ground's landforms (I2g): sinter shields under the geyser clusters (paint the apron), blast craters, painted hills.
				.Landforms(BiomeLandformPresets.Named("SinterShield").Painted(1, BiomeLandformPart.Whole),
					BiomeLandformPresets.Named("ExplosionCrater"),
					BiomeLandformPresets.Named("PaintedHills").Painted(3, BiomeLandformPart.Whole))
				.Look(BiomeLookClass.Cold)
				.Wildlife(BiomeHabitat.Volcanic | BiomeHabitat.Tundra)
				.Moat(BiomeMoatWater.Wet)
				/* Its hot ground (2026-10-18, phase I2q, decision 47; vents I2q-b). Steam is the emission; a quarter geysers, two fifths
				 * springs, a fifth fumaroles and the rest mud pots, on the grid of geothermal sites the cut digs (geyserite cones and
				 * pools, runoff, the heat mask the ground bands above read), erupting steadily on their own pure clock: this basin has no
				 * stored vent lines, so its layout stays the grid and its timing steady, which are the defaults (the Great Spout is the largest geyser dug). Its
				 * pools and streams hot: never iced, always steaming. */
				.Emits("Steam", 0.5f)
				.Geothermal(0.25f, 0.40f, 0.20f, 0.15f)
				.HotWater()
				// The hot pools: sapphire, silica-milky in the shallows.
				.WaterTint(new BiomeWaterLook(UplandHex("#6bc7db"), UplandHex("#083d85"), new Vector3(0.60f, 0.14f, 0.05f), 0.2f, UplandHex("#ddd8cc"), UplandHex("#f6f8f8")))
				.Blades("GrassFellRush", t => UplandBlade(t, "#4a4a2a", "#a8622a", 0.3f, width: 0.8f, stiffness: 1.8f, clump: 0.5f))
				.Blades("GrassThermalBent", t => UplandBlade(t, "#2e5a1e", "#8ac44a", 0.15f, width: 0.8f, clump: 0.2f, heightScale: 0.5f))
				.Blades("GrassHotPanic", t => UplandBlade(t, "#5a6a52", "#9aa88a", 0.25f, width: 1.3f, clump: 0.5f,
					head: GrassBladeHead.Plume, headSize: 0.04f, headShare: 0.4f, heads: new[] { "#8a6a8a" }))
				.Blades("FlowersHverMonkey", t => UplandBlade(t, "#5a9a3a", "#5a9a3a", 0.2f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f,
					heads: new[] { "#f0c820", "#a83a20" }))
				.Blades("FlowersThermalGentian", t => UplandBlade(t, "#4a6a3a", "#4a6a3a", 0.3f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.025f,
					heads: new[] { "#4a48c8" }))
				.Blades("FlowersHolurt", t => UplandBlade(t, "#6a7a7a", "#6a7a7a", 0.35f, heightScale: 0.5f, head: GrassBladeHead.Flower, sprinkle: 5f, headSize: 0.02f,
					heads: new[] { "#f4f2ec", "#b8a0b0" }))
				.Describe("A cold highland of hot ground: white sinter, sapphire pools and spouts that keep their own hours, mud pots and painted clay hills. Turf farms bake their bread in the steaming earth, travellers keep to the cairned bridle-ways, the whole country meets at the Law Rock in summer, and the hidden folk keep their glowing doors in the great boulders.")
				.Names(onsets: new[] { "Hver", "Reyk", "Laug", "Ketil", "Skal", "Hrafn", "Kald", "Brenni", "Sel", "Haukad", "Kjal", "Gufu", "Leir", "Strok" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ey", "au" },
					codas: new[] { "hver", "laug", "reykir", "vellir", "dalur", "holar", "sel", "tunga", "gil", "hraun" },
					middles: new[] { "n", "r", "l", "k" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Hellir", "Gja", "Haugur", "Ketill" },
					dungeonPrefixes: new[] { "The Steaming", "The Hidden", "The Troll-wife's", "The Sinter-crusted", "The Outlaws'" },
					poiSuffixes: new[] { "Hver", "Laug", "Reykir", "Varda", "Sel", "Rett", "Bud", "Gufa", "Leirhver" },
					adjectives: new[] { "Steaming", "Sinter-white", "Turf-roofed", "Hidden", "Sapphire", "Painted", "Bubbling" },
					poiWords: new[]
					{
						("hotspring", new[] { "Hver", "Laug" }),
						("fumarolefield", new[] { "Gufa", "Reykir" }),
						("waystation", new[] { "Sel" }),
						("ancientroad", new[] { "Varda" }),
						("camp", new[] { "Sel", "Rett" }),
						("village", new[] { "Reykir" }),
						("ritualsite", new[] { "Thing", "Bud" }),
						("spring", new[] { "Laug" }),
						("lake", new[] { "Vatn" }),
						("monsterden", new[] { "Hellir" }),
						("peak", new[] { "Fell", "Hnjukur" }),
						("pass", new[] { "Skard" }),
						("gorge", new[] { "Gil" }),
					},
					about: "Icelandic highland voice (Hver, Reyk, Laug, Haukad, Kjal; -hver, -laug, -reykir, -vellir, -dalur): invented names")
				// The hot ground's sites (approved): hot springs and fumaroles above all, refuges and cairned ways, summer camps, a few
				// farm villages; no city, capital, port, castle, tower or mill.
				.Sites((POIType.HotSpring, 2f), (POIType.FumaroleField, 1.6f), (POIType.Waystation, 1.4f), (POIType.AncientRoad, 1.4f),
					(POIType.Camp, 1.2f), (POIType.Landmark, 1.2f), (POIType.LavaTube, 1.2f), (POIType.Spring, 1f), (POIType.RitualSite, 1f),
					(POIType.ObsidianField, 1f), (POIType.AbandonedFarm, 1f), (POIType.Village, 0.8f), (POIType.Barrow, 0.8f), (POIType.Ruins, 0.8f),
					(POIType.Shrine, 0.8f), (POIType.Monument, 0.8f), (POIType.FeyRing, 0.8f), (POIType.Cave, 0.8f), (POIType.Gorge, 0.8f),
					(POIType.Waterfall, 0.8f), (POIType.Pass, 0.8f), (POIType.CrystalFormation, 0.8f), (POIType.MonsterDen, 0.8f), (POIType.BanditCamp, 0.8f),
					(POIType.Temple, 0.6f), (POIType.Hermitage, 0.6f), (POIType.Graveyard, 0.6f), (POIType.Statue, 0.6f), (POIType.Quarry, 0.6f),
					(POIType.Grotto, 0.6f), (POIType.Lake, 0.6f), (POIType.River, 0.6f), (POIType.Nest, 0.6f), (POIType.HerbGrove, 0.6f),
					(POIType.Crater, 0.4f), (POIType.IceCave, 0.4f), (POIType.Mine, 0.4f), (POIType.OreVein, 0.4f), (POIType.TradingPost, 0.4f),
					(POIType.FishingCamp, 0.4f), (POIType.LeyNexus, 0.4f), (POIType.BossLair, 0.4f), (POIType.Town, 0.3f), (POIType.Monastery, 0.3f),
					(POIType.HuntingLodge, 0.3f), (POIType.Volcano, 0.3f), (POIType.Keep, 1f), (POIType.StoneCircle, 0.2f), (POIType.FallenStar, 0.2f),
					(POIType.Glacier, 0.2f), (POIType.Fortress, 0.1f), (POIType.Portal, 0.1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.4f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f), (POIType.Hamlet, 0.3f),
					(POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f), (POIType.SpellScar, 0.8f),
					(POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WyvernNest, 1f),
					(POIType.FireTemple, 1.2f), (POIType.FireGiantHall, 1f), (POIType.SalamanderLair, 1.2f), (POIType.AshBuriedCity, 1f),
					(POIType.Tower, 1f))
				.Prefers(POIType.Village, "Longhouse", 1f)
				.Prefers(POIType.Village, "Stone", 0.8f)
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Waystation, "Stone Inn", 1.2f)
				.Prefers(POIType.Camp, "Crude", 1.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Camp, "Hide Tents", 0f)
				.Prefers(POIType.Ruins, "Burned", 0f)
				.Prefers(POIType.Ruins, "Temple", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1.4f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Hoard", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.4f)
				.Prefers(POIType.FireTemple, "Colonnade", 1.3f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Geyser Basin's signature pieces and variants — the passage farm, longhall, turf church, bathing pool, bread
				// ground, washing springs, Thing, rétt, refuge hut, cairn way, shieling, outlaw hut, boat-grave mound, tephra-buried farm,
				// raven-flint quarry, spar cavity, blessed well, virki, elf rock, dawn troll, spout clock, troll-wife's cave. Until then
				// the kit's nearest variants stand in at those weights, the generic rest at 0 (approved).
				.Favours(BiomeStructureStyle.Earth, 2f)
				.Favours(BiomeStructureStyle.Stone, 1f)
				.Favours(BiomeStructureStyle.Hide, 0.5f)
				.Favours(BiomeStructureStyle.Timber, 0.3f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: herringbone turf walls, driftwood gables, turf roofs at 45 degrees over birch brush, basalt footings with moss
				// joints, homespun. LATER (NEW-SURFACES): KlombruTurf, DriftwoodGable, TorfRoof, BasaltCourse, CarvedDriftDoor,
				// InterlaceRoundel, TurfStoneDyke, Vadmal and TarredStave as Geyser Basin's own surfaces; the shared surfaces tinted stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, UplandHex("#4a5a2e"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Timber, UplandHex("#8a8478"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, UplandHex("#5a6a32"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, UplandHex("#5a6a32"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, UplandHex("#8a8478"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, UplandHex("#7a7468"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, UplandHex("#4a4034"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, UplandHex("#5a6a32"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, UplandHex("#5a6a32"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, UplandHex("#7a6e5e"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, UplandHex("#4a5a2e"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, UplandHex("#4e4c4a"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, UplandHex("#3a3836"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, UplandHex("#4a5a2e"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, UplandHex("#b07a6a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, UplandHex("#8a8478"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, UplandHex("#9a9488"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, UplandHex("#4a3a30"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, UplandHex("#7a6e5e"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, UplandHex("#8a4a2a"))
				// Sintered: a white silica bloom creeping up the lower courses, condensate streaks under the eaves, bleached timber.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Sintered, 0.5f, 0.6f)
				// Homefield hay on the farms, a little warm-bed barley. LATER (I2j-b): the Thufur hummocked field surface.
				.Crops(("Detail_GrassHayMeadow", 1f), ("Detail_CropBarleySixRow", 0.3f))
				.Lines(new Entry.Line("Tungardur"))
				// Iron cruse lamps on the farms, brazier fires at the Thing, glowjars in the elf rocks, a sun-spar post on the cave routes.
				.Lights(POIType.Village, BiomeWayLightKind.IronCruse, 16f)
				.Lights(POIType.Town, BiomeWayLightKind.IronCruse, 16f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 20f)
				.Lights(POIType.FeyRing, BiomeWayLightKind.Glowjar, 14f)
				.Lights(POIType.Landmark, BiomeWayLightKind.Glowjar, 14f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 22f)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.IronCruse)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.IronCruse, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.IronCruse)
				.NoVines()
				.Gathers("Geyserite Nodule", "Hveraslý Mat", "Solfatara Alum", "Painter's Bole", "Raven-flint", "Iceland Spar", "Stilbite Sheaf",
					"Solarsteinn", "Moonwort", "Adder's-tongue", "Crottle Lichen")
				.Herbs("Detail_Moonwort", "Detail_AdderTongue", "Detail_MossThermal", "Detail_FlowersHverMonkey")
				.ErodesAs("Volcanic");
		}

		/// <summary>The rhyolite boulders (I6b stones of the crust rock): round, slab and spire (the Geyser Basin file's).</summary>
		private static string[] RhyoliteBoulders => new[] { Boulder("Rhyolite", "Round"), Boulder("Rhyolite", "Slab"), Boulder("Rhyolite", "Spire") };
	}
}
#endif
