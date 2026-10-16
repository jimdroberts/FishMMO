#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ice Shelf</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry IceShelf()
		{
			// Frozen sea of a frozen-through world: flat shelf ice with pressure ridges, frost and drifted snow, a few stranded blocks and seracs.
			// Pressure ridges in lines across the ice, frost flowers on the refrozen leads, wind-aligned sastrugi on the snow.
			return E("Ice Shelf", L(Ground.Ice, IceBlocks(0.03f, 14f), Seracs(0.005f, 40f),
					Aligned(Blocks("Pressure ridges", 0.006f, 30f, F(RockTypes.WaterIce, "Ridge"), 10f), -15f, 15f), Blocks("Frost flowers", 0.05f, 4f, F(RockTypes.WaterIce, "FrostFlowers"), 10f)),
				Ds(D(Ground.Frost, 64f), D(Ground.Snow, 96f, 2f, -1f, -1f, Aligned(Blocks("Sastrugi", 0.06f, 8f, F(RockTypes.Firn, "Sastrugi"), 15f), -12f, 12f))),
				C(Ground.Ice, 38f), Ground.Ice, Ground.Ice)
				.Look(BiomeLookClass.Ice)
				.Wildlife(BiomeHabitat.Coast | BiomeHabitat.Ice);
		}
	}
}
#endif
