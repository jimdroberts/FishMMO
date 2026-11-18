#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Abyss</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Abyss()
		{
			// The hadal trench joined to the cold seeps of the Gulf (biome round, approved 2026-10-18, authored 2026-10-26): an inner
			// wall of olive-black peridotite scaling into rusty ledges, an outer wall of glassy pillow basalt, a lightless floor of
			// grey-brown ooze under falling marine snow; seeps of white-and-orange mats, seep mussels and lamellibrachia bushes round
			// brine lakes, fire-ice mounds, asphalt flows, serpentinite mud volcanoes; sea pigs, tripod fish, harp sponges. The only
			// light is living: lure polyps, the umbellula's ring, the crystals' slow heartbeat. Fantasy: leviathan falls, galleons
			// pickled whole in the brine, a cyclopean fane, Alexander's glass bell, the Drowned Chain.
			// Painted and grown under the sea (decision 36 (a), I2m: UnderTheSea, no depth bands: the whole floor is the trench).
			// LATER (I9 T1): every ground and object texture as Abyss's own variant of its recipe (decision 12); until then its own recipes.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Atolla (wine-crimson) + Photophore (pure photophore blue).
			// LATER (I9 T7 / ground bands): the seep, asphalt and fire-ice patches lie "near the seep fields"; no band reads a seep, so
			// their noise and slope place them.
			return E("Abyss", L(Ground.SiltHadalOoze,
					// The ooze plain: herds of sea pigs facing the current, tripod fish on their stilts, harp sponges, the umbellula's lonely
					// beacons, the hadal xenophyophores, bored logs from the rivers, a leviathan's bones, peridotite chips near the walls.
					Undersea("SeaPig", 1.5f, 0f, 0f), Undersea("TripodFish", 0.15f, 0f, 0f), Undersea("HarpSponge", 0.3f, 0f, 0f),
					Undersea("Umbellula", 0.2f, 0f, 0f), Undersea("XenophyophoreHadal", 0.1f, 0f, 0f),
					In(BiomeBedWaters.Sea, Logs("Bored logs", 0.01f, 40f, Wood("BoredWood"))),
					In(BiomeBedWaters.Sea, Logs("Leviathan bones", 0.002f, 120f, Wood("LeviathanRib", "LeviathanVertebra"))),
					UnderseaStones(Pebbles("Peridotite"), 3f, 0f, 0f)),
				Ds(
					// d0: the trench wall — scaly peridotite ledges and buttresses, lure polyps on the ledges, sea lilies, chips at the foot.
					D(Ground.PeridotiteTrenchWall, 64f, 4f, 28f, 90f, Undersea("LurePolyp", 0.4f, 0f, 0f, slopeMax: 90f), Undersea("SeaLily", 0.6f, 0f, 0f, slopeMax: 90f),
						UnderseaStones(SmallRocks("Peridotite"), 2f, 0f, 0f),
						In(BiomeBedWaters.Sea, Outcrops("Peridotite ledges", 0.012f, 24f, F(RegionalRocks.Peridotite, "Ledges"), 90f)),
						In(BiomeBedWaters.Sea, Outcrops("Peridotite buttresses", 0.004f, 60f, F(RegionalRocks.Peridotite, "Buttress"), 90f))),
					// d1: the outer wall's pillow talus — glass-rinded pillows, harp sponges and sea lilies among them.
					D(Ground.BasaltPillowTalus, 48f, 3f, 18f, 40f, Undersea("HarpSponge", 0.3f, 0f, 0f), Undersea("SeaLily", 0.3f, 0f, 0f),
						UnderseaStones(SmallRocks("Basalt"), 2f, 0f, 0f)),
					// d2: the seep sediment — sulphide-black mud under Beggiatoa mats and lamellibrachia bushes.
					D(Ground.MudSeepSediment, 20f, 5f, 0f, 10f, Undersea("Lamellibrachia", 3f, 0f, 0f), Undersea("SeepMat", 6f, 0f, 0f)),
					// d3: the seep carbonate pavement — cavernous slabs, mussel rims, tube-worm bushes, lure polyps, chemoherm mounds.
					D(Ground.ChemohermSeepPavement, 30f, 4f, 0f, 20f, Undersea("Lamellibrachia", 3f, 0f, 0f), Undersea("SeepMussel", 4f, 0f, 0f),
						Undersea("LurePolyp", 0.6f, 0f, 0f),
						In(BiomeBedWaters.Sea, Outcrops("Seep chemoherms", 0.006f, 30f, F(RegionalRocks.Chemoherm, "Mound", "Block"), 20f))),
					// d4: asphalt flows in rare patches.
					D(Ground.AsphaltFlow, 90f, 6f, 0f, 14f),
					// d5: the fire-ice crust and its mounds, pink ice-worm pits.
					D(Ground.HydrateCrust, 16f, 6f, 0f, 15f, Undersea("HydrateMound", 0.5f, 0f, 0f))),
				C(Ground.CliffSerpentineScarp, 40f), Ground.SiltOozeRim, Ground.SandTurbiditeChannel)
				// The coastal fade only (an island's foot never reaches the trench): the sea pigs and xenophyophores of the ooze.
				.Under(Sea("SeaPig", 1f, 0f, 0f, 128), Sea("XenophyophoreHadal", 0.2f, 0f, 0f, 128))
				.UnderTheSea()
				.SitesBetween(200f, 11000f)
				// The generic deep kit this round retired (the plain's nodules and xenophyophores, the deep ocean's glass sponges and
				// crinoids, the vents' tube worms and mats, the basalt talus).
				.Moved("lakebed", "Sponge", "Anemone", "BacterialMat", "TubeWorms", "SeaCucumber", "Xenophyophore", "Crinoid",
					SmallRocks("Basalt"), Pebbles("Nodule"))
				.Ways(path: Ground.FlagstoneProcessional, road: Ground.FlagstoneCyclopeanCauseway, cobbles: Ground.PebblesPillowGlass)
				.Bars(sand: Ground.SandHyaloclastite, gravel: Ground.GravelPeridotiteBar)
				// Own rock peridotite (its ledges and buttresses; approved Q2), accepting the pillow basalt and the serpentinite of the mud
				// volcanoes. LATER (I9): Basalt's pillow and Serpentinite's blue-green mud-volcano looks as Abyss's own.
				.Accepts(RockTypes.Basalt, RegionalRocks.Serpentinite)
				.CaveShell(CaveForm.Cave, "Peridotite").CaveShell(CaveForm.Grotto, "Peridotite").CaveShell(CaveForm.SeaCave, "Basalt")
				.CaveShell(CaveForm.Mine, "Peridotite").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.FireIce, 0.5f), (BiomeCaveCrystalKind.Hadalite, 0.5f))
				// The hadalite's slow cold heartbeat, in its caves and on its crystal posts and lure-orb cages.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				// Serpentinite mud volcanoes and brine lakes with a mirror surface below their rim (approved Q3).
				.Landforms("MudVolcano", "BrineLake")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				// LATER (SEA-LIFE, floor-relative depth on I2m's NearFloor kinds): hadal snailfish 5, amphipod swarms 4, giant isopods 3,
				// dumbo octopus 2, siphonophores 2, anglerfish 1, the Leviathan 0.5 (never within 80 m) and the Deepwisps 2. The kit's kinds
				// swim 1-80 m under the surface, nowhere near a hadal floor, so no sea life is named until those kinds exist.
				.NoVines()
				.NoCrops()
				.Herbs()
				// Open water (2026-10-26): the trench's sea is cold but liquid; the old Frozen moat was the cold-only envelope's.
				.Moat(BiomeMoatWater.Wet)
				// The brine lakes: a black mirror, no foam.
				.WaterTint(new BiomeWaterLook(SeaHex("#22323a"), SeaHex("#04080c"), new Vector3(0.6f, 0.3f, 0.2f), 0.15f, SeaHex("#3a342a"), SeaHex("#b8c0c8")))
				// Clear, lightless water (the sun is dimmed by depth, I2m (c)); marine snow.
				.SeaTint(new BiomeSeaLook(new Color(0.008f, 0.026f, 0.06f), 40f, 1.3f))
				.Describe("The hadal trenches — lightless walls falling past six thousand fathoms, cold seeps round brine lakes, and the bones of leviathans")
				.Names(onsets: new[] { "Grund", "Myrk", "Fathom", "Bathy", "Tenebr", "Hadh", "Leviath", "Brin" },
					nuclei: new[] { "a", "e", "i", "o", "u", "y" },
					codas: new[] { "deep", "fathom", "grund", "maw", "trench" },
					middles: new[] { "al", "er", "or", "un" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Maw", "Deep", "Seep", "Fane", "Undercroft", "Trench", "Hold" },
					dungeonPrefixes: new[] { "The Lightless", "The Fathomless", "The Pickled", "The Drowned", "The Chained", "The Bone-fallen" },
					poiSuffixes: new[] { "Seep", "Brinemere", "Bonefall", "Fane", "Chain", "Bell", "Mudmount", "Deep", "Maw" },
					adjectives: new[] { "Lightless", "Fathomless", "Pickled", "Drowned", "Chained", "Bone-fallen" },
					poiWords: new[]
					{
						("sunkenship", new[] { "Pickled Galleon", "Brine Hulk", "Wreck" }),
						("sunkenruins", new[] { "Cyclopean Fane", "Fane" }),
						("sunkencity", new[] { "Cyclopean City", "Drowned City" }),
						("sunkenshrine", new[] { "Brine Ring", "Brinemere Stones" }),
						("sunkenmonument", new[] { "The Drowned Chain", "Chain" }),
						("sunkenstatue", new[] { "Glass Bell", "Alexander's Bell" }),
						("seamonsterden", new[] { "Kraken Maw", "Maw" }),
						("seacrystalformation", new[] { "Hadalite Garden", "Heartstones" }),
					},
					about: "Drowned-liturgical mariner: the hadal trench (Grund, Myrk, Bathy, Tenebr; -deep, -fathom, -grund, -maw, -trench)")
				// What fell from the world above rests here: galleons pickled in the brine, the cyclopean fane and its city, the brine
				// ring, the kraken's maw, the Drowned Chain (at most one a scene), the glass bell and the hadalite garden (approved Q7:
				// all, rare). The trench itself (Gorge) kept; the land detections, the islands, bays and headlands zeroed.
				// LATER (I7): the named pieces — the leviathan fall (skull, vertebral row, rib vault, bone-worm plumes, amphipod swarm),
				// the brine-pickled galleon standing in a brine lake, the cyclopean fane with its fish-king statues and rose window,
				// Alexander's glass bell with its lit lantern, the Drowned Chain and its ring-stone, the brine-lake ring, the kraken's
				// maw. The kit's variants below stand in.
				.Sites((POIType.Gorge, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 0.15f),
					(POIType.SunkenShrine, 0.3f), (POIType.SeaMonsterDen, 0.3f), (POIType.SeaCrystalFormation, 0.4f),
					(POIType.SunkenMonument, 0.1f), (POIType.SunkenStatue, 0.1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f))
				.Prefers(POIType.SunkenCity, "Drowned", 0.3f)
				.Prefers(POIType.SunkenCity, "Reef Halls", 0f)
				.Prefers(POIType.SunkenShip, "Debris", 0.5f)
				.Prefers(POIType.SunkenRuins, "Stone", 0.3f)
				.Favours(BiomeStructureStyle.Stone, 2f)
				.Favours(BiomeStructureStyle.Timber, 0.6f)
				// The palette: cyclopean polygonal basalt with a manganese film, peridotite plinths, black-veined marble, black slate
				// collapsed, pressure-studded iron doors; black brine-pickled oak, rotten silver-sigilled sails, tattered banners.
				// LATER (NEW-SURFACES / Bone style): the leviathan shrines' Bone style (ivory with red bone-worms); tinted kit materials
				// stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#1e2226"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#2e3628"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, SeaHex("#2a2e32"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Marble, SeaHex("#4a4e50"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, SeaHex("#2e3628"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, SeaHex("#1e2226"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Iron, SeaHex("#4a3a2a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#141210"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#141210"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#6a3a1a"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#2a2826"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#2a2638"))
				// Nothing is built here by the living: streets unlit. The sunken sites hold lure-orb cages (photophore blue, a slow pulse);
				// the caves' routes the same.
				// LATER (I7a): lights at non-town sites are laid once I7a places them; the entries are the template's already.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital).Unlit(POIType.Port)
				.Lights(POIType.SunkenCity, BiomeWayLightKind.Lurelight, 18f)
				.Lights(POIType.SunkenRuins, BiomeWayLightKind.Lurelight, 18f)
				.Lights(POIType.SunkenShrine, BiomeWayLightKind.Lurelight, 18f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.Lurelight, 22f)
				.Gathers("Seep Mussel Bed", "Fire-Ice Crust", "Leviathan Bone", "Lure Bulb", "Hadalite")
				// Encrusted: a manganese-black film, rust rusticles on iron, white serpulid and sponge crust, ooze on every horizontal.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Encrusted, 0.9f, 1f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
