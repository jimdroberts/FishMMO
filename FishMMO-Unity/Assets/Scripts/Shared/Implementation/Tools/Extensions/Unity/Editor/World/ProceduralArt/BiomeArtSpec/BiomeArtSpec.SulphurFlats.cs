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
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				/* Its vents (2026-10-25, I2q-b, decision 84): its sulphur dust is a deposit, not a vapour or a vented ash, so no site stood
				 * here; a site rate of its own gives it fumaroles (seven in ten, fuming yellow-white) and ponds of molten sulphur (three in
				 * ten), which wear an opaque melt look: amber in the shallows, dark red in the deep, drifting on the shared clock. */
				.Geothermal(0f, 0f, 0.7f, 0f, 0f, 0.3f, 0.35f, new UnityEngine.Color(0.925f, 0.902f, 0.753f, 1f))
				.WaterTint(new BiomeWaterLook(new UnityEngine.Color(0.941f, 0.753f, 0.251f, 1f), new UnityEngine.Color(0.627f, 0.251f, 0.055f, 1f),
					new UnityEngine.Vector3(0.9f, 0.5f, 0.1f), 1f, new UnityEngine.Color(0.784f, 0.541f, 0.188f, 1f), new UnityEngine.Color(0.784f, 0.541f, 0.188f, 1f)))
				.HotWater()
				.MeltWater();
		}
	}
}
#endif
