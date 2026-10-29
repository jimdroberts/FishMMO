#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Sulphuric Cloud Deck</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry SulphuricCloudDeck()
		{
			// Maxwell Montes: radar-bright metal frost with pyrite crystals, tessera fins in ridged fields, platy basalt slabs.
			return E("Sulphuric Cloud Deck", L(Ground.Sulphur, Stones(SmallRocks("Basalt"), 1f)),
				Ds(D(Ground.Basalt, 64f, 2f, -1f, -1f, Outcrops("Tessera fins", 0.02f, 16f, F(RockTypes.Basalt, "Fin"), 30f), Blocks("Platy slabs", 0.04f, 10f, F(RockTypes.Basalt, "Slabs"), 20f)),
					D(Ground.Ash, 96f),
					D(Ground.MetalFrost, 96f, 2f, -1f, -1f, Outcrops("Pyrite", 0.012f, 14f, F(RockTypes.Pyrite, "Cubes")))),
				C(Ground.Basalt), Ground.Sulphur, Ground.Sulphur)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Wet)
				// Acid fog off the cloud base (2026-10-25, I2q-b, decision 85), replacing the sulphur dust (that is the Sulphur Flats').
				.Emits("Acid Fog", 0.12f)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.None, 0f, 0f)
				.ErodesAs("Volcanic");
		}
	}
}
#endif
