#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ocean</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Ocean()
		{
			// The shelf edge and the continental slope of the Bay of Biscay (biome round, approved 2026-10-25, authored 2026-10-26):
			// green upwelling water where the shelf falls away, slicks marching across the swell, gannets and whales over the edge;
			// below, Nephrops mud, shell sand at the break, Lophelia coral mounds with bubblegum coral on their crests, pockmark
			// fields, honey sandstone ledges and mudstone scarps; in the cold band (t < −0.3) sponge-spicule grounds and old iceberg
			// plough marks. Its people the medieval Basque and Cantabrian whalers and deep-line fishermen; its fantasy Herensuge's
			// bones down the slope, the lamiak's grotto, Itsasgizon, Gaueko's black whaleboat in the night fog, the galerna-stone.
			// Painted and grown under the sea (decision 38/69). The draft's depths are Earth metres; the scene's floor lies at Earth
			// depth × the scene's vertical scale (Arthis 0.3575), and the bands below are SCENE metres (200 m Earth → 72 m).
			// LATER (I9 T1): every ground and object texture as Ocean's own variant (decision 12); until then its own recipes.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Whalelamp (whale-oil amber) + Lamiamilk (milk-jade).
			BiomeLandform mounds = BiomeLandformPresets.Named("CoralMounds");
			mounds.Depth = new Vector2(72f, 430f);
			BiomeLandform ploughs = BiomeLandformPresets.Named("PloughMarks");
			ploughs.Depth = new Vector2(72f, 180f);
			return E("Ocean", L(Ground.MudNephrops,
					// Nephrops ground: burrow craters thinning out down the slope, phosphorescent sea pens, Lophelia on the mound tops.
					// LATER (I7): the whalers' lost gear (tangled longlines, a broken harpoon, a sunk cask) round the wrecks.
					Undersea("NephropsBurrow", 6f, 0f, 286f), Undersea("PhosphorPen", 1.5f, 0f, 0f),
					OnLandform(Undersea("Lophelia", 3f, 72f, 430f), BiomeLandformPart.Top, "CoralMounds"),
					UnderseaStones(SmallRocks("Mudstone"), 0.5f, 0f, 0f)),
				Ds(
					// d0: shelf-edge shell sand at the break, feather-star fields on it, pale bank granite above the deeper slope.
					D(Ground.SandShelfEdge, 96f, 3f, 0f, 8f, Undersea("FeatherStar", 4f, 0f, 160f),
						In(BiomeBedWaters.Sea, Outcrops("Bank boulders", 0.002f, 80f, F(RockTypes.Granite, "Whaleback")))).Depth(0f, 160f),
					// d1: the Lophelia rubble apron on the mounds' flanks (the CoralMounds landform paints it there; its own patches
					// elsewhere at the slope's coral depths), thickets and their grazers, moundstone framestone showing through.
					D(Ground.CoralRubble, 48f, 5f, 0f, 30f, Undersea("Lophelia", 3f, 72f, 430f), Undersea("Madrepora", 1f, 72f, 430f),
						Undersea("BubblegumCoral", 0.4f, 72f, 430f, slopeMin: 10f), Undersea("EarSponge", 0.6f, 0f, 0f), Undersea("PencilUrchin", 0.8f, 0f, 0f),
						Undersea("PlatedCushionStar", 0.4f, 0f, 0f), Undersea("Bolocera", 0.5f, 0f, 0f),
						UnderseaStones(Pebbles("CoralRubble"), 4f, 0f, 0f), UnderseaStones(SmallRocks("CoralRubble"), 1.5f, 0f, 0f),
						In(BiomeBedWaters.Sea, Outcrops("Moundstone", 0.008f, 30f, F(RegionalRocks.Moundstone, "MoundCore", "Slabs"), 30f))).Depth(72f, 430f),
					// d2: the pockmarks' reduced black floors, only where the Pockmarks landform paints them.
					D(Ground.MudPockmark, 64f, 2f, 0f, 20f).Zone(BiomeLandformPart.Floor, "Pockmarks"),
					// d3: the cold band's ostur sponge-spicule felt with its boreal ball sponges.
					D(Ground.SpiculeMat, 80f, 3f, 0f, 15f, Undersea("Geodia", 2f, 143f, 536f)).Temp(-1f, -0.3f).Depth(143f, 536f),
					// d4: the cold band's iceberg plough-mark diamicton, only in the furrows the PloughMarks landform carves.
					D(Ground.GravelDiamicton, 64f, 2f, 0f, 20f, UnderseaStones(Pebbles("Striated"), 2f, 0f, 0f), UnderseaStones(SmallRocks("Striated"), 1f, 0f, 0f))
						.Temp(-1f, -0.3f).Zone(BiomeLandformPart.Whole, "PloughMarks"),
					// d5: honey-grey turbidite sandstone ledges on the steeper slope, sponges and corals on them, mudstone blocks below
					// the scarps.
					D(Ground.SandstoneIgueldo, 64f, 4f, 20f, 45f, Undersea("EarSponge", 0.6f, 0f, 0f, slopeMax: 50f), Undersea("Bolocera", 0.5f, 0f, 0f, slopeMax: 50f),
						Undersea("BubblegumCoral", 0.4f, 0f, 0f, slopeMax: 50f, slopeMin: 10f), Undersea("Lophelia", 0.5f, 72f, 430f, slopeMax: 60f, slopeMin: 15f),
						UnderseaStones(Pebbles("Sandstone"), 1f, 0f, 0f),
						In(BiomeBedWaters.Sea, Blocks("Slope ledges", 0.01f, 30f, F(RockTypes.Sandstone, "Ledges"))),
						In(BiomeBedWaters.Sea, Blocks("Scarp blocks", 0.004f, 60f, F(RegionalRocks.Mudstone, "Block", "SlopeLedge"))))),
				C(Ground.MudstoneScarp, 42f), Ground.MudShelfRim, Ground.GravelGullyChip)
				// The coastal fade (islands' shores) only.
				.Under(Sea("FeatherStar", 1f, 0f, 0f), Sea("PlatedCushionStar", 0.3f, 0f, 0f))
				.UnderTheSea()
				.SitesBetween(60f, 200f)
				// The shelf kit this round retired (Coastal Water's): kelp, seaweed, sand dollars, mussels, shells, holothurians,
				// brittle stars, the generic sponges, sea fans, urchins, stars and sea pens, the grey pebbles and rocks.
				.Moved("main", "Kelp")
				.Moved("lakebed", "Kelp", "Seaweed", "Sponge", "SeaFan", "Urchin", "Starfish", "Shells", "SandDollar", "SeaPen", "MusselBed", "SeaCucumber",
					"BrittleStar", Pebbles("Grey"), SmallRocks("Grey"))
				.Ways(path: Ground.PebblesCoralGrit, road: Ground.FlagstoneGaltzada, cobbles: Ground.PebblesArriBiribil)
				.Bars(sand: Ground.SandCoralGrit, gravel: Ground.GravelSandstoneChip)
				// Own rock mudstone (its formations; approved Q2), accepting the Igueldo sandstone and the banks' granite.
				// LATER (I9): Sandstone's Igueldo, Granite's Bank and Limestone's Urgonian looks as Ocean's own.
				.Accepts(RockTypes.Sandstone, RockTypes.Granite)
				.CaveShell(CaveForm.Cave, "Limestone").CaveShell(CaveForm.Grotto, "Limestone").CaveShell(CaveForm.SeaCave, "Sandstone")
				.CaveShell(CaveForm.Mine, "Mudstone").CaveShell(CaveForm.IceCave, "Granite").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Siderite, 0.6f), (BiomeCaveCrystalKind.GalernaStone, 0.3f))
				// The galerna-stone's warm glow brightens as the wind rises (decisions 64/69), in its caves and on its crystal posts.
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Wind, warm: true)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Wind, warm: true)
				// Lophelia mounds along the slope's contour (72–430 m in the scene), pockmark fields, and plough marks where the sea is
				// cold (approved Q3).
				.Landforms(mounds, BiomeLandformPresets.Named("Pockmarks"), ploughs)
				// Lost cork-float strings everywhere; wave-rounded growlers only on the coldest seas (their own gate, ≤ 5 °C).
				.FloatsAtSea((BiomeSeaFloatKind.Corkline, 0.3f), (BiomeSeaFloatKind.Growler, 1f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				// LATER (SEA-LIFE N12/N13 + kinds on I2p's shapes): lanternfish 3 (DielMigrate, photophore glow), Biscay anchovy as its own
				// baitfish, hake 2 and besugo 1.5 (the kit's jacks stand in at their sum), right whale 1.2 and humpback 0.8 (the kit's
				// whale at their sum), Risso's dolphin 0.8, Cuvier's beaked whale 0.3, thresher 0.3 and porbeagle 0.4 (the kit's shark),
				// the mauve stinger 1 (the kit's jellyfish), northern bottlenose whale 0.3, Itsasgizon 0.05; on the floor blackbelly
				// rosefish 1, orange roughy 0.8, Norway lobster 1, squat lobster 1.
				// LATER (AMBIENT-LIFE): gannets plunging over bait, fulmars, kittiwakes (cold band) on I2p's offshore gate; the Boil cast.
				.HomeTo(("Baitfish", 3f), ("Jacks", 3.5f), ("Whale", 2f), ("Shark", 0.7f), ("Jellyfish", 1f))
				.NoVines()
				.NoCrops()
				.Herbs()
				.Moat(BiomeMoatWater.Wet)
				// Shelf-edge slate-teal, upwelled. LATER (I2k column): surface colour #173f52 (cold band #22423e); LATER (WATER C16):
				// internal-wave slicks, the shelf-break foam front, the spring coccolith bloom.
				.SeaTint(new BiomeSeaLook(new Color(0.02f, 0.15f, 0.22f), 30f, 0.8f))
				.Describe("The slope sea where the shelf falls away — green upwelling water, slicks across the swell, whales and gannets over "
					+ "the edge, Lophelia mounds below, and Basque whaleboats out after the sarda")
				.Names(onsets: new[] { "Itsas", "Ur", "Ibar", "Arra", "Bel", "Gor", "Zur", "Lek", "Ber", "Mut", "Ond", "Gaz", "Ola", "Txo", "Sak" },
					nuclei: new[] { "a", "e", "i", "o", "u", "ai", "ea" },
					codas: new[] { "eta", "aga", "ola", "tza", "ondo", "arri", "tegi", "zabal", "ur" },
					middles: new[] { "ar", "er", "ol", "ur", "an" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Leize", "Hondo", "Sakon", "Hold", "Grotto", "Scarp", "Cleft", "Deep" },
					dungeonPrefixes: new[] { "The Shelf-edge", "The Whale-roaded", "The Galerna-struck", "The Slick-banded", "The Fathom-green", "The Drowned" },
					poiSuffixes: new[] { "Hondo", "Ertz", "Leize", "Sakon", "Balea", "Talaia", "Galerna", "Harkaitz", "Mound", "Slope" },
					adjectives: new[] { "Shelf-edge", "Whale-roaded", "Galerna-struck", "Slick-banded", "Fathom-green", "Upwelling", "Deep-lined" },
					poiWords: new[]
					{
						("island", new[] { "Uharte", "Harkaitz", "Islet" }),
						("sunkenship", new[] { "Nao Wreck", "Pinaza Wreck", "Whaler's Hulk" }),
						("sunkenruins", new[] { "Drowned Ermita", "Stelae" }),
						("sunkenshrine", new[] { "Lamien Leizea", "Grotto" }),
						("sunkenmonument", new[] { "Herensuge's Bones", "Dragon Bones" }),
						("seamonsterden", new[] { "Itsasgizon's Hold", "Thief's Hold" }),
						("seacrystalformation", new[] { "Galerna-stones", "Storm-stones" }),
						("longline", new[] { "Besuguera", "Longline" }),
						("whaletow", new[] { "Whale Tow", "Sarda Tow" }),
					},
					about: "Basque and Cantabrian: the whalers' slope sea (Itsas, Ur, Ibar; -eta, -aga, -ola, -tza, -ondo, -arri)")
				// The wrecks of naos and pinazas over the mounds, the longlines and whale tows adrift, Herensuge's bones, the lamiak's
				// grotto, Itsasgizon's hold, the galerna-stone garden and a drowned ermita on a bank crest (approved Q8/Q9: all, rare);
				// Island kept, every land detection, Bay, Headland and Gorge zeroed (canyons are Underwater Canyon's).
				// LATER (I7): the named pieces — the nao and pinaza wrecks with their barricas, try-pot and harpoons, Herensuge's
				// seven-headed skeleton (≤1 a scene), the lamien leizea with its combs and spindles, the ermita's hilarri stelae, the
				// besuguera longline (boardable) and the whale tow with its txalupas; the sea lanes' pinaza 1.4, txalupa 1.0 (near a
				// pod), nao vizcaína 0.8 and zabra 0.6 (I13), Gaueko's phantom whaleboat one fog night a lunar cycle (I14), the whale
				// chase. The kit's debris wreck and the sunken sites' pieces stand in; the adrift kinds lay nothing until then.
				.Sites((POIType.Island, 1f), (POIType.SunkenShip, 1.2f), (POIType.SunkenRuins, 0.1f), (POIType.SunkenShrine, 0.2f),
					(POIType.SeaMonsterDen, 0.15f), (POIType.SeaCrystalFormation, 0.1f), (POIType.SunkenMonument, 0.12f),
					(POIType.Longline, 0.4f), (POIType.WhaleTow, 0.25f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.KrakenWreck, 0.6f), (POIType.KrakenGrounds, 1f))
				.Prefers(POIType.SunkenShip, "Debris", 1f)
				.Favours(BiomeStructureStyle.Timber, 1.6f)
				.Favours(BiomeStructureStyle.Stone, 0.6f)
				// The palette: Biscay oak carvel planks with tarred seams and a red-ochre wale, oak-bark tanned sails, the cofradía's
				// white pennant, bloom iron; honey Igueldo sandstone with carved rope mouldings, mudstone plinths, galtzada setts.
				// LATER (NEW-SURFACES U1/U3/U4): clay tile roofs (Shingle stands in), esparto, baleen and bloom iron as surfaces.
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#5a4632"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, SeaHex("#8a3a22"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Sandstone, SeaHex("#b8a888"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Sandstone, SeaHex("#c4b494"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Carving, BiomeStructureMaterial.Sandstone, SeaHex("#c4b494"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#5a5248"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, SeaHex("#94846a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Shingle, SeaHex("#8a5a3a"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#5a4632"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#3a3632"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#a8683a"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#e8e4dc"))
				// No settlement stands in this sea: streets unlit. The adrift sites burn kriseilu whale-oil lamps on their staffs; the
				// sunken sites and the caves' routes crystal posts holding galerna-stone.
				// LATER (I7a): lights at non-town sites are laid once I7a places them; the entries are the template's already.
				.Unlit(POIType.Village).Unlit(POIType.Town).Unlit(POIType.City).Unlit(POIType.Capital).Unlit(POIType.Port)
				.Lights(POIType.Longline, BiomeWayLightKind.Kriseilu, 200f)
				.Lights(POIType.WhaleTow, BiomeWayLightKind.Kriseilu, 200f)
				.Lights(POIType.SunkenShip, BiomeWayLightKind.CrystalPost, 24f)
				.Lights(POIType.SunkenShrine, BiomeWayLightKind.CrystalPost, 24f)
				.Lights(POIType.SunkenMonument, BiomeWayLightKind.CrystalPost, 24f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost, 22f)
				.Gathers("Lophelia Coral", "Bubblegum Coral", "Baleen Plate", "Siderite Spar", "Galerna-stone", "Lamia Comb")
				// Coldreef: white Lophelia sprigs on high edges, salmon polyp dots, squat-lobster flecks, a grey silt mantle, manganese
				// stain on iron. LATER (I7): its surface half (salt-bleached oak, tar runs, whale-oil grime) on the adrift pieces.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Coldreef, 0.9f, 1f)
				.ErodesAs("Seabed");
		}
	}
}
#endif
