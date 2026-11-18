#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Coral Reef</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CoralReef()
		{
			// The atoll reefs of the medieval Maldives (Dhivehi Raajje; biome round, approved 2026-10-18, authored 2026-10-26):
			// turquoise lagoons over white Halimeda sand, the reef flat of cemented coral pavement where the cowries are gathered by
			// the basket, the crest of staghorn and table corals, great Porites boulders and the drop-off wall of red gorgonians
			// and black coral; coral-stone masons, cowrie gatherers, sewn bathheli ships and the drowned stupas of the Redin.
			// Hot band (t >= 0.9): the shallow flats bleach white. Fantasy: the Rannamaari lantern-ship, the Redin's reef palace.
			// Everything below is painted and grown UNDER the sea (decision 38, I2m: UnderTheSea, depth bands in metres below mean sea
			// level; the lakebed is only the coastal fade and the islets' pools).
			// LATER (I9 T1): every ground and object texture as Coral Reef's own variant of its recipe (decision 12); until then the
			// grounds are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Kauri (cowrie cream-and-honey) + Rannamaari (spawning-night green).
			return E("Coral Reef", L(Ground.SandAtoll,
					// Open sand: mushroom corals lying free, garden-eel colonies on the sand slope, leopard cucumbers, Linckia stars,
					// cowries and cone shells, shipworm-bored drift palms, a little coral rubble.
					Undersea("MushroomCoral", 1.5f, 3f, 25f), Undersea("GardenEels", 20f, 15f, 40f), Undersea("LeopardCucumber", 1f, 2f, 30f),
					Undersea("BlueLinckia", 0.8f, 2f, 20f), Undersea("ShellsReef", 3f, 0f, 0f),
					In(BiomeBedWaters.Sea, Logs("Drift palms", 0.02f, 20f, Wood("DriftPalm"))), UnderseaStones(Pebbles("CoralRubble"), 2f, 0f, 0f)),
				Ds(
					// d0: the live reef — staghorn thickets and finger corals on the crest, brain domes and Porites boulders, stalked
					// tables, plate corals shelving the slope, gorgonians and whips across the current on the drop-off, black coral,
					// soft-coral trees under the overhangs, barrel sponges, carpet anemones, urchins and giant clams.
					D(Ground.ReefFramework, 48f, 3f, 0f, 45f,
						Undersea("StaghornCoral", 14f, 1f, 15f), Undersea("FingerCoral", 8f, 0.5f, 12f), Undersea("BrainCoralReef", 3f, 3f, 25f),
						Undersea("PoritesBoulder", 1f, 2f, 20f), Undersea("TableAcropora", 2f, 3f, 15f, slopeMax: 25f),
						Undersea("PlateCoral", 3f, 10f, 35f, slopeMax: 60f, slopeMin: 15f),
						Undersea("GiantGorgonian", 1.2f, 15f, 45f, slopeMax: 90f, slopeMin: 30f), Undersea("WhipCoral", 1f, 15f, 45f, slopeMax: 90f, slopeMin: 20f),
						Undersea("BlackCoral", 0.4f, 25f, 50f, slopeMax: 90f, slopeMin: 25f),
						Undersea("TreeSoftCoral", 4f, 8f, 40f, slopeMax: 60f, slopeMin: 10f), Undersea("LeatherCoral", 3f, 1f, 15f),
						Undersea("BarrelSponge", 1f, 10f, 40f), Undersea("CarpetAnemone", 1f, 2f, 20f), Undersea("LongSpineUrchin", 3f, 0.5f, 15f),
						Undersea("CrownOfThorns", 0.3f, 3f, 20f), Undersea("GiantClamReef", 0.4f, 1f, 15f),
						UnderseaStones(SmallRocks("Coralstone"), 1f, 0f, 0f),
						// Bommies (patch-reef pinnacles) and spur-and-groove ridges.
						In(BiomeBedWaters.Sea, Outcrops("Bommies", 0.006f, 40f, F(RegionalRocks.Coralstone, "Bommie"), 40f)),
						In(BiomeBedWaters.Sea, Outcrops("Spurs", 0.01f, 15f, F(RegionalRocks.Coralstone, "Spur"), 30f))).Depth(1f, 30f),
					// d1: the rubble field below the crest — dead branch-coral rods, Halimeda, mushroom corals and Linckia stars.
					D(Ground.RubbleBranchRods, 36f, 4f, 0f, 25f, UnderseaStones(Pebbles("CoralRubble"), 10f, 0f, 0f), Undersea("Halimeda", 6f, 2f, 25f),
						Undersea("MushroomCoral", 1.5f, 3f, 25f), Undersea("BlueLinckia", 0.8f, 2f, 20f)).Depth(12f, 40f),
					// d2: the lagoon floor — powder sand, Halimeda meadows, ghost-shrimp mounds, cucumbers, patch-reef bommies.
					D(Ground.SandLagoon, 120f, 2f, 0f, 4f, Undersea("Halimeda", 6f, 2f, 25f), Undersea("ShrimpMounds", 4f, 0f, 0f),
						Undersea("LeopardCucumber", 1f, 2f, 30f), In(BiomeBedWaters.Sea, Logs("Drift palms", 0.02f, 20f, Wood("DriftPalm"))),
						In(BiomeBedWaters.Sea, Outcrops("Lagoon bommies", 0.006f, 40f, F(RegionalRocks.Coralstone, "Bommie"), 20f))).Depth(2f, 25f),
					// d3: the reef flat — cemented coral pavement in 0–2.5 m, money cowries by the thousand, finger corals, urchins,
					// microatoll rings and storm-thrown blocks.
					D(Ground.ReefFlatPavement, 40f, 5f, 0f, 6f, Undersea("MoneyCowrie", 6f, 0f, 2f), Undersea("FingerCoral", 2f, 0f, 2.5f),
						Undersea("PoritesBoulder", 0.4f, 0f, 2.5f), Undersea("LongSpineUrchin", 3f, 0f, 2.5f), Undersea("ShellsReef", 2f, 0f, 0f),
						UnderseaStones(SmallRocks("Coralstone"), 2f, 0f, 0f),
						In(BiomeBedWaters.Sea, Outcrops("Microatolls", 0.01f, 20f, F(RegionalRocks.Coralstone, "Microatoll"), 20f)),
						In(BiomeBedWaters.Sea, Blocks("Storm blocks", 0.002f, 80f, F(RegionalRocks.Coralstone, "StormBlock")))).Depth(0f, 2.5f),
					// d4: the hot band's bleached reef — white skeletons under a slime of filamentous algae.
					D(Ground.ReefBleached, 48f, 3f, 0f, 35f, Undersea("StaghornBleached", 10f, 0f, 10f)).Temp(0.9f, 1f).Depth(0f, 10f),
					// d5: the fore-reef talus — coarse grit grading to blue-grey, garden eels, plate corals, whips, barrel sponges.
					D(Ground.SandForeReef, 64f, 3f, 6f, 35f, Undersea("GardenEels", 20f, 15f, 40f), Undersea("PlateCoral", 3f, 10f, 35f, slopeMax: 60f, slopeMin: 15f),
						Undersea("WhipCoral", 1f, 15f, 45f, slopeMax: 90f, slopeMin: 20f), Undersea("BarrelSponge", 1f, 10f, 40f)).Depth(28f, 0f)),
				C(Ground.CliffDropOff, 45f), Ground.SandLagoonPowder, Ground.GravelReefGrit)
				// The coastal fade and the islets' pools: Linckia stars, cowries, the odd giant clam.
				.Under(Sea("ShellsReef", 3f, 0f, 3f), Sea("BlueLinckia", 0.5f, 0f, 3f), Sea("MoneyCowrie", 2f, 0f, 2f))
				.UnderTheSea()
				// The generic reef kit this round retired for its own variants, and the kelp and seagrass a tropical reef never had.
				.Moved("main", "Coral", "Kelp").Moved("lakebed", "Seagrass", "Kelp")
				.Moved("lakebed", "Coral", "BrainCoral", "TableCoral", "SeaFan", "Sponge", "Anemone", "Urchin", "Starfish", "Shells", "SoftCoral", "GiantClam",
					"SeaCucumber", Pebbles("Coral"))
				.Ways(path: Ground.PathSweptSand, road: Ground.FlagstoneHirigaa, cobbles: Ground.FlagstoneRagSett)
				.Bars(sand: Ground.SandForamBar, gravel: Ground.GravelCayShingle)
				// Own rock Coralstone (its formations; approved Q1), accepting the raised Feshi limestone reef and the atoll's basalt root.
				// LATER (I9): Limestone's Feshi notch ledges and Basalt's Atoll-root (below 40 m on the drop-off) as Coral Reef's own looks.
				.Accepts(RockTypes.Limestone, RockTypes.Basalt)
				.CaveShell(CaveForm.SeaCave, "Coralstone").CaveShell(CaveForm.Cave, "Coralstone").CaveShell(CaveForm.Grotto, "Limestone")
				.CaveShell(CaveForm.Mine, "Coralstone").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Apatite, 0.6f), (BiomeCaveCrystalKind.Lagoonglass, 0.4f))
				// The lagoonglass's light ripples like caustics, in its caves and on its crystal posts (decision 33's cold Glow).
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Caustic)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Caustic)
				// Faru ring reefs round their lagoons, a blue hole, kandu passes cut through the rings (approved Q2).
				.Landforms("Faru", "BlueHole", "Kandu")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				// LATER (SEA-LIFE N4/N5/N12/N13 + the Eel shape): bumphead parrotfish 1 (reef fish stand in), moray 0.6 (lurking, I2m's Eel
				// + Lurk), whale shark 0.2 (the kit's shark stands in), spinner dolphin 0.4, Kandu-fureta 0.1 (pale sea-spirit, passes by
				// night). The kit's reef fish carry the parrotfish's weight and the shark the whale shark's.
				.HomeTo(("Reef fish", 7f), ("Baitfish", 3f), ("Jacks", 1.5f), ("Shark", 1.2f), ("Manta", 1.2f), ("Sea turtle", 2f), ("Crab", 1f), ("Jellyfish", 0.4f))
				.NoVines()
				.NoCrops()
				.Herbs()
				.Moat(BiomeMoatWater.Wet)
				// The islets' pools and moats: clear tropical turquoise over white sand.
				.WaterTint(new BiomeWaterLook(SeaHex("#6ad0c8"), SeaHex("#0a5a78"), new Vector3(0.40f, 0.06f, 0.04f), 0.05f, SeaHex("#e0d8c2"), SeaHex("#f6f8f8")))
				// Clear tropical turquoise. LATER (I2k column): the coastal surface colour #2aa6b4.
				.SeaTint(new BiomeSeaLook(new Color(0.02f, 0.33f, 0.40f), 35f, 0.7f))
				.Describe("Warm atoll shallows — coral walls in every colour over white sand, cowrie flats, and the drowned stupas of the Redin")
				.Names(onsets: new[] { "Fu", "Hu", "Thi", "Dhi", "Ma", "Ka", "Ra", "Fe", "Vaa", "Kan", "Mii", "Hi", "Gaa", "Faa" },
					nuclei: new[] { "a", "e", "i", "o", "u", "aa" },
					codas: new[] { "dhoo", "fushi", "huraa", "giri", "faru", "kandu", "falhu" },
					middles: new[] { "la", "ri", "dhu", "mu", "ve" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Havitta", "Falhu", "Kandu", "Hirigaa Hall", "Grotto", "Undercroft", "Giri", "Reef Cave" },
					dungeonPrefixes: new[] { "The Drowned", "The Coral-crowned", "The Cowrie-strewn", "The Sunlit", "The Redin", "The Lantern-lit" },
					poiSuffixes: new[] { "Faru", "Giri", "Kandu", "Falhu", "Havitta", "Hirigaa", "Boli", "Dhoo", "Huraa", "Fushi" },
					adjectives: new[] { "Coral-crowned", "Cowrie-strewn", "Sunlit", "Drowned", "Lantern-lit", "Turquoise" },
					poiWords: new[]
					{
						("bay", new[] { "Lagoon", "Haven", "Faru" }),
						("headland", new[] { "Point", "Giri" }),
						("island", new[] { "Dhoo", "Huraa", "Fushi", "Islet" }),
						("rivermouth", new[] { "Mouth", "Kandu" }),
						("sunkenship", new[] { "Bathheli Wreck", "Dhow Wreck", "Hulk" }),
						("sunkenruins", new[] { "Havitta", "Hirigaa Hall" }),
						("sunkencity", new[] { "Redin Palace", "Reef Halls" }),
						("sunkenshrine", new[] { "Offering Ring", "Lamp Pillars" }),
						("sunkenmonument", new[] { "Lomafanu", "Copper Plates" }),
						("sunkenstatue", new[] { "Redin Head", "Tall King" }),
						("seamonsterden", new[] { "Moray Grotto", "Giri Cleft" }),
						("seacrystalformation", new[] { "Lagoonglass", "Glass Garden" }),
						("fishingcamp", new[] { "Fish Weir", "Cowrie Lines", "Hirigaa Quarry" }),
					},
					about: "Dhivehi: the Maldive atolls (Fu, Hu, Thi, Dhi, Ma; -dhoo, -fushi, -huraa, -giri, -faru, -kandu, -falhu)")
				// The shelf's sites: wrecks, havittas and coral halls, the Redin's reef palace (rare, at most one a scene), offering rings,
				// the copper-plate stand, a Redin head, a moray's grotto, the lagoonglass garden; over the flats the fish weirs, cowrie
				// lines and quarry. Island, Bay, Headland, the river mouths and the reef itself kept; the land detections zeroed.
				// LATER (I7): the named pieces — the drowned havitta, the coral-stone hall, the Redin reef palace re-dressed from "Reef
				// Halls", the bathheli and sewn-hull wrecks with cowrie drifts, the offering ring, the Lomafanu stand, the Redin head, the
				// moray grotto, fish weirs and cowrie lines over the water (I21 over-water placement), the hirigaa quarry, kandu cairns,
				// the harbours' coral-block jetties; sea lanes: bathheli 1.0, masdhoni 1.0 and bokkura inshore (I13) and the Rannamaari
				// phantom lantern-ship, one night a lunar cycle, fading within 400 m (I14). The kit's variants below stand in.
				.Sites((POIType.RiverMouth, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.CoralReef, 1f),
					(POIType.FishingCamp, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 0.2f),
					(POIType.SunkenShrine, 0.3f), (POIType.SunkenMonument, 0.2f), (POIType.SunkenStatue, 0.15f), (POIType.SeaMonsterDen, 0.25f),
					(POIType.SeaCrystalFormation, 0.15f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f), (POIType.MerfolkVillage, 1.4f))
				.Prefers(POIType.SunkenCity, "Reef Halls", 2f)
				.Prefers(POIType.SunkenCity, "Drowned", 0.2f)
				.Favours(BiomeStructureStyle.Stone, 1.6f)
				.Favours(BiomeStructureStyle.Timber, 1f)
				// The palette: carved Porites coral-stone walls, lime-wash, rag plinths and causeways, lacquered red-black-yellow timber,
				// plaited-frond roofs, bronze gone green; coconut-palm timber, palm-mat sails, the sultan's plain red banner.
				// LATER (NEW-SURFACES U1/U4): Hirigaa, Lacquerwood and the Cadjan thatch as surfaces of their own, coir rope and the
				// dhandimathi banner; tinted stone, timber and thatch stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#e4dcc4"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#e4dcc4"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, SeaHex("#e4dcc4"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#a29884"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Stone, SeaHex("#c8bea6"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, SeaHex("#a08a5a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#a02018"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#6a4a2e"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, SeaHex("#a08a5a"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#a02018"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, SeaHex("#8a7650"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#8a6a30"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#b09a6a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#b01c1c"))
				// No towns stand on the shelf (the land biomes build them): their streets unlit. The weirs, quarry and harbours burn
				// niche lamps; the sunken sites and the caves' routes crystal posts holding lagoonglass.
				// LATER (I7a): lights at non-town sites are laid once I7a places them; the entries are the template's already.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital).Unlit(POIType.Port)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.NicheLamp, 16f)
				.Lights(POIType.SunkenCity, BiomeWayLightKind.CrystalPost, 22f)
				.Lights(POIType.SunkenRuins, BiomeWayLightKind.CrystalPost, 22f)
				.Lights(POIType.SunkenShrine, BiomeWayLightKind.CrystalPost, 22f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 22f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.NicheLamp, 14f)
				.Gathers("Pearl Oyster Bed", "Hirigaa Block", "Money Cowrie", "Black Coral", "Apatite", "Lagoonglass")
				// Reefgrown: living coral heads and soft corals on every stone, crinoids on the edges, sponges in the joints.
				// LATER (I7): the harbour pieces' lighter coat (0.3); one cover for every piece today.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Reefgrown, 0.9f, 1f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
