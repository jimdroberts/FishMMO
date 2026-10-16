#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Tundra</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Tundra()
		{
			// Arctic tundra: dwarf birch and bilberry, reindeer lichen and arctic flowers; cottongrass and sedge tussocks on
			// the wet moss, moss campion on the frost-shattered gravel.
			return E("Tundra", L(Ground.Lichen, Grass("GrassTuft", 20f), Plants("ShrubSmall", 1f), Bush("Willow", 1f, 0.25f), Bush("Juniper", 0.15f, 0.3f), Stones(SmallRocks("Grey"), 2f),
					Bush("DwarfBirch", 1f, 0.7f), Bush("Bilberry", 2f), Plants("LichenClump", 4f), Plants("FlowersAlpine", 3f, 180)),
				Ds(D(Ground.Moss, 64f, 2f, -1f, -1f, Grass("GrassCotton", 10f), Grass("GrassSedge", 12f)), D(Ground.Gravel, 48f, 2f, -1f, -1f, Boulders(0.05f, 10f, GreyRound),
					Blocks("Granite erratics", 0.015f, 20f, F(RockTypes.Granite, "Corestone")), Blocks("Gneiss erratics", 0.015f, 20f, F(RockTypes.Gneiss, "Boulder")), Plants("CushionMossCampion", 2f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Basalt)
				.Look(BiomeLookClass.Cold)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Tundra);
		}
	}
}
#endif
