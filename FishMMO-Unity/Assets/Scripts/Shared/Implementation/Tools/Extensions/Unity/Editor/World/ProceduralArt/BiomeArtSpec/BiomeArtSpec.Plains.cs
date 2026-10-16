#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Plains</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Plains()
		{
			// Mixed-grass prairie / pampas: golden bunch grass and needle-grass, sunflower, coneflower and goldenrod patches
			// among the prairie forbs, lone bur oaks, rabbitbrush on the dry slopes.
			return E("Plains", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Plants("FlowersWarm", 3f), Stand(Was(0.03f, 20f), 20f, W(0.03f, 0.5f, open: 0.08f), "Oak"), Bush("Bramble", 0.05f),
					Plants("FlowersTall", 2f, 180), Grass("GrassFeather", 15f)),
				Ds(D(Ground.Grass, 96f, 2f, -1f, -1f, Grass("GrassLush", 25f)), D(Ground.Soil, 128f, 2f, 15f, 45f, Bush("Rabbitbrush", 0.3f, 1f, 45f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(LowlandGeology)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland);
		}
	}
}
#endif
