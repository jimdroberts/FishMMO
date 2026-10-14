#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Ships under way on a scene's sea (2026-10-12: "We should have ships floating out in the ocean too?"): lanes over
	/// deep water between its harbours and out to the open sea past the scene's edge, and the cogs, longships and smacks that
	/// ply them, moved by <see cref="FloatingBoats"/> from the synced tick on the server and every client alike: solid, and
	/// with a deck a player can ride (2026-10-12: "Solid sailing ships needs to be done.").
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The lanes.</b> A grid of <see cref="Cell"/> cells over the scene marks the water deep enough for a cog at low tide;
	/// each lane is the cheapest way across it (A*, dearer near the shallows, so ships keep off the coast), pulled straight
	/// where the water allows and its corners rounded. A lane runs from a harbour (a port, a dockyard, a pirates' cove, a
	/// coastal city) to the scene's edge or to another harbour, or across the open sea from edge to edge.
	/// </para>
	/// <para>Seeded by the scene's name: a re-cut sails the same ships on the same lanes.</para>
	/// </remarks>
	public static class PointOfInterestSeaTraffic
	{
		public const string ObjectName = "Sea Traffic";
		/// <summary>The lane grid's cell, metres.</summary>
		private const float Cell = 20f;
		/// <summary>The depth a lane needs under mean sea level, metres: a cog's draft, the tide's fall, and room.</summary>
		private const float LaneDepth = 6f;
		/// <summary>Cells of deep water a lane end wants about it.</summary>
		private const int EndClearance = 2;
		private const int MaxLanes = 7;
		private const int MaxShips = 12;
		/// <summary>How far a harbour looks for its anchorage, metres.</summary>
		private const float AnchorageReach = 700f;

		public static void Build(Transform root, ScenePointsOfInterest points, SceneGenerationRequest request, float width, float depth,
			Func<float, float, float> ground, SceneWater water, List<string> notes)
		{
			if (root == null || points == null || request == null || ground == null || request.Body == null || (request.Layer != null && request.Layer.Underground))
			{
				return;
			}
			if (SurfaceLiquids.For(SolarSystemProfile.Resolve(request.Body), request.Body, out _) != SurfaceLiquid.Water)
			{
				return;
			}
			float sea = PropsFeature.SeaLevelOf(request, water);
			if (float.IsNegativeInfinity(sea))
			{
				return;
			}
			var grid = new Grid(width, depth, ground, sea);
			if (grid.DeepCount < 400)
			{
				return;
			}
			var random = new DeterministicRNG(StableHash(request.SceneName) ^ 0x5EA7A1);

			// The ends: each harbour's anchorage, and the deep water along the scene's edge.
			var harbours = new List<(int cell, string name)>();
			foreach (PointOfInterestRecord record in points.Points)
			{
				if (!IsHarbour(record.Kind))
				{
					continue;
				}
				int anchorage = grid.Nearest(record.Position.x, record.Position.z, AnchorageReach, EndClearance);
				if (anchorage >= 0 && !harbours.Exists(h => grid.Distance(h.cell, anchorage) < 200f))
				{
					harbours.Add((anchorage, record.Name));
				}
			}
			List<int> edge = grid.EdgeCells(EndClearance + 1);

			var lanes = new List<FloatingBoats.Lane>();
			void TryLane(int from, int to, bool portFrom, bool portTo)
			{
				if (lanes.Count >= MaxLanes || from < 0 || to < 0 || from == to)
				{
					return;
				}
				List<Vector2> path = grid.Route(from, to);
				if (path == null || path.Count < 2)
				{
					return;
				}
				FloatingBoats.Lane lane = Finish(grid, path, sea, portFrom, portTo);
				if (lane.Length > 300f)
				{
					lanes.Add(lane);
				}
			}
			// Each harbour out to sea, then to its nearest neighbour.
			foreach ((int cell, string _) in harbours)
			{
				if (edge.Count > 0)
				{
					TryLane(cell, FarEdge(grid, edge, cell, random), true, false);
				}
			}
			for (int i = 0; i < harbours.Count; i++)
			{
				int best = -1;
				float nearest = float.PositiveInfinity;
				for (int j = i + 1; j < harbours.Count; j++)
				{
					float d = grid.Distance(harbours[i].cell, harbours[j].cell);
					if (d < nearest && d < 8000f)
					{
						nearest = d;
						best = j;
					}
				}
				if (best >= 0)
				{
					TryLane(harbours[i].cell, harbours[best].cell, true, true);
				}
			}
			// Across the open sea, edge to edge.
			int crossings = Mathf.Min(2, edge.Count / 20);
			for (int i = 0; i < crossings * 4 && lanes.Count < MaxLanes && crossings > 0; i++)
			{
				int a = edge[random.Next(edge.Count)];
				int b = FarEdge(grid, edge, a, random);
				if (grid.Distance(a, b) > 0.4f * Mathf.Max(width, depth))
				{
					int before = lanes.Count;
					TryLane(a, b, false, false);
					if (lanes.Count > before && --crossings == 0)
					{
						break;
					}
				}
			}
			if (lanes.Count == 0)
			{
				return;
			}

			/* Kept by the server (2026-10-12: "Solid sailing ships needs to be done."): the lanes are what every peer
			 * works a ship's pose out from, and each ship's root carries its hull's collider. Only the drawn hulls under the
			 * roots are client-only. No NetworkObject anywhere: a ship's pose is a function of the synced tick, so nothing
			 * about it is sent and no scene id is needed (see FloatingBoats). */
			var holder = new GameObject(ObjectName);
			holder.transform.SetParent(root, false);
			var ships = new List<FloatingBoats.Sailing>();
			for (int l = 0; l < lanes.Count && ships.Count < MaxShips; l++)
			{
				FloatingBoats.Lane lane = lanes[l];
				int count = lane.Length > 3000f ? 2 : 1;
				for (int k = 0; k < count && ships.Count < MaxShips; k++)
				{
					// A cog in the trade, a longship raiding, a smack on a short run between harbours.
					float roll = random.NextFloat();
					string piece = lane.PortAtStart && lane.PortAtEnd && lane.Length < 2500f && roll < 0.35f ? "FishingBoat" : roll < 0.75f ? "Cog" : "Longship";
					int underSail = piece == "Longship" ? 0 : 1, furled = piece == "Longship" ? 1 : 0;
					GameObject sails = StructureKit.Prefab(piece, underSail), atAnchor = StructureKit.Prefab(piece, furled);
					if (sails == null || !StructurePieces.TryHull(piece, out float length, out _, out _))
					{
						continue;
					}
					float speed = (piece == "Longship" ? 3.2f : piece == "Cog" ? 2.6f : 2.2f) * random.Range(0.85f, 1.15f);
					var ship = new FloatingBoats.Sailing
					{
						Lane = l,
						Speed = speed,
						DwellStart = lane.PortAtStart ? random.Range(240f, 600f) : random.Range(300f, 1200f),
						DwellEnd = lane.PortAtEnd ? random.Range(240f, 600f) : random.Range(300f, 1200f),
						Length = length,
						Phase = random.NextFloat(),
					};
					float period = 2f * lane.Length / speed + ship.DwellStart + ship.DwellEnd;
					ship.Offset = period * ((k + random.NextFloat() * 0.5f) / count);
					Vector3 start = lane.Points[0];
					// The root with the hull's collision, the drawn ship under sail (client-only) under it.
					ship.Root = PointOfInterestHarbour.Moor(sails, piece, underSail, holder.transform, start, Quaternion.identity, out ship.Ship);
					if (atAnchor != null && atAnchor != sails)
					{
						ship.Furled = PointOfInterestHarbour.Spawn(atAnchor, piece, furled, ship.Root, start, Quaternion.identity);
						ship.Furled.gameObject.AddComponent<ClientOnlyObject>();
						ship.Furled.gameObject.SetActive(false);
					}
					Solidify(ref ship);
					ships.Add(ship);
				}
			}
			holder.AddComponent<FloatingBoats>().Configure(null, lanes.ToArray(), ships.ToArray());
			float total = 0f;
			foreach (FloatingBoats.Lane lane in lanes)
			{
				total += lane.Length;
			}
			notes?.Add($"Sea traffic: {lanes.Count} lane(s), {total / 1000f:0.0} km, {ships.Count} ship(s) under way ({harbours.Count} harbour(s)).");
		}

		/// <summary>
		/// A ship's root made a hull a rider can stand on and a swimming NPC steers round: the deck's bounds (the collision
		/// mesh's, in the root's space) for <see cref="FloatingBoats.TryCarry"/>, and a NavMesh obstacle the size of the hull.
		/// The obstacle never carves: a carving obstacle re-cuts the NavMesh under it every time it moves, and a ship never
		/// stops moving; an avoiding one costs nothing and is all an agent in the water needs. The ship itself is kept out of
		/// the NavMesh bake (SceneNavMeshBaker), since it will not be where the cut left it.
		/// </summary>
		private static void Solidify(ref FloatingBoats.Sailing ship)
		{
			if (ship.Root == null || !ship.Root.TryGetComponent(out MeshCollider hull) || hull.sharedMesh == null)
			{
				return;
			}
			ship.Deck = hull.sharedMesh.bounds;
			var obstacle = ship.Root.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
			obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
			obstacle.center = ship.Deck.center;
			obstacle.size = ship.Deck.size;
			obstacle.carving = false;
		}

		/// <summary>The kinds a ship sails to.</summary>
		private static bool IsHarbour(POIType kind)
			=> kind == POIType.Port || kind == POIType.Dockyard || kind == POIType.PirateCove || kind == POIType.City || kind == POIType.Capital;

		/// <summary>An edge cell well away from <paramref name="from"/>: of a few drawn, the farthest.</summary>
		private static int FarEdge(Grid grid, List<int> edge, int from, DeterministicRNG random)
		{
			int best = -1;
			float far = -1f;
			for (int i = 0; i < 6; i++)
			{
				int c = edge[random.Next(edge.Count)];
				float d = grid.Distance(from, c);
				if (d > far)
				{
					far = d;
					best = c;
				}
			}
			return best;
		}

		/// <summary>A route's points pulled straight where the deep water allows, its corners rounded, measured along.</summary>
		private static FloatingBoats.Lane Finish(Grid grid, List<Vector2> path, float sea, bool portAtStart, bool portAtEnd)
		{
			// Pulled straight: from each kept point to the farthest it can see over deep water.
			var straight = new List<Vector2> { path[0] };
			int at = 0;
			while (at < path.Count - 1)
			{
				int next = at + 1;
				for (int j = path.Count - 1; j > at + 1; j--)
				{
					if (grid.Sees(path[at], path[j]))
					{
						next = j;
						break;
					}
				}
				straight.Add(path[next]);
				at = next;
			}
			// Rounded twice (Chaikin), kept only where every point is still over deep water.
			List<Vector2> smooth = straight;
			for (int pass = 0; pass < 2; pass++)
			{
				var round = new List<Vector2> { smooth[0] };
				for (int i = 0; i < smooth.Count - 1; i++)
				{
					Vector2 a = smooth[i], b = smooth[i + 1];
					round.Add(Vector2.Lerp(a, b, 0.25f));
					round.Add(Vector2.Lerp(a, b, 0.75f));
				}
				round.Add(smooth[smooth.Count - 1]);
				bool deep = true;
				for (int i = 1; i < round.Count && deep; i++)
				{
					deep = grid.Sees(round[i - 1], round[i]);
				}
				if (!deep)
				{
					break;
				}
				smooth = round;
			}
			var lane = new FloatingBoats.Lane
			{
				Points = new Vector3[smooth.Count],
				Along = new float[smooth.Count],
				PortAtStart = portAtStart,
				PortAtEnd = portAtEnd,
			};
			float along = 0f;
			for (int i = 0; i < smooth.Count; i++)
			{
				if (i > 0)
				{
					along += Vector2.Distance(smooth[i - 1], smooth[i]);
				}
				lane.Points[i] = new Vector3(smooth[i].x, sea, smooth[i].y);
				lane.Along[i] = along;
			}
			return lane;
		}

		private static int StableHash(string text)
		{
			unchecked
			{
				uint h = 2166136261u;
				foreach (char c in text ?? string.Empty)
				{
					h = (h ^ c) * 16777619u;
				}
				return (int)h;
			}
		}

		/// <summary>The scene's water deep enough for a lane, on a grid, with each cell's distance (in cells) from the shallows.</summary>
		private sealed class Grid
		{
			private readonly int w, d;
			private readonly float x0, z0;
			private readonly bool[] deep;
			private readonly int[] clearance;
			public int DeepCount { get; }

			public Grid(float width, float depth, Func<float, float, float> ground, float sea)
			{
				w = Mathf.Max(2, Mathf.FloorToInt(width / Cell));
				d = Mathf.Max(2, Mathf.FloorToInt(depth / Cell));
				x0 = -w * Cell * 0.5f;
				z0 = -d * Cell * 0.5f;
				deep = new bool[w * d];
				float limit = sea - LaneDepth, edgeLimit = sea - LaneDepth * 0.7f;
				const float side = Cell * 0.4f;
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						Vector2 c = Centre(z * w + x);
						bool ok = ground(c.x, c.y) <= limit
							&& ground(c.x + side, c.y) <= edgeLimit && ground(c.x - side, c.y) <= edgeLimit
							&& ground(c.x, c.y + side) <= edgeLimit && ground(c.x, c.y - side) <= edgeLimit;
						deep[z * w + x] = ok;
						DeepCount += ok ? 1 : 0;
					}
				}
				// Distance from the shallows (and from the scene's edge, which a lane may end on but not run along), in cells.
				clearance = new int[w * d];
				var queue = new Queue<int>();
				for (int i = 0; i < clearance.Length; i++)
				{
					if (!deep[i])
					{
						clearance[i] = 0;
						queue.Enqueue(i);
					}
					else
					{
						clearance[i] = int.MaxValue;
					}
				}
				while (queue.Count > 0)
				{
					int i = queue.Dequeue();
					int cx = i % w, cz = i / w;
					for (int n = 0; n < 4; n++)
					{
						int nx = cx + (n == 0 ? 1 : n == 1 ? -1 : 0), nz = cz + (n == 2 ? 1 : n == 3 ? -1 : 0);
						if (nx < 0 || nz < 0 || nx >= w || nz >= d)
						{
							continue;
						}
						int j = nz * w + nx;
						if (clearance[j] > clearance[i] + 1)
						{
							clearance[j] = clearance[i] + 1;
							queue.Enqueue(j);
						}
					}
				}
				for (int i = 0; i < clearance.Length; i++)
				{
					if (clearance[i] == int.MaxValue)
					{
						clearance[i] = 64;
					}
				}
			}

			public Vector2 Centre(int i) => new Vector2(x0 + (i % w + 0.5f) * Cell, z0 + (i / w + 0.5f) * Cell);

			public float Distance(int a, int b) => Vector2.Distance(Centre(a), Centre(b));

			private int CellOf(Vector2 p)
			{
				int x = Mathf.FloorToInt((p.x - x0) / Cell), z = Mathf.FloorToInt((p.y - z0) / Cell);
				return x < 0 || z < 0 || x >= w || z >= d ? -1 : z * w + x;
			}

			/// <summary>The nearest cell to a place with <paramref name="room"/> cells of deep water about it, within a reach; −1 for none.</summary>
			public int Nearest(float x, float z, float reach, int room)
			{
				int best = -1;
				float nearest = reach;
				int span = Mathf.CeilToInt(reach / Cell);
				int cx = Mathf.FloorToInt((x - x0) / Cell), cz = Mathf.FloorToInt((z - z0) / Cell);
				for (int dz = -span; dz <= span; dz++)
				{
					for (int dx = -span; dx <= span; dx++)
					{
						int gx = cx + dx, gz = cz + dz;
						if (gx < 0 || gz < 0 || gx >= w || gz >= d)
						{
							continue;
						}
						int i = gz * w + gx;
						if (!deep[i] || clearance[i] < room)
						{
							continue;
						}
						float dist = Vector2.Distance(Centre(i), new Vector2(x, z));
						if (dist < nearest)
						{
							nearest = dist;
							best = i;
						}
					}
				}
				return best;
			}

			/// <summary>The deep cells on the scene's border ring with room about them.</summary>
			public List<int> EdgeCells(int room)
			{
				var cells = new List<int>();
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						bool border = x == 1 || z == 1 || x == w - 2 || z == d - 2;
						int i = z * w + x;
						// Room counted inward only: the border itself is the edge of the grid, not of the water.
						if (border && deep[i] && InwardRoom(x, z) >= room)
						{
							cells.Add(i);
						}
					}
				}
				return cells;
			}

			private int InwardRoom(int x, int z)
			{
				int dx = x == 1 ? 1 : x == w - 2 ? -1 : 0, dz = z == 1 ? 1 : z == d - 2 ? -1 : 0;
				int n = 0;
				for (int k = 1; k <= 8; k++)
				{
					int gx = x + dx * k, gz = z + dz * k;
					if (gx < 0 || gz < 0 || gx >= w || gz >= d || !deep[gz * w + gx])
					{
						break;
					}
					n++;
				}
				return n;
			}

			/// <summary>Whether a straight run between two points is over deep water with a cell of room either side.</summary>
			public bool Sees(Vector2 a, Vector2 b)
			{
				float length = Vector2.Distance(a, b);
				int steps = Mathf.Max(1, Mathf.CeilToInt(length / (Cell * 0.5f)));
				for (int s = 0; s <= steps; s++)
				{
					int i = CellOf(Vector2.Lerp(a, b, s / (float)steps));
					if (i < 0 || !deep[i] || (clearance[i] < 1))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>The cheapest way over deep water between two cells (A*, eight ways, dearer near the shallows); null for none.</summary>
			public List<Vector2> Route(int from, int to)
			{
				int n = w * d;
				var cost = new float[n];
				var came = new int[n];
				var closed = new bool[n];
				for (int i = 0; i < n; i++)
				{
					cost[i] = float.PositiveInfinity;
					came[i] = -1;
				}
				var open = new Heap();
				cost[from] = 0f;
				Vector2 goal = Centre(to);
				open.Push(from, Vector2.Distance(Centre(from), goal));
				while (open.Count > 0)
				{
					int i = open.Pop();
					if (i == to)
					{
						break;
					}
					if (closed[i])
					{
						continue;
					}
					closed[i] = true;
					int cx = i % w, cz = i / w;
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							if (dx == 0 && dz == 0)
							{
								continue;
							}
							int nx = cx + dx, nz = cz + dz;
							if (nx < 0 || nz < 0 || nx >= w || nz >= d)
							{
								continue;
							}
							int j = nz * w + nx;
							if (!deep[j] || closed[j])
							{
								continue;
							}
							float step = (dx != 0 && dz != 0 ? 1.4142f : 1f) * Cell * (1f + 3f / (1f + clearance[j]));
							float next = cost[i] + step;
							if (next < cost[j])
							{
								cost[j] = next;
								came[j] = i;
								open.Push(j, next + Vector2.Distance(Centre(j), goal));
							}
						}
					}
				}
				if (came[to] < 0)
				{
					return null;
				}
				var path = new List<Vector2>();
				for (int c = to; c >= 0; c = came[c])
				{
					path.Add(Centre(c));
					if (c == from)
					{
						break;
					}
				}
				path.Reverse();
				return path;
			}
		}

		/// <summary>A binary min-heap of cells by priority.</summary>
		private sealed class Heap
		{
			private readonly List<(int cell, float priority)> items = new List<(int, float)>();
			public int Count => items.Count;

			public void Push(int cell, float priority)
			{
				items.Add((cell, priority));
				int i = items.Count - 1;
				while (i > 0)
				{
					int parent = (i - 1) >> 1;
					if (items[parent].priority <= items[i].priority)
					{
						break;
					}
					(items[parent], items[i]) = (items[i], items[parent]);
					i = parent;
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
					int l = 2 * i + 1, r = l + 1, smallest = i;
					if (l < items.Count && items[l].priority < items[smallest].priority)
					{
						smallest = l;
					}
					if (r < items.Count && items[r].priority < items[smallest].priority)
					{
						smallest = r;
					}
					if (smallest == i)
					{
						break;
					}
					(items[smallest], items[i]) = (items[i], items[smallest]);
					i = smallest;
				}
				return top;
			}
		}
	}
}
#endif
