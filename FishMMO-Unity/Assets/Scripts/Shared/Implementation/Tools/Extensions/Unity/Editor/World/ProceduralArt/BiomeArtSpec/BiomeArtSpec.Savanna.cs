#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Savanna</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Savanna()
		{
			// East African savanna: tall golden grass swathes, umbrella acacias and the odd baobab in the hottest, thornbush
			// thickets, termite cathedrals, candelabra euphorbia round the kopjes.
			return E("Savanna", L(Ground.GrassDry, Grass("GrassDry", 40f, 220), Stand(Was(0.08f, 20f), 20f, W(0.25f, 0.15f, open: 0.25f), "Acacia"), Plants("ShrubDry", 2f), Outcrops("Granite kopjes", 0.01f, 30f, F(RockTypes.Granite, "Tor", "Perched"), 15f),
					Grass("GrassSavanna", 25f, 200), Band(Trees(0.01f, 30f, 15f, "Baobab"), 0.65f, 1f), Bush("Thornbush", 0.3f), Outcrops("Termite mounds", 0.03f, 20f, F(RockTypes.Termitaria, "Cathedral"), 20f), Plants("Euphorbia", 0.2f)),
				Ds(D(Ground.Soil, 96f, 2f, -1f, -1f, Bush("Thornbush", 0.6f)), D(Ground.Clay, 64f, 2f, -1f, -1f, Boulders(0.03f, 12f, Sandstone))),
				C(Ground.Sandstone), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Sandstone, RockTypes.Granite, RockTypes.Gneiss, RockTypes.Conglomerate, RockTypes.Shale)
				.Look(BiomeLookClass.Arid)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Scrub);
		}
	}
}
#endif
