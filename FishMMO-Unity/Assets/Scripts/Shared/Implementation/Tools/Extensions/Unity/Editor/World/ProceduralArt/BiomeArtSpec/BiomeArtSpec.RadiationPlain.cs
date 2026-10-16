#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Radiation Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RadiationPlain()
		{
			// Callisto's dark lag: frost-capped knobs, radiolysis-browned ice, bright-rayed small craters, frost on shaded
			// slopes, ice grains. Fine dust in place of the cracked earth (mud cracks need water and clay).
			return E("Radiation Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 2f), Stones(Pebbles("Ice"), 2f),
					With(Outcrops("Frost-capped knobs", 0.01f, 20f, F(RockTypes.DarkLag, "FrostKnob"), 20f), r => r.Spacing = 16f)),
				Ds(D(Ground.Dust, 64f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.003f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)), D(Ground.Gravel, 48f),
					D(Ground.Frost, 48f, 2f, 15f, 40f), D(Ground.Lineae, 96f)),
				C(Ground.Rock), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
