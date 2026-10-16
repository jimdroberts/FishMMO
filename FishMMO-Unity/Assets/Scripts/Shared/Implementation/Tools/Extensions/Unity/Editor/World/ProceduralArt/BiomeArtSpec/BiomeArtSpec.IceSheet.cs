#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Sheet</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceSheet()
		{
			// Ice Sheet (any tier, 2026-10-10): land buried under ice that never thaws. Wind-packed snow over glacier ice, blue ice where the wind scours it bare, nunatak rock breaking through on steep ground, ice cliffs at the calving front, a few calved blocks below them. Nothing grows
			// but crustose lichen on the nunataks; sastrugi ridge the snow, and the wind-scoured blue ice keeps the odd meteorite.
			return E("Ice Sheet", L(Ground.Snow, IceBlocks(0.01f, 24f, "Calved", "Slab"), Aligned(Blocks("Sastrugi", 0.05f, 10f, F(RockTypes.Firn, "Sastrugi"), 20f), -20f, 20f)),
				Ds(D(Ground.Ice, 72f, 2f, -1f, -1f, Stones(SmallRocks("Basalt"), 0.05f)), D(Ground.Frost, 96f), D(Ground.Rock, 48f, 3f, 30f, 90f, Plants("LichenClump", 0.3f, 160, 60f))),
				C(Ground.Ice, 38f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Water(BiomeWaterRole.IceSheet)
				.Wildlife(BiomeHabitat.Ice);
		}
	}
}
#endif
