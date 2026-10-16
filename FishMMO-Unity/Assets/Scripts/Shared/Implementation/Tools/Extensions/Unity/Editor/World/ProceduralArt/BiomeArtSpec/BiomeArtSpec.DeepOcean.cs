#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Deep Ocean</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry DeepOcean()
		{
			return E("Deep Ocean", L(Ground.Silt), Ds(D(Ground.Mud, 128f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// The continental slope: Lophelia cold-water coral thickets on the rock, sea pens in the mud, crinoids and
				// glass sponges, brittle-star beds and the odd nodule.
				.Under(Sea("Sponge", 0.3f, 0f, 0f, 128), Sea("Starfish", 0.2f, 0f, 0f, 96), Sea("Anemone", 0.2f, 0f, 0f, 128),
					Sea("ColdCoral", 0.3f, 0f, 0f, 160, 60f), Sea("GlassSponge", 0.3f, 0f, 0f, 128), Sea("SeaPen", 1f, 0f, 0f, 160, 15f),
					Sea("Crinoid", 0.3f, 0f, 0f, 128, 60f), Sea("BrittleStar", 0.5f, 0f, 0f, 128), SeaStones(Pebbles("Nodule"), 0.5f, 0f, 0f))
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.HomeTo("Whale");
		}
	}
}
#endif
