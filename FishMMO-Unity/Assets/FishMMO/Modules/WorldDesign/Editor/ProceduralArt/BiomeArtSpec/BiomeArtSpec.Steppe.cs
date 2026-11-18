#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Steppe</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Steppe()
		{
			// The Kazakh (Sary-Arka) steppe (biome round, approved 2026-10-25, authored 2026-10-26): a flat-to-rolling upland where
			// feather grass runs silver to every horizon, combed by a wind that never stops; drier southward it thins to grey wormwood
			// and fescue, then to pink-autumn saltwort on light chestnut earth, white crusted sors in the hollows, smooth takyr pans that
			// ring under hooves and hummocky kum sands with black-twisted saxaul. Out of the plain rise the sopkas, low worn hills of
			// purple-red porphyry crowned with tent-shaped tors; the dry valleys hold the only trees, small-leaved elms by a spring. Its
			// people move (the Kipchak and Kimek clans, then the White Horde; invented names, no real faces or rites): felt yurts on red
			// lattices, the khan's ordo on great cart-yurts, sod-brick winter quarters in the lee hollows, relay posts on the Horde road,
			// a few brick towns with melon-ribbed tombs alone on the plain, chains of stone-capped mounds along the crests. Its fantasy:
			// the blue-maned wolf, the windfoal, the singing tomb, the twin lakes, the homing wormwood. Bands: TRUE h ≥ −0.35, DRY
			// −0.6…−0.35, DESERT h < −0.6; COLD t < −0.4 (frost steppe, no tulips). Grassland keeps the stone figures and loess.
			// LATER (I9): every ground and object texture as the Steppe's own variant of its recipe (decision 12); the feather grass and
			// tumbleweed keep their catalogue textures, the Steppe their only user (approved Q13).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Skymane V(0.545, 0.46, 0.94) and Ordugold V(0.115, 0.50, 0.64).
			return E("Steppe", L(Ground.GrassDryKovylSward,
					// The kovyl sward: Lessing's feather grass (thinning dryward), tyrsa and tipchak fescue between, the April tulips.
					Band(Grass("GrassFeather", 30f), -1f, 1f, -0.6f, 1f), Grass("GrassTyrsa", 14f), Grass("GrassTipchak", 20f),
					Band(Plants("FlowersTulipSteppe", 10f, 180), -0.4f, 1f, -0.35f, 1f), Band(Plants("FlowersGagea", 3f, 180), -1f, 1f),
					Plants("FlowersPhlomis", 1f, 180), Band(Plants("FlowersGoniolimon", 2f, 180), -1f, 1f, -1f, -0.35f),
					Band(Plants("WormwoodWhiteEarth", 4f), -1f, 1f, -1f, -0.35f),
					// The rolling-field tumbleweeds piled downwind of walls, fences and rut edges.
					Moded(Plants("ShrubTumbleweed", 0.4f, 128), BiomeScatterMode.LeeDrift()),
					// The wandering manna lichen, the black crust between the tussocks, harvester-ant discs, shed awns, the odd skull.
					Plants("LichenManna", 3f), Plants("LichenSoilCrust", 2f), Plants("DebrisHarvesterDisc", 0.3f, 128), Plants("DebrisAwns", 6f, 128),
					Plants("DebrisSaigaSkull", 0.05f, 128), Moded(Plants("DebrisSaigaSkull", 0.4f, 128), BiomeScatterMode.NearSites("Battlefield", 60f)),
					// Wind-polished white vein quartz. LATER (NEW-ROCK-TYPES): a steppe-quartz pebble set of its own; quartzite stands in.
					Stones(Pebbles("Quartzite"), 2f), Stones(SmallRocks("Porphyry"), 1f)),
				Ds(
					// d0: chestnut earth trodden round the camps, wells and folds (≤ 12°): tyrsa, izen, chiy, harvester discs.
					D(Ground.SoilChestnut, 96f, 3f, 0f, 12f, Grass("GrassTyrsa", 6f, 160), Plants("Izen", 2f), Plants("DebrisHarvesterDisc", 0.6f, 128),
						Moded(Grass("GrassChiy", 2f, 140), BiomeScatterMode.NearSites("Camp", 40f))),
					// d1: wormwood steppe (≤ 15°, the dry and desert bands): grey semi-shrub steppe on light chestnut.
					D(Ground.GrassDryWormwoodSteppe, 128f, 2f, 0f, 15f, Plants("WormwoodWhiteEarth", 14f), Band(Plants("Biyurgun", 8f), -1f, 1f, -1f, -0.6f),
						Plants("Izen", 4f), Band(Plants("Nanophyton", 2f), -1f, 1f, -1f, -0.6f), Grass("GrassTipchak", 14f), Grass("GrassFeather", 8f),
						Plants("FlowersGoniolimon", 3f, 180), Band(Plants("FlowersTulipSteppe", 6f, 180), -0.4f, 1f),
						Moded(Plants("ShrubTumbleweed", 1f, 128), BiomeScatterMode.LeeDrift()), Plants("LichenManna", 6f), Plants("LichenSoilCrust", 4f),
						Bush("Atraphaxis", 0.3f)).Humid(-1f, -0.3f),
					// d2: takyrs (≤ 2°; the Playa landform paints its floor): polished clay pans, no road texture on them (the pan is the road),
					// a faint wheel polish.
					D(Ground.ClayTakyr, 140f, 4f, 0f, 2f, Plants("LichenSoilCrust", 3f), Plants("Biyurgun", 2f))
						.Ways(path: Ground.PathTakyrPolish, road: Ground.PathTakyrPolish),
					// d3: sors (≤ 2°, the dry low ground; the SaltPan landform paints them): glasswort-red rims, chiy, the salt tree, red-bed
					// scarps on the rims. LATER (I2l): a fascine causeway of chiy bundles at a sor's neck (the ways skirt it until then).
					D(Ground.SaltCrustSor, 112f, 4f, 0f, 2f, Plants("Biyurgun", 3f), Grass("GrassChiy", 2f, 140), Bush("SaltTree", 0.6f),
						Blocks("Red-bed scarps", 0.003f, 60f, F(RockTypes.Sandstone, "Ledges"))).Humid(-1f, -0.5f),
					// d4: porphyry grit on the sopka shoulders (12–35°; the Sopka landform paints its crown): hedgehog cushions, wormwood,
					// Atraphaxis, the tent tors and knobs, block fields, porphyry and quartz stones and boulders, the rare granite dome.
					D(Ground.GravelPorphyryGrit, 64f, 3f, 12f, 35f, Plants("Nanophyton", 6f), Plants("WormwoodWhiteEarth", 2f), Bush("Atraphaxis", 0.5f),
						Stones(SmallRocks("Porphyry"), 6f), Stones(Pebbles("Porphyry"), 8f), Stones(Pebbles("Quartzite"), 4f),
						Boulders(0.4f, 8f, Boulder("Porphyry", "Round"), Boulder("Porphyry", "Slab"), Boulder("Porphyry", "Spire")),
						Outcrops("Porphyry tents", 0.004f, 60f, F(RegionalRocks.Porphyry, "Tent"), 35f),
						Blocks("Porphyry knobs", 0.01f, 30f, F(RegionalRocks.Porphyry, "Knob")),
						Blocks("Porphyry block fields", 0.006f, 30f, F(RegionalRocks.Porphyry, "BlockField", "Split")),
						Outcrops("Granite domes", 0.0008f, 250f, F(RockTypes.Granite, "Bornhardt"), 35f)),
					// d5: kum sand (the desert band; ≤ 15°): sedge-stitched hummocks, saxaul woods and their dead; a sand track over it.
					D(Ground.SandKum, 160f, 3f, 0f, 15f, Grass("GrassTyrsa", 4f, 140), Plants("Biyurgun", 2f),
						Stand(Was(0.1f, 15f), 15f, W(0.15f, 0.4f, open: 0.3f), "Saxaul"), Logs("Saxaul snags", 0.004f, 30f, Wood("Snag_Saxaul")))
						.Humid(-1f, -0.6f).Ways(path: Ground.PathSandTrackKum, road: Ground.PathSandTrackKum),
					// d6: the bobak mounds' pale spoil (the BobakMounds landform paints their tops): crested wheatgrass on the nitrophile ground.
					D(Ground.SoilBobakMound, 24f, 4f, 0f, 12f, Grass("GrassZhitnyak", 6f, 160)),
					// d7: the logs, dry-valley meadows (≤ 8°; the Wash landform paints their floors): tall kovyl, Phlomis, tulips, the karagach
					// elms in singles and pairs and their fallen boughs. LATER (SCATTER-MODES): the elms at springs and wells (a near-water
					// anchor); the valley floors stand in.
					D(Ground.GrassDryLogMeadow, 64f, 2f, 0f, 8f, Grass("GrassFeather", 10f), Grass("GrassZhitnyak", 6f), Plants("FlowersPhlomis", 3f, 180),
						Band(Plants("FlowersTulipSteppe", 4f, 180), -0.4f, 1f), Plants("DebrisAwns", 4f, 128), Grass("GrassChiy", 2f, 140), Bush("SaltTree", 0.2f),
						Trees(0.002f, 60f, 12f, "SteppeElm"), Logs("Elm boughs", 0.002f, 40f, Wood("FallenLimb_Elm"))),
					// d8: frost steppe (the cold band, t < −0.4): low fescue on frost-cracked chestnut, gold stars in spring, no tulips.
					D(Ground.GrassDryColdSteppe, 96f, 2f, 0f, 20f, Grass("GrassTipchak", 18f), Grass("GrassTyrsa", 10f), Plants("FlowersGagea", 4f, 180))
						.Temp(-1f, -0.4f)),
				C(Ground.CliffPorphyrySopka, 40f), Ground.MudBrackishOoze, Ground.GravelSteppeRiver)
				// The rules the round retired (High Desert's sagebrush, rabbitbrush and juniper, the shared spiny cushion and grey stones,
				// the generic dry grass, tufts and warm flowers), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "GrassDry", "GrassTuft", "FlowersWarm", "Bush: Sagebrush", "Bush: Juniper", "Bush: Rabbitbrush")
				.Moved("detail/1", SmallRocks("Grey"), "CushionSpiny")
				.Ways(path: Ground.PathSheepTrodSteppe, road: Ground.PathHordeRoad, cobbles: Ground.BrickSquareHorde)
				.Bars(sand: Ground.SandGreyRiver, gravel: Ground.GravelPorphyryQuartz)
				// Own rock porphyry (approved; its formations); accepts the Sary-Arka granite (rare domes) and the red-bed sandstone scarps.
				.Accepts(RegionalRocks.Porphyry, RockTypes.Granite, RockTypes.Sandstone)
				// No soluble rock: porphyry caves, the malachite-stained copper pits, a porphyry sea cave.
				.CaveShell(CaveForm.Cave, "Porphyry")
				.CaveShell(CaveForm.Grotto, "Porphyry")
				.CaveShell(CaveForm.Mine, "Porphyry")
				.CaveShell(CaveForm.SeaCave, "Porphyry")
				// Dioptase in the copper-pit walls; the fantasy wolf's-eye, its warm glow rising with the wind.
				.Crystals((BiomeCaveCrystalKind.Dioptase, 0.6f), (BiomeCaveCrystalKind.WolfsEye, 0.35f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Wind, warm: true)
				.Look(BiomeLookClass.Arid)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Scrub)
				// LATER (AMBIENT-LIFE N22): saiga herds migrating north and south with the halves of the shared year, kulan, tarpan, bobak
				// sentries on the mound pads, corsac fox, steppe eagles on the mounds and standards, demoiselle cranes dancing by the sors,
				// sociable lapwing, black lark, steppe wolves; the blue-maned wolf and the windfoal (0.02 each).
				// Approved: dry moats; cold slate-blue brackish water with a white salt rime. LATER (WATER C12): the sors' brine look and
				// the twin lakes (one sweet, one brine) as a per-lake override.
				.Moat(BiomeMoatWater.Dry)
				.WaterTint(new BiomeWaterLook(OpenHex("#6a8088"), OpenHex("#1e2c34"), new Vector3(0.40f, 0.16f, 0.12f), 0.35f, OpenHex("#8a8068"), OpenHex("#f0f0ea")))
				.NoVines()
				// Approved: the sopka hills, the marmots' bobak mounds, oba chains along the crests; the sors, takyrs and dry valleys as the
				// SaltPan, Playa and Wash presets painted with the Steppe's own grounds.
				.Landforms(BiomeLandformPresets.Named("Sopka"), BiomeLandformPresets.Named("BobakMounds"), BiomeLandformPresets.Named("ObaChain"),
					BiomeLandformPresets.Named("SaltPan").Painted(4, BiomeLandformPart.Floor).Capped(2, 3),
					BiomeLandformPresets.Named("Playa").Painted(3, BiomeLandformPart.Floor).Capped(2, 4),
					BiomeLandformPresets.Named("Wash").Painted(8, BiomeLandformPart.Floor).Capped(2, 5))
				// Approved: spring burns now and then. LATER (I2t): the burn on the TRUE band only (h ≥ −0.35); the whole biome's
				// occasional regime stands in.
				.Burns(BiomeBurnRegime.Occasional)
				.Describe("The dry steppe running further than anyone has walked it — silver feather grass and grey wormwood over chestnut earth, white sors, felt yurts on the move, and the stone mounds of older riders")
				// Approved: an invented Kipchak-Turkic voice.
				.Names(onsets: new[] { "Kara", "Ak", "Sary", "Kok", "Ul", "Tor", "Zhel", "Bes", "Ken", "Kum", "Tas", "Shu", "Ait", "Ors", "Yem", "Bal", "Kip", "Tal", "Oz", "Bur" },
					nuclei: new[] { "a", "y", "u", "e", "o", "ai", "ul" },
					codas: new[] { "kum", "sor", "tobe", "kol", "bulak", "sai", "tau", "zhar", "kuduk", "dala", "oba", "shoky" },
					middles: new[] { "an", "ar", "yl", "ek", "ol" },
					dungeonSuffixes: new[] { "Oba", "Tomb", "Copper-pit", "Hollow", "Den", "Warren", "Pit", "Stone Tent" },
					dungeonPrefixes: new[] { "The Windswept", "The Silver", "The Endless", "The Salt-white", "The Wormwood", "The Riders'", "The Trackless", "The Blue-sky" },
					adjectives: new[] { "Windswept", "Silver-grassed", "Salt-white", "Wormwood", "Endless", "Trackless", "Wolf-haunted", "Felt-roofed" },
					poiWords: new[]
					{
						("camp", new[] { "Yurt Ring", "Felt Camp", "Summer Camp", "Herders' Yurts", "Ordo" }),
						("barrow", new[] { "Oba", "Oba Chain", "Stone Tent", "Old Mound" }),
						("landmark", new[] { "White Stone", "Lone Elm", "Rag Elm", "Wolf Ridge", "Quartz Cairn" }),
						("battlefield", new[] { "Field", "Killing Ground", "Horse-field", "Last Ride" }),
					},
					about: "An invented Kipchak-Turkic voice of the Sary-Arka (1000–1450): no real names, faces or rites.")
				.Blades("GrassFeather", t => OpenBlade(t, "#6a6a3e", "#a8a070", 0.3f, clump: 0.7f, bend: 0.55f, head: GrassBladeHead.Plume, headSize: 0.25f,
					headShare: 0.6f, heads: new[] { "#ece8d8" }))
				.Blades("GrassTyrsa", t => OpenBlade(t, "#6a6236", "#b0a060", 0.3f, bend: 0.5f, head: GrassBladeHead.Plume, headSize: 0.15f, headShare: 0.4f,
					heads: new[] { "#c8a860" }))
				.Blades("GrassTipchak", t => OpenBlade(t, "#5a6a5a", "#8a9a88", 0.4f, stiffness: 1.4f, clump: 0.8f, head: GrassBladeHead.SeedHead, headSize: 0.02f,
					headShare: 0.2f, heads: new[] { "#a89c78" }))
				.Blades("GrassChiy", t => OpenBlade(t, "#6a6a46", "#b8b088", 0.2f, stiffness: 1.6f, clump: 0.9f, head: GrassBladeHead.Plume, headSize: 0.35f,
					headShare: 0.35f, heads: new[] { "#a08a9a" }))
				.Blades("GrassZhitnyak", t => OpenBlade(t, "#4e6036", "#8a9a5a", 0.4f, head: GrassBladeHead.SeedHead, headSize: 0.05f, headShare: 0.5f,
					heads: new[] { "#a8a070" }))
				.Blades("FlowersTulipSteppe", t => OpenBlade(t, "#6a8a7a", "#7a9a8a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 18f, headSize: 0.05f,
					heads: new[] { "#c8202a", "#f0c828", "#f4f0e8", "#d86a8a" }))
				.Blades("FlowersPhlomis", t => OpenBlade(t, "#4a5a34", "#5a6a3e", 0.35f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.04f,
					heads: new[] { "#9a5a8a" }))
				.Blades("FlowersGoniolimon", t => OpenBlade(t, "#5a6a5a", "#6a7a6a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.05f,
					heads: new[] { "#e8c8d0", "#f4ecee" }))
				.Blades("FlowersGagea", t => OpenBlade(t, "#6a8a4a", "#7a9a54", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.02f,
					heads: new[] { "#f0d030" }))
				// Approved settlement scale: camp-dominated, the Moving Ordo as the capital (≤ 1 a scene), Horde towns few, no castles.
				.Sites((POIType.Camp, 1.8f), (POIType.Village, 0.6f), (POIType.Town, 0.3f), (POIType.City, 0.08f), (POIType.Capital, 0.03f),
					(POIType.Waystation, 1.4f), (POIType.TradingPost, 0.8f), (POIType.Keep, 1f), (POIType.Fortress, 0.1f), (POIType.Tower, 1f),
					(POIType.Barrow, 1.6f), (POIType.StoneCircle, 0.6f), (POIType.Graveyard, 1f), (POIType.Monument, 0.6f), (POIType.Shrine, 0.4f),
					(POIType.RitualSite, 0.5f), (POIType.Landmark, 1f), (POIType.Battlefield, 1f), (POIType.Ruins, 0.6f), (POIType.RuinedTower, 0.2f),
					(POIType.AncientRoad, 0.5f), (POIType.Lake, 0.8f), (POIType.Spring, 0.6f), (POIType.River, 0.5f), (POIType.Mine, 0.4f),
					(POIType.Quarry, 0.15f), (POIType.OreVein, 0.3f), (POIType.CrystalFormation, 0.2f), (POIType.HuntingLodge, 0.4f), (POIType.Nest, 0.5f),
					(POIType.MonsterDen, 0.4f), (POIType.BanditCamp, 0.6f), (POIType.HerbGrove, 0.5f), (POIType.AncientTree, 0.2f), (POIType.Watermill, 0.1f),
					(POIType.Hermitage, 0.1f), (POIType.FeyRing, 0.1f), (POIType.FallenStar, 0.2f), (POIType.Bridge, 0.2f), (POIType.Cave, 0.2f),
					(POIType.Waterfall, 0.2f), (POIType.Rapids, 0.2f), (POIType.RiverMouth, 0.2f), (POIType.Delta, 0.2f), (POIType.Peak, 0.2f),
					(POIType.Pass, 0.2f), (POIType.Gorge, 0.2f), (POIType.Island, 0.2f), (POIType.Grotto, 0.2f), (POIType.LeyNexus, 0.2f), (POIType.Portal, 0.2f),
					(POIType.Crypt, 0.2f), (POIType.Ossuary, 0.2f), (POIType.Statue, 0.2f), (POIType.Obelisk, 0.2f), (POIType.AbandonedFarm, 0.2f),
					(POIType.DungeonEntrance, 0.2f), (POIType.BossLair, 0.2f), (POIType.HotSpring, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.2f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 1f), (POIType.TitansBones, 1f), (POIType.ElderMonolith, 0.4f),
					(POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.5f), (POIType.LongBarrow, 0.6f), (POIType.Henge, 0.6f), (POIType.KurganField, 1.6f),
					(POIType.NomadCamp, 1.4f), (POIType.StoneFigures, 1.2f), (POIType.CaravanHalt, 0.8f), (POIType.BeastBoneyard, 0.8f),
					(POIType.MammothGraveyard, 0.4f))
				.Prefers(POIType.Village, "Yurts", 0.8f)
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Stone", 0f)
				.Prefers(POIType.Town, "Stone", 1.8f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.City, "Stone", 1.4f)
				.Prefers(POIType.Capital, "Stone", 2f)
				.Prefers(POIType.Waystation, "Inn", 1.6f)
				.Prefers(POIType.Waystation, "Stone Inn", 0f)
				.Prefers(POIType.TradingPost, "Caravanserai", 1f)
				.Prefers(POIType.TradingPost, "Market", 1.4f)
				.Prefers(POIType.Barrow, "Ringed", 2.2f)
				.Prefers(POIType.StoneCircle, "Menhirs", 1.6f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Statue, "Standing", 0f)
				.Prefers(POIType.Camp, "Yurts", 2.2f)
				.Prefers(POIType.Camp, "Crude", 0.2f)
				.Prefers(POIType.Camp, "Hide Tents", 0f)
				.Prefers(POIType.Camp, "Lean-tos", 0f)
				.Prefers(POIType.Village, "Longhouse", 2f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 0f)
				.Prefers(POIType.BanditCamp, "Open", 1.2f)
				.Prefers(POIType.BanditCamp, "Palisade", 0f)
				.Prefers(POIType.Keep, "Stone", 1f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1.3f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Monument, "Obelisk", 0.5f)
				.Prefers(POIType.Monument, "Statue", 0f)
				.Prefers(POIType.Mine, "Abandoned", 1.6f)
				.Prefers(POIType.HuntingLodge, "Lodge", 1.4f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.6f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.DragonLair, "Bone Roost", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.5f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.4f)
				.Prefers(POIType.NomadCamp, "Felt Yurts", 1.4f)
				// (the fantasy wave's variants end here)
				// LATER (I7, I24, I26, I28): the Steppe's signature pieces — the felt yurt on its red lattice, the cart-yurt and the Moving
				// Ordo, winter quarters dug into a lee hollow, the foal line and kumis tripod, the felt-capped well with its horse-drawn rope,
				// the relay post, the horse fair, the Horde town of square-brick courtyards, the khan's melon-ribbed tomb, the corbelled
				// stone tent, oba chains on their pads, the balbal line running east, the eagle camp, the copper pits, the brick fort, the
				// singing tomb (wind-played), the twin lakes and Wolf Ridge (approved); until then the kit's variants below stand in.
				// LATER (I7): the sod-brick winter quarters ("Winter Quarters"); longhouses stand in.
				// LATER (I7): the Horde town and the Moving Ordo; stone towns and a yurt-ringed palisade stand in.
				.Favours(BiomeStructureStyle.Hide, 1.8f)
				.Favours(BiomeStructureStyle.Earth, 1f)
				.Favours(BiomeStructureStyle.Stone, 0.6f)
				.Favours(BiomeStructureStyle.Timber, 0.1f)
				.Favours(BiomeStructureStyle.Iron, 0.1f)
				// The palette: pressed grey-cream felt over a red willow lattice (white-limed for the khan's), patterned felt rugs, chiy
				// screens wound with wool, horse-tail standards; sod-brick winter quarters under flat dung-plastered earth roofs on porphyry
				// rubble; square baked brick in the Horde towns, cobalt and turquoise glazed insets sparing, porphyry plinths. LATER
				// (NEW-SURFACES U2/U3): SteppeFelt, RedLattice, Syrmak, ChiyScreen, TugStandard, SodBrick, SquareBrick and GlazedInset
				// surfaces; the kit's canvas, timber, earth, adobe and fieldstone tinted stand in.
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, OpenHex("#d0c6b0"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, OpenHex("#c8bca4"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, OpenHex("#8a3a24"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, OpenHex("#8a3a24"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Floor, BiomeStructureMaterial.Canvas, OpenHex("#8a2a20"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Fence, BiomeStructureMaterial.Reed, OpenHex("#c8b88a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, OpenHex("#6a5a44"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, OpenHex("#6a3e3a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, OpenHex("#b8ac94"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Fence, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, OpenHex("#b8946a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, OpenHex("#a88a62"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, OpenHex("#a88a62"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, OpenHex("#6a3e3a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Adobe, OpenHex("#b8946a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, OpenHex("#6a3e3a"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, OpenHex("#5a4a36"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, OpenHex("#7a6a4a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, OpenHex("#8a3a24"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, OpenHex("#8a6a48"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, OpenHex("#8a6a3a"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, OpenHex("#d0c6b0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, OpenHex("#1e1a18"))
				// Approved: small ditch-fed plots of safflower and Tartary buckwheat, near rivers only.
				.Crops(("Detail_FlowersSafflower", 0.6f), ("Detail_FlowersTartaryBuckwheat", 0.4f))
				.FieldsNearRivers(300f)
				// Cut-turf banks round the folds and winter quarters.
				.Lines(new Entry.Line("SodWall", BiomeLinearPlace.Folds))
				// Bronze cauldron fires on hoof legs in the camps, villages, relay posts, fairs, shrines and graveyards; hanging lanterns on
				// the Horde towns' street walls; cressets on the forts; wolf's-eye crystal posts along a cave's route; wall torches in the pits.
				.Lights(POIType.Camp, BiomeWayLightKind.KazanFire, 18f)
				.Lights(POIType.Village, BiomeWayLightKind.KazanFire, 18f)
				.Lights(POIType.Waystation, BiomeWayLightKind.KazanFire, 16f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.KazanFire, 16f)
				.Lights(POIType.Shrine, BiomeWayLightKind.KazanFire, 12f)
				.Lights(POIType.Graveyard, BiomeWayLightKind.KazanFire, 12f)
				.Lights(POIType.Town, BiomeWayLightKind.HangingLantern, 13f)
				.Lights(POIType.City, BiomeWayLightKind.HangingLantern, 13f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 22f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 15f)
				.Gathers("White-earth Wormwood", "Homing Wormwood", "Chiy Stems", "Manna Lichen", "Saxaul Wood", "Sor Salt", "Steppe Copper", "Dioptase",
					"Wolf's-eye", "Saiga Horn")
				.Herbs("Detail_WormwoodWhiteEarth", "Detail_FlowersPhlomis", "Detail_Izen")
				// Approved: the new Burandrift weathering (lee dust drifts, a sand-blast belt on windward brick, greyed patched felt,
				// tumbleweeds at the foot) as a coat; no moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Burandrift, 0.4f, 0.7f)
				.ErodesAs("Open");
		}
	}
}
#endif
