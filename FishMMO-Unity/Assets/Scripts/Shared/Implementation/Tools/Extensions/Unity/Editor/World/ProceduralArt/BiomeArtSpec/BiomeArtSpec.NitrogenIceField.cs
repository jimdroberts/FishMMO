#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Nitrogen Ice Field</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry NitrogenIceField()
		{
			// Sputnik Planitia: convecting nitrogen ice in sublimation ripples, drifting water-ice rafts, sublimation pits,
			// bladed terrain on the highs, dark tholin streaks.
			return E("Nitrogen Ice Field", L(Ground.Frost, Aligned(Blocks("Sublimation ripples", 0.05f, 6f, F(RockTypes.Firn, "Sastrugi"), 15f), -20f, 20f), With(IceBlocks(0.01f, 20f, "Slab", "Rounded"), r => r.Scale = new Vector2(2f, 4f))),
				Ds(D(Ground.Snow, 64f, 2f, -1f, -1f, With(Blocks("Sublimation pits", 0.003f, 24f, F(RockTypes.Firn, "PitRim"), 15f), r => r.Spacing = 22f)),
					D(Ground.Ice, 96f, 2f, -1f, -1f, Aligned(Outcrops("Bladed terrain", 0.004f, 24f, F(RockTypes.Firn, "Penitentes"), 25f), -10f, 10f)),
					D(Ground.Tholin, 128f)),
				C(Ground.Ice), Ground.Frost, Ground.Frost)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Frozen)
				// Triton's geysers (2026-10-25, I2q-b, decision 66): dark plume soot, at most four plumes a scene, waking with the sun.
				.Emits("Plume Soot", 0.04f)
				.Vents(BiomeVentLayout.Grid, BiomeVentTiming.Solar, 4)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process.
				.Sites((POIType.Camp, 0.2f), (POIType.Shrine, 0.2f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 0.2f),
					(POIType.Monument, 0.5f), (POIType.Wreck, 0.2f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.SeaCave, 1f),
					(POIType.IceCave, 1f), (POIType.BanditCamp, 0.2f), (POIType.FishingCamp, 0.2f), (POIType.MonsterDen, 0.2f), (POIType.Nest, 0.2f),
					(POIType.RitualSite, 0.2f), (POIType.Temple, 0.2f), (POIType.LeyNexus, 0.5f), (POIType.FallenStar, 0.7f), (POIType.Portal, 0.5f),
					(POIType.Graveyard, 0.2f), (POIType.Crypt, 0.2f), (POIType.Ossuary, 0.2f), (POIType.Ruins, 0.5f), (POIType.RuinedTower, 0.2f),
					(POIType.Statue, 0.2f), (POIType.Obelisk, 0.5f), (POIType.AncientRoad, 0.2f), (POIType.Hermitage, 0.2f), (POIType.Mine, 0.2f),
					(POIType.Quarry, 0.2f), (POIType.Lighthouse, 0.2f), (POIType.SmugglersCove, 0.2f), (POIType.PirateCove, 0.2f),
					(POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f), (POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 0.2f),
					(POIType.BossLair, 0.2f), (POIType.OreVein, 0.6f), (POIType.CrystalFormation, 0.6f), (POIType.HerbGrove, 0.2f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren");
		}
	}
}
#endif
