#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Wasteland</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Wasteland()
		{
			// Atacama-dead land: bleached snags of a lost wood, tumbleweed, varnished desert pavement, wind-faceted ventifacts.
			return E("Wasteland", L(Ground.CrackedEarth, Plants("ShrubDry", 3f), Trees(0.05f, 15f, 25f, "Dead"), Stones(SmallRocks("Grey"), 2f), Plants("ShrubTumbleweed", 0.5f, 128),
					Logs("Bleached logs", 0.01f, 16f, Wood("Driftwood"))),
				Ds(D(Ground.Ash, 64f), D(Ground.Gravel, 48f, 2f, -1f, -1f, Outcrops("Ventifacts", 0.01f, 16f, F(RockTypes.Basalt, "Ventifact"))),
					D(Ground.Reg, 96f, 2f, -1f, -1f, Stones(Pebbles("Grey"), 6f), Logs("Petrified logs", 0.01f, 16f, Wood("PetrifiedLog"), 20f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Look(BiomeLookClass.Volcanic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry);
		}
	}
}
#endif
