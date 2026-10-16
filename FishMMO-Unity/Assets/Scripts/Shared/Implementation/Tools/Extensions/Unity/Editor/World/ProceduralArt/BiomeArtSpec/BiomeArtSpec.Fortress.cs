#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Fortress</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Fortress()
		{
			// A mountain stronghold: moss cushions and alpine flowers in the stone's cracks, rubble, nettles at the middens,
			// stunted juniper and mountain pine outside the walls.
			return E("Fortress", L(Ground.Flagstone, Plants("CushionMoss", 3f, 128), Plants("FlowersAlpine", 1f, 160)),
				Ds(D(Ground.Gravel, 48f, 2f, -1f, -1f, Stones(SmallRocks("Grey"), 2f)),
					D(Ground.Grass, 32f, 2f, -1f, -1f, Grass("GrassTuft", 5f), Plants("HerbRuderal", 1.5f), Bush("Juniper", 0.1f, 0.7f), Bush("MountainPine", 0.08f))),
				C(Ground.CliffRock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Slate, RockTypes.Gneiss, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Settlement);
		}
	}
}
#endif
