#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The stretches of a scene's rivers no way crosses and no bridge is laid over: each waterfall
	/// (<see cref="FallLedges.FindFalls"/>, the falls the curtain is drawn for) from a little above its lip to past its plunge
	/// pool, its channel only. The path planner reads them as open water (never crossed: the way goes round, above the lip
	/// or below the pool), and the bridge rule passes them by. A deck over a falling curtain served no way: a spur to the
	/// fall ended in its pool and the pool was bridged, and the rule's crossings never asked which reach they stood on.
	/// </summary>
	public sealed class FallReaches
	{
		/// <summary>How far above a fall's lip the reach begins, metres: a deck on the lip stands in the curtain's top.</summary>
		public const float AboveLipMetres = 6f;

		/// <summary>How far past the plunge point the reach runs, metres: the pool and its spray.</summary>
		public const float PastPlungeMetres = 12f;

		/// <summary>How far past the channel's half width the reach reaches, metres.</summary>
		public const float MarginMetres = 2f;

		private struct Segment
		{
			public Vector2 A, B;
			public float Half;
		}

		private struct Reach
		{
			public Rect Bounds;
			public int First, Count;
		}

		private readonly List<Segment> segments = new List<Segment>();
		private readonly List<Reach> reaches = new List<Reach>();

		/// <summary>True when there is no fall at all.</summary>
		public bool Empty => reaches.Count == 0;

		/// <summary>Every fall of <paramref name="rivers"/> of at least <paramref name="minDrop"/> metres.</summary>
		public static FallReaches Of(IReadOnlyList<RiverPath> rivers, float minDrop)
		{
			var falls = new FallReaches();
			if (rivers == null)
			{
				return falls;
			}
			foreach (RiverPath river in rivers)
			{
				if (river == null || river.Count < 2)
				{
					continue;
				}
				foreach (FallLedges.FallSpan fall in FallLedges.FindFalls(river, minDrop))
				{
					falls.Add(river, fall);
				}
			}
			return falls;
		}

		private void Add(RiverPath river, FallLedges.FallSpan fall)
		{
			// Up the river from the lip, and down it past the plunge, by the reach's own lengths along the channel.
			int from = fall.Lip;
			for (float run = 0f; from > 0 && run < AboveLipMetres; from--)
			{
				run += Vector2.Distance(Point(river, from), Point(river, from - 1));
			}
			int to = fall.Plunge;
			for (float run = 0f; to < river.Count - 1 && run < PastPlungeMetres; to++)
			{
				run += Vector2.Distance(Point(river, to), Point(river, to + 1));
			}
			if (to <= from)
			{
				return;
			}
			var reach = new Reach { First = segments.Count };
			float xMin = float.MaxValue, zMin = float.MaxValue, xMax = float.MinValue, zMax = float.MinValue;
			for (int k = from; k < to; k++)
			{
				float half = 0.5f * Mathf.Max(river.Width[k], river.Width[k + 1]) + MarginMetres;
				Vector2 a = Point(river, k), b = Point(river, k + 1);
				segments.Add(new Segment { A = a, B = b, Half = half });
				xMin = Mathf.Min(xMin, Mathf.Min(a.x, b.x) - half);
				zMin = Mathf.Min(zMin, Mathf.Min(a.y, b.y) - half);
				xMax = Mathf.Max(xMax, Mathf.Max(a.x, b.x) + half);
				zMax = Mathf.Max(zMax, Mathf.Max(a.y, b.y) + half);
			}
			reach.Count = segments.Count - reach.First;
			reach.Bounds = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
			reaches.Add(reach);
		}

		private static Vector2 Point(RiverPath river, int k) => new Vector2(river.X[k], river.Z[k]);

		/// <summary>True when (east, north) lies in a fall's reach, <paramref name="extra"/> metres past it included.</summary>
		public bool Contains(float east, float north, float extra = 0f)
		{
			var p = new Vector2(east, north);
			foreach (Reach reach in reaches)
			{
				Rect r = reach.Bounds;
				if (east < r.xMin - extra || east > r.xMax + extra || north < r.yMin - extra || north > r.yMax + extra)
				{
					continue;
				}
				for (int s = reach.First; s < reach.First + reach.Count; s++)
				{
					Segment seg = segments[s];
					Vector2 ab = seg.B - seg.A;
					float t = Mathf.Clamp01(Vector2.Dot(p - seg.A, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float reachOut = seg.Half + extra;
					if ((seg.A + ab * t - p).sqrMagnitude < reachOut * reachOut)
					{
						return true;
					}
				}
			}
			return false;
		}
	}
}
#endif
