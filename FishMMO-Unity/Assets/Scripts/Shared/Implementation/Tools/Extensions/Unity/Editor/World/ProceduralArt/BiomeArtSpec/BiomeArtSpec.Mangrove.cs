#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Mangrove</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Mangrove()
		{
			// The mangal of the medieval Swahili coast (Kilwa, Lamu, Pemba, the Rufiji delta; biome round, approved 2026-10-25, authored
			// 2026-10-26): a dark, glossy, even-topped forest of mkoko standing knee-deep at high water on arching prop roots, a seaward
			// fringe of grey Avicennia and round-crowned Sonneratia over a bed of pencil and cone pneumatophores crusted with rock oysters,
			// behind it Bruguiera on knee roots, tannin-barked Ceriops and cannonball Xylocarpus, inland the tan hypersaline ukanda, then
			// the grey-black razor-pitted coral rag under Pemphis, doum and wild date palms, sea hibiscus and coconut groves. The creeks
			// drain to black mud where fiddler crabs wave their white claws by the thousand; the tide (shared clock) bares and floods it twice
			// a day. Its people are Swahili townsmen, pole-cutters and lime-burners: stone towns of coral rag in lime roofed flat with
			// mangrove poles, fishing villages of daub under steep makuti thatch, sewn mtepe on the mud, tidal uzio fences on the flats.
			// Wet reach (h >= 0.75): taller mangrove, Heritiera, flood rice, no ukanda; dry reach (h < 0.55): dwarf Avicennia, doum palms,
			// the big ukanda; cool edge (t < 0.65): Avicennia and Rhizophora only. Fantasy: nguva sea-maidens, seafire, the Root Throne, Nuru.
			// LATER (I9 T1): every ground and object texture as Mangrove's own variant of its recipe (decision 12); until then the grounds
			// are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Seaspark + Springtide. LATER (WATER): the seafire flag on dark-moon nights.
			// LATER (SEA-LIFE): mudskipper 2, dugong 0.4, sawfish 0.15, the nguva 0.05; the kit's baitfish, crabs and turtles stand in.
			// LATER (AMBIENT-LIFE): fiddler crabs waving by the thousand, mudskippers, ghost crabs, crab plovers, egrets, a creek crocodile.
			return E("Mangrove", L(Ground.MudMkokoBrown,
					// MKOKO: Rhizophora closed over the mid-tide mud, a few Avicennia and Bruguiera among it, on a litter of yellow leaves,
					// propagules and crab chimneys; die-back skeletons and the coupes' cut-pole stumps.
					Stand(Was(1.6f, 5f), 15f, W(0.9f, 1f, open: 0.02f), "Mkoko"), Trees(0.01f, 6f, 10f, "Mchu", "Muia"),
					Plants("Propagules", 10f, 160, 10f), Plants("MangroveLitter", 20f, 128, 10f), Plants("PneumatophoresAvicennia", 20f, 200, 15f),
					Plants("CrabChimneys", 15f, 128, 10f), Plants("MudWhelks", 12f, 128, 10f), Undersea("RootOysters", 6f, 0f, 3f, 160, 20f),
					Logs("Die-back skeletons", 0.006f, 40f, Wood("Snag_Mangrove"), 8f), Logs("Pole stumps", 0.003f, 40f, Wood("PoleStumps"), 8f))
					.Tide(0.1f, 0.95f).Ways(path: Ground.PathMudPoleWalk),
				Ds(
					// d0: the fiddler flat — firm grey-tan sandy mud pocked with crab chimneys, Sonneratia cones, mud whelks, champignon rocks.
					D(Ground.MudFiddlerFlat, 80f, 3f, 0f, 2f, Plants("CrabChimneys", 40f, 160, 5f), Plants("PneumatophoresSonneratia", 8f, 160, 5f),
						Plants("MudWhelks", 8f, 128, 5f), Undersea("RootOysters", 3f, 0f, 3f, 160, 20f),
						Outcrops("Champignon rocks", 0.002f, 80f, F(RegionalRocks.CoralRag, "Champignon"), 10f))
						.Tide(-0.9f, 0.2f).Ways(path: Ground.PathMudPoleWalk),
					// d1: the seaward fringe — pencil-root mud under the Sonneratia and Avicennia, rock oysters on the roots.
					D(Ground.MudPencilRoot, 60f, 2.5f, 0f, 4f, Stand(Was(0.8f, 5f), 15f, W(0.5f, 0.6f), "Mpia", "Mchu"),
						Plants("PneumatophoresAvicennia", 40f, 220, 8f), Plants("PneumatophoresSonneratia", 8f, 160, 8f),
						Undersea("RootOysters", 6f, 0f, 3f, 160, 20f), Plants("MudWhelks", 4f, 128, 8f),
						Outcrops("Champignon rocks", 0.002f, 80f, F(RegionalRocks.CoralRag, "Champignon"), 10f))
						.Tide(-0.5f, 0.35f).Ways(path: Ground.PathMudPoleWalk),
					// d2: the back mangal — Bruguiera over its looped knee roots, Ceriops, the odd Rhizophora; mangrove fern in the gaps.
					D(Ground.ForestFloorMangal, 56f, 2f, 0f, 8f, Stand(Was(1.2f, 5f), 15f, W(0.85f, 1f), "Muia", "Mkandaa", "Mkoko"),
						Plants("KneeRoots", 14f, 160, 10f), Plants("MangroveFern", 6f, 160, 10f), Plants("MudWhelks", 12f, 128, 10f),
						Plants("MangroveLitter", 12f, 128, 10f), Grass("GrassSedgeFimbristylis", 10f, 200, 10f), Bush("Lumnitzera", 1f),
						Band(Trees(0.01f, 14f, 10f, "Msikundazi"), -1f, 1f, 0.75f, 1f),
						Logs("Die-back skeletons", 0.006f, 40f, Wood("Snag_Mangrove"), 8f))
						.Tide(0.85f, 1.25f),
					// d3: the ukanda — a tan salt flat flooded only by spring tides: saltcouch rims, sesuvium, curled algal-mat chips, dwarf Avicennia.
					// LATER (I2j-a): the dry reach's doubled ukanda; the Ukanda pan carves it here wherever it stands.
					D(Ground.SaltUkandaCrust, 120f, 4f, 0f, 1f, Grass("GrassSaltCouch", 24f, 220, 5f), Plants("FlowersSesuvium", 8f, 200, 5f),
						Plants("AlgalMatChips", 25f, 128, 5f), Trees(0.02f, 3f, 5f, "Mchu"), Plants("PneumatophoresAvicennia", 30f, 200, 5f),
						Bush("Lumnitzera", 1f), Logs("Die-back skeletons", 0.006f, 40f, Wood("Snag_Mangrove"), 8f))
						.Tide(1.1f, 1.45f).Ways(path: Ground.PathSalt),
					// d4: the raised rag — razor-pitted fossil reef: pemphis scrub, lapiaz pinnacle fields, rag blocks and pebbles, the baobab
					// beside the old towns, doum palms in the dry reach.
					D(Ground.RagPavement, 40f, 5f, 3f, 35f, Bush("Pemphis", 1.02f), Outcrops("Lapiaz", 0.02f, 10f, F(RegionalRocks.CoralRag, "Lapiaz"), 35f),
						Blocks("Rag blocks", 0.004f, 30f, F(RegionalRocks.CoralRag, "Block")), Stones(Pebbles("CoralRag"), 3f),
						Trees(0.002f, 120f, 15f, "Baobab"), Band(Trees(0.03f, 10f, 12f, "Mkoma"), -1f, 1f, -1f, 0.55f))
						.Tide(1.3f, BiomeSeaFloors.OpenDepth),
					// d5: the red shamba sand under the palms — coconut, wild date, cannonball mangrove, doum (dry), sea hibiscus, jasmine at the houses.
					D(Ground.SoilShambaRed, 64f, 2f, 0f, 10f, Trees(0.02f, 9f, 10f, "Palm"), Trees(0.02f, 8f, 12f, "Mkindu"), Trees(0.01f, 14f, 12f, "Mkomafi"),
						Band(Trees(0.03f, 10f, 12f, "Mkoma"), -1f, 1f, -1f, 0.55f), Band(Trees(0.01f, 14f, 12f, "Msikundazi"), -1f, 1f, 0.75f, 1f),
						Bush("SeaHibiscus", 0.18f), Grass("GrassSaltCouch", 10f, 220, 14f), Grass("GrassSedgeFimbristylis", 8f, 200, 14f),
						Plants("FlowersAsmini", 4f, 180, 14f), Plants("Rihani", 1f, 160, 14f), Plants("MangroveFern", 4f, 160, 14f), Stones(Pebbles("CoralRag"), 1f))
						.Tide(1.4f, BiomeSeaFloors.OpenDepth),
					// d6: the creek banks — slumping grey mud over an exposed root mat.
					D(Ground.MudCreekBankMangal, 64f, 3f, 15f, 45f, Plants("Propagules", 4f, 128, 45f), Plants("CrabChimneys", 8f, 128, 45f))
						.Ways(path: Ground.PathMudPoleWalk)),
				C(Ground.RagCliffNotched, 40f), Ground.MudCreekBedBlack, Ground.SandLagoonMuddy)
				// The lagoon: rock oysters, upside-down jellies pulsing bell-down on the mud, sandfish half-buried.
				.Under(Sea("RootOysters", 4f, 0f, 3f, 160, 20f), Sea("UpsideDownJelly", 2f, 0.3f, 3f, 128, 10f), Sea("Sandfish", 1f, 0.5f, 6f, 128, 10f))
				.UnderTheSea(BiomeSeaFloor.ToLowWater)
				.Tides(3f)
				// The old generic mangrove rules this round retired (Phragmites, the jungle-tree stand, the generic pneumatophores, nipa,
				// fern, hibiscus and oysters).
				.Moved("main", "Trees: Jungle", "Reeds", "Trees: Mangrove", "Pneumatophores")
				.Moved("detail/0", "Trees: Nipa")
				.Moved("detail/1", "Fern", "Bush: Hibiscus")
				.Moved("lakebed", "MusselBedOyster")
				.Ways(path: Ground.PathMudCreekPath, road: Ground.LateriteRagRoad, cobbles: Ground.FlagstoneChokaa)
				.Bars(sand: Ground.SandCreekMouth, gravel: Ground.GravelRagShell)
				// Own rock coral rag (the raised reef: champignon, lapiaz, notch), accepting the red-and-white Magarini sands.
				// LATER (I9): Sandstone "Magarini" as Mangrove's own look.
				.Accepts(RegionalRocks.CoralRag, RockTypes.Sandstone)
				.CaveShell(CaveForm.Cave, "CoralRag").CaveShell(CaveForm.SeaCave, "CoralRag").CaveShell(CaveForm.Grotto, "CoralRag")
				.CaveShell(CaveForm.Mine, "Sandstone").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				// Marcasite of the tidal mud and rag-cave clays, and fantasy Nuru that sparkles like disturbed seafire (the Flicker mode).
				.Crystals((BiomeCaveCrystalKind.Marcasite, 0.6f), (BiomeCaveCrystalKind.Nuru, 0.4f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Flicker)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Flicker)
				// Creek network, mud islands, the ukanda pan, the raised reef's terrace and the dolines.
				.Landforms("MangroveCreeks", "MudIslands", "Ukanda", "RagTerrace", "RagDolines")
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Jungle | BiomeHabitat.Wetland | BiomeHabitat.Coast)
				.HomeTo(("Crab", 3f), ("Baitfish", 2.5f), ("Jacks", 0.8f), ("Sea turtle", 0.6f), ("Jellyfish", 0.5f), ("Shark", 0.2f))
				.Moat(BiomeMoatWater.Wet)
				// Mangrove creek water: sage-khaki, greener and warmer than the estuary's grey, a tan tannin froth.
				.WaterTint(new BiomeWaterLook(SeaHex("#5e6a48"), SeaHex("#1a2820"), new Vector3(0.55f, 0.22f, 0.32f), 0.55f, SeaHex("#5e4a32"), SeaHex("#cdbf96")))
				.SeaTint(new BiomeSeaLook(new Color(0.10f, 0.26f, 0.20f), 4f, 1.6f))
				.Floats((BiomeFloatingPlantKind.PropaguleRaft, 0.3f))
				.GrowsVines(0.3f, 0.6f, 0.3f, 0.15f, ("Derris", 0.6f), ("Pencilvine", 0.4f))
				.Blades("GrassSaltCouch", t => WoodsBlade(t, "#5a7a5a", "#9aaa88", 0.35f, stiffness: 1.4f, width: 0.8f,
					head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.2f, heads: new[] { "#a89a70" }))
				.Blades("GrassSedgeFimbristylis", t => WoodsBlade(t, "#4a6a34", "#6a8a44", 0.3f, width: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.5f, heads: new[] { "#6a4a24" }))
				.Blades("FlowersSesuvium", t => WoodsBlade(t, "#5a7a3a", "#a04a4a", 0.4f,
					head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.03f, heads: new[] { "#e070b0", "#f0a0cc" }))
				.Blades("FlowersAsmini", t => WoodsBlade(t, "#4a6a34", "#5e7e40", 0.4f,
					head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.03f, heads: new[] { "#f6f4ee" }))
				// Sites: the Swahili coast's mangal — mangrove mazes, villages, fishing camps, pole yards; the stone towns few.
				// LATER (I7): the named pieces — the long stone town house, the hall of domes, pillar tombs, the cliff-top Husuni, the makuti
				// village, the boriti landing, the lime kiln, the rag quarry, the mtepe yard, uzio and dema traps, the Kilwa harbour and mint
				// house, the mzimu, the root-split town, the kisima well, the Root Throne, the mangrove maze dressing and Nguva Rock;
				// traffic: mtepe, ngalawa, mtumbwi. The kit's variants below stand in.
				.Sites((POIType.MangroveMaze, 1.6f), (POIType.Village, 1.4f), (POIType.FishingCamp, 1.4f), (POIType.LumberCamp, 1.2f), (POIType.RiverMouth, 1.2f),
					(POIType.Nest, 1.2f), (POIType.Delta, 1f), (POIType.Island, 1f), (POIType.Port, 0.9f), (POIType.Town, 0.8f), (POIType.TradingPost, 0.8f),
					(POIType.Ruins, 0.8f), (POIType.Bay, 0.8f), (POIType.Graveyard, 0.6f), (POIType.Temple, 0.6f), (POIType.Dockyard, 0.6f), (POIType.Shrine, 0.6f),
					(POIType.AncientTree, 0.5f), (POIType.Mine, 0.5f), (POIType.Quarry, 0.4f), (POIType.Spring, 0.4f), (POIType.Wreck, 0.5f), (POIType.SunkenShip, 0.3f),
					(POIType.MonsterDen, 0.4f), (POIType.SmugglersCove, 0.4f), (POIType.PirateCove, 0.3f), (POIType.HerbGrove, 0.5f), (POIType.Keep, 1f),
					(POIType.Landmark, 0.4f), (POIType.Monument, 0.3f), (POIType.Waystation, 0.3f), (POIType.Camp, 0.4f), (POIType.Hermitage, 0.2f),
					(POIType.City, 0.1f), (POIType.Capital, 0.02f), (POIType.River, 1f), (POIType.Lake, 1f), (POIType.Bridge, 1f), (POIType.Cave, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HauntedManor, 0.6f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.GoblinMarket, 0.5f), (POIType.SerpentIdol, 0.6f), (POIType.LizardfolkVillage, 1f), (POIType.Crannog, 0.8f),
					(POIType.HagsCauldron, 1.2f), (POIType.HydraLair, 1.2f), (POIType.ReedVillage, 0.4f), (POIType.CovenCircle, 1f),
					(POIType.LizardfolkHatchery, 1f), (POIType.LadyOfTheLake, 0.4f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 1.4f)
				.Prefers(POIType.Village, "Stone", 0.3f)
				.Prefers(POIType.Village, "Longhouse", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Stone", 1.8f)
				.Prefers(POIType.Town, "Timber", 0.3f)
				.Prefers(POIType.Port, "Stone", 1.2f)
				.Prefers(POIType.Port, "Timber", 0.6f)
				.Prefers(POIType.TradingPost, "Market", 1.2f)
				.Prefers(POIType.Temple, "Court", 1.4f)
				.Prefers(POIType.Keep, "Stone", 1.4f)
				.Prefers(POIType.Dockyard, "Slipway", 1.4f)
				.Prefers(POIType.FishingCamp, "Shore", 1.2f)
				.Prefers(POIType.FishingCamp, "Huts", 0.6f)
				.Prefers(POIType.Waystation, "Inn", 1.2f)
				.Prefers(POIType.Graveyard, "Railed", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.Tower, "Wizard", 0.3f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.3f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.2f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.2f)
				.Prefers(POIType.LizardfolkVillage, "Basking", 1.4f)
				// (the fantasy wave's variants end here)
				.Favours(BiomeStructureStyle.Stone, 1.2f)
				.Favours(BiomeStructureStyle.Earth, 1.4f)
				.Favours(BiomeStructureStyle.Timber, 0.8f)
				.Favours(BiomeStructureStyle.Hide, 0f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: coral rag in lime rendered cream and roofed flat on projecting mangrove poles, dressed coral trim, dark hardwood
				// doors; grey-brown wattle-and-daub under steep makuti thatch, boriti poles; indigo and madder cotton.
				// LATER (NEW-SURFACES U1/U6): CoralRag rubble, Chokaa render, BoritiRoof, KilwaCoral, Mlango, Boriti, Pamba, Udongo and
				// Makuti as surfaces of their own; the kit's stone, plaster, thatch and timber, tinted, stand in.
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Stone, SeaHex("#8e8a7e"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Plaster, SeaHex("#ece4d0"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Plaster, SeaHex("#dcd4c0"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#d8cfb4"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Stone, SeaHex("#6a665a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Plaster, SeaHex("#dcd4c0"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#3a2418"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, SeaHex("#6a5a48"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, SeaHex("#8a7a52"), pitch: BiomeRoofPitch.Pitch45)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, SeaHex("#5a5852"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Floor, BiomeStructureMaterial.Earth, SeaHex("#6a4a30"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Timber, SeaHex("#6a3a24"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Thatch, SeaHex("#8a7a52"), pitch: BiomeRoofPitch.Pitch45)
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Timber, SeaHex("#6a3a24"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Timber, SeaHex("#6a3a24"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, SeaHex("#6a5a48"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Stone, SeaHex("#8e8a7e"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Stone, SeaHex("#d8cfb4"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#6a3a24"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#3a2e26"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#e0d6c0"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#2a3a6a"))
				// Finger millet and sesame on the red shamba fields, flood rice in cleared back mangal; coconut groves.
				.Crops(("Detail_CropFingerMillet", 0.4f), ("Detail_CropSesame", 0.3f), ("Detail_CropRiceMangrove", 0.3f))
				.CropSurface("Detail_CropFingerMillet", BiomeFieldSurface.Flat)
				.CropSurface("Detail_CropSesame", BiomeFieldSurface.Flat)
				.CropSurface("Detail_CropRiceMangrove", BiomeFieldSurface.Paddy)
				.Orchards(new Entry.Orchard("Palm", 0.6f, 7f))
				// Stake-and-coir fences on the field edges.
				.Lines(new Entry.Line(BiomeLinearForm.Picket, BiomeLinearPlace.FieldEdges))
				// Coconut-husk torches in the villages, wall lamps in the stone towns, braziers at a keep, cressets at the landings; the
				// Nuru posts at shrines, great trees and the mazes. LATER (I7a): non-town site lights are laid once I7a places them.
				.Lights(POIType.Village, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.LumberCamp, BiomeWayLightKind.TorchPost, 18f)
				.Lights(POIType.Town, BiomeWayLightKind.WallLantern, 12f)
				.Lights(POIType.Port, BiomeWayLightKind.WallLantern, 12f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.WallLantern, 12f)
				.Lights(POIType.Temple, BiomeWayLightKind.HangingLantern, 10f)
				.Lights(POIType.Graveyard, BiomeWayLightKind.HangingLantern, 10f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Dockyard, BiomeWayLightKind.Cresset, 18f)
				.Lights(POIType.Shrine, BiomeWayLightKind.CrystalPost, 16f)
				.Lights(POIType.AncientTree, BiomeWayLightKind.CrystalPost, 16f)
				.Lights(POIType.MangroveMaze, BiomeWayLightKind.CrystalPost, 16f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 15f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				.Describe("Tidal forest of the warm coasts — mangroves on arching prop roots over black mud, creeks that fill and drain with the tide, and coral-rag towns roofed with mangrove poles")
				.Names(onsets: new[] { "Ki", "Ma", "Mto", "Ku", "Pa", "Zi", "Ji", "Sha", "Wa", "Nya", "Bu", "La", "Ta", "Mwa", "Ru" },
					nuclei: new[] { "a", "i", "u", "e", "o" },
					codas: new[] { "ni", "wa", "mba", "ndo", "si", "ra", "ji", "le" },
					middles: new[] { "ka", "li", "mu", "nde" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Pango", "Kisima", "Handaki", "Mwamba", "Undercroft", "Vault" },
					dungeonPrefixes: new[] { "The Root-woven", "The Tide-drowned", "The Coral-built", "The Seafire-lit", "The Pole-roofed" },
					poiSuffixes: new[] { "Mkoko", "Mto", "Bandari", "Husuni", "Baraza", "Mzimu", "Kisima", "Ras", "Kisiwa", "Uzio", "Ukanda", "Mnara" },
					adjectives: new[] { "Root-woven", "Tide-drowned", "Coral-built", "Monsoon-blown", "Pole-roofed", "Seafire-lit" },
					poiWords: new[]
					{
						("village", new[] { "Makuti Village", "Kijiji" }),
						("town", new[] { "Stone Town", "Mji" }),
						("port", new[] { "Bandari", "Kilwa Harbour" }),
						("fishingcamp", new[] { "Uzio", "Dema" }),
						("lumbercamp", new[] { "Boriti Landing", "Pole Yard" }),
						("mangrovemaze", new[] { "Mkoko Maze", "Root Maze" }),
						("shrine", new[] { "Mzimu", "Cloth Shrine" }),
						("spring", new[] { "Kisima", "Stepped Well" }),
						("keep", new[] { "Husuni", "Cliff Palace" }),
						("quarry", new[] { "Rag Quarry", "Chokaa Pit" }),
						("mine", new[] { "Lime Kiln", "Kiln" }),
						("ancienttree", new[] { "Root Throne", "Mkoko Mkuu" }),
						("landmark", new[] { "Nguva Rock", "Champignon" }),
					},
					about: "Swahili coast: the mangal and the coral-rag towns (Ki, Ma, Mto, Zi, Mwa; -ni, -wa, -mba, -ndo, -si)")
				.Gathers("Boriti Poles", "Mangrove Bark", "Mud Crab", "Rock Oysters", "Mud Whelk", "Sandfish", "Coral Lime", "Sandarusi Copal", "Mangrove Fern",
					"Rihani", "Marcasite", "Nuru")
				.Herbs("Detail_Rihani", "Detail_MangroveFern", "Detail_FlowersSesuvium")
				// Tideline: oyster and barnacle crust below the high-water line, a dark tannin band above it, crab-bored daub at wattle feet.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Tideline, 0.5f, 0.7f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
