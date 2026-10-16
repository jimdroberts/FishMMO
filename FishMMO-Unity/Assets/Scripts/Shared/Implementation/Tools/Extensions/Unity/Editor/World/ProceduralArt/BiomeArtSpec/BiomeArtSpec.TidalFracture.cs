#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Tidal Fracture</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry TidalFracture()
		{
			// Europa's lineae: double ridges, rust-brown salts in the fractures, chaos rafts of broken plate, fresh frost
			// crystals, the penitentes predicted on the sunlit ice.
			return E("Tidal Fracture", L(Ground.Ice, Stones(SmallRocks("Grey"), 1f), IceBlocks(0.03f, 14f, "Calved", "Slab"),
					With(Blocks("Double ridges", 0.01f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
				Ds(D(Ground.Frost, 64f, 2f, -1f, -1f, With(IceBlocks(0.015f, 16f, "Slab"), r => r.Scale = new Vector2(1.8f, 3.5f)), Aligned(Outcrops("Penitentes", 0.004f, 24f, F(RockTypes.Firn, "Penitentes")), -10f, 10f)),
					D(Ground.Rock, 48f, 3f, 20f, 90f),
					D(Ground.Lineae, 48f, 3f, -1f, -1f, Outcrops("Frost crystals", 0.01f, 12f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice, 38f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren);
		}
	}
}
#endif
