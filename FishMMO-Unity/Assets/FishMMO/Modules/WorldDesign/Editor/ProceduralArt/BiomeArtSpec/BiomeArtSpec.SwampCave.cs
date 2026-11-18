#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SwampCave()
		{
			// A flooded cave, dark inside: pale fungi and slime-mould mats on the mud, dripstone and flowstone where it is drier.
			return E("Swamp Cave", L(Ground.Mud, Plants("Reeds", 6f), Plants("MushroomCave", 2f, 140), Plants("SlimeMat", 1.5f, 128), Logs("Washed-in timber", 0.02f, 10f, Wood("FallenLog_Oak", "Driftwood"))),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Outcrops("Stalagmites", 0.03f, 8f, F(RockTypes.Limestone, "Stalagmite"))),
					D(Ground.Rock, 48f, 3f, 20f, 90f, Blocks("Flowstone", 0.02f, 12f, F(RockTypes.Limestone, "Flowstone")))),
				C(Ground.CliffRock, 38f), Ground.Mud, Ground.Mud)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Cave)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet)
				// 2026-10-25 (decision 86, phase I2n-c): the inside of the swamps' caves cut in the soluble rocks (the Cave's set), so
				// a link to it never holds in granite or sandstone.
				.InteriorRocks(BiomeCaveInteriors.SolubleRocks)
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
					(POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f),
					(POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f),
					(POIType.OreVein, 1f), (POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1.2f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.TrollBridge, "Mossed Span", 1.4f)
				.Prefers(POIType.HauntedManor, "Burned Grange", 1.3f)
				.Prefers(POIType.ManorHouse, "Timber Manor", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.2f)
				.Prefers(POIType.LostLibrary, "Moss Archive", 1.2f)
				// (the fantasy wave's variants end here)
				.Prefers(POIType.Village, "Timber", 0.4f)
				.Prefers(POIType.Tower, "Wizard", 1.2f)
				.GrowsVines(1f, 0.8f, 0.3f, 0.1f, ("Liana", 0.6f), ("Ivy", 0.4f))
				.Gathers("Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.Algae, 0.3f, 0.6f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
