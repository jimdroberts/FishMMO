#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Castle</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Castle()
		{
			// Castle grounds: ivy and moss on the stone, nettles and docks at the wall feet, a few meadow flowers, rubble.
			return E("Castle", L(Ground.Flagstone, Plants("Ivy", 4f, 180), Plants("CushionMoss", 3f, 128)),
				Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 8f), Bush("Box", 0.4f), Plants("HerbRuderal", 2f), Plants("FlowersMeadow", 1.5f, 160)),
					D(Ground.Gravel, 48f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 2f))),
				C(Ground.CliffRock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Limestone, RockTypes.Slate, RockTypes.Gneiss)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Settlement)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
