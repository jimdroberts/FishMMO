#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Mountain Slope</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MountainSlope()
		{
			// Montane to subalpine slopes from −15 to 33 °C: larch woods on the cold grassy slopes, bristlecone on the dry
			// rock, alpenrose where it is wet; alpine flowers in the turf, cones and needles under the pines.
			return E("Mountain Slope", L(Ground.Rock, Boulders(0.3f, 6f, Grey), Stones(SmallRocks("Grey"), 4f), Stand(Was(0.1f, 10f), 30f, W(0.1f, 0.35f, scale: 0.8f), "Pine"), Bush("MountainPine", 0.4f, 1f, 40f), Bush("Juniper", 0.15f, 0.7f, 40f),
					Outcrops("Slate upright", 0.03f, 14f, F(RockTypes.Slate, "Upright"), 30f), Blocks("Slate", 0.05f, 10f, F(RockTypes.Slate, "Stack")),
					Band(Trees(0.02f, 14f, 35f, "Bristlecone"), -1f, 0.45f, -1f, -0.1f), Band(Bush("Rhododendron", 0.3f, 0.35f, 40f), -1f, 1f, -0.1f, 1f), Plants("DebrisNeedle", 3f, 128)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Blocks("Slate scree", 0.04f, 12f, F(RockTypes.Slate, "Scree"))),
					D(Ground.GrassDry, 64f, 2f, 0f, 25f, Grass("GrassTuft", 10f), Band(Stand(Was(0.1f, 10f), 25f, W(0.25f, 0.35f, scale: 0.8f), "Larch"), -1f, 0.15f), Plants("FlowersAlpine", 3f, 180))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(RockTypes.Slate, RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Quartzite, RockTypes.Andesite)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Mountain);
		}
	}
}
#endif
