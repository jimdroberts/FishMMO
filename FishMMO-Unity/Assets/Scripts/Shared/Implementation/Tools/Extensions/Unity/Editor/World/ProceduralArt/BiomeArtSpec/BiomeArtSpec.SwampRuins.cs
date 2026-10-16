#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp Ruins</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SwampRuins()
		{
			// A drowned settlement in swamp forest: bald cypress (knees round them) over the snags, ivy over the flagstones, fungi.
			return E("Swamp Ruins", L(Ground.Mud, Plants("Reeds", 15f), Stand(Was(0.15f, 8f), 15f, W(0.3f, 0.3f, open: 0.05f), "Dead"), Bush("Willow", 0.3f)),
				Ds(D(Ground.Flagstone, 32f, 4f, -1f, -1f, Plants("Ivy", 6f, 180)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f), Stand(Was(0.1f, 10f), 15f, W(0.3f, 0.35f, open: 0.05f), "BaldCypress"), Plants("CypressKnees", 1.5f, 128), Plants("MushroomSwamp", 1f, 128),
						Logs("Fallen logs", 0.04f, 10f, Wood("FallenLog_Oak", "Stump_Bracket")))),
				C(Ground.Soil), Ground.Mud, Ground.Mud)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
