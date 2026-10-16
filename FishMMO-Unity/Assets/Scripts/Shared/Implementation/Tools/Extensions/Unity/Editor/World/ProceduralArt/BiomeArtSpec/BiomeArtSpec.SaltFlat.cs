#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Salt Flat</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SaltFlat()
		{
			// Badwater, Uyuni, the Danakil: salt-polygon plates over the crust, Devil's Golf Course halite pinnacles, sailing
			// stones; selenite, saltbush, samphire and salt-crusted driftwood on the cracked margin; tamarisk by the springs.
			return E("Salt Flat", L(Ground.SaltCrust, With(Blocks("Salt polygons", 0.2f, 5f, F(RockTypes.Halite, "Polygons"), 8f), r => r.Spacing = 4.5f),
					Blocks("Halite pinnacles", 0.03f, 8f, F(RockTypes.Halite, "Pinnacles"), 15f), Stones(SmallRocks("Grey"), 0.3f)),
				Ds(D(Ground.CrackedEarth, 128f, 2f, -1f, -1f, Bush("Saltbush", 0.4f), Plants("ShrubSamphire", 3f), Outcrops("Gypsum", 0.005f, 16f, F(RockTypes.Gypsum, "Crystal")),
						Logs("Driftwood", 0.005f, 20f, Wood("Driftwood"))),
					D(Ground.Sand, 96f, 2f, -1f, -1f, Bush("Tamarisk", 0.08f))),
				C(Ground.Rock), Ground.SaltCrust, Ground.SaltCrust)
				.Look(BiomeLookClass.Desert)
				.Water(BiomeWaterRole.SaltFlat)
				.Wildlife(BiomeHabitat.Desert);
		}
	}
}
#endif
