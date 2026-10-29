#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Salt Flat</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SaltFlat()
		{
			// The Afar salt plain in the medieval centuries (biome round, approved 2026-10-25, authored 2026-10-26): a furnace-hot basin far
			// below its rim, a dead-flat sheet of hard white salt cracked into metre plates with raised rims and crossed by thrust-up
			// ridges; grey knobbly crust towards the edges, then a khaki clay pan of curled mud plates, then the black lava reg, salted soot
			// sand and a rust-black escarpment. Round the low places brine eyes red as wine and turquoise lakes floored with salt cubes; warm
			// springs building cauliflower terraces; steam from travertine chimneys. Almost nothing grows: bead-glasswort turning crimson,
			// salt-grass, saltwort, desert cotton in the wadis, a lone dobera by a well. Its people cut the crust into amole bars, stack
			// them in hundreds and carry them up the scarp by camel and donkey; herders live in oval mat-dome camps inside thorn fences; the
			// salt-masters' towns are built of salt blocks the night dew flutes like melting sugar; the dead lie under basalt ring-cairns.
			// Its fantasy is its own lore: the salt-wardens turning all day to watch the sun, the giant's bar, the crust ringing like far
			// bells in the afternoon heat, embersalt glowing at dusk, the ghost asses running white on the noon mirage. Salt Flat is also
			// the playa ring round every terminal lake (water role SaltFlat, decision 73), so it is painted on cool worlds too: a COLD band
			// (t < 0.25) wears the alkali playa, the etched crust and the warm-only plants off.
			// LATER (I9 T1): every ground and object texture as Salt Flat's own variant (the Afar basalt, the Abbe travertine, the Asale
			// plates, etched pinnacles and spring rims of the halite, the swallowtail gypsum); until then the grounds are its own recipes.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Amolegold V(0.125, 0.48, 0.58) and Wardensalt V(0.205, 0.30, 0.88).
			return E("Salt Flat", L(Ground.SaltAmoleCrust,
					// The walkable crust relief (the Asale plates) and the trade's litter thickest at the cuts, tolls and halts.
					With(Blocks("Salt polygons", 0.2f, 5f, F(RockTypes.Halite, "Polygons"), 8f), r => r.Spacing = 4.5f),
					Plants("DebrisAmole", 0.2f, 128), Moded(Plants("DebrisAmole", 4f, 128), BiomeScatterMode.NearSites("Quarry,Mine,TradingPost,Waystation", 60f)),
					// The wardenblooms: glassy salt flowers only round the salt-warden pillars, and only under breathable air.
					// LATER (I7 I27): the pillars themselves, turning to the sun's azimuth on the shared clock.
					OnWorlds(Moded(Plants("FlowersWardenbloom", 6f), BiomeScatterMode.NearSites("Statue", 6f)), BiomeAtmosphereRequirement.Standard | BiomeAtmosphereRequirement.Thick),
					// The bedded rock salt: ledges on the salt dome's flanks, fluted pinnacles and scree at the salt cliffs' feet.
					OnLandform(Blocks("Bedded salt", 0.004f, 40f, F(RegionalRocks.Saltrock, "Bedded"), 40f), BiomeLandformPart.Flank, "SaltDome"),
					Moded(Outcrops("Fluted salt", 0.006f, 20f, F(RegionalRocks.Saltrock, "Fluted"), 40f), BiomeScatterMode.CliffFoot(12f)),
					Moded(Blocks("Salt scree", 0.03f, 10f, F(RegionalRocks.Saltrock, "Scree"), 40f), BiomeScatterMode.CliffFoot(10f))),
				Ds(
					// d0: the rain-etched margin crust (and the SaltRidges' net paints it): grey knobs over mud cores, the etched pinnacles, the
					// odd salt diapir; off in the cold band.
					D(Ground.SaltEtchedCrust, 96f, 3f, 0f, 10f, Blocks("Halite pinnacles", 0.03f, 8f, F(RockTypes.Halite, "Pinnacles"), 15f),
						Outcrops("Salt diapirs", 0.001f, 150f, F(RegionalRocks.Saltrock, "Diapir"), 20f)).Temp(0.25f, 1f),
					// d1: the brine-edge ooze: bead-glasswort, the brine sedge, hopper crystals growing on the rims, salt-grass on the dry
					// rims. Ways across it are causeways of cut crust slabs. LATER (SCATTER-MODES / GROUND-BANDS near-water band): within 12 m
					// of the brine and lake water; the layer's own patches stand in.
					D(Ground.MudBrineEdge, 40f, 3f, 0f, 4f, Band(Plants("Halopeplis", 3f), 0.25f, 1f), Grass("GrassBrineSedge", 4f, 180), Plants("DebrisSaltHopper", 6f, 128),
						Grass("GrassOdyssea", 2f, 160), Band(Bush("Salsola", 0.2f), 0.25f, 1f)).Ways(road: Ground.PathCrustCauseway),
					// d2: the khaki clay pan of the outer playa: Odyssea mats, Cressa after rain, saltwort, the swallowtail gypsum, salted dead
					// acacias on the old shorelines, a dobera by a well or camp.
					D(Ground.ClayKhakiPan, 64f, 2f, 0f, 8f, Grass("GrassOdyssea", 5f, 200), PlayaRain(Plants("FlowersCressa", 3f)), Plants("FlowersCressa", 0.3f),
						Band(Bush("Salsola", 0.3f), 0.25f, 1f), Band(Plants("Halopeplis", 0.5f), 0.25f, 1f), PlayaRain(Plants("Aizoon", 1f)),
						Outcrops("Swallowtail gypsum", 0.004f, 16f, F(RockTypes.Gypsum, "Crystal")),
						With(Logs("Salted snags", 0.002f, 40f, Wood("Snag_SaltedAcacia")), r => r.AlignToNormal = false),
						Band(Moded(Trees(0.001f, 80f, 15f, "Dobera"), BiomeScatterMode.NearSites("Spring,Camp,Village,Waystation", 40f)), 0.25f, 1f)),
					// d3: the Afar lava reg of the rim slopes: desert cotton, after-rain aizoon, aloes on the steeper ground, caper bushes, the
					// myrrh's peeling stems, vesicular basalt blocks, scree and boulders. Ways across it are tracks cleared to low windrows.
					D(Ground.RegAfarBasalt, 64f, 2.5f, 6f, 30f, Band(Plants("Aerva", 1f), 0.25f, 1f), PlayaRain(Plants("Aizoon", 1f)),
						With(Plants("HerbAloe", 0.2f), s => s.Slope = new Vector2(10f, 30f)), Band(Bush("Cadaba", 0.1f), 0.25f, 1f),
						With(Band(Bush("Commiphora", 0.05f), 0.25f, 1f), s => s.Slope = new Vector2(8f, 30f)),
						Stones(SmallRocks("Basalt"), 3f), Blocks("Afar basalt", 0.02f, 14f, F(RockTypes.Basalt, "Block")),
						With(Blocks("Basalt scree", 0.02f, 10f, F(RockTypes.Basalt, "Scree")), s => s.Slope = new Vector2(20f, 45f)),
						Boulders(0.01f, 20f, BasaltRocks)).Ways(road: Ground.PathBasaltTrack),
					// d4: black basalt sand salted white in the lee of the reg.
					D(Ground.SandSaltSoot, 128f, 2f, 0f, 25f, Grass("GrassOdyssea", 1f, 170), Band(Plants("Aerva", 0.5f), 0.25f, 1f), Stones(Pebbles("Basalt"), 1f),
						Stones(SmallRocks("Basalt"), 0.5f)),
					// d5: the warm springs' cauliflower crust in the heat band round the hot springs and fumarole fields: spring-rim terraces,
					// the Abbe chimneys venting steam, the brine sedge round the pools.
					D(Ground.SaltSpringCrust, 32f, 3f, 0f, 20f, Blocks("Spring rims", 0.01f, 20f, F(RockTypes.Halite, "Terrace")),
						With(Outcrops("Abbe chimneys", 0.002f, 60f, F(RegionalRocks.Travertine, "AbbeChimney"), 20f), r => r.MaxPerChunk = 3),
						Grass("GrassBrineSedge", 14f, 200)).Heat(0.05f, 1f),
					// d6: the cold band's alkali playa (the terminal-lake rings of cool worlds), in place of the amole crust: bare.
					D(Ground.SaltAlkaliPlaya, 200f, 1.5f, 0f, 8f).Temp(-1f, 0.25f)),
				C(Ground.CliffSaltrock, 38f), Ground.SaltBrineLakeFloor, Ground.GravelWadiBasalt)
				// The generic kit the round retired (the samphire, saltbush and tamarisk taken by other rounds, driftwood, grey stones, the
				// generic sand and cracked-earth layers' rules), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "Detail_Rocks_Grey")
				.Moved("detail/0", "Bush: Saltbush", "ShrubSamphire", "Formations: Gypsum", "Deadwood: Driftwood")
				.Moved("detail/1", "Bush: Tamarisk")
				.Ways(path: Ground.PathCuttersTrace, road: Ground.PathBarRoad, cobbles: Ground.FlagstoneBasaltSetts)
				.Bars(sand: Ground.SandSaltPepper, gravel: Ground.GravelCrustedPebbles)
				// Own rock the bedded rock salt (Saltrock: soluble, but no Cave-interior link); approved: accepts the halite and gypsum
				// formations, the Afar basalt of the escarpment and the Abbe travertine. Granite dropped.
				.Accepts(RegionalRocks.Saltrock, RockTypes.Halite, RockTypes.Gypsum, RockTypes.Basalt, RegionalRocks.Travertine)
				// Salt caves, grottoes and salt galleries keep the host's content (no interior link); lava tubes in the Afar basalt.
				// LATER (I9 S3): the Saltrock dripstone (salt straws, curtains, white-to-amber flowstone) and halite crystal walls.
				.CaveShell(CaveForm.Cave, "Saltrock").CaveShell(CaveForm.Grotto, "Saltrock").CaveShell(CaveForm.Mine, "Saltrock")
				.CaveShell(CaveForm.LavaTube, "Basalt").CaveShell(CaveForm.IceCave, "Ice")
				// Halite hopper cubes, brick-red sylvite, and embersalt glowing warm from dusk, lining the ways it lights.
				.Crystals((BiomeCaveCrystalKind.Halite, 0.5f), (BiomeCaveCrystalKind.Sylvite, 0.3f), (BiomeCaveCrystalKind.Embersalt, 0.2f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.AfterSunset, warm: true)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.AfterSunset, warm: true)
				// Approved: no geysers (Dallol's colours are Sulphur Flats'), warm brine springs and Abbe fumaroles, no mud pots.
				.Geothermal(0f, 0.6f, 0.4f, 0f)
				// The pan filled dead flat, the pressure-ridge megapolygon net, the brine eyes, the salt dome and its dolines.
				.Landforms("SaltPan", "SaltRidges", "BrineEye", "SaltDome", "SaltDomeDolines")
				.Look(BiomeLookClass.Desert)
				.Water(BiomeWaterRole.SaltFlat)
				// Approved: the herders' and salt towns' life and the ruins' beside the desert's and the barren ground's.
				.Wildlife(BiomeHabitat.Desert | BiomeHabitat.Barren | BiomeHabitat.Settlement | BiomeHabitat.Ruins)
				// LATER (AMBIENT-LIFE): African wild ass at dawn on the clay pan, beisa oryx, Soemmerring's gazelle, hamadryas baboon troops on
				// the escarpment, striped hyena by night, Arabian bustard, cream-coloured coursers, brine-fly mats lifting where walked
				// through (client only), goat flocks round the camps; the salt caravans of camels and donkeys carrying paired bars on the bar
				// roads (I18); the ghost asses on the noon mirage (I15, ≤ 1 herd a scene). LATER (SEA-LIFE): Danakil cichlids in the warm
				// springs. LATER (I7 I26): the bell-crust's afternoon pings. LATER (WATER C21): the mirror morning after rain.
				.Moat(BiomeMoatWater.Dry)
				// The brine lakes: turquoise over a white salt floor. LATER (WATER C12): the brine eyes' wine-red per-lake look, and the
				// playa-ringed terminal lakes taking this look.
				.WaterTint(new BiomeWaterLook(ErgHex("#7ad6cc"), ErgHex("#1e7e8e"), new Vector3(0.32f, 0.06f, 0.05f), 0.06f, ErgHex("#f0ece2"), ErgHex("#fbfbf8")))
				// Salt rafts drifting at the brine shores.
				.Floats((BiomeFloatingPlantKind.SaltRaft, 0.15f))
				.Blades("GrassOdyssea", t => UplandBlade(t, "#5e5c48", "#c4bc98", 0.25f, stiffness: 2f, width: 0.5f, clump: 0.6f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.4f, heads: new[] { "#a89670" }))
				.Blades("GrassBrineSedge", t => UplandBlade(t, "#3e4a2a", "#7a9a3a", 0.15f, width: 0.4f, clump: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.015f, headShare: 0.7f, heads: new[] { "#4a3420" }))
				.Blades("FlowersCressa", t => UplandBlade(t, "#6a6a58", "#a8a890", 0.3f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.02f,
					heads: new[] { "#f2dfe4", "#d8a8b8" }))
				.Blades("FlowersWardenbloom", t =>
				{
					UplandBlade(t, "#8a8a7a", "#f8f8f4", 0f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.04f, heads: new[] { "#f8f8f4", "#e8c0c8" });
					t.Rigid = true;
				})
				.Describe("A dead lake of salt in a furnace-hot basin — white polygon crust to the horizon, brine eyes red as wine, salt cut into bars and carried up the scarp by caravans, mat-dome camps on the black lava rim, and pillars of salt that watch the sun")
				.Names(onsets: new[] { "Ab", "Ad", "As", "Ga", "Ha", "Qa", "Dab", "Ger", "Lo", "Ari", "Ka", "Ted", "Waa", "Bur", "Mad" },
					nuclei: new[] { "a", "aa", "e", "i", "oo", "u" },
					codas: new[] { "ale", "ole", "ela", "ado", "aali", "ita", "ru", "bo", "ad" },
					middles: new[] { "dd", "ar", "ol", "aq" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Salt Cave", "Bitter Hollow", "Salt Gallery", "Slab Chamber", "Brine Well" },
					dungeonPrefixes: new[] { "The Bitter", "The Shining", "The Oath-broken", "The Thirsting", "The Ringing", "The Cut" },
					poiSuffixes: new[] { "Pan", "Cut", "Crust", "Eye", "Ring", "Bar" },
					adjectives: new[] { "Bitter", "Glaring", "Brine-red", "Salt-white", "Thirsting", "Ringing" },
					poiWords: new[]
					{
						("camp", new[] { "Ari Camp", "Mat Camp", "Thorn Fold" }),
						("tradingpost", new[] { "Toll Ring", "Bar Market", "Tally Stones" }),
						("waystation", new[] { "Toll Ring", "Well-halt" }),
						("spring", new[] { "Wadi Well", "Brine Eye", "Bitter Well" }),
						("ruins", new[] { "Melted Saltwall", "Dew-ruins" }),
						("graveyard", new[] { "Ring-cairns", "Cairn-field" }),
						("statue", new[] { "Salt-wardens", "Oath Pillars" }),
						("monument", new[] { "Giant's Bar", "First Bar" }),
						("clearing", new[] { "Pan", "Plates", "Crust" }),
						("valley", new[] { "Wadi", "Rim Track" }),
						("cave", new[] { "Salt Cave", "Bitter Hollow" }),
						("crypt", new[] { "Ring-cairn", "Slab Chamber" }),
						("quarry", new[] { "Salt Cut", "Bar Cut" }),
						("town", new[] { "Saltwall" }),
					},
					about: "Afar-flavoured voice (Ab, Ad, Ga, Dab, Ari, Waa; -ale, -ole, -ado, -aali): the salt plain and its bar trade")
				// The salt trade's country: the plain, the cuts, the camps, the toll rings and the ring-cairns; no capital, port, castle,
				// lighthouse, monastery or temple, no coast, wetland, forest, mill or fishing kinds (approved as drafted).
				// LATER (no per-band site column): settlements ×0.3 in the cold band.
				.Sites((POIType.SaltFlat, 2f), (POIType.Quarry, 1.8f), (POIType.Camp, 1.6f), (POIType.TradingPost, 1.2f), (POIType.Mine, 1.2f),
					(POIType.Village, 1f), (POIType.Waystation, 1f), (POIType.Graveyard, 1f), (POIType.Spring, 0.8f), (POIType.HotSpring, 0.8f),
					(POIType.AncientRoad, 0.8f), (POIType.Town, 0.6f), (POIType.Statue, 0.6f), (POIType.Lake, 0.6f), (POIType.Ruins, 0.6f),
					(POIType.Landmark, 0.6f), (POIType.Monument, 0.5f), (POIType.RitualSite, 0.5f), (POIType.BanditCamp, 0.5f), (POIType.Crypt, 0.4f),
					(POIType.FumaroleField, 0.4f), (POIType.Mesa, 0.4f), (POIType.Tower, 1f), (POIType.Shrine, 0.3f), (POIType.DungeonEntrance, 0.4f),
					(POIType.MonsterDen, 0.4f), (POIType.Cave, 0.3f), (POIType.Nest, 0.3f), (POIType.Grotto, 0.2f), (POIType.LavaTube, 0.2f),
					(POIType.Battlefield, 0.2f), (POIType.Fortress, 0.2f), (POIType.Keep, 1f), (POIType.BossLair, 0.2f), (POIType.Overhang, 0.2f),
					(POIType.FallenStar, 0.2f), (POIType.HerbGrove, 0.2f), (POIType.Oasis, 0.2f), (POIType.DuneSea, 0.2f), (POIType.Hermitage, 0.2f),
					(POIType.City, 0.1f), (POIType.Obelisk, 0.1f), (POIType.NaturalArch, 0.1f), (POIType.Portal, 0.1f), (POIType.LeyNexus, 0.1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HangingTree, 0.6f),
					(POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.WreckedCaravan, 1.4f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.PetrifiedBattle, 0.6f), (POIType.TitansBones, 1.2f),
					(POIType.ElderMonolith, 0.8f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 1f), (POIType.KoboldDelving, 0.5f),
					(POIType.LostLibrary, 0.5f), (POIType.IdolVault, 0.4f), (POIType.NomadCamp, 0.6f), (POIType.Necropolis, 1.2f),
					(POIType.RockCutFacade, 0.6f), (POIType.DjinnPrison, 1f), (POIType.CaravanHalt, 1.2f), (POIType.BeastBoneyard, 1.2f),
					(POIType.FireTemple, 0.4f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Stone", 1.2f)
				.Prefers(POIType.Camp, "Yurts", 1.8f)
				.Prefers(POIType.Camp, "Hide Tents", 0.3f)
				.Prefers(POIType.Camp, "Crude", 0.3f)
				.Prefers(POIType.Camp, "Lean-tos", 0f)
				.Prefers(POIType.Village, "Yurts", 1.4f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Town, "Stone", 1.8f)
				.Prefers(POIType.Town, "Palisaded", 0f)
				.Prefers(POIType.City, "Stone", 1f)
				.Prefers(POIType.City, "Timber", 0f)
				.Prefers(POIType.Quarry, "Cut Stone", 2f)
				.Prefers(POIType.Mine, "Adit", 1.6f)
				.Prefers(POIType.Mine, "Shaft Head", 0.6f)
				.Prefers(POIType.TradingPost, "Market", 1.6f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Waystation, "Inn", 1f)
				.Prefers(POIType.Waystation, "Stone Inn", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 1.8f)
				.Prefers(POIType.Graveyard, "Railed", 0f)
				.Prefers(POIType.Crypt, "Tomb", 1.2f)
				.Prefers(POIType.Statue, "Standing", 1.6f)
				.Prefers(POIType.Statue, "Broken", 0.2f)
				.Prefers(POIType.Monument, "Obelisk", 0.4f)
				.Prefers(POIType.RitualSite, "Stones", 1f)
				.Prefers(POIType.RitualSite, "Bonefire", 0f)
				.Prefers(POIType.Ruins, "Stone", 1.2f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Ruins, "Burned", 0f)
				.Prefers(POIType.Ruins, "Temple", 0f)
				.Prefers(POIType.Ruins, "Excavation", 0.2f)
				.Prefers(POIType.AncientRoad, "Colonnade", 0f)
				.Prefers(POIType.Fortress, "Stone", 1f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Keep, "Stone", 1f)
				.Prefers(POIType.DungeonEntrance, "Door", 0.8f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.DragonLair, "Hoard", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.4f)
				.Prefers(POIType.TollGate, "Toll House", 1.2f)
				.Prefers(POIType.Hamlet, "Stone Stead", 1.4f)
				.Prefers(POIType.BurnedVillage, "Cinders", 1.3f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.4f)
				.Prefers(POIType.LostLibrary, "Dry Athenaeum", 1.4f)
				.Prefers(POIType.NomadCamp, "Black Tents", 1.4f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Salt Flat's signature pieces and their variants — the salt cut with its lever poles, trimming floor and bar
				// stacks (Quarry "Salt Cut" 2.0, Mine 0.6), the loading line, the Ari camp of mat domes in a thorn zariba under its dobera
				// (Camp 1.8, Village 1.4; I28 MatDome), the saltwall town of salt blocks (Town 1.6, Village 1.0, City 1.0), the toll ring
				// and its cairn of bars (TradingPost 1.6, Waystation 0.8), the ring-cairns (Graveyard 1.8, Crypt 1.2), the brine-pan works, the
				// brine spring (HotSpring 1.6), the Abbe chimneys (FumaroleField 1.6), the wadi well (Spring 1.2, Waystation 1.0), the
				// escarpment track (AncientRoad 1.2), the melted saltwall (Ruins 1.2), the salt mine (Mine 1.6, DungeonEntrance 0.8), the
				// salt-wardens (Statue 1.6, ≤ 2 fields a scene), the giant's bar (Monument 0.4, ≤ 1) and the brine eye's first bars (RitualSite
				// 1.0). Until then the kit's variants below stand in at the draft's weights (yurts for the mat domes).
				// The escarpment track stands in for its own piece (LATER (I7)): the kit's walled caravan trace with its rest-cairns (2026-10-27).
				.Prefers(POIType.AncientRoad, "Caravan Track", 1.2f)
				.Favours(BiomeStructureStyle.Hide, 2f)
				.Favours(BiomeStructureStyle.Stone, 1.6f)
				.Favours(BiomeStructureStyle.Timber, 0f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: the herders' doum mats striped brown and madder over bent acacia ribs, goat-hide panels, dull iron; the salt
				// towns' rough-hewn salt blocks with brown clay seams under flat pole-and-mat roofs skinned in mud-salt, pale dobera lintels,
				// narrow plank doors, basalt rubble plinths; the ring-cairns' and wells' black vesicular drystone. LATER (NEW-SURFACES U10):
				// the SaltBlock building material (translucent edges under a low sun) — ice block tinted salt-grey stands in; LATER (U3/U8):
				// DoumMat, AcaciaRib, GoatHide, PoleMatRoof, DoberaBeam, AcaciaPlankDoor, AfarDrystone as Salt Flat's own surfaces.
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed, ErgHex("#c8b088"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, ErgHex("#c8b088"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Reed, ErgHex("#b8a078"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, ErgHex("#4e4840"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, ErgHex("#9a7448"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.IceBlock, ErgHex("#d8d2c6"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.IceBlock, ErgHex("#e0dad0"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, ErgHex("#a89a80"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, ErgHex("#a89a80"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, ErgHex("#2a2624"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, ErgHex("#b8a888"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, ErgHex("#8a6a48"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Fieldstone, ErgHex("#3a3632"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, ErgHex("#6a5a48"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, ErgHex("#3a3632"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Adobe, ErgHex("#a89a80"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, ErgHex("#2a2624"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Adobe, ErgHex("#a89a80"), pitch: BiomeRoofPitch.Flat)
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, ErgHex("#2a2624"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, ErgHex("#2a2624"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, ErgHex("#6a5a48"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, ErgHex("#3a3430"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, ErgHex("#9a7448"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, ErgHex("#9a3a2a"))
				// Dewfluted: the salt-block faces fluted by the night dew, corners rounded like melting sugar, polished bands at the doors.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Dewfluted, 0.5f, 1f)
				// LATER (I2j-c J19): the brine-pan works as fields — the SaltPan field surface (bunded pans, brine clear → rose → white, raked
				// cones; 0.6, bottomland by the brine lakes, ≤ 80 × 60 m). No grain and no orchards, so no fields until then.
				.NoCrops()
				// Thorn zaribas round the camps (a dead thorn hedge of caper stands in) and black basalt drystone round the towns, toll rings
				// and graveyards. LATER (I2f-b style): ThornZariba and AfarDrystone as named styles, each on its own sites' bounds.
				.Lines(new Entry.Line(BiomeLinearForm.Dead, BiomeLinearPlace.SiteBounds, "Cadaba"), AfarDrystoneLine())
				// Salt lamps (the wick seen through its salt) in the towns, villages and halts, braziers at the toll rings and forts, embersalt
				// posts at the wardens, the giant's bar and the brine eyes; the camps go unlit (their hearths).
				.Lights(POIType.Town, BiomeWayLightKind.SaltLamp, 14f)
				.Lights(POIType.Village, BiomeWayLightKind.SaltLamp, 18f)
				.Lights(POIType.City, BiomeWayLightKind.SaltLamp, 14f)
				.Lights(POIType.Waystation, BiomeWayLightKind.SaltLamp, 16f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.Brazier, 12f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Statue, BiomeWayLightKind.CrystalPost, 12f)
				.Lights(POIType.Monument, BiomeWayLightKind.CrystalPost, 12f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.CrystalPost, 12f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.SaltLamp, 16f)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.SaltLamp, 10f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.SaltLamp)
				.NoVines()
				.Gathers("Amole Bar", "Brine Salt", "Sylvite", "Hopper Crystal", "Halite Crystal", "Salsola Ash", "Commiphora Resin", "Dobera Seeds",
					"Aerva Wool", "Aloe Sap", "Embersalt")
				.Herbs("Detail_HerbAloe", "Detail_Aizoon")
				.ErodesAs("Arid")
				.Loose("Silicate Sand");
		}

		/// <summary>An after-rain plant of the playa: only inside the rain cells (SCATTER-MODES Z18). LATER (I9b P29): their timing after rain.</summary>
		private static Scatter PlayaRain(Scatter rule) => Moded(rule, BiomeScatterMode.RainCellClusters(300f, 0.35f));

		/// <summary>The Afar drystone wall stand-in: black basalt rubble 1.2 m round the sites.</summary>
		private static Entry.Line AfarDrystoneLine()
		{
			var line = new Entry.Line(BiomeLinearForm.Drystone, BiomeLinearPlace.SiteBounds);
			line.Style.Height = 1.2f;
			line.Style.OwnMaterial = true;
			line.Style.Material = BiomeStructureMaterial.Fieldstone;
			line.Style.Tint = ErgHex("#2a2624");
			return line;
		}
	}
}
#endif
