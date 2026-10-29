#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Wetlands</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Wetlands()
		{
			// The great reed marshes of the lower Tigris and Euphrates, the Batiha (biome round, approved 2026-10-18, authored 2026-10-26):
			// horizons of five-metre giant reed and narrow-leaf cattail walling winding channels of startlingly clear jade water over
			// white shell-marl, open lagoons red with Azolla and starred with floating-heart, buffalo wading neck-deep, salt-crusted
			// levee backs, poplars and date palms on the built-up islands and the low tells of drowned cities shedding potsherds. Its
			// people live in reed: arched houses of bundled giant reed on layered dibin islands or floating rafts, a mudhif guest-hall at
			// every village's heart, tar-black canoes; their lords build baked-brick khans with turquoise tile bands. Cool band
			// (t < 0.2): the same reed world as a Danube-delta winter marsh — white-willow galleries, dun reed, water soldier, no palms,
			// rice or salt. Fantasy: the Abzu springs ringed by half-sunk winged bulls, fish-cloaked sages, glowing tablets, Abzulite.
			// LATER (I9 T1): every ground and object texture as Wetlands' own variant of its recipe (decision 12); until then the grounds
			// are its own recipes, the stones, rocks and pieces the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Faience + Eveningstar.
			// LATER (AMBIENT-LIFE): water buffalo herds wading and wallowing, pelican flotillas, flamingos over the sabkha (warm), purple
			// swamphens, otters, the reed-warbler chorus, whooper swans and booming bitterns in the cool reach, the apkallu at dawn.
			return E("Wetlands", L(Ground.MudReedbedSilt,
					// The reedbed: giant reed, the marsh's own litter, flowering rush; sweet flag and the delta's flowers in the cool reach;
					// reed sheaves stood in the stubble round the villages.
					Plants("ReedsQasab", 40f, 220), Plants("ReedsBardi", 8f, 220), Plants("DebrisReed", 8f, 128), Grass("GrassBulrush", 4f, 200),
					Plants("FlowersButomus", 3f, 180), Band(Grass("GrassSweetFlag", 6f, 200), -1f, 0.2f), Band(Plants("FlowersDelta", 4f, 180), -1f, 0.2f),
					Plants("ReedSheaves", 0.5f, 128), Band(Bush("Willow", 0.3f), -1f, 0.2f))
					.Ways(path: Ground.PathMudBuffaloTrack),
				Ds(
					// d0: the shell-marl flat of the shallows — cattail and bulrush in the water, spired snail shells in heaps.
					With(D(Ground.MarlShellFlat, 96f, 3.5f, 0f, 3f, Plants("ReedsBardi", 16f, 220), Grass("GrassBulrush", 12f, 200), Plants("ReedsQasab", 6f, 220),
						Plants("WaterPlantain", 4f, 160), Plants("FlowersButomus", 4f, 180), Band(Grass("GrassSweetFlag", 4f, 200), -1f, 0.2f),
						Plants("ShellsMelanopsis", 3f, 128), Plants("DebrisReed", 3f, 128),
						Band(Logs("Drowned palms", 0.01f, 30f, Wood("Snag_DrownedPalm"), 10f), 0.2f, 1f)),
						l => l.Height = new Vector2(0.45f, 0.48f))
						.Ways(path: Ground.PathMudBuffaloTrack),
					// d1: the buffalo lawn — grazed knotgrass and sedge, dung, ink caps.
					With(D(Ground.GrassBuffaloLawn, 64f, 2f, 0f, 8f, Grass("GrassKnotgrass", 34f, 255, 10f), Grass("GrassFlatsedge", 8f, 220, 10f),
						Band(Plants("FlowersBatiha", 4f, 200, 10f), 0.2f, 1f), Band(Plants("FlowersDelta", 3f, 200, 10f), -1f, 0.2f), Plants("Licorice", 1f, 160, 10f),
						Plants("MushroomInkcap", 0.6f, 128, 10f), Plants("DungCakes", 0.3f, 128, 10f)),
						l => l.Height = new Vector2(0.49f, 0.56f))
						.Ways(path: Ground.PathMudBuffaloTrack),
					// d2: the sabkha on the levee backs (the warm reach only) — saltgrass, red Halocnemum knots, tamarisk and mesquite.
					With(D(Ground.SaltSabkhaLevee, 140f, 4f, 0f, 3f, Grass("GrassSaltgrass", 18f, 220, 8f), Plants("Halocnemum", 6f, 200, 8f),
						Bush("Prosopis", 0.8f), Bush("Tamarisk", 0.4f))
						.Temp(0.2f, 1f).Ways(path: Ground.PathSaltTrace, road: Ground.PathSaltTrace),
						l => l.Height = new Vector2(0.55f, 0.6f)),
					// d3: the floating raft mat (kibasha) — reed on a rhizome mat riding the lagoon.
					With(D(Ground.PeatRaftMat, 40f, 3f, 0f, 2f, Plants("ReedsQasab", 12f, 220), Plants("ReedsBardi", 6f, 220), Plants("DebrisReed", 6f, 128)),
						l => l.Height = new Vector2(0.45f, 0.47f)),
					// d4: the dibin and levee silt-sand — the islands' poplars, date palms and sidr (warm) or white-willow galleries (cool),
					// herbs, ink caps and the potsherds the tells shed.
					With(D(Ground.SandDibinSilt, 112f, 3f, 0f, 12f, Band(Trees(0.02f, 16f, 12f, "EuphratesPoplar"), 0.2f, 1f), Band(Trees(0.03f, 12f, 10f, "DatePalm"), 0.2f, 1f),
						Band(Trees(0.025f, 14f, 12f, "WillowGallery"), -1f, 0.2f), Band(Bush("Willow", 0.62f), -1f, 0.2f), Band(Bush("Tamarisk", 0.4f), 0.2f, 1f),
						Band(Bush("Sidr", 0.2f), 0.2f, 1f), Grass("GrassKnotgrass", 6f, 220, 14f), Grass("GrassFlatsedge", 6f, 220, 14f),
						Band(Plants("FlowersBatiha", 3f, 200, 14f), 0.2f, 1f), Plants("Licorice", 3f, 160, 14f), Plants("MushroomInkcap", 0.4f, 128, 14f),
						Plants("ReedSheaves", 0.3f, 128, 14f), Plants("Sherds", 0.5f, 128, 14f),
						OnLandform(Plants("Sherds", 6f, 128, 14f), BiomeLandformPart.Whole, "Tell"),
						Band(Logs("Fallen willows", 0.02f, 20f, Wood("FallenLog_Willow"), 12f), -1f, 0.2f)),
						l => l.Height = new Vector2(0.53f, 0.6f)),
					// d5: the river-cut silt banks and the gypsum that shows through them.
					// Gypsum only stands as scattered formations (no cliff of its own), so the marl-limestone edge hills carry the bluffs.
					D(Ground.SiltRiverBank, 64f, 3.5f, 18f, 40f, Outcrops("Gypsum crystals", 0.004f, 40f, F(RockTypes.Gypsum, "Crystal"), 40f),
						Blocks("Marl limestone blocks", 0.006f, 30f, F(RockTypes.Limestone, "Block"), 40f))),
				C(Ground.GypsumAlabaster, 40f), Ground.SiltChannelShell, Ground.MarlStonewort)
				// The old generic fen rules this round retired (tall grass and sedge, Phragmites, the birch-and-alder wood, willow,
				// fern, rush and the wet flowers).
				.Moved("main", "Trees: Birch", "Trees: Birch, Alder", "GrassTall", "Bush: Willow")
				.Moved("detail/0", "Reeds", "ReedsPlume", "GrassSedge")
				.Moved("detail/1", "Fern", "GrassRush", "FlowersWet")
				.Ways(path: Ground.PathMudBuffaloTrack, road: Ground.ClayDykeCrown, cobbles: Ground.FlagstoneBitumenBrick)
				.Bars(sand: Ground.SandShellHash, gravel: Ground.GravelShellBar)
				// Gypsum (the Fars alabaster) accepted first, with the marl-limestone edge hills and the marl shale; the marsh floor is alluvium. Gypsum is
				// a formations-only rock, so the limestone is the bluffs' own.
				// LATER (I9): Gypsum's grey-white alabaster with green-grey and red marl interbeds as Wetlands' own look.
				.Accepts(RockTypes.Gypsum, RockTypes.Limestone, RockTypes.Shale)
				.CaveShell(CaveForm.Cave, "Gypsum").CaveShell(CaveForm.Grotto, "Gypsum").CaveShell(CaveForm.Mine, "Gypsum")
				.CaveShell(CaveForm.SeaCave, "Limestone").CaveShell(CaveForm.IceCave, "Ice").CaveShell(CaveForm.LavaTube, "Basalt")
				.Crystals((BiomeCaveCrystalKind.Carnelian, 0.45f), (BiomeCaveCrystalKind.Selenite, 0.2f), (BiomeCaveCrystalKind.Abzulite, 0.35f))
				// Abzulite's slow inner tide (the crystals' and the crystal posts' glow pulses).
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.Pulse)
				.GlowMode(SceneGlowSource.Way, SceneGlowMode.Pulse)
				// Tells over buried cities and the settlement islands carved out of the water.
				.Landforms("Tell", "Dibin")
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland | BiomeHabitat.FreshWater)
				.Moat(BiomeMoatWater.Wet)
				// Reed-filtered jade, startlingly clear over white marl.
				.WaterTint(new BiomeWaterLook(SeaHex("#5a8a6a"), SeaHex("#12302a"), new Vector3(0.42f, 0.14f, 0.20f), 0.10f, SeaHex("#a8a68a"), SeaHex("#e8ecd8")))
				.GrowsVines(0.3f, 0.7f, 0.2f, 0.15f, ("Bindweed", 0.6f), ("Caper", 0.4f))
				.Floats((BiomeFloatingPlantKind.Azolla, 0.35f), (BiomeFloatingPlantKind.FloatingHeart, 0.3f), (BiomeFloatingPlantKind.WaterSoldier, 0.2f),
					(BiomeFloatingPlantKind.Frogbit, 0.15f))
				// Reed-mat walks on palm-trunk bents between the dibin houses and across the reedbeds; the roads stay on embanked dykes.
				.RaisesBoardwalks(BiomeBoardwalks.Paths | BiomeBoardwalks.Streets)
				.Blades("ReedsQasab", t => { WoodsBlade(t, "#8a9a5a", "#5e7a3a", 0.2f, stiffness: 1.1f, width: 1.3f, bend: 0.5f,
					head: GrassBladeHead.Plume, headSize: 0.35f, headShare: 0.6f, heads: new[] { "#6a4a5a", "#c8c0b8" }); t.Wade = 0.9f; })
				.Blades("ReedsBardi", t => { WoodsBlade(t, "#5a7a3a", "#7a9a4a", 0.25f, width: 0.9f, clump: 0.7f,
					head: GrassBladeHead.SeedHead, headSize: 0.12f, headShare: 0.4f, heads: new[] { "#4a2e1e", "#c8a84a" }); t.Wade = 0.8f; })
				.Blades("GrassBulrush", t => { WoodsBlade(t, "#4a6a4a", "#6e8a6e", 0.3f, stiffness: 1.2f, width: 0.7f,
					head: GrassBladeHead.SeedHead, headSize: 0.05f, headShare: 0.5f, heads: new[] { "#6a4a2a" }); t.Wade = 1f; })
				.Blades("GrassKnotgrass", t => WoodsBlade(t, "#3e5a2a", "#7a9a4a", 0.45f, width: 0.6f, bend: 0.7f, clump: 0.1f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.2f, heads: new[] { "#b0a070" }))
				.Blades("GrassFlatsedge", t => WoodsBlade(t, "#4a6a34", "#7a8a4a", 0.35f, width: 0.9f,
					head: GrassBladeHead.Plume, headSize: 0.08f, headShare: 0.4f, heads: new[] { "#8a6a3a" }))
				.Blades("GrassSaltgrass", t => WoodsBlade(t, "#6a7a6a", "#9aaa9a", 0.5f, stiffness: 1.4f, width: 0.5f,
					head: GrassBladeHead.SeedHead, headSize: 0.02f, headShare: 0.3f, heads: new[] { "#b8b0a0" }))
				.Blades("GrassSweetFlag", t => { WoodsBlade(t, "#5a6a3a", "#7a9a4a", 0.35f, width: 1.4f,
					head: GrassBladeHead.SeedHead, headSize: 0.04f, headShare: 0.1f, heads: new[] { "#8a8a3a" }); t.Wade = 0.5f; })
				.Blades("FlowersButomus", t => { WoodsBlade(t, "#5a7a3a", "#6e8e44", 0.35f, width: 1.1f,
					head: GrassBladeHead.Flower, sprinkle: 6f, headSize: 0.1f, heads: new[] { "#e8a0b0", "#8a3a2a" }); t.Wade = 0.5f; })
				.Blades("FlowersBatiha", t => WoodsBlade(t, "#4a6a34", "#6a8a44", 0.4f,
					head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.05f, heads: new[] { "#b8a0d0", "#f0c8d0", "#e8c030", "#c83a6a" }))
				.Blades("FlowersDelta", t => WoodsBlade(t, "#4a6a34", "#6a8a44", 0.4f,
					head: GrassBladeHead.Flower, sprinkle: 10f, headSize: 0.05f, heads: new[] { "#e8c830", "#b05a8a", "#7aa0e0", "#f0d040" }))
				// Sites: fishing camps and reed isles first, ruins (the tells) and islands; the strongholds few, the ports none.
				// LATER (I7): the named pieces — the mudhif guest hall on its arched reed ribs, the arched reed houses, the Isles layout, the
				// floating kibasha raft, tarada landings, hadhra weirs, reed yards, buffalo byres, the bitumen seep, the noria, the khan with
				// its wind-catchers, the Emir's hold, tell ruins, the ziggurat, the Abzu spring, the fish-sage shrine, winged bulls, the ark
				// ribs, the tablet house. The kit's variants below stand in (stilt houses for the reed isles).
				.Sites((POIType.FishingCamp, 1.8f), (POIType.StiltVillage, 1.6f), (POIType.Ruins, 1.4f), (POIType.Island, 1.4f), (POIType.Village, 1.2f),
					(POIType.SunkenTemple, 1.2f), (POIType.Nest, 1.2f), (POIType.Lake, 1.2f), (POIType.Delta, 1.2f), (POIType.River, 1f), (POIType.Camp, 1f),
					(POIType.HerbGrove, 1f), (POIType.TradingPost, 0.8f), (POIType.LumberCamp, 0.8f), (POIType.Shrine, 0.8f), (POIType.Spring, 0.8f),
					(POIType.MonsterDen, 0.8f), (POIType.Waystation, 0.7f), (POIType.Temple, 0.6f), (POIType.Statue, 0.6f), (POIType.Landmark, 0.6f),
					(POIType.RitualSite, 0.6f), (POIType.DrownedVillage, 0.6f), (POIType.HuntingLodge, 0.6f), (POIType.BanditCamp, 0.6f), (POIType.Graveyard, 0.5f),
					(POIType.Town, 0.4f), (POIType.Keep, 1f), (POIType.Monument, 0.4f), (POIType.Battlefield, 0.4f), (POIType.AbandonedFarm, 0.4f),
					(POIType.Hermitage, 0.4f), (POIType.WitchHut, 0.3f), (POIType.RuinedTower, 0.3f), (POIType.Watermill, 0.3f), (POIType.Mine, 0.2f),
					(POIType.Crypt, 0.2f), (POIType.AncientTree, 0.2f), (POIType.Monastery, 0.2f), (POIType.City, 0.1f), (POIType.Castle, 0.1f),
					(POIType.Quarry, 0.1f), (POIType.Capital, 0.05f), (POIType.Fortress, 0.05f), (POIType.Bridge, 1f), (POIType.RiverMouth, 1f), (POIType.Cave, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HauntedManor, 0.6f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.GoblinMarket, 0.5f), (POIType.SerpentIdol, 0.6f), (POIType.LizardfolkVillage, 1f), (POIType.Crannog, 0.8f),
					(POIType.HagsCauldron, 1.2f), (POIType.HydraLair, 1.2f), (POIType.ReedVillage, 1.2f), (POIType.CovenCircle, 1f),
					(POIType.LizardfolkHatchery, 1f), (POIType.LadyOfTheLake, 0.4f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 1f)
				.Prefers(POIType.Village, "Longhouse", 0.4f)
				.Prefers(POIType.Village, "Stone", 0f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.StiltVillage, "Stilts", 1.5f)
				.Prefers(POIType.Town, "Stone", 1f)
				.Prefers(POIType.Town, "Timber", 0.4f)
				.Prefers(POIType.City, "Stone", 1.4f)
				.Prefers(POIType.City, "Timber", 0.2f)
				.Prefers(POIType.Capital, "Stone", 1.4f)
				.Prefers(POIType.Capital, "Timber", 0.2f)
				.Prefers(POIType.TradingPost, "Caravanserai", 1.4f)
				.Prefers(POIType.TradingPost, "Market", 0.6f)
				.Prefers(POIType.Waystation, "Inn", 1.2f)
				.Prefers(POIType.Waystation, "Stone Inn", 0.2f)
				.Prefers(POIType.Keep, "Stone", 1.6f)
				.Prefers(POIType.FishingCamp, "Shore", 1.4f)
				.Prefers(POIType.FishingCamp, "Huts", 0.6f)
				.Prefers(POIType.Camp, "Lean-tos", 1.4f)
				.Prefers(POIType.Camp, "Hide Tents", 0.4f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.SunkenTemple, "Colonnade", 0.8f)
				.Prefers(POIType.SunkenTemple, "Idol", 0f)
				.Prefers(POIType.Ruins, "Excavation", 1.4f)
				.Prefers(POIType.Ruins, "Stone", 1f)
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
				.Prefers(POIType.ReedVillage, "Marsh Town", 1.3f)
				// (the fantasy wave's variants end here)
				.Favours(BiomeStructureStyle.Earth, 1.8f)
				.Favours(BiomeStructureStyle.Stone, 0.6f)
				.Favours(BiomeStructureStyle.Hide, 0.3f)
				.Favours(BiomeStructureStyle.Timber, 0.2f)
				.Favours(BiomeStructureStyle.Iron, 0f)
				// The palette: reed, mud, palm wood, baked brick in bitumen, turquoise faience; no timber framing, no tile roofs. The
				// houses' pointed arch is a 60-degree reed roof until the true barrel arches (I7).
				// LATER (NEW-SURFACES U1/U6): QasabBundle, ReedLattice, Dibin, PalmTrunk, Kilim, BitumenBrick, Faience, GoatHair as
				// surfaces of their own; the kit's reed, earth, stone, adobe and canvas, tinted, stand in.
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed, SeaHex("#c8b078"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, SeaHex("#c8b078"), pitch: BiomeRoofPitch.Pitch60)
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Reed, SeaHex("#c8b078"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, SeaHex("#6a5a44"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Adobe, SeaHex("#b49a6a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Adobe, SeaHex("#a89070"), pitch: BiomeRoofPitch.Flat)
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Trim, BiomeStructureMaterial.Stone, SeaHex("#2a9a9a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Adobe, SeaHex("#8a7450"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Door, BiomeStructureMaterial.Timber, SeaHex("#5a4a38"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Reed, SeaHex("#c8b078"), pitch: BiomeRoofPitch.Pitch50)
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, SeaHex("#2a2420"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, SeaHex("#2a2420"), pitch: BiomeRoofPitch.Pitch30)
				.BuildsAll(BiomeStructureRole.Floor, BiomeStructureMaterial.Reed, SeaHex("#b8a070"))
				.BuildsAll(BiomeStructureRole.Fence, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.BuildsAll(BiomeStructureRole.Paving, BiomeStructureMaterial.Earth, SeaHex("#8a7458"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Reed, SeaHex("#a89868"))
				.BuildsAll(BiomeStructureRole.GroundFloor, BiomeStructureMaterial.Earth, SeaHex("#6a5a44"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Reed, SeaHex("#8a8458"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Adobe, SeaHex("#8a7458"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.Reed, SeaHex("#c8b078"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Timber, SeaHex("#6a5a46"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, SeaHex("#1e1a16"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, SeaHex("#8a2a1e"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, SeaHex("#8a2a1e"))
				// The marsh's red-awned rice in paddies (the warm reach), six-row barley on the levees (both reaches), sugarcane in
				// ridge-and-furrow with water in the furrows (warm); date groves as orchards.
				.Crops(("Detail_CropRiceAnbar", 0.45f), ("Detail_CropBarleySixRow", 0.35f), ("Detail_CropSugarcane", 0.2f))
				.CropSurface("Detail_CropRiceAnbar", BiomeFieldSurface.Paddy)
				.CropSurface("Detail_CropBarleySixRow", BiomeFieldSurface.Flat)
				.CropSurface("Detail_CropSugarcane", BiomeFieldSurface.RidgeAndFurrow)
				.Orchards(new Entry.Orchard("DatePalm", 0.3f, 8f))
				// Naphtha lamps in the villages, brass bracket lanterns on the khan walls, braziers at the holds, Abzulite posts at the
				// springs and shrines. LATER (I7a): non-town site lights are laid once I7a places them; the NaphthaLamp is the kit's own.
				.Lights(POIType.Village, BiomeWayLightKind.NaphthaLamp, 14f)
				.Lights(POIType.StiltVillage, BiomeWayLightKind.NaphthaLamp, 14f)
				.Lights(POIType.FishingCamp, BiomeWayLightKind.NaphthaLamp, 14f)
				.Lights(POIType.Waystation, BiomeWayLightKind.NaphthaLamp, 14f)
				.Lights(POIType.Town, BiomeWayLightKind.HangingBracket, 12f)
				.Lights(POIType.City, BiomeWayLightKind.HangingBracket, 12f)
				.Lights(POIType.Capital, BiomeWayLightKind.HangingBracket, 12f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Castle, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.Shrine, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.Spring, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.SunkenTemple, BiomeWayLightKind.CrystalPost, 10f)
				.Lights(POIType.Temple, BiomeWayLightKind.CrystalPost, 10f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.CrystalPost)
				.CaveLight(BiomeCaveLightPlace.Camp, BiomeWayLightKind.LanternStand)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallTorch, 17f)
				.CaveLight(BiomeCaveLightPlace.MineStope, BiomeWayLightKind.Brazier)
				.CaveLight(BiomeCaveLightPlace.MineLamp, BiomeWayLightKind.HangingLantern)
				.Describe("Open reed marsh and delta fen — giant reeds over clear jade water, floating reed isles, buffalo, and arched reed halls on mud-built islands")
				.Names(onsets: new[] { "Ba", "Qa", "Ha", "Mu", "Su", "Ur", "Ni", "Ki", "Za", "Sha", "Ta", "Eri" },
					nuclei: new[] { "a", "u", "i", "ai", "aa" },
					codas: new[] { "iha", "ash", "ur", "ab", "an", "ul", "ida" },
					middles: new[] { "ar", "ul", "an", "ib" }, syllablesMin: 2, syllablesMax: 3,
					dungeonSuffixes: new[] { "Abzu", "Ishan", "Sump", "Vault", "Archive", "Undercroft" },
					dungeonPrefixes: new[] { "The Drowned", "The Silted", "The Reed-walled", "The Bitumen-black", "The Tablet-lit" },
					poiSuffixes: new[] { "Mudhif", "Dibin", "Kibasha", "Ishan", "Khan", "Hold", "Weir", "Reed-yard", "Abzu-well", "Lagoon", "Channel" },
					adjectives: new[] { "Reed-walled", "Silted", "Jade-watered", "Bitumen-black", "Drowned", "Tell-crowned" },
					poiWords: new[]
					{
						("village", new[] { "Dibin", "Mudhif Village", "Isles" }),
						("stiltvillage", new[] { "Kibasha", "Reed Isles" }),
						("fishingcamp", new[] { "Hadhra Weir", "Tarada Landing", "Weirs" }),
						("ruins", new[] { "Ishan", "Tell" }),
						("keep", new[] { "Emir's Hold", "Hold" }),
						("tradingpost", new[] { "Khan", "Caravanserai" }),
						("lumbercamp", new[] { "Reed-yard", "Qasab Yard" }),
						("spring", new[] { "Abzu-well", "Abzu" }),
						("shrine", new[] { "Fish-sage Shrine", "Reed Shrine" }),
						("sunkentemple", new[] { "Ziggurat", "Drowned Ziggurat" }),
						("lake", new[] { "Lagoon", "Hawr" }),
						("river", new[] { "Channel", "Reach" }),
					},
					about: "Mesopotamian marsh: the Batiha of the reed-builders (Ba, Qa, Mu, Ur, Eri; -iha, -ash, -ur, -ab, -ida)")
				.Gathers("Qasab Sheaf", "Bardi Pollen", "Licorice Root", "Date Cluster", "Bitumen Seep", "Carnelian Nodule", "Abzulite Shard", "Plant of Youth")
				.Herbs("Detail_Licorice", "Detail_GrassSweetFlag", "Detail_WaterPlantain", "Detail_Halocnemum")
				// Silted: a pale grey silt crust caked to flood height with a sharp tide-mark, reed fibre and shell caught in it.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Silted, 0.4f, 0.6f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
