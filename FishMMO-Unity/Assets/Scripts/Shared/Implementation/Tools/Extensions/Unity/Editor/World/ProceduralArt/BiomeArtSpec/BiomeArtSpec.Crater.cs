#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Crater</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Crater()
		{
			// A volcanic crater: fumarole cones with sulphur rims on the floor, sulphur crystals and scoria bombs on the cinder,
			// pioneer tufts, lava lichen on the walls.
			return E("Crater", L(Ground.Gravel, Stones(SmallRocks("Grey"), 4f), Outcrops("Fumarole cones", 0.006f, 20f, F(RockTypes.Sulphur, "Cone"), 20f), Grass("GrassTuft", 4f, 160)),
				Ds(D(Ground.Rock, 48f, 3f, 20f, 90f, Boulders(0.3f, 6f, GreyRound), Blocks("Andesite talus", 0.03f, 14f, F(RockTypes.Andesite, "Talus")), Plants("LichenLava", 3f, 128, 60f)),
					D(Ground.Ash, 64f, 2f, -1f, -1f, Blocks("Andesite", 0.05f, 12f, F(RockTypes.Andesite, "Block", "Platy")), Blocks("Pumice", 0.04f, 12f, F(RockTypes.Pumice, "Lump"))),
					D(Ground.Cinder, 64f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.015f, 10f, F(RockTypes.Sulphur, "Crystals")), Blocks("Scoria bombs", 0.02f, 10f, F(RockTypes.Scoria, "Bomb", "Lump")))),
				C(Ground.CliffRock, 38f), Ground.Gravel, Ground.Gravel)
				.Accepts(VolcanicGeology)
				// The class the old name reading gave: its name has no keyword, so its climate's middle (warm, middling humidity) gave Temperate; a per-biome round may make it Volcanic.
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic);
		}
	}
}
#endif
