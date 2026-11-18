#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rocky Terrain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RockyTerrain()
		{
			// The shattered high cold desert of the Pamir (biome round, approved 2026-10-25, authored 2026-10-26), in the register of
			// the Pamir principalities of Wakhan, Shughnan and Badakhshan: dark blue-black hornstone baked by old granites and frost-split
			// into knife-edged combs, needle spires and block fields; white marble bands where lapis lies in blue veins; broad flat pamirs
			// between the ranges, sedge turf ringed with salt round milky streams and still lakes; teresken gravel and silver cushions
			// on the slopes, rock glaciers in the shaded cirques; not a tree in the east, a line of crooked birch and wild rose along
			// each stream in the Wakhan; lamp-lit flat-roofed stone houses on the alluvial fans, led channels watering bearded red
			// wheat; its fantasy the dragon of the lake, the silent-trade stone tower, the night-lamp gem of the ruby mountain.
			// On thin-aired and airless worlds (decisions 41, 44) the frost-shattered rock stays but nothing alive does: the main ground
			// gives way to the rust-lee hornstone (Thin) and the zap-shattered rock (Airless), a rigid mineral wool stands where the
			// grass would, nobody lives there and nothing burns.
			// Reaches: COLD t ≤ −0.2 (the treeless eastern Pamir), MID, WARM t ≥ 0.4 (the Badakhshan sun-slopes).
			// LATER (I9): every ground and object texture as Rocky Terrain's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Dragonlake + Kuhilal.
			// LATER (I9b): the Tien Shan birch stream strip (the kit's birch stands in), the birch drift deadwood, the stone roof roller
			// and horn finials, the dung-cake stacks at the camps; the sorted stripes on the regs.
			return E("Rocky Terrain", L(Ground.RockHornstoneShatter,
					// The pamirs' and slopes' sparse cover, never off the living air.
					OnWorlds(Grass("GrassAlaiFescue", 0.6f, 255, 25f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Plants("FlowersAllardia", 0.6f, 160, 40f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Plants("CushionOxytropis", 1f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Plants("LichenAcarospora", 3f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Plants("LichenLecanora", 1.5f), BiomeAtmosphereRequirement.Living),
					OnWorlds(Bush("Teresken", 0.6f, 1f, 20f), BiomeAtmosphereRequirement.Living),
					Stones(SmallRocks("Hornfels"), 6f), Stones(Pebbles("Hornfels"), 3f),
					Boulders(0.6f, 5f, HornfelsBoulders),
					Blocks("Split blocks", 0.01f, 20f, F(RegionalRocks.Hornfels, "Split", "Block")),
					Blocks("Spires", 0.003f, 50f, F(RegionalRocks.Hornfels, "Pinnacle"), 45f)).Air(BiomeAtmosphereRequirement.Living),
				Ds(
					// d0: splinter grit aprons under the crags.
					D(Ground.GravelSplinterGrit, 48f, 2f, 0f, 32f, OnWorlds(Plants("FlowersAllardia", 1f, 160, 40f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("LichenLecanora", 1f), BiomeAtmosphereRequirement.Living),
						Stones(SmallRocks("Hornfels"), 8f), Stones(Pebbles("Hornfels"), 10f)).Ways(path: Ground.PathPackTrail, road: Ground.GravelCaravanTrack),
					// d1: the pamir floors' sedge turf (the Pamir landform paints it whole).
					D(Ground.GrassDrySazTurf, 64f, 2f, 0f, 5f, OnWorlds(Grass("GrassPamirSedge", 0.9f, 255, 10f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("FlowersStarAster", 0.5f, 160, 20f), BiomeAtmosphereRequirement.Living),
						Plants("DebrisArgaliSkull", 0.02f, 128, 20f)).Zone(BiomeLandformPart.Floor, "Pamir").Air(BiomeAtmosphereRequirement.Living),
					// d2: teresken reg, the cold-desert gravel steppe.
					D(Ground.GravelTereskenReg, 64f, 2f, 0f, 15f, OnWorlds(Bush("Teresken", 1.8f, 1f, 20f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Grass("GrassAlaiFescue", 1.2f, 255, 15f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("CushionOxytropis", 1.5f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("HerbDracocephalum", 0.4f), BiomeAtmosphereRequirement.Living),
						Plants("DebrisArgaliSkull", 0.02f, 128, 20f), Stones(Pebbles("Hornfels"), 4f)).Ways(path: Ground.PathPackTrail),
					// d3: the summit block field, felsenmeer.
					D(Ground.RockSplitBlockField, 80f, 2f, 0f, 12f, OnWorlds(Plants("LichenAcarospora", 2f), BiomeAtmosphereRequirement.Living),
						Blocks("Frost-heaved blocks", 0.02f, 15f, F(RegionalRocks.Hornfels, "Block", "Split")), TalusGraded(Boulders(1.4f, 5f, HornfelsBoulders)),
						Stones(SmallRocks("Hornfels"), 3f)),
					// d4: fan silt, the alluvial fans' fields and villages, a stream strip of birch and wild rose.
					D(Ground.SoilFanSilt, 48f, 2f, 0f, 10f, OnWorlds(Grass("GrassPamirSedge", 0.45f, 255, 10f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("FlowersStarAster", 0.6f, 160, 20f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Plants("HerbDracocephalum", 0.8f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Band(Bush("PamirRose", 0.4f, 1f, 15f), -0.2f, 1f), BiomeAtmosphereRequirement.Living),
						OnWorlds(Band(Trees(0.004f, 12f, 15f, "Birch"), -0.2f, 0.6f), BiomeAtmosphereRequirement.Living),
						Stones(Pebbles("Hornfels"), 3f)).Ways(path: Ground.PathPackTrail, road: Ground.GravelCaravanTrack, cobbles: Ground.FlagstoneFortStreet),
					// d5: the warm band's sun-baked colluvium, almond on the slopes.
					D(Ground.ClaySunBaked, 64f, 2f, 0f, 28f, OnWorlds(Band(Bush("BukharaAlmond", 0.3f, 1f, 25f), 0.4f, 1f), BiomeAtmosphereRequirement.Living),
						Stones(SmallRocks("Hornfels"), 4f)).Temp(0.4f, 1f),
					// d6: the thin-aired worlds' rust-lee hornstone in the main ground's place, with rigid stonewool where the grass would stand.
					D(Ground.RockRustLeeHornstone, 64f, 2f, 0f, 35f, OnWorlds(Grass("GrassStonewool", 2f, 255, 20f), BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin),
						Stones(SmallRocks("Hornfels"), 6f), Boulders(0.6f, 5f, HornfelsBoulders),
						Blocks("Split blocks", 0.01f, 20f, F(RegionalRocks.Hornfels, "Split", "Block"))).Air(BiomeAtmosphereRequirement.Thin),
					// d7: the airless worlds' zap-shattered rock in the main ground's place.
					D(Ground.RockZapShatter, 64f, 2f, 0f, 35f, OnWorlds(Grass("GrassStonewool", 2f, 255, 20f), BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin),
						Stones(SmallRocks("Hornfels"), 6f), Boulders(0.6f, 5f, HornfelsBoulders),
						Blocks("Split blocks", 0.01f, 20f, F(RegionalRocks.Hornfels, "Split", "Block"))).Air(BiomeAtmosphereRequirement.Airless)),
				C(Ground.CliffHornfelsSplinter, 45f), Ground.SiltDragonMarl, Ground.GravelPanjMilk)
				.AddCliff(C(Ground.CliffMarbleLajvard, 50f))
				// The rules the round retired (the generic grey stones, the alpine pine, the desert shrubs and tufts, quartzite).
				.Moved("main", "Boulders", "Detail_Rocks_Grey", "Bush: MountainPine", "Formations: Quartzite", "Trees: Bristlecone", "Trees: Pinyon",
					"CushionSpiny", "GrassTuft", "LichenClump")
				.Moved("detail/0", "Formations: Quartzite scree", "Detail_Pebbles_Grey")
				.Ways(path: Ground.PathPackTrail, road: Ground.GravelCaravanTrack, cobbles: Ground.FlagstoneFortStreet)
				.Bars(sand: Ground.SandMicaPamir, gravel: Ground.GravelBraid)
				// Own rock hornfels; approved: accepts the lapis marble and the pale leucogranite erratics.
				.Accepts(RegionalRocks.Hornfels, RockTypes.Marble, RockTypes.Granite)
				.CaveShell(CaveForm.Cave, "Marble").CaveShell(CaveForm.Grotto, "Hornfels").CaveShell(CaveForm.Mine, "Marble")
				.CaveShell(CaveForm.SeaCave, "Hornfels").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				// Lapis in the marble, the night-lamp gem warm after sunset.
				.Crystals((BiomeCaveCrystalKind.Lajvard, 0.7f), (BiomeCaveCrystalKind.Shabchiragh, 0.25f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.AfterSunset, warm: true)
				// The roof of the world (I2g-c presets): flat pamir floors, serrated combs, the fans at the gorge mouths, rock glaciers in the
				// shaded cirques, a salt pan on a dry pamir floor.
				.Landforms(BiomeLandformPresets.Named("Pamir").Painted(2, BiomeLandformPart.Floor).Capped(0, 2),
					BiomeLandformPresets.Named("ShatterComb").Painted(1, BiomeLandformPart.Whole),
					BiomeLandformPresets.Named("AlluvialFan").Painted(5, BiomeLandformPart.Whole),
					BiomeLandformPresets.Named("RockGlacier").Painted(1, BiomeLandformPart.Whole),
					BiomeLandformPresets.Named("SaltPan").Painted(2, BiomeLandformPart.Floor).Capped(1, 1))
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Barren)
				// LATER (AMBIENT-LIFE): argali herds (moving up at noon on the shared clock), manul at dusk on the block fields, chukar
				// coveys, bar-headed geese over the passes in the spring and autumn halves of the year, a pair of Pamir wolves at dawn, the
				// dragon-horse in the Dragon Lake shallows on moonlit nights, the Pari herders at dawn on their fold. All need air.
				.Moat(BiomeMoatWater.Dry)
				// The glacier-milk streams and the pamir lakes: grey-olive, silty, not Alpine's turquoise.
				.WaterTint(new BiomeWaterLook(UplandHex("#7a8070"), UplandHex("#2a3434"), new Vector3(0.42f, 0.14f, 0.10f), 0.65f, UplandHex("#8a8070"), UplandHex("#eef0ee")))
				// Warm springs, no geysers.
				.Geothermal(0f, 0.8f, 0.2f, 0f)
				.Blades("GrassPamirSedge", t => UplandBlade(t, "#3a3424", "#8a8452", 0.35f, width: 1.2f, clump: 0.5f, stiffness: 1.4f, heightScale: 0.6f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.3f, heads: new[] { "#4a3a24" }))
				.Blades("GrassAlaiFescue", t => UplandBlade(t, "#4a5a5a", "#8aa0a0", 0.3f, width: 1.2f, stiffness: 1.8f, bend: 0.2f, clump: 0.6f,
					head: GrassBladeHead.Plume, headSize: 0.05f, headShare: 0.4f, heads: new[] { "#a8a080" }))
				.Blades("GrassStonewool", t =>
				{
					UplandBlade(t, "#c8ccd0", "#e8ecf0", 0f, width: 0.6f, stiffness: 2f);
					t.Rigid = true;
				})
				.Blades("FlowersAllardia", t => UplandBlade(t, "#c0c4bc", "#c0c4bc", 0.2f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#d8a0c8", "#e0b040" }))
				.Blades("FlowersStarAster", t => UplandBlade(t, "#5a6a4a", "#5a6a4a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.03f,
					heads: new[] { "#a890d0", "#e8c040" }))
				.Describe("The shattered high cold desert of the Pamir: black hornstone combs above broad flat pamirs, teresken and silver cushions on the gravel, lapis in the marble bands, and lamp-lit stone houses on the fan-heads where led channels water bearded red wheat. Not a tree in the east; in the sheltered valleys a line of crooked birch beside each stream. Travellers tell of a dragon in the still lake, a tower where caravans trade in silence, and a ruby mountain whose stones glow after sunset.")
				.Names(onsets: new[] { "Yam", "Qal", "Ish", "Zong", "Shugh", "Ros", "Vrang", "Lang", "Khand", "Pyan", "Dar", "Sar", "Bad", "Zor", "Rang", "Shakh", "Tash", "Kuh", "Ghund", "Bash" },
					nuclei: new[] { "a", "o", "u", "i", "ay", "ar", "ob" },
					codas: new[] { "dara", "kul", "qala", "dasht", "kuh", "band", "bel", "sang", "khona", "ob", "shor", "kand" },
					middles: new[] { "n", "r", "sh", "k" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Qala", "Ghar", "Kan", "Adit", "Gallery", "Cleft", "Shaft", "Cell", "Grotto", "Hollow" },
					dungeonPrefixes: new[] { "The Frost-split", "The Lapis-veined", "The Dragon-haunted", "The Wind-shattered", "The Lamp-lit", "The Salt-ringed" },
					poiSuffixes: new[] { "Dara", "Bel", "Kotal", "Qala", "Oston", "Chid", "Shpun", "Kuh", "Sang", "Pamir", "Dasht", "Band", "Rabat", "Ovring", "Kul" },
					adjectives: new[] { "Frost-split", "Hornstone", "Lamp-lit", "Lapis-veined", "Argali-trodden", "Wind-shattered", "Dragon-haunted", "Salt-ringed" },
					poiWords: new[]
					{
						("peak", new[] { "Kuh", "Sang" }),
						("pass", new[] { "Kotal", "Bel" }),
						("gorge", new[] { "Dara" }),
						("village", new[] { "Chid", "Chid Hamlet" }),
						("keep", new[] { "Qala", "Spur Qala" }),
						("fortress", new[] { "Qala" }),
						("castle", new[] { "Crag Qala" }),
						("shrine", new[] { "Oston" }),
						("camp", new[] { "Shpun" }),
						("lake", new[] { "Kul", "Dragon Kul" }),
						("waystation", new[] { "Rabat" }),
						("watermill", new[] { "Horizontal Mill" }),
						("mine", new[] { "Lajvard Adit" }),
						("landmark", new[] { "Stone Tower", "Pari Fold" }),
						("valley", new[] { "Pamir", "Dasht" }),
					},
					about: "Pamiri mountain voice of the Wakhan and Badakhshan (Qal, Zong, Shugh; -dara, -kul, -qala, -dasht, -kuh): invented names")
				// The roof of the world's sites (approved): peaks, passes and gorges, the pamirs, marble mines and lapis veins, warm springs
				// and still lakes; a few chid hamlets and spur forts where the air is breathable; no port, mill but the horizontal one, or coast.
				.Sites((POIType.Peak, 1.4f), (POIType.Pass, 1.4f), (POIType.Gorge, 1.2f), (POIType.Valley, 1f), (POIType.Overhang, 1f),
					(POIType.Cave, 0.8f), (POIType.Grotto, 0.4f), (POIType.Mine, 1f), (POIType.Quarry, 0.4f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.5f), (POIType.Lake, 0.8f), (POIType.HotSpring, 0.8f), (POIType.Spring, 0.5f), (POIType.River, 0.6f),
					(POIType.Waterfall, 0.3f), (POIType.NaturalArch, 0.3f), (POIType.Landmark, 1.2f), (POIType.Ruins, 0.8f), (POIType.RuinedTower, 0.8f),
					(POIType.AncientRoad, 0.8f), (POIType.Barrow, 0.6f), (POIType.StoneCircle, 0.4f), (POIType.Graveyard, 0.5f), (POIType.Battlefield, 0.2f),
					(POIType.Shrine, 1f), (POIType.Hermitage, 0.6f), (POIType.Village, 0.7f), (POIType.Town, 0.2f), (POIType.City, 0.02f),
					(POIType.Keep, 1f), (POIType.Fortress, 0.4f), (POIType.Castle, 0.2f), (POIType.Tower, 1f), (POIType.Waystation, 0.8f),
					(POIType.TradingPost, 0.4f), (POIType.Watermill, 0.4f), (POIType.Camp, 0.6f), (POIType.BanditCamp, 0.4f), (POIType.HuntingLodge, 0.3f),
					(POIType.Nest, 0.4f), (POIType.MonsterDen, 0.3f), (POIType.FallenStar, 0.2f), (POIType.Portal, 0.05f), (POIType.DungeonEntrance, 0.3f),
					(POIType.HerbGrove, 0.3f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1.2f), (POIType.TrollBridge, 1.2f), (POIType.GiantsSteading, 1f), (POIType.GoblinWarren, 1f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.8f), (POIType.AmbushSite, 0.8f), (POIType.Crossroads, 1f),
					(POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 1.2f), (POIType.TollGate, 0.8f), (POIType.Hamlet, 1f),
					(POIType.SiegeCamp, 0.4f), (POIType.BurnedVillage, 0.6f), (POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 1f), (POIType.SpellScar, 0.4f), (POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.6f),
					(POIType.WishingStone, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.GolemGraveyard, 0.8f),
					(POIType.KoboldDelving, 1.2f), (POIType.TrollStones, 1f), (POIType.WolfDen, 0.8f), (POIType.LongBarrow, 0.8f), (POIType.Henge, 0.5f),
					(POIType.Shieling, 0.8f), (POIType.RockCutFacade, 0.4f), (POIType.CliffPalace, 0.4f), (POIType.DwarvenGate, 1.2f),
					(POIType.MountainKingsHall, 0.8f), (POIType.HangingMonastery, 1f), (POIType.PassFort, 1.2f), (POIType.Eyrie, 1.2f),
					(POIType.GriffinRoost, 1f), (POIType.WyvernNest, 0.8f), (POIType.CliffRelief, 1f), (POIType.SummitCairn, 1f),
					(POIType.SilverMineTown, 1f), (POIType.FrostGiantSteading, 0.5f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Village, "Stone", 2f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.Town, "Stone", 1.4f)
				.Prefers(POIType.City, "Stone", 1f)
				.Prefers(POIType.Fortress, "Stone", 1.4f)
				.Prefers(POIType.Fortress, "Palisade", 0f)
				.Prefers(POIType.Waystation, "Stone Inn", 1.6f)
				.Prefers(POIType.Waystation, "Inn", 0f)
				.Prefers(POIType.Castle, "Highland", 1.6f)
				.Prefers(POIType.Tower, "Stone", 1.4f)
				.Prefers(POIType.Tower, "Observatory", 0f)
				.Prefers(POIType.Tower, "Timber", 0f)
				.Prefers(POIType.Hermitage, "Stone", 1.6f)
				.Prefers(POIType.DungeonEntrance, "Door", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				.Prefers(POIType.Keep, "Dwarven Hold", 1.2f)
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
				.Prefers(POIType.MountainKingsHall, "Kings Before the Gate", 1.2f)
				.Prefers(POIType.SilverMineTown, "Stone Delving", 1.3f)
				.Prefers(POIType.DwarvenGate, "Deep Gate", 1.2f)
				// (the fantasy wave's variants end here)
				// The natural kinds keep their air flag off (the approved draft): Belron 1 and Ithaewyn 1 keep their peaks, combs, caves,
				// landmarks, fallen stars and ore veins.
				.Air(POIType.Village, true).Air(POIType.Town, true).Air(POIType.City, true).Air(POIType.Keep, true)
				.Air(POIType.Fortress, true).Air(POIType.Castle, true).Air(POIType.Tower, true).Air(POIType.Waystation, true)
				.Air(POIType.TradingPost, true).Air(POIType.Watermill, true).Air(POIType.Camp, true).Air(POIType.BanditCamp, true)
				.Air(POIType.HuntingLodge, true).Air(POIType.Hermitage, true).Air(POIType.Shrine, true).Air(POIType.Ruins, true)
				.Air(POIType.RuinedTower, true).Air(POIType.Quarry, true).Air(POIType.Mine, true).Air(POIType.Nest, true)
				.Air(POIType.MonsterDen, true).Air(POIType.AncientRoad, true).Air(POIType.Graveyard, true).Air(POIType.Barrow, true)
				.Air(POIType.StoneCircle, true).Air(POIType.Battlefield, true).Air(POIType.HerbGrove, true)
				.Water(POIType.Spring).Water(POIType.Lake).Water(POIType.HotSpring)
				// LATER (I7): Rocky Terrain's signature pieces and their variants — the chid hamlet with its five-pillared hall and
				// lantern ceiling (Village 2.0), the fort-town, the spur fort (Keep/Fortress), the crag citadel (Castle ≤ 1), the round
				// watch-tower, the pass refuge and the Stone Tower (Waystation/TradingPost), the oston shrine, the exile's cave, the
				// lajvard mine, the horizontal mill, the grotto bath, the Saka rings, the old-faith terraces, the sangar, the shpun, the
				// Dragon Lake and the Pari fold (≤ 1 a scene), the ruby mountain. Until then the kit's nearest variants stand in.
				.Favours(BiomeStructureStyle.Stone, 1.6f)
				.Favours(BiomeStructureStyle.Earth, 0.8f)
				.Favours(BiomeStructureStyle.Timber, 0.1f)
				// The palette: mud-laid hornstone rubble walls, flat earth roofs, hornfels plinths, chip-carved birch trim, fort-street
				// paving, undyed homespun and a single long white pennant. LATER (NEW-SURFACES): Mud-laid hornstone, Pamir earth roof,
				// Chip-carved birch, Wakhi homespun and Alam pennant as Rocky Terrain's own surfaces; the shared ones tinted stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#4a444a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Fieldstone, UplandHex("#4a444a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, UplandHex("#7a6a52"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Adobe, UplandHex("#7a6a52"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, UplandHex("#34323e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, UplandHex("#8a7a64"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, UplandHex("#8a7a64"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, UplandHex("#7a6a52"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, UplandHex("#5a544c"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, UplandHex("#7a6a52"), pitch: BiomeRoofPitch.Flat)
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, UplandHex("#5a544c"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, UplandHex("#66646a"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Stone, UplandHex("#34323e"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, UplandHex("#4a444a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, UplandHex("#8a7a64"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, UplandHex("#8a7a64"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, UplandHex("#3a3632"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, UplandHex("#6a5a4a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, UplandHex("#ece8e0"))
				// Sunfrost: plaster crumbling at the wall foot, pale spall scars, a white salt tide-line, bleached beams; no moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Sunfrost, 0.5f, 0.8f)
				// Red-beard wheat and a grass-pea stand-in on the fan fields behind their clearance rows.
				// LATER (I9b): Detail_CropRedBeardWheat and Detail_CropGrassPea as their own crops.
				.Crops(("Detail_CropBarleySixRow", 0.6f), ("Detail_CropChickpea", 0.4f))
				.Lines(new Entry.Line("ClearanceRow"))
				.Crosses(BiomeCrossingStyle.Cantilever)
				// The lamp-bowls on rubble pillars and in wall niches, small and dim; the lapis mine and the ruby mountain by crystal
				// post. Off the breathable air nothing burns (Glow only, decision 41).
				.Lights(POIType.Village, BiomeWayLightKind.Chiragh, 18f)
				.Lights(POIType.Town, BiomeWayLightKind.Chiragh, 14f)
				.Lights(POIType.City, BiomeWayLightKind.Chiragh, 13f)
				.Lights(POIType.Waystation, BiomeWayLightKind.Chiragh, 16f)
				.Lights(POIType.Keep, BiomeWayLightKind.Chiragh, 14f)
				.Lights(POIType.Fortress, BiomeWayLightKind.Chiragh, 14f)
				.Lights(POIType.Shrine, BiomeWayLightKind.Chiragh, 10f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 22f)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Chiragh)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.Chiragh, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.CrystalPost)
				.NoVines()
				// LATER (I7): Shabchiragh only at the ruby mountain, Allardia and Dragonhead near the herb groves.
				.Gathers("Lajvard", "Shabchiragh", "Teresken", "Pamir Hips", "Dragonhead", "Allardia", "Hornstone")
				.Herbs("Detail_HerbDracocephalum", "Detail_FlowersAllardia")
				.ErodesAs("Mountain");
		}

		/// <summary>The hornfels boulders (I6b stones of the crust rock): round, slab and spire (the Rocky Terrain file's).</summary>
		private static string[] HornfelsBoulders => new[] { Boulder("Hornfels", "Round"), Boulder("Hornfels", "Slab"), Boulder("Hornfels", "Spire") };
	}
}
#endif
