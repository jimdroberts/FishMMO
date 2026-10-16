#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Scree</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Scree()
		{
			// Talus: scree specialists very sparse (alpine poppy, purple saxifrage, moss campion), lichen-crusted blocks.
			return E("Scree", L(Ground.Gravel, Stones(SmallRocks("Grey"), 10f), Stones(Pebbles("Grey"), 15f), Bush("MountainPine", 0.05f, 0.8f, 40f),
					Blocks("Slate scree", 0.06f, 10f, F(RockTypes.Slate, "Scree")), Blocks("Quartzite scree", 0.04f, 12f, F(RockTypes.Quartzite, "Scree")),
					Plants("FlowersAlpine", 0.3f, 160), Plants("CushionMossCampion", 0.4f)),
				Ds(D(Ground.Rock, 48f, 3f, 30f, 90f, Boulders(0.4f, 5f, Grey), Plants("LichenClump", 0.5f, 160, 60f)), D(Ground.Pebbles, 32f)),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(RockTypes.Quartzite, RockTypes.Slate, RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain);
		}
	}
}
#endif
