#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class PointOfInterestRules
	{
		/// <summary>
		/// What the fantasy wave's rows' per-km² budgets are scaled by (2026-10-27). A biome's .Sites names 20–60 of the wave's
		/// kinds, and each kind is budgeted on its own (PointOfInterestPlanner.HostedAreaKm2), so at their packs' own densities
		/// the wave alone would double a scene's sites; at a half it adds about a third, the rest of the variety coming from
		/// which kinds a scene draws. Keeps and towers are not wave rows and are not scaled.
		/// </summary>
		public const float FantasyWaveDensity = 0.5f;

		/// <summary>
		/// The fantasy wave's rows (2026-10-27, poi-fantasy-wave/WAVE.md): every one hosted by no biome by default (.Only()), so a
		/// kind stands only where a biome's .Sites list names it. Each pack refines its own rows in its own file; the budgets are
		/// scaled here by <see cref="FantasyWaveDensity"/>. The carved kinds' rows (the Shapes file) come last: a later row of a kind wins.
		/// </summary>
		private static IEnumerable<PointOfInterestKindRule> FantasyRows()
		{
			var packs = new List<IEnumerable<PointOfInterestKindRule>> { FantasyRealmRows(), FantasyGreenRows(), FantasyDryRows(), FantasyColdRows(), FantasyShapesRows() };
			foreach (IEnumerable<PointOfInterestKindRule> pack in packs)
			{
				foreach (PointOfInterestKindRule rule in pack)
				{
					rule.PerKm2 *= FantasyWaveDensity;
					yield return rule;
				}
			}
		}
	}
}
#endif
