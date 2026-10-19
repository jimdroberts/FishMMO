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
	/// <para>
	/// <b>No cliffs, ever.</b> A relaxed search (the retry when the strict one found nothing) prices any climb rather than
	/// refusing it, but still never steps steeper than <see cref="WalkableGrade"/>. A bridge's cells are climbed at its
	/// deck's height, not for free, and only cells at the deck's height are its deck; a way is network only in the cells
	/// at its own height. Each of those once let a way step 60 m down a fall's cliff (Flo Monolith, 2026-10-12).
	/// </para>
	/// <para>Deterministic: fixed neighbour order, and ties in the open list broken by cell index.</para>
	/// </remarks>
	public sealed class PathRouter
	{
		/// <summary>
		/// The steepest step any way takes, relaxed or not (rise per metre, 45°): well under what a character stands on, so a
		/// way is always walked, never climbed.
		/// </summary>
		public const float WalkableGrade = 1f;

		/// <summary>How many times its class's grade a strict search lets a way climb before it refuses the step.</summary>
		public const float GradeLimit = 2.2f;

		/// <summary>The steepest a way of a class is routed (rise per metre): its grade's limit, never past walkable.</summary>
		public static float MaxGradeOf(ScenePathClass kind) => Mathf.Min(WalkableGrade, ScenePathStyle.For(kind).MaxGrade * GradeLimit);

		/// <summary>How far above a bridge's deck a cell it marks may stand and still be its deck, metres (a bank, not a cliff top).</summary>
		private const float DeckRise = 1.5f;

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
		/// <summary>
		/// Per cell: 0 not yet asked, 1 steep, 2 not (<see cref="IsSteep"/>), worked out the first time a search asks.
		/// </summary>
		private readonly byte[] steep;
		/// <summary>
		/// The ground every half cell, read once each and only where asked (NaN until then): the cliffs between cell
		/// centres (<see cref="IsSteep"/>) and the lines a way's ends are drawn along (<see cref="GroundAt"/>).
		/// </summary>
		private float[] lattice = Array.Empty<float>();
		private int latticeWidth, latticeDepth;
		private Func<float, float, float> sampler;
		/// <summary>The site whose footprint covers a cell (0 none): entered only by that site's own ways.</summary>
		public readonly int[] Owner;
		/// <summary>The best class of way already through a cell, or 255.</summary>
		public readonly byte[] Network;
		/// <summary>The bridge (record id) whose deck spans a cell, or 0.</summary>
		public readonly int[] Bridge;
		private readonly float seedNoise;
		/// <summary>Each marked bridge's deck height (its record's or site's height: the higher bank), by id.</summary>
		private readonly Dictionary<int, float> deckHeight = new Dictionary<int, float>();

		// Search scratch, stamped so a search never clears the grid.
		private readonly float[] cost;
		private readonly int[] parent;
		private readonly int[] stamp;
		private readonly bool[] closed;
		private int search;
		private int searchStart = -1, searchGoal = -1;

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
			steep = new byte[n];
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

			sampler = ground;
			latticeWidth = 2 * Width + 1;
			latticeDepth = 2 * Depth + 1;
			lattice = new float[latticeWidth * latticeDepth];
			for (int k = 0; k < lattice.Length; k++)
			{
				lattice[k] = float.NaN;
			}
			Array.Clear(steep, 0, steep.Length);
			gorgeCache.Clear();
			gorgeState = new byte[Width * Depth];
			wanderCache = new float[Width * Depth];
			for (int k = 0; k < wanderCache.Length; k++)
			{
				wanderCache[k] = float.NaN;
			}
		}

		/// <summary>
		/// How steep the ground may run between cell centres (rise per metre) and still have no cliff hidden between: a wall
		/// high enough to be found at a corner (<see cref="WalkableGrade"/> over half a cell's diagonal, 2.8 m) shows as at
		/// least this much between two centres a cell apart, allowing for a little fall the other way.
		/// </summary>
		private const float SteepHint = 0.6f;

		/// <summary>
		/// True when a cell has a cliff in it (a bank's wall, a fall's face): a corner of it is steeper from its centre than
		/// <see cref="WalkableGrade"/>. Never entered but on a bridge's deck or where a way starts or
		/// ends, since a cliff between two cell centres a step apart is invisible to the step's own grade, and the smoothed
		/// way crossed it (or forded a river down its fall).
		/// </summary>
		/// <remarks>
		/// Asked of the cells a search reaches, not filled for the whole grid: the half-cell ground is read only round a cell
		/// whose centre-to-centre slope to a neighbour is past <see cref="SteepHint"/> (a cliff shows there; open ground
		/// costs nothing), and each corner once. Filling every cell up front read the ground four times as often as
		/// the rest of the plan together.
		/// </remarks>
		public bool IsSteep(int i)
		{
			byte state = steep[i];
			if (state != 0)
			{
				return state == 1;
			}
			int x = i % Width, z = i / Width;
			bool hint = false;
			for (int dz = -1; dz <= 1 && !hint; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					int nx = x + dx, nz = z + dz;
					if ((dx != 0 || dz != 0) && nx >= 0 && nz >= 0 && nx < Width && nz < Depth
						&& Mathf.Abs(Height[Index(nx, nz)] - Height[i]) > SteepHint * Cell * (dx != 0 && dz != 0 ? 1.41421356f : 1f))
					{
						hint = true;
						break;
					}
				}
			}
			// Its four corners (each shared by four cells): any cliff line through the cell has a corner across it from the centre.
			bool found = false;
			if (hint && sampler != null)
			{
				float reach = WalkableGrade * Cell * 0.5f * 1.41421356f;
				int ck = 2 * x + 1, cj = 2 * z + 1;
				for (int corner = 0; corner < 4 && !found; corner++)
				{
					found = Mathf.Abs(Lattice(ck + ((corner & 1) == 0 ? -1 : 1), cj + ((corner & 2) == 0 ? -1 : 1)) - Height[i]) > reach;
				}
			}
			steep[i] = found ? (byte)1 : (byte)2;
			return found;
		}

		/// <summary>The ground at a half-cell point (k, j) from the grid's corner, read the first time it is asked.</summary>
		private float Lattice(int k, int j)
		{
			k = Mathf.Clamp(k, 0, latticeWidth - 1);
			j = Mathf.Clamp(j, 0, latticeDepth - 1);
			if ((k & 1) == 1 && (j & 1) == 1)
			{
				return Height[Index(k >> 1, j >> 1)];
			}
			int at = j * latticeWidth + k;
			float h = lattice[at];
			if (float.IsNaN(h))
			{
				h = sampler(OriginX + k * Cell * 0.5f, OriginZ + j * Cell * 0.5f);
				lattice[at] = h;
			}
			return h;
		}

		/// <summary>The ground at a scene position from the half-cell points round it (each read once, shared by every question).</summary>
		public float GroundAt(float east, float north)
		{
			if (sampler == null)
			{
				return 0f;
			}
			float gx = (east - OriginX) / (Cell * 0.5f), gz = (north - OriginZ) / (Cell * 0.5f);
			int k = Mathf.Clamp(Mathf.FloorToInt(gx), 0, latticeWidth - 2), j = Mathf.Clamp(Mathf.FloorToInt(gz), 0, latticeDepth - 2);
			float fx = Mathf.Clamp01(gx - k), fz = Mathf.Clamp01(gz - j);
			return Mathf.Lerp(Mathf.Lerp(Lattice(k, j), Lattice(k + 1, j), fx), Mathf.Lerp(Lattice(k, j + 1), Lattice(k + 1, j + 1), fx), fz);
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

		/// <summary>
		/// Marks a site's kept ground as its footprint, entered only by that site's own ways: its outline (grown by its margin)
		/// where it has one, else its disc. A cell counts when any of it may lie inside (its centre within half a cell).
		/// </summary>
		public void MarkOwner(in PointOfInterestKeepOut keepOut)
		{
			if (!PointOfInterestOutline.Drawn(keepOut.Outline))
			{
				MarkOwner(keepOut.Id, keepOut.X, keepOut.Z, keepOut.Radius);
				return;
			}
			float radius = keepOut.Radius, half = Cell * 0.5f;
			int x0 = Mathf.Max(0, Mathf.FloorToInt((keepOut.X - radius - OriginX) / Cell)), x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((keepOut.X + radius - OriginX) / Cell));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((keepOut.Z - radius - OriginZ) / Cell)), z1 = Mathf.Min(Depth - 1, Mathf.FloorToInt((keepOut.Z + radius - OriginZ) / Cell));
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					int i = Index(x, z);
					if (Owner[i] == 0 && PointOfInterestOutline.Contains(keepOut.Outline, radius, XOf(x) - keepOut.X, ZOf(z) - keepOut.Z, half))
					{
						Owner[i] = keepOut.Id;
					}
				}
			}
		}

		/// <summary>
		/// Marks everything within an outline round a centre as no way's, not even its own site's (a moat that follows a wall's
		/// outline, and the bailey inside it): every cell that may lie inside <paramref name="outer"/>; its disc of
		/// <paramref name="radius"/> when it has no outline.
		/// </summary>
		public void MarkBlocked(float east, float north, float[] outer, float radius)
		{
			if (!PointOfInterestOutline.Drawn(outer))
			{
				MarkBlocked(east, north, radius);
				return;
			}
			float most = PointOfInterestOutline.Max(outer, radius), half = Cell * 0.5f;
			int x0 = Mathf.Max(0, Mathf.FloorToInt((east - most - OriginX) / Cell)), x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((east + most - OriginX) / Cell));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((north - most - OriginZ) / Cell)), z1 = Mathf.Min(Depth - 1, Mathf.FloorToInt((north + most - OriginZ) / Cell));
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					if (PointOfInterestOutline.Contains(outer, radius, XOf(x) - east, ZOf(z) - north, half))
					{
						Blocked[Index(x, z)] = true;
					}
				}
			}
		}

		/// <summary>Marks a disc no way is routed through, not even its own site's (a moated stronghold: its ways cross on the drawbridge).</summary>
		public void MarkBlocked(float east, float north, float radius)
		{
			ForDisc(east, north, radius, i => Blocked[i] = true);
		}

		/// <summary>Marks a bridge's deck: the cells along its heading within its half-span, a few metres wide.</summary>
		public void MarkBridge(int id, Vector3 centre, float yawDegrees, float halfSpan)
		{
			deckHeight[id] = centre.y;
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

		/// <summary>
		/// Marks the cells a finished way runs through as network of its class: those at its own height (a cell on the
		/// cliff top over a way at its foot is not on the way, and a spur that joined there stepped down the cliff).
		/// </summary>
		public void MarkWay(IReadOnlyList<Vector3> points, ScenePathClass kind, float halfWidth)
		{
			float r = Mathf.Max(Cell * 0.5f, halfWidth);
			float rise = 0.5f + (r + Cell * 0.5f) * WalkableGrade;
			for (int i = 0; i < points.Count; i++)
			{
				float y = points[i].y;
				ForDisc(points[i].x, points[i].z, r, c =>
				{
					if (Mathf.Abs(Height[c] - y) <= rise && (Network[c] == 255 || Network[c] < (byte)kind))
					{
						Network[c] = (byte)kind;
					}
				});
			}
		}

		/// <summary>True when a cell is a bridge's deck: marked by it and no higher than its deck (not the cliff past a bank).</summary>
		public bool IsDeck(int i) => Bridge[i] != 0 && Height[i] <= DeckHeightOf(Bridge[i]) + DeckRise;

		/// <summary>A marked bridge's deck height; +∞ when unknown (every cell it marks is then its deck).</summary>
		public float DeckHeightOf(int id) => deckHeight.TryGetValue(id, out float y) ? y : float.PositiveInfinity;

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
			searchStart = start;
			searchGoal = goal;

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

				// Across a gorge from its rim: one long step to the far rim, priced as the bridge it will be.
				gorgeCells.Clear();
				gorgePrices.Clear();
				GorgeSteps(current, cx, cz, request, gorgeCells, gorgePrices);
				for (int k = 0; k < gorgeCells.Count; k++)
				{
					int next = gorgeCells[k], nx = next % Width, nz = next / Width;
					if (nx < bx0 || nz < bz0 || nx > bx1 || nz > bz1 || stamp[next] == search && closed[next])
					{
						continue;
					}
					float total = cost[current] + gorgePrices[k];
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

		/// <summary>
		/// The longest span a gorge is bridged with, rim to rim, metres: about what a medieval stone arch or a timber truss
		/// (with no pier standing in the gorge, which nobody could build there) carried; wider is gone round.
		/// </summary>
		public const float MaxGorgeSpan = 40f;

		/// <summary>How far below both rims a gorge's floor lies at least, metres: shallower is a dip a way walks through.</summary>
		public const float GorgeDepth = 3f;

		private readonly List<int> gorgeCells = new List<int>();
		private readonly List<float> gorgePrices = new List<float>();
		/// <summary>
		/// Each rim cell's far rims and spans, found once (the ground does not change between searches, and every search
		/// over the same country asks again); an empty array where a cell has none.
		/// </summary>
		private readonly Dictionary<int, (int cell, float span)[]> gorgeCache = new Dictionary<int, (int cell, float span)[]>();
		/// <summary>Per cell: 0 not yet asked, 1 no gorge from it, 2 its far rims are in <see cref="gorgeCache"/> (most cells: one byte read).</summary>
		private byte[] gorgeState = Array.Empty<byte>();
		/// <summary>Each cell's trail noise (<see cref="Noise"/>), worked out once: every trail's search asks it of every step.</summary>
		private float[] wanderCache = Array.Empty<float>();
		private readonly List<(int cell, float span)> gorgeScratch = new List<(int cell, float span)>();

		/// <summary>
		/// The far rims a gorge bridge from this cell could reach: along each of sixteen headings, past a wall dropping from
		/// this rim steeper than walkable, over a floor at least <see cref="GorgeDepth"/> below both rims (no ridge, no hole,
		/// no lake), up a wall to the first walkable rim cell within <see cref="MaxGorgeSpan"/> whose height the deck reaches
		/// at no more than its 20° (<see cref="PropsFeature.MaxDeckSlopeDegrees"/>). Each is priced as a new bridge:
		/// the crossing's effort once and three times every metre of span, so it is used where it saves a long way round.
		/// </summary>
		/// <remarks>
		/// The router plans river bridges by walking through the river's cells, which a cliff-walled channel's walls refuse
		/// (<see cref="IsSteep"/>): without this a gorge was a wall across the map.
		/// </remarks>
		private void GorgeSteps(int from, int fx, int fz, in Request request, List<int> cells, List<float> prices)
		{
			byte state = gorgeState[from];
			if (state == 1)
			{
				return;
			}
			if (state == 0)
			{
				(int cell, float span)[] found = FarRims(from, fx, fz);
				gorgeState[from] = found.Length > 0 ? (byte)2 : (byte)1;
				if (found.Length == 0)
				{
					return;
				}
				gorgeCache[from] = found;
			}
			(int cell, float span)[] rims = gorgeCache[from];
			foreach ((int cell, float span) in rims)
			{
				if (Passable(cell, request))
				{
					cells.Add(cell);
					prices.Add(span * 4f + BridgeCost(request.Class));
				}
			}
		}

		/// <summary>The far rims across a gorge from a cell, and the span to each (see <see cref="GorgeSteps"/>).</summary>
		private (int cell, float span)[] FarRims(int from, int fx, int fz)
		{
			if (Water[from] == PathCellWater.River || Water[from] == PathCellWater.Open || Blocked[from])
			{
				return Array.Empty<(int cell, float span)>();
			}
			// Only from beside a rim: a neighbour with a cliff in it, or dropping away steeper than walkable; the gorge lies
			// that way.
			float rim = Height[from];
			var down = Vector2.zero;
			for (int k = 0; k < 8; k++)
			{
				int nx = fx + StepX[k], nz = fz + StepZ[k];
				if (nx >= 0 && nz >= 0 && nx < Width && nz < Depth)
				{
					int n = Index(nx, nz);
					if (rim - Height[n] > WalkableGrade * Cell * (k < 4 ? 1f : 1.41421356f) || steep[n] == 1 || steep[n] == 0 && IsSteep(n))
					{
						down += new Vector2(StepX[k], StepZ[k]).normalized;
					}
				}
			}
			if (down.sqrMagnitude < 1e-4f)
			{
				return Array.Empty<(int cell, float span)>();
			}
			down.Normalize();
			gorgeScratch.Clear();
			float deckGrade = Mathf.Tan(PropsFeature.MaxDeckSlopeDegrees * Mathf.Deg2Rad);
			float ox = XOf(fx), oz = ZOf(fz), step = Cell * 0.5f;
			for (int heading = 0; heading < 16; heading++)
			{
				float a = heading * (Mathf.PI / 8f);
				float dx = Mathf.Sin(a), dz = Mathf.Cos(a);
				// Off the rim, not along it or back from it: within 67.5° of the way it drops.
				if (dx * down.x + dz * down.y < 0.38f)
				{
					continue;
				}
				// 0: the near rim's wall (cells with the cliff in them), 1: over the gorge, 2: the far rim's wall.
				int previous = from, phase = 0, landing = -1;
				bool walled = false;
				float deepest = float.PositiveInfinity, previousHeight = rim, landedAt = 0f;
				for (float t = step; t <= MaxGorgeSpan + Cell + 1e-3f; t += step)
				{
					CellOf(ox + dx * t, oz + dz * t, out int x, out int z);
					int c = Index(x, z);
					if (c == previous)
					{
						continue;
					}
					float h = Height[c];
					bool under = h < rim - deckGrade * t - 0.5f;
					if (phase == 0)
					{
						if (!under)
						{
							if (!IsSteep(c))
							{
								// Rim ground on: not heading into a gorge from here.
								break;
							}
							previous = c;
							previousHeight = h;
							continue;
						}
						// Down off the rim: a wall (a cliff in the cell before, or a drop steeper than walkable).
						walled = previous != from || rim - h > WalkableGrade * t;
						if (!walled)
						{
							break;
						}
						phase = 1;
					}
					if (phase == 1)
					{
						if (under)
						{
							if (Blocked[c] || Water[c] == PathCellWater.Open && Bridge[c] == 0 || t > MaxGorgeSpan)
							{
								break;
							}
							deepest = Mathf.Min(deepest, h);
							previous = c;
							previousHeight = h;
							continue;
						}
						// Up the far wall, or a rise in the floor that is no gorge.
						if (!IsSteep(c) && h - previousHeight <= WalkableGrade * Cell)
						{
							break;
						}
						phase = 2;
					}
					// The far rim's wall: on to the first cell without a cliff in it.
					if (!IsSteep(c))
					{
						landing = c;
						landedAt = t;
						break;
					}
					if (h < rim - deckGrade * t - 0.5f)
					{
						break;
					}
					previous = c;
					previousHeight = h;
				}
				if (landing < 0 || Water[landing] == PathCellWater.River || Blocked[landing] || Water[landing] == PathCellWater.Open
					|| Mathf.Abs(Height[landing] - rim) > deckGrade * landedAt || deepest > Mathf.Min(rim, Height[landing]) - GorgeDepth)
				{
					continue;
				}
				if (!gorgeScratch.Exists(r => r.cell == landing))
				{
					gorgeScratch.Add((landing, landedAt));
				}
			}
			return gorgeScratch.Count > 0 ? gorgeScratch.ToArray() : Array.Empty<(int cell, float span)>();
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
			// The cliff test's answer read straight off its cache first: asked of every neighbour of every cell searched.
			byte cliff = steep[i];
			if (cliff != 2 && (cliff == 1 || IsSteep(i)) && i != searchStart && i != searchGoal && !IsDeck(i))
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
			int d2 = sx * sx + sz * sz;
			float unit = d2 == 1 ? 1f : d2 == 2 ? 1.41421356f : 2.23606798f;
			float length = Cell * unit;
			bool deckFrom = IsDeck(from), bridged = IsDeck(to);

			/* The climb, against the class's grade. Along one bridge's deck it is level; onto or off it, from the deck's
			 * height or the ground's under it, whichever is gentler (a low bank ramps up to the deck); never free, or a way
			 * stepped from a cliff top straight onto a deck at its foot. */
			float grade;
			if (deckFrom && bridged && Bridge[from] == Bridge[to])
			{
				grade = 0f;
			}
			else
			{
				float hFrom = deckFrom ? DeckHeightOf(Bridge[from]) : Height[from], hTo = bridged ? DeckHeightOf(Bridge[to]) : Height[to];
				grade = Mathf.Min(Mathf.Abs(Height[to] - Height[from]), Mathf.Abs(hTo - hFrom)) / length;
			}
			float g = grade / Mathf.Max(0.01f, style.MaxGrade);
			if (grade > WalkableGrade || g > GradeLimit && !request.Relaxed)
			{
				return float.PositiveInfinity;
			}
			// A knight's step passes over the two cells beside its line (asked after the climb, which refuses more and costs less).
			if (d2 == 5)
			{
				int ax = fx + (Mathf.Abs(sx) == 2 ? sx / 2 : 0), az = fz + (Mathf.Abs(sz) == 2 ? sz / 2 : 0);
				int bx = fx + (Mathf.Abs(sx) == 2 ? sx / 2 : sx), bz = fz + (Mathf.Abs(sz) == 2 ? sz / 2 : sz);
				if (!Passable(Index(ax, az), request) || !Passable(Index(bx, bz), request))
				{
					return float.PositiveInfinity;
				}
			}
			float climb = 2.5f * g * g + (g > 1f ? 30f * (g - 1f) * (g - 1f) : 0f);

			// The side slope: a bench cut along a hillside, dearer the wider the way.
			Vector2 grad = (Gradient[from] + Gradient[to]) * 0.5f;
			float inv = 1f / unit;
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
				float wander = wanderCache[to];
				if (float.IsNaN(wander))
				{
					wander = Noise(tx * 0.37f + seedNoise, tz * 0.37f - seedNoise);
					wanderCache[to] = wander;
				}
				price *= 1f + 0.3f * wander;
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
