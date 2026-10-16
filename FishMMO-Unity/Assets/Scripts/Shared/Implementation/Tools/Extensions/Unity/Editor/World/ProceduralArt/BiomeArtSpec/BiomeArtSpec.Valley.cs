#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Valley</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Valley()
		{
			// Montane valley: meadow floor with tall herbs, alder, sedge and wet flowers along the wet bottoms, broadleaf
			// woods on the lower ground and larch on the colder upper slopes.
			return E("Valley", L(Ground.Grass, Grass("GrassLush", 45f, 220), Stand(Was(0.3f, 10f), 25f, W(0.3f, 0.7f, open: 0.05f), "Oak", "Birch"), Bush("Hazel", 0.3f), Bush("Bramble", 0.3f), Bush("Willow", 0.15f),
					Plants("FlowersTall", 2f, 180)),
				Ds(D(Ground.GrassMeadow, 64f, 2f, -1f, -1f, Plants("FlowersMeadow", 8f, 180)), D(Ground.Pebbles, 32f, 3f, 0f, 8f, Blocks("Conglomerate", 0.03f, 14f, F(RockTypes.Conglomerate, "Boulder", "Block"), 10f)),
					D(Ground.Mud, 48f, 2f, 0f, 10f, Stand(Was(0.3f, 8f), 15f, W(0.4f, 0.7f, open: 0.05f), "Alder"), Grass("GrassSedge", 15f), Plants("FlowersWet", 4f, 180)),
					D(Ground.NeedleLitter, 96f, 2f, 15f, 40f, Band(Stand(Was(0.3f, 8f), 35f, W(0.5f, 0.6f, open: 0.05f), "Larch"), -1f, 0.1f))),
				C(Ground.Rock), Ground.Mud, Ground.Pebbles)
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Shale, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Grassland);
		}
	}
}
#endif
