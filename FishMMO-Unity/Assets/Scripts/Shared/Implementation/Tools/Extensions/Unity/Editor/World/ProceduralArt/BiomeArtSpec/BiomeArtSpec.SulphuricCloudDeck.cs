#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Sulphuric Cloud Deck</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SulphuricCloudDeck()
		{
			// Maxwell Montes: radar-bright metal frost with pyrite crystals, tessera fins in ridged fields, platy basalt slabs.
			return E("Sulphuric Cloud Deck", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f)),
				Ds(D(Ground.Basalt, 64f, 2f, -1f, -1f, Outcrops("Tessera fins", 0.02f, 16f, F(RockTypes.Basalt, "Fin"), 30f), Blocks("Platy slabs", 0.04f, 10f, F(RockTypes.Basalt, "Slabs"), 20f)),
					D(Ground.Ash, 96f),
					D(Ground.MetalFrost, 96f, 2f, -1f, -1f, Outcrops("Pyrite", 0.012f, 14f, F(RockTypes.Pyrite, "Cubes")))),
				C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren);
		}
	}
}
#endif
