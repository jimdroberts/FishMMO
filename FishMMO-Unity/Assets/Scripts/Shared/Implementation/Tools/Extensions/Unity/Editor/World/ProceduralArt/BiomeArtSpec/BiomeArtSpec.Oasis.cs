#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Oasis</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Oasis()
		{
			// A Saharan / Arabian spring oasis: date-palm groves (no coconut palm grows at a desert oasis) with their frond
			// litter, tamarisk and oleander (in place of hibiscus), Phragmites and sedges at the spring.
			return E("Oasis", L(Ground.Grass, Grass("GrassTall", 25f), Stand(Was(1.5f, 6f), 15f, W(0.6f, 0.9f, open: 0.05f, metres: 160f), "DatePalm"),
					Bush("Tamarisk", 0.3f), Bush("Oleander", 0.3f), Plants("DebrisPalm", 2f, 128, 20f)),
				Ds(D(Ground.Sand, 64f, 2f, -1f, -1f, Bush("Tamarisk", 0.15f)), D(Ground.Mud, 32f, 2f, 0f, 8f, Plants("Reeds", 12f), Plants("ReedsPlume", 10f, 200), Grass("GrassSedge", 15f))),
				C(Ground.Sandstone), Ground.Mud, Ground.Sand)
				.Moved("main", "Trees: Palm", "Bush: Hibiscus")
				.Accepts(RockTypes.Sandstone, RockTypes.Limestone, RockTypes.Conglomerate)
				.Look(BiomeLookClass.Jungle)
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Desert | BiomeHabitat.FreshWater);
		}
	}
}
#endif
