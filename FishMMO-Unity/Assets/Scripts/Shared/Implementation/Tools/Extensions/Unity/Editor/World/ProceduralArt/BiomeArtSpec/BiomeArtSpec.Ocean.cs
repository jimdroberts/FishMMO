#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Ocean</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Ocean()
		{
			return E("Ocean", L(Ground.Sand), Ds(D(Ground.Silt, 64f), D(Ground.Pebbles, 32f, 3f)), C(Ground.Rock), Ground.Sand, Ground.Sand)
				// The shelf: giant kelp only in cold water (no kelp forest on a warm shelf), sand-dollar beds in the
				// shallows, sea pens on the silt, mussel patches, holothurians and brittle stars on the sand.
				.Under(Band(Sea("Kelp", 3f, 4f, 30f, 160), -1f, 0.6f), Sea("Seaweed", 2f, 1.5f, 20f, 160), Sea("Sponge", 1f, 6f, 0f, 128), Sea("SeaFan", 0.5f, 8f, 120f, 128),
					Sea("Urchin", 1f, 2f, 40f, 128), Sea("Starfish", 0.5f, 1f, 0f, 96), Sea("Shells", 1f, 1f, 30f, 96),
					Sea("SandDollar", 2f, 2f, 20f, 160, 10f), Sea("SeaPen", 1f, 20f, 0f, 160, 15f), Sea("MusselBed", 1.5f, 1f, 15f, 180),
					Sea("SeaCucumber", 0.3f, 5f, 0f, 128), Sea("BrittleStar", 0.5f, 10f, 0f, 128),
					// Shell gravel and the odd glacial or reef stone lying on the shelf.
					SeaStones(Pebbles("Grey"), 1.5f, 2f, 0f), SeaStones(SmallRocks("Grey"), 0.5f, 4f, 0f))
				.Moved("main", "Kelp")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.HomeTo("Whale");
		}
	}
}
#endif
