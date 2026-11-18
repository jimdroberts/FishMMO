#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Methane Lake</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry MethaneLake()
		{
			// Titan's shores (Huygens): rounded water-ice cobbles, bright evaporite rings on the flats the lake left, tholin dunes.
			return E("Methane Lake", L(Ground.Frost, Stones(SmallRocks("Ice"), 4f), Stones(Pebbles("Ice"), 6f), IceBlocks(0.01f, 18f, "Rounded")),
				Ds(D(Ground.Ice, 64f), D(Ground.SaltCrust, 48f, 2f, 0f, 5f), D(Ground.TholinDune, 96f, 2f, -1f, -1f, Stones(Pebbles("Ice"), 2f))), C(Ground.Ice), Ground.Tholin, Ground.Tholin)
				.Under(SeaStones(Pebbles("Ice"), 4f, 0f, 8f))
				.Look(BiomeLookClass.Alien)
				.Water(BiomeWaterRole.MethaneLake)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Frozen)
				// Methane mist off the shores (2026-10-25, I2q-b, decision 62), replacing the tholin haze (that stays the Tholin Plain's).
				.Emits("Methane Mist", 0.1f)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Monument, 0.5f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f),
					(POIType.River, 1f), (POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f),
					(POIType.Gorge, 1f), (POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f),
					(POIType.SeaCave, 1f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f), (POIType.Ruins, 0.5f),
					(POIType.Obelisk, 0.5f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.OreVein, 0.6f),
					(POIType.CrystalFormation, 0.6f), (POIType.MethaneLake, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 1f)
				.ErodesAs("Wetland");
		}
	}
}
#endif
