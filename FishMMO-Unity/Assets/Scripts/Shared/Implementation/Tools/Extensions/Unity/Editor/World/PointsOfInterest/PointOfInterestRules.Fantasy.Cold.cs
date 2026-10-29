#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>
		/// The fantasy wave's Cold pack (poi-fantasy-wave/WAVE.md): one row per kind, hosted by no biome by default. Written at the density the
		/// kind wants before PointOfInterestRules.Default() scales it by FantasyWaveDensity. Coast kinds stand on low beaches or, for the cliff
		/// castle and the wreckers' beacon, on exposed high ground by the sea; the waters' kinds stand on the sea floor, a lake bed or a lake shore.
		/// </summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyColdRows()
		{
			return new List<PointOfInterestKindRule>
			{
				// ── Pack C ──
				Placed(POIType.FrostGiantSteading, 0.02f, 0, 1, 2500f).Footprint(14f, 26f, 22f).Only().AwayFromSettlements(500f).Races("Giant"),
				Placed(POIType.RuneStone, 0.08f, 0, 2, 700f).Footprint(12f, 26f, 18f).Only(),
				Placed(POIType.FrozenArmy, 0.05f, 0, 2, 1200f).Footprint(10f, 24f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.DragonInTheIce, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Draconic"),
				Placed(POIType.MammothGraveyard, 0.08f, 0, 2, 700f).Footprint(10f, 24f, 16f).Only(),
				Placed(POIType.WinterWolfDen, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 22f).Only().AwayFromSettlements(500f).Races("Beast"),
				Placed(POIType.AuroraShrine, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only(),
				Placed(POIType.LichSpire, 0.02f, 0, 1, 2500f).Footprint(10f, 24f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.IcePalace, 0.02f, 0, 1, 2500f).Footprint(12f, 26f, 18f).Only().AwayFromSettlements(500f).Races("Undead", "Elemental"),
				Placed(POIType.FrozenShipwreck, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 16f).Only().Coast(40f).Altitude(0.3f, 6f),
				// ── Pack Co ──
				// (2026-10-27) The tidal island abbey is a carved kind: its row is in PointOfInterestRules.Fantasy.Shapes.cs. The cliff castle and the
				// wreckers' light stand by a detected headland (NearLandform), their coast and exposure gates kept.
				Placed(POIType.CliffCastle, 0.02f, 0, 1, 2500f).Footprint(25f, 35f, 18f).Only().Coast(80f).NearLandform(POIType.Headland, 150f).OnExposedGround().Altitude(8f, 90f).AwayFromSettlements(400f).Races("Humanoid", "Beastfolk"),
				Placed(POIType.SmugglersTunnel, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 16f).Only().Hard(0.45f, true).Coast(60f).Altitude(0.5f, 25f).AwayFromSettlements(400f).Races("Humanoid"),
				Placed(POIType.WreckersBeacon, 0.08f, 0, 2, 700f).Footprint(12f, 20f, 16f).Only().Coast(60f).NearLandform(POIType.Headland, 120f).OnExposedGround().Altitude(6f, 80f).AwayFromSettlements(400f).Races("Humanoid"),
				Placed(POIType.SirenRock, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 16f).Only().Coast(30f).Altitude(0f, 8f).Races("Outsider"),
				Placed(POIType.SeaWitchGrotto, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 16f).Only().Hard(0.3f, true).Coast(40f).Altitude(0.5f, 14f).Races("Outsider", "Aquatic"),
				Placed(POIType.LongshipLanding, 0.05f, 0, 2, 1200f).Footprint(14f, 22f, 12f).Only().Coast(25f).Altitude(0.3f, 5f).AwayFromSettlements(500f).Races("Humanoid", "Beastfolk", "Giant"),
				Placed(POIType.KrakenWreck, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 16f).Only().Coast(25f).Altitude(0.5f, 4f),
				// ── Pack S ──
				Placed(POIType.MerfolkVillage, 0.02f, 0, 1, 2500f).Footprint(20f, 32f, 16f).Only().Sea(6f, 30f).Races("Aquatic"),
				Placed(POIType.KrakenGrounds, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 16f).Only().Sea(20f, 200f).Races("Aquatic", "Aberration"),
				Placed(POIType.LadyOfTheLake, 0.05f, 0, 2, 1200f).Footprint(8f, 16f, 18f).Only().NearWater(30f),
				Placed(POIType.NymphPool, 0.08f, 0, 2, 700f).Footprint(8f, 16f, 18f).Only().NearWater(20f),
				Placed(POIType.LakeMonsterLair, 0.05f, 0, 2, 1200f).Footprint(12f, 20f, 22f).Only().Sea(6f, 60f).UnderLakes().AwayFromSettlements(500f).Races("Aquatic", "Beast"),
			};
		}
	}
}
#endif
