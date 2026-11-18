#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where a way comes into a site laid out along streets, and where it ends inside it: one rule the way planner
	/// (<see cref="PathNetworkPlanner"/>) and the site's layout (<see cref="PointOfInterestLayouts"/>) both read, so the road
	/// ends on the street the layout runs in from that edge, and a walled site's gate stands on the road's own bearing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The road decides the street (2026-10-18).</b> A town's ways once came in only at its streets' ends, its heading and the
	/// opposite (and across in a big one), because its streets were a cross along its heading. A town grows along the roads that
	/// reach it, so now the road comes in on its own bearing toward the place it goes to, a second road within
	/// <see cref="MergeDegrees"/> of one already in shares its entrance (two roads meeting outside a gate), and the layout runs a
	/// street in from every entrance to the town's market.
	/// </para>
	/// <para>
	/// <b>The end.</b> A way's last point in the site is <see cref="EntryDepth"/> of the outline's reach in along its bearing,
	/// which the layout's entry street passes through. On a terraced pad it is held on the terrace its edge stands on, short of
	/// the bank, so the road never climbs a bank to its end: the entry street turns along that terrace to its street.
	/// </para>
	/// </remarks>
	public static class SiteEntrances
	{
		/// <summary>How far in along its bearing a way's last point stands, as a share of the outline's reach.</summary>
		public const float EntryDepth = 0.7f;

		/// <summary>A way within this many degrees of an entrance already made comes in by it.</summary>
		public const float MergeDegrees = 40f;

		/// <summary>How far past the outline's edge a way's route starts, metres.</summary>
		public const float OuterMetres = 8f;

		/// <summary>How far short of a terrace's bank (beyond the bank's own run) a way's end is held, metres.</summary>
		public const float BankClearMetres = 3f;

		/// <summary>The most entrances a site of a kind opens: a capital or a city six, a town four, anything else three.</summary>
		public static int MaxFor(POIType kind)
		{
			switch (kind)
			{
				case POIType.Capital:
				case POIType.City:
					return 6;
				case POIType.Town:
				case POIType.Port:
					return 4;
				default:
					return 3;
			}
		}

		/// <summary>
		/// The entrance (degrees in the site's frame, 0 its heading) a way coming in from <paramref name="worldBearing"/>
		/// (degrees, as a yaw) takes: the nearest of <paramref name="taken"/> within <see cref="MergeDegrees"/>, or the nearest of
		/// them once the site has <see cref="MaxFor"/>, else its own bearing, whole degrees.
		/// </summary>
		public static float Choose(PointOfInterestRecord record, float worldBearing, IReadOnlyList<float> taken)
		{
			float local = Mathf.Repeat(Mathf.Round(worldBearing - record.Yaw), 360f);
			if (taken == null || taken.Count == 0)
			{
				return local;
			}
			float best = float.NaN, bestGap = float.PositiveInfinity;
			foreach (float entrance in taken)
			{
				float gap = Mathf.Abs(Mathf.DeltaAngle(entrance, local));
				if (gap < bestGap)
				{
					bestGap = gap;
					best = entrance;
				}
			}
			return bestGap <= MergeDegrees || taken.Count >= MaxFor(record.Kind) ? best : local;
		}

		/// <summary>The unit direction (x east, y north) of an entrance in a site's frame.</summary>
		public static Vector2 Direction(PointOfInterestRecord record, float entrance)
		{
			float a = (record.Yaw + entrance) * Mathf.Deg2Rad;
			return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
		}

		/// <summary>How far the site's outline reaches toward an entrance, metres.</summary>
		public static float Reach(PointOfInterestRecord record, float entrance)
			=> PointOfInterestOutline.ReachToward(record.Outline, record.Radius, record.Yaw + entrance);

		/// <summary>
		/// How far in along an entrance's bearing a way's last point stands, metres from the site's centre:
		/// <see cref="EntryDepth"/> of the reach, or on a terraced pad no further in than the terrace the layout's edge stands on.
		/// </summary>
		public static float EndDistance(PointOfInterestRecord record, PointOfInterestPad? pad, float entrance)
		{
			float reach = Reach(record, entrance);
			float end = reach * EntryDepth;
			if (pad == null || !PointOfInterestTerraces.Terraced(pad.Value))
			{
				return end;
			}
			PointOfInterestPad p = pad.Value;
			Vector2 dir = Direction(record, entrance);
			float edge = reach * PointOfInterestLayouts.Inset;
			float uEdge = edge * Vector2.Dot(dir, p.TerraceAxis);
			float rate = Vector2.Dot(dir, p.TerraceAxis);
			if (Mathf.Abs(rate) < 1e-3f)
			{
				return end;
			}
			// In from the edge along the bearing u changes by −rate per metre: stop short of the first bank met on the way in.
			float most = edge - end;
			for (int s = 0; s < p.TerraceSteps.Length; s++)
			{
				float clear = Mathf.Max(PointOfInterestTerraces.BankHalf(p, s), PointOfInterestTerraces.RampHalf(p, s)) + BankClearMetres;
				float near = rate > 0f ? p.TerraceSteps[s] + clear : p.TerraceSteps[s] - clear;
				float metres = (uEdge - near) / rate;
				if (metres > 0f)
				{
					most = Mathf.Min(most, metres);
				}
			}
			return Mathf.Max(edge - Mathf.Max(0f, most), Mathf.Min(edge, end));
		}

		/// <summary>The site's pad, by its id, among <paramref name="pads"/>; null for none.</summary>
		public static PointOfInterestPad? PadOf(IReadOnlyList<PointOfInterestPad> pads, int id)
		{
			if (pads != null)
			{
				foreach (PointOfInterestPad pad in pads)
				{
					if (pad.Id == id)
					{
						return pad;
					}
				}
			}
			return null;
		}
	}
}
#endif
