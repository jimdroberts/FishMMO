#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Steppe</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Steppe()
		{
			// Kazakh / Great Basin steppe: feather grass over fescue, sagebrush and rabbitbrush, tumbleweed, spiny cushions on the gravel.
			return E("Steppe", L(Ground.GrassDry, Grass("GrassDry", 35f), Grass("GrassTuft", 15f), Plants("FlowersWarm", 2f), Bush("Sagebrush", 0.8f), Bush("Juniper", 0.05f, 0.7f),
					Grass("GrassFeather", 20f), Plants("ShrubTumbleweed", 0.3f, 128), Bush("Rabbitbrush", 0.4f)),
				Ds(D(Ground.Soil, 96f), D(Ground.Gravel, 64f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 1f), Plants("CushionSpiny", 1f))),
				C(Ground.Rock), Ground.Gravel, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Shale, RockTypes.Limestone, RockTypes.Sandstone, RockTypes.Conglomerate)
				.Look(BiomeLookClass.Arid)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Scrub);
		}
	}
}
#endif
