#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rocky Coast</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RockyCoast()
		{
			// North Atlantic rocky shore (−33…5 °C): sea stacks, wrack and driftwood on the pebbles, orange and black
			// lichen above the splash zone, sea-thrift cushions and heather on the clifftop, gorse only where it is mild
			// and dwarf birch where it is arctic; mussel and barnacle bands in the intertidal.
			return E("Rocky Coast", L(Ground.Rock, Boulders(0.6f, 5f, Grey), Stones(SmallRocks("Grey"), 5f), Outcrops("Sea stacks", 0.002f, 60f, F(RockTypes.Granite, "Tor"), 20f)),
				Ds(D(Ground.Pebbles, 32f, 3f, 0f, 25f, Stones(Pebbles("Grey"), 10f), Blocks("Granite", 0.04f, 12f, F(RockTypes.Granite, "Corestone", "Split")),
						Plants("DebrisWrack", 2f, 128, 20f), Logs("Driftwood", 0.01f, 12f, Wood("Driftwood"), 20f)),
					D(Ground.GrassDry, 64f, 2f, 0f, 20f, Grass("GrassTuft", 12f), Band(Bush("Gorse", 0.5f), 0f, 1f),
						Plants("CushionThrift", 3f, 160, 45f), Bush("Heather", 1f), Band(Bush("DwarfBirch", 0.4f), -1f, -0.3f)),
					D(Ground.Lichen, 48f, 3f, 0f, 60f)),
				C(Ground.CliffRock, 38f), Ground.Pebbles, Ground.Gravel)
				.Under(Sea("MusselBed", 4f, 0f, 3f, 180, 60f), Sea("Barnacles", 4f, 0f, 3f, 180, 70f))
				.Accepts(RockTypes.Granite, RockTypes.Gneiss, RockTypes.Basalt, RockTypes.Slate, RockTypes.Schist, RockTypes.Quartzite)
				.Look(BiomeLookClass.Coastal)
				.Wildlife(BiomeHabitat.Coast | BiomeHabitat.Mountain);
		}
	}
}
#endif
