#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>
		/// The fantasy wave's carved kinds (2026-10-27, poi-fantasy-wave/WAVE.md, cost C): each one's ground is cut by its own shaper
		/// (Shapers/FantasyShapers.cs, DungeonEntranceShaper for the dwarven gate), so its row asks for the ground the cut needs. Hosted
		/// by no biome by default (.Only()): the biomes' .Sites name them.
		/// </summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyShapesRows()
		{
			return new List<PointOfInterestKindRule>
			{
				// A cenote: a round sheer-sided well cut to the water in level ground (CenoteShaper); its footprint is the pit and its rim.
				Placed(POIType.Cenote, 0.05f, 0, 2, 1200f).Footprint(12f, 18f, 16f).Only(),
				// A sandworm's funnel in open sand (SandFunnelShaper): a cone at 30° from the lip, the boss's lair at its throat.
				Placed(POIType.SandwormPit, 0.05f, 0, 2, 1200f).Footprint(14f, 22f, 12f).Only().AwayFromSettlements(600f).Races("Beast"),
				// A dwarven gate: a great door set into a hard rock face (DungeonEntranceShaper at the gate's scale).
				Placed(POIType.DwarvenGate, 0.02f, 0, 1, 2500f).Footprint(12f, 20f, 22f).Only().Hard(0.5f, true).AwayFromSettlements(400f).Races("Humanoid"),
				// A hanging monastery: halls pinned on beams to a sheer face over its foot (FaceSnapShaper; the pieces carry their own beams).
				Placed(POIType.HangingMonastery, 0.05f, 0, 2, 1200f).Footprint(8f, 14f, 24f).Only().Hard(0.45f, true).AwayFromSettlements(300f).Races("Humanoid"),
				// A tidal island's abbey: on a small island the low tide joins to the shore by a sand causeway (CausewayShaper).
				Placed(POIType.TidalIslandAbbey, 0.02f, 0, 1, 2500f).Footprint(10f, 18f, 16f).Only().OnTidalIsland(150f).Races("Humanoid"),
			};
		}
	}
}
#endif
