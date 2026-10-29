#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>The fantasy wave's Dry pack (poi-fantasy-wave/WAVE.md): one row per kind, hosted by no biome by default.</summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyDryRows()
		{
			return new List<PointOfInterestKindRule>
			{
				// ── Pack G ──
				Placed(POIType.Grange, 0.05f, 0, 2, 1200f).Footprint(14f, 26f, 18f).Only().Races("Humanoid"),
				Placed(POIType.Dovecote, 0.08f, 0, 2, 700f).Footprint(14f, 26f, 18f).Only(),
				Placed(POIType.BeeGarden, 0.08f, 0, 2, 700f).Footprint(14f, 26f, 18f).Only(),
				Placed(POIType.VillageGreen, 0.08f, 0, 2, 700f).Footprint(14f, 26f, 12f).Only().NearSettlement(300f),
				Placed(POIType.ScarecrowField, 0.08f, 0, 2, 700f).Footprint(10f, 24f, 16f).Only().NearSettlement(800f).AwayFromSettlements(120f).Races("Undead", "Construct"),
				Placed(POIType.LongBarrow, 0.05f, 0, 2, 1200f).Footprint(10f, 24f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.Henge, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only(),
				Placed(POIType.GallowsHill, 0.08f, 0, 2, 700f).Footprint(10f, 24f, 16f).Only().OnExposedGround().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.KurganField, 0.05f, 0, 2, 1200f).Footprint(10f, 24f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.NomadCamp, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().Races("Humanoid", "Beastfolk"),
				Placed(POIType.StoneFigures, 0.08f, 0, 2, 700f).Footprint(12f, 26f, 18f).Only(),
				Placed(POIType.Kraal, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().Races("Humanoid", "Beastfolk"),
				Placed(POIType.Baobab, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only(),
				Placed(POIType.Shieling, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().Races("Humanoid"),
				// ── Pack A ──
				Placed(POIType.Necropolis, 0.02f, 0, 1, 2500f).Footprint(14f, 30f, 14f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.Pyramid, 0.02f, 0, 1, 2500f).Footprint(16f, 30f, 12f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.Sphinx, 0.05f, 0, 2, 1200f).Footprint(10f, 22f, 12f).Only().Races("Construct"),
				Placed(POIType.RockCutFacade, 0.05f, 0, 2, 1200f).Footprint(14f, 26f, 16f).Only().Hard(0.45f, true).AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.SandBuriedTemple, 0.05f, 0, 2, 1200f).Footprint(12f, 26f, 18f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.DjinnPrison, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only().AwayFromSettlements(500f).Races("Elemental", "Outsider"),
				Placed(POIType.CaravanHalt, 0.05f, 0, 2, 1200f).Footprint(14f, 26f, 14f).Only().NearWater(400f).Races("Humanoid", "Beastfolk"),
				Placed(POIType.BeastBoneyard, 0.08f, 0, 2, 700f).Footprint(10f, 24f, 16f).Only(),
				Placed(POIType.CliffPalace, 0.05f, 0, 2, 1200f).Footprint(16f, 28f, 16f).Only().Hard(0.45f, true).AwayFromSettlements(500f).Races("Humanoid", "Undead"),
				Placed(POIType.DreadRoad, 0.08f, 0, 2, 700f).Footprint(12f, 26f, 18f).Only(),
				// ── Pack H ──
				Placed(POIType.MountainKingsHall, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().Hard(0.35f).Races("Humanoid"),
				Placed(POIType.PassFort, 0.02f, 0, 1, 2500f).Footprint(16f, 28f, 18f).Only().NearLandform(POIType.Pass, 150f).OnExposedGround().Altitude(120f, PointOfInterestKindRule.Unbounded).Races("Humanoid"),
				Placed(POIType.Eyrie, 0.08f, 0, 2, 700f).Footprint(10f, 18f, 22f).Only().OnExposedGround().Altitude(30f, PointOfInterestKindRule.Unbounded).AwayFromSettlements(500f).Races("Beast", "Draconic"),
				Placed(POIType.GriffinRoost, 0.05f, 0, 2, 1200f).Footprint(10f, 18f, 22f).Only().OnExposedGround().Altitude(80f, PointOfInterestKindRule.Unbounded).AwayFromSettlements(500f).Races("Beast"),
				Placed(POIType.WyvernNest, 0.05f, 0, 2, 1200f).Footprint(10f, 18f, 22f).Only().OnExposedGround().Altitude(40f, PointOfInterestKindRule.Unbounded).AwayFromSettlements(500f).Races("Draconic"),
				Placed(POIType.CliffRelief, 0.05f, 0, 2, 1200f).Footprint(16f, 28f, 16f).Only().Hard(0.45f, true).AwayFromSettlements(400f),
				Placed(POIType.SummitCairn, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only().NearLandform(POIType.Peak, 40f).OnExposedGround().Altitude(150f, PointOfInterestKindRule.Unbounded),
				Placed(POIType.PrayerFlagPass, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only().NearLandform(POIType.Pass, 120f).OnExposedGround().Altitude(150f, PointOfInterestKindRule.Unbounded),
				Placed(POIType.SilverMineTown, 0.02f, 0, 1, 2500f).Footprint(30f, 50f, 16f).Only().Hard(0.4f).Races("Humanoid"),
			};
		}
	}
}
#endif
