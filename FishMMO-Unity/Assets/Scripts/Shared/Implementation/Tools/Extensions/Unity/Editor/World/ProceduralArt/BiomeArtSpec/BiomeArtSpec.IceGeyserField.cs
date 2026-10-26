#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Geyser Field</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceGeyserField()
		{
			// Enceladus' tiger stripes: flank ridges along the vent fissures, ice vent cones, frost blooms, house-sized ice boulders.
			return E("Ice Geyser Field", L(Ground.Snow, Stones(SmallRocks("Grey"), 0.5f), IceBlocks(0.02f, 16f, "Calved"),
					With(Blocks("Fissure ridges", 0.012f, 14f, F(RockTypes.WaterIce, "Ridge"), 20f), r => r.Spacing = 10f)),
				Ds(D(Ground.Ice, 64f, 2f, -1f, -1f, Outcrops("Ice vent cones", 0.008f, 20f, F(RockTypes.WaterIce, "Cone"), 20f),
						With(IceBlocks(0.004f, 30f, "Calved", "Rounded"), r => { r.Scale = new Vector2(3f, 6f); r.Spacing = 20f; })),
					D(Ground.Frost, 48f, 2f, -1f, -1f, Outcrops("Frost blooms", 0.012f, 10f, F(RockTypes.WaterIce, "Crystal")))),
				C(Ground.Ice), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Alien)
				// Alien ground as well: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.Ice)
				.Moat(BiomeMoatWater.Frozen)
				/* Its plumes (2026-10-25, I2q-b, decision 53): jets laid a few dozen metres apart along its stored stripes (the TigerStripe
				 * landform, which its round authors; until a scene stores stripes its vents stand on the grid), breathing with the moon's
				 * plume tide; their ice falls back as plume frost (Cryo Tephra stays with the Cryovolcanic Plain and Tidal Fracture). */
				.Emits("Plume Frost", 0.15f)
				.Vents(BiomeVentLayout.Fissures, BiomeVentTiming.PlumeTide);
		}
	}
}
#endif
