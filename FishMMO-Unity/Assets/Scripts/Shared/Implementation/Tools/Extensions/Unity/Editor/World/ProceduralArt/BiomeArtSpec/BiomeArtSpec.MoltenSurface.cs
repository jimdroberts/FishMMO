#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

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
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
