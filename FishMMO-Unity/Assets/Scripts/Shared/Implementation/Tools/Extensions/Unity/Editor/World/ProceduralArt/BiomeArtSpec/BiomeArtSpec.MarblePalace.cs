#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Marble Palace</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MarblePalace()
		{
			// Classical palace gardens: Italian cypress in rows, lone olives, oleander and laurel, a little ivy on the stone.
			return E("Marble Palace", L(Ground.Flagstone, Plants("Ivy", 1.5f, 128)),
				Ds(D(Ground.Limestone, 48f, 2f, -1f, -1f, Blocks("Marble", 0.02f, 16f, F(RockTypes.Marble, "Block", "Boulder"))),
					D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 15f), Bush("Box", 0.6f), Trees(0.08f, 6f, 15f, "Cypress"), Trees(0.02f, 14f, 20f, "Olive"), Bush("Oleander", 0.3f), Bush("Laurel", 0.15f))),
				C(Ground.Limestone), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Marble, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Settlement);
		}
	}
}
#endif
