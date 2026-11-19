using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// The line along a coast where the waves break, pulled out of a depth grid as clean, evenly
	/// spaced polylines for the breaker mesh to be built along.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a line at all.</b> The FFT sea is a single height field, so it cannot curl: a crest
	/// that throws a lip forward over its own trough is two heights at one point, which a height
	/// field has no way to hold. The breaking wave is therefore a separate mesh, and a mesh needs a
	/// path to be built along — the place where the water gets shallow enough to break, which is the
	/// contour depth == break depth of the shore field.
	/// </para>
	/// <para>
	/// <b>Which way the line runs is part of the answer.</b> A breaker faces the beach. Every
	/// polyline here runs with the SHALLOWER water on its left, so the shoreward normal at any point
	/// is simply the left normal (-dz, dx) of the direction of travel, with no search for which side
	/// the land is on. A round island's contour therefore runs anticlockwise, and a lagoon inside it
	/// runs clockwise. Every step after extraction — simplifying, smoothing, resampling — keeps the
	/// points in their order, so the contract survives the whole pipeline.
	/// </para>
	/// <para>
	/// <b>Pure, and safe off the main thread.</b> Nothing here touches a Unity object; <see
	/// cref="Vector2"/> is a plain struct. Every call builds its own state, so a rebuild can run in
	/// <c>Task.Run</c> while the old line is still being drawn.
	/// </para>
	/// </remarks>
	public static class WaterBreakLine
	{
		/// <summary>One piece of the contour.</summary>
		/// <remarks>
		/// World XZ, with x in <c>x</c> and z in <c>y</c> — the same convention the shore field's
		/// <see cref="WaterShoreField.Area"/> uses.
		/// </remarks>
		public sealed class Polyline
		{
			/// <summary>The points in order, shallow water to the left of the direction of travel.</summary>
			public readonly List<Vector2> Points = new List<Vector2>();

			/// <summary>
			/// True for a loop (last point connects back to the first; the first point is NOT
			/// repeated at the end).
			/// </summary>
			public bool Closed;

			/// <summary>Total length in metres, including the closing segment for a loop.</summary>
			/// <remarks>
			/// Computed on demand rather than cached: <see cref="Points"/> is a public list, and a
			/// cached length would go stale the moment a caller touched it.
			/// </remarks>
			public float Length
			{
				get { return (float)PathLength(Points, Closed); }
			}
		}

		/// <summary>
		/// Two points closer than this, in texels, are one point.
		/// </summary>
		/// <remarks>
		/// Only a sample lying exactly on the iso value puts two crossings in the same place (both
		/// edges meeting at it interpolate to the sample itself), and those come out bit-identical,
		/// because the arithmetic is done in grid space where a whole texel is exact. The margin is
		/// there for crossings a hair from a sample, which are the same point for any purpose a
		/// breaker has.
		/// </remarks>
		private const float DuplicateTexels = 1e-4f;

		/// <summary>
		/// The segments in each marching-squares cell, looked up by <c>(case &lt;&lt; 1) | centreShallow</c>,
		/// four entries each: from edge, to edge, from edge, to edge, with -1 for none.
		/// </summary>
		/// <remarks>
		/// Generated from the orientation rule rather than typed out, because sixteen hand-written
		/// cases is where a single flipped entry hides: one backwards cell splits a coastline in two
		/// and turns a piece of it round, and nothing else would notice. See <see cref="BuildSegmentTable"/>.
		/// </remarks>
		private static readonly sbyte[] SegmentTable = BuildSegmentTable();

		/// <summary>
		/// Marching squares over a grid of depths. Sample (x, y) is grid[y * width + x] and sits at world
		/// XZ = origin + (x, y) * texel. Returns every piece of the line where depth == iso, with linear
		/// interpolation along cell edges. Pieces that run off the grid's edge are open polylines.
		/// ORIENTATION CONTRACT: every polyline runs so that SHALLOWER water (depth &lt; iso) lies to its
		/// LEFT, where the left of a direction (dx, dz) is (-dz, dx). So for a circular island (depth
		/// grows with radius) the loop runs counter-clockwise (positive signed area in XZ with x right, z up).
		/// NaN samples must be treated as "deep" (&gt; iso) — never produce NaN points.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Shallow is <c>depth &lt; iso</c>, everything else is deep</b> — a sample exactly at the
		/// iso value, and a NaN, included. One strict comparison used everywhere means a sample can
		/// never be classified two ways by the two cells that share it, which is what keeps every
		/// piece joined. A sample exactly at iso makes the crossings on its edges land on the sample
		/// itself; those coincide exactly and are merged, so no piece has a zero-length segment.
		/// </para>
		/// <para>
		/// <b>How the direction falls out.</b> Walk a cell's boundary anticlockwise. Where the walk
		/// steps from shallow to deep, a piece of the line STARTS; where it steps from deep to
		/// shallow, one ENDS. A chord from start to end then has the anticlockwise arc from its end
		/// back to its start — the shallow arc — on its left. Two cells sharing an edge walk it in
		/// opposite directions, so a crossing that starts a segment in one cell ends one in the
		/// other, and the segments join head to tail with no reversing.
		/// </para>
		/// <para>
		/// <b>Saddles</b> (two shallow corners diagonally opposite) are settled by the average of the
		/// four corners: a shallow centre joins the shallow corners through the middle and cuts the
		/// deep ones off, a deep centre does the reverse. Either way the cut-off corners keep the
		/// rule above, so a saddle can only change which pieces connect, never which way they run.
		/// </para>
		/// <para>
		/// <b>Chained by edge, never by position.</b> Each crossing is one grid edge, and each edge
		/// is shared by at most two cells, so the join is exact: the cell that comes next in scan
		/// order finds the crossing its neighbour already made, from a row buffer. Matching points by
		/// float equality instead welds pieces that merely touch — at a sample exactly on the iso
		/// value, four edges can meet in one place.
		/// </para>
		/// <para>
		/// A null grid, a grid shorter than <paramref name="width"/> × <paramref name="height"/>, a
		/// side under two samples or a texel that is not a positive finite number returns an empty
		/// list. A negative texel would mirror the grid and turn every line round, so it is refused
		/// rather than honoured.
		/// </para>
		/// </remarks>
		public static List<Polyline> Extract(float[] grid, int width, int height, Vector2 origin, float texel, float iso)
		{
			var result = new List<Polyline>();
			if (grid == null || width < 2 || height < 2 || (long)width * height > grid.Length
				|| !(texel > 0f) || float.IsInfinity(texel))
			{
				return result;
			}

			// The crossings, in grid space, and the segment graph through them. Every crossing has at
			// most one segment leaving it and one arriving, so two index lists are the whole graph.
			var nodes = new List<Vector2>();
			var next = new List<int>();
			var prev = new List<int>();

			int cellsX = width - 1;
			// Node on each bottom edge of the current row of cells (made by the row below as its top
			// edges), and on each top edge, for the row above.
			var below = new int[cellsX];
			var above = new int[cellsX];
			for (int x = 0; x < cellsX; x++)
			{
				below[x] = -1;
			}
			var edgeNode = new int[4];

			for (int y = 0; y < height - 1; y++)
			{
				int row0 = y * width;
				int row1 = row0 + width;
				float d0 = grid[row0];
				float d3 = grid[row1];
				// The node on this cell's left edge, made by the cell before it as its right edge.
				int left = -1;
				for (int x = 0; x < cellsX; x++)
				{
					float d1 = grid[row0 + x + 1];
					float d2 = grid[row1 + x + 1];
					int shallow = (d0 < iso ? 1 : 0) | (d1 < iso ? 2 : 0) | (d2 < iso ? 4 : 0) | (d3 < iso ? 8 : 0);
					int right = -1;
					above[x] = -1;

					if (shallow != 0 && shallow != 15)
					{
						// Only read for saddles; a NaN corner makes the average NaN, and NaN is deep.
						bool centreShallow = (d0 + d1 + d2 + d3) * 0.25f < iso;
						int entry = ((shallow << 1) | (centreShallow ? 1 : 0)) * 4;
						edgeNode[0] = edgeNode[1] = edgeNode[2] = edgeNode[3] = -1;
						for (int s = 0; s < 4; s += 2)
						{
							int from = SegmentTable[entry + s];
							if (from < 0)
							{
								break;
							}
							int to = SegmentTable[entry + s + 1];
							int a = EdgeNode(from, x, y, d0, d1, d2, d3, iso, below, left, edgeNode, nodes, next, prev);
							int b = EdgeNode(to, x, y, d0, d1, d2, d3, iso, below, left, edgeNode, nodes, next, prev);
							next[a] = b;
							prev[b] = a;
						}
						right = edgeNode[1];
						above[x] = edgeNode[2];
					}

					left = right;
					d0 = d1;
					d3 = d2;
				}
				int[] swap = below;
				below = above;
				above = swap;
			}

			int count = nodes.Count;
			if (count == 0)
			{
				return result;
			}
			var visited = new bool[count];
			float duplicate = DuplicateTexels * DuplicateTexels;

			// Open pieces first. Each starts at a crossing nothing leads into, which can only be on the
			// grid's border: an inner crossing always has a cell on both sides.
			for (int i = 0; i < count; i++)
			{
				if (prev[i] < 0 && !visited[i])
				{
					Chain(i, nodes, next, visited, origin, texel, duplicate, result);
				}
			}
			// Whatever is left is a loop.
			for (int i = 0; i < count; i++)
			{
				if (!visited[i])
				{
					Chain(i, nodes, next, visited, origin, texel, duplicate, result);
				}
			}
			return result;
		}

		/// <summary>
		/// Douglas–Peucker; keeps endpoints of open lines; for loops, keeps the loop closed and never
		/// collapses below 3 points.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Marching squares puts a point on every grid edge the line crosses, so a straight stretch of
		/// coast arrives as dozens of points in a staircase a texel wide. This removes every point
		/// that lies within <paramref name="tolerance"/> metres of the line through its neighbours,
		/// which is the staircase, and keeps the ones that carry the shape.
		/// </para>
		/// <para>
		/// A loop has no endpoints, so it is split into two chains at its first point and the point
		/// farthest from it, and each chain is simplified with those two held fixed. The distance is
		/// measured to the SEGMENT, not the infinite line, so a spit that doubles back on itself is
		/// not mistaken for a straight edge. A tolerance that is not a positive number returns a copy.
		/// </para>
		/// </remarks>
		public static List<Vector2> Simplify(List<Vector2> points, bool closed, float tolerance)
		{
			var result = new List<Vector2>();
			if (points == null)
			{
				return result;
			}
			int n = points.Count;
			if (n < (closed ? 4 : 3) || !(tolerance > 0f))
			{
				result.AddRange(points);
				return result;
			}

			float tolerance2 = tolerance * tolerance;
			var keep = new bool[n];
			var stack = new List<int>();
			keep[0] = true;
			if (!closed)
			{
				keep[n - 1] = true;
				DouglasPeucker(points, 0, n - 1, tolerance2, keep, stack);
			}
			else
			{
				int far = 1;
				float farthest = -1f;
				for (int i = 1; i < n; i++)
				{
					float d = (points[i] - points[0]).sqrMagnitude;
					if (d > farthest)
					{
						farthest = d;
						far = i;
					}
				}
				keep[far] = true;
				// Index n is point 0 again: the second chain runs round through the closing segment.
				DouglasPeucker(points, 0, far, tolerance2, keep, stack);
				DouglasPeucker(points, far, n, tolerance2, keep, stack);

				int kept = 0;
				for (int i = 0; i < n; i++)
				{
					kept += keep[i] ? 1 : 0;
				}
				if (kept < 3)
				{
					// Both chains came out straight, which leaves a loop of two points: a line drawn
					// there and back. Put back the point farthest from it so the loop still encloses
					// something and still has a direction.
					int best = -1;
					float bestDistance = -1f;
					for (int i = 1; i < n; i++)
					{
						if (keep[i])
						{
							continue;
						}
						float d = SegmentDistanceSquared(points[i], points[0], points[far]);
						if (d > bestDistance)
						{
							bestDistance = d;
							best = i;
						}
					}
					if (best >= 0)
					{
						keep[best] = true;
					}
				}
			}

			for (int i = 0; i < n; i++)
			{
				if (keep[i])
				{
					result.Add(points[i]);
				}
			}
			return result;
		}

		/// <summary>
		/// Re-spaces points evenly along the line at (as close as possible to) this spacing. Open lines
		/// keep both endpoints exactly. Loops get an integer count of equal steps around the whole loop.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The breaker is a strip of profile rings, one per point, and a ring's spacing is the
		/// resolution of the curl along the coast. Uneven spacing shows up as a wave that is finely
		/// shaped in one place and faceted a metre further on, so the step is made the same all the
		/// way round rather than left to wherever simplification happened to keep a point.
		/// </para>
		/// <para>
		/// The step is the length divided by a whole number of steps, so it differs from
		/// <paramref name="spacing"/> by at most half a step spread over the whole line — and a loop
		/// meets itself without a short last segment. The points are placed at equal arc length, so
		/// the straight-line gap across a sharp corner is a little shorter than the step. A spacing
		/// that is not a positive finite number, or a line of no length, returns a copy.
		/// </para>
		/// </remarks>
		public static List<Vector2> Resample(List<Vector2> points, bool closed, float spacing)
		{
			var result = new List<Vector2>();
			if (points == null || points.Count == 0)
			{
				return result;
			}
			int n = points.Count;
			double length = PathLength(points, closed);
			if (n < 2 || !(spacing > 0f) || float.IsInfinity(spacing) || !(length > 0.0))
			{
				result.AddRange(points);
				return result;
			}

			// Capped so a spacing far too small for the line cannot ask for billions of points.
			double wanted = System.Math.Min(length / spacing, 1e7);
			int steps = System.Math.Max(closed ? 3 : 1, (int)System.Math.Round(wanted));
			double step = length / steps;
			int segments = closed ? n : n - 1;

			result.Capacity = steps + 1;
			result.Add(points[0]);
			int segment = 0;
			double segmentStart = 0.0;
			double segmentLength = Distance(points[0], points[1]);
			for (int k = 1; k < steps; k++)
			{
				double target = k * step;
				while (segment < segments - 1 && target > segmentStart + segmentLength)
				{
					segmentStart += segmentLength;
					segment++;
					segmentLength = Distance(points[segment], points[(segment + 1) % n]);
				}
				Vector2 a = points[segment];
				Vector2 b = points[(segment + 1) % n];
				double t = segmentLength > 0.0 ? (target - segmentStart) / segmentLength : 0.0;
				t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
				result.Add(a + (b - a) * (float)t);
			}
			if (!closed)
			{
				result.Add(points[n - 1]);
			}
			return result;
		}

		/// <summary>
		/// Chaikin corner cutting, this many iterations. Open lines keep both endpoints exactly.
		/// </summary>
		/// <remarks>
		/// Each pass replaces every corner with two points a quarter of the way along its two
		/// segments, which rounds it off without moving the line anywhere a straight stretch was
		/// already right. The new points lie on the old segments, in the old order, so the line stays
		/// on the same side of the water and runs the same way. An open line's end segments keep
		/// their outer point, so a piece that runs off the field still ends exactly on its edge.
		/// </remarks>
		public static List<Vector2> Smooth(List<Vector2> points, bool closed, int iterations)
		{
			var current = new List<Vector2>();
			if (points == null)
			{
				return current;
			}
			current.AddRange(points);
			for (int iteration = 0; iteration < iterations; iteration++)
			{
				int n = current.Count;
				if (n < 3)
				{
					break;
				}
				var cut = new List<Vector2>(n * 2);
				if (closed)
				{
					for (int i = 0; i < n; i++)
					{
						Vector2 p = current[i];
						Vector2 q = current[(i + 1) % n];
						cut.Add(p * 0.75f + q * 0.25f);
						cut.Add(p * 0.25f + q * 0.75f);
					}
				}
				else
				{
					cut.Add(current[0]);
					for (int i = 0; i < n - 1; i++)
					{
						Vector2 p = current[i];
						Vector2 q = current[i + 1];
						// The quarter point next to an endpoint would only duplicate the endpoint's
						// own straight segment, so it is left out at both ends.
						if (i > 0)
						{
							cut.Add(p * 0.75f + q * 0.25f);
						}
						if (i < n - 2)
						{
							cut.Add(p * 0.25f + q * 0.75f);
						}
					}
					cut.Add(current[n - 1]);
				}
				current = cut;
			}
			return current;
		}

		/// <summary>
		/// The whole pipeline: Extract, drop pieces shorter than minLength, Simplify(tolerance = 0.35 * texel),
		/// Smooth(1 iteration), Resample(spacing). Orientation must survive every step.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The order matters. Simplifying first throws the marching-squares staircase away before
		/// anything can round it into a wobble; one Chaikin pass then takes the corners off what is
		/// left; resampling last is what makes the spacing even, because both earlier steps leave
		/// points wherever the shape put them.
		/// </para>
		/// <para>
		/// The short pieces are dropped on their EXTRACTED length, before any smoothing shrinks them:
		/// a rock a couple of texels across makes a loop too small to carry a breaking wave, and a
		/// breaker curled round it reads as a glitch rather than surf.
		/// </para>
		/// </remarks>
		public static List<Polyline> Build(float[] grid, int width, int height, Vector2 origin, float texel,
			float iso, float spacing, float minLength)
		{
			List<Polyline> pieces = Extract(grid, width, height, origin, texel, iso);
			var result = new List<Polyline>(pieces.Count);
			float tolerance = 0.35f * texel;
			for (int i = 0; i < pieces.Count; i++)
			{
				Polyline piece = pieces[i];
				if (piece.Length < minLength)
				{
					continue;
				}
				List<Vector2> points = Simplify(piece.Points, piece.Closed, tolerance);
				points = Smooth(points, piece.Closed, 1);
				points = Resample(points, piece.Closed, spacing);
				if (points.Count < (piece.Closed ? 3 : 2))
				{
					continue;
				}
				var line = new Polyline { Closed = piece.Closed };
				line.Points.AddRange(points);
				result.Add(line);
			}
			return result;
		}

		/// <summary>
		/// Builds <see cref="SegmentTable"/> from the rule in <see cref="Extract"/>'s remarks.
		/// </summary>
		/// <remarks>
		/// Corner k of a cell is 0 (x, y), 1 (x+1, y), 2 (x+1, y+1), 3 (x, y+1) — anticlockwise —
		/// and edge k runs from corner k to corner k + 1: 0 bottom, 1 right, 2 top, 3 left. Bit k of
		/// the case is set when corner k is shallow.
		/// <list type="bullet">
		/// <item>Ordinary cells have one crossing where the anticlockwise walk goes shallow to deep
		/// (the start) and one where it goes deep to shallow (the end).</item>
		/// <item>Saddles (cases 5 and 10) cut off the two corners whose kind differs from the
		/// centre's. Cutting corner k joins edge k - 1 (arriving at it) and edge k (leaving it): a
		/// shallow corner is left of the chord from edge k to edge k - 1, a deep one right of the
		/// chord from edge k - 1 to edge k.</item>
		/// </list>
		/// </remarks>
		private static sbyte[] BuildSegmentTable()
		{
			var table = new sbyte[32 * 4];
			for (int i = 0; i < table.Length; i++)
			{
				table[i] = -1;
			}
			for (int shallow = 1; shallow < 15; shallow++)
			{
				for (int centre = 0; centre < 2; centre++)
				{
					int entry = ((shallow << 1) | centre) * 4;
					if (shallow == 5 || shallow == 10)
					{
						int written = 0;
						for (int k = 0; k < 4; k++)
						{
							bool cornerShallow = (shallow & (1 << k)) != 0;
							if (cornerShallow == (centre == 1))
							{
								// Joined to its diagonal partner through the centre, not cut off.
								continue;
							}
							sbyte leaving = (sbyte)k;
							sbyte arriving = (sbyte)((k + 3) & 3);
							table[entry + written] = cornerShallow ? leaving : arriving;
							table[entry + written + 1] = cornerShallow ? arriving : leaving;
							written += 2;
						}
					}
					else
					{
						for (int e = 0; e < 4; e++)
						{
							bool from = (shallow & (1 << e)) != 0;
							bool to = (shallow & (1 << ((e + 1) & 3))) != 0;
							if (from && !to)
							{
								table[entry] = (sbyte)e;
							}
							else if (!from && to)
							{
								table[entry + 1] = (sbyte)e;
							}
						}
					}
				}
			}
			return table;
		}

		/// <summary>
		/// The node for one edge of cell (x, y): the one a neighbour already made for it, or a new one.
		/// </summary>
		/// <remarks>
		/// Only the bottom and left edges can already exist — the cells below and to the left come
		/// first in scan order — and those come from the row buffer and the carried left node. The
		/// right and top edges are always new, and are handed on the same way.
		/// </remarks>
		private static int EdgeNode(int edge, int x, int y, float d0, float d1, float d2, float d3, float iso,
			int[] below, int left, int[] edgeNode, List<Vector2> nodes, List<int> next, List<int> prev)
		{
			int node = edgeNode[edge];
			if (node >= 0)
			{
				return node;
			}
			if (edge == 0)
			{
				node = below[x];
			}
			else if (edge == 3)
			{
				node = left;
			}
			if (node < 0)
			{
				Vector2 at;
				switch (edge)
				{
					case 0:
						at = new Vector2(x + Crossing(d0, d1, iso), y);
						break;
					case 1:
						at = new Vector2(x + 1, y + Crossing(d1, d2, iso));
						break;
					case 2:
						at = new Vector2(x + Crossing(d3, d2, iso), y + 1);
						break;
					default:
						at = new Vector2(x, y + Crossing(d0, d3, iso));
						break;
				}
				node = nodes.Count;
				nodes.Add(at);
				next.Add(-1);
				prev.Add(-1);
			}
			edgeNode[edge] = node;
			return node;
		}

		/// <summary>
		/// Where along an edge from sample <paramref name="a"/> to sample <paramref name="b"/> the
		/// depth passes <paramref name="iso"/>, as a fraction from a. Exactly one of the two is shallow.
		/// </summary>
		/// <remarks>
		/// Always measured from the lower-index sample, whichever cell is asking, so the two cells
		/// sharing an edge could never disagree about the point even if both computed it. A NaN
		/// (deep) or infinite sample has no meaningful crossing, and the middle of the edge is as
		/// good as anywhere.
		/// </remarks>
		private static float Crossing(float a, float b, float iso)
		{
			float s = (iso - a) / (b - a);
			return s >= 0f && s <= 1f ? s : 0.5f;
		}

		/// <summary>
		/// Follows the segment graph from <paramref name="start"/> and adds the piece it traces.
		/// </summary>
		private static void Chain(int start, List<Vector2> nodes, List<int> next, bool[] visited,
			Vector2 origin, float texel, float duplicate, List<Polyline> result)
		{
			var line = new Polyline();
			Vector2 first = nodes[start];
			Vector2 last = first;
			bool any = false;
			int node = start;
			do
			{
				visited[node] = true;
				Vector2 at = nodes[node];
				if (!any || (at - last).sqrMagnitude > duplicate)
				{
					line.Points.Add(origin + at * texel);
					last = at;
					any = true;
				}
				node = next[node];
			}
			while (node >= 0 && node != start && !visited[node]);

			line.Closed = node == start;
			if (line.Closed && line.Points.Count > 1 && (last - first).sqrMagnitude <= duplicate)
			{
				line.Points.RemoveAt(line.Points.Count - 1);
			}
			if (line.Points.Count >= (line.Closed ? 3 : 2))
			{
				result.Add(line);
			}
		}

		/// <summary>
		/// Douglas–Peucker over points[first..last], indices taken modulo the count so a loop's
		/// second chain can run through its closing segment. Iterative, so a long coast cannot
		/// overflow the stack of a worker thread.
		/// </summary>
		private static void DouglasPeucker(List<Vector2> points, int first, int last, float tolerance2,
			bool[] keep, List<int> stack)
		{
			int n = points.Count;
			stack.Clear();
			stack.Add(first);
			stack.Add(last);
			while (stack.Count > 0)
			{
				int j = stack[stack.Count - 1];
				int i = stack[stack.Count - 2];
				stack.RemoveRange(stack.Count - 2, 2);
				if (j - i < 2)
				{
					continue;
				}
				Vector2 a = points[i % n];
				Vector2 b = points[j % n];
				int index = -1;
				float farthest = -1f;
				for (int k = i + 1; k < j; k++)
				{
					float d = SegmentDistanceSquared(points[k % n], a, b);
					if (d > farthest)
					{
						farthest = d;
						index = k;
					}
				}
				if (farthest > tolerance2)
				{
					keep[index % n] = true;
					stack.Add(i);
					stack.Add(index);
					stack.Add(index);
					stack.Add(j);
				}
			}
		}

		/// <summary>Squared distance from p to the segment a–b.</summary>
		private static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			Vector2 ap = p - a;
			float lengthSquared = ab.sqrMagnitude;
			if (lengthSquared <= 0f)
			{
				return ap.sqrMagnitude;
			}
			float t = (ap.x * ab.x + ap.y * ab.y) / lengthSquared;
			t = t < 0f ? 0f : (t > 1f ? 1f : t);
			return (ap - ab * t).sqrMagnitude;
		}

		/// <summary>Length of a path in double precision, including the closing segment of a loop.</summary>
		private static double PathLength(List<Vector2> points, bool closed)
		{
			if (points == null || points.Count < 2)
			{
				return 0.0;
			}
			int n = points.Count;
			double length = 0.0;
			for (int i = 0; i < n - 1; i++)
			{
				length += Distance(points[i], points[i + 1]);
			}
			if (closed)
			{
				length += Distance(points[n - 1], points[0]);
			}
			return length;
		}

		private static double Distance(Vector2 a, Vector2 b)
		{
			double dx = (double)b.x - a.x;
			double dy = (double)b.y - a.y;
			return System.Math.Sqrt(dx * dx + dy * dy);
		}
	}
}
