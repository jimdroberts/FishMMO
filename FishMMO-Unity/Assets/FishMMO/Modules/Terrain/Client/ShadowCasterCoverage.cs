using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Which instances the cached main-light shadows hold (<see cref="CachedMainLightShadows"/>): the instance ranges of the
	/// shadow work a redraw culled, merged, and whether a later frame's shadow work lies wholly inside them.
	/// </summary>
	/// <remarks>
	/// The props' gather keeps only chunks inside the camera's frustum or its shadow sweep (CliffRockInstancing), so the
	/// casters a redraw culls depend on where the camera looks. Stable cascades do not turn with the camera, so a camera that
	/// turns in place keeps every window and, before this, never redrew: props it turned towards were in neither the cache
	/// (never culled into it) nor URP's map (the batch group leaves static casters out of it while the cache serves), and
	/// their shadows were missing (browser, settlement close view, 2026-11-05). Now a frame whose shadow work reaches an
	/// instance the last redraw did not hold redraws (TerrainGpuRenderer.ShadowContentVersion).
	/// </remarks>
	public static class ShadowCasterCoverage
	{
		private sealed class ByStart : IComparer<Vector2Int>
		{
			public int Compare(Vector2Int a, Vector2Int b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
		}

		private static readonly ByStart Order = new ByStart();

		/// <summary>
		/// <paramref name="ranges"/> (instance ranges, x the first, y one past the last) sorted and merged into
		/// <paramref name="into"/>: disjoint, ascending, touching ranges joined. <paramref name="ranges"/> is sorted in place.
		/// </summary>
		public static void Merge(List<Vector2Int> ranges, List<Vector2Int> into)
		{
			into.Clear();
			ranges.Sort(Order);
			foreach (Vector2Int r in ranges)
			{
				if (r.y <= r.x)
				{
					continue;
				}
				int last = into.Count - 1;
				if (last >= 0 && r.x <= into[last].y)
				{
					into[last] = new Vector2Int(into[last].x, Mathf.Max(into[last].y, r.y));
				}
				else
				{
					into.Add(r);
				}
			}
		}

		/// <summary>Whether every range of <paramref name="ranges"/> lies wholly inside the merged <paramref name="held"/>.</summary>
		public static bool Covers(List<Vector2Int> held, List<Vector2Int> ranges)
		{
			foreach (Vector2Int r in ranges)
			{
				if (r.y > r.x && !Covers(held, r.x, r.y))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Whether [<paramref name="start"/>, <paramref name="end"/>) lies wholly inside one of the merged <paramref name="held"/>.</summary>
		public static bool Covers(List<Vector2Int> held, int start, int end)
		{
			// The last held range starting at or before start: merged ranges are disjoint, so only it can hold start.
			int lo = 0, hi = held.Count - 1, at = -1;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				if (held[mid].x <= start)
				{
					at = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			return at >= 0 && held[at].y >= end;
		}
	}
}
