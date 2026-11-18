#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One run of a yard's fence, in the site's frame: from <see cref="A"/> to <see cref="B"/>, the yard on its left.</summary>
	public struct PointOfInterestYardFence
	{
		public Vector2 A, B;
		/// <summary>The run its gate stands in: the front's (to the street), or the back's for a yard behind a shop.</summary>
		public bool Gate;
	}

	/// <summary>
	/// A house's fenced yard the layout laid (2026-10-19: "some houses should have fences around them with gates to the roads"): the
	/// house as laid (set back behind its front garden), and the fence's runs round its garden and the plot behind it.
	/// </summary>
	public sealed class PointOfInterestLayoutYard
	{
		/// <summary>The house's index in the pieces the layout returned.</summary>
		public int Piece;
		/// <summary>The request item it lays.</summary>
		public int Item;
		public PointOfInterestYardKind Kind;
		/// <summary>The house as laid: its centre (site frame), yaw (degrees, its front looking down its −z to the street) and footprint.</summary>
		public Vector2 Centre;
		public float Yaw;
		public Vector2 Footprint;
		/// <summary>How far the house stands back from where it would have stood without its garden, metres (0 for a yard behind).</summary>
		public float SetBack;
		/// <summary>Where the gate's middle goes on its run with no door to aim at: square out from the house's middle.</summary>
		public Vector2 GateAt;
		/// <summary>The fence's runs, round the yard with the yard on each run's left; a side shared with a neighbour's yard is not repeated.</summary>
		public readonly List<PointOfInterestYardFence> Fences = new List<PointOfInterestYardFence>();
	}

	public static partial class PointOfInterestLayouts
	{
		/// <summary>The front garden a fenced house keeps between its fence and its front wall, metres (its draw between the two).</summary>
		public const float GardenMinMetres = 2.5f, GardenMaxMetres = 4.5f;

		/// <summary>The least front garden and back yard a yard is laid with, metres: a house whose plot cannot give both keeps none.</summary>
		public const float LeastGardenMetres = 2f, LeastBackYardMetres = 2.5f;

		/// <summary>How far apart two neighbours' side fences may run and still be one shared fence, metres (the gap between their houses).</summary>
		public const float FenceShareMetres = 1.5f;

		/// <summary>The inner edge of the band an <see cref="PointOfInterestSlotRole.Edge"/> piece is laid in, as a share of the boundary's inner reach.</summary>
		public const float EdgeBandShare = 0.65f;

		/// <summary>
		/// The share of the houses that may keep a yard that do, by town plan (2026-10-19): a village's crofts half fenced (its green's a
		/// little more), a market town's burgage plots a third, a hill town's terraces and a port's or city's packed streets few (their
		/// streets keep no plot, so in practice none), a bailey's none.
		/// </summary>
		public static float YardShareOf(PointOfInterestTownPlan plan)
		{
			switch (plan)
			{
				case PointOfInterestTownPlan.StreetVillage: return 0.5f;
				case PointOfInterestTownPlan.GreenVillage: return 0.55f;
				case PointOfInterestTownPlan.MarketTown: return 0.35f;
				case PointOfInterestTownPlan.HillTown: return 0.25f;
				case PointOfInterestTownPlan.Port:
				case PointOfInterestTownPlan.RadialCity: return 0.15f;
				case PointOfInterestTownPlan.Bailey: return 0f;
				default: return 0.4f;
			}
		}

		private sealed partial class Packer
		{
			/// <summary>Each yard's fence boxes are a group of their own below this, so one yard's fences never stop at its own but do at another's.</summary>
			private const int YardFenceGroup = -100;

			/// <summary>A house the street fronts laid: its item, its piece, its box and its plot's box (−1 for none), whether it backs onto a curtain, where and how it stands.</summary>
			private struct Fronted
			{
				public int Item, Piece, HouseBox, PlotBox;
				public bool Backed;
				public Vector2 Centre;
				public float Yaw;
				public Vector2 Size;
			}

			private readonly List<Fronted> fronted = new List<Fronted>();

			/// <summary>The side fences the yards laid so far, for a neighbour's to share.</summary>
			private readonly List<(Vector2 A, Vector2 B)> yardSides = new List<(Vector2, Vector2)>();

			/// <summary>
			/// The fenced yards (2026-10-19), after every house is laid: a share of the houses whose slot may keep one (by the town plan, on
			/// the request's own hash stream; none backed onto a curtain) get one round the plot behind them (its burgage plot or croft,
			/// else the free ground there, claimed). A fronted yard sets the house back a few metres, the front garden fenced along the
			/// street with the gate before the door, the sides fenced back past the house round the plot and the plot's end; a side running
			/// alongside a neighbour's yard's is that fence, shared. Nothing a house was laid by moves: every house is laid before any yard
			/// is drawn, so a house with no yard stands exactly where it did, and a yard that would reach anything else (a street, another
			/// house, another plot or fence, the boundary) is not laid at all. What comes after (the edge, rows, scatter) fits round them.
			/// </summary>
			private void Yards()
			{
				if (fronted.Count == 0)
				{
					return;
				}
				float share = float.IsNaN(request.YardShare) ? YardShareOf(request.ChosenPlan) : Mathf.Clamp01(request.YardShare);
				if (share <= 0f)
				{
					return;
				}
				foreach (Fronted f in fronted)
				{
					int slot = request.Items[f.Item].Slot;
					PointOfInterestYardKind kind = slot >= 0 && slot < request.Slots.Count ? request.Slots[slot].Yard : PointOfInterestYardKind.None;
					if (kind == PointOfInterestYardKind.None || f.Backed)
					{
						continue;
					}
					if (PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, f.Item, 0x59, 0x4152)) >= share)
					{
						continue;
					}
					TryYard(f, kind, PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, f.Item, 0x59, 0x5342)));
				}
			}

			/// <summary>Lays one house's yard where everything about it fits; leaves the house as it stood where anything does not.</summary>
			private void TryYard(in Fronted f, PointOfInterestYardKind kind, float roll)
			{
				Vector2 ax = Rotate(Vector2.right, f.Yaw), az = Rotate(Vector2.up, f.Yaw);
				Vector2 c0 = f.Centre, size = f.Size;
				// The plot's far end along the house's own depth (+z, away from its street): its burgage plot or croft, else the free
				// ground behind it, claimed for the yard.
				Box plot;
				if (f.PlotBox >= 0)
				{
					plot = boxes[f.PlotBox];
				}
				else if (!FreePlot(f, PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, f.Item, 0x59, 0x504C)), out plot))
				{
					return;
				}
				float end = float.NegativeInfinity;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sz = -1; sz <= 1; sz += 2)
					{
						end = Mathf.Max(end, Vector2.Dot(plot.C + plot.Ax * (sx * plot.Half.x) + plot.Az * (sz * plot.Half.y) - c0, az));
					}
				}
				float back = end - 0.2f;
				// The sides a little out from the walls (no way between a wall and its fence), inside the gap the house kept.
				float side = size.x * 0.5f + Mathf.Max(0.3f, Gap * 0.5f - 0.05f);
				float front, setBack = 0f;
				if (kind == PointOfInterestYardKind.Fronted)
				{
					// The front fence just inside the ground the house kept from its street, the house set back behind its garden.
					front = -(size.y * 0.5f + Gap * 0.5f) + 0.15f;
					float least = LeastGardenMetres + front + size.y * 0.5f, most = back - size.y * 0.5f - LeastBackYardMetres;
					if (most < least)
					{
						return;
					}
					setBack = Mathf.Clamp(Mathf.Lerp(GardenMinMetres, GardenMaxMetres, roll) + front + size.y * 0.5f, least, most);
					var moved = Box.Of(c0 + az * setBack, f.Yaw, size, 0.2f, 0);
					if (!Inside(moved.C, f.Yaw, size, Bound.Interior) || !Fits(moved, Allow.None, f.HouseBox, f.PlotBox))
					{
						return;
					}
				}
				else
				{
					// Behind a shop: the sides from just short of its back corners, its front left on the street.
					front = size.y * 0.5f - 0.6f;
					if (back - size.y * 0.5f < LeastBackYardMetres)
					{
						return;
					}
				}
				Vector2 fl = c0 + ax * -side + az * front, fr = c0 + ax * side + az * front;
				Vector2 br = c0 + ax * side + az * back, bl = c0 + ax * -side + az * back;
				var runs = new List<PointOfInterestYardFence>();
				var sides = new List<(Vector2, Vector2)>();
				// The right side runs front to back, the left back to front: the yard on every run's left.
				Side(ref fr, ref br, ax, ax, false, runs, sides);
				Side(ref fl, ref bl, -ax, ax, true, runs, sides);
				if (kind == PointOfInterestYardKind.Behind)
				{
					// A side moved out onto a neighbour's line is closed back to the shop's wall, so the yard has no way in but its gate.
					Vector2 wallRight = c0 + ax * (size.x * 0.5f + 0.05f) + az * front, wallLeft = c0 - ax * (size.x * 0.5f + 0.05f) + az * front;
					if ((fr - wallRight).magnitude > 0.5f)
					{
						runs.Add(new PointOfInterestYardFence { A = wallRight, B = fr });
					}
					if ((fl - wallLeft).magnitude > 0.5f)
					{
						runs.Add(new PointOfInterestYardFence { A = fl, B = wallLeft });
					}
				}
				if (kind == PointOfInterestYardKind.Fronted)
				{
					runs.Add(new PointOfInterestYardFence { A = fl, B = fr, Gate = true });
				}
				runs.Add(new PointOfInterestYardFence { A = br, B = bl, Gate = kind == PointOfInterestYardKind.Behind });
				int group = YardFenceGroup - request.Yards.Count;
				foreach (PointOfInterestYardFence run in runs)
				{
					// Its core (a hand's breadth short of each end, where it meets its neighbours) clear of all but its own house and plot.
					Vector2 d = run.B - run.A;
					float length = d.magnitude;
					if (length < 0.5f)
					{
						continue;
					}
					if (!InsidePoint(run.A, 0f) || !InsidePoint(run.B, 0f)
						|| !Fits(Box.Of((run.A + run.B) * 0.5f, DirectionOf(d) - 90f, new Vector2(length - 0.4f, 0.2f), 0f, group), Allow.None, f.HouseBox, f.PlotBox))
					{
						return;
					}
				}
				// Everything fits: the house steps back, its fences are kept clear of what comes after.
				PointOfInterestLayoutPiece piece = Pieces[f.Piece];
				piece.Offset = c0 + az * setBack;
				Pieces[f.Piece] = piece;
				if (setBack > 0f)
				{
					Add(Box.Of(piece.Offset, f.Yaw, size, Gap, 0));
				}
				if (f.PlotBox < 0)
				{
					Add(plot);
				}
				var yard = new PointOfInterestLayoutYard
				{
					Piece = f.Piece,
					Item = f.Item,
					Kind = kind,
					Centre = piece.Offset,
					Yaw = piece.Yaw,
					Footprint = size,
					SetBack = setBack,
				};
				foreach (PointOfInterestYardFence run in runs)
				{
					Vector2 d = run.B - run.A;
					if (d.magnitude < 0.3f)
					{
						continue;
					}
					Add(Box.Of((run.A + run.B) * 0.5f, DirectionOf(d) - 90f, new Vector2(d.magnitude, 0.3f), 0f, group));
					yard.Fences.Add(run);
					if (run.Gate)
					{
						// Square out from the house's middle onto the run.
						float t = Mathf.Clamp01(Vector2.Dot(piece.Offset - run.A, d) / d.sqrMagnitude);
						yard.GateAt = run.A + d * t;
					}
				}
				yardSides.AddRange(sides);
				request.Yards.Add(yard);
			}

			/// <summary>
			/// The free ground behind a house whose street keeps no plot, as a plot (its depth drawn from 5 to 9 m, less where that does not
			/// fit): claimed only if its yard is laid, after every house, so it moves none.
			/// </summary>
			private bool FreePlot(in Fronted f, float roll, out Box plot)
			{
				Vector2 az = Rotate(Vector2.up, f.Yaw);
				float deep = Mathf.Lerp(5f, 9f, roll);
				foreach (float share in new[] { 1f, 0.75f, 0.55f })
				{
					float d = deep * share;
					Vector2 at = f.Centre + az * (f.Size.y * 0.5f + Gap * 0.5f + d * 0.5f);
					var size = new Vector2(f.Size.x, d);
					plot = Box.Of(at, f.Yaw, size, 0f, PlotGroup);
					if (Inside(at, f.Yaw, size, Bound.Interior) && Fits(plot, Allow.None, f.HouseBox))
					{
						return true;
					}
				}
				plot = default;
				return false;
			}

			/// <summary>
			/// One side of a yard from its front corner to its back one (<paramref name="outward"/> away from the house; the run laid back
			/// to front when <paramref name="reversed"/>): where a neighbour's side fence runs alongside within <see cref="FenceShareMetres"/>,
			/// the corners move onto that fence's line (<paramref name="across"/>: the front and back fences' direction) and only what it
			/// does not already cover is laid; else the side as it is.
			/// </summary>
			private void Side(ref Vector2 front, ref Vector2 back, Vector2 outward, Vector2 across, bool reversed,
				List<PointOfInterestYardFence> runs, List<(Vector2, Vector2)> sides)
			{
				Vector2 along = back - front;
				float length = along.magnitude;
				if (length < 1e-3f)
				{
					return;
				}
				along /= length;
				for (int e = 0; e < yardSides.Count; e++)
				{
					(Vector2 ea, Vector2 eb) = yardSides[e];
					Vector2 ed = eb - ea;
					if (ed.sqrMagnitude < 1e-4f || Mathf.Abs(along.x * ed.normalized.y - along.y * ed.normalized.x) > 0.14f)
					{
						continue;
					}
					float off = Vector2.Dot(ea - front, outward);
					float t0 = Vector2.Dot(ea - front, along), t1 = Vector2.Dot(eb - front, along);
					if (off < -0.3f || off > FenceShareMetres || Mathf.Min(length, Mathf.Max(t0, t1)) - Mathf.Max(0f, Mathf.Min(t0, t1)) < 1f)
					{
						continue;
					}
					// The front and back fences run on to the neighbour's line; this side lays only what that fence leaves.
					front = Meet(front, across, ea, ed);
					back = Meet(back, across, ea, ed);
					Vector2 line = back - front;
					float span = line.magnitude;
					if (span < 1e-3f)
					{
						return;
					}
					line /= span;
					float c0 = Mathf.Clamp(Mathf.Min(Vector2.Dot(ea - front, line), Vector2.Dot(eb - front, line)), 0f, span);
					float c1 = Mathf.Clamp(Mathf.Max(Vector2.Dot(ea - front, line), Vector2.Dot(eb - front, line)), 0f, span);
					if (c0 > 0.3f)
					{
						Own(front, front + line * c0, reversed, runs, sides);
					}
					if (span - c1 > 0.3f)
					{
						Own(front + line * c1, back, reversed, runs, sides);
					}
					return;
				}
				Own(front, back, reversed, runs, sides);
			}

			private static void Own(Vector2 from, Vector2 to, bool reversed, List<PointOfInterestYardFence> runs, List<(Vector2, Vector2)> sides)
			{
				runs.Add(reversed ? new PointOfInterestYardFence { A = to, B = from } : new PointOfInterestYardFence { A = from, B = to });
				sides.Add((from, to));
			}

			/// <summary>Where the line through <paramref name="p"/> along <paramref name="d"/> meets the line through <paramref name="q"/> along <paramref name="e"/> (p itself when they are parallel).</summary>
			private static Vector2 Meet(Vector2 p, Vector2 d, Vector2 q, Vector2 e)
			{
				float den = d.x * e.y - d.y * e.x;
				if (Mathf.Abs(den) < 1e-6f)
				{
					return p;
				}
				Vector2 w = q - p;
				return p + d * ((w.x * e.y - w.y * e.x) / den);
			}

			/// <summary>
			/// The edge pieces (2026-10-19, <see cref="PointOfInterestSlotRole.Edge"/>), the largest first: each in the band from
			/// <see cref="EdgeBandShare"/> of the boundary's inner reach out to it, its long side along the boundary where it fits so,
			/// else turned; on the bearings round from its own draw, as far out as it fits (a croft behind a house will do, as a barn or a
			/// kiln stands in one). With no room in the band, anywhere free as the scatter is thrown. Every draw is the request's own hash
			/// of the piece, never the scatter's sequence.
			/// </summary>
			private void Edge()
			{
				var order = new List<int>();
				for (int i = 0; i < request.Items.Count; i++)
				{
					if (Of(request.Items[i]) == PointOfInterestSlotRole.Edge && !laidItems.Contains(i))
					{
						order.Add(i);
					}
				}
				if (order.Count == 0)
				{
					return;
				}
				order.Sort((p, q) =>
				{
					Vector2 fp = request.Items[p].Footprint, fq = request.Items[q].Footprint;
					int c = (fq.x * fq.y).CompareTo(fp.x * fp.y);
					return c != 0 ? c : p.CompareTo(q);
				});
				foreach (int i in order)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (EdgeBand(i, item))
					{
						continue;
					}
					for (int attempt = 0; attempt < 48; attempt++)
					{
						float a = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, i, attempt, 0xED01)) * Mathf.PI * 2f;
						float d = Mathf.Sqrt(PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, i, attempt, 0xED02)));
						float yaw = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, i, attempt, 0xED03)) * 360f;
						Vector2 dir = Dir(a);
						float reach = Mathf.Max(0f, InnerEdge(dir) - item.Footprint.magnitude * 0.5f);
						if (TryLay(item.Slot, i, dir * (d * reach), yaw, item.Footprint, Bound.Interior, 0, Allow.Plots))
						{
							break;
						}
					}
				}
			}

			/// <summary>One edge piece in the band, its front to the middle: true when it was laid.</summary>
			private bool EdgeBand(int i, PointOfInterestLayoutItem item)
			{
				Vector2 size = item.Footprint;
				float start = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(request.Seed, i, 0, 0xED00)) * 360f;
				bool wide = size.x >= size.y;
				for (int k = 0; k < 72; k++)
				{
					// Alternately either side of its own bearing, so it stays near it.
					float bearing = start + ((k & 1) == 0 ? 1f : -1f) * ((k + 1) / 2) * 5f;
					Vector2 dir = Dir(bearing * Mathf.Deg2Rad);
					float inner = InnerEdge(dir);
					if (inner < 4f)
					{
						continue;
					}
					for (int q = 0; q < 2; q++)
					{
						// Its long side along the boundary first: a wide piece square to the bearing, a deep one turned a quarter.
						int quarter = wide ? q : 1 - q;
						float across = quarter == 0 ? size.y * 0.5f : size.x * 0.5f;
						float yaw = FacingYaw(DirectionOf(-dir)) + quarter * 90f;
						for (float rho = inner - across; rho >= inner * EdgeBandShare; rho -= 1f)
						{
							Vector2 at = dir * rho;
							for (int pull = 0; pull < 4; pull++)
							{
								float over = Overshoot(at, yaw, size);
								if (over <= 0f)
								{
									break;
								}
								at -= dir * (over + 0.05f);
							}
							if (at.magnitude < InnerEdge(at) * EdgeBandShare)
							{
								break;
							}
							if (TryLay(item.Slot, i, at, yaw, size, Bound.Interior, 0, Allow.Plots))
							{
								return true;
							}
						}
					}
				}
				return false;
			}
		}
	}
}
#endif
