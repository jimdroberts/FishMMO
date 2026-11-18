#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Tundra</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Tundra()
		{
			// The treeless arctic (biome round, approved 2026-10-16, authored 2026-10-26): endless low ground patterned by frost —
			// ice-wedge polygons with water in their troughs, frost-boil circles ringed with sorted stones, solifluction lobes,
			// pingos and palsas out of the flats, thaw lakes everywhere; a knee-high mat of dwarf birch, arctic willow and
			// bearberry that burns scarlet in autumn, cottongrass drifts, Dryas and arctic poppies on the gravel, pink late
			// snowbanks, banded gneiss shattered into block fields, bleached antlers and rafted driftwood. Its people are reindeer
			// nomads and lake hunters (great hide chums ringed by sledges, turf-and-whalebone houses, inuksuit on every skyline);
			// its fantasy an Ice Age that never quite ended (mammoth-tusk halls, the fire-fox's aurora, labradorite and aurorite).
			// Taiga keeps the reindeer-lichen heath, juniper and bilberry; Peat Bog the crowberry and hare's-tail; Alpine the moss
			// campion and alpine flowers.
			// LATER (I9): every ground and object texture as Tundra's own variant of its recipe (decision 12); until then the
			// grounds below are Tundra's own recipes and the stones, barks and leaves the catalogue's.
			// LATER (RARE-LOOKS, no spec column yet): rare looks Aurora + Hoarfrost (BiomeTemplate.rareLookA/B, decision 25).
			// LATER (CODE-TABLE CLEARANCE Q13): nothing changes in its selection row.
			return E("Tundra", L(Ground.LichenTundraMat,
					// The dwarf-shrub and lichen mat: bronze fescue tufts, the odd cotton drift, arctic flowers.
					Grass("GrassTuft", 14f), Grass("GrassCotton", 2f), Plants("FlowersArctic", 3f, 180),
					// Dwarf birch turning crimson by patch, the prostrate willow and bearberry, the odd Lapland rosebay.
					Bush("DwarfBirch", 6f, 0.65f), Bush("ArcticWillow", 3.25f), Bush("Bearberry", 5f), Bush("LaplandRosebay", 2f),
					Plants("LichenSnow", 8f), Plants("CushionDryas", 2f), Plants("CushionHairCap", 3f),
					// Rough-stalk boletes stand above the mat by the dwarf birch.
					Moded(Plants("MushroomRoughStalk", 0.6f), BiomeScatterMode.UnderShrub("DwarfBirch", 1.5f)),
					// Shed antlers and bleached bones everywhere, heaped at the herders' sacred sites.
					Plants("DebrisAntler", 0.6f, 128), Moded(Plants("DebrisAntler", 3f, 128), BiomeScatterMode.NearSites("RitualSite", 40f)),
					Stones(SmallRocks("Gneiss"), 2f),
					// Lichened gneiss erratics and the odd perched granite boulder.
					Blocks("Gneiss erratics", 0.015f, 20f, F(RockTypes.Gneiss, "Boulder", "Block")),
					Outcrops("Perched erratics", 0.004f, 40f, F(RockTypes.Granite, "Perched")),
					// Solifluction lobes creeping down the 6–20° slopes.
					With(Blocks("Solifluction lobes", 0.02f, 16f, F(RegionalRocks.Permafrost, "Lobe")), s => s.Slope = new Vector2(6f, 20f)),
					// The thaw slumps' ice-rich headwalls, where the landform carved one beside a thaw lake.
					OnLandform(Blocks("Thaw slump headwalls", 0.004f, 60f, F(RegionalRocks.Permafrost, "ThawSlump")), BiomeLandformPart.Whole, "ThawSlump"),
					// Rafted driftwood. LATER (SCATTER-MODES): a near-water anchor for the lake shores and bars; sparse on the mat until then.
					Logs("Driftwood", 0.004f, 30f, Wood("Driftwood", "DriftwoodTrunk"))),
				Ds(
					// d0: the ice-wedge polygon mire on the flats (≤ 3°, low ground), cottongrass and sedge in the centres, pendant grass in
					// the troughs, hair-cap moss on the rims; slab causeways cross it.
					With(D(Ground.MossPolygonMire, 128f, 3f, 0f, 3f, Grass("GrassCotton", 10f), Grass("GrassSedge", 14f), Grass("GrassPendant", 12f),
						Plants("CushionHairCap", 6f), Blocks("Polygon rims", 0.03f, 24f, F(RegionalRocks.Permafrost, "Polygons")),
						Blocks("Palsas", 0.01f, 40f, F(RegionalRocks.Permafrost, "Palsa"))), l => l.Height = new Vector2(0f, 0.52f)).Ways(road: Ground.PathSlabCauseway),
					// d1: frost-boil sorted circles (≤ 5°), Dryas and saxifrage on their rims, wood-rush; the ring stones as pebbles laid in rings.
					D(Ground.GravelSortedCircles, 64f, 3f, 0f, 5f, Plants("FlowersArctic", 6f, 180), Plants("CushionDryas", 5f), Plants("CushionSaxifrage", 2f),
						Grass("GrassWoodrushArctic", 4f),
						// LATER (I7): a stone-ring piece of its own (Detail_StoneRing, 1.5–3 m rings); gneiss pebbles in rings stand in.
						Moded(Stones(Pebbles("Gneiss"), 4f), BiomeScatterMode.FairyRings(1.2f, 0.6f)),
						Blocks("Frost-jacked slabs", 0.01f, 16f, F(RockTypes.Gneiss, "FrostJacked"))),
					// d2: tussock cottongrass meadow on the damp ground (≤ 8°, humidity ≥ 0), earth hummocks, palsas, dwarf birch; slab
					// causeways across it; the feltleaf willow along its streams.
					D(Ground.GrassTussockTundra, 80f, 2f, 0f, 8f, Grass("GrassCotton", 16f), Grass("GrassSedge", 8f), Bush("DwarfBirch", 2f, 0.65f),
						// LATER (SCATTER-MODES): the willow within 15 m of water (a near-water anchor); the wet tussock stands in.
						Bush("Willow", 0.6f, 0.5f),
						Blocks("Earth hummocks", 0.06f, 6f, F(RegionalRocks.Permafrost, "Thufur")), Blocks("Palsas", 0.01f, 40f, F(RegionalRocks.Permafrost, "Palsa")))
						.Humid(0f, 1f).Ways(road: Ground.PathSlabCauseway),
					// d3: the frost-shattered gneiss fell-field (felsenmeer) on 4–28°. LATER (GROUND-BANDS): also on the high ground (≥ 0.54)
					// and the polar desert (humidity < −0.3) as one either-or band; the slope band alone until then, ventifacts in the dry reach.
					D(Ground.RockFellField, 96f, 3f, 4f, 28f, Grass("GrassTuft", 4f), Grass("GrassWoodrushArctic", 8f), Plants("CushionDryas", 6f),
						Plants("CushionSaxifrage", 3f), Plants("FlowersArctic", 4f, 180), Plants("LichenSnow", 6f), Plants("LichenRockTripe", 4f),
						Bush("Bearberry", 3f), Bush("LaplandRosebay", 2f), Stones(SmallRocks("Gneiss"), 5f),
						Blocks("Felsenmeer", 0.03f, 12f, F(RockTypes.Gneiss, "Felsenmeer")),
						Blocks("Frost-jacked slabs", 0.01f, 16f, F(RockTypes.Gneiss, "FrostJacked")),
						Band(Blocks("Ventifacts", 0.008f, 20f, F(RockTypes.Basalt, "Ventifact")), -1f, 1f, -1f, -0.3f)),
					// d4: late snowbanks in the lee hollows (a small share, on the shaded faces), white heather and willow at their margins.
					D(Ground.SnowWatermelon, 112f, 4f, 0f, 30f, Bush("ArcticWillow", 3.25f), Bush("ArcticWhiteHeather", 4f)).Facing(BiomeFacing.Poleward),
					// d5: banded gneiss outcrops on the steep ground, rock tripe on them, roseroot at their foot.
					D(Ground.RockGneissOutcrop, 64f, 4f, 28f, 90f, Plants("LichenRockTripe", 6f, 160, 60f), Plants("Roseroot", 1.5f),
						Blocks("Gneiss blocks", 0.015f, 20f, F(RockTypes.Gneiss, "Block"))),
					// d6: frost-heaved scree on 30–40°.
					D(Ground.GravelHeaveScree, 24f, 2f, 30f, 40f, Stones(SmallRocks("Gneiss"), 6f), Plants("Roseroot", 1f), Plants("FlowersRiverBeauty", 1f, 180),
						Blocks("Schist flags", 0.015f, 12f, F(RockTypes.Schist, "Flags")))),
				C(Ground.CliffGneissBanded, 40f), Ground.SiltThermokarst, Ground.GravelOutwashBraided)
				// The rules the round retired (Taiga's juniper, bilberry and reindeer lichen, Alpine's flowers and moss campion, the generic
				// shrub and grey stones and boulders), switched off where the tool wrote them and nobody tuned them since.
				.Moved("main", "ShrubSmall", "Bush: Willow", "Bush: Juniper", SmallRocks("Grey"), "Bush: Bilberry", "LichenClump", "FlowersAlpine")
				.Moved("detail/1", "Boulders", "Formations: Granite erratics", "Formations: Gneiss erratics", "CushionMossCampion")
				.Ways(path: Ground.PathTundraTrod, road: Ground.PathDroveRoad, cobbles: Ground.FlagstoneGneissSplit)
				.Bars(sand: Ground.SandGlacialFlour, gravel: Ground.GravelOutwashBark)
				// Own rock gneiss (its formations); approved: accepts granite, schist, basalt.
				.Accepts(RockTypes.Gneiss, RockTypes.Granite, RockTypes.Schist, RockTypes.Basalt)
				// Labradorite flashing in the caves, and the fantasy aurorite, brightest in the polar night.
				.Crystals((BiomeCaveCrystalKind.Labradorite, 0.6f), (BiomeCaveCrystalKind.Aurorite, 0.4f))
				.GlowMode(SceneGlowSource.Crystal, SceneGlowMode.NightLength)
				.Look(BiomeLookClass.Cold)
				// Approved: the thaw lakes' ducks, terns and geese (FreshWater); Grassland's insects, toads and rats dropped.
				.Wildlife(BiomeHabitat.Tundra | BiomeHabitat.FreshWater)
				// LATER (AMBIENT-LIFE): reindeer herd, arctic fox, lemming, ptarmigan, snowy owl, skua, snow buntings, loon, muskox, the
				// rare distant woolly mammoth and the revontuli fire-fox trailing green-violet sparks at night.
				.Moat(BiomeMoatWater.Frozen)
				.NoVines()
				// Pingos, palsas and thaw slumps carved into the ground at the cut (approved: sculpted landforms).
				.Landforms("Pingo", "Palsa", "ThawSlump")
				.Describe("Treeless arctic tundra — frost polygons, cottongrass, dwarf shrubs, pingos and reindeer herders' camps")
				.Blades("GrassTuft", t => NivalBlade(t, "#5a5a34", "#a08a4a", 0.35f, stiffness: 0.9f, clump: 0.6f, head: GrassBladeHead.SeedHead,
					headSize: 0.03f, headShare: 0.25f, heads: new[] { "#5a4030" }))
				.Blades("GrassCotton", t => NivalBlade(t, "#5a6a3a", "#a0402a", 0.3f, clump: 0.4f, headShare: 0.6f, heads: new[] { "#fbfaf6", "#f0eee6" }))
				.Blades("GrassSedge", t => NivalBlade(t, "#4a5a34", "#8a8a50", 0.35f, head: GrassBladeHead.SeedHead, headSize: 0.03f, headShare: 0.2f,
					heads: new[] { "#3a2a20" }))
				.Blades("GrassPendant", t => NivalBlade(t, "#4a6a3a", "#c06a2a", 0.25f, wade: 0.3f, head: GrassBladeHead.Plume, headSize: 0.08f,
					headShare: 0.4f, heads: new[] { "#a05a30" }))
				.Blades("GrassWoodrushArctic", t => NivalBlade(t, "#4a4a2a", "#7a6a3a", 0.4f, head: GrassBladeHead.SeedHead, headSize: 0.012f, headShare: 0.35f,
					heads: new[] { "#2a1e18" }))
				.Blades("FlowersArctic", t => NivalBlade(t, "#4a6a2c", "#5e7a34", 0.4f, head: GrassBladeHead.Flower, sprinkle: 12f, headSize: 0.02f,
					heads: new[] { "#f4e04a", "#f6f4ec", "#b04a9a", "#e08ab0" }))
				.Blades("FlowersRiverBeauty", t => NivalBlade(t, "#6a8a7a", "#7a9a8a", 0.35f, head: GrassBladeHead.Flower, sprinkle: 14f, headSize: 0.035f,
					heads: new[] { "#c83a8a", "#b02a78" }))
				// Approved: sparse nomadic settlement — village 0.35, town 0.08, no city, capital or castle; camps, hunting lodges and
				// fishing camps at the thaw lakes, ritual sites, labyrinths, inuksuit, cairn graves and the mammoth graveyards.
				.Sites((POIType.Camp, 1.8f), (POIType.Shrine, 0.8f), (POIType.Tower, 1f), (POIType.Bridge, 1f), (POIType.Spring, 0.2f),
					(POIType.HotSpring, 0.2f), (POIType.Cave, 0.6f), (POIType.IceCave, 0.2f), (POIType.Monument, 0.4f), (POIType.Wreck, 0.6f),
					(POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f), (POIType.Lake, 1.5f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f),
					(POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.Grotto, 0.4f), (POIType.SeaCave, 0.6f), (POIType.BanditCamp, 0.3f), (POIType.HuntingLodge, 1.2f), (POIType.FishingCamp, 1.3f),
					(POIType.MonsterDen, 1.2f), (POIType.Nest, 0.8f), (POIType.RitualSite, 1.6f), (POIType.StoneCircle, 0.8f), (POIType.LeyNexus, 0.3f),
					(POIType.FallenStar, 0.5f), (POIType.Portal, 0.3f), (POIType.Graveyard, 0.4f), (POIType.Barrow, 1f), (POIType.Crypt, 0.2f),
					(POIType.Battlefield, 0.3f), (POIType.Ossuary, 0.6f), (POIType.Ruins, 0.4f), (POIType.RuinedTower, 0.3f), (POIType.Statue, 0.6f),
					(POIType.AncientRoad, 0.4f), (POIType.Hermitage, 0.4f), (POIType.Village, 0.35f), (POIType.Town, 0.08f), (POIType.Port, 0.05f),
					(POIType.Keep, 1f), (POIType.Fortress, 0.05f), (POIType.TradingPost, 1.2f), (POIType.Waystation, 0.3f), (POIType.Mine, 0.2f),
					(POIType.Quarry, 0.2f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.DungeonEntrance, 0.6f), (POIType.BossLair, 0.4f),
					(POIType.OreVein, 0.5f), (POIType.CrystalFormation, 0.4f), (POIType.HerbGrove, 0.5f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 1f), (POIType.TrollBridge, 0.6f), (POIType.GiantsSteading, 0.8f), (POIType.GoblinWarren, 0.7f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 0.5f),
					(POIType.AncientWall, 0.6f), (POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.TitansBones, 0.8f),
					(POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f), (POIType.WolfDen, 0.8f), (POIType.SummitCairn, 0.4f),
					(POIType.FrostGiantSteading, 1.2f), (POIType.RuneStone, 1f), (POIType.FrozenArmy, 1.2f), (POIType.MammothGraveyard, 1.4f),
					(POIType.WinterWolfDen, 1f), (POIType.AuroraShrine, 1f), (POIType.LichSpire, 1f), (POIType.FrozenShipwreck, 0.4f),
					(POIType.LostThrone, 0.4f))
				.Prefers(POIType.Village, "Timber", 0.05f)
				.Prefers(POIType.Village, "Stone", 0.4f)
				.Prefers(POIType.Town, "Stone", 1f)
				.Prefers(POIType.Town, "Palisaded", 0.2f)
				.Prefers(POIType.Village, "Longhouse", 1.5f)
				.Prefers(POIType.Village, "Yurts", 0f)
				.Prefers(POIType.Town, "Timber", 0.05f)
				.Prefers(POIType.TradingPost, "Market", 1f)
				.Prefers(POIType.TradingPost, "Caravanserai", 0f)
				.Prefers(POIType.Keep, "Stone", 1.5f)
				.Prefers(POIType.Keep, "Palisade", 0.1f)
				.Prefers(POIType.Fortress, "Stone", 1f)
				.Prefers(POIType.Fortress, "Palisade", 0.3f)
				.Prefers(POIType.Waystation, "Stone Inn", 1.5f)
				.Prefers(POIType.Waystation, "Inn", 0.2f)
				.Prefers(POIType.Camp, "Hide Tents", 2.5f)
				.Prefers(POIType.Camp, "Crude", 0.8f)
				.Prefers(POIType.Camp, "Lean-tos", 0.2f)
				.Prefers(POIType.Camp, "Yurts", 0f)
				.Prefers(POIType.HuntingLodge, "Hide Lodge", 2f)
				.Prefers(POIType.HuntingLodge, "Lodge", 0.2f)
				.Prefers(POIType.FishingCamp, "Huts", 1.5f)
				.Prefers(POIType.FishingCamp, "Shore", 1f)
				.Prefers(POIType.MonsterDen, "Bones", 1.5f)
				.Prefers(POIType.Nest, "Bones", 1.5f)
				.Prefers(POIType.RitualSite, "Bonefire", 1.5f)
				.Prefers(POIType.RitualSite, "Stones", 1f)
				.Prefers(POIType.StoneCircle, "Menhirs", 1f)
				.Prefers(POIType.StoneCircle, "Trilithons", 0.1f)
				.Prefers(POIType.Graveyard, "Earthen", 1.5f)
				.Prefers(POIType.Graveyard, "Railed", 0.1f)
				.Prefers(POIType.Barrow, "Ringed", 1.5f)
				.Prefers(POIType.Ossuary, "Tomb", 0.5f)
				.Prefers(POIType.Hermitage, "Stone", 1.5f)
				.Prefers(POIType.Hermitage, "Cottage", 0.2f)
				.Prefers(POIType.DungeonEntrance, "Barrow", 1.6f)
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
				.Prefers(POIType.DragonLair, "Bone Roost", 1.2f)
				.Prefers(POIType.GiantsSteading, "Great Hall", 1.2f)
				.Prefers(POIType.GiantsSteading, "Bone Garth", 1.2f)
				.Prefers(POIType.WreckedCaravan, "Last Camp", 1.2f)
				.Prefers(POIType.SignalBeacon, "Watch Fire", 1.3f)
				.Prefers(POIType.Hamlet, "Longhouse Stead", 1.5f)
				.Prefers(POIType.WarbandCamp, "Horde", 1.5f)
				.Prefers(POIType.PetrifiedBattle, "Frozen Field", 1.2f)
				.Prefers(POIType.TitansBones, "Sunken Skull", 1.2f)
				.Prefers(POIType.Commandery, "Preceptory", 1.2f)
				.Prefers(POIType.RoyalMausoleum, "Barrowdown", 1.2f)
				.Prefers(POIType.NomadCamp, "Felt Yurts", 0.7f)
				// (the fantasy wave's variants end here)
				// LATER (I7): Tundra's signature pieces — the great chum with its ring of sledges, the semi-sunken turf-and-whalebone house,
				// inuksuit and route cairns, the stone meat cache and fox trap, the mammoth-tusk hall, the boulder labyrinth, the sacred
				// antler heap, drying and skin-boat racks, the reindeer corral, the permafrost ice cellar and the snow-house winter camp
				// (approved); until then the kit's variants below stand in, the palette giving them hide, turf, bone and gneiss.
				// LATER (I7): a mammoth "Tusks" ossuary variant; the tomb stands in.
				.Favours(BiomeStructureStyle.Hide, 1.8f)
				.Favours(BiomeStructureStyle.Earth, 1.6f)
				.Favours(BiomeStructureStyle.Stone, 1.2f)
				.Favours(BiomeStructureStyle.Timber, 0.3f)
				.Favours(BiomeStructureStyle.Iron, 0.3f)
				// The palette: two-tone reindeer hide on bare poles (the chum), turf blocks on gneiss footings under sod with whale and
				// mammoth rib rafters (the qarmaq), drystone gneiss, schist flags, driftwood logs, ivory and antler carving, sky-iron,
				// sealskin, ochre-red felt. LATER (I9 / NEW-SURFACES): Tundra's own textured variants of hide, turf, bone, driftwood,
				// gneiss and schist (the shared surfaces tinted stand in).
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Wall, BiomeStructureMaterial.Canvas, NivalHex("#5a4a3a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Roof, BiomeStructureMaterial.Canvas, NivalHex("#c8bcaa"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Plinth, BiomeStructureMaterial.Earth, NivalHex("#4a4430"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Floor, BiomeStructureMaterial.Canvas, NivalHex("#6a5a48"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Door, BiomeStructureMaterial.Canvas, NivalHex("#5a4a3a"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Trim, BiomeStructureMaterial.Timber, NivalHex("#a89a84"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Fence, BiomeStructureMaterial.BoneWork, NivalHex("#d8d0bc"))
				.Builds(BiomeStructureStyle.Hide, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, NivalHex("#847a7a"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Wall, BiomeStructureMaterial.Earth, NivalHex("#4a4430"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#4a5228"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, NivalHex("#7a7070"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Trim, BiomeStructureMaterial.BoneWork, NivalHex("#d8d0bc"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Door, BiomeStructureMaterial.BoneWork, NivalHex("#d8d0bc"))
				.Builds(BiomeStructureStyle.Earth, BiomeStructureRole.Floor, BiomeStructureMaterial.Fieldstone, NivalHex("#847a7a"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Wall, BiomeStructureMaterial.Fieldstone, NivalHex("#8a7e7c"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.UpperWall, BiomeStructureMaterial.Earth, NivalHex("#4a4430"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#4a5228"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.ShingleRoof, BiomeStructureMaterial.Slate, NivalHex("#5e5a58"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Plinth, BiomeStructureMaterial.Fieldstone, NivalHex("#7a7070"))
				.Builds(BiomeStructureStyle.Stone, BiomeStructureRole.Paving, BiomeStructureMaterial.Fieldstone, NivalHex("#847a7a"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Wall, BiomeStructureMaterial.Log, NivalHex("#b0a898"))
				.Builds(BiomeStructureStyle.Timber, BiomeStructureRole.Roof, BiomeStructureMaterial.Earth, NivalHex("#4a5228"))
				.BuildsAll(BiomeStructureRole.LogWall, BiomeStructureMaterial.Log, NivalHex("#b0a898"))
				.BuildsAll(BiomeStructureRole.Turf, BiomeStructureMaterial.Earth, NivalHex("#4a5228"))
				.BuildsAll(BiomeStructureRole.Dressing, BiomeStructureMaterial.Fieldstone, NivalHex("#847a7a"))
				.BuildsAll(BiomeStructureRole.Carving, BiomeStructureMaterial.BoneWork, NivalHex("#e0d8c4"))
				.BuildsAll(BiomeStructureRole.Woodwork, BiomeStructureMaterial.Log, NivalHex("#b0a898"))
				.BuildsAll(BiomeStructureRole.Ironwork, BiomeStructureMaterial.Iron, NivalHex("#3a3a3e"))
				.BuildsAll(BiomeStructureRole.Cloth, BiomeStructureMaterial.Canvas, NivalHex("#6a6050"))
				.BuildsAll(BiomeStructureRole.Banner, BiomeStructureMaterial.Dyed, NivalHex("#a04a2a"))
				// Approved: no fields at all (authored empty list).
				.NoCrops()
				// Inuksuit and route cairns along the drove roads. LATER (I7): the inuksuk and window-stone pieces; the marker cairn stands in.
				.Lines(With(new Entry.Line(BiomeLinearForm.Cairn, BiomeLinearPlace.Lanes), l =>
				{
					l.Style.Spacing = 80f;
					l.Style.Sides = BiomeLinearSides.One;
				}))
				// Soapstone lamps on gneiss cairns in the camps and villages, bone-fat braziers at the trading forts, aurorite shard posts
				// at the ritual sites; lamp cairns along a cave's route, wall lanterns in the mines.
				.Lights(POIType.Village, BiomeWayLightKind.LampCairn, 20f)
				.Lights(POIType.Town, BiomeWayLightKind.LampCairn, 14f)
				.Lights(POIType.Keep, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.TradingPost, BiomeWayLightKind.Brazier, 14f)
				.Lights(POIType.RitualSite, BiomeWayLightKind.CrystalPost, 12f)
				.CaveLight(BiomeCaveLightPlace.Route, BiomeWayLightKind.LampCairn)
				.CaveLight(BiomeCaveLightPlace.MineDrift, BiomeWayLightKind.WallLantern, 12f)
				.Gathers("Silver Vein", "Frostbloom Patch", "Mammoth Ivory", "Arctic Root Patch", "Snow Lichen Patch", "Labradorite Seam", "Sky-iron Fragment")
				.Herbs("Detail_Roseroot", "Detail_CushionDryas", "Detail_LichenSnow", ProceduralArtCatalogue.BushPrefix + "LaplandRosebay")
				// Approved: the new Lichen weathering (orange Xanthoria and grey crust on stone, wood and bone silvered) as a coat in place
				// of moss.
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.Coated(BiomeStructureCoat.Lichen, 0.55f)
				.ErodesAs("Periglacial");
		}

		/// <summary>
		/// A blade type's look as a cold round's draft gives it (<see cref="Entry.Blades"/>; shared by the Tundra, Ice Sheet and Glacier files):
		/// root and tip colours over the mesh's, the ground tint, and the shape and head values that are set (null keeps the type's own).
		/// </summary>
		private static void NivalBlade(BiomeGrassType t, string root, string tip, float groundTint, float? stiffness = null, float? width = null,
			float? clump = null, float? bend = null, float? heightScale = null, float? wade = null, GrassBladeHead? head = null, float? sprinkle = null,
			float? headSize = null, float? headShare = null, string[] heads = null)
		{
			t.RootColour = NivalHex(root);
			t.TipColour = NivalHex(tip);
			t.ColoursOverMesh = true;
			t.GroundTint = groundTint;
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
				t.HeadColours = System.Array.ConvertAll(heads, NivalHex);
				t.HeadColoursOverMesh = true;
			}
		}

		/// <summary>An sRGB colour from #rrggbb, for the Tundra, Ice Sheet and Glacier files.</summary>
		private static Color NivalHex(string hex) => SurfaceCatalogue.Hex(hex);
	}
}
#endif
