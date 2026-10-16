#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Alpine</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Alpine()
		{
			// Fellfield above the trees: map lichen on the rock, moss campion cushions and alpine flowers in the gravel,
			// krummholz mountain pine, sastrugi on the snow patches.
			return E("Alpine", L(Ground.Rock, Boulders(0.3f, 6f, GreyRound), Stones(SmallRocks("Grey"), 4f),
					Blocks("Schist", 0.06f, 10f, F(RockTypes.Schist, "Lump", "Ridge", "Flags")), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder")), Plants("LichenClump", 2f)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Bush("MountainPine", 0.3f, 0.8f, 40f), Plants("CushionMossCampion", 3f), Plants("FlowersAlpine", 2f, 180)),
					D(Ground.Snow, 64f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Aligned(Blocks("Sastrugi", 0.03f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(MountainGeology)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain);
		}
	}
}
#endif
