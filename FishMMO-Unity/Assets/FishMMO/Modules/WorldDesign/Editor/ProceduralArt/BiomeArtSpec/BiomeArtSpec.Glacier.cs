#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Glacier</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Glacier()
		{
			// A flowing valley glacier (biome round, approved 2026-10-18, authored 2026-10-26), the glacier heads of a Kashmir-like
			// range (700–1300): blue-white ice pours between dark greenstone nunataks, breaks over icefalls into leaning séracs,
			// is barred by ogives, slashed by crevasse fields and striped with medial moraines; meltwater runs into moulins,
			// cryoconite holes pock the bare ice, the tongue goes black under rubble where glacier mice roll, and it ends in an ice
			// mouth the grey river is born from. Knife-crested moraines hold it in; the forefield is raw gravel with river-beauty,
			// snowmelt primulas and rhubarb, copper-barked bhojpatra birches up to the snout in the warm reach. Its people are
			// pilgrims, shepherds and temple-keepers (trefoil temples, nāga tanks, sarais, dhoks dug into the moraines, wand lines
			// across the ice); its fantasy the nāgas under the tarns, a waxing ice-pillar shrine, the pishacha in winter, the
			// gold-digging ants of the outwash and the nāgamani serpent-jewels (invented names; no worship shown, decision 97).
			// Alpine keeps the dolomite, red snow and sun-cups; Ice Sheet the sastrugi plateau; Tundra the polygons and gneiss.
			// LATER (I9): every ground and object texture as the Glacier's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Mapstone V(0.22, 0.95, 0.78) + Meltglow V(0.48, 0.48, 0.98).
			// LATER (CODE-TABLE CLEARANCE Q13): Atmosphere 15 → 14 (drop Airless) in its selection row.
			// LATER (I2g-f A13): a true flow field (chevron crevasses, ogives bent by the flow, medial moraines from confluences); the
			// presets carve along the cut's smoothed downslope until then.
			const BiomeAtmosphereRequirement thin = BiomeAtmosphereRequirement.Thin | BiomeAtmosphereRequirement.Airless;
			// Every plant rule is world-gated to breathable air (decision 41, approved Q7).
			Scatter Live(Scatter rule) => OnWorlds(rule, BiomeAtmosphereRequirement.Living);
			return E("Glacier", L(Ground.IceAblation,
					// The bare ice's cryoconite holes (dust on thin worlds too), the odd glacier mouse inside the medial stripes, the
					// ice's litter and the decayed garlands near the shrines and wand lines.
					Plants("LichenCryoconite", 3f, 160), Live(Plants("CushionGlacierMice", 0.2f)),
					Plants("DebrisGlacier", 1f, 128), Moded(Plants("DebrisGlacier", 4f, 128), BiomeScatterMode.NearSites("Shrine, Temple", 30f)),
					Stones(SmallRocks("Greenstone"), 0.5f),
					// Calved blocks below the icefalls and at the snout, glacier tables and dirt cones.
					IceBlocks(0.04f, 12f, "Calved", "Slab"),
					Outcrops("Glacier tables", 0.005f, 30f, F(RockTypes.WaterIce, "GlacierTable"), 15f),
					Outcrops("Dirt cones", 0.005f, 30f, F(RockTypes.WaterIce, "DirtCone"), 15f),
					Outcrops("Granite erratics", 0.004f, 40f, F(RockTypes.Granite, "Perched"))).Air(BiomeAtmosphereRequirement.Living),
				Ds(
					// d0: the runnelled firn basin, penitentes in the dry reach only (Glacier keeps them per Alpine's draft).
					D(Ground.SnowFirnRunnels, 64f, 2f, 0f, 28f,
						Band(Aligned(Outcrops("Penitentes", 0.01f, 20f, F(RockTypes.Firn, "Penitentes"), 20f), -10f, 10f), -1f, 1f, -1f, -0.2f))
						.Air(BiomeAtmosphereRequirement.Living),
					// d1: the icefall (18–40°), its séracs in ranks on the steep steps (the crevasse fields paint it too).
					D(Ground.IceIcefall, 48f, 3f, 18f, 40f, With(Seracs(0.03f, 18f, "Tower", "Block", "Leaning"), s => s.Slope = new Vector2(18f, 40f))),
					// d2: glacier-polished greenstone slabs on the steep ground, dhoop on the ledges, bergenia at the cliff foot.
					// LATER (GROUND-BANDS): map lichen above the trimline only (a trimline band); everywhere on the slabs until then.
					D(Ground.RockGreenstonePolished, 48f, 3f, 28f, 90f, Live(Plants("LichenMap", 2f, 160, 70f)),
						Live(Band(Plants("HerbDhoop", 0.3f), -0.5f, 1f)), Live(Moded(Plants("BergeniaStone", 1f), BiomeScatterMode.CliffFoot())),
						Blocks("Whalebacks", 0.015f, 20f, F(RegionalRocks.Greenstone, "Whaleback", "Plucked"))),
					// d3: the moraines' unsorted till (≤ 34°; the moraine landforms paint it), the first grass, the lichen that dates
					// them, camel's tail on the outer flanks, avalanche-snapped birch in the warm reach.
					D(Ground.GravelTill, 40f, 3f, 0f, 34f, Stones(SmallRocks("Greenstone"), 4f), Live(Band(Grass("GrassTrisetum", 2f, 255, 30f), -0.5f, 1f)),
						Live(Plants("LichenMap", 2f, 160)), Live(Band(Plants("HerbDhoop", 0.5f), -0.5f, 1f)),
						Live(Band(Plants("RhubarbHimalayan", 0.2f), -0.3f, 1f)), Live(Band(Plants("FlowersRiverBeauty", 0.5f, 180), -0.4f, 1f)),
						Live(Moded(Plants("BergeniaStone", 1f), BiomeScatterMode.CliffFoot(6f))), Live(Band(Bush("CaraganaJubata", 0.15f), -0.3f, 1f)),
						Band(Logs("Avalanche birch", 0.004f, 40f, Wood("FallenLog_AvalancheBirch")), 0f, 1f),
						Blocks("Moraine blocks", 0.02f, 14f, F(RegionalRocks.Greenstone, "Block")),
						With(Blocks("Moraine scree", 0.015f, 12f, F(RegionalRocks.Greenstone, "Scree")), s => s.Slope = new Vector2(20f, 34f))),
					// d4: the debris-covered tongue in the warm reach (t ≥ −0.05), glacier mice rolling on it; a rubble trod over it.
					D(Ground.GravelDebrisTongue, 96f, 3f, 0f, 16f, Stones(SmallRocks("Greenstone"), 2f), Live(Plants("CushionGlacierMice", 0.8f)),
						Plants("DebrisGlacier", 3f, 128), Outcrops("Dirt cones", 0.01f, 24f, F(RockTypes.WaterIce, "DirtCone"), 15f))
						.Temp(-0.05f, 1f).Air(BiomeAtmosphereRequirement.Living).Ways(path: Ground.PathRubbleTrod, road: Ground.PathRubbleTrod),
					// d5: the raw forefield below the terminal moraine (t ≥ −0.2), its pioneers, striated pebbles, rare bhojpatra stands in
					// the warm reach (t ≥ 0.05). LATER (SCATTER-MODES): the melt sedge and primulas within a few metres of the meltwater
					// (a near-water anchor); a sparse scatter over the forefield until then.
					D(Ground.SiltForefield, 56f, 2f, 0f, 18f, Live(Grass("GrassTrisetum", 6f)), Live(Band(Grass("GrassMeltSedge", 2f), -0.4f, 1f, -0.4f, 1f)),
						Live(Band(Plants("FlowersRiverBeauty", 2f, 180), -0.4f, 1f)), Live(Band(Plants("FlowersSnowmeltPrimula", 1f, 180), -0.3f, 1f)),
						Live(Band(Plants("RhubarbHimalayan", 0.4f), -0.3f, 1f)), Live(Band(Bush("CaraganaJubata", 0.2f), -0.3f, 1f)),
						Stones(Pebbles("Striated"), 3f), Outcrops("Granite erratics", 0.004f, 40f, F(RockTypes.Granite, "Perched")),
						Live(Band(Stand(Was(0.01f, 25f), 35f, W(0.15f, 0.6f, open: 0.05f), "Bhojpatra"), 0.05f, 1f)),
						Band(Logs("Avalanche birch", 0.004f, 40f, Wood("FallenLog_AvalancheBirch")), 0f, 1f))
						.Temp(-0.2f, 1f).Air(BiomeAtmosphereRequirement.Living),
					// d6 (Thin): the dust-mantled lobate ice, the main ground's (and the debris tongue's) stand-in under thin air.
					D(Ground.IceDustMantled, 64f, 2f, -1f, -1f, Plants("LichenCryoconite", 2f, 160)).Air(thin),
					// d7 (Thin): the firn under thin air, rimed dust.
					D(Ground.FrostDustRimed, 64f, 2f, 0f, 28f).Air(thin)),
				C(Ground.CliffGreenstone, 38f), Ground.SiltVarvedClay, Ground.GravelGreenBoulderBed)
				// The rules the round retired (grey stones and boulders, the polar plateau's sastrugi, séracs off the flat ice).
				.Moved("main", SmallRocks("Grey"), "Seracs")
				.Moved("detail/0", "Formations: Sastrugi")
				.Moved("detail/2", SmallRocks("Grey"), "Boulders")
				.Ways(path: Ground.PathMoraineTrod, road: Ground.PathPitchedTrail, cobbles: Ground.FlagstoneTempleSlab)
				.Bars(sand: Ground.SandPlacerFlour, gravel: Ground.GravelBraidedShingle)
				// Approved: own rock greenstone (cliffs, nunataks, river boulders); accepts granite, gneiss, limestone; ice for formations.
				.Accepts(RegionalRocks.Greenstone, RockTypes.Granite, RockTypes.Gneiss, RockTypes.Limestone)
				// Cupped depth hoar, frost crystals and the glowing nāgamani.
				.Crystals((BiomeCaveCrystalKind.CupHoar, 0.4f), (BiomeCaveCrystalKind.FrostCrystal, 0.3f), (BiomeCaveCrystalKind.Nagamani, 0.3f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.Look(BiomeLookClass.Ice)
				// Approved: nunatak crag life (Mountain); fresh water stays off (frozen and silty).
				.Wildlife(BiomeHabitat.Ice | BiomeHabitat.Mountain)
				// LATER (AMBIENT-LIFE N20): ice worms surfacing at dusk, glacier fleas, snowcock, ibex, griffon vultures, accentors, the
				// rare brown bear; the two white doves of the ice-pillar cave, a nāga coil under a tarn at dusk, pishacha on the winter
				// ice, gold-digging ants on the outwash.
				// Supraglacial azure: clear meltwater over blue ice, cryoconite silt.
				.WaterTint(new BiomeWaterLook(NivalHex("#5ab0f4"), NivalHex("#1846c8"), new Vector3(0.55f, 0.1f, 0.03f), 0.05f, NivalHex("#2a2622"),
					NivalHex("#f0f6fa")))
				// Bergy bits on the proglacial lakes.
				.Floats((BiomeFloatingPlantKind.BergyBits, 1f))
				.Moat(BiomeMoatWater.Frozen)
				.NoVines()
				// Snow bridges over the torrents.
				.Crosses(BiomeCrossingStyle.SnowBridge)
				// The glacier bundle (approved Q2): crevasse fields, moulins fed by supraglacial channels, lateral, medial and terminal
				// moraines with kettle ponds, ogives below the icefalls, the snout portal the river is born from.
				// LATER (I7 I34): the moulin's terrain hole and MoulinThroat piece; LATER (I2n-e F24): the snout portal's meltwater stream.
				.Landforms(BiomeLandformPresets.Named("CrevasseField"), BiomeLandformPresets.Named("Moulin"), BiomeLandformPresets.Named("SupraglacialChannels"),
					BiomeLandformPresets.Named("LateralMoraine").Painted(4, BiomeLandformPart.Whole), BiomeLandformPresets.Named("MedialMoraine"),
					BiomeLandformPresets.Named("TerminalMoraine"), BiomeLandformPresets.Named("KettlePonds"), BiomeLandformPresets.Named("Ogives"),
					BiomeLandformPresets.Named("SnoutPortal"))
				.Describe("A valley glacier — blue ice and leaning séracs between polished rock, crevasse fields and roaring moulins, striped moraines "
					+ "and a black rubble tongue ending in the ice mouth where the river is born; pilgrims cross by wand lines to ice-cave shrines")
				.Names(onsets: new[] { "Kol", "Har", "Shesh", "Nag", "Gang", "Tar", "Sar", "Mar", "Pan", "Var", "Chand", "Him", "Lid", "Ama", "Thaj", "Kun" },
					nuclei: new[] { "a", "aa", "i", "u", "o", "e" },
					codas: new[] { "nag", "sar", "marg", "gali", "kund", "bal", "pora", "hom", "wan" },
					dungeonSuffixes: new[] { "Gufa", "Pataal", "Kandar", "Darra" },
					poiSuffixes: new[] { "Nag", "Sar", "Gali", "Marg", "Kund", "Teerth", "Gufa", "Dhok", "Sarai", "Pal" },
					about: "A Kashmiri voice of the high valleys (700–1300): invented names only.")
				.Blades("GrassTrisetum", t => NivalBlade(t, "#4a5a3a", "#8a9a6a", 0.4f, stiffness: 1.6f, clump: 0.6f, head: GrassBladeHead.Plume, headSize: 0.04f,
					headShare: 0.35f, heads: new[] { "#9a8aa8", "#d0c8d8" }))
				.Blades("GrassMeltSedge", t => NivalBlade(t, "#3e4a2a", "#6a7a42", 0.45f, clump: 0.4f, head: GrassBladeHead.SeedHead, headSize: 0.02f,
					headShare: 0.25f, heads: new[] { "#3a2a22" }))
				.Blades("FlowersRiverBeauty", t => NivalBlade(t, "#7a8a8a", "#7a8a8a", 0.3f, head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.035f,
					heads: new[] { "#d0308a" }))
				.Blades("FlowersSnowmeltPrimula", t => NivalBlade(t, "#3a5a2a", "#3a5a2a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 8f, headSize: 0.025f,
					heads: new[] { "#e04a8a", "#f0c020" }))
				// Approved: near-empty settlement — pilgrim, shepherd and ice-work kinds; village 0.1, keep 0.05, no towns or castles.
				.Sites((POIType.Glacier, 1.5f), (POIType.IceCave, 1.4f), (POIType.Pass, 1.4f), (POIType.Peak, 1.4f), (POIType.Lake, 1.2f), (POIType.Spring, 1f),
					(POIType.River, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 0.8f), (POIType.Gorge, 1f), (POIType.Bridge, 1f), (POIType.Shrine, 1.2f),
					(POIType.Temple, 0.6f), (POIType.Hermitage, 1f), (POIType.Waystation, 0.8f), (POIType.Camp, 1f), (POIType.Quarry, 0.4f),
					(POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.8f), (POIType.MonsterDen, 0.8f), (POIType.Nest, 0.6f), (POIType.RitualSite, 0.6f),
					(POIType.Overhang, 0.6f), (POIType.NaturalArch, 0.3f), (POIType.Valley, 0.6f), (POIType.HerbGrove, 0.5f), (POIType.Monastery, 0.3f),
					(POIType.AncientRoad, 0.4f), (POIType.Ruins, 0.4f), (POIType.RuinedTower, 0.3f), (POIType.Statue, 0.3f), (POIType.LeyNexus, 0.4f),
					(POIType.FallenStar, 0.3f), (POIType.HuntingLodge, 0.3f), (POIType.Grotto, 0.3f), (POIType.Cave, 0.3f), (POIType.Mine, 0.3f),
					(POIType.Ossuary, 0.2f), (POIType.Battlefield, 0.2f), (POIType.Portal, 0.2f), (POIType.BanditCamp, 0.2f), (POIType.Graveyard, 0.1f),
					(POIType.Village, 0.1f), (POIType.Keep, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.8f), (POIType.GoblinWarren, 0.7f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 0.6f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.TitansBones, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.8f), (POIType.SummitCairn, 0.4f),
					(POIType.FrostGiantSteading, 1.2f), (POIType.RuneStone, 1f), (POIType.FrozenArmy, 1.2f), (POIType.DragonInTheIce, 1.2f),
					(POIType.MammothGraveyard, 0.6f), (POIType.WinterWolfDen, 1f), (POIType.AuroraShrine, 1f), (POIType.LichSpire, 1f),
					(POIType.IcePalace, 0.6f), (POIType.LostThrone, 0.4f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.MonsterDen, "Bones", 0.6f)
				.Prefers(POIType.MonsterDen, "Lair", 0.4f)
				.Prefers(POIType.OreVein, "Spoil", 0.8f)
				.Prefers(POIType.HuntingLodge, "Lodge", 0.6f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Monastery, "Cloister", 1f)
				.Prefers(POIType.Temple, "Court", 2f)
				.Prefers(POIType.Temple, "Colonnade", 0.3f)
				.Prefers(POIType.Shrine, "Shrine", 1.6f)
				.Prefers(POIType.Shrine, "Statue", 0.3f)
				.Prefers(POIType.Waystation, "Stone Inn", 1.8f)
				.Prefers(POIType.Waystation, "Inn", 0.2f)
				.Prefers(POIType.Camp, "Crude", 1.5f)
				.Prefers(POIType.Camp, "Hide Tents", 0.3f)
				.Prefers(POIType.Camp, "Lean-tos", 0.3f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Hermitage, "Stone", 1.6f)
				.Prefers(POIType.Hermitage, "Cottage", 0.2f)
				.Prefers(POIType.Bridge, "Timber", 0.4f)
				.Prefers(POIType.Quarry, "Cut Stone", 1.6f)
				.Prefers(POIType.Village, "Stone", 1.6f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 0f)
				.Prefers(POIType.Keep, "Stone", 1f)
				.Prefers(POIType.FishingCamp, "Huts", 0f)
				.Prefers(POIType.Monastery, "Timber", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 0f)
				.Prefers(POIType.Graveyard, "Railed", 1f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
				.Prefers(POIType.DungeonEntrance, "Door", 1f)
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
				.Prefers(POIType.DragonLair, "Bone Roost", 1.4f)
				.Prefers(POIType.GiantsSteading, "Great Hall", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.5f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.4f)
				.Prefers(POIType.Commandery, "Preceptory", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.2f)
				// (the fantasy wave's variants end here)
				// LATER (I7): the Glacier's signature pieces — the trefoil temple with its two-tier pyramidal stone roof, the nāga tank,
				// the waxing ice-pillar cave (≤ 1 a scene, moon clock), the glacier-mouth shrine, the sarai, the dhok dug into a moraine,
				// the ladder bridge, the ice quarry and cellar, the bhoj cell, the taq house, the deepstambha, the nāga tarn, the demon
				// bones, the pishacha den and the ant-gold workings (approved); until then the kit's variants below stand in.
				.Favours(BiomeStructureStyle.Stone, 1.6f)
				.Favours(BiomeStructureStyle.Timber, 1.2f)
				.Favours(BiomeStructureStyle.Hide, 0.6f)
				.Favours(BiomeStructureStyle.Iron, 0.2f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				// The palette. Stone (temples, sarais): big grey limestone blocks on stepped plinths, temple roofs of stone, houses under
				// earth over birch bark at 35°, deodar lattice, carved deodar doors, greenstone drystone, limestone carving, brass and
				// iron, oatmeal pattu, saffron pennants. Timber (houses): timber-laced greenstone ground floors, dhajji lattice upper
				// walls. Hide (shepherds): fieldstone dug into the moraine. Iron (mines): fieldstone and iron.
				// LATER (NEW-SURFACES / I9): BhojEarth, TimberLaced, Dhajji, Pattu, TempleSlab and the saffron pennant as the Glacier's own
				// surfaces; LATER (I7/I6h I28): the two-tier pyramidal stone temple roof. The kit's materials tinted stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, NivalHex("#8e8a80"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Stone, NivalHex("#8e8a80"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, NivalHex("#6a5a48"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, NivalHex("#a29e94"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, NivalHex("#5a3a24"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, NivalHex("#5a3a24"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Floor, BiomeStructureMaterial.Stone, NivalHex("#9a9890"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Fence, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, NivalHex("#9a9890"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, NivalHex("#a89a84"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#6a5a48"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Earth, NivalHex("#6a5a48"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, NivalHex("#5a3a24"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, NivalHex("#5a3a24"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#6a5a48"), pitch: BiomeRoofPitch.Pitch35)
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, NivalHex("#5a3a24"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, NivalHex("#6a5a48"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, NivalHex("#56645a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Monolith, NivalHex("#a29e94"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, NivalHex("#5a3a24"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, NivalHex("#3a3630"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, NivalHex("#8a7458"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, NivalHex("#d86a1a"))
				// Approved: no fields (authored empty list).
				.NoCrops()
				// Wand lines across the ice (the ways over ice and firn) and moraine-boulder dykes round the folds.
				// LATER (I2f-b LEFT (g)): wand lines over ice and firn only (every lane of the biome until a per-ground gate exists).
				.Lines(new Entry.Line("WandLine"), new Entry.Line("MoraineDyke"))
				// Oil-lamp niches on the village walls, dhuni fires at the sarais, stone lamp pillars at temple and shrine, fire baskets
				// up the pilgrim track, the shepherds' camps unlit; torches along a cave's route, lanterns in the mines.
				.Lights(POIType.Village, BiomeWayLightKind.WallLantern, 14f)
				.Lights(POIType.Waystation, BiomeWayLightKind.Brazier, 18f)
				.Lights(POIType.Temple, BiomeWayLightKind.LanternStand, 16f)
				.Lights(POIType.Shrine, BiomeWayLightKind.LanternStand, 16f)
				// The passes and the pilgrim road are natural kinds, sited on the thin worlds too, where no flame burns (decision 41): a cold
				// crystal post marks them. LATER (I7a): the draft's fire-basket cressets on the breathable worlds, once way lights swap by
				// the world's air.
				.Lights(POIType.Pass, BiomeWayLightKind.CrystalPost, 30f)
				.Lights(POIType.AncientRoad, BiomeWayLightKind.CrystalPost, 30f)
				.Lights(POIType.Hermitage, BiomeWayLightKind.Brazier, 12f)
				.Unlit(POIType.Camp)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.WallTorch)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.HangingLantern, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.WallLantern)
				.Gathers("Bhojpatra Bark", "Dhoop Root Patch", "Rhubarb Root Patch", "Ant-gold Sand", "Cornflower Sapphire", "Old Ice", "Nagamani Cluster")
				.Herbs("Detail_HerbDhoop", "Detail_RhubarbHimalayan", "Detail_BergeniaStone")
				// Approved: the new Floured weathering (rock-flour film and tide-marks on the lower walls, silt in the carvings, rust
				// bleed from the iron) as a coat in place of moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Floured, 0.5f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
