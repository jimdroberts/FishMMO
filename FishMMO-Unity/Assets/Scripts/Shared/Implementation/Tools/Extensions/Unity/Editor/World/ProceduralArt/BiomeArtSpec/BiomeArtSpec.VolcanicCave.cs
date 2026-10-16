#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Volcanic Cave</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry VolcanicCave()
		{
			// An active lava tube with crystal-lined chambers: dribble spires, sulphur needles, Naica-like gypsum blades,
			// obsidian, glowing lava, ropy pahoehoe with scoria.
			return E("Volcanic Cave", L(Ground.Basalt, Stones(SmallRocks("Basalt"), 3f), Blocks("Obsidian", 0.06f, 10f, F(RockTypes.Obsidian, "Chunk", "Shard", "Scree")),
					Outcrops("Lava dribbles", 0.04f, 8f, F(RockTypes.Basalt, "Dribble"))),
				Ds(D(Ground.Lava, 96f, 5f),
					D(Ground.Ash, 48f, 2f, -1f, -1f, Outcrops("Sulphur crystals", 0.02f, 10f, F(RockTypes.Sulphur, "Crystals")), Outcrops("Gypsum", 0.006f, 16f, F(RockTypes.Gypsum, "Crystal"))),
					D(Ground.Pahoehoe, 64f, 2f, -1f, -1f, Blocks("Scoria", 0.02f, 10f, F(RockTypes.Scoria, "Lump")))),
				C(Ground.Basalt, 35f), Ground.Basalt, Ground.Basalt)
				.Accepts(VolcanicGeology)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Mountain | BiomeHabitat.Volcanic)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
