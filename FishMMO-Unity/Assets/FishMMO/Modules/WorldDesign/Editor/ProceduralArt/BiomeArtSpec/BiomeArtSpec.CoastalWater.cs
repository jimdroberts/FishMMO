#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Coastal Water</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CoastalWater()
		{
			// The Celtic Atlantic shelf of Armorica, Cornwall and west Wales (biome round, approved 2026-10-18, authored 2026-10-26):
			// bottle-green water over dark dolerite reefs roofed by a stiff-stemmed cuvie kelp forest, pink maerl gravel, tide-swept
			// cobble grounds, scallop and flat-oyster beds, jewel-anemone walls, brittlestar carpets below 25 m and the black peat
			// and oak stumps of a drowned forest. Cold band (t ≤ −0.4): sugar kelp and purple urchin barrens; warm band (t ≥ 0.45):
			// golden kelp and rainbow wrack. Fantasy: drowned Ker-Ys and its bells, the sarn causeways, Morgens in the kelp.
			// Everything below is painted and grown UNDER the sea (decision 38: UnderTheSea, depth bands in metres below mean sea
			// level; the lakebed is only the coastal fade and the rock pools).
			// LATER (I9 T1): every ground and object texture as Coastal Water's own variant of its recipe (decision 12); until then
			// the grounds are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Dahut (violet-blue) + Maerlrose (coralline rose).
			return E("Coastal Water", L(Ground.SandShellGravel,
					// Open floors: scallop grounds, sea-potato tests, Atlantic shells, storm-torn kelp in the gullies, the odd maerl.
					Undersea("MusselBedScallop", 3f, 8f, 40f), Undersea("SeaPotato", 1.5f, 2f, 20f), Undersea("ShellsAtlantic", 3f, 0f, 0f),
					Undersea("KelpDrift", 2f, 0f, 0f), Undersea("MusselBedFlatOyster", 2f, 2f, 15f), UnderseaStones(Pebbles("Maerl"), 2f, 0f, 0f)),
				Ds(
					// d0: the kelp reef — dark dolerite under coralline paint, roofed by the cuvie forest (furbelows among it, thongweed on
					// the shallow fringe, red weeds under the canopy), jewel anemones and pink sea fans on its faces.
					D(Ground.DoleriteKelpReef, 64f, 3f, 4f, 60f,
						Band(Undersea("KelpCuvie", 18f, 0f, 20f), -0.4f, 0.45f), Band(Undersea("KelpCuvie", 9f, 0f, 20f), 0.45f, 1f),
						Band(Undersea("KelpGolden", 12f, 0f, 20f), 0.45f, 1f), Band(Undersea("KelpSugar", 10f, 0f, 15f), -1f, -0.4f),
						Undersea("KelpFurbelows", 3f, 0f, 12f), Undersea("Thongweed", 8f, 0f, 4f), Band(Undersea("RainbowWrack", 6f, 0f, 6f), 0.45f, 1f),
						Undersea("RedWeedTurf", 20f, 10f, 35f), Undersea("Carrageen", 8f, 0f, 6f), Undersea("Dulse", 5f, 0f, 10f),
						Undersea("PinkSeaFan", 1.5f, 15f, 40f, slopeMin: 15f), Undersea("RossCoral", 1f, 20f, 45f), Undersea("DeadMansFingers", 2f, 10f, 40f),
						// The walls' colour: jewel-anemone carpets only on steep rock (decision 38's slopeMin); the cliff texture carries the rest.
						Undersea("JewelAnemone", 4f, 6f, 40f, slopeMax: 60f, slopeMin: 35f),
						Undersea("EdibleUrchin", 2f, 3f, 30f), Undersea("CushionStar", 0.6f, 0f, 30f), Undersea("BarnaclesCeltic", 4f, 0f, 3f),
						UnderseaStones(SmallRocks("Dolerite"), 1.5f, 0f, 0f),
						// Gully reefs parallel to the swell, and drowned pinnacles rising to a few metres under the surface.
						In(BiomeBedWaters.Sea, Outcrops("Gully reefs", 0.008f, 30f, F(RegionalRocks.Dolerite, "Ridge", "Gully"), 40f)),
						In(BiomeBedWaters.Sea, Outcrops("Drowned pinnacles", 0.002f, 120f, F(RegionalRocks.Dolerite, "Pinnacle"), 40f)),
						In(BiomeBedWaters.Sea, Outcrops("Pink tors", 0.003f, 60f, F(RockTypes.Granite, "Tor", "Corestone"), 30f))).Depth(0f, 25f),
					// d1: the maerl bed — pink-lilac nodule gravel on the flat floors of the lit zone.
					D(Ground.Maerl, 48f, 4f, 0f, 8f, UnderseaStones(Pebbles("Maerl"), 12f, 4f, 30f), Undersea("ShellsAtlantic", 2f, 4f, 30f),
						Undersea("SunStar", 0.6f, 4f, 30f)).Depth(4f, 30f),
					// d2: tide-swept cobble ground of the straits and headlands, sponge-crusted, with its own corals, anemones and beds.
					D(Ground.CobbleTideSwept, 30f, 5f, 0f, 15f, Undersea("RedWeedTurf", 10f, 10f, 35f), Undersea("PinkSeaFan", 1.5f, 15f, 40f),
						Undersea("RossCoral", 1f, 20f, 45f), Undersea("DeadMansFingers", 3f, 10f, 40f), Undersea("PlumoseAnemone", 2f, 10f, 40f),
						Undersea("DahliaAnemone", 1.5f, 3f, 25f), Undersea("SunStar", 0.6f, 0f, 30f), Undersea("MusselBedFlatOyster", 4f, 2f, 15f),
						Undersea("MusselBedHorse", 2f, 10f, 50f), Undersea("KelpDrift", 3f, 0f, 0f), Band(Undersea("KelpSugar", 10f, 0f, 15f), -1f, -0.4f),
						UnderseaStones(SmallRocks("Dolerite"), 3f, 0f, 0f),
						In(BiomeBedWaters.Sea, Blocks("Dolerite boulder fields", 0.01f, 20f, F(RegionalRocks.Dolerite, "BoulderField")))),
					// d3: circalittoral muddy sand below 25 m, carpeted by brittlestars.
					D(Ground.SiltBrittlestar, 64f, 2f, 0f, 6f, Undersea("BrittleStarBed", 30f, 25f, 60f), Undersea("MusselBedHorse", 2f, 25f, 50f),
						Undersea("SunStar", 0.4f, 25f, 60f)).Depth(25f, 0f),
					// d4: honeycomb-worm reef at the shallow sand edge.
					D(Ground.SandHoneycomb, 24f, 5f, 0f, 12f, Undersea("SabellariaHummock", 3f, 0f, 6f), Undersea("DahliaAnemone", 1f, 3f, 6f)).Depth(0f, 6f),
					// d5: the drowned forest's peat, in rare patches, its rooted oak and hazel stumps piddock-bored.
					// LATER (I9 bark variants): black, weed-capped stumps; the catalogue's broken stump and root plate stand in.
					D(Ground.PeatLyonesse, 120f, 6f, 0f, 8f, In(BiomeBedWaters.Sea, Logs("Lyonesse stumps", 0.03f, 20f, Wood("Stump_Broken", "RootPlate"))),
						Undersea("KelpDrift", 1f, 0f, 8f)).Depth(0f, 8f),
					// d6: the cold band's urchin barrens — grazed coralline pavement, purple urchins.
					D(Ground.CoralBarren, 40f, 3f, 0f, 40f, Undersea("PurpleUrchin", 14f, 3f, 30f), Undersea("EdibleUrchin", 2f, 3f, 30f))
						.Temp(-1f, -0.4f).Depth(3f, 30f)),
				C(Ground.DoleriteSeaCliff, 50f), Ground.SandSurfSorted, Ground.GravelSlateQuartz)
				// The coastal fade and the rock pools.
				.Under(Sea("Carrageen", 3f, 0f, 3f), Sea("CushionStar", 0.5f, 0f, 3f), Sea("ShellsAtlantic", 2f, 0f, 0f), Sea("BarnaclesCeltic", 2f, 0f, 2f))
				.UnderTheSea()
				// The generic shelf kit this round retired (the seaweed, kelp, urchins, stars, shells, anemones, mussels, barnacles, sea
				// lettuce, sand dollars and grey stones), switched off where the tool wrote them and nobody tuned them since.
				.Moved("detail/0", "Kelp", SmallRocks("Grey")).Moved("lakebed", "Seagrass")
				.Moved("lakebed", "Seaweed", "Kelp", "Urchin", "Starfish", "Shells", "Anemone", SmallRocks("Grey"), "MusselBed", "Barnacles", "SeaLettuce", "SandDollar")
				.Ways(path: Ground.RockSkerry, road: Ground.FlagstoneSarn, cobbles: Ground.FlagstonePave)
				.Bars(sand: Ground.SandMaerlBar, gravel: Ground.GravelKillas)
				// Own rock dolerite (its formations; approved Q1), accepting the pink granite and the killas slate.
				// LATER (I9): Granite's pink tafoni and Dolerite's spotted bluestone as Coastal Water's own looks.
				.Accepts(RockTypes.Granite, RockTypes.Slate)
				.CaveShell(CaveForm.SeaCave, "Dolerite").CaveShell(CaveForm.Cave, "Dolerite").CaveShell(CaveForm.Grotto, "Granite")
				.CaveShell(CaveForm.Mine, "Granite").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Cassiterite, 0.6f), (BiomeCaveCrystalKind.Bellspar, 0.4f))
				// The drowned princess's sarn causeways running out from the shore (approved Q3).
				.Landforms("Sarn")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				// LATER (SEA-LIFE N4/N5/N12/N13): wrasse 3, lobster 1, cuttlefish 0.8, grey seal 0.8, bottlenose dolphin 0.4, a true basking
				// shark (the kit's shark stands in at its weight) and the Morgen 0.1; warm band octopus 0.6. Lobsters join the crabs' weight.
				.HomeTo(("Baitfish", 3f), ("Jacks", 1.5f), ("Crab", 3f), ("Jellyfish", 1f), ("Shark", 0.3f))
				.NoVines()
				.NoCrops()
				.Herbs()
				.Moat(BiomeMoatWater.Wet)
				// The islets' rock pools and moats: Atlantic-clear, a little turbid, green in the shallows.
				.WaterTint(new BiomeWaterLook(SeaHex("#3e8a7a"), SeaHex("#0a2a34"), new Vector3(0.42f, 0.08f, 0.06f), 0.15f, SeaHex("#8a8574"), SeaHex("#eef2f0")))
				// Atlantic bottle-green. LATER (I2k column): the coastal surface colour slate-green #2e5652.
				.SeaTint(new BiomeSeaLook(new Color(0.04f, 0.24f, 0.27f), 15f, 1.1f))
				.Describe("Atlantic shallows — kelp forests over dark reefs, pink maerl, and the bells of a drowned city under the swell")
				.Names(onsets: new[] { "Ker", "Pen", "Tre", "Lan", "Plou", "Porz", "Gwen", "Mor", "Enez", "Lok", "Kast", "Bod" },
					nuclei: new[] { "a", "e", "o", "ou", "eu", "i" },
					codas: new[] { "mor", "ven", "ec", "arvor", "ow", "an", "ell", "oc" },
					middles: new[] { "ar", "el", "en", "ou" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Toull", "Zawn", "Sea Cave", "Cleft", "Hold", "Gully", "Gwaelod", "Undercroft", "Bell-vault" },
					dungeonPrefixes: new[] { "The Drowned", "The Tolling", "The Weed-hung", "The Kelp-dark", "The Bottle-green", "The Tide-swept", "The Bell-haunted" },
					poiSuffixes: new[] { "Karreg", "Beg", "Sarn", "Men", "Toull", "Gwaelod", "Kelp-forest", "Maerl-bed", "Scallop-ground", "Oyster-parc" },
					adjectives: new[] { "Drowned", "Tolling", "Weed-hung", "Kelp-dark", "Bottle-green", "Maerl-pink", "Tide-swept", "Bell-haunted" },
					poiWords: new[]
					{
						("bay", new[] { "Porz", "Cove", "Haven", "Aber" }),
						("headland", new[] { "Beg", "Penn", "Point" }),
						("island", new[] { "Enez", "Skerry", "Islet" }),
						("rivermouth", new[] { "Aber", "Mouth" }),
						("sunkenship", new[] { "Wreck", "Drowned Keel", "Hulk" }),
						("sunkenruins", new[] { "Bell-chapel", "Drowned Chapel", "Sluice" }),
						("sunkencity", new[] { "Ker", "Drowned City" }),
						("sunkenshrine", new[] { "Stone Boat", "Drowned Cross" }),
						("sunkenmonument", new[] { "Men", "Ogham Stone" }),
						("sunkenstatue", new[] { "Morgen", "Sea-maid" }),
						("seamonsterden", new[] { "Conger Cleft", "Toull" }),
						("seacrystalformation", new[] { "Ringing Stones", "Bell-stones" }),
						("fishingcamp", new[] { "Bouchots", "Oyster Parc" }),
					},
					about: "Breton–Cornish: the drowned lands of the Atlantic shelf (Ker, Pen, Tre, Lan; -mor, -ven, -ec, -arvor)")
				// Under the sea: wrecks, drowned chapels, the drowned city, a saint's stone boat, an ogham stone, a Morgen's statue, a
				// conger's cleft and the bellspar garden; over the shallows the mussel bouchots and oyster parcs (approved Q7: all, rare).
				// Island, Bay, Headland and the river mouths kept; the land detections zeroed.
				// LATER (I7): the named pieces — Ker-Ys with its open bronze sea-gate and tolling bells (≤1 a scene, site audio cue on the
				// shared clock), the bell-chapel, the Gwaelod sluice at a sarn's tip, the longship, the Gascon wine cog, the pilgrim ship,
				// the stone boat, the ogham stone, the Morgen statue, the conger cleft, bouchot rows and oyster parcs over the water
				// (I21 over-water placement), seamark perches on the reef crests, the harbours' mooring stones and creels, and the sea
				// lanes' balinger, wine cog and goémonier kelp boats (I13). The kit's variants below stand in.
				.Sites((POIType.RiverMouth, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.FishingCamp, 0.8f),
					(POIType.SunkenShip, 1.2f), (POIType.SunkenRuins, 0.8f), (POIType.SunkenCity, 0.15f), (POIType.SunkenShrine, 0.3f),
					(POIType.SeaMonsterDen, 0.2f), (POIType.SeaCrystalFormation, 0.15f), (POIType.SunkenMonument, 0.2f), (POIType.SunkenStatue, 0.15f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f), (POIType.MerfolkVillage, 1f))
				.Prefers(POIType.SunkenCity, "Drowned", 1f)
				.Prefers(POIType.SunkenCity, "Reef Halls", 0f)
				.Favours(BiomeStructureStyle.Stone, 1.5f)
				.Favours(BiomeStructureStyle.Timber, 1.2f)
				// The palette: pink granite ashlar, dark Kersanton carving, blue-grey killas slate, dolerite plinths, pavé setts, a bronze
				// sea-gate; tarred oak, tan sails, tarred hemp, ermine and black-and-white banners.
				// LATER (NEW-SURFACES U4/U6): Kersanton stone and bronze as surfaces of their own; tinted stone and iron stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#9a7a70"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#3a3e3a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, SeaHex("#3a3e3a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Slate, SeaHex("#4a5058"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#2a3430"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, SeaHex("#8a746a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Iron, SeaHex("#6a5a2a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#1e1a14"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#1e1a14"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#7a3a1a"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#8a5a30"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#f0ece8"))
				// No towns stand in the water (the land biomes build them): their streets unlit here. The drowned sites glow with
				// bellglows, the bouchots and perches burn cressets; the sea caves' routes bellglows, the islets' tin adits wall lanterns.
				// LATER (I7a): lights at non-town sites are laid once I7a places them; the entries are the template's already.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital).Unlit(POIType.Port)
				.Lights(POIType.SunkenCity, BiomeWayLightKind.Bellglow, 22f)
				.Lights(POIType.SunkenRuins, BiomeWayLightKind.Bellglow, 22f)
				.Lights(POIType.SunkenShrine, BiomeWayLightKind.Bellglow, 22f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.Cresset, 16f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.Bellglow, 22f)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 10f)
				.Gathers("Kelp Holdfast", "Flat Oyster Bed", "Scallop Bed", "Carrageen", "Dulse", "Maerl", "Cassiterite", "Bellspar")
				// Coralline: pink crust on every stone face, barnacles near the top, kelp holdfasts and red-weed fringes (no green algae).
				// LATER (I7): the harbour pieces' lighter coat (0.4); one cover for every piece today.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Coralline, 0.9f, 1f)
				.ErodesAs("Seabed");
		}

		/// <summary>
		/// A sea-floor rule on a sea-floor biome's own main or detail ground (decision 36 (a)/38: <see cref="Entry.UnderTheSea"/>), grown under
		/// the sea between <paramref name="shallowest"/> and <paramref name="deepest"/> metres (0 open); shared by the sea-floor files.
		/// </summary>
		private static Scatter Undersea(string detail, float density, float shallowest, float deepest, int coverage = 180, float slopeMax = 40f, float slopeMin = 0f)
			=> In(BiomeBedWaters.Sea, Sea(detail, density, shallowest, deepest, coverage, slopeMax, slopeMin));

		/// <summary>Small stones on a sea-floor biome's own ground under the sea (<see cref="Undersea"/>'s stones).</summary>
		private static Scatter UnderseaStones(string prefab, float density, float shallowest, float deepest)
			=> In(BiomeBedWaters.Sea, SeaStones(prefab, density, shallowest, deepest));

		/// <summary>An sRGB colour from a hex string (shared by the sea-floor files).</summary>
		private static Color SeaHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
