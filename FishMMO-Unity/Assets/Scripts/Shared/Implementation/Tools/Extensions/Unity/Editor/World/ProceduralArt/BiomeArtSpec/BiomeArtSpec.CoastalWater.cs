#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Coastal Water</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CoastalWater()
		{
			return E("Coastal Water", L(Ground.Sand), Ds(D(Ground.Pebbles, 32f, 3f, -1f, -1f, Stones(SmallRocks("Grey"), 3f)), D(Ground.Silt, 64f)),
				C(Ground.Rock), Ground.Sand, Ground.Pebbles)
				.Under(Sea("Seaweed", 6f, 1f, 20f, 200), Sea("Kelp", 6f, 5f, 30f, 200),
					Sea("Urchin", 3f, 1.5f, 30f, 160), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 4f, 0.5f, 25f, 128),
					Sea("Anemone", 1f, 2f, 40f, 128), SeaStones(SmallRocks("Grey"), 2f, 0.5f, 0f),
					// Mussel and barnacle bands from the low tide down, sea lettuce in the shallows, sand dollars on the sand.
					Sea("MusselBed", 4f, 0f, 10f, 200, 60f), Sea("Barnacles", 3f, 0f, 3f, 180, 70f), Sea("SeaLettuce", 3f, 0.5f, 8f, 180),
					Sea("SandDollar", 1f, 3f, 20f, 160, 10f))
				.Moved("detail/0", "Kelp").Moved("lakebed", "Seagrass")
				.Accepts(WatersideGeology)
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.HomeTo("Sea turtle", "Crab")
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
