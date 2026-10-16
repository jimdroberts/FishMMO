#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Karst</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Karst()
		{
			// Limestone country from Dinaric pavement to Guilin towers: holm oak, and olive and rock-rose where it is warm
			// and drier, tree ferns in the warmest; hart's-tongue fern in the grikes and ivy over the rock.
			return E("Karst", L(Ground.Grass, Grass("GrassLush", 30f), Stand(Was(0.2f, 10f), 25f, W(0.2f, 0.6f, open: 0.05f), "Oak"), Plants("Fern", 4f), Bush("Box", 0.6f), Bush("Juniper", 0.15f), Blocks("Limestone pavement", 0.03f, 18f, F(RockTypes.Limestone, "Pavement", "Block"), 12f),
					Band(Trees(0.03f, 12f, 25f, "HolmOak"), 0.35f, 1f), Band(Trees(0.02f, 12f, 20f, "Olive"), 0.45f, 1f, -1f, 0.3f), Band(Bush("Cistus", 0.3f), 0.4f, 1f, -1f, 0.5f), Band(Trees(0.05f, 8f, 25f, "TreeFern"), 0.6f, 1f)),
				Ds(D(Ground.Limestone, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, LimestoneRocks), Outcrops("Limestone pinnacles", 0.06f, 12f, F(RockTypes.Limestone, "Pinnacle"), 30f),
						Plants("FernHartstongue", 3f, 160, 45f), Plants("Ivy", 2f, 128, 60f)),
					D(Ground.Soil, 64f)),
				C(Ground.Limestone, 35f), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Limestone, RockTypes.Chalk, RockTypes.Marble)
				.Look(BiomeLookClass.Forest)
				.Wildlife(BiomeHabitat.Scrub | BiomeHabitat.Mountain);
		}
	}
}
#endif
