#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Alpine Meadow</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry AlpineMeadow()
		{
			// Subalpine meadow at the treeline: gentians and edelweiss, lupin and globeflower, alpenrose; larch joins the
			// stunted spruce (one rule, so they share the treeline woods); sedge in the wet hollows.
			return E("Alpine Meadow", L(Ground.GrassMeadow, Grass("GrassLush", 30f), Plants("FlowersMeadow", 12f, 200), Stand(Was(0.05f, 12f), 25f, W(0.08f, 0.15f, open: 0.02f, scale: 0.65f), "Spruce", "Larch"), Bush("MountainPine", 0.4f), Bush("Juniper", 0.2f, 0.7f),
					Plants("FlowersAlpine", 8f, 200), Bush("Rhododendron", 0.4f, 0.35f), Plants("FlowersTall", 2f, 180)),
				Ds(D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.08f, 8f, GreyRound), Blocks("Gneiss", 0.03f, 12f, F(RockTypes.Gneiss, "Boulder", "Block"))), D(Ground.Moss, 64f, 2f, -1f, -1f, Grass("GrassSedge", 15f))),
				C(Ground.Rock), Ground.Gravel, Ground.Gravel)
				.Moved("main", "Trees: Spruce")
				.Accepts(MountainGeology)
				.Look(BiomeLookClass.Alpine)
				.Wildlife(BiomeHabitat.Grassland | BiomeHabitat.Mountain)
				.Moat(BiomeMoatWater.Wet);
		}
	}
}
#endif
