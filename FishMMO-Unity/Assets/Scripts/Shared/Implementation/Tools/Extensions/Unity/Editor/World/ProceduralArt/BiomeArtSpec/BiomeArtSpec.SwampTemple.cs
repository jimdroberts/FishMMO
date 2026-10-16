#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Swamp Temple</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SwampTemple()
		{
			// A sacred site swallowed by the swamp: a few great cypresses, rushes, ivy dense on the flagstones, knees and fungi.
			return E("Swamp Temple", L(Ground.Mud, Plants("Reeds", 10f), Grass("GrassRush", 10f, 200), Trees(0.01f, 20f, 15f, "BaldCypress")),
				Ds(D(Ground.Flagstone, 24f, 4f, -1f, -1f, Plants("Ivy", 8f, 200)),
					D(Ground.Moss, 48f, 2f, -1f, -1f, Plants("Fern", 5f), Plants("CypressKnees", 1f, 128), Plants("MushroomSwamp", 0.8f, 128))),
				C(Ground.CliffRock), Ground.Mud, Ground.Mud)
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
