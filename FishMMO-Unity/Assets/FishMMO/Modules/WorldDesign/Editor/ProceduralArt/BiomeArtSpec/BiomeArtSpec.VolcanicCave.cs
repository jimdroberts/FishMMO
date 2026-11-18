#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Volcanic Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry VolcanicCave()
		{
			// An active lava tube with crystal-lined chambers: dribble spires, sulphur needles, Naica-like gypsum blades,
			// obsidian, glowing lava, ropy pahoehoe with scoria.
			return E("Volcanic Cave", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Obsidian", 0.06f, 10f, F(RockTypes.Obsidian, "Chunk", "Shard", "Scree")),
					Outcrops("Lava dribbles", 0.04f, 8f, F(RockTypes.Basalt, "Dribble"))),
				Ds(D(Ground.Lava, 96f, 5f),
					D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.02f, 10f, F(RockTypes.Sulphur, "Crystals")), Outcrops("Gypsum", 0.006f, 16f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Blocks("Scoria", 0.02f, 10f, F(RockTypes.Scoria, "Lump")))),
				C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.HotSpring, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f),
					(POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f),
					(POIType.Headland, 1f), (POIType.Grotto, 1f), (POIType.SeaCave, 1f), (POIType.LavaTube, 1f), (POIType.BanditCamp, 1f),
					(POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.Nest, 1f), (POIType.RitualSite, 1f), (POIType.Temple, 1f),
					(POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f), (POIType.Crypt, 1f),
					(POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f), (POIType.AncientRoad, 1f),
					(POIType.Hermitage, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f), (POIType.SmugglersCove, 1f),
					(POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f),
					(POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f), (POIType.CrystalFormation, 1f),
					(POIType.HerbGrove, 1f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.DragonLair, "Hoard", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.4f)
				// (the fantasy wave's variants end here)
				.Gathers("Emberglass Cluster", "Lumen Quartz Cluster", "Glowcap Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Volcanic");
		}
	}
}
#endif
