#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Mangrove</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Mangrove()
		{
			// Rhizophora/Avicennia forest in tidal mud: a closed low canopy of real mangroves on prop roots, a carpet of
			// pneumatophores, nipa palms along the channels, sea hibiscus and mangrove fern on the landward side, oysters
			// on the roots below the tide. (The jungle tree at a third of its size it stood in for is retired.)
			return E("Mangrove", L(Ground.Mud, Plants("Reeds", 10f), Stand(Was(1.5f, 6f), 15f, W(0.85f, 1f), "Mangrove"), Plants("Pneumatophores", 30f, 200, 15f)),
				Ds(D(Ground.Silt, 32f, 2f, -1f, -1f, Trees(0.05f, 6f, 10f, "Nipa")), D(Ground.JungleFloor, 48f, 2f, -1f, -1f, Plants("Fern", 6f), Bush("Hibiscus", 0.2f))),
				C(Ground.Soil), Ground.Silt, Ground.Mud)
				.Under(Sea("MusselBedOyster", 1.5f, 0f, 2f, 160, 20f))
				.Moved("main", "Trees: Jungle")
				.Accepts(TropicalGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Forest | BiomeHabitat.Wetland | BiomeHabitat.Coast);
		}
	}
}
#endif
