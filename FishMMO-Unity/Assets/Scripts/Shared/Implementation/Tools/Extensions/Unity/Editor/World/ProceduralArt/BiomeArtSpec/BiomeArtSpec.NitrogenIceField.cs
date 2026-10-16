#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Nitrogen Ice Field</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry NitrogenIceField()
		{
			// Sputnik Planitia: convecting nitrogen ice in sublimation ripples, drifting water-ice rafts, sublimation pits,
			// bladed terrain on the highs, dark tholin streaks.
			return E("Nitrogen Ice Field", L(Ground.Frost, Aligned(Blocks("Sublimation ripples", 0.05f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f), With(IceBlocks(0.01f, 20f, "Slab", "Rounded"), r => r.Scale = new Vector2(2f, 4f))),
				Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, With(Blocks("Sublimation pits", 0.003f, 24f, F(RockTypes.Firn, "PitRim"), 15f), r => r.Spacing = 22f)),
					D(Ground.Ice, 96f, 2f, -1f, -1f, Aligned(Outcrops("Bladed terrain", 0.004f, 24f, F(RockTypes.Firn, "Penitentes"), 25f), -10f, 10f)),
					D(Ground.Tholin, 128f)),
				C(Ground.Ice), Ground.Frost, Ground.Frost)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
