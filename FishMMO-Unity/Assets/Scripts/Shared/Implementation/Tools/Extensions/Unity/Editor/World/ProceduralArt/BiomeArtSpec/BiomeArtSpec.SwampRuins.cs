#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SwampRuins()
		{
			// A drowned settlement in swamp forest: bald cypress (knees round them) over the snags, ivy over the flagstones, fungi.
			return E("Swamp Ruins", L(Ground.Mud, Plants("Reeds", 15f), Stand(Was(0.15f, 8f), 15f, W(0.3f, 0.3f, open: 0.05f), "Dead"), Bush("Willow", 0.3f)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 6f, 180)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f), Stand(Was(0.1f, 10f), 15f, W(0.3f, 0.35f, open: 0.05f), "BaldCypress"), Plants("CypressKnees", 1.5f, 128), Plants("MushroomSwamp", 1f, 128),
						Logs("Fallen logs", 0.04f, 10f, Wood("FallenLog_Oak", "Stump_Bracket")))),
				C(Ground.Soil), Ground.Mud, Ground.Mud)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet)
				// 2026-10-25 (decisions 86–88, phase I2n-c): its natural caves and grottos are the Swamp Cave's drowned galleries
				// inside, where they are cut in a rock the Swamp Cave forms in (its soluble set); elsewhere its own, as before.
				.CaveInterior(CaveForm.Cave, BiomeCaveInteriors.SwampCave)
				.CaveInterior(CaveForm.Grotto, BiomeCaveInteriors.SwampCave)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its vines, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SunkenTemple, 1f), (POIType.DrownedVillage, 1f), (POIType.WitchHut, 1f), (POIType.StiltVillage, 1f),
					(POIType.BogShrine, 1f), (POIType.WispHollow, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.BanditCamp, 1f),
					(POIType.HuntingLodge, 0.3f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 0.5f),
					(POIType.RitualSite, 1f), (POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.CorruptedGrove, 1f),
					(POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f),
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.Obelisk, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f),
					(POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f),
					(POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f),
					(POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.DragonLair, 0.5f), (POIType.TrollBridge, 0.6f), (POIType.GoblinWarren, 0.7f), (POIType.HauntedManor, 0.6f),
					(POIType.HangingTree, 0.6f), (POIType.BuriedHoard, 0.5f), (POIType.AmbushSite, 0.8f), (POIType.BoundaryStone, 0.6f),
					(POIType.Hamlet, 0.3f), (POIType.BurnedVillage, 0.3f), (POIType.WarbandCamp, 1f), (POIType.CultistHideout, 1f),
					(POIType.SpellScar, 0.8f), (POIType.PlanarRift, 0.6f), (POIType.ElderMonolith, 0.4f), (POIType.WaygateRing, 0.3f),
					(POIType.GolemGraveyard, 1f), (POIType.LostLibrary, 1.4f), (POIType.GoblinMarket, 0.5f), (POIType.SerpentIdol, 0.6f),
					(POIType.LizardfolkVillage, 1f), (POIType.Crannog, 0.8f), (POIType.HagsCauldron, 1.2f), (POIType.HydraLair, 1.2f),
					(POIType.CovenCircle, 1f), (POIType.LizardfolkHatchery, 1f), (POIType.LadyOfTheLake, 0.4f), (POIType.LostThrone, 1.2f),
					(POIType.Keep, 1f), (POIType.Tower, 1f))
				.Prefers(POIType.Village, "Timber", 0.4f)
				.Prefers(POIType.Tower, "Wizard", 1.2f)
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.Keep, "Orc Hold", 1f)
				.Prefers(POIType.Keep, "Ruined Keep", 1.4f)
				.Prefers(POIType.Keep, "Dark Keep", 0.6f)
				.Prefers(POIType.Keep, "Stone", 0.2f)
				.Prefers(POIType.Keep, "Palisade", 0.2f)
				.Prefers(POIType.Tower, "Dark Tower", 0.8f)
				.Prefers(POIType.Tower, "Ruined Watchtower", 1.2f)
				.Prefers(POIType.Tower, "Timber", 0.2f)
				.Prefers(POIType.Tower, "Stone", 0.2f)
				.Prefers(POIType.Tower, "Observatory", 0.2f)
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.3f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.2f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.2f)
				.Prefers(POIType.StepPyramid, "Fallen Fane", 1.6f)
				.Prefers(POIType.LostCity, "Ruined Forum", 1.3f)
				.Prefers(POIType.SwampCathedral, "Sunk Minster", 1.6f)
				// (the fantasy wave's variants end here)
				.GrowsVines(0.9f, 1f, 0.3f, 0.14f, ("Liana", 0.6f), ("Ivy", 0.4f))
				.Gathers("Mirewort Patch", "Bog Puffball Cluster")
				.Weathers(BiomeStructureFinish.Algae, 0.3f, 0.6f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
