#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Molten Surface</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MoltenSurface()
		{
			// Io's paterae, Erta Ale: nothing on the lake itself; spatter cones and rafted crust plates on the cooled margins,
			// ropy pahoehoe beside them, scoria bombs on the cinder.
			return E("Molten Surface", L(Ground.Lava),
				Ds(D(Ground.Basalt, 32f, 3f, -1f, -1f, Stones(SmallRocks("Basalt"), 2f), Blocks("Obsidian", 0.03f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard")),
						Outcrops("Spatter cones", 0.01f, 20f, F(RockTypes.Scoria, "Cone"), 20f), Blocks("Crust plates", 0.03f, 12f, F(RockTypes.Basalt, "Slabs"), 15f)),
					D(Ground.Ash, 64f), D(Ground.Pahoehoe, 48f),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Blocks("Scoria bombs", 0.03f, 10f, F(RockTypes.Scoria, "Bomb", "Lump")))),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Wet)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.HotSpring, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f),
					(POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f),
					(POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.LavaLake, 1f), (POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f),
					(POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f), (POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f),
					(POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				// The fantasy wave (2026-10-27): its variants here, and who holds the keeps and towers.
				.Prefers(POIType.DragonLair, "Hoard", 1.4f)
				.Prefers(POIType.WarbandCamp, "Black Host", 1.4f)
				// (the fantasy wave's variants end here)
				.Gathers("Emberglass Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Volcanic")
				.Emits("Sulphur Dust", 0.08f);
		}
	}
}
#endif
