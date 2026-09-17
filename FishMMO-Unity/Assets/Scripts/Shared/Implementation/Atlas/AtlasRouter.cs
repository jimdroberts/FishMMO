using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Atlas
{
	/// <summary>A connection to draw between two scenes, by obstacle index.</summary>
	public struct AtlasRouteRequest
	{
		public int From;
		public int To;
		/// <summary>Where the line starts inside the first scene (a teleporter), if known: the exit is placed on the edge nearest it.</summary>
		public Vector2? FromHint;
		/// <summary>Where the line ends inside the second scene (an arrival point), if known.</summary>
		public Vector2? ToHint;

		public AtlasRouteRequest(int from, int to, Vector2? fromHint = null, Vector2? toHint = null)
		{
			From = from;
			To = to;
			FromHint = fromHint;
			ToHint = toHint;
		}
	}

	/// <summary>A drawn connection, in the plane's km.</summary>
	public sealed class AtlasRoute
	{
		public AtlasRouteRequest Request;
		/// <summary>
		/// From the edge of the first scene to the edge of the second: perpendicular exits, then a
		/// path pulled straight where nothing is in the way, with rounded corners.
		/// </summary>
		public readonly List<Vector2> Points = new List<Vector2>();
		/// <summary>False when no path avoided every scene: <see cref="Points"/> is then a straight line.</summary>
		public bool Clean;
		/// <summary>True when the path had to run along another route's lane.</summary>
		public bool SharesCorridor;
		/// <summary>
		/// True when the scenes are too close to route around but a straight line between their
		/// edges crosses nothing: clean, just short and direct.
		/// </summary>
		public bool Direct;
	}

	/// <summary>Tuning for <see cref="AtlasRouter"/>.</summary>
	public struct AtlasRouterSettings
	{
		public float CellKm;
		public float PaddingKm;
		public float MarginKm;
		public float ParallelCost;
		public float CrossCost;
		public int MaxCellsPerSide;
		/// <summary>How far each corner is rounded, in km. Kept below the padding so a rounded corner never reaches a scene.</summary>
		public float CornerKm;

		public static AtlasRouterSettings Default => new AtlasRouterSettings
		{
			CornerKm = 0.4f,
			CellKm = 0.25f,
			PaddingKm = 0.5f,
			MarginKm = 3f,
			ParallelCost = 400f,
			CrossCost = 3f,
			MaxCellsPerSide = 600,
		};
	}

	/// <summary>
	/// Routes connection lines between scenes on a flat km plane so they avoid every scene and
	/// each other.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each scene, grown by the padding, is a wall on a grid. Every connection leaves its scene
	/// from a port on the side facing the other scene; ports on one side are spread out and each
	/// port claims its cell, so two lines never leave through the same gap. A* then finds a path
	/// where running along an existing line is expensive and crossing one is cheap.
	/// </para>
	/// <para>
	/// When nothing avoids the scenes the route is a straight line marked not clean, and callers
	/// report it. The router never returns a clean route with a point inside a scene. Pure and
	/// deterministic: the designer and the in-game atlas draw the same lines.
	/// </para>
	/// </remarks>
	public static class AtlasRouter
	{
		private static readonly int[] StepX = { 1, 1, 0, -1, -1, -1, 0, 1 };
		private static readonly int[] StepY = { 0, 1, 1, 1, 0, -1, -1, -1 };

		private sealed class Grid
		{
			public int Width, Height;
			public float Cell;
			public Vector2 Origin;
			public bool[] Blocked;
			/// <summary>Bit per direction class (0 horizontal, 1 diagonal /, 2 vertical, 3 diagonal \) used by earlier routes.</summary>
			public byte[] Used;
			public bool[] Claimed;

			public int Index(int x, int y) => y * Width + x;
			public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
			public Vector2 Centre(int x, int y) => Origin + new Vector2((x + 0.5f) * Cell, (y + 0.5f) * Cell);

			public void CellOf(Vector2 p, out int x, out int y)
			{
				x = Mathf.Clamp(Mathf.FloorToInt((p.x - Origin.x) / Cell), 0, Width - 1);
				y = Mathf.Clamp(Mathf.FloorToInt((p.y - Origin.y) / Cell), 0, Height - 1);
			}
		}

		private struct Port
		{
			public Vector2 Edge;
			public Vector2 Outside;
			public int X, Y;
			public bool Valid;
		}

		/// <summary>Routes every request over the given scene polygons (convex, km).</summary>
		public static List<AtlasRoute> Route(IReadOnlyList<Vector2[]> scenes, IReadOnlyList<AtlasRouteRequest> requests, AtlasRouterSettings settings)
		{
			var routes = new List<AtlasRoute>();
			if (scenes == null || requests == null || requests.Count == 0)
			{
				return routes;
			}

			Grid grid = BuildGrid(scenes, settings);

			// Short connections first: they have the fewest ways around and claim the obvious lanes.
			var order = new List<int>(requests.Count);
			for (int i = 0; i < requests.Count; i++)
			{
				order.Add(i);
			}
			order.Sort((a, b) =>
			{
				float da = DistanceOf(scenes, requests[a]);
				float db = DistanceOf(scenes, requests[b]);
				return da != db ? da.CompareTo(db) : a.CompareTo(b);
			});

			Port[,] ports = AssignPorts(grid, scenes, requests, settings);
			var results = new AtlasRoute[requests.Count];
			foreach (int i in order)
			{
				results[i] = RouteOne(grid, scenes, requests[i], ports[i, 0], ports[i, 1], settings);
			}
			routes.AddRange(results);
			return routes;
		}

		private static float DistanceOf(IReadOnlyList<Vector2[]> scenes, AtlasRouteRequest r)
		{
			if (!Valid(scenes, r))
			{
				return float.MaxValue;
			}
			return Vector2.Distance(Centroid(scenes[r.From]), Centroid(scenes[r.To]));
		}

		private static bool Valid(IReadOnlyList<Vector2[]> scenes, AtlasRouteRequest r)
		{
			return r.From >= 0 && r.To >= 0 && r.From < scenes.Count && r.To < scenes.Count && r.From != r.To
				&& scenes[r.From] != null && scenes[r.To] != null && scenes[r.From].Length >= 3 && scenes[r.To].Length >= 3;
		}

		public static Vector2 Centroid(IReadOnlyList<Vector2> polygon)
		{
			Vector2 sum = Vector2.zero;
			for (int i = 0; i < polygon.Count; i++)
			{
				sum += polygon[i];
			}
			return polygon.Count > 0 ? sum / polygon.Count : sum;
		}

		private static Grid BuildGrid(IReadOnlyList<Vector2[]> scenes, AtlasRouterSettings settings)
		{
			Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
			foreach (Vector2[] polygon in scenes)
			{
				if (polygon == null)
				{
					continue;
				}
				foreach (Vector2 p in polygon)
				{
					min = Vector2.Min(min, p);
					max = Vector2.Max(max, p);
				}
			}
			if (min.x > max.x)
			{
				min = max = Vector2.zero;
			}
			float margin = settings.MarginKm + settings.PaddingKm;
			min -= new Vector2(margin, margin);
			max += new Vector2(margin, margin);

			float cell = Mathf.Max(0.01f, settings.CellKm);
			float longest = Mathf.Max(max.x - min.x, max.y - min.y);
			int limit = Mathf.Max(16, settings.MaxCellsPerSide);
			if (longest / cell > limit)
			{
				cell = longest / limit;
			}

			var grid = new Grid
			{
				Cell = cell,
				Origin = min,
				Width = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / cell)),
				Height = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / cell)),
			};
			int count = grid.Width * grid.Height;
			grid.Blocked = new bool[count];
			grid.Used = new byte[count];
			grid.Claimed = new bool[count];

			// A cell is a wall when any part of it could be within the padding of a scene.
			float reach = settings.PaddingKm + cell * 0.7072f;
			foreach (Vector2[] polygon in scenes)
			{
				if (polygon == null || polygon.Length < 3)
				{
					continue;
				}
				Vector2 pMin = new Vector2(float.MaxValue, float.MaxValue), pMax = new Vector2(float.MinValue, float.MinValue);
				foreach (Vector2 p in polygon)
				{
					pMin = Vector2.Min(pMin, p);
					pMax = Vector2.Max(pMax, p);
				}
				grid.CellOf(pMin - new Vector2(reach, reach), out int x0, out int y0);
				grid.CellOf(pMax + new Vector2(reach, reach), out int x1, out int y1);
				for (int y = y0; y <= y1; y++)
				{
					for (int x = x0; x <= x1; x++)
					{
						if (AtlasGeometry.DistanceTo(polygon, grid.Centre(x, y)) <= reach)
						{
							grid.Blocked[grid.Index(x, y)] = true;
						}
					}
				}
			}
			return grid;
		}

		/// <summary>Picks each connection's exit on each scene, spreading exits that share a side.</summary>
		private static Port[,] AssignPorts(Grid grid, IReadOnlyList<Vector2[]> scenes, IReadOnlyList<AtlasRouteRequest> requests, AtlasRouterSettings settings)
		{
			var ports = new Port[requests.Count, 2];
			// (scene, side) → the requests (and which end) leaving through it, ordered along the side.
			var bySide = new Dictionary<long, List<(int request, int end, float along)>>();
			for (int i = 0; i < requests.Count; i++)
			{
				AtlasRouteRequest r = requests[i];
				if (!Valid(scenes, r))
				{
					continue;
				}
				for (int end = 0; end < 2; end++)
				{
					int self = end == 0 ? r.From : r.To;
					int other = end == 0 ? r.To : r.From;
					Vector2[] polygon = scenes[self];
					Vector2 target = Centroid(scenes[other]);
					Vector2? hint = end == 0 ? r.FromHint : r.ToHint;
					Vector2? otherHint = end == 0 ? r.ToHint : r.FromHint;
					if (otherHint.HasValue)
					{
						target = otherHint.Value;
					}
					int side = FacingSide(polygon, target);
					Vector2 a = polygon[side], b = polygon[(side + 1) % polygon.Length];
					Vector2 ab = b - a;
					// Ordered along the side by where the line starts, so neighbouring exits do not cross.
					Vector2 orderBy = hint ?? target;
					float along = ab.sqrMagnitude > 0f ? Vector2.Dot(orderBy - a, ab) / ab.sqrMagnitude : 0.5f;
					long key = ((long)self << 32) | (uint)side;
					if (!bySide.TryGetValue(key, out var list))
					{
						list = new List<(int, int, float)>();
						bySide.Add(key, list);
					}
					list.Add((i, end, along));
				}
			}

			foreach (KeyValuePair<long, List<(int request, int end, float along)>> pair in bySide)
			{
				int self = (int)(pair.Key >> 32);
				int side = (int)(pair.Key & 0xffffffff);
				Vector2[] polygon = scenes[self];
				Vector2 a = polygon[side], b = polygon[(side + 1) % polygon.Length];
				Vector2 normal = OutwardNormal(polygon, side);
				List<(int request, int end, float along)> list = pair.Value;
				list.Sort((p, q) => p.along != q.along ? p.along.CompareTo(q.along) : p.request.CompareTo(q.request));
				for (int k = 0; k < list.Count; k++)
				{
					float t = (k + 1f) / (list.Count + 1f);
					AtlasRouteRequest request = requests[list[k].request];
					if (list.Count == 1 && (list[k].end == 0 ? request.FromHint : request.ToHint).HasValue)
					{
						// A lone exit sits beside its teleporter.
						t = Mathf.Clamp(list[k].along, 0.15f, 0.85f);
					}
					Vector2 edge = Vector2.Lerp(a, b, t);
					ports[list[k].request, list[k].end] = MakePort(grid, polygon, edge, normal, settings);
				}
			}
			return ports;
		}

		private static Port MakePort(Grid grid, Vector2[] own, Vector2 edge, Vector2 normal, AtlasRouterSettings settings)
		{
			// Step outwards until the cell is free of every scene's padding. A wall met beyond the
			// scene's own padding belongs to a neighbour: the stub would run through it, so the
			// exit is blocked instead.
			float ownReach = settings.PaddingKm + grid.Cell * 1.5f;
			float distance = settings.PaddingKm + grid.Cell;
			for (int attempt = 0; attempt < 64; attempt++)
			{
				Vector2 outside = edge + normal * distance;
				grid.CellOf(outside, out int x, out int y);
				int index = grid.Index(x, y);
				if (!grid.Blocked[index] && !grid.Claimed[index])
				{
					grid.Claimed[index] = true;
					// Straight out from the edge, not snapped to the cell centre, so the exit has no kink.
					return new Port { Edge = edge, Outside = outside, X = x, Y = y, Valid = true };
				}
				if (grid.Blocked[index] && AtlasGeometry.DistanceTo(own, outside) > ownReach)
				{
					break;
				}
				distance += grid.Cell;
			}
			return new Port { Edge = edge, Outside = edge, Valid = false };
		}

		private static int FacingSide(Vector2[] polygon, Vector2 target)
		{
			Vector2 centre = Centroid(polygon);
			Vector2 toward = (target - centre).normalized;
			int best = 0;
			float bestDot = float.MinValue;
			for (int i = 0; i < polygon.Length; i++)
			{
				float d = Vector2.Dot(OutwardNormal(polygon, i), toward);
				if (d > bestDot + 1e-5f)
				{
					bestDot = d;
					best = i;
				}
			}
			return best;
		}

		private static Vector2 OutwardNormal(Vector2[] polygon, int side)
		{
			Vector2 a = polygon[side], b = polygon[(side + 1) % polygon.Length];
			Vector2 e = (b - a).normalized;
			var n = new Vector2(e.y, -e.x);
			Vector2 mid = (a + b) * 0.5f;
			return Vector2.Dot(n, mid - Centroid(polygon)) >= 0f ? n : -n;
		}

		private static AtlasRoute RouteOne(Grid grid, IReadOnlyList<Vector2[]> scenes, AtlasRouteRequest request, Port from, Port to, AtlasRouterSettings settings)
		{
			var route = new AtlasRoute { Request = request };
			if (!Valid(scenes, request))
			{
				return route;
			}
			if (!from.Valid || !to.Valid)
			{
				if (!DirectLine(grid, route, scenes, request))
				{
					StraightLine(route, scenes, request);
				}
				return route;
			}

			List<(int x, int y)> path = FindPath(grid, from, to, settings, out bool shared);
			if (path == null)
			{
				if (!DirectLine(grid, route, scenes, request))
				{
					StraightLine(route, scenes, request);
				}
				return route;
			}

			route.Clean = true;
			route.SharesCorridor = shared;
			var corners = new List<Vector2> { from.Outside };
			for (int i = 1; i < path.Count - 1; i++)
			{
				int dir = DirectionIndex(path[i].x - path[i - 1].x, path[i].y - path[i - 1].y);
				int next = DirectionIndex(path[i + 1].x - path[i].x, path[i + 1].y - path[i].y);
				if (dir != next)
				{
					corners.Add(grid.Centre(path[i].x, path[i].y));
				}
			}
			corners.Add(to.Outside);
			corners = PullTight(grid, corners);

			var line = new List<Vector2>(corners.Count + 2) { from.Edge };
			line.AddRange(corners);
			line.Add(to.Edge);
			route.Points.AddRange(RoundCorners(line, settings.CornerKm));
			// Later routes keep off the line as drawn, not the staircase it was found on.
			MarkLine(grid, corners);
			return route;
		}

		/// <summary>
		/// Drops every corner the line can skip without crossing a wall: the grid's staircase
		/// becomes the few straight runs a person would draw.
		/// </summary>
		private static List<Vector2> PullTight(Grid grid, List<Vector2> points)
		{
			if (points.Count <= 2)
			{
				return points;
			}
			var result = new List<Vector2> { points[0] };
			int anchor = 0;
			while (anchor < points.Count - 1)
			{
				int furthest = anchor + 1;
				for (int j = points.Count - 1; j > anchor + 1; j--)
				{
					if (Clear(grid, points[anchor], points[j]))
					{
						furthest = j;
						break;
					}
				}
				result.Add(points[furthest]);
				anchor = furthest;
			}
			return result;
		}

		/// <summary>
		/// True when a straight segment only crosses free cells and never runs along a lane another
		/// route already uses (crossing one is fine).
		/// </summary>
		private static bool Clear(Grid grid, Vector2 a, Vector2 b)
		{
			float length = Vector2.Distance(a, b);
			int lane = 1 << (LaneOf(b - a) % 4);
			int steps = Mathf.Max(1, Mathf.CeilToInt(length / (grid.Cell * 0.25f)));
			for (int i = 0; i <= steps; i++)
			{
				grid.CellOf(Vector2.Lerp(a, b, i / (float)steps), out int x, out int y);
				int index = grid.Index(x, y);
				if (grid.Blocked[index] || (grid.Used[index] & lane) != 0)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The grid direction (0..7) nearest a vector.</summary>
		private static int LaneOf(Vector2 v)
		{
			float length = v.magnitude;
			if (length < 1e-6f)
			{
				return 0;
			}
			int dx = Mathf.Abs(v.x / length) > 0.3827f ? Math.Sign(v.x) : 0;
			int dy = Mathf.Abs(v.y / length) > 0.3827f ? Math.Sign(v.y) : 0;
			return Math.Max(0, DirectionIndex(dx, dy));
		}

		/// <summary>Marks the cells a drawn polyline covers with its lane direction.</summary>
		private static void MarkLine(Grid grid, List<Vector2> points)
		{
			for (int p = 0; p + 1 < points.Count; p++)
			{
				Vector2 a = points[p], b = points[p + 1];
				byte lane = (byte)(1 << (LaneOf(b - a) % 4));
				int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (grid.Cell * 0.25f)));
				for (int i = 0; i <= steps; i++)
				{
					grid.CellOf(Vector2.Lerp(a, b, i / (float)steps), out int x, out int y);
					grid.Used[grid.Index(x, y)] |= lane;
				}
			}
		}

		/// <summary>
		/// For scenes too close to route around: a straight line between the facing edges, accepted
		/// when it crosses no scene. Returns false when even that is blocked.
		/// </summary>
		private static bool DirectLine(Grid grid, AtlasRoute route, IReadOnlyList<Vector2[]> scenes, AtlasRouteRequest request)
		{
			Vector2[] fromScene = scenes[request.From], toScene = scenes[request.To];
			Vector2 a = request.FromHint ?? Centroid(fromScene);
			Vector2 b = request.ToHint ?? Centroid(toScene);
			if (!AtlasGeometry.Contains(fromScene, a))
			{
				a = Centroid(fromScene);
			}
			if (!AtlasGeometry.Contains(toScene, b))
			{
				b = Centroid(toScene);
			}
			Vector2 exit = Boundary(fromScene, a, b);
			Vector2 entry = Boundary(toScene, b, a);
			float length = Vector2.Distance(exit, entry);
			if (AtlasGeometry.Contains(toScene, exit) || AtlasGeometry.Contains(fromScene, entry))
			{
				// The scenes overlap: there is no gap to draw.
				return false;
			}
			int steps = Mathf.Max(2, Mathf.CeilToInt(length / 0.05f));
			for (int i = 1; i < steps; i++)
			{
				Vector2 point = Vector2.Lerp(exit, entry, i / (float)steps);
				for (int s = 0; s < scenes.Count; s++)
				{
					if (scenes[s] != null && scenes[s].Length >= 3 && AtlasGeometry.Contains(scenes[s], point))
					{
						return false;
					}
				}
			}
			route.Clean = true;
			route.Direct = true;
			route.Points.Add(exit);
			route.Points.Add(entry);
			MarkLine(grid, route.Points);
			return true;
		}

		/// <summary>Where the segment from a point inside a convex polygon toward another point leaves it.</summary>
		private static Vector2 Boundary(Vector2[] polygon, Vector2 inside, Vector2 toward)
		{
			float lo = 0f, hi = 1f;
			if (AtlasGeometry.Contains(polygon, toward))
			{
				return toward;
			}
			for (int i = 0; i < 40; i++)
			{
				float mid = (lo + hi) * 0.5f;
				if (AtlasGeometry.Contains(polygon, Vector2.Lerp(inside, toward, mid)))
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			return Vector2.Lerp(inside, toward, hi);
		}

		/// <summary>
		/// Rounds every interior corner with a quadratic curve that starts and ends at most
		/// <paramref name="radius"/> from the corner. The ends stay where they are.
		/// </summary>
		public static List<Vector2> RoundCorners(IReadOnlyList<Vector2> points, float radius, int samples = 6)
		{
			var result = new List<Vector2>();
			if (points == null || points.Count == 0)
			{
				return result;
			}
			result.Add(points[0]);
			for (int i = 1; i < points.Count - 1; i++)
			{
				Vector2 previous = points[i - 1], corner = points[i], next = points[i + 1];
				float inLength = Vector2.Distance(previous, corner), outLength = Vector2.Distance(corner, next);
				float r = Mathf.Min(radius, inLength * 0.5f, outLength * 0.5f);
				Vector2 inDir = inLength > 1e-6f ? (corner - previous) / inLength : Vector2.zero;
				Vector2 outDir = outLength > 1e-6f ? (next - corner) / outLength : Vector2.zero;
				if (r <= 1e-4f || Vector2.Dot(inDir, outDir) > 0.9995f)
				{
					result.Add(corner);
					continue;
				}
				Vector2 start = corner - inDir * r, end = corner + outDir * r;
				for (int k = 0; k <= samples; k++)
				{
					float t = k / (float)samples;
					float u = 1f - t;
					result.Add(u * u * start + 2f * u * t * corner + t * t * end);
				}
			}
			if (points.Count > 1)
			{
				result.Add(points[points.Count - 1]);
			}
			return result;
		}

		private static void StraightLine(AtlasRoute route, IReadOnlyList<Vector2[]> scenes, AtlasRouteRequest request)
		{
			route.Clean = false;
			route.Points.Add(Centroid(scenes[request.From]));
			route.Points.Add(Centroid(scenes[request.To]));
		}

		private static int DirectionIndex(int dx, int dy)
		{
			for (int i = 0; i < 8; i++)
			{
				if (StepX[i] == Math.Sign(dx) && StepY[i] == Math.Sign(dy))
				{
					return i;
				}
			}
			return -1;
		}

		private sealed class MinHeap
		{
			private readonly List<(float key, int value)> items = new List<(float, int)>();
			public int Count => items.Count;

			public void Push(float key, int value)
			{
				items.Add((key, value));
				int i = items.Count - 1;
				while (i > 0)
				{
					int parent = (i - 1) / 2;
					if (items[parent].key <= items[i].key)
					{
						break;
					}
					(items[parent], items[i]) = (items[i], items[parent]);
					i = parent;
				}
			}

			public int Pop()
			{
				int result = items[0].value;
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int i = 0;
				while (true)
				{
					int l = i * 2 + 1, r = l + 1, smallest = i;
					if (l < items.Count && items[l].key < items[smallest].key) smallest = l;
					if (r < items.Count && items[r].key < items[smallest].key) smallest = r;
					if (smallest == i)
					{
						break;
					}
					(items[smallest], items[i]) = (items[i], items[smallest]);
					i = smallest;
				}
				return result;
			}
		}

		private static List<(int x, int y)> FindPath(Grid grid, Port from, Port to, AtlasRouterSettings settings, out bool shared)
		{
			shared = false;
			int count = grid.Width * grid.Height;
			var cost = new float[count];
			var parent = new int[count];
			var closed = new bool[count];
			for (int i = 0; i < count; i++)
			{
				cost[i] = float.MaxValue;
				parent[i] = -1;
			}
			int start = grid.Index(from.X, from.Y);
			int goal = grid.Index(to.X, to.Y);
			var open = new MinHeap();
			cost[start] = 0f;
			open.Push(Heuristic(from.X, from.Y, to.X, to.Y), start);

			while (open.Count > 0)
			{
				int current = open.Pop();
				if (closed[current])
				{
					continue;
				}
				if (current == goal)
				{
					break;
				}
				closed[current] = true;
				int cx = current % grid.Width, cy = current / grid.Width;
				for (int d = 0; d < 8; d++)
				{
					int nx = cx + StepX[d], ny = cy + StepY[d];
					if (!grid.InBounds(nx, ny))
					{
						continue;
					}
					int next = grid.Index(nx, ny);
					if (closed[next] || grid.Blocked[next] || (grid.Claimed[next] && next != goal))
					{
						continue;
					}
					// No corner cutting past a wall.
					if (d % 2 == 1 && (grid.Blocked[grid.Index(cx + StepX[d], cy)] || grid.Blocked[grid.Index(cx, cy + StepY[d])]))
					{
						continue;
					}
					float step = d % 2 == 1 ? 1.41421f : 1f;
					byte used = grid.Used[next];
					if (used != 0)
					{
						step += (used & (1 << (d % 4))) != 0 ? settings.ParallelCost : settings.CrossCost;
					}
					// A small turn penalty keeps lines straight.
					int came = parent[current];
					if (came >= 0)
					{
						int px = came % grid.Width, py = came / grid.Width;
						if (DirectionIndex(cx - px, cy - py) != d)
						{
							step += 0.2f;
						}
					}
					float candidate = cost[current] + step;
					if (candidate < cost[next])
					{
						cost[next] = candidate;
						parent[next] = current;
						open.Push(candidate + Heuristic(nx, ny, to.X, to.Y), next);
					}
				}
			}

			if (parent[goal] < 0 && goal != start)
			{
				return null;
			}
			var path = new List<(int x, int y)>();
			for (int i = goal; i >= 0; i = parent[i])
			{
				path.Add((i % grid.Width, i / grid.Width));
				if ((grid.Used[i] & 0x0f) != 0)
				{
					int prev = parent[i];
					if (prev >= 0)
					{
						int d = DirectionIndex(i % grid.Width - prev % grid.Width, i / grid.Width - prev / grid.Width);
						if ((grid.Used[i] & (1 << (d % 4))) != 0)
						{
							shared = true;
						}
					}
				}
				if (i == start)
				{
					break;
				}
			}
			path.Reverse();
			return path;
		}

		private static float Heuristic(int x0, int y0, int x1, int y1)
		{
			int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
			return Math.Max(dx, dy) + 0.41421f * Math.Min(dx, dy);
		}
	}
}
