#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How a settlement's streets are laid (2026-10-18: "More interesting and natural town and city layouts"): the medieval
	/// town plans, each grown the way such places grew. Auto picks by the site's kind, its ground and its seed.
	/// </summary>
	/// <remarks>Append only: may be stored by ordinal.</remarks>
	public enum PointOfInterestTownPlan : byte
	{
		/// <summary>By kind, terraces, shore and size, then the site's seed.</summary>
		Auto,
		/// <summary>Houses both sides of one winding street through the village, end to end, a little green at its middle, back lanes behind the crofts.</summary>
		StreetVillage,
		/// <summary>Houses round an irregular green, facing it across the lane that rings it, the roads in joining the lane.</summary>
		GreenVillage,
		/// <summary>The roads in widening into a funnel of a market place, burgage plots behind the houses, back lanes behind the plots.</summary>
		MarketTown,
		/// <summary>Streets along the terraces' contours, lanes climbing the banks' ramps in switchbacks, the church or keep on the top terrace.</summary>
		HillTown,
		/// <summary>Streets from every gate converging on a square, ring streets following the wall, a citadel against the wall.</summary>
		RadialCity,
		/// <summary>A quay street along the shore ahead, streets down to it, warehouses on its seaward side.</summary>
		Port,
		/// <summary>A stronghold's bailey: the keep in the middle (on the top terrace), a lane round inside the wall, buildings backed onto the wall.</summary>
		Bailey,
	}

	/// <summary>One street a layout laid: its centre line (site frame, metres), width and rank (0 main, 1 secondary, 2 lane).</summary>
	public sealed class PointOfInterestLayoutStreet
	{
		public Vector2[] Points;
		public float Width;
		public byte Rank;
	}

	public static partial class PointOfInterestLayouts
	{
		/// <summary>A footprint as an oriented rectangle grown by half the gap.</summary>
		private struct Box
		{
			public Vector2 C, Ax, Az, Half;
			public float Bound;
			/// <summary>Pieces of one non-zero group may touch (a wall ring's modules, a bridge's spans).</summary>
			public int Group;

			public static Box Of(Vector2 centre, float yaw, Vector2 size, float gap, int group)
			{
				Vector2 half = new Vector2(Mathf.Max(0f, size.x) * 0.5f + gap * 0.5f, Mathf.Max(0f, size.y) * 0.5f + gap * 0.5f);
				return new Box
				{
					C = centre,
					Ax = Rotate(Vector2.right, yaw),
					Az = Rotate(Vector2.up, yaw),
					Half = half,
					Bound = half.magnitude,
					Group = group,
				};
			}

			public float Project(Vector2 axis) => Mathf.Abs(Vector2.Dot(Ax, axis)) * Half.x + Mathf.Abs(Vector2.Dot(Az, axis)) * Half.y;

			public bool Overlaps(in Box other)
			{
				Vector2 d = other.C - C;
				float reach = Bound + other.Bound;
				if (d.sqrMagnitude >= reach * reach)
				{
					return false;
				}
				return !(Separated(Ax, d, other) || Separated(Az, d, other) || Separated(other.Ax, d, other) || Separated(other.Az, d, other));
			}

			private bool Separated(Vector2 axis, Vector2 d, in Box other)
				=> Mathf.Abs(Vector2.Dot(d, axis)) >= Project(axis) + other.Project(axis) - 1e-3f;

			/// <summary>Whether the box overlaps a convex polygon by more than a hair: separating axes.</summary>
			public bool Overlaps(Vector2[] polygon)
			{
				if (polygon == null || polygon.Length < 3)
				{
					return false;
				}
				if (SeparatedFrom(polygon, Ax) || SeparatedFrom(polygon, Az))
				{
					return false;
				}
				for (int i = 0; i < polygon.Length; i++)
				{
					Vector2 e = polygon[(i + 1) % polygon.Length] - polygon[i];
					if (e.sqrMagnitude > 1e-8f && SeparatedFrom(polygon, new Vector2(-e.y, e.x).normalized))
					{
						return false;
					}
				}
				return true;
			}

			private bool SeparatedFrom(Vector2[] polygon, Vector2 axis)
			{
				float c = Vector2.Dot(C, axis), r = Project(axis);
				float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
				foreach (Vector2 p in polygon)
				{
					float d = Vector2.Dot(p, axis);
					lo = Mathf.Min(lo, d);
					hi = Mathf.Max(hi, d);
				}
				return c + r <= lo + 1e-3f || c - r >= hi - 1e-3f;
			}
		}

		/// <summary>
		/// Lays a request's pieces. The perimeter follows the site's boundary (its outline's <see cref="Inset"/>), its gates on
		/// the ways' own bearings; the streets follow the town plan (<see cref="PointOfInterestTownPlan"/>); everything else
		/// packs round them inside the boundary.
		/// </summary>
		private sealed class Packer
		{
			private const int PerimeterGroup = 1, SpanGroup = 2, PierGroup = 3, StreetGroup = -1, ClearingGroup = -2, PlazaGroup = -3, PlotGroup = -4, BankGroup = -5;

			/// <summary>What a piece being tried may stand over: a street (the centrepiece), a plaza (the market's stalls), a plot (a back yard's sheds).</summary>
			[Flags]
			private enum Allow
			{
				None = 0,
				Streets = 1,
				Plaza = 2,
				Plots = 4,
			}

			/// <summary>Which boundary a piece must stand inside: none, the site's edge, or inside its wall.</summary>
			private enum Bound
			{
				None,
				Edge,
				Interior,
			}

			/// <summary>A street (or a plaza's edge, which houses front but no one paves): a polyline in the site's frame.</summary>
			private sealed class Way
			{
				public readonly List<Vector2> P = new List<Vector2>();
				public float[] S;
				public float Width;
				public int Rank;
				/// <summary>Which sides houses front it from: 1 the +side, 2 the −side.</summary>
				public int Sides = 3;
				/// <summary>Houses on it keep a plot behind them (a burgage plot, a croft).</summary>
				public bool Plots;
				/// <summary>
				/// Not a street but the line houses back onto (a bailey's curtain): each stands on its fronted side with its back
				/// to the line, facing away from it, whatever its depth.
				/// </summary>
				public bool Backed;

				public float Length => S[S.Length - 1];

				public void Measure()
				{
					S = new float[P.Count];
					for (int i = 1; i < P.Count; i++)
					{
						S[i] = S[i - 1] + Vector2.Distance(P[i - 1], P[i]);
					}
				}

				/// <summary>The point and unit tangent at arc length <paramref name="t"/>.</summary>
				public (Vector2 point, Vector2 tangent) At(float t)
				{
					t = Mathf.Clamp(t, 0f, Length);
					int lo = 0, hi = S.Length - 1;
					while (hi - lo > 1)
					{
						int mid = (lo + hi) >> 1;
						if (S[mid] <= t)
						{
							lo = mid;
						}
						else
						{
							hi = mid;
						}
					}
					float span = Mathf.Max(1e-4f, S[hi] - S[lo]);
					Vector2 d = P[hi] - P[lo];
					return (Vector2.Lerp(P[lo], P[hi], (t - S[lo]) / span), d.sqrMagnitude > 1e-8f ? d.normalized : Vector2.up);
				}

				/// <summary>The arc length of the way's point nearest <paramref name="q"/>.</summary>
				public float Nearest(Vector2 q)
				{
					float best = float.PositiveInfinity, at = 0f;
					for (int i = 1; i < P.Count; i++)
					{
						Vector2 a = P[i - 1], ab = P[i] - a;
						float u = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
						float d = (a + ab * u - q).sqrMagnitude;
						if (d < best)
						{
							best = d;
							at = S[i - 1] + u * (S[i] - S[i - 1]);
						}
					}
					return at;
				}
			}

			/// <summary>One side of a way, filled from a starting point one way along it.</summary>
			private sealed class Front
			{
				public Way Way;
				public int Side;
				public int Direction;
				public float Cursor;
				public bool Closed;
			}

			private readonly PointOfInterestLayoutRequest request;
			private readonly DeterministicRNG random;
			/// <summary>The town plan's own stream (<see cref="PointOfInterestLayoutRequest.Seed"/>): the streets move nothing else.</summary>
			private readonly DeterministicRNG plan;
			private readonly PointOfInterestSlotRole[] roles;
			private readonly List<Box> boxes = new List<Box>();
			private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
			private int[] seen = new int[64];
			private int stamp;
			private readonly List<(Vector2[] polygon, int group)> areas = new List<(Vector2[], int)>();
			private readonly List<Way> ways = new List<Way>();
			private readonly HashSet<int> laidItems = new HashSet<int>();
			public readonly List<PointOfInterestLayoutPiece> Pieces = new List<PointOfInterestLayoutPiece>();
			/// <summary>The boundary's farthest reach, metres (the disc's <see cref="Inset"/> share of the radius).</summary>
			private readonly float r;
			/// <summary>The boundary per half degree of direction in the site's frame; null for a disc.</summary>
			private readonly float[] edge;
			/// <summary>How far inside the boundary a wall keeps everything else, metres.</summary>
			private float wallInset;
			private bool walled;
			/// <summary>The gates' inner faces by entrance bearing, for the streets in from them.</summary>
			private readonly Dictionary<float, float> gateInner = new Dictionary<float, float>();
			/// <summary>Where the market (the green, the square) is: the middle the streets converge on.</summary>
			private Vector2 market;
			/// <summary>Where the first centrepiece stands when it is not the market's (a citadel's keep).</summary>
			private Vector2? citadel;

			// The terraces in the site's frame.
			private readonly bool terraced;
			private readonly Vector2 axis, across;
			private readonly float[] steps, ramps, bankHalf, rampHalf;
			private readonly float rampWidth;

			public Packer(PointOfInterestLayoutRequest request, DeterministicRNG random)
			{
				this.request = request;
				this.random = random;
				plan = new DeterministicRNG(request.Seed);
				roles = new PointOfInterestSlotRole[request.Slots.Count];
				for (int s = 0; s < roles.Length; s++)
				{
					roles[s] = RoleFor(request.Layout, s, request.Slots[s].Role);
				}
				float radius = Mathf.Max(0f, request.Radius);
				r = radius * Inset;
				bool outlined = PointOfInterestOutline.Drawn(request.Outline);
				PointOfInterestShape moat = request.Moat;
				if (outlined || moat != null)
				{
					edge = new float[720];
					float most = 0f;
					for (int j = 0; j < edge.Length; j++)
					{
						float local = j * 0.5f;
						float e = Inset * PointOfInterestOutline.ReachToward(request.Outline, radius, local + request.Yaw);
						if (moat != null)
						{
							// Never past the ditch's inner bank, less a footing's clearance: a wall stands on the berm, not in the moat.
							Vector2 world = Rotate(Dir(local * Mathf.Deg2Rad), request.Yaw);
							e = Mathf.Min(e, MoatShaper.InnerToward(moat, world.x, world.y) - MoatClearMetres);
						}
						edge[j] = Mathf.Max(0.5f, e);
						most = Mathf.Max(most, edge[j]);
					}
					r = most;
				}
				if (request.Pad.HasValue && PointOfInterestTerraces.Terraced(request.Pad.Value))
				{
					PointOfInterestPad pad = request.Pad.Value;
					terraced = true;
					axis = Rotate(pad.TerraceAxis, -request.Yaw).normalized;
					across = PointOfInterestTerraces.Across(axis);
					steps = (float[])pad.TerraceSteps.Clone();
					ramps = new float[steps.Length];
					bankHalf = new float[steps.Length];
					rampHalf = new float[steps.Length];
					for (int i = 0; i < steps.Length; i++)
					{
						ramps[i] = pad.TerraceRamps != null && i < pad.TerraceRamps.Length ? pad.TerraceRamps[i] : 0f;
						bankHalf[i] = PointOfInterestTerraces.BankHalf(pad, i);
						rampHalf[i] = PointOfInterestTerraces.RampHalf(pad, i);
					}
					rampWidth = pad.RampHalfWidth;
				}
			}

			private float Gap => Mathf.Max(0f, request.Gap);

			// ── The boundary ────────────────────────────────────────

			/// <summary>The boundary toward a site-frame offset, metres from the centre.</summary>
			private float Edge(Vector2 p)
			{
				if (edge == null)
				{
					return r;
				}
				float f = DirectionOf(p) * 2f;
				int i = Mathf.FloorToInt(f) % edge.Length;
				return Mathf.Lerp(edge[i], edge[(i + 1) % edge.Length], f - Mathf.Floor(f));
			}

			private float InnerEdge(Vector2 p) => Mathf.Max(0f, Edge(p) - wallInset);

			/// <summary>The inner boundary's mean reach, metres: the size the plan is chosen by.</summary>
			private float MeanInner
			{
				get
				{
					if (edge == null)
					{
						return Mathf.Max(0f, r - wallInset);
					}
					float sum = 0f;
					foreach (float e in edge)
					{
						sum += e;
					}
					return Mathf.Max(0f, sum / edge.Length - wallInset);
				}
			}

			private float MostInner => Mathf.Max(0f, r - wallInset);

			private bool Inside(Vector2 centre, float yaw, Vector2 size, Bound bound)
			{
				if (bound == Bound.None)
				{
					return true;
				}
				var four = new Vector2[4];
				Corners(centre, yaw, size, four);
				foreach (Vector2 c in four)
				{
					float limit = bound == Bound.Edge ? Edge(c) : InnerEdge(c);
					if (c.sqrMagnitude > (limit + 1e-3f) * (limit + 1e-3f))
					{
						return false;
					}
				}
				return true;
			}

			private bool InsidePoint(Vector2 p, float margin) => p.magnitude <= InnerEdge(p) - margin;

			// ── Keeping clear ───────────────────────────────────────

			private const float CellMetres = 8f;

			private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

			private void Add(in Box box)
			{
				int index = boxes.Count;
				boxes.Add(box);
				int x0 = Mathf.FloorToInt((box.C.x - box.Bound) / CellMetres), x1 = Mathf.FloorToInt((box.C.x + box.Bound) / CellMetres);
				int z0 = Mathf.FloorToInt((box.C.y - box.Bound) / CellMetres), z1 = Mathf.FloorToInt((box.C.y + box.Bound) / CellMetres);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = Key(x, z);
						if (!cells.TryGetValue(key, out List<int> list))
						{
							list = new List<int>();
							cells[key] = list;
						}
						list.Add(index);
					}
				}
				if (seen.Length < boxes.Count)
				{
					Array.Resize(ref seen, boxes.Count * 2);
				}
			}

			private bool Fits(in Box box, Allow allow = Allow.None)
			{
				stamp++;
				int x0 = Mathf.FloorToInt((box.C.x - box.Bound) / CellMetres), x1 = Mathf.FloorToInt((box.C.x + box.Bound) / CellMetres);
				int z0 = Mathf.FloorToInt((box.C.y - box.Bound) / CellMetres), z1 = Mathf.FloorToInt((box.C.y + box.Bound) / CellMetres);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						if (!cells.TryGetValue(Key(x, z), out List<int> list))
						{
							continue;
						}
						foreach (int i in list)
						{
							if (seen[i] == stamp)
							{
								continue;
							}
							seen[i] = stamp;
							Box other = boxes[i];
							if (other.Group != 0 && other.Group == box.Group)
							{
								continue;
							}
							if ((other.Group == StreetGroup && (allow & Allow.Streets) != 0) || (other.Group == PlotGroup && (allow & Allow.Plots) != 0))
							{
								continue;
							}
							if (box.Overlaps(other))
							{
								return false;
							}
						}
					}
				}
				foreach ((Vector2[] polygon, int group) in areas)
				{
					if (group == PlazaGroup && (allow & Allow.Plaza) != 0)
					{
						continue;
					}
					if (box.Overlaps(polygon))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Lays a piece when it fits inside <paramref name="bound"/> and clear of everything; returns whether it did.</summary>
			private bool TryLay(int slot, int item, Vector2 centre, float yaw, Vector2 size, Bound bound, int group = 0, Allow allow = Allow.None)
			{
				if (!Inside(centre, yaw, size, bound))
				{
					return false;
				}
				Box box = Box.Of(centre, yaw, size, Gap, group);
				if (!Fits(box, allow))
				{
					return false;
				}
				Lay(slot, item, centre, yaw, size, box);
				return true;
			}

			private void Lay(int slot, int item, Vector2 centre, float yaw, Vector2 size, in Box box)
			{
				Add(box);
				if (item >= 0)
				{
					laidItems.Add(item);
				}
				Pieces.Add(new PointOfInterestLayoutPiece
				{
					Slot = slot,
					Item = item,
					Role = roles[slot],
					Offset = centre,
					Yaw = Mathf.Repeat(yaw, 360f),
					Footprint = size,
				});
			}

			private static Vector2 Dir(float radians) => new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));

			// ── The order ───────────────────────────────────────────

			public void Run()
			{
				int perimeter = FirstSlot(PointOfInterestSlotRole.Perimeter), gate = FirstSlot(PointOfInterestSlotRole.Gate), tower = FirstSlot(PointOfInterestSlotRole.WallTower);
				if (perimeter >= 0)
				{
					Perimeter(perimeter, gate, tower);
				}
				else
				{
					Loose(gate, tower);
				}
				for (int s = 0; s < roles.Length; s++)
				{
					if (roles[s] == PointOfInterestSlotRole.Span)
					{
						Span(s);
					}
				}
				for (int s = 0; s < roles.Length; s++)
				{
					if (roles[s] == PointOfInterestSlotRole.Pier)
					{
						Pier(s);
					}
				}
				Banks();
				if (HasItems(PointOfInterestSlotRole.Street))
				{
					Town();
				}
				foreach (Vector3 clearing in request.Clearings)
				{
					Add(Box.Of(new Vector2(clearing.x, clearing.z), 0f, Vector2.one * (2f * Mathf.Max(0f, clearing.y)), 0f, ClearingGroup));
				}
				Centre();
				Line();
				for (int s = 0; s < roles.Length; s++)
				{
					if (roles[s] == PointOfInterestSlotRole.Ring)
					{
						Ring(s);
					}
				}
				Fronts();
				Rows();
				Scatter();
			}

			private int FirstSlot(PointOfInterestSlotRole role)
			{
				for (int s = 0; s < roles.Length; s++)
				{
					if (roles[s] == role)
					{
						return s;
					}
				}
				return -1;
			}

			private bool HasItems(PointOfInterestSlotRole role)
			{
				foreach (PointOfInterestLayoutItem item in request.Items)
				{
					if (Of(item) == role)
					{
						return true;
					}
				}
				return false;
			}

			private PointOfInterestSlotRole Of(PointOfInterestLayoutItem item)
				=> item.Slot >= 0 && item.Slot < roles.Length ? roles[item.Slot] : PointOfInterestSlotRole.Scatter;

			// ── The perimeter ───────────────────────────────────────

			private struct Planned
			{
				public int Slot;
				public Vector2 C;
				public float Yaw;
				public Vector2 Size;
			}

			/// <summary>The wall ring, its gates and its towers; drawn in until every corner lies inside the boundary.</summary>
			private void Perimeter(int wall, int gate, int tower)
			{
				float pull = 0f;
				List<Planned> planned = null;
				float inset = 0f;
				for (int attempt = 0; attempt < 8; attempt++)
				{
					planned = PlanPerimeter(wall, gate, tower, pull, out inset);
					if (planned == null)
					{
						return;
					}
					// The farthest any corner stands past the boundary.
					float over = 0f;
					var four = new Vector2[4];
					foreach (Planned p in planned)
					{
						Corners(p.C, p.Yaw, p.Size, four);
						foreach (Vector2 c in four)
						{
							over = Mathf.Max(over, c.magnitude - Edge(c));
						}
					}
					if (over <= 1e-3f)
					{
						break;
					}
					pull += over + 0.05f;
					planned = null;
				}
				if (planned == null)
				{
					return;
				}
				gateInner.Clear();
				foreach (Planned p in planned)
				{
					Lay(p.Slot, -1, p.C, p.Yaw, p.Size, Box.Of(p.C, p.Yaw, p.Size, Gap, PerimeterGroup));
					if (p.Slot == gate)
					{
						gateInner[Mathf.Round(DirectionOf(p.C) * 100f) / 100f] = p.C.magnitude - p.Size.y * 0.5f;
					}
				}
				walled = request.Slots[wall].Module > 0f;
				wallInset = Mathf.Max(0f, inset);
			}

			/// <summary>
			/// The gates' bearings, degrees in the site's frame: the ways' entrances first, then the heading, then into the widest
			/// gaps left, so a ring of four with two roads opens on the roads and splits what is left.
			/// </summary>
			private List<float> GateBearings(int count)
			{
				var bearings = new List<float>();
				foreach (float e in request.Entrances)
				{
					float b = Mathf.Repeat(e, 360f);
					if (bearings.Count < count && !bearings.Exists(x => Mathf.Abs(Mathf.DeltaAngle(x, b)) < 1f))
					{
						bearings.Add(b);
					}
				}
				if (bearings.Count == 0 && count > 0)
				{
					bearings.Add(0f);
				}
				while (bearings.Count < count)
				{
					bearings.Sort();
					float widest = -1f, at = 0f;
					for (int i = 0; i < bearings.Count; i++)
					{
						float a = bearings[i], b = i + 1 < bearings.Count ? bearings[i + 1] : bearings[0] + 360f;
						if (b - a > widest + 1e-3f)
						{
							widest = b - a;
							at = Mathf.Repeat(a + (b - a) * 0.5f, 360f);
						}
					}
					bearings.Add(at);
				}
				return bearings;
			}

			/// <summary>
			/// Plans the ring along the boundary <paramref name="pull"/> metres further in: the wall's line follows the outline,
			/// each gate on its bearing square to it (its front facing out along the bearing), and between each pair of gates the
			/// wall's modules laid as equal chords of the line from jamb to jamb, each fitted to its chord (its own length within a
			/// few per cent) and run on past the corner by what keeps its outer face meeting its neighbour's.
			/// </summary>
			private List<Planned> PlanPerimeter(int wall, int gate, int tower, float pull, out float inset)
			{
				PointOfInterestLayoutSlot w = request.Slots[wall];
				var planned = new List<Planned>();
				Vector2 towerSize = tower >= 0 ? request.Slots[tower].Footprint : Vector2.zero;
				Vector2 gateSize = gate >= 0 ? request.Slots[gate].Footprint : Vector2.zero;
				float depth = Mathf.Max(0f, w.Footprint.y);
				float module = w.Module;
				inset = 0f;

				if (module <= 0f)
				{
					// Free-standing pieces round the edge (stones, posts): evenly, facing out; gates on the ways' bearings first.
					int n = Math.Max(0, w.Count);
					float half = w.Footprint.magnitude * 0.5f;
					int ways = gate >= 0 ? Math.Max(Math.Max(1, request.Slots[gate].Count), request.Entrances.Count) : 0;
					foreach (float b in GateBearings(ways))
					{
						Vector2 dir = Dir(b * Mathf.Deg2Rad);
						planned.Add(new Planned { Slot = gate, C = dir * Mathf.Max(0f, Edge(dir) - pull - gateSize.magnitude * 0.5f), Yaw = FacingYaw(b), Size = gateSize });
					}
					for (int i = 0; i < n; i++)
					{
						float a = (i + 0.5f) * Mathf.PI * 2f / Math.Max(1, n);
						Vector2 dir = Dir(a);
						planned.Add(new Planned { Slot = wall, C = dir * Mathf.Max(0f, Edge(dir) - pull - half), Yaw = FacingYaw(a * Mathf.Rad2Deg), Size = w.Footprint });
					}
					int towers = tower >= 0 ? Math.Max(0, request.Slots[tower].Count) : 0;
					for (int t = 0; t < towers; t++)
					{
						float a = (t + 0.25f) * Mathf.PI * 2f / towers;
						Vector2 dir = Dir(a);
						planned.Add(new Planned { Slot = tower, C = dir * Mathf.Max(0f, Edge(dir) - pull - towerSize.magnitude * 0.5f), Yaw = FacingYaw(a * Mathf.Rad2Deg), Size = towerSize });
					}
					inset = pull + 2f * Mathf.Max(half, Mathf.Max(gateSize.magnitude, towerSize.magnitude) * 0.5f) + Gap;
					return planned;
				}

				// The wall's line: the boundary less the deepest half of anything standing on it.
				float off = pull + Mathf.Max(depth * 0.5f, towerSize.magnitude * 0.5f);
				const int M = 720;
				var line = new Vector2[M];
				float circumference = 0f;
				for (int j = 0; j < M; j++)
				{
					Vector2 dir = Dir(j * Mathf.PI * 2f / M);
					line[j] = dir * Mathf.Max(0.5f, Edge(dir) - off);
				}
				for (int j = 0; j < M; j++)
				{
					circumference += Vector2.Distance(line[j], line[(j + 1) % M]);
				}
				if (circumference < module * 3f)
				{
					return null;
				}
				Vector2 LineAt(float degrees)
				{
					Vector2 dir = Dir(degrees * Mathf.Deg2Rad);
					return dir * Mathf.Max(0.5f, Edge(dir) - off);
				}

				// A gatehouse spans whole modules; a ring with no gate keeps an opening at the heading (or its first way's).
				float gateLength = gate >= 0 ? Mathf.Max(request.Slots[gate].Module, gateSize.x) : Math.Max(1, Mathf.CeilToInt(EntranceMetres / module - 1e-3f)) * module;
				int most = Math.Max(1, Mathf.FloorToInt(circumference / (gateLength + 2f * module)));
				int count = gate >= 0 ? Mathf.Clamp(Math.Max(request.Slots[gate].Count, request.Entrances.Count), 1, most) : 1;
				List<float> bearings = GateBearings(count);
				bearings.Sort();

				// Each gate (or opening) on its bearing, square to the bearing; its jambs where the wall's runs end.
				var jambs = new List<(Vector2 left, Vector2 right)>();
				foreach (float b in bearings)
				{
					Vector2 dir = Dir(b * Mathf.Deg2Rad);
					Vector2 at = LineAt(b);
					Vector2 side = new Vector2(dir.y, -dir.x);
					// The side vector points a right angle round toward the higher bearing: the left jamb is the one toward the lower.
					jambs.Add((at - side * (gateLength * 0.5f), at + side * (gateLength * 0.5f)));
					if (gate >= 0)
					{
						planned.Add(new Planned { Slot = gate, C = at, Yaw = FacingYaw(b), Size = gateSize });
					}
				}

				// The runs between consecutive gates: jamb, the line's points between, the next jamb.
				var vertices = new List<(Vector2 at, bool jamb)>();
				for (int g = 0; g < bearings.Count; g++)
				{
					float from = bearings[g], to = g + 1 < bearings.Count ? bearings[g + 1] : bearings[0] + 360f;
					Vector2 start = jambs[g].right, end = jambs[(g + 1) % bearings.Count].left;
					var run = new List<Vector2> { start };
					float a0 = DirectionOf(start), a1 = DirectionOf(end);
					float s0 = Mathf.Repeat(a0 - from, 360f) + from, s1 = Mathf.Repeat(a1 - from, 360f) + from;
					if (s1 <= s0 || s1 > to + 1f)
					{
						continue;
					}
					for (int j = Mathf.CeilToInt(s0 * 2f + 1e-3f); j < s1 * 2f; j++)
					{
						run.Add(line[((j % M) + M) % M]);
					}
					run.Add(end);
					List<Vector2> chords = Chords(run, module);
					if (chords == null)
					{
						continue;
					}
					for (int k = 0; k + 1 < chords.Count; k++)
					{
						Vector2 a = chords[k], b = chords[k + 1];
						Vector2 d = b - a, mid = (a + b) * 0.5f;
						var normal = new Vector2(d.y, -d.x).normalized;
						if (Vector2.Dot(normal, mid) < 0f)
						{
							normal = -normal;
						}
						// Run on past each corner by what closes the wedge its outer face would leave there (half each side).
						float turnIn = k > 0 ? Turn(chords[k - 1], a, b) : 0f, turnOut = k + 2 < chords.Count ? Turn(a, b, chords[k + 2]) : 0f;
						float extra = depth * 0.5f * (Mathf.Tan(Mathf.Min(turnIn, 1f) * 0.5f) + Mathf.Tan(Mathf.Min(turnOut, 1f) * 0.5f));
						Vector2 along = d.normalized;
						Vector2 centre = mid + along * (depth * 0.25f * (Mathf.Tan(Mathf.Min(turnOut, 1f) * 0.5f) - Mathf.Tan(Mathf.Min(turnIn, 1f) * 0.5f)));
						planned.Add(new Planned { Slot = wall, C = centre, Yaw = FacingYaw(DirectionOf(normal)), Size = new Vector2(d.magnitude + extra, depth) });
						if (k > 0)
						{
							vertices.Add((a, false));
						}
					}
					if (gate < 0)
					{
						// An opening is flanked by towers: its jambs are corners too.
						vertices.Add((start, true));
						vertices.Add((end, true));
					}
				}

				int towerCount = tower >= 0 ? Math.Max(0, request.Slots[tower].Count) : 0;
				float towerInner = 0f;
				if (towerCount > 0 && vertices.Count > 0)
				{
					// Evenly round the ring, each at the sharpest corner near its share of the way round.
					vertices.Sort((p, q) => DirectionOf(p.at).CompareTo(DirectionOf(q.at)));
					var used = new bool[vertices.Count];
					int n = Math.Min(towerCount, vertices.Count);
					float offset = bearings.Count > 0 ? bearings[0] : 0f;
					for (int t = 0; t < n; t++)
					{
						float target = Mathf.Repeat(offset + (t + 0.5f) * 360f / n, 360f);
						int best = -1;
						float bestScore = float.PositiveInfinity;
						for (int i = 0; i < vertices.Count; i++)
						{
							if (used[i])
							{
								continue;
							}
							float gap = Mathf.Abs(Mathf.DeltaAngle(DirectionOf(vertices[i].at), target)) / (180f / n);
							float sharp = Corner(vertices, i);
							float score = gap - 0.6f * sharp;
							if (score < bestScore)
							{
								bestScore = score;
								best = i;
							}
						}
						if (best < 0)
						{
							break;
						}
						used[best] = true;
						Vector2 at = vertices[best].at;
						planned.Add(new Planned { Slot = tower, C = at, Yaw = FacingYaw(DirectionOf(at)), Size = towerSize });
						towerInner = towerSize.magnitude * 0.5f;
					}
				}
				inset = off + Mathf.Max(Mathf.Max(depth * 0.5f, gate >= 0 ? gateSize.y * 0.5f : 0f), towerInner) + Gap;
				return planned;
			}

			/// <summary>The turn at <paramref name="b"/> from a→b to b→c, radians (0 straight on).</summary>
			private static float Turn(Vector2 a, Vector2 b, Vector2 c)
			{
				Vector2 u = b - a, v = c - b;
				if (u.sqrMagnitude < 1e-8f || v.sqrMagnitude < 1e-8f)
				{
					return 0f;
				}
				return Mathf.Acos(Mathf.Clamp(Vector2.Dot(u.normalized, v.normalized), -1f, 1f));
			}

			/// <summary>How sharp a ring corner is, 0 … 1 (a right angle and sharper is 1).</summary>
			private static float Corner(List<(Vector2 at, bool jamb)> vertices, int i)
			{
				if (vertices[i].jamb)
				{
					return 1f;
				}
				int n = vertices.Count;
				Vector2 a = vertices[(i + n - 1) % n].at, b = vertices[i].at, c = vertices[(i + 1) % n].at;
				return Mathf.Clamp01(Turn(a, b, c) / (Mathf.PI * 0.5f));
			}

			/// <summary>
			/// Points along a polyline from its first to its last, each the same straight distance from the one before, that
			/// distance as near <paramref name="module"/> as a whole number of them allows: the ends of a wall's modules laid
			/// end to end along a curved line. Null when the run is shorter than half a module.
			/// </summary>
			private static List<Vector2> Chords(List<Vector2> run, float module)
			{
				float length = 0f;
				for (int i = 1; i < run.Count; i++)
				{
					length += Vector2.Distance(run[i - 1], run[i]);
				}
				int n = Mathf.RoundToInt(length / module);
				if (n < 1)
				{
					return null;
				}
				Vector2 end = run[run.Count - 1];
				float lo = 0.5f * length / n, hi = 1.05f * length / n;
				List<Vector2> best = null;
				for (int iteration = 0; iteration < 32; iteration++)
				{
					float c = 0.5f * (lo + hi);
					List<Vector2> points = Walk(run, c, n, out bool ran);
					if (ran)
					{
						// Ran off the end before n chords: too long.
						hi = c;
						continue;
					}
					best = points;
					// Short of the end by more than the last chord's slack: longer.
					float left = Vector2.Distance(points[points.Count - 1], end);
					if (left < 1e-3f)
					{
						break;
					}
					lo = c;
				}
				if (best == null)
				{
					return null;
				}
				best[best.Count - 1] = end;
				return best;
			}

			/// <summary>
			/// Walks <paramref name="n"/> chords of length <paramref name="c"/> along a polyline from its start; <paramref name="ran"/>
			/// when it runs off the end first. The last point is where the n-th chord ends.
			/// </summary>
			private static List<Vector2> Walk(List<Vector2> run, float c, int n, out bool ran)
			{
				var points = new List<Vector2> { run[0] };
				Vector2 at = run[0];
				int seg = 0;
				float t = 0f;
				ran = false;
				for (int k = 0; k < n; k++)
				{
					bool found = false;
					while (seg < run.Count - 1)
					{
						Vector2 a = run[seg], b = run[seg + 1], ab = b - a;
						// |a + ab·u − at| = c for the least u ≥ t.
						float qa = ab.sqrMagnitude, qb = 2f * Vector2.Dot(ab, a - at), qc = (a - at).sqrMagnitude - c * c;
						if (qa > 1e-10f)
						{
							float disc = qb * qb - 4f * qa * qc;
							if (disc >= 0f)
							{
								float root = Mathf.Sqrt(disc);
								float u = (-qb + root) / (2f * qa);
								if (u >= t - 1e-6f && u <= 1f + 1e-6f)
								{
									at = a + ab * Mathf.Clamp01(u);
									t = Mathf.Clamp01(u);
									found = true;
									break;
								}
							}
						}
						seg++;
						t = 0f;
					}
					if (!found)
					{
						ran = true;
						return points;
					}
					points.Add(at);
				}
				return points;
			}

			/// <summary>Gates and towers with no wall: a lone gateway on each way's bearing at the edge, towers round it.</summary>
			private void Loose(int gate, int tower)
			{
				if (gate >= 0)
				{
					PointOfInterestLayoutSlot g = request.Slots[gate];
					int n = Math.Max(Math.Max(1, g.Count), request.Entrances.Count);
					foreach (float b in GateBearings(n))
					{
						Vector2 dir = Dir(b * Mathf.Deg2Rad);
						Vector2 at = dir * Mathf.Max(0f, Edge(dir) - g.Footprint.y * 0.5f - 0.5f);
						TryLay(gate, -1, at, FacingYaw(b), g.Footprint, Bound.Edge, PerimeterGroup);
					}
				}
				if (tower >= 0)
				{
					PointOfInterestLayoutSlot t = request.Slots[tower];
					int n = Math.Max(0, t.Count);
					for (int i = 0; i < n; i++)
					{
						float a = (i + 0.5f) * Mathf.PI * 2f / n;
						Vector2 dir = Dir(a);
						Vector2 at = dir * Mathf.Max(0f, Edge(dir) - t.Footprint.magnitude * 0.5f);
						TryLay(tower, -1, at, FacingYaw(a * Mathf.Rad2Deg), t.Footprint, Bound.Edge, PerimeterGroup);
					}
				}
			}

			// ── Spans and piers ─────────────────────────────────────

			/// <summary>A bridge: modules end to end along the heading (across the river), centred on the site.</summary>
			private void Span(int s)
			{
				PointOfInterestLayoutSlot slot = request.Slots[s];
				float module = slot.Module > 0f ? slot.Module : Mathf.Max(1f, slot.Footprint.y);
				float from, to;
				if (!float.IsNaN(request.SpanFrom) && !float.IsNaN(request.SpanTo) && request.SpanTo > request.SpanFrom)
				{
					from = Mathf.Max(request.SpanFrom, -2f * request.Radius);
					to = Mathf.Min(request.SpanTo, 2f * request.Radius);
				}
				else
				{
					float half = request.SpanHalf > 0f ? Mathf.Min(request.SpanHalf, 2f * request.Radius) : r;
					from = -half;
					to = half;
				}
				/* Exactly from one end to the other: as many modules as come nearest the length, each stretched or shrunk a
				 * little to fit (its footprint's depth says by how much, PropsFeature.Build). Whole modules centred on the
				 * crossing ran up to most of a module past each end, over the path that comes to it (2026-10-11). */
				int n = Math.Max(1, Mathf.RoundToInt((to - from) / module));
				float length = (to - from) / n;
				float middle = 0.5f * (from + to);
				for (int i = 0; i < n; i++)
				{
					var at = new Vector2(0f, middle + (i - (n - 1) * 0.5f) * length);
					TryLay(s, -1, at, 0f, new Vector2(slot.Footprint.x, length), Bound.None, SpanGroup);
				}
			}

			/// <summary>Piers from the shore ahead out over the water, side by side; none where the site has no shore.</summary>
			private void Pier(int s)
			{
				if (float.IsNaN(request.ShoreAhead))
				{
					return;
				}
				PointOfInterestLayoutSlot slot = request.Slots[s];
				float module = slot.Module > 0f ? slot.Module : Mathf.Max(1f, slot.Footprint.y);
				int piers = Mathf.Clamp(slot.Count, 1, 4);
				bool measured = !float.IsNaN(request.PierFrom) && !float.IsNaN(request.PierTo);
				int modules = measured
					? Math.Max(2, Mathf.RoundToInt((request.PierTo - request.PierFrom) / module))
					: Math.Max(2, Mathf.RoundToInt(request.Radius * 0.6f / module));
				float spacing = Mathf.Max(slot.Footprint.x * 3f, 8f);
				for (int p = 0; p < piers; p++)
				{
					float x = (p - (piers - 1) * 0.5f) * spacing;
					for (int i = 0; i < modules; i++)
					{
						// From the bank at the deck's height, module by module out over the water; unmeasured, the first
						// module stands half on the shore.
						var at = new Vector2(x, measured ? request.PierFrom + (i + 0.5f) * module : request.ShoreAhead + i * module);
						TryLay(s, -1, at, 0f, new Vector2(slot.Footprint.x, module), Bound.None, PierGroup);
					}
				}
			}

			// ── Terraces ────────────────────────────────────────────

			/// <summary>Keeps every bank (and each ramp's run up it) clear of pieces: nothing is built across a step.</summary>
			private void Banks()
			{
				if (!terraced)
				{
					return;
				}
				float reach = r + 10f;
				for (int i = 0; i < steps.Length; i++)
				{
					Vector2 at = axis * steps[i];
					Add(Box.Of(at, DirectionOf(across), new Vector2(2f * (bankHalf[i] + 1f), 2f * reach), 0f, BankGroup));
					Vector2 ramp = at + across * ramps[i];
					Add(Box.Of(ramp, DirectionOf(axis), new Vector2(2f * (rampWidth + 2f * bankHalf[i] + 0.5f), 2f * (rampHalf[i] + 1f)), 0f, BankGroup));
				}
			}

			/// <summary>Where a site-frame point lies along the terraces' axis, metres.</summary>
			private float U(Vector2 p) => Vector2.Dot(p, axis);

			/// <summary>The terrace (0 lowest) a site-frame point's band is.</summary>
			private int BandOf(Vector2 p)
			{
				float u = U(p);
				int band = 0;
				while (band < steps.Length && u > steps[band])
				{
					band++;
				}
				return band;
			}

			// ── The town plan ───────────────────────────────────────

			/// <summary>A main street's width, metres: wider in a larger place, a city's never over nine.</summary>
			private float MainWidth => Mathf.Clamp(MeanInner * 0.07f, 4f, 9f);
			private float SecondWidth => Mathf.Clamp(MainWidth * 0.7f, 3.5f, 6f);
			private const float LaneWidth = 3f;

			/// <summary>
			/// A street house's usual depth (the median of their footprints' y, which is laid square to the street), metres: how
			/// far a frontage reaches back from its street. The median, not the deepest: one long hall would space every street
			/// for itself.
			/// </summary>
			private float HouseDepth
			{
				get
				{
					var depths = new List<float>();
					foreach (PointOfInterestLayoutItem item in request.Items)
					{
						if (Of(item) == PointOfInterestSlotRole.Street)
						{
							depths.Add(item.Footprint.y);
						}
					}
					if (depths.Count == 0)
					{
						return 7f;
					}
					depths.Sort();
					return Mathf.Max(3f, depths[depths.Count / 2]);
				}
			}

			/// <summary>One way in: its bearing (site frame, degrees), where its street starts (the gate's inner face or the edge) and where the road ends.</summary>
			private struct Entry
			{
				public float Bearing;
				public Vector2 Dir;
				public Vector2 Start;
				public Vector2 End;
			}

			private List<Entry> Entries(bool synthesise)
			{
				var list = new List<Entry>();
				for (int i = 0; i < request.Entrances.Count; i++)
				{
					float b = Mathf.Repeat(request.Entrances[i], 360f);
					if (list.Exists(e => Mathf.Abs(Mathf.DeltaAngle(e.Bearing, b)) < 1f))
					{
						continue;
					}
					float depth = i < request.EntranceDepths.Count ? request.EntranceDepths[i] : float.NaN;
					list.Add(MakeEntry(b, depth));
				}
				if (list.Count == 0 && synthesise)
				{
					// No road comes in yet: the town opens ahead (and, drawn long, at both ends of its length).
					float lie = LongAxis(out float elongation);
					list.Add(MakeEntry(elongation > 1.2f ? lie : 0f, float.NaN));
					list.Add(MakeEntry(Mathf.Repeat((elongation > 1.2f ? lie : 0f) + 180f, 360f), float.NaN));
				}
				return list;
			}

			private Entry MakeEntry(float bearing, float depth)
			{
				Vector2 dir = Dir(bearing * Mathf.Deg2Rad);
				float inner = InnerEdge(dir);
				float start = inner;
				foreach (KeyValuePair<float, float> g in gateInner)
				{
					if (Mathf.Abs(Mathf.DeltaAngle(g.Key, bearing)) < 1f)
					{
						start = Mathf.Min(start, g.Value);
					}
				}
				if (float.IsNaN(depth))
				{
					depth = Edge(dir) / Inset * SiteEntrances.EntryDepth;
				}
				// The road's end, short of the middle. Where it ends in the gate's own passage (a small hold's deep gatehouse), the
				// street starts out through the gate from just beyond it, so the road still ends on the street.
				float end = Mathf.Clamp(depth, 4f, Mathf.Max(4f, Edge(dir) - 1f));
				start = Mathf.Min(Mathf.Max(start, end + 0.5f), Edge(dir) - 0.5f);
				return new Entry { Bearing = bearing, Dir = dir, Start = dir * start, End = dir * Mathf.Min(end, start - 0.5f) };
			}

			/// <summary>The boundary's long axis (degrees in the site's frame) and how much longer it is than it is broad.</summary>
			private float LongAxis(out float elongation)
			{
				float xx = 0f, zz = 0f, xz = 0f;
				for (int k = 0; k < 64; k++)
				{
					Vector2 dir = Dir(k * Mathf.PI * 2f / 64);
					Vector2 p = dir * InnerEdge(dir);
					xx += p.x * p.x;
					zz += p.y * p.y;
					xz += p.x * p.y;
				}
				float angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
				float mean = 0.5f * (xx + zz), spread = Mathf.Sqrt(0.25f * (xx - zz) * (xx - zz) + xz * xz);
				elongation = Mathf.Sqrt((mean + spread) / Mathf.Max(1e-3f, mean - spread));
				// atan2 above measures from +x toward +y; a bearing is from +y toward +x.
				return Mathf.Repeat(90f - angle * Mathf.Rad2Deg, 180f);
			}

			private PointOfInterestTownPlan Choose()
			{
				if (request.Plan != PointOfInterestTownPlan.Auto)
				{
					return request.Plan;
				}
				if (request.Layout == PointOfInterestLayout.Linear)
				{
					return PointOfInterestTownPlan.StreetVillage;
				}
				POIType? kind = request.Kind;
				bool hold = kind == POIType.Keep || kind == POIType.Castle || kind == POIType.Fortress;
				if (hold || (walled && kind == null && MeanInner < 45f))
				{
					return PointOfInterestTownPlan.Bailey;
				}
				if (terraced)
				{
					return PointOfInterestTownPlan.HillTown;
				}
				if (!float.IsNaN(request.ShoreAhead) && FirstSlot(PointOfInterestSlotRole.Pier) >= 0)
				{
					return PointOfInterestTownPlan.Port;
				}
				float size = MeanInner;
				bool village = kind == POIType.Village || kind == POIType.StiltVillage || kind == POIType.DrownedVillage || (kind == null && size < 45f);
				if (village || size < 30f)
				{
					LongAxis(out float elongation);
					List<Entry> entries = Entries(false);
					bool through = false;
					for (int a = 0; a < entries.Count; a++)
					{
						for (int b = a + 1; b < entries.Count; b++)
						{
							through |= Mathf.Abs(Mathf.DeltaAngle(entries[a].Bearing, entries[b].Bearing)) > 120f;
						}
					}
					float roll = plan.NextFloat();
					return elongation > 1.3f || through ? (roll < 0.75f ? PointOfInterestTownPlan.StreetVillage : PointOfInterestTownPlan.GreenVillage)
						: (roll < 0.6f ? PointOfInterestTownPlan.GreenVillage : PointOfInterestTownPlan.StreetVillage);
				}
				bool city = kind == POIType.City || kind == POIType.Capital || (kind == null && size >= 85f);
				if (city)
				{
					return PointOfInterestTownPlan.RadialCity;
				}
				return walled && size >= 70f && plan.NextFloat() < 0.4f ? PointOfInterestTownPlan.RadialCity : PointOfInterestTownPlan.MarketTown;
			}

			/// <summary>Lays the town plan's streets (kept clear), its market and its plots before anything else is fitted round them.</summary>
			private void Town()
			{
				market = Vector2.zero;
				if (MostInner < 8f)
				{
					return;
				}
				PointOfInterestTownPlan chosen = Choose();
				request.ChosenPlan = chosen;
				switch (chosen)
				{
					case PointOfInterestTownPlan.GreenVillage:
						GreenVillage();
						break;
					case PointOfInterestTownPlan.MarketTown:
						MarketTown();
						break;
					case PointOfInterestTownPlan.HillTown:
						if (terraced)
						{
							HillTown();
						}
						else
						{
							MarketTown();
						}
						break;
					case PointOfInterestTownPlan.RadialCity:
						RadialCity();
						break;
					case PointOfInterestTownPlan.Port:
						if (float.IsNaN(request.ShoreAhead))
						{
							MarketTown();
						}
						else
						{
							Port();
						}
						break;
					case PointOfInterestTownPlan.Bailey:
						Bailey();
						break;
					default:
						StreetVillage();
						break;
				}
				if (ways.Count == 0)
				{
					// Nothing fitted: one street along the heading, as a hamlet has.
					List<Entry> entries = Entries(true);
					AddWay(Spline(entries[0].Start, entries[0].End, -entries[0].End * 0.5f, -entries[0].Start), MainWidth, 0, 3, false);
				}
			}

			/// <summary>The two entries furthest apart round the site (a through road), or the one and the far end of the long axis.</summary>
			private (Entry a, Entry b) Through(List<Entry> entries)
			{
				if (entries.Count == 1)
				{
					float lie = LongAxis(out _);
					float opposite = Mathf.Abs(Mathf.DeltaAngle(lie, entries[0].Bearing)) < 90f ? lie + 180f : lie;
					return (entries[0], MakeEntry(Mathf.Repeat(opposite, 360f), float.NaN));
				}
				int ia = 0, ib = 1;
				float widest = -1f;
				for (int a = 0; a < entries.Count; a++)
				{
					for (int b = a + 1; b < entries.Count; b++)
					{
						float gap = Mathf.Abs(Mathf.DeltaAngle(entries[a].Bearing, entries[b].Bearing));
						if (gap > widest)
						{
							widest = gap;
							ia = a;
							ib = b;
						}
					}
				}
				return (entries[ia], entries[ib]);
			}

			private void StreetVillage()
			{
				List<Entry> entries = Entries(true);
				(Entry a, Entry b) = Through(entries);
				float size = MeanInner;
				// The street wanders a little off the straight line between its ends.
				Vector2 chord = b.End - a.End;
				Vector2 side = chord.sqrMagnitude > 1e-4f ? new Vector2(chord.y, -chord.x).normalized : Vector2.right;
				Vector2 middle = (a.End + b.End) * 0.5f + side * (plan.Range(-0.14f, 0.14f) * size);
				middle = Pull(middle, 0.55f);
				Way main = AddWay(Spline(a.Start, a.End, middle, b.End, b.Start), MainWidth, 0, 3, true);
				if (main == null)
				{
					return;
				}
				// The village's little green: the street widening at its middle, where the well stands.
				float t = main.Nearest(middle);
				(Vector2 green, Vector2 tangent) = main.At(t);
				market = green;
				AddPlaza(green, tangent, Mathf.Clamp(size * 0.22f, 8f, 14f), MainWidth * 0.5f + Mathf.Clamp(size * 0.08f, 3f, 6f), 0.35f);
				foreach (Entry e in entries)
				{
					if (e.Bearing == a.Bearing || e.Bearing == b.Bearing)
					{
						continue;
					}
					JoinEntry(e, main, SecondWidth, 1, true);
				}
				// Back lanes behind the crofts, where the village is broad enough.
				if (size >= 34f)
				{
					float behind = MainWidth * 0.5f + Gap + HouseDepth + Mathf.Clamp(size * 0.22f, 7f, 12f) + LaneWidth * 0.5f;
					BackLanes(main, behind);
				}
			}

			private void GreenVillage()
			{
				List<Entry> entries = Entries(true);
				float size = MeanInner;
				Vector2 centre = Pull(new Vector2(plan.Range(-0.1f, 0.1f), plan.Range(-0.1f, 0.1f)) * size, 0.3f);
				float rho = Mathf.Clamp(size * 0.3f, 9f, 20f);
				// The green: seven corners each a little in or out, kept convex.
				const int Corners7 = 7;
				var green = new Vector2[Corners7];
				float turn = plan.NextFloat() * Mathf.PI * 2f;
				for (int k = 0; k < Corners7; k++)
				{
					float jitter = plan.Range(-0.13f, 0.13f);
					green[k] = centre + Dir(turn + k * Mathf.PI * 2f / Corners7) * (rho * (1f + jitter));
				}
				market = centre;
				areas.Add((green, PlazaGroup));
				request.Plazas.Add(green);
				// The lane round it, a little out from its edge, rounded off.
				var ring = new List<Vector2>();
				float push = LaneWidth * 0.5f + 1f;
				foreach (Vector2 g in green)
				{
					Vector2 d = g - centre;
					ring.Add(centre + d * ((d.magnitude + push) / Mathf.Max(1e-3f, d.magnitude)));
				}
				ring = Chaikin(Chaikin(Chaikin(ring, true), true), true);
				ring.Add(ring[0]);
				Way lane = AddWay(ring, SecondWidth, 1, 3, true);
				if (lane != null)
				{
					OutwardOnly(lane, centre);
				}
				foreach (Entry e in entries)
				{
					if (lane != null)
					{
						JoinEntry(e, lane, MainWidth, 0, true);
					}
					else
					{
						AddWay(Spline(e.Start, e.End, centre), MainWidth, 0, 3, true);
					}
				}
			}

			private void MarketTown()
			{
				List<Entry> entries = Entries(true);
				(Entry a, Entry b) = Through(entries);
				float size = MeanInner;
				Vector2 lie = (b.End - a.End).sqrMagnitude > 1e-4f ? (b.End - a.End).normalized : Vector2.up;
				Vector2 centre = Pull(new Vector2(plan.Range(-0.1f, 0.1f), plan.Range(-0.1f, 0.1f)) * size, 0.3f);
				market = centre;
				float length = Mathf.Clamp(size * 0.26f, 12f, 28f), width = Mathf.Clamp(size * 0.11f, 6f, 13f);
				// The market place: a funnel, wide where the main roads meet, narrowing toward the far end.
				Vector2[] place = AddPlaza(centre, plan.NextFloat() < 0.5f ? lie : -lie, length, width, 0.4f);
				Vector2 PlaceEdge(Vector2 from) => Toward(place, centre, from, 1f);
				AddWay(Spline(a.Start, a.End, Bend(a.End, PlaceEdge(a.End), 0.08f), PlaceEdge(a.End)), MainWidth, 0, 3, true);
				AddWay(Spline(b.Start, b.End, Bend(b.End, PlaceEdge(b.End), 0.08f), PlaceEdge(b.End)), MainWidth, 0, 3, true);
				foreach (Entry e in entries)
				{
					if (e.Bearing == a.Bearing || e.Bearing == b.Bearing)
					{
						continue;
					}
					AddWay(Spline(e.Start, e.End, Bend(e.End, PlaceEdge(e.End), 0.1f), PlaceEdge(e.End)), SecondWidth, 1, 3, true);
				}
				// Back lanes behind the burgage plots of the through street, and cross lanes joining them to it.
				float behind = MainWidth * 0.5f + Gap + HouseDepth + Mathf.Clamp(size * 0.2f, 8f, 16f) + LaneWidth * 0.5f;
				var through = new Way { Width = MainWidth };
				through.P.AddRange(Spline(a.Start, a.End, centre, b.End, b.Start));
				through.Measure();
				List<Way> lanes = BackLanes(through, behind);
				int crossings = size >= 60f ? 3 : 2;
				for (int c = 0; c < crossings && lanes.Count > 0; c++)
				{
					float t = through.Length * (c + 1f) / (crossings + 1f) + plan.Range(-6f, 6f);
					(Vector2 p, Vector2 tangent) = through.At(t);
					Vector2 n = new Vector2(tangent.y, -tangent.x) * (c % 2 == 0 ? 1f : -1f);
					Vector2 from = p + n * (MainWidth * 0.5f), to = p + n * behind;
					if (InsidePoint(to, 2f) && !InPlaza(from) && !InPlaza(to))
					{
						AddWay(new List<Vector2> { from, to }, LaneWidth, 2, 3, false);
					}
				}
			}

			private void RadialCity()
			{
				List<Entry> entries = Entries(true);
				float size = MeanInner;
				Vector2 centre = Pull(new Vector2(plan.Range(-0.06f, 0.06f), plan.Range(-0.06f, 0.06f)) * size, 0.2f);
				market = centre;
				float half = Mathf.Clamp(size * 0.15f, 12f, 30f);
				Vector2 lie = Dir(plan.NextFloat() * Mathf.PI);
				Vector2[] square = AddPlaza(centre, lie, half, half * plan.Range(0.7f, 0.95f), 0.85f);
				// The citadel against the wall, in the widest stretch between the gates' streets.
				Citadel(entries, 0.66f);
				foreach (Entry e in entries)
				{
					Vector2 to = Toward(square, centre, e.End, 1f);
					AddWay(Spline(e.Start, e.End, Bend(e.End, to, 0.06f), to), MainWidth, 0, 3, false);
				}
				// Ring streets following the wall: one half way out, and in a large city one near the wall.
				RingStreet(0.5f, SecondWidth, 1);
				if (size >= 80f)
				{
					RingStreet(0.8f, SecondWidth, 1);
				}
				// Lanes out from the inner ring between the gates' streets where the stretch is long.
				var bearings = new List<float>();
				foreach (Entry e in entries)
				{
					bearings.Add(e.Bearing);
				}
				bearings.Sort();
				for (int i = 0; i < bearings.Count; i++)
				{
					float a = bearings[i], b = i + 1 < bearings.Count ? bearings[i + 1] : bearings[0] + 360f;
					float gap = b - a;
					int lanes = Mathf.FloorToInt(gap * Mathf.Deg2Rad * size * 0.5f / 55f);
					for (int l = 1; l <= lanes; l++)
					{
						float bearing = a + gap * l / (lanes + 1f);
						Vector2 dir = Dir(bearing * Mathf.Deg2Rad);
						float inner = InnerEdge(dir);
						Vector2 from = dir * (inner * 0.5f), to = dir * (inner * (size >= 80f ? 0.8f : 0.92f));
						if (!NearCitadel(from) && !NearCitadel(to))
						{
							AddWay(new List<Vector2> { from, Vector2.Lerp(from, to, 0.5f) + new Vector2(dir.y, -dir.x) * plan.Range(-2f, 2f), to }, SecondWidth * 0.8f, 2, 3, false);
						}
					}
				}
			}

			private void HillTown()
			{
				List<Entry> entries = Entries(true);
				float size = MeanInner;
				int bands = steps.Length + 1;
				// The boundary's reach along the axis, to bound the lowest and highest bands.
				float uLow = float.PositiveInfinity, uHigh = float.NegativeInfinity;
				for (int k = 0; k < 64; k++)
				{
					Vector2 dir = Dir(k * Mathf.PI * 2f / 64);
					float u = U(dir * InnerEdge(dir));
					uLow = Mathf.Min(uLow, u);
					uHigh = Mathf.Max(uHigh, u);
				}
				var contour = new Way[bands];
				var middles = new float[bands];
				int middleBand = bands / 2;
				for (int band = 0; band < bands; band++)
				{
					float lo = band == 0 ? uLow : steps[band - 1] + Mathf.Max(bankHalf[band - 1], rampHalf[band - 1]);
					float hi = band == bands - 1 ? uHigh : steps[band] - Mathf.Max(bankHalf[band], rampHalf[band]);
					if (hi - lo < 4f)
					{
						continue;
					}
					// A terrace's street keeps to its middle, a little bowed, the houses either side down to the banks.
					float u = (lo + hi) * 0.5f;
					middles[band] = u;
					float bow = Mathf.Min(2.5f, (hi - lo) * 0.12f) * plan.Range(-1f, 1f);
					var points = new List<Vector2>();
					float reach = r + 4f;
					for (float v = -reach; v <= reach; v += 4f)
					{
						float t = v / reach;
						points.Add(axis * (u + bow * (1f - t * t)) + across * v);
					}
					List<Vector2> clipped = ClipInside(points, 3f);
					contour[band] = clipped != null ? AddWay(clipped, band == middleBand ? MainWidth : SecondWidth, band == middleBand ? 0 : 1, 3, false) : null;
				}
				// The lanes up the banks: each through its ramp, straight up the axis from one terrace's street to the next.
				for (int i = 0; i < steps.Length; i++)
				{
					if (contour[i] == null || contour[i + 1] == null)
					{
						continue;
					}
					Vector2 from = axis * middles[i] + across * ramps[i], to = axis * middles[i + 1] + across * ramps[i];
					AddWay(new List<Vector2> { from, axis * steps[i] + across * ramps[i], to }, Mathf.Min(SecondWidth, 2f * rampWidth - 1f), 1, 3, false);
				}
				// The market: on the middle terrace's street, nearest the middle.
				Way middle = contour[middleBand];
				if (middle != null)
				{
					(Vector2 at, Vector2 tangent) = middle.At(middle.Nearest(Vector2.zero));
					market = at;
					float lo = middleBand == 0 ? uLow : steps[middleBand - 1];
					float hi = middleBand == bands - 1 ? uHigh : steps[middleBand];
					float room = Mathf.Min(U(at) - lo, hi - U(at)) - 3f;
					AddPlaza(at, tangent, Mathf.Clamp(size * 0.18f, 9f, 16f), Mathf.Clamp(Mathf.Min(MainWidth * 0.5f + 5f, room), MainWidth * 0.5f + 1f, 14f), 0.8f);
				}
				// The roads in, held on the terrace they arrive at, turning to its street.
				foreach (Entry e in entries)
				{
					Way street = contour[Mathf.Clamp(BandOf(e.End), 0, bands - 1)];
					if (street == null)
					{
						continue;
					}
					Vector2 join = street.At(street.Nearest(e.End)).point;
					AddWay(new List<Vector2> { e.Start, e.End, join }, MainWidth, 0, 3, false);
				}
				// The keep (or a great house) on the top terrace.
				if (contour[bands - 1] != null)
				{
					Way top = contour[bands - 1];
					Vector2 spot = top.At(top.Nearest(Vector2.zero)).point + axis * (SecondWidth * 0.5f + 8f);
					PlaceCitadel(spot);
				}
			}

			private void Port()
			{
				List<Entry> entries = Entries(true);
				float size = MeanInner;
				Vector2 ahead = Vector2.up;
				float quay = Mathf.Min(request.ShoreAhead - 3f - MainWidth * 0.5f, InnerEdge(ahead) * 0.92f - MainWidth * 0.5f);
				var line = new List<Vector2>();
				for (float x = -r - 4f; x <= r + 4f; x += 4f)
				{
					line.Add(new Vector2(x, quay));
				}
				List<Vector2> clipped = ClipInside(line, 2f);
				Way front = clipped != null ? AddWay(clipped, MainWidth, 0, 3, false) : null;
				if (front == null)
				{
					MarketTown();
					return;
				}
				// Houses front the quay from the land (it runs along +x, so its +side looks −y, inland); its seaward side is the warehouses' (scatter).
				front.Sides = 1;
				(Vector2 head, Vector2 tangent) = front.At(front.Nearest(new Vector2(0f, quay)));
				market = head + Vector2.down * (MainWidth * 0.5f + 4f);
				AddPlaza(market, tangent, Mathf.Clamp(size * 0.2f, 9f, 16f), 5f, 0.9f);
				foreach (Entry e in entries)
				{
					Vector2 join = front.At(front.Nearest(e.End)).point;
					// No street comes in from the sea's side (a made-up entrance ahead would run out along the piers).
					if (Vector2.Distance(join, e.End) < 4f || (request.Entrances.Count == 0 && e.Dir.y > 0.3f))
					{
						continue;
					}
					AddWay(Spline(e.Start, e.End, Bend(e.End, join, 0.1f), join), MainWidth, 0, 3, false);
				}
				// Streets behind the quay's houses, each a block back from the last, as a harbour town climbs from its waterfront,
				// and alleys down through the blocks to the quay.
				// A block is two rows of houses back to back: the quay's facing it, the next street's facing away.
				float block = Gap + HouseDepth + 1f;
				float back = quay - (MainWidth * 0.5f + 2f * block + 1f + SecondWidth * 0.5f);
				float inland = InnerEdge(Vector2.down);
				for (int row = 0; back > -inland * 0.85f && row < 6; row++)
				{
					var backLine = new List<Vector2>();
					float bow = plan.Range(-2f, 2f);
					for (float x = -r - 4f; x <= r + 4f; x += 4f)
					{
						float t = x / (r + 4f);
						backLine.Add(new Vector2(x, back + bow * (1f - t * t)));
					}
					List<Vector2> backClipped = ClipInside(backLine, 2f);
					if (backClipped != null)
					{
						AddWay(backClipped, SecondWidth, 1, 3, false);
					}
					back -= SecondWidth + 2f * block + 2f;
				}
				// The alleys a block's width and more out either side of the middle, in a port large enough to want them.
				for (int side = -1; side <= 1 && size >= 55f; side += 2)
				{
					float x = side * Mathf.Max(plan.Range(0.32f, 0.45f) * size, 2f * block + MainWidth);
					var lane = new List<Vector2> { new Vector2(x, quay - MainWidth * 0.5f), new Vector2(x + plan.Range(-3f, 3f), -inland * 0.85f) };
					List<Vector2> inside = ClipInside(lane, 2f);
					if (inside != null)
					{
						AddWay(inside, LaneWidth, 2, 3, false);
					}
				}
			}

			private void Bailey()
			{
				List<Entry> entries = Entries(true);
				// The keep in the middle, or on the top terrace of a terraced bailey.
				Vector2 spot = Vector2.zero;
				if (terraced)
				{
					float top = float.NegativeInfinity;
					for (int k = 0; k < 64; k++)
					{
						Vector2 dir = Dir(k * Mathf.PI * 2f / 64);
						top = Mathf.Max(top, U(dir * InnerEdge(dir)));
					}
					float from = steps[steps.Length - 1] + Mathf.Max(bankHalf[steps.Length - 1], rampHalf[steps.Length - 1]);
					spot = axis * (0.5f * (from + top));
				}
				market = spot;
				PlaceCitadel(spot);
				// The bailey's buildings backed onto the curtain all round, facing the ward (the line they back onto is no street).
				var ring = new List<Vector2>();
				for (int k = 0; k < 120; k++)
				{
					Vector2 dir = Dir(k * Mathf.PI * 2f / 120);
					ring.Add(dir * Mathf.Max(0f, InnerEdge(dir) - 0.5f));
				}
				ring.Add(ring[0]);
				Way curtain = AddWay(ring, 0f, 1, 3, false, false);
				if (curtain != null)
				{
					curtain.Backed = true;
					OutwardOnly(curtain, Vector2.zero);
				}
				// The ways in from the gates across the ward to the keep's door.
				foreach (Entry e in entries)
				{
					Vector2 to = citadel.HasValue ? citadel.Value + (e.Start - citadel.Value).normalized * (CitadelReach() + 2f) : e.End;
					if (Vector2.Distance(e.Start, to) > 4f)
					{
						AddWay(new List<Vector2> { e.Start, e.End, to }, LaneWidth + 1f, 0, 3, false);
					}
				}
			}

			// ── The plan's pieces ───────────────────────────────────

			/// <summary>A point drawn in toward the centre until it lies within <paramref name="share"/> of the inner boundary.</summary>
			private Vector2 Pull(Vector2 p, float share)
			{
				float limit = InnerEdge(p) * share;
				return p.magnitude > limit ? p.normalized * limit : p;
			}

			/// <summary>A control point bowing the line from a to b to one side by a share of its length.</summary>
			private Vector2 Bend(Vector2 a, Vector2 b, float share)
			{
				Vector2 d = b - a;
				return (a + b) * 0.5f + new Vector2(d.y, -d.x) * plan.Range(-share, share);
			}

			/// <summary>A Catmull-Rom curve through the points, sampled about every three metres.</summary>
			private static List<Vector2> Spline(params Vector2[] through)
			{
				var points = new List<Vector2>();
				var clean = new List<Vector2>();
				foreach (Vector2 p in through)
				{
					if (clean.Count == 0 || Vector2.Distance(clean[clean.Count - 1], p) > 0.5f)
					{
						clean.Add(p);
					}
				}
				if (clean.Count < 2)
				{
					return clean;
				}
				for (int i = 0; i + 1 < clean.Count; i++)
				{
					Vector2 p0 = clean[Math.Max(0, i - 1)], p1 = clean[i], p2 = clean[i + 1], p3 = clean[Math.Min(clean.Count - 1, i + 2)];
					int n = Math.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 3f));
					for (int k = 0; k < n; k++)
					{
						float t = k / (float)n, t2 = t * t, t3 = t2 * t;
						points.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
					}
				}
				points.Add(clean[clean.Count - 1]);
				return points;
			}

			/// <summary>Chaikin's corner cutting: a ring (or line) rounded off once.</summary>
			private static List<Vector2> Chaikin(List<Vector2> points, bool closed)
			{
				var cut = new List<Vector2>();
				int n = points.Count, last = closed ? n : n - 1;
				for (int i = 0; i < last; i++)
				{
					Vector2 a = points[i], b = points[(i + 1) % n];
					cut.Add(Vector2.Lerp(a, b, 0.25f));
					cut.Add(Vector2.Lerp(a, b, 0.75f));
				}
				return cut;
			}

			/// <summary>The longest run of points inside the inner boundary by <paramref name="margin"/>; null when shorter than 8 m.</summary>
			private List<Vector2> ClipInside(List<Vector2> points, float margin)
			{
				List<Vector2> best = null, run = null;
				float bestLength = 0f, length = 0f;
				foreach (Vector2 p in points)
				{
					if (InsidePoint(p, margin))
					{
						if (run == null)
						{
							run = new List<Vector2>();
							length = 0f;
						}
						if (run.Count > 0)
						{
							length += Vector2.Distance(run[run.Count - 1], p);
						}
						run.Add(p);
						if (length > bestLength)
						{
							bestLength = length;
							best = run;
						}
					}
					else
					{
						run = null;
					}
				}
				return best != null && bestLength >= 8f ? best : null;
			}

			/// <summary>
			/// A plaza kept open (a green, a market place, a square): a convex quadrilateral round <paramref name="centre"/> along
			/// <paramref name="lie"/>, <paramref name="halfLength"/> each way, <paramref name="halfWidth"/> across at its wide end
			/// and <paramref name="taper"/> of that at its narrow one; houses front it from outside.
			/// </summary>
			private Vector2[] AddPlaza(Vector2 centre, Vector2 lie, float halfLength, float halfWidth, float taper)
			{
				lie = lie.sqrMagnitude > 1e-6f ? lie.normalized : Vector2.up;
				Vector2 side = new Vector2(lie.y, -lie.x);
				float j() => plan.Range(0.9f, 1.1f);
				var polygon = new[]
				{
					centre + lie * (halfLength * j()) + side * (halfWidth * taper * j()),
					centre + lie * (halfLength * j()) - side * (halfWidth * taper * j()),
					centre - lie * (halfLength * j()) - side * (halfWidth * j()),
					centre - lie * (halfLength * j()) + side * (halfWidth * j()),
				};
				// Drawn in where it would reach past the boundary.
				for (int i = 0; i < polygon.Length; i++)
				{
					polygon[i] = Pull(polygon[i], 0.85f);
				}
				areas.Add((polygon, PlazaGroup));
				request.Plazas.Add(polygon);
				// Its edge is a frontage: market houses and a church face the plaza from outside.
				var edgeWay = new Way { Width = 0f, Rank = 0 };
				foreach (Vector2 p in polygon)
				{
					edgeWay.P.Add(p);
				}
				edgeWay.P.Add(polygon[0]);
				edgeWay.Measure();
				OutwardOnly(edgeWay, centre);
				plazaFronts.Add(edgeWay);
				return polygon;
			}

			private readonly List<Way> plazaFronts = new List<Way>();

			private bool InPlaza(Vector2 p)
			{
				foreach ((Vector2[] polygon, int group) in areas)
				{
					if (group == PlazaGroup && Box.Of(p, 0f, Vector2.one * 0.5f, 0f, 0).Overlaps(polygon))
					{
						return true;
					}
				}
				return false;
			}

			/// <summary>Where the line from a plaza's centre toward <paramref name="from"/> leaves the plaza, drawn <paramref name="inside"/> metres back in.</summary>
			private static Vector2 Toward(Vector2[] polygon, Vector2 centre, Vector2 from, float inside)
			{
				Vector2 d = from - centre;
				if (d.sqrMagnitude < 1e-6f)
				{
					return centre;
				}
				float best = 0f;
				for (int i = 0; i < polygon.Length; i++)
				{
					Vector2 a = polygon[i] - centre, b = polygon[(i + 1) % polygon.Length] - centre, e = b - a;
					float den = d.x * e.y - d.y * e.x;
					if (Mathf.Abs(den) < 1e-6f)
					{
						continue;
					}
					float t = (a.x * e.y - a.y * e.x) / den, s = (a.x * d.y - a.y * d.x) / den;
					if (t > 0f && s >= 0f && s <= 1f)
					{
						best = Mathf.Max(best, t);
					}
				}
				float length = d.magnitude;
				return centre + d * Mathf.Max(0f, best - inside / length);
			}

			/// <summary>A way's houses only on the side away from <paramref name="centre"/> (round a green, inside a wall).</summary>
			private static void OutwardOnly(Way way, Vector2 centre)
			{
				int plus = 0, minus = 0;
				for (int i = 1; i < way.P.Count; i++)
				{
					Vector2 d = way.P[i] - way.P[i - 1], mid = (way.P[i] + way.P[i - 1]) * 0.5f;
					if (Vector2.Dot(new Vector2(d.y, -d.x), mid - centre) > 0f)
					{
						plus++;
					}
					else
					{
						minus++;
					}
				}
				way.Sides = plus >= minus ? 1 : 2;
			}

			/// <summary>A road's street from its gate in to where it ends, then on to the nearest point of <paramref name="onto"/>.</summary>
			private void JoinEntry(Entry e, Way onto, float width, int rank, bool plots)
			{
				Vector2 join = onto.At(onto.Nearest(e.End)).point;
				if (Vector2.Distance(join, e.End) < 3f)
				{
					AddWay(new List<Vector2> { e.Start, join }, width, rank, 3, plots);
					return;
				}
				AddWay(Spline(e.Start, e.End, Bend(e.End, join, 0.08f), join), width, rank, 3, plots);
			}

			/// <summary>Lanes either side of a street at <paramref name="behind"/> metres, inside the boundary; the lanes laid.</summary>
			private List<Way> BackLanes(Way street, float behind)
			{
				var laid = new List<Way>();
				for (int side = -1; side <= 1; side += 2)
				{
					var points = new List<Vector2>();
					for (float t = 0f; t <= street.Length; t += 4f)
					{
						(Vector2 p, Vector2 tangent) = street.At(t);
						Vector2 q = p + new Vector2(tangent.y, -tangent.x) * (side * behind);
						if (!InPlaza(q))
						{
							points.Add(q);
						}
					}
					List<Vector2> clipped = ClipInside(points, 2f);
					if (clipped == null || PolyLength(clipped) < 24f)
					{
						continue;
					}
					Way lane = AddWay(clipped, LaneWidth, 2, 3, false);
					if (lane != null)
					{
						laid.Add(lane);
					}
				}
				return laid;
			}

			private static float PolyLength(List<Vector2> points)
			{
				float length = 0f;
				for (int i = 1; i < points.Count; i++)
				{
					length += Vector2.Distance(points[i - 1], points[i]);
				}
				return length;
			}

			/// <summary>A ring street at a share of the inner boundary all round, broken round the citadel.</summary>
			private void RingStreet(float share, float width, int rank)
			{
				var ring = new List<Vector2>();
				for (int k = 0; k < 180; k++)
				{
					Vector2 dir = Dir(k * Mathf.PI * 2f / 180);
					ring.Add(dir * (InnerEdge(dir) * share));
				}
				foreach (List<Vector2> run in CutAroundCitadel(ring, true))
				{
					if (PolyLength(run) >= 16f)
					{
						AddWay(run, width, rank, 3, false);
					}
				}
			}

			/// <summary>A ring's runs with the stretch over the citadel (and its approach) cut out; the whole ring, closed, with none.</summary>
			private List<List<Vector2>> CutAroundCitadel(List<Vector2> ring, bool closed)
			{
				var runs = new List<List<Vector2>>();
				if (!citadel.HasValue)
				{
					var whole = new List<Vector2>(ring);
					if (closed)
					{
						whole.Add(ring[0]);
					}
					runs.Add(whole);
					return runs;
				}
				int start = ring.FindIndex(NearCitadel);
				if (start < 0)
				{
					var whole = new List<Vector2>(ring);
					if (closed)
					{
						whole.Add(ring[0]);
					}
					runs.Add(whole);
					return runs;
				}
				List<Vector2> run = null;
				for (int k = 1; k <= ring.Count; k++)
				{
					Vector2 p = ring[(start + k) % ring.Count];
					if (NearCitadel(p))
					{
						if (run != null && run.Count >= 2)
						{
							runs.Add(run);
						}
						run = null;
						continue;
					}
					run ??= new List<Vector2>();
					run.Add(p);
				}
				if (run != null && run.Count >= 2)
				{
					runs.Add(run);
				}
				return runs;
			}

			private float CitadelReach()
			{
				int index = CitadelItem();
				return index >= 0 ? request.Items[index].Footprint.magnitude * 0.5f : 0f;
			}

			private bool NearCitadel(Vector2 p) => citadel.HasValue && Vector2.Distance(p, citadel.Value) < CitadelReach() + SecondWidth + Gap + 2f;

			/// <summary>The first centrepiece when it is a keep's size (a footprint ten metres or more across): a citadel, not a well.</summary>
			private int CitadelItem()
			{
				for (int i = 0; i < request.Items.Count; i++)
				{
					if (Of(request.Items[i]) == PointOfInterestSlotRole.Centre)
					{
						Vector2 f = request.Items[i].Footprint;
						return Mathf.Max(f.x, f.y) >= 10f ? i : -1;
					}
				}
				return -1;
			}

			/// <summary>A city's citadel: against the wall in the widest stretch between its gates' streets, <paramref name="share"/> of the way out.</summary>
			private void Citadel(List<Entry> entries, float share)
			{
				if (CitadelItem() < 0)
				{
					return;
				}
				var bearings = new List<float>();
				foreach (Entry e in entries)
				{
					bearings.Add(e.Bearing);
				}
				bearings.Sort();
				float widest = -1f, at = 180f;
				for (int i = 0; i < bearings.Count; i++)
				{
					float a = bearings[i], b = i + 1 < bearings.Count ? bearings[i + 1] : bearings[0] + 360f;
					if (b - a > widest)
					{
						widest = b - a;
						at = a + (b - a) * 0.5f;
					}
				}
				Vector2 dir = Dir(at * Mathf.Deg2Rad);
				float reach = CitadelReach();
				PlaceCitadel(dir * Mathf.Max(0f, Mathf.Min(InnerEdge(dir) * share, InnerEdge(dir) - reach - 2f)));
			}

			/// <summary>Lays the citadel's keep at a spot (or a little in from it) before the streets, which then go round it.</summary>
			private void PlaceCitadel(Vector2 spot)
			{
				int index = CitadelItem();
				if (index < 0)
				{
					return;
				}
				PointOfInterestLayoutItem item = request.Items[index];
				for (int attempt = 0; attempt < 6; attempt++)
				{
					Vector2 at = spot * (1f - attempt * 0.12f);
					float facing = at.sqrMagnitude > 1f ? FacingYaw(DirectionOf(-at)) : 180f;
					if (TryLay(item.Slot, index, at, facing, item.Footprint, at == Vector2.zero ? Bound.None : Bound.Interior))
					{
						citadel = at;
						return;
					}
				}
			}

			/// <summary>
			/// Lays a street: its line kept clear (a box along every stretch and one at every bend) and written to the request.
			/// Null when it is too short to be one.
			/// </summary>
			private Way AddWay(List<Vector2> points, float width, int rank, int sides, bool plots, bool street = true)
			{
				if (points == null || points.Count < 2)
				{
					return null;
				}
				var way = new Way { Width = width, Rank = rank, Sides = sides, Plots = plots };
				foreach (Vector2 p in points)
				{
					if (way.P.Count == 0 || Vector2.Distance(way.P[way.P.Count - 1], p) > 0.2f)
					{
						way.P.Add(p);
					}
				}
				if (way.P.Count < 2)
				{
					return null;
				}
				way.Measure();
				if (way.Length < 4f)
				{
					return null;
				}
				if (!street)
				{
					// A frontage only (a line buildings back onto): kept with the ways, neither kept clear nor paved.
					ways.Add(way);
					return way;
				}
				for (int i = 1; i < way.P.Count; i++)
				{
					Vector2 a = way.P[i - 1], b = way.P[i];
					Add(Box.Of((a + b) * 0.5f, DirectionOf(b - a), new Vector2(width, Vector2.Distance(a, b) + width * 0.3f), 0f, StreetGroup));
					Add(Box.Of(b, DirectionOf(b - a), new Vector2(width, width), 0f, StreetGroup));
				}
				ways.Add(way);
				request.Streets.Add(new PointOfInterestLayoutStreet { Points = way.P.ToArray(), Width = width, Rank = (byte)rank });
				return way;
			}

			// ── Middle, lines, rings, fronts, rows, scatter ───────

			/// <summary>The first middle piece at the market facing ahead (a citadel's keep is laid already); the rest spiralled out round the market, facing in.</summary>
			private void Centre()
			{
				bool first = true;
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Centre)
					{
						continue;
					}
					float turn = random.NextFloat() * 360f;
					if (laidItems.Contains(i))
					{
						first = false;
						continue;
					}
					if (first)
					{
						// The centrepiece always stands, a lighthouse wider than its little footprint included.
						first = false;
						if (TryLay(item.Slot, i, market, 180f, item.Footprint, market == Vector2.zero ? Bound.None : Bound.Interior, 0, Allow.Streets | Allow.Plaza))
						{
							continue;
						}
					}
					Spiral(item, i, turn);
				}
			}

			private void Spiral(PointOfInterestLayoutItem item, int index, float turn)
			{
				float most = MostInner + market.magnitude;
				for (float rho = 1f; rho <= most; rho += 1f)
				{
					int count = Math.Max(6, Mathf.CeilToInt(2f * Mathf.PI * rho / 1.5f));
					for (int k = 0; k < count; k++)
					{
						float a = (turn + k * 360f / count) * Mathf.Deg2Rad;
						Vector2 at = market + Dir(a) * rho;
						if (TryLay(item.Slot, index, at, FacingYaw(DirectionOf(market - at)), item.Footprint, Bound.Interior, 0, Allow.Plaza))
						{
							return;
						}
					}
				}
			}

			/// <summary>End to end along the heading, centred.</summary>
			private void Line()
			{
				float total = 0f;
				foreach (PointOfInterestLayoutItem item in request.Items)
				{
					if (Of(item) == PointOfInterestSlotRole.Line)
					{
						total += item.Footprint.y + Gap;
					}
				}
				float z = -total * 0.5f;
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Line)
					{
						continue;
					}
					float pitch = item.Footprint.y + Gap;
					TryLay(item.Slot, i, new Vector2(0f, z + pitch * 0.5f), 0f, item.Footprint, Bound.Interior);
					z += pitch;
				}
			}

			/// <summary>One slot's pieces evenly round a ring outside what is already in the middle, facing in; drawn in where the boundary is near.</summary>
			private void Ring(int slot)
			{
				var indices = new List<int>();
				float widths = 0f, deepest = 0f, widest = 0f;
				for (int i = 0; i < request.Items.Count; i++)
				{
					if (request.Items[i].Slot == slot)
					{
						indices.Add(i);
						widths += request.Items[i].Footprint.x + Gap;
						deepest = Mathf.Max(deepest, request.Items[i].Footprint.y);
						widest = Mathf.Max(widest, request.Items[i].Footprint.magnitude * 0.5f);
					}
				}
				if (indices.Count == 0)
				{
					return;
				}
				float middle = 0f;
				foreach (Box box in boxes)
				{
					if (box.Group == 0)
					{
						middle = Mathf.Max(middle, box.C.magnitude + box.Bound);
					}
				}
				float interior = MeanInner;
				float rho = Mathf.Max(middle > 0f ? middle + deepest * 0.5f + Gap : 0f, widths / (2f * Mathf.PI), interior * 0.6f);
				rho = Mathf.Min(rho, Mathf.Max(0f, interior - widest));
				if (rho <= 0f)
				{
					rho = 0.01f;
				}
				float slack = Mathf.Max(0f, Mathf.PI * 2f - widths / rho) / indices.Count;
				float a = random.NextFloat() * Mathf.PI * 2f;
				foreach (int i in indices)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					float arc = (item.Footprint.x + Gap) / rho;
					Vector2 dir = Dir(a + arc * 0.5f);
					float here = Mathf.Min(rho, Mathf.Max(0.01f, InnerEdge(dir) - widest));
					Vector2 at = dir * here;
					TryLay(item.Slot, i, at, FacingYaw(DirectionOf(-at)), item.Footprint, Bound.Interior);
					a += arc + slack;
				}
			}

			/// <summary>
			/// Street fronts: the church and temple first, each house where the nearest free frontage to the market has room, its
			/// front square to its street there (the street's own bend), set back a little more or less, turned a hair, a few
			/// paces between neighbours; on a market town's or village's streets a plot kept behind it.
			/// </summary>
			private void Fronts()
			{
				var frontages = new List<Front>();
				foreach (Way way in plazaFronts)
				{
					AddFronts(frontages, way);
				}
				foreach (Way way in ways)
				{
					AddFronts(frontages, way);
				}
				if (frontages.Count == 0)
				{
					return;
				}
				var order = new List<int>();
				for (int i = 0; i < request.Items.Count; i++)
				{
					if (Of(request.Items[i]) == PointOfInterestSlotRole.Street && !laidItems.Contains(i))
					{
						order.Add(i);
					}
				}
				// The landmarks before the houses, so they take the plaza's best frontage; then the largest first (a hall needs the
				// deep frontages, the cottages fill what is left between), ties in the slots' order.
				order.Sort((p, q) =>
				{
					bool lp = IsLandmark(p), lq = IsLandmark(q);
					if (lp != lq)
					{
						return lp ? -1 : 1;
					}
					Vector2 fp = request.Items[p].Footprint, fq = request.Items[q].Footprint;
					int c = (fq.x * fq.y).CompareTo(fp.x * fp.y);
					return c != 0 ? c : p.CompareTo(q);
				});
				float[] penalty = { 0f, 14f, 28f };
				foreach (int i in order)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					Vector2 size = item.Footprint;
					bool landmark = IsLandmark(i);
					float setback = plan.Range(0f, 0.7f), turn = plan.Range(-3f, 3f), spacing = plan.Range(0f, 1.2f);
					Front best = null;
					float bestScore = float.PositiveInfinity, bestT = 0f, bestYaw = 0f;
					Vector2 bestAt = default, bestNormal = default;
					float bestAlong = size.x;
					foreach (Front f in frontages)
					{
						if (f.Closed)
						{
							continue;
						}
						Way way = f.Way;
						// A long building backed onto a curtain may stand with its long side to it, its end to the ward.
						int turns = way.Backed && size.y > size.x * 1.4f ? 2 : 1;
						for (int quarter = 0; quarter < turns; quarter++)
						{
							float along = quarter == 0 ? size.x : size.y, deep = quarter == 0 ? size.y : size.x;
							// Along the way past what stands in it (a cross street, a plaza, a gate's street), as far as it runs.
							float reach = Mathf.Min(way.Length, way.Backed ? way.Length : 120f);
							for (float scanned = 0f; scanned <= reach; scanned += 0.75f)
							{
								float t = f.Cursor + f.Direction * scanned;
								float middle = t + f.Direction * along * 0.5f;
								if (middle - along * 0.5f < 0f || middle + along * 0.5f > way.Length)
								{
									if (scanned == 0f && quarter == turns - 1)
									{
										f.Closed = true;
									}
									break;
								}
								(Vector2 p, Vector2 tangent) = way.At(middle);
								Vector2 normal = new Vector2(tangent.y, -tangent.x) * (f.Side == 1 ? 1f : -1f);
								float set = way.Width * 0.5f + Gap + setback + deep * 0.5f;
								// Backed onto the line (its fronted side looks outward): the house stands inside it, its back to it, facing in.
								Vector2 at = way.Backed ? p - normal * (deep * 0.5f + 0.3f) : p + normal * set;
								float score = Vector2.Distance(at, market) + penalty[Mathf.Clamp(way.Rank, 0, 2)] + quarter * 6f;
								if (landmark)
								{
									// A church faces the plaza if it can, else stands as near the market as there is room; on terraces, the higher the better.
									score += plazaFronts.Contains(way) ? -1000f : 0f;
									score -= terraced ? 2f * U(at) : 0f;
								}
								if (score >= bestScore)
								{
									break;
								}
								float yaw = FacingYaw(DirectionOf(-normal)) + turn + quarter * 90f;
								if (way.Backed)
								{
									// A straight building's ends stand out past a curved curtain: drawn in until its corners are inside it.
									for (int pull = 0; pull < 4; pull++)
									{
										float over = Overshoot(at, yaw, size);
										if (over <= 0f)
										{
											break;
										}
										at -= normal * (over + 0.05f);
									}
								}
								if (Inside(at, yaw, size, Bound.Interior) && Fits(Box.Of(at, yaw, size, Gap, 0)))
								{
									best = f;
									bestScore = score;
									bestT = t;
									bestAt = at;
									bestYaw = yaw;
									bestNormal = normal;
									bestAlong = along;
									break;
								}
							}
						}
					}
					if (best == null)
					{
						continue;
					}
					best.Cursor = bestT + best.Direction * (bestAlong + Gap + spacing);
					Lay(item.Slot, i, bestAt, bestYaw, size, Box.Of(bestAt, bestYaw, size, Gap, 0));
					if (best.Way.Plots)
					{
						Plot(bestAt, bestNormal, bestYaw, size);
					}
				}
			}

			/// <summary>How far the footprint's farthest corner stands past the inner boundary, metres (0 inside).</summary>
			private float Overshoot(Vector2 centre, float yaw, Vector2 size)
			{
				var four = new Vector2[4];
				Corners(centre, yaw, size, four);
				float over = 0f;
				foreach (Vector2 c in four)
				{
					over = Mathf.Max(over, c.magnitude - InnerEdge(c));
				}
				return over;
			}

			private bool IsLandmark(int item)
			{
				int slot = request.Items[item].Slot;
				return slot >= 0 && slot < request.Slots.Count && request.Slots[slot].Landmark;
			}

			/// <summary>Both directions along each side a way is fronted from, from its point nearest the market.</summary>
			private void AddFronts(List<Front> frontages, Way way)
			{
				float start = way.Nearest(market);
				for (int side = 1; side <= 2; side++)
				{
					if ((way.Sides & side) == 0)
					{
						continue;
					}
					frontages.Add(new Front { Way = way, Side = side, Direction = 1, Cursor = start });
					frontages.Add(new Front { Way = way, Side = side, Direction = -1, Cursor = start });
				}
			}

			/// <summary>The plot behind a house (a burgage plot, a croft): kept from other houses, open to the back yard's sheds.</summary>
			private void Plot(Vector2 house, Vector2 normal, float yaw, Vector2 size)
			{
				float deep = Mathf.Clamp(MeanInner * 0.2f, 6f, 14f) * plan.Range(0.8f, 1.2f);
				foreach (float share in new[] { 1f, 0.6f, 0.3f })
				{
					float d = deep * share;
					Vector2 at = house + normal * (size.y * 0.5f + Gap * 0.5f + d * 0.5f);
					var plot = Box.Of(at, yaw, new Vector2(size.x, d), 0f, PlotGroup);
					if (Inside(at, yaw, new Vector2(size.x, d), Bound.Interior) && Fits(plot))
					{
						Add(plot);
						return;
					}
				}
			}

			/// <summary>Rows square to the heading, all facing ahead, filled from the middle out.</summary>
			private void Rows()
			{
				Vector2 largest = Vector2.zero;
				bool any = false;
				foreach (PointOfInterestLayoutItem item in request.Items)
				{
					if (Of(item) == PointOfInterestSlotRole.Rows)
					{
						largest = Vector2.Max(largest, item.Footprint);
						any = true;
					}
				}
				if (!any)
				{
					return;
				}
				float interior = MostInner;
				float px = largest.x + Gap, pz = largest.y + Gap + RowAisleMetres;
				int kx = Mathf.CeilToInt(interior / px), kz = Mathf.CeilToInt(interior / pz);
				var lattice = new List<Vector2>();
				for (int j = -kz; j <= kz; j++)
				{
					for (int i = -kx; i <= kx; i++)
					{
						var at = new Vector2(i * px, j * pz);
						if (Inside(at, 180f, largest, Bound.Interior))
						{
							lattice.Add(at);
						}
					}
				}
				lattice.Sort((p, q) =>
				{
					int c = p.sqrMagnitude.CompareTo(q.sqrMagnitude);
					if (c != 0) return c;
					c = p.y.CompareTo(q.y);
					return c != 0 ? c : p.x.CompareTo(q.x);
				});
				var used = new bool[lattice.Count];
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Rows)
					{
						continue;
					}
					for (int k = 0; k < lattice.Count; k++)
					{
						if (!used[k] && TryLay(item.Slot, i, lattice[k], 180f, item.Footprint, Bound.Interior))
						{
							used[k] = true;
							break;
						}
					}
				}
			}

			/// <summary>Dart throwing: each piece at the first of 48 throws that is clear, any way round; a back yard's plot will do.</summary>
			private void Scatter()
			{
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Scatter)
					{
						continue;
					}
					for (int attempt = 0; attempt < 48; attempt++)
					{
						float a = random.NextFloat() * Mathf.PI * 2f;
						float d = Mathf.Sqrt(random.NextFloat());
						float yaw = random.NextFloat() * 360f;
						Vector2 dir = Dir(a);
						float reach = Mathf.Max(0f, InnerEdge(dir) - item.Footprint.magnitude * 0.5f);
						if (TryLay(item.Slot, i, dir * (d * reach), yaw, item.Footprint, Bound.Interior, 0, Allow.Plots))
						{
							break;
						}
					}
				}
			}
		}

		/// <summary>How far inside a moat's inner bank a wall's footing keeps, metres: the berm it stands on.</summary>
		public const float MoatClearMetres = 1.5f;
	}
}
#endif
