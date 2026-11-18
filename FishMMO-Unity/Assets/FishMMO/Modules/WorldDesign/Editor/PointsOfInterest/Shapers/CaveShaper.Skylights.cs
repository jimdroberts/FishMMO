#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	public sealed partial class CaveShaper
	{
		// ── Skylights (2026-10-20, caves livened) ─────────────────

		/// <summary>How far an opening in a roof keeps from water (a river, a lake, the sea's reach), metres.</summary>
		private const float SkylightWaterClear = 10f;
		/// <summary>How far an opening in a roof keeps from another site's footprint, metres.</summary>
		private const float SkylightSiteClear = 12f;
		/// <summary>The ring round an opening kept clear of what grows, falls or is stood (trees, rocks), past its lip, metres.</summary>
		private const float SkylightKeepRing = 2f;

		/// <summary>
		/// Opens a planned system's skylights (<see cref="CaveSystem.TryAddSkylights"/>): over its great chambers where the rock
		/// over them is thin, never in water or on another site. Before a join, so a joined system brings its own along; the
		/// plan's mouth loop then holes the terrain over each and keeps its lip clear (<see cref="AddSkylightKeepOut"/>). Never
		/// graded (CaveSystem.GradesApproachOf), never a way's end (CaveSystem.LandingOf, <see cref="OwnMouths"/>).
		/// </summary>
		private static void TrySkylights(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveSystem system, CaveGround ground)
		{
			if (CaveSystem.SkylightShare(system.Form) <= 0f)
			{
				return;
			}
			bool Refused(float x, float z) => NearWater(context, x, z, SkylightWaterClear) || OnOtherSite(context, site.Id, x, z, SkylightSiteClear);
			int seed = unchecked(SeedOf(site) * 0x5B1D + 0x2E7F);
			int added = system.TryAddSkylights(ground, site.Id, seed, Refused, out _);
			if (added > 0)
			{
				context.Notes?.Add($"{PointOfInterestKinds.Info(site.Kind).DisplayName} at ({site.Position.x:F0}, {site.Position.z:F0}): {added} opening(s) in its roof to the sky.");
			}
		}

		/// <summary>
		/// Every opening in a cave's roof the stored shapes hold, as a pit on the ground (world middle, radius past its lip):
		/// what the ways are routed round (PathNetworkPlanner), even the cave's own. Read without building the systems.
		/// </summary>
		public static List<(Vector3 Middle, float Radius)> SkylightPits(IReadOnlyList<PointOfInterestShape> shapes)
		{
			var pits = new List<(Vector3, float)>();
			if (shapes == null)
			{
				return pits;
			}
			foreach (PointOfInterestShape shape in shapes)
			{
				if (shape == null || shape.Shaper != ShaperName || IsLink(shape.Values, out _))
				{
					continue;
				}
				foreach (CaveMouth mouth in CaveSystem.MouthsFromValues(shape.Values))
				{
					if (mouth.Kind == CaveMouthKind.Skylight)
					{
						pits.Add((shape.Position + CaveSystem.SkylightOpening(mouth), mouth.Radius * (1f + CaveSystem.SkylightFlare) + mouth.Collar + SkylightKeepRing));
					}
				}
			}
			return pits;
		}

		/// <summary>Nothing grows, falls or is stood in an opening in a roof or on the lip round it, on the ground over the cave.</summary>
		private static void AddSkylightKeepOut(PointOfInterestPlanContext context, PointOfInterestRecord site, CaveSystem system, in CaveMouth mouth)
		{
			Vector3 opening = system.Origin + CaveSystem.SkylightOpening(mouth);
			context.AddKeepOut(site, opening.x, opening.z, mouth.Radius * (1f + CaveSystem.SkylightFlare) + mouth.Collar + SkylightKeepRing);
		}
	}
}
#endif
