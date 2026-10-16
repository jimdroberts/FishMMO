#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Volcanic Temple</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry VolcanicTemple()
		{
			// A temple inside a volcano: fumarole cones, standing basalt columns, obsidian, sulphur crystals and sparse lava
			// lichen on the cinder.
			return E("Volcanic Temple", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt fallen columns", 0.03f, 16f, F(RockTypes.Basalt, "Fallen", "Block")),
					Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Basalt columns", 0.02f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Obsidian", 0.02f, 14f, F(RockTypes.Obsidian, "Chunk", "Shard"))),
				Ds(D(Ground.Flagstone, 32f, 4f), D(Ground.Lava, 96f, 5f),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Plants("LichenLava", 1f, 128))),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic);
		}
	}
}
#endif
