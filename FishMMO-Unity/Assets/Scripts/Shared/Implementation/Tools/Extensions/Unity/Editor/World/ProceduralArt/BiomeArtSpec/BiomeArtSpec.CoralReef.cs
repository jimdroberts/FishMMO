#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Coral Reef</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry CoralReef()
		{
			return E("Coral Reef", L(Ground.Coral), Ds(D(Ground.SandBeach, 32f), D(Ground.Sand, 96f)),
				C(Ground.Limestone), Ground.Sand, Ground.Sand)
				.Under(Sea("Coral", 20f, 1f, 30f, 200), Sea("BrainCoral", 3f, 1f, 30f, 200), Sea("TableCoral", 2f, 1f, 15f, 200),
					Sea("SeaFan", 2f, 3f, 60f, 160), Sea("Sponge", 2f, 2f, 0f, 160), Sea("Anemone", 3f, 1f, 40f, 160),
					Sea("Urchin", 2f, 1f, 30f, 128), Sea("Starfish", 1f, 0.5f, 40f, 128), Sea("Shells", 3f, 0.5f, 25f, 128),
					// Soft corals among the reef builders, giant clams, white coral rubble, holothurians on the sand halos.
					Sea("SoftCoral", 4f, 1f, 30f, 200), Sea("GiantClam", 0.3f, 1f, 20f, 160), SeaStones(Pebbles("Coral"), 3f, 0.5f, 20f),
					Sea("SeaCucumber", 0.5f, 2f, 0f, 128))
				// No giant kelp on a tropical reef.
				.Moved("main", "Coral", "Kelp").Moved("lakebed", "Seagrass", "Kelp")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Sea)
				.HomeTo("Reef fish", "Sea turtle", "Crab");
		}
	}
}
#endif
