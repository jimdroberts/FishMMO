#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Runaway Greenhouse Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RunawayGreenhousePlain()
		{
			// The Venera landing sites: platy layered basalt slabs over dark soil, lobate pahoehoe flows, cinder.
			return E("Runaway Greenhouse Plain", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 2f), Blocks("Basalt blocks", 0.03f, 16f, F(RockTypes.Basalt, "Block")),
					Blocks("Platy slabs", 0.08f, 8f, F(RockTypes.Basalt, "Slabs"), 20f)),
				Ds(D(Ground.Sulphur, 96f), D(Ground.Ash, 64f), D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Stones(Pebbles("Basalt"), 2f)), D(Ground.Cinder, 96f)),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				// Kiln haze (2026-10-25, I2q-b, decision 89), replacing the sulphur dust.
				.Emits("Kiln Haze", 0.06f);
		}
	}
}
#endif
