#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Volcanic</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Volcanic()
		{
			// Warm dry volcanic ground (Canaries, Hawaiʻi): lava lichen on the flows, euphorbia and dragon trees where it is
			// warm, ropy pahoehoe, cinder fields with scoria cones and bombs, sulphur fumaroles in the ash.
			return E("Volcanic", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Boulders(0.2f, 8f, BasaltRocks), Trees(0.02f, 20f, 25f, "Dead"),
					Outcrops("Basalt columns", 0.05f, 16f, F(RockTypes.Basalt, "Columns")), Blocks("Basalt causeway", 0.02f, 24f, F(RockTypes.Basalt, "Causeway", "Fallen"), 15f),
					Plants("LichenLava", 3f), Band(Plants("Euphorbia", 0.3f), 0.4f, 1f), Band(Trees(0.01f, 15f, 20f, "DragonTree"), 0.45f, 1f)),
				Ds(D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Pumice", 0.06f, 10f, F(RockTypes.Pumice, "Lump", "Raft")), Outcrops("Tuff chimneys", 0.015f, 30f, F(RockTypes.Tuff, "Chimney"), 15f),
						Outcrops("Sulphur cones", 0.006f, 30f, F(RockTypes.Sulphur, "Cone"), 20f)),
					D(Ground.Lava, 128f, 5f),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Scoria cones", 0.01f, 40f, F(RockTypes.Scoria, "Cone"), 20f), Blocks("Scoria bombs", 0.03f, 10f, F(RockTypes.Scoria, "Bomb", "Lump"))),
					D(Ground.Pahoehoe, 96f, 3f, 0f, 15f, Plants("LichenLava", 2f))),
				C(Ground.Basalt), Ground.Basalt, Ground.Basalt)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
