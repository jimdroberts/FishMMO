#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Grassland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Grassland()
		{
			// The Pannonian–Pontic puszta and meadow-steppe (biome round, approved 2026-10-16, authored 2026-10-26): an ocean of tall
			// bronze-and-gold beard-grass and brome over a stoneless black earth, violet sage and yellow inula drifts, crimson steppe
			// peonies on the kurgans, white soda pans glittering round milky alkali lakes, blown-sand puszta of blue fescue, buff loess
			// bluffs cut by sunken lanes and riddled with bee-eater holes; lone wild pears, Turkey-oak islands and white poplars the
			// only trees. Its people are horse-herders and cattle-drovers: whitewashed adobe farmsteads under steep reed roofs, sweep
			// wells against the sky, wayside inns, horse mills, wagon forts, brick churches, cellar rows dug into the loess. Its
			// fantasy is the sky's: the noon mirage, the great golden falcon, horse-skull poles, the stone figures on the mounds.
			// Plains keeps the prairie, Steppe the arid feather-grass and yurts, Woodland the chalk and hedges.
			// LATER (I9): every ground and object texture as Grassland's own variant of its recipe (decision 12); until then the
			// grounds below are its own recipes and the stones, barks and leaves the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Emberleaf (steppe fire: scarlet beard-grass) + Arcane (the mirage's
			// lilac-silver shimmer at the noon heat) (BiomeTemplate.rareLookA/B, decision 25).
			return E("Grassland", L(Ground.GrassPusztaSward,
					// The meadow-steppe sward: brome under goldbeard drifts and red beard-grass, the Pannonian forbs through it.
					Grass("GrassBrome", 30f), Grass("GrassGoldbeard", 6f), Grass("GrassRedBeard", 8f),
					Plants("FlowersPannonia", 8f, 180), Plants("FlowersSpringSteppe", 3f, 180), Plants("FlowersTatar", 3f, 180),
					// The grazed pasture's thistles and eryngo, a mullein spike now and then, field mushrooms in rings.
					Plants("ThistleMusk", 1.5f), Plants("Eryngo", 1f), Plants("Mullein", 0.4f),
					Moded(Plants("MushroomField", 0.4f), BiomeScatterMode.FairyRings(6f, 0.35f)),
					// The litter of a grass sea; suslik colonies gathered into towns.
					Plants("DebrisSteppe", 6f, 128), Moded(Plants("DebrisSuslikSpoil", 0.3f, 128), BiomeScatterMode.ClusteredFields(40f, 0.3f)),
					// Steppe peonies crowding the kunhalom mounds.
					Moded(Plants("FlowersPeony", 2f, 180), BiomeScatterMode.NearSites("Barrow", 30f)),
					// Spring drifts of dwarf almond and the Austrian broom.
					Bush("DwarfAlmond", 0.3f), Bush("SteppeBroom", 0.4f),
					// The lone wild pear, never in stands; the lightning-split one standing dead.
					Trees(0.004f, 90f, 18f, "WildPear"), Logs("Lone pear snags", 0.0008f, 120f, Wood("Snag_Pear")),
					// The forest-steppe's Turkey-oak islands on a low-frequency field (about a twentieth of the biome).
					Stand(Was(0.25f, 10f), 18f, W(0.08f, 0.55f, open: 0.15f, metres: 600f), "TurkeyOak"),
					// Rare basalt witness hills where the geology carries one.
					Outcrops("Basalt witness hills", 0.0004f, 300f, F(RockTypes.Basalt, "Columns"))),
				Ds(
					// d0: goldbeard stands (≤ 18°), tall bronze swathes in noise drifts.
					D(Ground.GrassDryGoldbeard, 80f, 2.5f, 0f, 18f, Grass("GrassGoldbeard", 26f), Grass("GrassBrome", 8f), Plants("FlowersPannonia", 5f, 180),
						Plants("DebrisSteppe", 4f, 128)),
					// d1: chernozem trodden round the wells, herd trails and suslik towns (≤ 10°): thistles, spoil, mushroom rings.
					D(Ground.SoilChernozem, 96f, 3f, 0f, 10f, Grass("GrassBrome", 6f, 160), Plants("ThistleMusk", 3f),
						Moded(Plants("DebrisSuslikSpoil", 0.6f, 128), BiomeScatterMode.ClusteredFields(40f, 0.4f)),
						Moded(Plants("MushroomField", 0.2f), BiomeScatterMode.FairyRings(6f, 0.35f))),
					// d2: the Kiskunság blown-sand puszta (≤ 14°): sand fescue tussocks with bare sand between, eryngo, mullein, thyme,
					// broom; white-poplar groves on it. A sand track over it in place of the black-earth road (approved: decision 19).
					D(Ground.SandPuszta, 128f, 3f, 0f, 14f, Grass("GrassBlueFescue", 18f, 200), Grass("GrassRedBeard", 4f), Plants("FlowersSpringSteppe", 3f, 180),
						Plants("Eryngo", 2f), Plants("Mullein", 0.6f), Plants("ThymeMat", 3f), Plants("ArtemisiaSalt", 1f), Plants("DebrisSteppe", 2f, 128),
						Bush("SteppeBroom", 0.6f), Bush("Spiraea", 0.3f),
						Stand(Was(0.3f, 8f), 14f, W(0.12f, 0.5f, open: 0.15f), "WhitePoplar"))
						.Ways(path: Ground.PathSandTrackPuszta, road: Ground.PathSandTrackPuszta),
					// d3: szik soda pans in the hollows (≤ 3°, low ground): alkali grass, statice and sea wormwood round them, the cut
					// thatching reed and the ditches' white willows at the lake fringe. LATER (GROUND-BANDS): a ring band at the pan's
					// edge (the crust itself bare); thin on the whole layer until then. LATER (SCATTER-MODES): a near-water anchor for the
					// reed (within 15 m of a lake or soda pond) and the willow (12 m); the hollows' pans stand in.
					With(D(Ground.SaltCrustSzik, 112f, 3.5f, 0f, 3f, Grass("GrassAlkali", 8f, 160), Plants("FlowersSzik", 5f, 180), Plants("ArtemisiaSalt", 4f),
						Plants("ReedsThatch", 3f), Trees(0.004f, 12f, 8f, "WhiteWillow")), l => l.Height = new Vector2(0f, 0.5f)),
					// d4: loess banks on the 22–48° slopes: red beard-grass, spring flowers, peonies, Tatar forbs, thyme, the forest-steppe
					// scrub and the loess dolls, the plain's only stones.
					D(Ground.LoessBank, 40f, 2f, 22f, 48f, Grass("GrassRedBeard", 6f), Plants("FlowersPannonia", 4f, 180), Plants("FlowersSpringSteppe", 6f, 180),
						Plants("FlowersPeony", 1.5f, 180), Plants("FlowersTatar", 4f, 180), Plants("Mullein", 0.8f), Plants("ThymeMat", 4f),
						Bush("DwarfAlmond", 1.2f), Bush("SteppeCherry", 1f), Bush("Spiraea", 0.8f), Bush("TatarianMaple", 0.3f),
						Stones(SmallRocks("Loess"), 1.5f), Stones(Pebbles("Loess"), 1f)),
					// d5: the bare loess walls (≥ 48°): bluffs of vertical prisms, earth pillars at their feet, loess dolls in the spill.
					D(Ground.LoessWall, 24f, 4f, 48f, 90f, Blocks("Loess bluffs", 0.006f, 30f, F(RegionalRocks.Loess, "Bluff"), 70f),
						Blocks("Loess pillars", 0.003f, 40f, F(RegionalRocks.Loess, "Pillar"), 70f), Stones(SmallRocks("Loess"), 1.5f))),
				C(Ground.CliffLoessBluff, 45f), Ground.MudSodaOoze, Ground.SiltTisza)
				// The rules the round retired (Woodland's oak, hazel, hawthorn, bramble and bracken, the Hills' gorse, the generic meadow
				// grass and flowers and grey boulders), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "GrassLush", "Trees: Oak", "Bush: Bramble", "Bush: Gorse", "Bush: Hazel", "Bush: Hawthorn")
				.Moved("detail/0", "FlowersMeadow", "GrassLush", "FlowersTall")
				.Moved("detail/1", "Boulders", "FernBracken")
				.Ways(path: Ground.PathHerdTrail, road: Ground.PathBlackEarthTrack, cobbles: Ground.BrickHerringbone)
				.Bars(sand: Ground.SandMicaRiver, gravel: Ground.GravelConcretionGrit)
				// Own rock loess (approved: a new unconsolidated rock); accepts the Sarmatian limestone, the clay vales' shale and the
				// rare basalt buttes. Granite, gneiss and schist dropped (upland rocks).
				.Accepts(RegionalRocks.Loess, RockTypes.Limestone, RockTypes.Shale, RockTypes.Basalt)
				// Loess caves: the cellar rows and cave dwellings cut in it, timber-propped clay workings.
				.CaveShell(CaveForm.Cave, "Loess")
				.CaveShell(CaveForm.Grotto, "Loess")
				.CaveShell(CaveForm.Mine, "Loess")
				// Calcite dog-tooth in the loess-doll geodes and the fantasy skyglass, the sky's tears, chiming in the wind.
				.Crystals((BiomeCaveCrystalKind.Dogtooth, 0.6f), (BiomeCaveCrystalKind.Skyglass, 0.4f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Wind)
				// Approved: Arid (straw-gold summer; shared class with Steppe, colours each biome's own species).
				.Look(BiomeLookClass.Arid)
				// Approved: the soda lakes' and ditches' storks, avocets, frogs and dragonflies.
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): great bustard, white stork on the chimney and pole nests, suslik sentries at the spoil, bee-eaters
				// at the loess walls, skylark, roller and hoopoe, saker falcon, quail; the grey longhorn and racka herds; the Turul at dawn.
				// Approved: wet moats, milky soda water (turbid jade-grey, white-crusted margins) in its lakes, ponds and moats.
				.Moat(BiomeMoatWater.Wet)
				.WaterTint(new BiomeWaterLook(OpenHex("#9aa898"), OpenHex("#4a5a54"), new Vector3(0.35f, 0.18f, 0.16f), 0.45f, OpenHex("#c8c8b8"), OpenHex("#f4f4ee")))
				// Wild grape on the poplars, ruins and cellar fronts; bindweed on the fences and walls. Loess sheds vines.
				.GrowsVines(0.15f, 0.45f, 0.08f, 0.05f, ("WildGrape", 0.65f), ("BindweedField", 0.35f))
				// The kunhalom mounds (approved: Grassland's, with the stone figures and carved grave posts).
				.Landforms("Kunhalom")
				.Describe("Meadow-steppe on black earth — tall gold grass, soda pans, loess bluffs, sweep wells against the sky")
				// Approved: puszta-fitting names in place of the English downland's (an invented Pannonian voice).
				.Names(onsets: new[] { "Ke", "Sza", "Tor", "Ba", "Hor", "Csa", "Ki", "Ne", "Pu", "Ma", "Go", "Ar", "Bel", "Ku", "Ho", "Sar", "Ege", "Deb" },
					nuclei: new[] { "a", "e", "o", "i", "u", "ai", "ei" },
					codas: new[] { "halom", "tanya", "szeg", "lak", "telek", "hat", "kut", "ret", "gat", "vasar", "er", "laposa" },
					middles: new[] { "ar", "en", "ol", "as", "ur" },
					dungeonSuffixes: new[] { "Cellar", "Burrow", "Mound", "Warren", "Loess Cave", "Pit" },
					poiSuffixes: new[] { "Halom", "Tanya", "Csarda", "Kut", "Szallas", "Gat", "Ret", "Lapos" },
					adjectives: new[] { "Wind-combed", "Gold-bearded", "Soda-white", "Black-earth", "Mirage-haunted", "Stork-nested", "Sun-gold", "Endless" },
					poiWords: new[]
					{
						("barrow", new[] { "Kunhalom", "Halom", "Mound", "Old Mound" }), ("village", new[] { "Tanya", "Tanyas", "Farmsteads" }),
						("waystation", new[] { "Csarda", "Wayside Inn" }), ("camp", new[] { "Herders' Huts", "Reed Huts", "Drovers' Camp" }),
						("landmark", new[] { "Lone Pear", "Sweep Well", "White Pan" }), ("statue", new[] { "Stone Figure", "Watcher Stone" }),
					},
					about: "A Pannonian horse-herders' voice of the puszta (Magyar and Cuman register): invented names only.")
				.Blades("GrassBrome", t => OpenBlade(t, "#4a6a2a", "#8aa048", 0.45f, stiffness: 0.5f, clump: 0.3f, head: GrassBladeHead.Plume, headSize: 0.06f,
					headShare: 0.25f, heads: new[] { "#8a7a4a" }))
				.Blades("GrassGoldbeard", t => OpenBlade(t, "#5a6a3a", "#a89a5a", 0.3f, clump: 0.7f, bend: 0.55f, head: GrassBladeHead.Plume, headSize: 0.12f,
					headShare: 0.4f, heads: new[] { "#7a4a5a", "#c8a050" }))
				.Blades("GrassRedBeard", t => OpenBlade(t, "#6a6a3a", "#a0603a", 0.4f, head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.3f,
					heads: new[] { "#8a3a3a" }))
				.Blades("GrassBlueFescue", t => OpenBlade(t, "#6a8a8a", "#a8b8b0", 0.2f, stiffness: 1.4f, clump: 0.9f, head: GrassBladeHead.SeedHead, headSize: 0.03f,
					headShare: 0.15f, heads: new[] { "#c8b88a" }))
				.Blades("GrassAlkali", t => OpenBlade(t, "#6a7a5a", "#9aa480", 0.35f, clump: 0.4f, head: GrassBladeHead.Plume, headSize: 0.04f, headShare: 0.3f,
					heads: new[] { "#8a7a8a" }))
				.Blades("ReedsThatch", t => OpenBlade(t, "#c8b078", "#e0cc98", 0.25f, stiffness: 1.6f, head: GrassBladeHead.Plume, headSize: 0.2f, headShare: 0.6f,
					heads: new[] { "#9a8a8a" }))
				.Blades("FlowersPannonia", t => OpenBlade(t, "#4a6a34", "#5a7a3a", 0.4f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.03f,
					heads: new[] { "#6a4ac0", "#f0c020", "#c83a7a", "#7aa0e0" }))
				.Blades("FlowersSpringSteppe", t => OpenBlade(t, "#4a6a3a", "#5a7a42", 0.4f, head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.03f,
					heads: new[] { "#f2d020", "#7a4aa8", "#e8d040", "#e0dce8" }))
				.Blades("FlowersPeony", t => OpenBlade(t, "#3e6a2a", "#4e7a32", 0.3f, head: GrassBladeHead.Flower, sprinkle: 4f, headSize: 0.08f,
					heads: new[] { "#c01828", "#e0b020" }))
				.Blades("FlowersSzik", t => OpenBlade(t, "#5a6a4a", "#6a7a54", 0.35f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.03f,
					heads: new[] { "#8a6ac0", "#b0a0e0", "#f6f6f0", "#e0c030" }))
				.Blades("FlowersTatar", t => OpenBlade(t, "#4a6a34", "#5a7a3a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.05f,
					heads: new[] { "#a01830", "#7a3a8a", "#f4f2ea" }))
				// Approved settlement mix: sprawling market towns, farmstead villages, wayside inns and herders' camps, mills, ring-forts,
				// the kunhalom mounds and stone figures, open-plain battlefields; no lumber kinds.
				.Sites((POIType.Camp, 1.3f), (POIType.Shrine, 0.8f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 0.5f), (POIType.Cave, 0.6f),
					(POIType.Monument, 0.6f), (POIType.Wreck, 0.3f), (POIType.Waterfall, 0.6f), (POIType.Rapids, 0.6f), (POIType.River, 1f), (POIType.Lake, 1.2f),
					(POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 0.3f), (POIType.Pass, 0.3f), (POIType.Gorge, 0.4f), (POIType.Mesa, 0.2f),
					(POIType.Butte, 0.3f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 0.5f), (POIType.Grotto, 0.6f), (POIType.SeaCave, 0.2f),
					(POIType.BanditCamp, 0.7f), (POIType.HuntingLodge, 0.3f), (POIType.FishingCamp, 0.6f), (POIType.MonsterDen, 0.4f), (POIType.Nest, 0.5f),
					(POIType.RitualSite, 0.6f), (POIType.StoneCircle, 0.15f), (POIType.Temple, 0.4f), (POIType.Monastery, 0.6f), (POIType.FeyRing, 0.3f),
					(POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.5f), (POIType.Portal, 0.3f), (POIType.Graveyard, 0.8f), (POIType.Barrow, 1.5f),
					(POIType.Crypt, 0.4f), (POIType.Battlefield, 1f), (POIType.Ossuary, 0.3f), (POIType.Ruins, 0.6f), (POIType.RuinedTower, 0.4f),
					(POIType.Statue, 0.8f), (POIType.Obelisk, 0.1f), (POIType.AncientRoad, 0.4f), (POIType.AbandonedFarm, 1f), (POIType.Hermitage, 0.4f),
					(POIType.Village, 1.1f), (POIType.Town, 0.7f), (POIType.City, 0.3f), (POIType.Capital, 0.15f), (POIType.Port, 0.3f), (POIType.Keep, 1f),
					(POIType.Castle, 0.4f), (POIType.Fortress, 0.2f), (POIType.TradingPost, 0.9f), (POIType.Waystation, 1.4f), (POIType.Mine, 0.1f),
					(POIType.Quarry, 0.3f), (POIType.BrickKiln, 0.5f), (POIType.Lighthouse, 0.2f), (POIType.SmugglersCove, 0.2f), (POIType.PirateCove, 0.1f),
					(POIType.SunkenShip, 0.5f), (POIType.SunkenRuins, 0.5f), (POIType.SunkenCity, 0.3f), (POIType.DungeonEntrance, 0.8f), (POIType.BossLair, 0.6f),
					(POIType.OreVein, 0.3f), (POIType.CrystalFormation, 0.3f), (POIType.AncientTree, 0.5f), (POIType.HerbGrove, 0.9f), (POIType.Dockyard, 0.2f),
					(POIType.Windmill, 1.4f), (POIType.Watermill, 0.4f), (POIType.WitchHut, 0.2f), (POIType.CorruptedGrove, 0.1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.4f), (POIType.GoblinWarren, 0.7f),
					(POIType.HauntedManor, 1f), (POIType.HangingTree, 1f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f),
					(POIType.WreckedCaravan, 0.6f), (POIType.Crossroads, 1f), (POIType.BoundaryStone, 0.6f), (POIType.SignalBeacon, 0.8f),
					(POIType.TollGate, 0.8f), (POIType.Hamlet, 1f), (POIType.ManorHouse, 0.8f), (POIType.SiegeCamp, 0.6f), (POIType.BurnedVillage, 0.6f),
					(POIType.WarbandCamp, 0.5f), (POIType.CultistHideout, 0.5f), (POIType.AncientWall, 0.8f), (POIType.SpellScar, 0.4f),
					(POIType.PlanarRift, 0.3f), (POIType.PetrifiedBattle, 0.8f), (POIType.TitansBones, 0.5f), (POIType.WishingStone, 1f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.DruidGrove, 0.3f), (POIType.ElfWaystone, 0.4f),
					(POIType.VillageGreen, 0.6f), (POIType.LongBarrow, 1f), (POIType.Henge, 1f), (POIType.GallowsHill, 1f), (POIType.KurganField, 0.6f),
					(POIType.StoneFigures, 0.4f), (POIType.RuneStone, 0.4f), (POIType.LadyOfTheLake, 0.3f), (POIType.NymphPool, 0.4f),
					(POIType.RoyalMausoleum, 0.4f))
				.Prefers(POIType.Village, "Timber", 1.6f)
				.Prefers(POIType.Village, "Stone", 0.4f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 1.2f)
				.Prefers(POIType.Town, "Stone", 1f)
				.Prefers(POIType.Town, "Palisaded", 0.6f)
				.Prefers(POIType.City, "Stone", 1.3f)
				.Prefers(POIType.City, "Timber", 0.6f)
				.Prefers(POIType.Capital, "Stone", 1.3f)
				.Prefers(POIType.Capital, "Timber", 0.5f)
				.Prefers(POIType.Keep, "Palisade", 1.4f)
				.Prefers(POIType.Keep, "Stone", 0.6f)
				.Prefers(POIType.Castle, "Stone", 1.2f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Waystation, "Inn", 1.8f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.3f)
				.Prefers(POIType.Camp, "Hide Tents", 2f)
				.Prefers(POIType.Camp, "Lean-tos", 0.4f)
				.Prefers(POIType.Camp, "Crude", 0.3f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.BanditCamp, "Palisade", 1.4f)
				.Prefers(POIType.BanditCamp, "Open", 0.6f)
				.Prefers(POIType.Barrow, "Ringed", 1.6f)
				.Prefers(POIType.Barrow, "Excavated", 0.1f)
				.Prefers(POIType.Statue, "Standing", 1.6f)
				.Prefers(POIType.Statue, "Broken", 0.3f)
				.Prefers(POIType.Graveyard, "Earthen", 1.4f)
				.Prefers(POIType.Graveyard, "Railed", 0.3f)
				.Prefers(POIType.Windmill, "Post", 2f)
				.Prefers(POIType.Quarry, "Cut Stone", 0.6f)
				.Prefers(POIType.Monastery, "Cloister", 1f)
				.Prefers(POIType.Monastery, "Timber", 0.6f)
				.Prefers(POIType.RitualSite, "Bonefire", 1.4f)
				.Prefers(POIType.RitualSite, "Stones", 0.3f)
				.Prefers(POIType.StoneCircle, "Menhirs", 1f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0f)
				.Prefers(POIType.AbandonedFarm, "Farmstead", 1.4f)
				.Prefers(POIType.AbandonedFarm, "Burned", 0.6f)
				.Prefers(POIType.Ruins, "Stone", 1f)
				.Prefers(POIType.Ruins, "Burned", 0.6f)
				.Prefers(POIType.Tower, "Timber", 1f)
				.Prefers(POIType.Tower, "Stone", 0.6f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.4f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Tower, "Wizard", 0.5f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Grassland's signature pieces — the sweep well (THE skyline signature, at every farmstead, inn and corral),
				// the tanya farmstead (long whitewashed house, reed hip roof, carved porch, blue footing, stork on the chimney), the
				// csárda inn, the horse mill, the reed-and-shingle post mill, the herders' conical reed hut and reed corral, the wagon
				// fort, the kunhalom with Cuman stone figures and carved grave posts, the loess cellar row, the brick rotunda church and
				// its timber belfry, the ring-fort, the Turul shrine, the horse-skull pole, and the noon mirage (approved); until then
				// the kit's variants below stand in, the palette giving them adobe, whitewash, reed and brick.
				// LATER (I7): the herders' reed-hut camp ("Herders"); hide tents stand in.
				// LATER (I7): the wagon fort; a palisaded camp stands in.
				// LATER (I7): the kunhalom ("Kunhalom") and the stone figures ("Stone baba"); the ringed barrow and the standing statue stand in.
				// LATER (I7): carved grave posts ("Kopjafa"); the earthen graveyard stands in.
				// LATER (I7): the horse mill; the post mill stands in for both.
				// LATER (I7): the clay and brick pit ("Clay pit"); the cut-stone quarry stands in.
				// LATER (I7): the horse-skull pole ("Táltos"); the bone-fire stands in.
				.Favours(BiomeStructureStyle.Earth, 1.4f)
				.Favours(BiomeStructureStyle.Timber, 1f)
				.Favours(BiomeStructureStyle.Stone, 0.9f)
				.Favours(BiomeStructureStyle.Hide, 0.6f)
				.Favours(BiomeStructureStyle.Iron, 0.1f)
				// The palette: whitewashed adobe under steep reed hip roofs with an indigo footing, carved dark porch posts, blue-green
				// doors, reed wattle fences, beaten-clay floors; brick towns and churches under shingle with cream limestone dressings.
				// LATER (NEW-SURFACES U2): a FiredBrick surface (red-orange brick, stretcher and diaper bonds) for the towns' walls,
				// plinths and paving; tinted adobe stands in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, OpenHex("#f2f0e8"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, OpenHex("#eeece4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, OpenHex("#b8a478"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.Adobe, OpenHex("#3a4a6a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, OpenHex("#4a3a2a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, OpenHex("#3a6a6a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, OpenHex("#8a7458"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Fence, BiomeStructureMaterial.Reed, OpenHex("#b8a478"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Paving, BiomeStructureMaterial.Adobe, OpenHex("#9a4a30"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, OpenHex("#9a4a30"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, OpenHex("#d8b870"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Shingle, OpenHex("#8a8478"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Adobe, OpenHex("#8a3e28"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Fence, BiomeStructureMaterial.Adobe, OpenHex("#9a4a30"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Adobe, OpenHex("#9a4a30"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, OpenHex("#b8a07a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, OpenHex("#b8a478"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Adobe, OpenHex("#3a4a6a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed, OpenHex("#c8b484"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, OpenHex("#b8a478"), pitch: BiomeRoofPitch.Pitch60)
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Adobe, OpenHex("#f2f0e8"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Adobe, OpenHex("#f2f0e8"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, OpenHex("#6a6a32"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, OpenHex("#d8ccaa"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, OpenHex("#4a3a2a"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, OpenHex("#8a6a40"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, OpenHex("#2e2a26"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, OpenHex("#b8b0a0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, OpenHex("#a8201c"))
				// Approved crops: broomcorn millet, hemp, melons, a little wheat.
				.Crops(("Detail_GrassMillet", 0.45f), ("Detail_CropHemp", 0.25f), ("Detail_CropMelon", 0.2f), ("Detail_GrassWheat", 0.1f))
				// Reed wattle fences round the farmsteads and corrals.
				.Lines(With(new Entry.Line(BiomeLinearForm.Wattle, BiomeLinearPlace.SiteBounds), l =>
				{
					l.Style.OwnMaterial = true;
					l.Style.Material = BiomeStructureMaterial.Reed;
					l.Style.Tint = OpenHex("#c8b484");
				}))
				// Lanterns hung from crooked poles in the farmstead villages, lantern posts in the towns, porch lanterns at the inns,
				// iron fire-baskets on the ring-forts' ramparts (the plain's beacons), torches in the herders' camps; wall lanterns down
				// the cellar rows, wall torches in the clay workings.
				.Lights(POIType.Village, BiomeWayLightKind.CrookLantern, 18f)
				.Lights(POIType.Town, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.City, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.Capital, BiomeWayLightKind.LanternPost, 13f)
				.Lights(POIType.Waystation, BiomeWayLightKind.HangingLantern, 14f)
				.Lights(POIType.Keep, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Castle, BiomeWayLightKind.Cresset, 16f)
				.Lights(POIType.Camp, BiomeWayLightKind.TorchPost, 16f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.WallLantern)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 15f)
				.Gathers("Chamomile Patch", "Soda Bloom", "Thatching Reed", "Wild Pear Fall", "Steppe Peony Root", "Field Mushroom Ring", "Turul Feather")
				.Herbs("Detail_HerbChamomile", "Detail_ThymeMat", "Detail_Mullein")
				// Sun- and wind-bleached through the palette's tints; no moss on a dry plain.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Open");
		}

		/// <summary>
		/// A blade type's look as an open-grassland round's draft gives it (<see cref="Entry.Blades"/>; shared by the Grassland, Plains,
		/// Steppe and Hills files): root and tip colours over the mesh's, the ground tint, and the shape and head values that are set
		/// (null keeps the type's own).
		/// </summary>
		private static void OpenBlade(BiomeGrassType t, string root, string tip, float groundTint, float? density = null, float? stiffness = null,
			float? width = null, float? clump = null, float? bend = null, float? heightScale = null, float? wade = null, GrassBladeHead? head = null,
			float? sprinkle = null, float? headSize = null, float? headShare = null, string[] heads = null)
		{
			t.RootColour = OpenHex(root);
			t.TipColour = OpenHex(tip);
			t.ColoursOverMesh = true;
			t.GroundTint = groundTint;
			t.Density = density ?? t.Density;
			t.Stiffness = stiffness ?? t.Stiffness;
			t.Width = width ?? t.Width;
			t.ClumpPull = clump ?? t.ClumpPull;
			t.Bend = bend ?? t.Bend;
			t.HeightScale = heightScale ?? t.HeightScale;
			t.Wade = wade ?? t.Wade;
			t.Head = head ?? t.Head;
			t.Sprinkle = sprinkle ?? t.Sprinkle;
			t.HeadSize = headSize ?? t.HeadSize;
			t.HeadShare = headShare ?? t.HeadShare;
			if (heads != null)
			{
				t.HeadColours = System.Array.ConvertAll(heads, OpenHex);
				t.HeadColoursOverMesh = true;
			}
		}

		/// <summary>An sRGB colour from #rrggbb, for the Grassland, Plains, Steppe and Hills files.</summary>
		private static Color OpenHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
