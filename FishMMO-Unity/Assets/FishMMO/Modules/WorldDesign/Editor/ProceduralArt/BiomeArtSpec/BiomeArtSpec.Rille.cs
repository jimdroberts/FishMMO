#if UNITY_EDITOR
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;

using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public static partial class BiomeArtSpec
	{
		/// <summary>The <c>Rille</c> biome's entry (<see cref="Entries"/>, by its asset's name).</summary>
		private static Entry Rille()
		{
			// Hadley Rille: layered basalt ledges at the rim, talus down the walls, a dusty boulder-strewn floor, small craters.
			return E("Rille", L(Ground.Regolith, Stones(SmallRocks("Basalt"), 3f),
					With(Blocks("Crater rims", 0.002f, 24f, F(RockTypes.Breccia, "CraterRim"), 15f), r => { r.Spacing = 24f; r.Scale = new Vector2(0.35f, 0.8f); })),
				Ds(D(Ground.Basalt, 48f, 3f, 20f, 90f, Boulders(0.2f, 8f, BasaltRocks), Blocks("Basalt blocks", 0.05f, 12f, F(RockTypes.Basalt, "Block", "Fallen")),
						Outcrops("Basalt ledges", 0.03f, 16f, F(RockTypes.Basalt, "Ledges"), 45f), Blocks("Basalt talus", 0.03f, 14f, F(RockTypes.Basalt, "Scree"))),
					D(Ground.Gravel, 64f, 2f, -1f, -1f, Blocks("Breccia", 0.015f, 14f, F(RockTypes.Breccia, "Block", "Boulder"))),
					D(Ground.Dust, 96f, 2f, 0f, 10f, Stones(Pebbles("Basalt"), 2f))),
				C(Ground.Basalt), Ground.Regolith, Ground.Regolith)
				.Look(BiomeLookClass.Alien)
				// Alien ground alone: the client reads that live from the template's air (BiomeHabitats.Live), never stored.
				.Wildlife(BiomeHabitat.None)
				.Moat(BiomeMoatWater.Dry)
				// What the code tables decided by its name until 2026-10-26, written here by phase CODE-TABLE CLEARANCE (decision 1):
				// the sites it hosts and their variants, its gathering, weathering and terrain process, and what its ground gives the air.
				.Sites((POIType.Camp, 1f), (POIType.Shrine, 1f), (POIType.Bridge, 1f), (POIType.Spring, 1f), (POIType.Cave, 1f),
					(POIType.Monument, 1f), (POIType.Wreck, 1f), (POIType.Waterfall, 1f), (POIType.Rapids, 1f), (POIType.River, 1f),
					(POIType.Lake, 1f), (POIType.RiverMouth, 1f), (POIType.Delta, 1f), (POIType.Peak, 1f), (POIType.Pass, 1f), (POIType.Gorge, 1f),
					(POIType.Mesa, 1f), (POIType.Butte, 1f), (POIType.Island, 1f), (POIType.Bay, 1f), (POIType.Headland, 1f), (POIType.Grotto, 1f),
					(POIType.SeaCave, 1f), (POIType.BanditCamp, 1f), (POIType.FishingCamp, 1f), (POIType.MonsterDen, 1f), (POIType.RitualSite, 1f),
					(POIType.Temple, 1f), (POIType.LeyNexus, 1f), (POIType.FallenStar, 1f), (POIType.Portal, 1f), (POIType.Graveyard, 1f),
					(POIType.Crypt, 1f), (POIType.Ossuary, 1f), (POIType.Ruins, 1f), (POIType.RuinedTower, 1f), (POIType.Statue, 1f),
					(POIType.AncientRoad, 1f), (POIType.Hermitage, 1f), (POIType.Mine, 1f), (POIType.Quarry, 1f), (POIType.Lighthouse, 1f),
					(POIType.SmugglersCove, 1f), (POIType.PirateCove, 1f), (POIType.SunkenShip, 1f), (POIType.SunkenRuins, 1f),
					(POIType.SunkenCity, 1f), (POIType.DungeonEntrance, 1f), (POIType.BossLair, 1f), (POIType.OreVein, 1f),
					(POIType.CrystalFormation, 1f), (POIType.HerbGrove, 1f),
					// The fantasy wave (2026-10-27): its kinds here, and keeps and towers at least 1.
					(POIType.ElderMonolith, 0.6f), (POIType.WaygateRing, 0.3f))
				.Gathers("Voidstone Cluster")
				.Weathers(BiomeStructureFinish.Mossy, 0f, 0.5f)
				.ErodesAs("Barren")
				.Loose("Regolith Dust");
		}
	}
}
#endif
