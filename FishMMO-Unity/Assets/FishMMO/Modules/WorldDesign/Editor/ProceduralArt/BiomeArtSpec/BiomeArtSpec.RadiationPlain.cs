#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Radiation Plain</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry RadiationPlain()
		{
			// Callisto's dark lag: frost-capped knobs, radiolysis-browned ice, bright-rayed small craters, frost on shaded
			// slopes, ice grains. Fine dust in place of the cracked earth (mud cracks need water and clay).
			return E("Radiation Plain", L(Ground.Regolith, Stones(SmallRocks("Grey"), 2f), Stones(Pebbles("Ice"), 2f),
					With(Outcrops("Frost-capped knobs", 0.01f, 20f, F(RockTypes.DarkLag, "FrostKnob"), 20f), r => r.Spacing = 16f)),
				Ds(D(Ground.Dust, 64f, 2f, -1f, -1f, With(Blocks("Crater rims", 0.003f, 30f, F(RockTypes.Breccia, "CraterRim"), 20f), r => r.Spacing = 30f)), D(Ground.Gravel, 48f),
					D(Ground.Frost, 48f, 2f, 15f, 40f), D(Ground.Lineae, 96f)),
				C(Ground.Rock), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				.Wildlife(BiomeHabitat.Barren)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren")
				.Loose("Irradiated Dust");
		}
	}
}
#endif
