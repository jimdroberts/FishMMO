#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>The fantasy wave's Realm pack (poi-fantasy-wave/WAVE.md): one row per kind, hosted by no biome by default.</summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyRealmRows()
		{
			return new List<PointOfInterestKindRule>
			{
				// ── Pack U ──
				// (2026-10-27) Refined from the default fill: the lairs and landmarks stay rare (tier-1 ~0.02/km2, Max 1), the roadside
				// kinds stand by the ways the path planner laid (OnWay / AtJunction: sited after the ways, PointOfInterestPlanner.PlaceBesideWays),
				// a troll's bridge is sited exactly like a Bridge (where a river crosses the way between two settlements), a beacon on exposed ground.
				Placed(POIType.DragonLair, 0.02f, 0, 1, 2500f).Footprint(16f, 24f, 24f).Only().AwayFromSettlements(900f).Races("Draconic"),
				Placed(POIType.TrollBridge, 0.05f, 0, 2, 1200f).Footprint(6f, 30f, 90f).Only().AtCrossing().Races("Giant"),
				Placed(POIType.GiantsSteading, 0.05f, 0, 2, 1500f).Footprint(30f, 42f, 20f).Only().AwayFromSettlements(700f).Races("Giant"),
				Placed(POIType.GoblinWarren, 0.07f, 0, 3, 900f).Footprint(16f, 24f, 24f).Only().AwayFromSettlements(500f).Races("Humanoid"),
				Placed(POIType.HauntedManor, 0.05f, 0, 2, 1200f).Footprint(22f, 32f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
				Placed(POIType.HangingTree, 0.08f, 0, 2, 700f).Footprint(8f, 14f, 18f).Only().AwayFromSettlements(300f).Races("Undead"),
				Placed(POIType.BuriedHoard, 0.08f, 0, 2, 700f).Footprint(6f, 10f, 24f).Only(),
				Placed(POIType.AmbushSite, 0.08f, 0, 2, 700f).Footprint(10f, 16f, 20f).Only().OnWay(4f).AwayFromSettlements(400f).Races("Humanoid", "Beastfolk"),
				Placed(POIType.WreckedCaravan, 0.08f, 0, 2, 700f).Footprint(10f, 16f, 20f).Only().OnWay(8f).AwayFromSettlements(300f),
				Placed(POIType.Crossroads, 0.08f, 0, 2, 900f).Footprint(6f, 10f, 20f).Only().AtJunction(10f).AwayFromSettlements(150f),
				Placed(POIType.BoundaryStone, 0.08f, 0, 2, 700f).Footprint(5f, 9f, 22f).Only(),
				Placed(POIType.SignalBeacon, 0.05f, 0, 2, 1500f).Footprint(8f, 14f, 22f).Only().OnExposedGround().Races("Humanoid"),
				Placed(POIType.TollGate, 0.08f, 0, 2, 900f).Footprint(10f, 16f, 16f).Only().OnWay(3f).AwayFromSettlements(250f).Races("Humanoid"),
				Placed(POIType.Hamlet, 0.05f, 0, 2, 1200f).Footprint(34f, 50f, 14f).Only().AwayFromSettlements(600f).Races("Humanoid", "Beastfolk"),
				Placed(POIType.ManorHouse, 0.05f, 0, 2, 1200f).Footprint(22f, 32f, 14f).Only().AwayFromSettlements(350f).Races("Humanoid"),
				Placed(POIType.SiegeCamp, 0.04f, 0, 2, 1200f).Footprint(20f, 30f, 18f).Only().NearSettlement(700f).Races("Humanoid", "Beastfolk"),
				Placed(POIType.BurnedVillage, 0.05f, 0, 2, 1200f).Footprint(22f, 36f, 16f).Only().AwayFromSettlements(600f),
				Placed(POIType.WarbandCamp, 0.06f, 0, 2, 1000f).Footprint(20f, 30f, 20f).Only().AwayFromSettlements(700f).Races("Humanoid", "Beastfolk", "Giant"),
				Placed(POIType.CultistHideout, 0.05f, 0, 2, 1200f).Footprint(8f, 14f, 20f).Only().AwayFromSettlements(600f).Races("Humanoid", "Outsider"),
				Placed(POIType.AncientWall, 0.05f, 0, 2, 1200f).Footprint(18f, 32f, 20f).Only(),
				Placed(POIType.SpellScar, 0.08f, 0, 2, 700f).Footprint(8f, 14f, 20f).Only(),
				Placed(POIType.PlanarRift, 0.04f, 0, 2, 1500f).Footprint(8f, 14f, 20f).Only().AwayFromSettlements(700f).Races("Outsider", "Elemental"),
				Placed(POIType.PetrifiedBattle, 0.05f, 0, 2, 1200f).Footprint(16f, 28f, 16f).Only().AwayFromSettlements(400f),
				Placed(POIType.TitansBones, 0.05f, 0, 2, 1200f).Footprint(16f, 28f, 18f).Only().AwayFromSettlements(400f),
				Placed(POIType.WishingStone, 0.08f, 0, 2, 700f).Footprint(5f, 9f, 18f).Only(),
				Placed(POIType.ElderMonolith, 0.05f, 0, 2, 1200f).Footprint(8f, 14f, 20f).Only(),
				Placed(POIType.WaygateRing, 0.02f, 0, 1, 2500f).Footprint(10f, 14f, 14f).Only().AwayFromSettlements(600f),
				Placed(POIType.GolemGraveyard, 0.05f, 0, 2, 1200f).Footprint(14f, 24f, 18f).Only().AwayFromSettlements(600f).Races("Construct"),
				Placed(POIType.KoboldDelving, 0.05f, 0, 2, 1200f).Footprint(10f, 16f, 24f).Only().Hard(0.45f, true).AwayFromSettlements(500f).Races("Humanoid"),
				Placed(POIType.LostLibrary, 0.05f, 0, 2, 1200f).Footprint(14f, 24f, 18f).Only().AwayFromSettlements(600f).Races("Undead", "Construct"),
				// ── Pack Hold ──
				Placed(POIType.Commandery, 0.05f, 0, 2, 1200f).Footprint(26f, 36f, 16f).Only().AwayFromSettlements(500f).Races("Humanoid"),
				Placed(POIType.LostThrone, 0.08f, 0, 2, 700f).Footprint(8f, 14f, 18f).Only(),
				Placed(POIType.RoyalMausoleum, 0.05f, 0, 2, 1200f).Footprint(24f, 32f, 16f).Only().AwayFromSettlements(500f).Races("Undead"),
			};
		}
	}
}
#endif
