#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Hills</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Hills()
		{
			// Rolling hills from −33 to 33 °C: temperate pasture and heather moor in the middle, garrigue (olive, rock-rose)
			// when warm, dwarf birch when cold. Hawthorn, bracken and heather each kept to the climate they grow in.
			return E("Hills", L(Ground.Grass, Grass("GrassLush", 35f, 220), Plants("FlowersMeadow", 4f), Stand(Was(0.1f, 12f), 25f, W(0.12f, 0.6f, open: 0.05f), "Oak"), Bush("Gorse", 0.5f), Bush("Juniper", 0.2f), Bush("Bramble", 0.15f), Outcrops("Granite tors", 0.008f, 40f, F(RockTypes.Granite, "Tor", "Perched"), 15f),
					Band(Bush("Hawthorn", 0.15f), -0.15f, 0.6f), Band(Trees(0.02f, 12f, 20f, "Olive"), 0.45f, 1f), Band(Bush("Cistus", 0.4f), 0.45f, 1f), Band(Bush("DwarfBirch", 0.5f), -1f, -0.25f)),
				Ds(D(Ground.Moss, 64f, 2f, -1f, -1f, Band(Plants("FernBracken", 3f), -0.2f, 0.7f)), D(Ground.Rock, 48f, 3f, 25f, 90f, Boulders(0.1f, 8f, GreyRound), Blocks("Granite corestones", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split"))),
					D(Ground.Heath, 64f, 2f, 0f, 30f, Band(Bush("Heather", 12f), -0.4f, 0.5f), Grass("GrassTuft", 10f))),
				C(Ground.Rock), Ground.Mud, Ground.Gravel)
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Schist, RockTypes.Slate, RockTypes.Limestone)
				.Look(BiomeLookClass.Temperate)
				.Wildlife(BiomeHabitat.Woodland | BiomeHabitat.Grassland | BiomeHabitat.Scrub);
		}
	}
}
#endif
