#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Farmland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Farmland()
		{
			// Mixed farming: standard oaks and poplar rows, hawthorn hedgerow thickets with hazel and bramble (scatter makes
			// thickets, not lines), cow parsley on the verges, poppies at the field margins. (Wheat fields need hand-made masks.)
			return E("Farmland", L(Ground.Soil, Stand(Was(0.02f, 20f), 15f, W(0.03f, 0.5f, open: 0.06f), "Oak"), Bush("Bramble", 0.3f), Bush("Hazel", 0.15f),
					Bush("Hawthorn", 0.3f), Trees(0.01f, 8f, 15f, "LombardyPoplar")),
				Ds(D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassLush", 30f), Plants("FlowersTall", 1f, 160)), D(Ground.GrassDry, 64f, 2f, -1f, -1f, Grass("GrassDry", 20f), Plants("FlowersWarm", 1f, 160))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(LowlandGeology)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Farmland)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
