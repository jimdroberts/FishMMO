#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Sheet</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceSheet()
		{
			// The continental ice cap's interior (biome round, approved 2026-10-25, authored 2026-10-26): a white dome to every horizon
			// combed by the katabatic wind falling off it — hard wind-slab carved into sastrugi down the fall line, slopes polished to
			// a wind glaze, megadune swells, and in the lee of every nunatak a field of old blue ice where black star-stones lie. The
			// nunataks are dark charnockite, honey-rinded, ringed by wind-scooped moats, their ledges streaked with petrels'
			// stone-oil; grey and black frigid lichens are the only life. The Sakha cross it on the ice road from one carved serge
			// post to the next, sleeping in balagan refuges banked to the eaves; the olonkho says it is the Winter Bull's pasture.
			// On thin-aired worlds nobody comes: dust-banded layered ice, spiral troughs, Swiss-cheese carbon-dioxide slab (the Thin
			// band's grounds below, banded Thin and Airless until the selection row drops Airless).
			// Calving fronts and ice cliffs are Ice Shelf's; Glacier keeps the flowing valley ice, Tundra the herders' snow-houses.
			// LATER (I9): every ground and object texture as the Ice Sheet's own variant of its recipe (decision 12).
			// LATER (RARE-LOOKS, no spec column yet): rare looks Stoneoil V(0.075, 0.85, 0.16) + Parhelion V(0.16, 0.32, 1.00).
			// LATER (CODE-TABLE CLEARANCE Q13): Atmosphere 15 → 14 (drop Airless) in its selection row.
			// LATER (WEATHER O1): the spindrift ground-mist mode and the katabatic wind term, on the shared clock.
			const BiomeAtmosphereRequirement thin = BiomeAtmosphereRequirement.Thin | BiomeAtmosphereRequirement.Airless;
			return E("Ice Sheet", L(Ground.SnowKatabatic,
					// The biome's signature: katabatic sastrugi along the fall line turned 30°, sharper in the dry interior.
					// LATER (I9): the "Katabatic" variant of the firn sastrugi (undercut prows, layered cut faces).
					Moded(Blocks("Katabatic sastrugi", 0.06f, 8f, F(RockTypes.Firn, "Sastrugi"), 20f), BiomeScatterMode.DownslopeAligned(30f, 12f)),
					Moded(Band(Blocks("Dry-interior sastrugi", 0.03f, 8f, F(RockTypes.Firn, "Sastrugi"), 20f), -1f, 1f, -1f, -0.3f),
						BiomeScatterMode.DownslopeAligned(30f, 12f)),
					// LATER (NEW-ROCK-TYPES): wind-crust plates of their own (Detail_WindCrustPlates); ice cobbles stand in.
					Stones(SmallRocks("IceCobble"), 0.2f)).Air(BiomeAtmosphereRequirement.Living),
				Ds(
					// d0: the wind glaze (≤ 6°), migrating snow barchans and wind-crust litter on it; no way is painted over it (the
					// serge line is the way). LATER (NEW-ROCK-TYPES): a SnowBarchan crescent drift; the firn wave dune stands in.
					D(Ground.IceWindGlaze, 96f, 3f, 0f, 6f, Blocks("Snow barchans", 0.01f, 30f, F(RockTypes.Firn, "WaveDune"), 20f),
						Stones(SmallRocks("IceCobble"), 0.5f)).Air(BiomeAtmosphereRequirement.Living),
					// d1: blue-ice fields in the nunataks' lee (the BlueIceTail landform paints it), star-stones on it.
					// LATER (NEW-ROCK-TYPES): a star-stone set (black fusion crust, regmaglypts); basalt stones stand in.
					D(Ground.IceBlueField, 160f, 4f, 0f, 10f, Stones(SmallRocks("Basalt"), 0.05f), Stones(SmallRocks("Charnockite"), 0.3f)),
					// d2: the charnockite nunatak fell (≥ 22° and on the nunataks), its lichens, the petrels' stone-oil by the nests,
					// seep moss where the guano feeds it; the path over it is a shingle trod.
					D(Ground.RockCharnockiteFell, 64f, 3f, 22f, 90f, Stones(SmallRocks("Charnockite"), 3f),
						Moded(Plants("LichenStoneOil", 1.2f), BiomeScatterMode.NearSites("Nest", 40f)),
						With(Plants("LichenStoneOil", 0.05f, 160, 70f), s => s.Slope = new Vector2(30f, 70f)),
						Band(Plants("LichenFrigid", 2f, 160, 70f), -0.9f, 1f), Plants("LichenBlacktuft", 0.8f, 160, 70f),
						Plants("LichenSulphur", 0.5f, 160, 70f), Moded(Plants("LichenSulphur", 1.5f, 160, 70f), BiomeScatterMode.NearSites("Nest", 40f)),
						Band(Moded(Plants("CushionMossSeep", 0.3f, 160, 20f), BiomeScatterMode.NearSites("Nest", 20f)), -0.4f, 1f),
						Outcrops("Charnockite tors", 0.01f, 30f, F(RegionalRocks.Charnockite, "Tor")),
						Blocks("Fell blocks", 0.02f, 14f, F(RegionalRocks.Charnockite, "FellBlock", "Boulder")),
						Blocks("Charnockite ledges", 0.01f, 20f, F(RegionalRocks.Charnockite, "Ledges"), 60f))
						.Air(BiomeAtmosphereRequirement.Living).Ways(path: Ground.GravelNunatakShingle),
					// d3: surface hoar on the calm, cold, wetter accumulation ground.
					D(Ground.FrostSurfaceHoar, 48f, 2f, 0f, 5f).Temp(-1f, -0.6f).Humid(0f, 1f),
					// d4: blue-ice moraine welling against the nunataks, wind-fluted windstones at their foot.
					D(Ground.GravelBlueIceMoraine, 40f, 3f, 0f, 15f, Stones(SmallRocks("Charnockite"), 2f),
						Blocks("Windstones", 0.02f, 12f, F(RegionalRocks.Charnockite, "Windstone"), 25f)),
					// d5: the stepped escarpments of the dome's wind-cut flanks.
					D(Ground.IceSteppedEscarpment, 64f, 3f, 20f, 40f),
					// d6 (Thin): spiral-trough scarps (the SpiralTroughs landform paints them).
					D(Ground.IceSpiralScarp, 64f, 3f, 12f, 35f).Air(thin),
					// d7: the tidewater face where the cap meets the sea (40 m and less above it).
					D(Ground.IceTidewater, 32f, 4f, 40f, 90f).Depth(-40f, 2f).Air(BiomeAtmosphereRequirement.Living),
					// d8 (Thin): the layered deposits, the main ground's stand-in under thin air, thin-wind sastrugi on them.
					D(Ground.IceLayeredDeposit, 64f, 2f, -1f, -1f,
						Moded(Blocks("Thin-wind sastrugi", 0.02f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), BiomeScatterMode.DownslopeAligned(30f, 12f))).Air(thin),
					// d9 (Thin): Swiss-cheese slab (the SwissCheese landform's floors).
					D(Ground.FrostSwissCheese, 64f, 3f, 0f, 8f).Air(thin),
					// d10 (Thin): the dust-varnished fell, no lichen, star-stones and windstones only.
					D(Ground.RockCharnockiteDust, 64f, 3f, 22f, 90f, Stones(SmallRocks("Charnockite"), 3f),
						Blocks("Windstones", 0.02f, 12f, F(RegionalRocks.Charnockite, "Windstone"), 25f)).Air(thin)),
				C(Ground.CliffCharnockite, 40f).Air(BiomeAtmosphereRequirement.Living), Ground.IceBubbleStack, Ground.IceMeltRunnel)
				.AddCliff(C(Ground.CliffRedDust, 40f).Air(thin))
				// The rules the round retired (calved blocks to Ice Shelf, the scene-wind sastrugi, basalt stones, Taiga's lichen).
				.Moved("main", "Ice boulders", "Formations: Sastrugi")
				.Moved("detail/0", SmallRocks("Basalt"))
				.Moved("detail/2", "LichenClump")
				.Ways(path: Ground.PathSnowHorseTrace, road: Ground.PathIceRoad, cobbles: Ground.SnowBlockYard)
				.Bars(sand: Ground.SandCharnockiteGrus, gravel: Ground.GravelNunatakShingle)
				// Approved: own rock charnockite (its formations); firn and water ice for formations only.
				.Accepts(RegionalRocks.Charnockite, RockTypes.Firn, RockTypes.WaterIce)
				.CaveShell(CaveForm.LavaTube, "Charnockite")
				// Blue quartz (the Ice Sheet's own clear quartz), frost crystals, and the fantasy hornice glowing with the wind.
				.Crystals((BiomeCaveCrystalKind.ClearQuartz, 0.5f), (BiomeCaveCrystalKind.FrostCrystal, 0.3f), (BiomeCaveCrystalKind.Hornice, 0.2f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Wind)
				.Look(BiomeLookClass.Ice)
				.Water(BiomeWaterRole.IceSheet)
				// The rare liquid under lake ice.
				.WaterTint(new BiomeWaterLook(NivalHex("#5a8aa8"), NivalHex("#08161e"), new Vector3(0.55f, 0.12f, 0.06f), 0.04f, NivalHex("#8a8478"),
					NivalHex("#eef4f8")))
				.Wildlife(BiomeHabitat.Ice)
				// LATER (AMBIENT-LIFE): snow petrels round the nunataks, Antarctic petrel flocks high over the dome, and the Winter Bull
				// glimpsed through drifting snow when the wind is strong (client-only, ≤ 1 visible a scene).
				.Moat(BiomeMoatWater.Frozen)
				.NoVines()
				// Nunataks with their windward wind-scoop moats, a blue-ice tail in each one's lee (the entry after it), megadunes with
				// their lee faces; on thin air the spiral troughs and the Swiss-cheese pits (painted on d9).
				.Landforms(BiomeLandformPresets.Named("Nunatak"), BiomeLandformPresets.Named("BlueIceTail"), BiomeLandformPresets.Named("Megadunes"),
					BiomeLandformPresets.Named("SpiralTroughs"), BiomeLandformPresets.Named("SwissCheese").Painted(10, BiomeLandformPart.Floor))
				.Describe("The heart of a continental ice cap: a white dome to every horizon combed into sastrugi by the falling wind, mirror-glazed "
					+ "slopes and fields of old blue ice strewn with black star-stones, dark charnockite nunataks with wind-scooped moats and petrel "
					+ "ledges black with stone-oil. Ice-road carriers cross it from serge post to serge post; the Winter Bull grazes upwind.")
				.Names(onsets: new[] { "Muus", "Khaar", "Tym", "Kyh", "Taas", "Ürüŋ", "Bul", "Sar", "Kyr", "Doy", "Ölöö", "Kel", "Ytar", "Aan", "Säs" },
					nuclei: new[] { "a", "y", "ü", "ö", "aa", "uu", "yy", "ie" },
					codas: new[] { "lakh", "daakh", "tyy", "kha", "yt", "aan", "taas", "khaya", "bys", "ölöön" },
					dungeonSuffixes: new[] { "Abaasy Gate", "Iron House", "Lower Hollow", "Bull's Bed", "Horn Vault", "Crevasse" },
					adjectives: new[] { "White", "Falling-wind", "Glazed", "Blue", "Horned", "Unthawing", "Starfallen", "Sastrugi", "Endless", "Bull-breathed" },
					poiWords: new[]
					{
						("waystation", new[] { "Balagan" }), ("landmark", new[] { "Serge Row", "Bull's Horns" }), ("shrine", new[] { "Choron Cairn", "Serge Row" }),
						("nest", new[] { "Stone-oil Ledges" }), ("graveyard", new[] { "Arangas" }), ("monument", new[] { "Rider Panel" }),
						("fallenstar", new[] { "Black Stones" }), ("camp", new[] { "Windbreak Camp" }), ("ancientroad", new[] { "Ice Road" }),
						("peak", new[] { "Nunatak Cairn" }),
					},
					about: "Sakha voice of the olonkho (ice-road carriers of the middle Lena, 600–1500): invented names only.")
				// Approved: nobody lives here — no settlements; the waystation refuges and camps are the only lived-in kinds (all needing air).
				.Sites((POIType.Waystation, 1.4f), (POIType.Camp, 1f), (POIType.Peak, 1.4f), (POIType.Landmark, 1.2f), (POIType.Shrine, 0.8f),
					(POIType.FallenStar, 1f), (POIType.IceCave, 0.8f), (POIType.Nest, 0.8f), (POIType.Pass, 0.5f), (POIType.Overhang, 0.3f),
					(POIType.NaturalArch, 0.2f), (POIType.Cave, 0.3f), (POIType.CrystalFormation, 0.4f), (POIType.Monument, 0.5f), (POIType.Graveyard, 0.5f),
					(POIType.MonsterDen, 0.3f), (POIType.BossLair, 0.2f), (POIType.DungeonEntrance, 0.3f), (POIType.Portal, 0.2f), (POIType.AncientRoad, 0.4f),
					(POIType.Ruins, 0.2f), (POIType.Glacier, 0.3f), (POIType.Lake, 0.2f), (POIType.Hermitage, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.8f), (POIType.GoblinWarren, 0.7f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 0.6f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.TitansBones, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.8f), (POIType.SummitCairn, 0.4f),
					(POIType.FrostGiantSteading, 1.2f), (POIType.RuneStone, 1f), (POIType.FrozenArmy, 1.2f), (POIType.DragonInTheIce, 1f),
					(POIType.MammothGraveyard, 0.6f), (POIType.WinterWolfDen, 1f), (POIType.AuroraShrine, 1f), (POIType.LichSpire, 1f),
					(POIType.IcePalace, 0.6f), (POIType.FrozenShipwreck, 0.6f), (POIType.LostThrone, 0.4f), (POIType.Keep, 1f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 0f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0f)
				.Prefers(POIType.City, "Timber", 0f)
				.Prefers(POIType.Capital, "Timber", 0f)
				.Prefers(POIType.Keep, "Palisade", 0f)
				.Prefers(POIType.Castle, "Highland", 0f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.Camp, "Lean-tos", 0f)
				.Prefers(POIType.Shrine, "Shrine", 1.4f)
				.Prefers(POIType.Shrine, "Statue", 0.4f)
				.Prefers(POIType.Graveyard, "Railed", 2f)
				.Prefers(POIType.Monument, "Obelisk", 1.4f)
				.Prefers(POIType.Monument, "Statue", 0.2f)
				.Prefers(POIType.FallenStar, "Crater", 2f)
				.Prefers(POIType.Nest, "Bones", 2f)
				.Prefers(POIType.BossLair, "Stones", 1.6f)
				.Prefers(POIType.BossLair, "Warcamp", 0f)
				.Prefers(POIType.DungeonEntrance, "Door", 1f)
				.Prefers(POIType.Portal, "Arch", 1f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 0f)
				.Prefers(POIType.FishingCamp, "Huts", 0f)
				.Prefers(POIType.Monastery, "Timber", 0f)
				.Prefers(POIType.Graveyard, "Earthen", 0f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 0f)
				.Prefers(POIType.Waystation, "Inn", 2f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.2f)
				.Prefers(POIType.Camp, "Hide Tents", 1.6f)
				.Prefers(POIType.Camp, "Crude", 0.8f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
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
				.Air(POIType.Shrine, true)
				.Air(POIType.Nest, true)
				.Air(POIType.Monument, true)
				.Air(POIType.Graveyard, true)
				.Air(POIType.MonsterDen, true)
				.Air(POIType.BossLair, true)
				.Air(POIType.Hermitage, true)
				.Air(POIType.Waystation, true)
				.Air(POIType.Camp, true)
				// LATER (I7): the Ice Sheet's signature pieces — the balagan refuge, serge row, Horns of the Winter Bull (≤ 1 a scene),
				// the Bull's Bed (≤ 1), stone-oil ledges, arangas, the rider panel, the abaasy gate (≤ 1), black stones on blue ice, the
				// windbreak camp, sledge cache, choron cairn, nunatak cairn and old ice road (approved); until then the kit's variants
				// below stand in, the palette giving them larch, sod, hide, wind-slab and charnockite.
				.Favours(BiomeStructureStyle.Timber, 1.5f)
				.Favours(BiomeStructureStyle.Hide, 1f)
				.Favours(BiomeStructureStyle.Stone, 0.4f)
				.Favours(BiomeStructureStyle.Earth, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette. Timber (the refuges): slanted larch daubed grey with clay and dung, bark-and-sod roofs at 30°, snow banked to
				// the sills, larch carved in ring-bands, cowhide doors, bloomery iron, white horsehair. Hide (the camps): wind-slab block
				// walls under smoked cowhide. Stone: charnockite cairns. Earth and Iron build as Timber.
				// LATER (NEW-SURFACES U3/U4/U7/U8/U9): SlantLarch, BarkSod, SnowBank, SergeCarve, HideDoor, IcePane, BloomIron,
				// WhiteHorsehair, WindSlabBlock, SmokedCowhide, CharnockiteCairn; the kit's materials tinted stand in.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, NivalHex("#8a8272"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#5a4a3a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Plinth, BiomeStructureMaterial.IceBlock, NivalHex("#e8eef4"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, NivalHex("#8a4a2a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, NivalHex("#5a3e26"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Floor, BiomeStructureMaterial.Timber, NivalHex("#8a6a4a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, NivalHex("#8a6a4a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, NivalHex("#8a8272"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#5a4a3a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.IceBlock, NivalHex("#e8eef4"))
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Wall, BiomeStructureMaterial.Plaster, NivalHex("#8a8272"))
				.Builds(BiomeStructureStyle.Iron, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#5a4a3a"), pitch: BiomeRoofPitch.Pitch30)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.IceBlock, NivalHex("#dfe7f0"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, NivalHex("#5a3e26"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Plinth, BiomeStructureMaterial.IceBlock, NivalHex("#e8eef4"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, NivalHex("#5a3e26"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, NivalHex("#6a6250"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, NivalHex("#5a5240"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, NivalHex("#6a6250"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Timber, NivalHex("#9a3a20"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Log, NivalHex("#8a6a4a"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, NivalHex("#8a6a4a"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, NivalHex("#6a6250"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, NivalHex("#2a2a2e"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, NivalHex("#f0ece4"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Canvas, NivalHex("#f0ece4"))
				// Approved: no fields, no herb groves.
				.NoCrops()
				// The serge line along the ice road (the ways over glaze and blue ice), wind-slab walls on a camp's windward arc.
				// LATER (I2f-b LEFT (i)): the serge line on Standard/Thick worlds only; nobody lays ways on thin air anyway (no lived-in sites).
				.Lines(new Entry.Line("SergeLine"), new Entry.Line("SnowWall"))
				// Serge lamps at the refuges, dung-fire braziers in the camps, every settlement unlit; hornice crystal posts along a cave's
				// route, braziers at its camps, no mines.
				.Lights(POIType.Waystation, BiomeWayLightKind.SergeLamp, 22f)
				.Lights(POIType.Camp, BiomeWayLightKind.Brazier, 14f)
				.Unlit(POIType.Village)
				.Unlit(POIType.Town)
				.Unlit(POIType.City)
				.Unlit(POIType.Capital)
				.Unlit(POIType.Port)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.Brazier)
				.Unlit(BiomeCaveLightPlace.MineDrift)
				.Unlit(BiomeCaveLightPlace.MineStope)
				.Unlit(BiomeCaveLightPlace.MineLamp)
				.Gathers("Stone-oil Crust", "Breath-ice", "Star-stone", "Blue Quartz Cluster", "Horn-ice")
				// LATER (CODE-TABLE / spec column): the cave mouths' woodland bushes and ferns emptied (BiomeArtSpec.CaveMouth falls back
				// to them when the rules plant none); charnockite stones and sastrugi only.
				.Herbs()
				// Approved: the new Windpacked weathering (windward faces scoured and glazed, gaps packed with wind-slab, drift ramps to
				// the eaves) as a coat in place of moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Windpacked, 0.9f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
