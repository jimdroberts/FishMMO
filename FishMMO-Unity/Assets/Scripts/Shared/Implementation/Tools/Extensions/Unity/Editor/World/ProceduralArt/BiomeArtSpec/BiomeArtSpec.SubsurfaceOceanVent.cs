#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Subsurface Ocean Vent</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SubsurfaceOceanVent()
		{
			return E("Subsurface Ocean Vent", L(Ground.Basalt), Ds(D(Ground.Silt, 48f), D(Ground.Sulphur, 24f, 4f)), C(Ground.Basalt), Ground.Silt, Ground.Silt)
				// Black smokers in vent fields and white Lost City carbonate towers; round them tube worms, vent clams and
				// bacterial mats on sulphide rubble. No corals: nothing at a vent builds a reef.
				.Under(Sea("TubeWorms", 6f, 0f, 0f, 200, 50f), Sea("Anemone", 1f, 0f, 0f, 128),
					Sea("MusselBedVentClam", 3f, 0f, 0f, 180, 40f), Sea("BacterialMat", 2f, 0f, 0f, 160, 30f), SeaStones(SmallRocks("Basalt"), 2f, 0f, 0f),
					Outcrops("Sulphide smokers", 0.01f, 16f, F(RockTypes.Sulphide, "Smoker"), 30f), Outcrops("Carbonate towers", 0.004f, 30f, F(RockTypes.Chalk, "Pinnacle"), 30f))
				.Moved("main", "Coral").Moved("lakebed", "Coral")
				.Look(BiomeLookClass.Aquatic)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Frozen);
		}
	}
}
#endif
