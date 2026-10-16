#if UNITY_EDITOR
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Peat Bog</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry PeatBog()
		{
			// Raised/blanket bog: a living red-green sphagnum carpet (Peat now only the hollows and cuttings) with
			// cottongrass, heather and sphagnum hummocks, dwarf birch and bilberry on the moss, stunted pine and birch
			// among the snags (one wood: the pine joins the birch and snags rather than standing as a second).
			return E("Peat Bog", L(Ground.Sphagnum, Plants("Reeds", 15f), Bush("Willow", 0.3f, 0.7f), Stand(Was(0.05f, 12f), 15f, W(0.1f, 0.12f, open: 0.03f, scale: 0.55f), "Dead", "Birch", "Pine"),
					Grass("GrassCotton", 10f, 200), Bush("Heather", 1.5f), Plants("CushionSphagnum", 3f, 160, 15f)),
				Ds(D(Ground.Moss, 48f, 2f, -1f, -1f, Grass("GrassTuft", 20f), Bush("DwarfBirch", 0.3f), Bush("Bilberry", 0.8f)), D(Ground.Mud, 64f),
					D(Ground.Peat, 32f, 3f)),
				C(Ground.Soil), Ground.Mud, Ground.Peat)
				.Moved("main", "Trees: Dead, Birch")
				.Accepts(BorealGeology)
				.Look(BiomeLookClass.Wetland)
				.Wildlife(BiomeHabitat.Wetland | BiomeHabitat.Tundra);
		}
	}
}
#endif
