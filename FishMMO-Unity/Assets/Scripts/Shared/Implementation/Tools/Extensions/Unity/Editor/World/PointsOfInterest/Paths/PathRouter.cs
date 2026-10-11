#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What the ground is to a route: a cell of the router's grid.</summary>
	public enum PathCellWater : byte
	{
		Dry,
		/// <summary>A river's channel: waded where shallow, bridged where not.</summary>
		River,
		/// <summary>A dry wash, bar or salt flat: walkable, a little rough.</summary>
		Rough,
		/// <summary>A lake or the sea: never crossed.</summary>
		Open,
	}

	/// <summary>
	/// Finds where a path goes over a scene's ground: an A* search on a grid of cells a few metres across, priced the way a
	/// path-maker would price it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The price of a step</b> is its length, times what the climb costs against the class's grade (gentle is nearly
	/// free; past the grade it soars; past twice it is refused, so a road winds and a steep slope is taken in switchbacks),
	/// times what the side slope costs (a path along a hillside needs a bench cut), plus water: a river waded where it is
	/// no deeper than the class fords, otherwise bridged, which costs a crossing's worth of effort once (so ways gather at
	/// the few good crossings) and every metre of span. Lakes and the sea are never crossed; holes and other sites'
	/// footprints never entered.
	/// </para>
	/// <para>
	/// <b>Ways join ways.</b> A step along an existing way of the same class or better costs a third: a footpath to a
	/// shrine leaves the road where the road comes nearest, two villages' tracks share the trunk to the town. A trail's
	/// steps are also priced by a little noise, so it wanders as trails do.
	/// </para>
	/// <para>Deterministic: fixed neighbour order, and ties in the open list broken by cell index.</para>
	/// </remarks>
	public sealed class PathRouter
	{
		public readonly float Cell;
		public readonly int Width, Depth;
		public readonly float OriginX, OriginZ;
		public readonly float[] Height;
		/// <summary>Ground gradient per cell (rise per metre east, north).</summary>
		public readonly Vector2[] Gradient;
		public readonly PathCellWater[] Water;
		/// <summary>A river cell's depth under its surface, metres.</summary>
		public readonly float[] WaterDepth;
		/// <summary>Never entered: holes, the scene's edge.</summary>
		public readonly bool[] Blocked;
		/// <summary>The site whose footprint covers a cell (0 none): entered only by that site's own ways.</summary>
		public readonly int[] Owner;
		/// <summary>The best class of way already through a cell, or 255.</summary>
		public readonly byte[] Network;
		/// <summary>The bridge (record id) whose deck spans a cell, or 0.</summary>
		public readonly int[] Bridge;
		private readonly float seedNoise;

		// Search scratch, stamped so a search never clears the grid.
		private readonly float[] cost;
		private readonly int[] parent;
		private readonly int[] stamp;
		private readonly bool[] closed;
		private int search;

		private static readonly int[] StepX = { 1, -1, 0, 0, 1, 1, -1, -1, 1, 2, 2, 1, -1, -2, -2, -1 };
		private static readonly int[] StepZ = { 0, 0, 1, -1, 1, -1, 1, -1, 2, 1, -1, -2, -2, -1, 1, 2 };

		public PathRouter(float widthMetres, float depthMetres, float cell, int seed)
		{
			Cell = Mathf.Max(1f, cell);
			Width = Mathf.Max(2, Mathf.CeilToInt(widthMetres / Cell));
			Depth = Mathf.Max(2, Mathf.CeilToInt(depthMetres / Cell));
			OriginX = -widthMetres * 0.5f;
			OriginZ = -depthMetres * 0.5f;
			int n = Width * Depth;
			Height = new float[n];
			Gradient = new Vector2[n];
			Water = new PathCellWater[n];
			WaterDepth = new float[n];
			Blocked = new bool[n];
			Owner = new int[n];
			Network = new byte[n];
			Bridge = new int[n];
			cost = new float[n];
			parent = new int[n];
			stamp = new int[n];
			closed = new bool[n];
			for (int i = 0; i < n; i++)
			{
				Network[i] = 255;
			}
			seedNoise = (PointOfInterestPlanner.Hash(seed, 0x7A11, 0, 0) & 0xFFFF) * 0.013f;
		}

		public float XOf(int x) => OriginX + (x + 0.5f) * Cell;
		public float ZOf(int z) => OriginZ + (z + 0.5f) * Cell;
		public int Index(int x, int z) => z * Width + x;

		public bool CellOf(float east, float north, out int x, out int z)
		{
			x = Mathf.FloorToInt((east - OriginX) / Cell);
			z = Mathf.FloorToInt((north - OriginZ) / Cell);
			bool inside = x >= 0 && z >= 0 && x < Width && z < Depth;
			x = Mathf.Clamp(x, 0, Width - 1);
			z = Mathf.Clamp(z, 0, Depth - 1);
			return inside;
		}

		public int IndexAt(float east, float north)
		{
			CellOf(east, north, out int x, out int z);
			return Index(x, z);
		}

		/// <summary>Fills the heights and water from the scene, and the gradients from the heights.</summary>
		public void Fill(Func<float, float, float> ground, Func<float, float, PathCellWater> water, Func<float, float, float> depth,
			Func<float, float, bool> blocked, float edgeMargin)
		{
			for (int z = 0; z < Depth; z++)
			{
				for (int x = 0; x < Width; x++)
				{
					int i = Index(x, z);
					float east = XOf(x), north = ZOf(z);
					Height[i] = ground(east, north);
					Water[i] = water != null ? water(east, north) : PathCellWater.Dry;
					WaterDepth[i] = Water[i] == PathCellWater.River && depth != null ? Mathf.Max(0f, depth(east, north)) : 0f;
					bool edge = east < OriginX + edgeMargin || east > -OriginX - edgeMargin || north < OriginZ + edgeMargin || north > -OriginZ - edgeMargin;
					Blocked[i] = edge || (blocked != null && blocked(east, north));
				}
			}
			for (int z = 0; z < Depth; z++)
			{
				for (int x = 0; x < Width; x++)
				{
					int xa = Mathf.Max(0, x - 1), xb = Mathf.Min(Width - 1, x + 1), za = Mathf.Max(0, z - 1), zb = Mathf.Min(Depth - 1, z + 1);
					Gradient[Index(x, z)] = new Vector2(
						(Height[Index(xb, z)] - Height[Index(xa, z)]) / ((xb - xa) * Cell),
						(Height[Index(x, zb)] - Height[Index(x, za)]) / ((zb - za) * Cell));
				}
			}
		}

		/// <summary>Marks a disc as a site's footprint: entered only by that site's own ways.</summary>
		public void MarkOwner(int id, float east, float north, float radius)
		{
			ForDisc(east, north, radius, i =>
			{
				if (Owner[i] == 0)
				{
					Owner[i] = id;
				}
			});
		}

		/// <summary>Marks a bridge's deck: the cells along its heading within its half-span, a few metres wide.</summary>
		public void MarkBridge(int id, Vector3 centre, float yawDegrees, float halfSpan)
		{
			float r = yawDegrees * Mathf.Deg2Rad;
			var along = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
			for (float t = -halfSpan; t <= halfSpan; t += Cell * 0.5f)
			{
				for (float s = -3f; s <= 3f; s += Cell * 0.5f)
				{
					float east = centre.x + along.x * t + along.y * s, north = centre.z + along.y * t - along.x * s;
					if (CellOf(east, north, out int x, out int z))
					{
						Bridge[Index(x, z)] = id;
					}
				}
			}
		}

		/// <summary>Marks the cells a finished way runs through as network of its class.</summary>
		public void MarkWay(IReadOnlyList<Vector3> points, ScenePathClass kind, float halfWidth)
		{
			float r = Mathf.Max(Cell * 0.5f, halfWidth);
			for (int i = 0; i < points.Count; i++)
			{
				ForDisc(points[i].x, points[i].z, r, c =>
				{
					if (Network[c] == 255 || Network[c] < (byte)kind)
					{
						Network[c] = (byte)kind;
					}
				});
			}
		}

		private void ForDisc(float east, float north, float radius, Action<int> visit)
		{
			int x0 = Mathf.Max(0, Mathf.FloorToInt((east - radius - OriginX) / Cell)), x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((east + radius - OriginX) / Cell));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((north - radius - OriginZ) / Cell)), z1 = Mathf.Min(Depth - 1, Mathf.FloorToInt((north + radius - OriginZ) / Cell));
			float r2 = (radius + Cell * 0.5f) * (radius + Cell * 0.5f);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					float dx = XOf(x) - east, dz = ZOf(z) - north;
					if (dx * dx + dz * dz <= r2)
					{
						visit(Index(x, z));
					}
				}
			}
		}

		/// <summary>One route asked for.</summary>
		public struct Request
		{
			public ScenePathClass Class;
			public Vector2 From;
			/// <summary>Where it goes; ignored past the first network cell when <see cref="JoinNetwork"/> is set.</summary>
			public Vector2 To;
			/// <summary>Stop at the first cell of an existing way (any class), at least this many metres from the start; 0 never.</summary>
			public float JoinNetwork;
			/// <summary>The sites whose footprints this way may cross (its own ends).</summary>
			public int OwnA, OwnB;
			/// <summary>Give up past this many metres of route; 0 for no limit.</summary>
			public float MaxLength;
			/// <summary>Never refuse a climb, only price it: the retry when the strict search found no way.</summary>
			public bool Relaxed;
		}

		/// <summary>A route found: its cells' centres from start to end, which cell ended it, and its price.</summary>
		public sealed class Route
		{
			public readonly List<int> Cells = new List<int>();
			public bool Joined;
			public float Cost;
		}

		/// <summary>Searches one route; null when there is none (the goal walled off, or past the length limit).</summary>
		public Route Find(Request request)
		{
			ScenePathStyle style = ScenePathStyle.For(request.Class);
			CellOf(request.From.x, request.From.y, out int sx, out int sz);
			CellOf(request.To.x, request.To.y, out int gx, out int gz);
			int start = Index(sx, sz), goal = Index(gx, gz);

			// The search box: both ends and a generous margin, so a way may swing wide round a ridge or to a bridge.
			float span = Vector2.Distance(request.From, request.To);
			int margin = Mathf.CeilToInt(Mathf.Max(400f, span * 0.75f) / Cell);
			int bx0 = Mathf.Max(0, Mathf.Min(sx, gx) - margin), bx1 = Mathf.Min(Width - 1, Mathf.Max(sx, gx) + margin);
			int bz0 = Mathf.Max(0, Mathf.Min(sz, gz) - margin), bz1 = Mathf.Min(Depth - 1, Mathf.Max(sz, gz) + margin);

			search++;
			var open = new MinHeap();
			Touch(start);
			cost[start] = 0f;
			parent[start] = -1;
			open.Push(Heuristic(sx, sz, gx, gz), start);
			int reached = -1;
			bool joined = false;
			float maxCost = request.MaxLength > 0f ? request.MaxLength * 6f : float.PositiveInfinity;
			float joinFrom2 = request.JoinNetwork * request.JoinNetwork;

			while (open.Count > 0)
			{
				int current = open.Pop();
				if (closed[current])
				{
					continue;
				}
				closed[current] = true;
				if (current == goal)
				{
					reached = current;
					break;
				}
				int cx = current % Width, cz = current / Width;
				if (request.JoinNetwork > 0f && Network[current] != 255)
				{
					float dx = (cx - sx) * Cell, dz = (cz - sz) * Cell;
					if (dx * dx + dz * dz >= joinFrom2)
					{
						reached = current;
						joined = true;
						break;
					}
				}
				if (cost[current] > maxCost)
				{
					continue;
				}
				for (int k = 0; k < StepX.Length; k++)
				{
					int nx = cx + StepX[k], nz = cz + StepZ[k];
					if (nx < bx0 || nz < bz0 || nx > bx1 || nz > bz1)
					{
						continue;
					}
					int next = Index(nx, nz);
					if (stamp[next] == search && closed[next])
					{
						continue;
					}
					float step = StepCost(current, next, cx, cz, nx, nz, style, request);
					if (float.IsPositiveInfinity(step))
					{
						continue;
					}
					float total = cost[current] + step;
					if (stamp[next] != search)
					{
						Touch(next);
					}
					else if (total >= cost[next])
					{
						continue;
					}
					cost[next] = total;
					parent[next] = current;
					open.Push(total + Heuristic(nx, nz, gx, gz), next);
				}
			}
			if (reached < 0)
			{
				return null;
			}
			var route = new Route { Joined = joined, Cost = cost[reached] };
			for (int c = reached; c >= 0; c = parent[c])
			{
				route.Cells.Add(c);
			}
			route.Cells.Reverse();
			return route;
		}

		private void Touch(int i)
		{
			stamp[i] = search;
			cost[i] = float.PositiveInfinity;
			closed[i] = false;
		}

		/// <summary>Half the straight distance: a way may run along a road at a third of the price, so more would overestimate.</summary>
		private float Heuristic(int x, int z, int gx, int gz)
		{
			float dx = (gx - x) * Cell, dz = (gz - z) * Cell;
			return 0.5f * Mathf.Sqrt(dx * dx + dz * dz);
		}

		private bool Passable(int i, in Request request)
		{
			if (Blocked[i] || Water[i] == PathCellWater.Open && Bridge[i] == 0)
			{
				return false;
			}
			int owner = Owner[i];
			return owner == 0 || owner == request.OwnA || owner == request.OwnB || Bridge[i] != 0;
		}

		/// <summary>The price of one step between neighbouring cells, or +∞ where it may not go.</summary>
		private float StepCost(int from, int to, int fx, int fz, int tx, int tz, in ScenePathStyle style, in Request request)
		{
			if (!Passable(to, request))
			{
				return float.PositiveInfinity;
			}
			int sx = tx - fx, sz = tz - fz;
			// A knight's step passes over the two cells beside its line.
			if (Mathf.Abs(sx) + Mathf.Abs(sz) == 3)
			{
				int ax = fx + (Mathf.Abs(sx) == 2 ? sx / 2 : 0), az = fz + (Mathf.Abs(sz) == 2 ? sz / 2 : 0);
				int bx = fx + (Mathf.Abs(sx) == 2 ? sx / 2 : sx), bz = fz + (Mathf.Abs(sz) == 2 ? sz / 2 : sz);
				if (!Passable(Index(ax, az), request) || !Passable(Index(bx, bz), request))
				{
					return float.PositiveInfinity;
				}
			}
			float length = Cell * Mathf.Sqrt(sx * sx + sz * sz);
			bool bridged = Bridge[to] != 0;

			// The climb, against the class's grade.
			float grade = bridged ? 0f : Mathf.Abs(Height[to] - Height[from]) / length;
			float g = grade / Mathf.Max(0.01f, style.MaxGrade);
			if (g > 2.2f && !request.Relaxed)
			{
				return float.PositiveInfinity;
			}
			float climb = 2.5f * g * g + (g > 1f ? 30f * (g - 1f) * (g - 1f) : 0f);

			// The side slope: a bench cut along a hillside, dearer the wider the way.
			Vector2 grad = (Gradient[from] + Gradient[to]) * 0.5f;
			float inv = 1f / Mathf.Sqrt(sx * sx + sz * sz);
			float side = Mathf.Abs(grad.x * -sz * inv + grad.y * sx * inv);
			float bench = side * side * style.HalfWidth * 1.2f;

			float price = length * (1f + climb + bench);

			// Water.
			if (!bridged)
			{
				switch (Water[to])
				{
					case PathCellWater.River:
						if (WaterDepth[to] <= style.FordDepth)
						{
							price += length * 2f + (Water[from] != PathCellWater.River ? 25f : 0f);
						}
						else
						{
							// A new bridge: a crossing's worth of effort once, then its span.
							price += length * 3f + (Water[from] != PathCellWater.River ? BridgeCost(request.Class) : 0f);
						}
						break;
					case PathCellWater.Rough:
						price += length * 0.3f;
						break;
				}
			}

			// Along an existing way: the same class or better at a third, a lesser one at most of the price.
			byte way = Network[to];
			if (way != 255)
			{
				price *= way >= (byte)request.Class ? 0.35f : 0.8f;
			}
			else if (request.Class <= ScenePathClass.Footpath)
			{
				// A trail wanders: a little noise on every untrodden step.
				price *= 1f + 0.3f * Noise(tx * 0.37f + seedNoise, tz * 0.37f - seedNoise);
			}
			return price;
		}

		/// <summary>
		/// Smooth value noise, 0 … 1 a unit cell: managed (Mathf.PerlinNoise is native, which the pure test runner cannot
		/// call) and the same on every machine.
		/// </summary>
		public static float Noise(float x, float z)
		{
			int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
			float fx = x - ix, fz = z - iz;
			float ux = fx * fx * (3f - 2f * fx), uz = fz * fz * (3f - 2f * fz);
			float Corner(int cx, int cz) => (PointOfInterestPlanner.Hash(0x51A7, cx, cz, 0) >> 8) * (1f / 16777216f);
			float a = Corner(ix, iz), b = Corner(ix + 1, iz), c = Corner(ix, iz + 1), d = Corner(ix + 1, iz + 1);
			return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uz);
		}

		/// <summary>What a new bridge is worth avoiding, metres of easy walking: a highway is bridged gladly, a trail wades or goes round.</summary>
		public static float BridgeCost(ScenePathClass kind)
		{
			switch (kind)
			{
				case ScenePathClass.Highway: return 150f;
				case ScenePathClass.Road: return 200f;
				case ScenePathClass.CartTrack: return 300f;
				case ScenePathClass.Footpath: return 500f;
				default: return 800f;
			}
		}

		/// <summary>A binary min-heap of (priority, cell); ties by cell, so the search never rests on insertion order.</summary>
		private sealed class MinHeap
		{
			private readonly List<(float key, int cell)> items = new List<(float, int)>(1024);

			public int Count => items.Count;

			private static bool Less((float key, int cell) a, (float key, int cell) b) => a.key < b.key || a.key == b.key && a.cell < b.cell;

			public void Push(float key, int cell)
			{
				items.Add((key, cell));
				int i = items.Count - 1;
				while (i > 0)
				{
					int p = (i - 1) >> 1;
					if (!Less(items[i], items[p]))
					{
						break;
					}
					(items[i], items[p]) = (items[p], items[i]);
					i = p;
				}
			}

			public int Pop()
			{
				int top = items[0].cell;
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int i = 0;
				while (true)
				{
					int l = 2 * i + 1, r = l + 1, m = i;
					if (l < items.Count && Less(items[l], items[m])) m = l;
					if (r < items.Count && Less(items[r], items[m])) m = r;
					if (m == i)
					{
						break;
					}
					(items[i], items[m]) = (items[m], items[i]);
					i = m;
				}
				return top;
			}
		}
	}
}
#endif
