#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Sulphur Flats</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SulphurFlats()
		{
			// Io's sulphur plains and Dallol: sulphur chimneys and fumarole cones in vent fields, needle crystals, salt
			// terraces and polygons on the crust, SO₂ frost.
			return E("Sulphur Flats", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f), Outcrops("Sulphur chimneys", 0.02f, 10f, F(RockTypes.Sulphur, "Chimney"), 20f),
					Outcrops("Fumarole cones", 0.008f, 18f, F(RockTypes.Sulphur, "Cone"), 20f), Outcrops("Sulphur crystals", 0.03f, 8f, F(RockTypes.Sulphur, "Crystals"), 30f)),
				Ds(D(Ground.CrackedEarth, 64f, 2f, -1f, -1f, Blocks("Salt terraces", 0.006f, 20f, F(RockTypes.Halite, "Terrace"), 12f), Blocks("Salt polygons", 0.02f, 8f, F(RockTypes.Halite, "Polygons"), 8f)),
					D(Ground.Ash, 96f), D(Ground.Frost, 96f)),
				C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren);
		}
	}
}
#endif
