#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>The fantasy wave's Green pack (poi-fantasy-wave/WAVE.md): one row per kind, hosted by no biome by default.</summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyGreenRows()
		{
			return new List<PointOfInterestKindRule>
			{
				// Tuned 2026-10-27: the wet kinds keep to water (a crannog over lake shallows, a hydra's fen within reach of it); the rest are
				// ground-only and take their hosts from the biomes' .Sites. Cenote is a carved kind: its row is in PointOfInterestRules.Fantasy.Shapes.cs.
				// ── Pack W ──
				Placed(POIType.DruidGrove, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only().Races("Humanoid", "Plant"),
				Placed(POIType.TreeVillage, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().Races("Humanoid"),
				Placed(POIType.TrollStones, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only(),
				Placed(POIType.SpiderThicket, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Beast", "Aberration"),
				Placed(POIType.CharcoalClamps, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().Races("Humanoid"),
				Placed(POIType.GreenwoodHideout, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Humanoid"),
				Placed(POIType.DryadOak, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only().Races("Plant"),
				Placed(POIType.GoblinMarket, 0.05f, 0, 2, 1200f).Footprint(14f, 26f, 18f).Only().Races("Humanoid"),
				Placed(POIType.ElfWaystone, 0.08f, 0, 2, 700f).Footprint(12f, 26f, 18f).Only(),
				Placed(POIType.WolfDen, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Beast"),
				Placed(POIType.MossColossus, 0.05f, 0, 2, 1200f).Footprint(12f, 26f, 18f).Only(),
				// ── Pack J ──
				Placed(POIType.StepPyramid, 0.02f, 0, 1, 2500f).Footprint(18f, 28f, 18f).Only().AwayFromSettlements(500f).Races("Undead", "Humanoid"),
				Placed(POIType.JaguarTemple, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only().AwayFromSettlements(500f).Races("Beastfolk", "Undead"),
				Placed(POIType.SerpentIdol, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only(),
				Placed(POIType.LostCity, 0.02f, 0, 1, 2500f).Footprint(12f, 26f, 18f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.LizardfolkVillage, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().NearWater(80f).Races("Beastfolk"),
				Placed(POIType.IdolVault, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Construct", "Undead"),
				// ── Pack Wet ──
				Placed(POIType.Crannog, 0.05f, 0, 2, 1200f).Footprint(30f, 50f, 16f).Only().OverShallows().Races("Humanoid"),
				Placed(POIType.HagsCauldron, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only().NearWater(80f).AwayFromSettlements(500f).Races("Outsider", "Humanoid"),
				Placed(POIType.HydraLair, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 22f).Only().NearWater(40f).AwayFromSettlements(500f).Races("Draconic"),
				Placed(POIType.ReedVillage, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().NearWater(60f).Races("Humanoid"),
				Placed(POIType.CovenCircle, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only().AwayFromSettlements(500f).Races("Humanoid"),
				Placed(POIType.LizardfolkHatchery, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().NearWater(60f).AwayFromSettlements(500f).Races("Beastfolk"),
				Placed(POIType.SwampCathedral, 0.02f, 0, 1, 2500f).Footprint(12f, 26f, 18f).Only().NearWater(30f).AwayFromSettlements(500f).Races("Undead"),
				// ── Pack V ──
				Placed(POIType.FireTemple, 0.02f, 0, 1, 2500f).Footprint(8f, 16f, 18f).Only().Races("Humanoid", "Elemental"),
				Placed(POIType.FireGiantHall, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Giant"),
				Placed(POIType.SalamanderLair, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Elemental"),
				Placed(POIType.AshBuriedCity, 0.02f, 0, 1, 2500f).Footprint(12f, 26f, 18f).Only().AwayFromSettlements(500f).Races("Undead"),
			};
		}
	}
}
#endif
