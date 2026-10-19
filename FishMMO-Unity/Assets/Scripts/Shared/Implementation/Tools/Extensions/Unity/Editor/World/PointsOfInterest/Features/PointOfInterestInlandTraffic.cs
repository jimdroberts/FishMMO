#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Boats on lakes and rivers (decision 35 (e)/(j), 2026-10-18): which waters carry traffic and how fast it goes, as pure
	/// functions of the river's line and the shared clock, for the inland lanes the sea's traffic machinery will ply.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The plan (built in a later pass).</b> Inland traffic reuses the sea's: <see cref="FloatingBoats"/> lanes (a ship's pose a
	/// pure function of the world clock, solid hulls that carry riders), laid by an inland twin of PointOfInterestSeaTraffic after
	/// the sites — along each navigable river stretch (<see cref="NavigableStretches"/>: no fall or rapid, wide and deep enough)
	/// between its landings (ports, ferries, mills) or out past the scene's edge, and across each lake big enough to sail
	/// (<see cref="LakeCarries"/>) between its shore landings. A river lane's water is its own surface (falling with the river),
	/// so its points carry the river's height; there is no tide. What is left for that pass: a ONE-WAY lane mode in FloatingBoats (a
	/// log raft drifts down and is hidden at the scene's edge, never drifting back up), the inland hulls (Lädine, Oberländer barge,
	/// log raft, Nachen: the Lake and River rounds' pieces) and the stage hook beside the sea's.
	/// </para>
	/// <para>
	/// <b>Speeds from the river.</b> A raft drifts at the current, which changes along the river, and the shared-clock rule wants
	/// the motion integrated exactly over fixed knots: <see cref="DriftSeconds"/> integrates 1/v along the line with v linear between
	/// its points (closed form, ln), and <see cref="DriftAlong"/> inverts it, so a raft's place is a pure function of the clock that
	/// every client works out alike, at any frame rate. For a lane plied both ways at one speed (a towed barge, a punt) the lane's
	/// speed is the stretch's length over that drift time (<see cref="LaneSpeed"/>): the harmonic mean of the current, the speed
	/// the water itself would carry a boat down the stretch in the same time.
	/// </para>
	/// </remarks>
	public static class PointOfInterestInlandTraffic
	{
		/// <summary>The smallest lake that carries boats, hectares (Lädine barges and logboats: decision 35 (e)).</summary>
		public const float MinLakeHectares = 20f;
		/// <summary>The narrowest river a barge or a raft is taken down, metres.</summary>
		public const float MinRiverWidthMetres = 12f;
		/// <summary>The shallowest channel it is taken down, metres.</summary>
		public const float MinRiverDepthMetres = 1.2f;
		/// <summary>The shortest stretch worth a lane, metres.</summary>
		public const float MinStretchMetres = 200f;
		/// <summary>The slowest current counted: still water is taken as this, so a drift time is always finite, m/s.</summary>
		public const float MinCurrent = 0.05f;

		/// <summary>True when a lake of this area (square metres) carries boats.</summary>
		public static bool LakeCarries(float areaSquareMetres) => areaSquareMetres >= MinLakeHectares * 10000f;

		/// <summary>
		/// The river's navigable stretches, [from, to] point index pairs in order: runs of points with no fall or rapid, at least
		/// <see cref="MinRiverWidthMetres"/> wide and <see cref="MinRiverDepthMetres"/> deep, at least <see cref="MinStretchMetres"/> long,
		/// and not <paramref name="blocked"/> (a fall's reach, a weir).
		/// </summary>
		public static List<(int from, int to)> NavigableStretches(RiverPath river, Func<int, bool> blocked = null)
		{
			var stretches = new List<(int, int)>();
			if (river == null || !river.Perennial || river.Count < 2 || river.S == null || river.Width == null || river.Depth == null)
			{
				return stretches;
			}
			int start = -1;
			for (int i = 0; i <= river.Count; i++)
			{
				bool ok = i < river.Count && Navigable(river, i) && (blocked == null || !blocked(i));
				if (ok && start < 0)
				{
					start = i;
				}
				else if (!ok && start >= 0)
				{
					int end = i - 1;
					if (end > start && river.S[end] - river.S[start] >= MinStretchMetres)
					{
						stretches.Add((start, end));
					}
					start = -1;
				}
			}
			return stretches;
		}

		private static bool Navigable(RiverPath river, int i)
		{
			if (river.Reach != null && i < river.Reach.Length && (river.Reach[i] == RiverReach.Fall || river.Reach[i] == RiverReach.Rapid))
			{
				return false;
			}
			return river.Width[i] >= MinRiverWidthMetres && river.Depth[i] >= MinRiverDepthMetres;
		}

		/// <summary>
		/// Seconds the current takes to carry a boat from point <paramref name="from"/> to point <paramref name="to"/> (from ≤ to,
		/// downstream): ∫ ds / v with v linear in s between points (<paramref name="along"/> the distances, <paramref name="speed"/> the
		/// current at each, never below <see cref="MinCurrent"/>), exact on each segment.
		/// </summary>
		public static double DriftSeconds(float[] along, float[] speed, int from, int to)
		{
			double seconds = 0.0;
			for (int i = from; i < to; i++)
			{
				seconds += SegmentSeconds(along[i + 1] - along[i], Mathf.Max(MinCurrent, speed[i]), Mathf.Max(MinCurrent, speed[i + 1]));
			}
			return seconds;
		}

		/// <summary>The time to cross one segment of length <paramref name="ds"/> whose speed runs linearly from <paramref name="v0"/> to <paramref name="v1"/>: ds·ln(v1/v0)/(v1 − v0).</summary>
		public static double SegmentSeconds(double ds, double v0, double v1)
		{
			if (ds <= 0.0)
			{
				return 0.0;
			}
			double dv = v1 - v0;
			return Math.Abs(dv) < 1e-9 * Math.Max(v0, v1) ? ds / v0 : ds * Math.Log(v1 / v0) / dv;
		}

		/// <summary>
		/// Where the current has carried a boat <paramref name="seconds"/> after it left point <paramref name="from"/>: the distance
		/// along the river (same frame as <paramref name="along"/>), clamped to point <paramref name="to"/>. The exact inverse of
		/// <see cref="DriftSeconds"/> on each segment (s = s0 + v0·(e^{a·τ} − 1)/a with a = (v1 − v0)/ds).
		/// </summary>
		public static double DriftAlong(float[] along, float[] speed, int from, int to, double seconds)
		{
			if (seconds <= 0.0)
			{
				return along[from];
			}
			double left = seconds;
			for (int i = from; i < to; i++)
			{
				double ds = along[i + 1] - along[i];
				double v0 = Mathf.Max(MinCurrent, speed[i]), v1 = Mathf.Max(MinCurrent, speed[i + 1]);
				double cross = SegmentSeconds(ds, v0, v1);
				if (left < cross)
				{
					double a = (v1 - v0) / Math.Max(1e-9, ds);
					double travelled = Math.Abs(a) < 1e-12 ? v0 * left : v0 * (Math.Exp(a * left) - 1.0) / a;
					return along[i] + Math.Min(ds, travelled);
				}
				left -= cross;
			}
			return along[to];
		}

		/// <summary>
		/// The one speed a lane plied both ways is sailed at, m/s: the stretch's length over the current's drift time (the harmonic
		/// mean of the current), within <paramref name="min"/> … <paramref name="max"/> (a towed barge's pace).
		/// </summary>
		public static float LaneSpeed(float[] along, float[] speed, int from, int to, float min = 0.6f, float max = 2f)
		{
			double seconds = DriftSeconds(along, speed, from, to);
			float length = along[to] - along[from];
			return seconds > 0.0 ? Mathf.Clamp((float)(length / seconds), min, max) : min;
		}
	}
}
#endif
