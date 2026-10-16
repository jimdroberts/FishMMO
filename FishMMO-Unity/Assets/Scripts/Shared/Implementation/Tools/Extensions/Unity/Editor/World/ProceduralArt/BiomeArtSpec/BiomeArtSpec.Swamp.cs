#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Swamp()
		{
			// Forested wetland (−3…20 °C): a bald-cypress swamp with its knees and palmetto where it is warm, an alder carr
			// where it is cool, snags in both (one wood each, the snags joined to it; the two bands meet at about 11.6 °C,
			// so only one wood stands anywhere), mossy logs and fungi, sedges on the peat.
			return E("Swamp", L(Ground.Mud, Plants("Reeds", 20f), Grass("GrassTall", 15f), Bush("Willow", 0.4f),
					Band(Stand(Was(0.2f, 8f), 15f, W(0.35f, 0.4f, open: 0.05f), "BaldCypress", "Dead"), 0.35f, 1f),
					Band(Stand(Was(0.2f, 8f), 15f, W(0.35f, 0.4f, open: 0.05f), "Alder", "Dead"), -1f, 0.35f),
					Band(Plants("CypressKnees", 3f, 160, 15f), 0.35f, 1f), Band(Bush("Palmetto", 0.3f), 0.5f, 1f)),
				Ds(D(Ground.Peat, 48f, 2f, -1f, -1f, Grass("GrassSedge", 12f)),
					D(Ground.Moss, 64f, 2f, -1f, -1f, Plants("Fern", 5f), Logs("Fallen logs", 0.05f, 12f, Wood("FallenLog_Oak", "FallenLog_Birch")), Plants("MushroomSwamp", 1f, 128))),
				C(Ground.Soil), Ground.Mud, Ground.Mud)
				// The snags joined the cypress and alder woods.
				.Moved("main", "Trees: Dead")
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland);
		}
	}
}
#endif
