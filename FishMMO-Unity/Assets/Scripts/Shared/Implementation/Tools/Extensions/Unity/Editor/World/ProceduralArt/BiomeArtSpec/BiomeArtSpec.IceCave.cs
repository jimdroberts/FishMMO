#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceCave()
		{
			// Eisriesenwelt, floor only: ice stalagmites and columns, giant hoar-frost crystals, ice pebbles.
			return E("Ice Cave", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.04f, 10f, "Calved", "Rounded"), Outcrops("Ice stalagmites", 0.05f, 6f, F(RockTypes.WaterIce, "Stalagmite")), Stones(Pebbles("Ice"), 3f)),
				Ds(D(Ground.Snow, 48f), D(Ground.Frost, 64f, 2f, -1f, -1f, Outcrops("Hoar crystals", 0.03f, 6f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice, 35f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 0.2f), (POIType.Shrine, 0.2f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 0.2f),
					(POIType.Monument, 0.2f), (POIType.Wreck, 0.2f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.IceCave, 1f), (POIType.BanditCamp, 0.2f), (POIType.FishingCamp, 0.2f),
					(POIType.MonsterDen, 0.2f), (POIType.Nest, 0.2f), (POIType.RitualSite, 0.2f), (POIType.Temple, 0.2f), (POIType.LeyNexus, 1f),
					(POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 0.2f), (POIType.Crypt, 0.2f), (POIType.Ossuary, 0.2f),
					(POIType.Ruins, 0.2f), (POIType.RuinedTower, 0.2f), (POIType.Statue, 0.2f), (POIType.AncientRoad, 0.2f),
					(POIType.Hermitage, 0.2f), (POIType.Mine, 0.2f), (POIType.Quarry, 0.2f), (POIType.Lighthouse, 0.2f),
					(POIType.SmugglersCove, 0.2f), (POIType.PirateCove, 0.2f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 0.2f), (POIType.BossLair, 0.2f), (POIType.OreVein, 0.2f),
					(POIType.CrystalFormation, 0.2f), (POIType.HerbGrove, 0.2f))
				.Gathers("Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Periglacial");
		}
	}
}
#endif
