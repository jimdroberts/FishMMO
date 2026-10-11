#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Where a template slot's pieces go in its site.</summary>
	/// <remarks>Append only: stored in template assets by ordinal.</remarks>
	public enum PointOfInterestSlotRole : byte
	{
		/// <summary>The layout's default for the slot (<see cref="PointOfInterestLayouts.RoleFor"/>).</summary>
		Auto,
		/// <summary>The first piece in the middle facing ahead, the rest packed round it facing in (a keep, a fire, an altar, stalls round a square).</summary>
		Centre,
		/// <summary>Evenly round a ring outside whatever stands in the middle, facing in (tents round a fire, a stone circle).</summary>
		Ring,
		/// <summary>A closed ring of modules along the edge, facing out, broken only by the gates (or one opening).</summary>
		Perimeter,
		/// <summary>Set into the perimeter, the first across the heading: how many is the slot's count.</summary>
		Gate,
		/// <summary>On the perimeter's corners, evenly; how many is the slot's count.</summary>
		WallTower,
		/// <summary>Along both sides of the site's streets, fronts to the street, nearest the middle first.</summary>
		Street,
		/// <summary>In rows square to the heading, all facing ahead (graves, a farm's sheds).</summary>
		Rows,
		/// <summary>Anywhere free, any way round (crates, rubble, bones).</summary>
		Scatter,
		/// <summary>End to end along the heading through the middle.</summary>
		Line,
		/// <summary>Modules end to end across the water the site stands over, at deck height (a bridge).</summary>
		Span,
		/// <summary>From the shore ahead out over the water, at deck height; the count is how many piers.</summary>
		Pier,
	}

	/// <summary>One piece a layout places: which slot it fills, where in the site's frame, and which way it faces.</summary>
	public struct PointOfInterestLayoutPiece
	{
		public int Slot;
		/// <summary>The request item it lays, or −1 for a piece the layout made up (a wall module, a gate, a span).</summary>
		public int Item;
		public PointOfInterestSlotRole Role;
		/// <summary>Metres in the site's frame: x to its right, y (z) ahead along its heading.</summary>
		public Vector2 Offset;
		/// <summary>Degrees about +y, added to the site's heading. A piece's front looks down its −z, so 180 faces ahead.</summary>
		public float Yaw;
		/// <summary>Width (along the piece's x) and depth (z) it was laid with, metres.</summary>
		public Vector2 Footprint;
	}

	/// <summary>A slot as a layout sees it: its role, its pieces' size, and how many it asks for.</summary>
	public struct PointOfInterestLayoutSlot
	{
		public PointOfInterestSlotRole Role;
		/// <summary>Its pieces' footprint (the size the layout gives the pieces it makes up).</summary>
		public Vector2 Footprint;
		/// <summary>The module length of a piece laid end to end; 0 for none.</summary>
		public float Module;
		public int Count;
	}

	/// <summary>One counted piece a layout places (any role but the made-up ones).</summary>
	public struct PointOfInterestLayoutItem
	{
		public int Slot;
		public Vector2 Footprint;
	}

	/// <summary>Everything a layout needs: pure data, so tests lay sites with no scene.</summary>
	public sealed class PointOfInterestLayoutRequest
	{
		public PointOfInterestLayout Layout;
		/// <summary>The site's footprint radius, metres.</summary>
		public float Radius;
		/// <summary>The least clear ground between two pieces, metres.</summary>
		public float Gap = 1f;
		public readonly List<PointOfInterestLayoutSlot> Slots = new List<PointOfInterestLayoutSlot>();
		public readonly List<PointOfInterestLayoutItem> Items = new List<PointOfInterestLayoutItem>();
		/// <summary>Half the length a <see cref="PointOfInterestSlotRole.Span"/> must cover; 0 takes the footprint.</summary>
		public float SpanHalf;
		/// <summary>
		/// Where a bridge's spans start and end along the heading (metres, signed, the start behind the centre): bank top to
		/// bank top, so a crossing off-centre between banks at different distances is covered. NaN uses ±<see cref="SpanHalf"/>.
		/// </summary>
		public float SpanFrom = float.NaN, SpanTo = float.NaN;
		/// <summary>Metres ahead of the centre where the water begins, for piers; NaN where there is none.</summary>
		public float ShoreAhead = float.NaN;
		/// <summary>Spots kept clear for the site's gameplay (a waypoint, a respawn point): x, z in the site's frame and y the radius.</summary>
		public readonly List<Vector3> Clearings = new List<Vector3>();
		/// <summary>
		/// Bearings (degrees in the site's frame, 0 its heading) where ways come in (<see cref="ScenePath.FromEntrance"/>): a
		/// walled site opens its gates there first.
		/// </summary>
		public readonly List<float> Entrances = new List<float>();
		/// <summary>The streets the layout laid (filled by the packer): centre, axis, width and half length, in the site's frame.</summary>
		public readonly List<(Vector2 centre, Vector2 axis, float width, float half)> Streets = new List<(Vector2, Vector2, float, float)>();
	}

	/// <summary>
	/// Where a template's pieces go inside a footprint: footprint-based packing, so no two pieces overlap (with
	/// <see cref="PointOfInterestLayoutRequest.Gap"/> between them) and every piece but a span or pier lies inside the
	/// footprint. Pure: tests read it with no scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Order.</b> The pieces that define the site are laid first and the rest fit round them: the perimeter (walls,
	/// gates, towers), the spans and piers, the streets (kept clear), the middle, the lines, the rings, the street
	/// fronts, the rows and last the scatter. A piece with no room left is dropped, never forced into an overlap.
	/// </para>
	/// <para>
	/// <b>A wall ring closes.</b> Its modules are the chords of a regular polygon whose side is exactly the module
	/// length, so neighbouring modules meet end to end; a gate takes the place of as many whole modules as its own
	/// module spans, and a site with no gate slot keeps one module open at the heading as its way in.
	/// </para>
	/// <para>
	/// <b>Facing.</b> A kit piece's front looks down its −z (StructureKit's convention), so a piece facing a direction
	/// <c>d</c> (degrees, 0 ahead) has yaw <c>d + 180</c> (<see cref="FacingYaw"/>).
	/// </para>
	/// </remarks>
	public static class PointOfInterestLayouts
	{
		/// <summary>Pieces stay this share of the footprint radius in, so nothing hangs over a pad's edge.</summary>
		public const float Inset = 0.9f;

		/// <summary>The opening a perimeter with no gate slot leaves at its heading, at least, metres.</summary>
		public const float EntranceMetres = 4f;

		/// <summary>The walk between two rows, beyond the gap, metres.</summary>
		public const float RowAisleMetres = 1f;

		/// <summary>A slot's role, its Auto resolved by the template's layout.</summary>
		public static PointOfInterestSlotRole RoleFor(PointOfInterestLayout layout, int slot, PointOfInterestSlotRole role)
		{
			if (role != PointOfInterestSlotRole.Auto)
			{
				return role;
			}
			switch (layout)
			{
				case PointOfInterestLayout.Single:
					return slot == 0 ? PointOfInterestSlotRole.Centre : PointOfInterestSlotRole.Scatter;
				case PointOfInterestLayout.Ring:
					return slot == 0 ? PointOfInterestSlotRole.Ring : PointOfInterestSlotRole.Scatter;
				case PointOfInterestLayout.Grid:
					return PointOfInterestSlotRole.Rows;
				case PointOfInterestLayout.Street:
					return PointOfInterestSlotRole.Street;
				case PointOfInterestLayout.Walled:
					return slot == 0 ? PointOfInterestSlotRole.Perimeter : PointOfInterestSlotRole.Rows;
				case PointOfInterestLayout.Linear:
					return PointOfInterestSlotRole.Line;
				default:
					return PointOfInterestSlotRole.Scatter;
			}
		}

		/// <summary>True for a role whose pieces the layout makes up from the site's geometry rather than counts.</summary>
		public static bool Generates(PointOfInterestSlotRole role)
			=> role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower
				|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier;

		/// <summary>True for a role whose pieces may reach past the footprint (a bridge over a wide river, a pier out over the sea).</summary>
		public static bool Reaches(PointOfInterestSlotRole role) => role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier;

		/// <summary>The yaw that turns a piece's front (its −z) to look along a direction, degrees (0 ahead, 90 right).</summary>
		public static float FacingYaw(float directionDegrees) => Mathf.Repeat(directionDegrees + 180f, 360f);

		/// <summary>The direction of a site-frame vector, degrees (0 ahead, 90 right).</summary>
		public static float DirectionOf(Vector2 v) => Mathf.Repeat(Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg, 360f);

		/// <summary>A site-frame offset turned by a yaw (Unity's rotation about +y).</summary>
		public static Vector2 Rotate(Vector2 local, float yawDegrees)
		{
			float a = yawDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return new Vector2(local.x * c + local.y * s, -local.x * s + local.y * c);
		}

		/// <summary>The four corners of a footprint at a centre and yaw.</summary>
		public static void Corners(Vector2 centre, float yawDegrees, Vector2 size, Vector2[] four)
		{
			Vector2 hx = Rotate(new Vector2(size.x * 0.5f, 0f), yawDegrees), hz = Rotate(new Vector2(0f, size.y * 0.5f), yawDegrees);
			four[0] = centre + hx + hz;
			four[1] = centre + hx - hz;
			four[2] = centre - hx - hz;
			four[3] = centre - hx + hz;
		}

		/// <summary>The farthest corner of a footprint from the site's centre, metres.</summary>
		public static float Reach(Vector2 centre, float yawDegrees, Vector2 size)
		{
			var four = new Vector2[4];
			Corners(centre, yawDegrees, size, four);
			float far = 0f;
			foreach (Vector2 corner in four)
			{
				far = Mathf.Max(far, corner.magnitude);
			}
			return far;
		}

		/// <summary>Whether two footprints (rectangles at centres and yaws) overlap by more than a hair: separating axes.</summary>
		public static bool Overlaps(Vector2 centreA, float yawA, Vector2 sizeA, Vector2 centreB, float yawB, Vector2 sizeB)
		{
			Box a = Box.Of(centreA, yawA, sizeA, 0f, 0), b = Box.Of(centreB, yawB, sizeB, 0f, 0);
			return a.Overlaps(b);
		}

		/// <summary>Lays a slot list by counts with point-sized pieces (the layout before any piece is measured).</summary>
		public static List<PointOfInterestLayoutPiece> Place(PointOfInterestLayout layout, float radius, IReadOnlyList<int> counts, DeterministicRNG random)
		{
			var request = new PointOfInterestLayoutRequest { Layout = layout, Radius = radius };
			for (int s = 0; s < counts.Count; s++)
			{
				PointOfInterestSlotRole role = RoleFor(layout, s, PointOfInterestSlotRole.Auto);
				int count = Math.Max(0, counts[s]);
				request.Slots.Add(new PointOfInterestLayoutSlot { Role = role, Count = count });
				if (!Generates(role))
				{
					for (int k = 0; k < count; k++)
					{
						request.Items.Add(new PointOfInterestLayoutItem { Slot = s });
					}
				}
			}
			return Place(request, random);
		}

		/// <summary>Lays a request: see the class remarks. Deterministic in <paramref name="random"/>.</summary>
		public static List<PointOfInterestLayoutPiece> Place(PointOfInterestLayoutRequest request, DeterministicRNG random)
		{
			var packer = new Packer(request, random);
			if (request.Radius > 0f && request.Slots.Count > 0)
			{
				packer.Run();
			}
			return packer.Pieces;
		}

		// ── Packing ──────────────────────────────────────────────────

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

			private float Project(Vector2 axis) => Mathf.Abs(Vector2.Dot(Ax, axis)) * Half.x + Mathf.Abs(Vector2.Dot(Az, axis)) * Half.y;

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
		}

		private sealed class Packer
		{
			private const int PerimeterGroup = 1, SpanGroup = 2, PierGroup = 3, StreetGroup = -1, ClearingGroup = -2;

			private readonly PointOfInterestLayoutRequest request;
			private readonly DeterministicRNG random;
			private readonly PointOfInterestSlotRole[] roles;
			private readonly List<Box> boxes = new List<Box>();
			private readonly List<(Vector2 a, Vector2 b, float width, float half)> streets = new List<(Vector2, Vector2, float, float)>();
			public readonly List<PointOfInterestLayoutPiece> Pieces = new List<PointOfInterestLayoutPiece>();
			private readonly float r;
			private float interior;

			public Packer(PointOfInterestLayoutRequest request, DeterministicRNG random)
			{
				this.request = request;
				this.random = random;
				r = Mathf.Max(0f, request.Radius) * Inset;
				interior = r;
				roles = new PointOfInterestSlotRole[request.Slots.Count];
				for (int s = 0; s < roles.Length; s++)
				{
					roles[s] = RoleFor(request.Layout, s, request.Slots[s].Role);
				}
			}

			private float Gap => Mathf.Max(0f, request.Gap);

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
				if (HasItems(PointOfInterestSlotRole.Street))
				{
					Streets();
				}
				foreach (Vector3 clearing in request.Clearings)
				{
					boxes.Add(Box.Of(new Vector2(clearing.x, clearing.z), 0f, Vector2.one * (2f * Mathf.Max(0f, clearing.y)), 0f, ClearingGroup));
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

			private bool Inside(Vector2 centre, float yaw, Vector2 size, float limit) => Reach(centre, yaw, size) <= limit + 1e-3f;

			private bool Fits(in Box box, bool ignoreStreets = false)
			{
				foreach (Box other in boxes)
				{
					if (other.Group != 0 && other.Group == box.Group)
					{
						continue;
					}
					if (ignoreStreets && other.Group == StreetGroup)
					{
						continue;
					}
					if (box.Overlaps(other))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Lays a piece when it fits inside <paramref name="limit"/> and clear of everything; returns whether it did.</summary>
			private bool TryLay(int slot, int item, Vector2 centre, float yaw, Vector2 size, float limit, int group = 0, bool ignoreStreets = false)
			{
				if (limit > 0f && !Inside(centre, yaw, size, limit))
				{
					return false;
				}
				Box box = Box.Of(centre, yaw, size, Gap, group);
				if (!Fits(box, ignoreStreets))
				{
					return false;
				}
				Lay(slot, item, centre, yaw, size, box);
				return true;
			}

			private void Lay(int slot, int item, Vector2 centre, float yaw, Vector2 size, in Box box)
			{
				boxes.Add(box);
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

			// ── The perimeter ───────────────────────────────────────

			private struct Planned
			{
				public int Slot;
				public Vector2 C;
				public float Yaw;
				public Vector2 Size;
			}

			/// <summary>The wall ring, its gates and its towers; shrunk until every corner lies inside the footprint.</summary>
			private void Perimeter(int wall, int gate, int tower)
			{
				float limit = r;
				List<Planned> plan = null;
				float inner = r;
				for (int attempt = 0; attempt < 8 && limit > 1f; attempt++)
				{
					plan = PlanPerimeter(wall, gate, tower, limit, out inner);
					if (plan == null)
					{
						return;
					}
					float far = 0f;
					foreach (Planned p in plan)
					{
						far = Mathf.Max(far, Reach(p.C, p.Yaw, p.Size));
					}
					if (far <= r + 1e-3f)
					{
						break;
					}
					limit -= far - r + 0.05f;
					plan = null;
				}
				if (plan == null)
				{
					return;
				}
				foreach (Planned p in plan)
				{
					Lay(p.Slot, -1, p.C, p.Yaw, p.Size, Box.Of(p.C, p.Yaw, p.Size, Gap, PerimeterGroup));
				}
				interior = Mathf.Max(0f, Mathf.Min(r, inner));
			}

			private List<Planned> PlanPerimeter(int wall, int gate, int tower, float limit, out float inner)
			{
				PointOfInterestLayoutSlot w = request.Slots[wall];
				var plan = new List<Planned>();
				Vector2 towerSize = tower >= 0 ? request.Slots[tower].Footprint : Vector2.zero;
				Vector2 gateSize = gate >= 0 ? request.Slots[gate].Footprint : Vector2.zero;
				float depth = Mathf.Max(0f, w.Footprint.y);
				float module = w.Module;
				inner = limit;

				if (module <= 0f)
				{
					// Free-standing pieces round the edge (stones, posts): evenly, facing out; gates across the heading first.
					int n = Math.Max(0, w.Count);
					float ring = Mathf.Max(0f, limit - w.Footprint.magnitude * 0.5f);
					int ways = gate >= 0 ? Math.Max(Math.Max(1, request.Slots[gate].Count), request.Entrances.Count) : 0;
					for (int g = 0; g < ways; g++)
					{
						float a = GateAngle(g, ways);
						plan.Add(new Planned { Slot = gate, C = Dir(a) * Mathf.Max(0f, limit - gateSize.magnitude * 0.5f), Yaw = FacingYaw(a * Mathf.Rad2Deg), Size = gateSize });
					}
					for (int i = 0; i < n; i++)
					{
						float a = (i + 0.5f) * Mathf.PI * 2f / Math.Max(1, n);
						plan.Add(new Planned { Slot = wall, C = Dir(a) * ring, Yaw = FacingYaw(a * Mathf.Rad2Deg), Size = w.Footprint });
					}
					int towers = tower >= 0 ? Math.Max(0, request.Slots[tower].Count) : 0;
					for (int t = 0; t < towers; t++)
					{
						float a = (t + 0.25f) * Mathf.PI * 2f / towers;
						plan.Add(new Planned { Slot = tower, C = Dir(a) * Mathf.Max(0f, limit - towerSize.magnitude * 0.5f), Yaw = FacingYaw(a * Mathf.Rad2Deg), Size = towerSize });
					}
					inner = ring - Mathf.Max(w.Footprint.magnitude, Mathf.Max(gateSize.magnitude, towerSize.magnitude)) * 0.5f - Gap;
					return plan;
				}

				float circum = limit - depth * 0.5f;
				if (tower >= 0)
				{
					circum = Mathf.Min(circum, Mathf.Sqrt(Mathf.Max(0f, limit * limit - towerSize.x * towerSize.x * 0.25f)) - towerSize.y * 0.5f);
				}
				if (circum <= 0f || module >= 2f * circum)
				{
					return null;
				}
				int sides = Mathf.FloorToInt(Mathf.PI / Mathf.Asin(module / (2f * circum)));
				if (sides < 3)
				{
					return null;
				}
				float step = Mathf.PI * 2f / sides;
				// The circumradius that makes every side exactly one module long: neighbours meet end to end.
				float radius = module / (2f * Mathf.Sin(step * 0.5f));

				float gateLength = gate >= 0 ? Mathf.Max(request.Slots[gate].Module, gateSize.x) : EntranceMetres;
				int span = gate >= 0 ? Math.Max(1, Mathf.RoundToInt(gateLength / module)) : Math.Max(1, Mathf.CeilToInt(EntranceMetres / module - 1e-3f));
				int gates = gate >= 0 ? Mathf.Clamp(Math.Max(request.Slots[gate].Count, request.Entrances.Count), 1, Math.Max(1, sides / (span + 1))) : 1;
				// An odd span centres a side on the heading, an even one a corner.
				float start = span % 2 == 1 ? -step * 0.5f : 0f;
				Vector2 Vertex(int j) => Dir(start + j * step) * radius;
				int Wrap(int j) => ((j % sides) + sides) % sides;

				var covered = new bool[sides];
				var gateStarts = new List<int>();
				for (int g = 0; g < gates; g++)
				{
					// The ways' bearings first (a gate where each road comes in), the rest evenly.
					int first = Wrap(-(span / 2) + Mathf.RoundToInt(GateAngle(g, gates) / step));
					if (gateStarts.Contains(first))
					{
						continue;
					}
					gateStarts.Add(first);
					for (int c = 0; c < span; c++)
					{
						covered[Wrap(first + c)] = true;
					}
				}
				for (int c = 0; c < sides; c++)
				{
					if (covered[c])
					{
						continue;
					}
					Vector2 mid = (Vertex(c) + Vertex(c + 1)) * 0.5f;
					plan.Add(new Planned { Slot = wall, C = mid, Yaw = FacingYaw(DirectionOf(mid)), Size = new Vector2(module, depth) });
				}
				float gateInner = float.PositiveInfinity;
				if (gate >= 0)
				{
					foreach (int first in gateStarts)
					{
						Vector2 mid = (Vertex(first) + Vertex(first + span)) * 0.5f;
						plan.Add(new Planned { Slot = gate, C = mid, Yaw = FacingYaw(DirectionOf(mid)), Size = gateSize });
						gateInner = Mathf.Min(gateInner, mid.magnitude - gateSize.y * 0.5f);
					}
				}
				float towerInner = float.PositiveInfinity;
				int towerCount = tower >= 0 ? Math.Max(0, request.Slots[tower].Count) : 0;
				if (towerCount > 0)
				{
					// Corners a gate does not take: a gatehouse carries its own towers, an opening is flanked by them.
					var corners = new List<int>();
					for (int j = 0; j < sides; j++)
					{
						bool free = true;
						foreach (int first in gateStarts)
						{
							for (int c = gate >= 0 ? 0 : 1; c <= (gate >= 0 ? span : span - 1); c++)
							{
								free &= Wrap(first + c) != j;
							}
						}
						if (free)
						{
							corners.Add(j);
						}
					}
					int count = Math.Min(towerCount, corners.Count);
					for (int t = 0; t < count; t++)
					{
						int j = corners[Mathf.Min(corners.Count - 1, Mathf.RoundToInt(t * corners.Count / (float)count))];
						Vector2 at = Vertex(j);
						plan.Add(new Planned { Slot = tower, C = at, Yaw = FacingYaw(DirectionOf(at)), Size = towerSize });
						towerInner = radius - towerSize.magnitude * 0.5f;
					}
				}
				inner = Mathf.Min(radius * Mathf.Cos(step * 0.5f) - depth * 0.5f, Mathf.Min(gateInner, towerInner)) - Gap;
				return plan;
			}

			/// <summary>
			/// Gate <paramref name="g"/> of <paramref name="count"/>'s bearing, radians: the ways' entrances first
			/// (<see cref="PointOfInterestLayoutRequest.Entrances"/>), then evenly round from the heading.
			/// </summary>
			private float GateAngle(int g, int count)
			{
				if (g < request.Entrances.Count)
				{
					return Mathf.Repeat(request.Entrances[g], 360f) * Mathf.Deg2Rad;
				}
				return g * Mathf.PI * 2f / Math.Max(1, count);
			}

			/// <summary>Gates and towers with no wall: a lone gateway across the heading at the edge, towers round it.</summary>
			private void Loose(int gate, int tower)
			{
				if (gate >= 0)
				{
					PointOfInterestLayoutSlot g = request.Slots[gate];
					int n = Math.Max(Math.Max(1, g.Count), request.Entrances.Count);
					for (int i = 0; i < n; i++)
					{
						float a = GateAngle(i, n);
						Vector2 at = Dir(a) * Mathf.Max(0f, r - g.Footprint.y * 0.5f - 0.5f);
						TryLay(gate, -1, at, FacingYaw(a * Mathf.Rad2Deg), g.Footprint, r, PerimeterGroup);
					}
				}
				if (tower >= 0)
				{
					PointOfInterestLayoutSlot t = request.Slots[tower];
					int n = Math.Max(0, t.Count);
					for (int i = 0; i < n; i++)
					{
						float a = (i + 0.5f) * Mathf.PI * 2f / n;
						Vector2 at = Dir(a) * Mathf.Max(0f, r - t.Footprint.magnitude * 0.5f);
						TryLay(tower, -1, at, FacingYaw(a * Mathf.Rad2Deg), t.Footprint, r, PerimeterGroup);
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
				 * crossing ran up to most of a module past each end, over the path that comes to it (Jim, 2026-10-11). */
				int n = Math.Max(1, Mathf.RoundToInt((to - from) / module));
				float length = (to - from) / n;
				float middle = 0.5f * (from + to);
				for (int i = 0; i < n; i++)
				{
					var at = new Vector2(0f, middle + (i - (n - 1) * 0.5f) * length);
					TryLay(s, -1, at, 0f, new Vector2(slot.Footprint.x, length), 0f, SpanGroup);
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
				int modules = Math.Max(2, Mathf.RoundToInt(request.Radius * 0.6f / module));
				float spacing = Mathf.Max(slot.Footprint.x * 3f, 8f);
				for (int p = 0; p < piers; p++)
				{
					float x = (p - (piers - 1) * 0.5f) * spacing;
					for (int i = 0; i < modules; i++)
					{
						// The first module stands half on the shore.
						var at = new Vector2(x, request.ShoreAhead + i * module);
						TryLay(s, -1, at, 0f, new Vector2(slot.Footprint.x, module), 0f, PierGroup);
					}
				}
			}

			// ── Streets ─────────────────────────────────────────────

			/// <summary>
			/// The street plan inside the walls (or the footprint): a main street along the heading, a cross street once
			/// there is room, and two more each way in a city. Kept clear of everything but the first piece in the middle.
			/// </summary>
			private void Streets()
			{
				float width = Mathf.Clamp(interior * 0.08f, 4f, 10f);
				void Add(bool alongZ, float offset)
				{
					float half = Mathf.Sqrt(Mathf.Max(0f, interior * interior - offset * offset));
					if (half < 4f)
					{
						return;
					}
					Vector2 axis = alongZ ? Vector2.up : Vector2.right, across = alongZ ? Vector2.right : Vector2.up;
					Vector2 centre = across * offset;
					streets.Add((centre, axis, width, half));
					request.Streets.Add((centre, axis, width, half));
					Vector2 size = alongZ ? new Vector2(width, 2f * half) : new Vector2(2f * half, width);
					boxes.Add(Box.Of(centre, 0f, size, 0f, StreetGroup));
				}
				Add(true, 0f);
				if (request.Layout != PointOfInterestLayout.Linear)
				{
					if (interior >= 35f)
					{
						Add(false, 0f);
					}
					if (interior >= 90f)
					{
						Add(true, -interior * 0.5f);
						Add(true, interior * 0.5f);
						Add(false, -interior * 0.5f);
						Add(false, interior * 0.5f);
					}
				}
			}

			/// <summary>Street fronts: each piece where the nearest free frontage to the middle has room, its front to the street.</summary>
			private void Fronts()
			{
				if (streets.Count == 0)
				{
					return;
				}
				// One cursor per street, side and direction, from the street's middle outward.
				int frontages = streets.Count * 4;
				var cursor = new float[frontages];
				var closed = new bool[frontages];
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Street)
					{
						continue;
					}
					Vector2 size = item.Footprint;
					int best = -1;
					float bestDistance = float.PositiveInfinity, bestT = 0f;
					Vector2 bestAt = default;
					float bestYaw = 0f;
					for (int f = 0; f < frontages; f++)
					{
						if (closed[f])
						{
							continue;
						}
						(Vector2 centre, Vector2 axis, float width, float half) = streets[f / 4];
						float side = (f & 1) == 0 ? 1f : -1f, direction = (f & 2) == 0 ? 1f : -1f;
						Vector2 across = new Vector2(axis.y, axis.x);
						// Facing the street: from the +side the front looks back across it.
						float yaw = FacingYaw(DirectionOf(-across * side));
						float set = width * 0.5f + Gap + size.y * 0.5f;
						for (float t = cursor[f]; ; t += direction * 0.5f)
						{
							float along = t + direction * size.x * 0.5f;
							if (Mathf.Abs(along) + size.x * 0.5f > half)
							{
								break;
							}
							Vector2 at = centre + across * (side * set) + axis * along;
							if (!Inside(at, yaw, size, interior))
							{
								break;
							}
							float distance = at.magnitude;
							if (distance >= bestDistance)
							{
								break;
							}
							if (Fits(Box.Of(at, yaw, size, Gap, 0)))
							{
								best = f;
								bestDistance = distance;
								bestT = t;
								bestAt = at;
								bestYaw = yaw;
								break;
							}
						}
					}
					if (best < 0)
					{
						continue;
					}
					float dir = (best & 2) == 0 ? 1f : -1f;
					cursor[best] = bestT + dir * (size.x + Gap);
					Lay(item.Slot, i, bestAt, bestYaw, size, Box.Of(bestAt, bestYaw, size, Gap, 0));
				}
			}

			// ── Middle, lines, rings, rows, scatter ────────────────

			/// <summary>The first middle piece at the centre facing ahead; the rest spiralled out round it, facing in.</summary>
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
					if (first)
					{
						// The centrepiece always stands, a lighthouse wider than its little footprint included.
						first = false;
						if (TryLay(item.Slot, i, Vector2.zero, 180f, item.Footprint, 0f, 0, true))
						{
							continue;
						}
					}
					Spiral(item, i, turn);
				}
			}

			private void Spiral(PointOfInterestLayoutItem item, int index, float turn)
			{
				for (float rho = 1f; rho <= interior; rho += 1f)
				{
					int steps = Math.Max(6, Mathf.CeilToInt(2f * Mathf.PI * rho / 1.5f));
					for (int k = 0; k < steps; k++)
					{
						float a = (turn + k * 360f / steps) * Mathf.Deg2Rad;
						Vector2 at = Dir(a) * rho;
						if (TryLay(item.Slot, index, at, FacingYaw(DirectionOf(-at)), item.Footprint, interior))
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
					TryLay(item.Slot, i, new Vector2(0f, z + pitch * 0.5f), 0f, item.Footprint, interior);
					z += pitch;
				}
			}

			/// <summary>One slot's pieces evenly round a ring outside what is already in the middle, facing in.</summary>
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
					Vector2 at = Dir(a + arc * 0.5f) * rho;
					TryLay(item.Slot, i, at, FacingYaw(DirectionOf(-at)), item.Footprint, interior);
					a += arc + slack;
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
				float px = largest.x + Gap, pz = largest.y + Gap + RowAisleMetres;
				int kx = Mathf.CeilToInt(interior / px), kz = Mathf.CeilToInt(interior / pz);
				var lattice = new List<Vector2>();
				for (int j = -kz; j <= kz; j++)
				{
					for (int i = -kx; i <= kx; i++)
					{
						var at = new Vector2(i * px, j * pz);
						if (Inside(at, 180f, largest, interior))
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
						if (!used[k] && TryLay(item.Slot, i, lattice[k], 180f, item.Footprint, interior))
						{
							used[k] = true;
							break;
						}
					}
				}
			}

			/// <summary>Dart throwing: each piece at the first of 48 throws that is clear, any way round.</summary>
			private void Scatter()
			{
				for (int i = 0; i < request.Items.Count; i++)
				{
					PointOfInterestLayoutItem item = request.Items[i];
					if (Of(item) != PointOfInterestSlotRole.Scatter)
					{
						continue;
					}
					float reach = Mathf.Max(0f, interior - item.Footprint.magnitude * 0.5f);
					for (int attempt = 0; attempt < 48; attempt++)
					{
						float a = random.NextFloat() * Mathf.PI * 2f;
						float d = Mathf.Sqrt(random.NextFloat()) * reach;
						float yaw = random.NextFloat() * 360f;
						if (TryLay(item.Slot, i, Dir(a) * d, yaw, item.Footprint, interior))
						{
							break;
						}
					}
				}
			}
		}
	}

	/// <summary>A piece's weathering: Auto reads the site's kind and biome; the rest force one finish, laid on a share of the pieces.</summary>
	/// <remarks>Append only: stored in template assets by ordinal.</remarks>
	public enum PointOfInterestFinish : byte
	{
		Auto,
		None,
		Mossy,
		Charred,
		Algae,
	}

	/// <summary>
	/// Lays a template's pieces by its layout inside the site's footprint, on the ground, as instanced props of the
	/// scene's POI prop set. A piece no source can resolve is skipped (and counted in the notes).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stable before the art.</b> Every draw is taken whether or not the piece resolves, and the layout packs the
	/// pieces by the kit's measured footprints (<see cref="PointOfInterestPieceResolver.Measure"/>, from the kit's table,
	/// never the art), so a site laid before its art exists lays the same pieces in the same places once it arrives.
	/// </para>
	/// <para>
	/// <b>Seating.</b> A piece on the ground is set at its centre's ground, lowered until its foundation reaches the
	/// lowest corner of its footprint, so a house on a slope shows its plinth rather than a gap. A deck piece (a bridge
	/// span, a pier) is set at its deck: the banks' height over a river, a metre above the water out from a shore.
	/// </para>
	/// <para>
	/// <b>Decay</b> leaves a piece out with probability decay / 2, lays a piece past the kit's ruin threshold as its ruin,
	/// and sinks and tilts the ones it keeps. <b>Counts</b> follow the site's size class: a small site draws from the
	/// lowest third of a slot's range, a large one (every capital) from the top third.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class PropsFeature : PointOfInterestFeature
	{
		/// <summary>The most a fully ruined piece is sunk, metres, and tilted, degrees.</summary>
		public float MaxSinkMetres = 0.4f;
		public float MaxTiltDegrees = 8f;
		/// <summary>The least clear ground between two pieces, metres.</summary>
		[Min(0f)] public float Gap = 1f;
		/// <summary>Draw a slot's count from the third of its range the site's size class names.</summary>
		public bool CountsFollowSize = true;
		/// <summary>Weathering: Auto by kind and biome (algae under water and in swamps, moss on old pieces in damp country, char on battlefields).</summary>
		public PointOfInterestFinish Finish = PointOfInterestFinish.Auto;
		/// <summary>The share of pieces a forced finish is laid on.</summary>
		[Range(0f, 1f)] public float FinishChance = 1f;
		/// <summary>
		/// Leave out a ground piece whose middle is under water (a port's houses past its shore). Off for the kinds that
		/// stand in water on purpose (a drowned village); never applies under the sea, to stilts, piers or bridges.
		/// </summary>
		public bool KeepOutOfWater = true;
		/// <summary>Spots no piece is laid on, kept for the site's gameplay: x, z in the site's frame and y the radius, metres.</summary>
		public List<Vector3> Clearings = new List<Vector3>();

		/// <summary>One piece's draws, always all taken so a piece's draws never shift another's.</summary>
		private struct Draws
		{
			public int Seed;
			public float Decay, LeaveOut, Scale, TiltX, TiltZ, FinishRoll;
		}

		private static Draws Take(DeterministicRNG random, PointOfInterestPieceSlot slot, bool unscaled)
		{
			var d = new Draws
			{
				Decay = random.Range(slot.MinDecay, Mathf.Max(slot.MinDecay, slot.MaxDecay)),
				LeaveOut = random.NextFloat(),
				Seed = random.Next(),
			};
			float scale = random.Range(slot.MinScale, Mathf.Max(slot.MinScale, slot.MaxScale));
			d.Scale = unscaled ? 1f : scale;
			d.TiltX = random.Range(-1f, 1f);
			d.TiltZ = random.Range(-1f, 1f);
			d.FinishRoll = random.NextFloat();
			return d;
		}

		/// <summary>
		/// The streets the layout laid, written to the scene's ways (<see cref="ScenePathClass.Street"/>) so the path surface
		/// draws them: cobbled in a city, gravel in a town (cobbled where it is built of stone), trodden earth in a village.
		/// </summary>
		private static void AddStreets(PointOfInterestSiteContext context, PointOfInterestLayoutRequest request)
		{
			if (context.Points == null || request.Streets.Count == 0)
			{
				return;
			}
			PointOfInterestRecord record = context.Record;
			int rank = PathNetworkPlanner.RankOf(record.Kind);
			bool stoneBuilt = !string.IsNullOrEmpty(context.Style) && context.Style.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0;
			ScenePathSurface surface = rank >= 4 ? ScenePathSurface.Stone : rank >= 3 ? (stoneBuilt ? ScenePathSurface.Stone : ScenePathSurface.Gravel) : ScenePathSurface.Earth;
			float wear = rank >= 3 ? 1f : 0.85f;
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			foreach ((Vector2 centre, Vector2 axis, float width, float half) in request.Streets)
			{
				int n = Math.Max(2, Mathf.CeilToInt(2f * half / PathNetworkPlanner.PointSpacing) + 1);
				var path = new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Street,
					FromId = record.Id,
					ToId = record.Id,
					Points = new Vector3[n],
					HalfWidth = new float[n],
					Wear = new float[n],
					Surface = new byte[n],
					Flags = new byte[n],
				};
				for (int i = 0; i < n; i++)
				{
					Vector2 local = centre + axis * Mathf.Lerp(-half, half, i / (float)(n - 1));
					path.Points[i] = context.OnGround(local);
					// Narrower than the strip kept clear for it: the houses' fronts keep a verge.
					path.HalfWidth[i] = width * 0.4f;
					path.Wear[i] = wear;
					path.Surface[i] = (byte)surface;
					path.Flags[i] = (byte)ScenePathPointFlags.InSite;
				}
				context.Points.Paths.Add(path);
			}
		}

		/// <summary>
		/// The ground a site's pieces wear, written to the scene's ways as yards (<see cref="ScenePathClass.Yard"/>): bare
		/// earth under and just round each building (no grass blade comes up through a floor), a trodden apron round a
		/// well, a fire or a stall, and a worn way out of each front door. The grass between them grows; the path field
		/// thins it where feet go (FishGroundPaths.hlsl).
		/// </summary>
		private static void AddYards(PointOfInterestSiteContext context,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds)
		{
			if (context.Points == null || grounds.Count == 0)
			{
				return;
			}
			int id = 0;
			foreach (ScenePath existing in context.Points.Paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			// How lived in the site is: a ruin's ground has grown back over, a camp's is fresh, a town's beaten bare.
			PointOfInterestGroup group = PointOfInterestKinds.Info(context.Record.Kind).Group;
			float kept = group == PointOfInterestGroup.Ruins || group == PointOfInterestGroup.Dead ? 0.3f
				: PointOfInterestKinds.Info(context.Record.Kind).Has(PointOfInterestTraits.Settlement) ? 1f : 0.8f;
			void Add(Vector3 a, Vector3 b, float half, float wear, int toId)
			{
				wear *= kept;
				var path = new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Yard,
					FromId = -1,
					ToId = toId,
					Points = new[] { a, b },
					HalfWidth = new[] { half, half },
					Wear = new[] { wear, wear },
					Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
					Flags = new[] { (byte)ScenePathPointFlags.InSite, (byte)ScenePathPointFlags.InSite },
				};
				context.Points.Paths.Add(path);
			}
			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag) in grounds)
			{
				// Walls, gates, spans and piers: the ground beside them is not worn by them.
				if (role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower
					|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier)
				{
					continue;
				}
				float width = Mathf.Max(0.1f, size.x), depth = Mathf.Max(0.1f, size.y);
				float longest = Mathf.Max(width, depth), shortest = Mathf.Min(width, depth);
				if (longest < 1.2f)
				{
					continue;
				}
				string t = tag ?? string.Empty;
				bool gathering = t.IndexOf("well", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("fire", StringComparison.OrdinalIgnoreCase) >= 0
					|| t.IndexOf("stall", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0
					|| role == PointOfInterestSlotRole.Centre;
				if (gathering)
				{
					// Where people stand about: a trodden ring a pace and a half round it.
					Add(at, at, longest * 0.5f + 1.6f, 0.8f, -1);
					continue;
				}
				bool building = longest >= 3f;
				// Under and round it: a rectangle as a capsule along its long side.
				Vector3 axis = turn * (width >= depth ? Vector3.right : Vector3.forward);
				float reach = Mathf.Max(0f, longest - shortest) * 0.5f;
				Add(at - axis * reach, at + axis * reach, shortest * 0.5f + (building ? 0.6f : 0.3f), building ? 1f : 0.75f, -1);
				if (building)
				{
					// Out of its front door (a piece's front looks down its −z) a few paces toward the street.
					Vector3 front = turn * Vector3.back;
					Vector3 door = at + front * (depth * 0.5f);
					Add(door, door + front * 3.5f, 0.55f, 0.9f, context.Record.Id);
				}
			}
		}

		/// <summary>
		/// What gathers round lived-in buildings (Jim, 2026-10-11: sites looked dated): by each front door a water butt, a
		/// bench or a low lantern; a woodpile against a side wall; a cart, hay or a washing line in the back yard; lantern
		/// posts along a town's streets. Round a camp's tents, drying and weapon racks, water and a log heap. Each is kept
		/// clear of the buildings, the streets and one another; a ruin gets none.
		/// </summary>
		private static void AddClutter(PointOfInterestSiteContext context,
			List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)> grounds,
			PointOfInterestLayoutRequest request, DeterministicRNG random)
		{
			PointOfInterestRecord record = context.Record;
			PointOfInterestKindInfo info = PointOfInterestKinds.Info(record.Kind);
			if (info.Group == PointOfInterestGroup.Ruins || info.Group == PointOfInterestGroup.Dead || info.Has(PointOfInterestTraits.Underwater))
			{
				return;
			}
			bool town = info.Has(PointOfInterestTraits.Settlement) || info.Group == PointOfInterestGroup.Settlement;
			bool camp = !town && (info.Group == PointOfInterestGroup.Wild || record.Kind == POIType.BossLair || record.Kind == POIType.PirateCove
				|| record.Kind == POIType.SmugglersCove || record.Kind == POIType.Oasis);
			if (!town && !camp)
			{
				return;
			}
			// What may not be stood on: every laid piece's footprint, and what has been placed since.
			var taken = new List<(Vector2 centre, float yaw, Vector2 size)>();
			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole _, string _) in grounds)
			{
				taken.Add((new Vector2(at.x, at.z), turn.eulerAngles.y, size + Vector2.one * 0.6f));
			}
			var streets = new List<(Vector2 a, Vector2 b, float half)>();
			Quaternion siteTurn = Quaternion.Euler(0f, record.Yaw, 0f);
			var origin = new Vector2(record.Position.x, record.Position.z);
			foreach ((Vector2 centre, Vector2 axis, float width, float half) in request.Streets)
			{
				Vector3 a = siteTurn * new Vector3(centre.x - axis.x * half, 0f, centre.y - axis.y * half);
				Vector3 b = siteTurn * new Vector3(centre.x + axis.x * half, 0f, centre.y + axis.y * half);
				streets.Add((origin + new Vector2(a.x, a.z), origin + new Vector2(b.x, b.z), width * 0.5f));
			}
			bool OnStreet(Vector2 p, float pad)
			{
				foreach ((Vector2 a, Vector2 b, float half) in streets)
				{
					Vector2 ab = b - a;
					float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
					if (Vector2.Distance(a + ab * t, p) < half + pad)
					{
						return true;
					}
				}
				return false;
			}
			int placed = 0;
			bool Put(string query, Vector3 at, float yaw)
			{
				var pieceRequest = new PointOfInterestPieceRequest(query, "timber", (int)(random.NextDouble() * 1000000));
				GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
				if (prefab == null)
				{
					return false;
				}
				Vector2 size = PointOfInterestPieceResolver.Measure(pieceRequest).Footprint;
				var c = new Vector2(at.x, at.z);
				if (OnStreet(c, Mathf.Max(size.x, size.y) * 0.5f))
				{
					return false;
				}
				foreach ((Vector2 centre, float takenYaw, Vector2 takenSize) in taken)
				{
					if (PointOfInterestLayouts.Overlaps(c, yaw, size, centre, takenYaw, takenSize))
					{
						return false;
					}
				}
				if (context.Water != null && context.Water.KindAt(at.x, at.z) != WaterKind.None)
				{
					return false;
				}
				taken.Add((c, yaw, size + Vector2.one * 0.3f));
				context.AddProp(prefab, new Vector3(at.x, context.GroundAt(at.x, at.z), at.z), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
				placed++;
				return true;
			}
			string Pick(params string[] choices) => choices[(int)(random.NextDouble() * choices.Length) % choices.Length];

			foreach ((Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag) in grounds)
			{
				if (role == PointOfInterestSlotRole.Perimeter || role == PointOfInterestSlotRole.Gate || role == PointOfInterestSlotRole.WallTower
					|| role == PointOfInterestSlotRole.Span || role == PointOfInterestSlotRole.Pier)
				{
					continue;
				}
				float width = size.x, depth = size.y;
				float yaw = turn.eulerAngles.y;
				Vector3 front = turn * Vector3.back, right = turn * Vector3.right;
				if (town && tag == PointOfInterestTemplateSpecs.Church)
				{
					// The churchyard: headstones in rows behind and beside the church, the odd plain mound among them, all
					// facing the same way as the church; rows break where the ground is taken or a street runs.
					int rows = 2 + (int)(random.NextDouble() * 2.0);
					int across = 3 + (int)(random.NextDouble() * 3.0);
					for (int r = 0; r < rows; r++)
					{
						for (int c = 0; c < across; c++)
						{
							if (random.NextDouble() < 0.15)
							{
								continue; // a gap: an old grave gone, or room not yet used
							}
							float x = (c - (across - 1) * 0.5f) * 1.7f + (float)(random.NextDouble() - 0.5) * 0.3f;
							float z = depth * 0.5f + 2.2f + r * 2.3f;
							float lean = (float)(random.NextDouble() - 0.5) * 8f;
							Put(random.NextDouble() < 0.15 ? "=GraveMound" : "=Gravestone", at - front * z + right * x, yaw + lean);
						}
					}
					// And a few along the church's sunny side.
					float sideways = random.NextDouble() < 0.5 ? -1f : 1f;
					for (int g = 0; g < 3; g++)
					{
						Put("=Gravestone", at + right * sideways * (width * 0.5f + 2f) + front * ((g - 1) * 2.4f), yaw + (float)(random.NextDouble() - 0.5) * 8f);
					}
					continue;
				}
				if (town && tag == PointOfInterestTemplateSpecs.Temple)
				{
					// A temple's court is kept clear: no woodpiles or washing against its columns.
					continue;
				}
				if (town && Mathf.Max(width, depth) >= 3f)
				{
					// By the door.
					if (random.NextDouble() < 0.7)
					{
						float along = (random.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Max(0.6f, width * 0.5f - 0.7f);
						Put(Pick("=WaterButt", "=Bench", "=LanternPost"), at + front * (depth * 0.5f + 0.55f) + right * along, yaw);
					}
					// Against a side wall.
					if (random.NextDouble() < 0.6)
					{
						float side = random.NextDouble() < 0.5 ? -1f : 1f;
						Put(Pick("=Woodpile", "=WaterButt", "=HayBales"), at + right * side * (width * 0.5f + 0.5f) - front * (float)(random.NextDouble() - 0.5) * depth * 0.6f, yaw + 90f);
					}
					// The back yard.
					if (random.NextDouble() < 0.45)
					{
						Put(Pick("=Cart", "=HayBales", "=WashingLine", "=Woodpile"), at - front * (depth * 0.5f + 2.5f) + right * (float)(random.NextDouble() - 0.5) * width, yaw + (float)(random.NextDouble() - 0.5) * 30f);
					}
				}
				else if (camp && Mathf.Max(width, depth) >= 1.8f && random.NextDouble() < 0.6)
				{
					bool hostile = info.Has(PointOfInterestTraits.Hostile);
					float a = (float)(random.NextDouble() * Math.PI * 2.0);
					var offset = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (Mathf.Max(width, depth) * 0.5f + 1.4f);
					Put(hostile ? Pick("=WeaponRack", "=DryingRack", "=WaterButt", "=Woodpile") : Pick("=DryingRack", "=WaterButt", "=Woodpile"), at + offset, a * Mathf.Rad2Deg + 90f);
				}
			}
			// Lanterns along a town's streets, either side in turn, every dozen paces or so.
			if (town && (record.Kind == POIType.Town || record.Kind == POIType.City || record.Kind == POIType.Capital || record.Kind == POIType.Port))
			{
				foreach ((Vector2 a, Vector2 b, float half) in streets)
				{
					Vector2 ab = b - a;
					float length = ab.magnitude;
					if (length < 10f)
					{
						continue;
					}
					Vector2 along = ab / length, across = new Vector2(-along.y, along.x);
					int side = 1;
					for (float t = 6f; t < length - 6f; t += 13f, side = -side)
					{
						Vector2 p = a + along * t + across * side * (half + 0.7f);
						float yaw = Mathf.Atan2(-across.x * side, -across.y * side) * Mathf.Rad2Deg;
						var pieceRequest = new PointOfInterestPieceRequest("=LanternPost", "timber", 0);
						GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
						if (prefab != null && !taken.Exists(x => Vector2.Distance(x.centre, p) < Mathf.Max(x.size.x, x.size.y) * 0.5f + 0.5f))
						{
							context.AddProp(prefab, new Vector3(p.x, context.GroundAt(p.x, p.y), p.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
							taken.Add((p, yaw, new Vector2(0.4f, 0.8f)));
							placed++;
						}
					}
				}
			}
		}

		public override void Build(PointOfInterestSiteContext context)
		{
			PointOfInterestTemplate template = context.Template;
			if (template == null || template.Pieces == null || template.Pieces.Count == 0)
			{
				return;
			}
			DeterministicRNG random = context.Random;
			PointOfInterestRecord record = context.Record;
			bool allowLocal = context.Scene.IsValid() && LocalArtScope.IsLocalScenePath(context.Scene.path);
			string biome = context.BiomeAt?.Invoke(record.Position.x, record.Position.z)?.name;

			var request = new PointOfInterestLayoutRequest { Layout = template.Layout, Radius = record.Radius, Gap = Gap };
			if (Clearings != null)
			{
				request.Clearings.AddRange(Clearings);
			}
			int slots = template.Pieces.Count;
			var styles = new string[slots];
			for (int s = 0; s < slots; s++)
			{
				PointOfInterestPieceSlot slot = template.Pieces[s];
				if (slot == null)
				{
					request.Slots.Add(new PointOfInterestLayoutSlot { Role = PointOfInterestSlotRole.Scatter });
					continue;
				}
				styles[s] = string.IsNullOrWhiteSpace(slot.Style) ? context.Style : slot.Style;
				PointOfInterestSlotRole role = PointOfInterestLayouts.RoleFor(template.Layout, s, slot.Role);
				int count = CountFor(slot, record.SizeClass, CountsFollowSize, random);
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, styles[s], 0));
				request.Slots.Add(new PointOfInterestLayoutSlot { Role = role, Footprint = metrics.Footprint, Module = metrics.Module, Count = count });
			}
			var draws = new List<Draws>();
			for (int s = 0; s < slots; s++)
			{
				PointOfInterestPieceSlot slot = template.Pieces[s];
				if (slot == null || PointOfInterestLayouts.Generates(request.Slots[s].Role))
				{
					continue;
				}
				for (int k = 0; k < request.Slots[s].Count; k++)
				{
					Draws d = Take(random, slot, false);
					PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(new PointOfInterestPieceRequest(slot.Tag, styles[s], d.Seed, d.Decay));
					request.Items.Add(new PointOfInterestLayoutItem { Slot = s, Footprint = metrics.Footprint * d.Scale });
					draws.Add(d);
				}
			}
			float pierDeck = record.Position.y;
			Crossing crossing = default;
			if (request.Slots.Exists(s => s.Role == PointOfInterestSlotRole.Span))
			{
				crossing = MeasureCrossing(context);
				request.SpanFrom = crossing.From;
				request.SpanTo = crossing.To;
				request.SpanHalf = Mathf.Max(-crossing.From, crossing.To);
			}
			if (request.Slots.Exists(s => s.Role == PointOfInterestSlotRole.Pier))
			{
				request.ShoreAhead = MeasureShore(context, out pierDeck);
			}

			// The ways that come in: a walled site opens its gates toward them.
			if (context.Points != null && context.Points.Paths != null)
			{
				foreach (ScenePath path in context.Points.Paths)
				{
					if (path == null || path.Class == ScenePathClass.Street)
					{
						continue;
					}
					if (path.FromId == record.Id && !float.IsNaN(path.FromEntrance) && !request.Entrances.Contains(path.FromEntrance))
					{
						request.Entrances.Add(path.FromEntrance);
					}
					if (path.ToId == record.Id && !float.IsNaN(path.ToEntrance) && !request.Entrances.Contains(path.ToEntrance))
					{
						request.Entrances.Add(path.ToEntrance);
					}
				}
				request.Entrances.Sort();
			}

			List<PointOfInterestLayoutPiece> pieces = PointOfInterestLayouts.Place(request, random);
			AddStreets(context, request);
			var grounds = new List<(Vector3 at, Quaternion turn, Vector2 size, PointOfInterestSlotRole role, string tag)>();
			int laid = 0, missing = 0, ruined = 0, wet = 0;
			bool dryOnly = KeepOutOfWater && !PointOfInterestKinds.Info(record.Kind).Has(PointOfInterestTraits.Underwater);
			float sea = dryOnly ? SeaLevelOf(context) : float.NegativeInfinity;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				PointOfInterestPieceSlot slot = template.Pieces[piece.Slot];
				Draws d = piece.Item >= 0 ? draws[piece.Item] : Take(random, slot, true);
				if (d.LeaveOut < d.Decay * 0.5f)
				{
					ruined++;
					continue;
				}
				Vector3 at = context.OnGround(piece.Offset);
				bool deck = piece.Role == PointOfInterestSlotRole.Span || piece.Role == PointOfInterestSlotRole.Pier;
				if (dryOnly && !deck && !StandsInWater(slot.Tag) && WaterLevelAt(context, at.x, at.z, sea) > at.y + 0.25f)
				{
					wet++;
					continue;
				}
				StructureFinish finish = FinishFor(Finish, FinishChance, record.Kind, biome, d.Decay, d.FinishRoll);
				var pieceRequest = new PointOfInterestPieceRequest(slot.Tag, styles[piece.Slot], d.Seed, d.Decay, finish, allowLocal);
				GameObject prefab = PointOfInterestPieceResolver.Resolve(pieceRequest);
				if (prefab == null)
				{
					missing++;
					continue;
				}
				PointOfInterestPieceMetrics metrics = PointOfInterestPieceResolver.Measure(pieceRequest);
				Quaternion pitch = Quaternion.identity;
				if (piece.Role == PointOfInterestSlotRole.Span)
				{
					// On the deck's profile, pitched to its own stretch of it: its two ends on the profile, so the modules
					// meet end to end, level over the water and ramping down to each bank.
					float half = piece.Footprint.y * 0.5f;
					float near = crossing.DeckAt(piece.Offset.y - half), far = crossing.DeckAt(piece.Offset.y + half);
					at.y = 0.5f * (near + far);
					pitch = Quaternion.Euler(-Mathf.Atan2(far - near, 2f * half) * Mathf.Rad2Deg, 0f, 0f);
				}
				else if (piece.Role == PointOfInterestSlotRole.Pier)
				{
					at.y = pierDeck;
				}
				else
				{
					at.y = SeatHeight(context, piece.Offset, piece.Yaw, metrics.Footprint * d.Scale, metrics.Depth * d.Scale) - d.Decay * MaxSinkMetres;
				}
				float tilt = deck ? 0f : d.Decay * MaxTiltDegrees;
				Quaternion rotation = Quaternion.Euler(d.TiltX * tilt, record.Yaw + piece.Yaw, d.TiltZ * tilt) * pitch;
				Vector3 scale = Vector3.one * d.Scale;
				if (piece.Role == PointOfInterestSlotRole.Span)
				{
					// A span is fitted end to end: its own size, stretched along the deck to the module the layout gave it.
					float module = metrics.Module > 0f ? metrics.Module : Mathf.Max(0.01f, metrics.Footprint.y);
					scale = new Vector3(1f, 1f, piece.Footprint.y / module);
				}
				context.AddProp(prefab, at, rotation, scale);
				grounds.Add((at, Quaternion.Euler(0f, record.Yaw + piece.Yaw, 0f), metrics.Footprint * d.Scale, piece.Role, slot.Tag));
				laid++;
			}
			AddYards(context, grounds);
			AddClutter(context, grounds, request, random);
			int dropped = request.Items.Count;
			foreach (PointOfInterestLayoutPiece piece in pieces)
			{
				if (piece.Item >= 0)
				{
					dropped--;
				}
			}
			if (missing > 0)
			{
				context.Notes?.Add($"{record.Name}: {missing} piece(s) of '{template.name}' have no art yet (laid {laid}, {ruined} left out as ruin).");
			}
			if (dropped > 0)
			{
				context.Notes?.Add($"{record.Name}: {dropped} piece(s) of '{template.name}' found no room in its {record.Radius:0} m footprint.");
			}
			if (wet > 0)
			{
				context.Notes?.Add($"{record.Name}: {wet} piece(s) of '{template.name}' left out where its footprint is under water.");
			}
		}

		/// <summary>Pieces built to stand in water: stilts, piers, bridges.</summary>
		private static bool StandsInWater(string tag)
			=> !string.IsNullOrEmpty(tag) && (tag.Contains("stilt") || tag.Contains("dock") || tag.Contains("bridge") || tag.Contains("Stilt") || tag.Contains("Pier") || tag.Contains("Bridge"));

		/// <summary>The water's surface over a point (river, lake, or the sea where the ground is below it); negative infinity on dry land.</summary>
		private static float WaterLevelAt(PointOfInterestSiteContext context, float x, float z, float sea)
		{
			float level = context.Water != null ? context.Water.SurfaceAt(x, z) : float.NegativeInfinity;
			return Mathf.Max(level, sea);
		}

		/// <summary>A slot's count: its whole range, or the third of it the size class names (0 small … 2 large).</summary>
		public static int CountFor(PointOfInterestPieceSlot slot, int sizeClass, bool followSize, DeterministicRNG random)
		{
			int min = Math.Max(0, slot.MinCount), max = Math.Max(min, slot.MaxCount);
			if (followSize && max > min)
			{
				int size = Mathf.Clamp(sizeClass, 0, 2);
				float third = (max - min) / 3f;
				int lo = min + Mathf.RoundToInt(third * size), hi = min + Mathf.RoundToInt(third * (size + 1));
				min = Mathf.Clamp(lo, slot.MinCount, max);
				max = Mathf.Clamp(hi, min, max);
			}
			return random.Range(min, max + 1);
		}

		private static readonly string[] Wet = { "Swamp", "Mangrove", "Bog", "Wetland", "Marsh", "Estuary" };
		private static readonly string[] Damp = { "Forest", "Woodland", "Jungle", "Rainforest", "Taiga", "Bamboo", "Valley", "Karst", "Temperate", "Lake", "River" };
		private static readonly string[] Dry = { "Desert", "Badlands", "Salt", "Arid", "Dust", "Wasteland", "Savanna", "Scrub", "Volcanic", "Molten", "Sulphur", "Oasis" };

		/// <summary>
		/// A piece's finish. Auto: algae on everything under water and on most of a swamp's pieces (more the more worn);
		/// char on half a battlefield; nothing in dry country; else moss on worn pieces, more in damp country.
		/// A forced finish is laid on <paramref name="chance"/> of the pieces. Pure.
		/// </summary>
		public static StructureFinish FinishFor(PointOfInterestFinish mode, float chance, POIType kind, string biome, float decay, float roll)
		{
			switch (mode)
			{
				case PointOfInterestFinish.None:
					return StructureFinish.None;
				case PointOfInterestFinish.Mossy:
					return roll < chance ? StructureFinish.Mossy : StructureFinish.None;
				case PointOfInterestFinish.Charred:
					return roll < chance ? StructureFinish.Charred : StructureFinish.None;
				case PointOfInterestFinish.Algae:
					return roll < chance ? StructureFinish.Algae : StructureFinish.None;
			}
			if (PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater))
			{
				return StructureFinish.Algae;
			}
			if (kind == POIType.Battlefield && roll < 0.5f)
			{
				return StructureFinish.Charred;
			}
			biome ??= string.Empty;
			if (Mentions(biome, Wet))
			{
				return roll < 0.3f + 0.6f * decay ? StructureFinish.Algae : StructureFinish.None;
			}
			if (Mentions(biome, Dry))
			{
				return StructureFinish.None;
			}
			float moss = decay * (Mentions(biome, Damp) ? 1f : 0.5f);
			PointOfInterestGroup group = PointOfInterestKinds.Info(kind).Group;
			if (group == PointOfInterestGroup.Ruins || group == PointOfInterestGroup.Dead)
			{
				// Long abandoned: the moss has had the stones for years (2026-10-11: ruins looked freshly built).
				moss = Mathf.Max(moss, 0.55f + 0.4f * decay);
			}
			return roll < moss ? StructureFinish.Mossy : StructureFinish.None;
		}

		private static bool Mentions(string biome, string[] words)
		{
			foreach (string word in words)
			{
				if (biome.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Where a ground piece's origin goes: its centre's ground, lowered until its foundation (<paramref name="depth"/>)
		/// reaches the lowest corner, so a slope shows plinth, never a gap.
		/// </summary>
		public static float SeatHeight(PointOfInterestSiteContext context, Vector2 offset, float yaw, Vector2 footprint, float depth)
		{
			float centre = context.OnGround(offset).y;
			var corners = new Vector2[4];
			PointOfInterestLayouts.Corners(offset, yaw, footprint, corners);
			float lowest = centre;
			foreach (Vector2 corner in corners)
			{
				lowest = Mathf.Min(lowest, context.OnGround(corner).y);
			}
			return Mathf.Min(centre, lowest + Mathf.Max(0f, depth) * 0.9f);
		}

		/// <summary>
		/// A bridge's deck along its heading: where it starts and ends, and its height everywhere between. Level at the
		/// water's clearance over the water, ramping down each side onto that bank's ground at the end, never under the
		/// straight line from end to end. Each span module is pitched to its own stretch of it (rotation, not steps: Jim,
		/// 2026-10-11), so both ends sit on their banks whatever their heights and the middle clears the water.
		/// </summary>
		public struct Crossing
		{
			/// <summary>Metres along the heading (signed) of the start and the end.</summary>
			public float From, To;
			/// <summary>World height of the deck at the start and at the end: the ground there.</summary>
			public float DeckFrom, DeckTo;
			/// <summary>Metres along the heading where the water (or a dry channel's bed) begins and ends.</summary>
			public float WaterFrom, WaterTo;
			/// <summary>The deck's height over the water: its surface and the clearance. NaN for none (a plain straight deck).</summary>
			public float Level;
			/// <summary>How steeply each ramp comes down from <see cref="Level"/> to its end (rise per metre).</summary>
			public float RampFrom, RampTo;

			/// <summary>The deck's height at a distance along the heading.</summary>
			public float DeckAt(float along)
			{
				float line = To > From ? Mathf.Lerp(DeckFrom, DeckTo, Mathf.Clamp01((along - From) / (To - From))) : DeckFrom;
				if (float.IsNaN(Level))
				{
					return line;
				}
				float raised = along < WaterFrom ? Level - (WaterFrom - along) * RampFrom
					: along > WaterTo ? Level - (along - WaterTo) * RampTo
					: Level;
				return Mathf.Max(line, raised);
			}

			/// <summary>The deck's pitch between two distances along it, degrees, rising toward <see cref="To"/>.</summary>
			public float SlopeDegreesBetween(float a, float b) => b > a ? Mathf.Atan2(DeckAt(b) - DeckAt(a), b - a) * Mathf.Rad2Deg : 0f;

			/// <summary>The straight line's slope end to end, degrees.</summary>
			public float SlopeDegrees => To > From ? Mathf.Atan2(DeckTo - DeckFrom, To - From) * Mathf.Rad2Deg : 0f;
		}

		/// <summary>The steepest the deck's line runs from one end to the other between banks of different heights, degrees.</summary>
		public const float MaxDeckSlopeDegrees = 20f;

		/// <summary>The gentlest a ramp comes down from the deck over the water to a low bank (rise per metre): about 14°.</summary>
		public const float MinRampGrade = 0.25f;

		/// <summary>Clearance the deck keeps over the water, metres.</summary>
		public const float DeckClearance = 0.6f;

		/// <summary>
		/// The bridge's line across the water at this site: from where each bank first stands clear of the water by
		/// <see cref="DeckClearance"/>, carried on up a steep channel wall to its lip (never along a gently rising bank), to
		/// the same on the other side; a bank too low ever to clear the water ends a few metres past its edge. Both ends
		/// sit on their ground; the deck over the water stands at the clearance and ramps down to each end
		/// (<see cref="Crossing"/>).
		/// </summary>
		/// <remarks>
		/// <para>
		/// History. It first stopped at the first dry ground and laid one flat deck at the site's own height: one end buried,
		/// one in the air. Then it ran each end out to the bank's top (2026-10-10), which on a lowland river's long, gentle
		/// banks ran the deck far over the grass (2026-10-11). Then one straight deck from end to end, which over a low bank
		/// had to stand that end up in the air to clear the water ("aligns on one side and stands on the other", Jim,
		/// 2026-10-11): the deck is a profile now, and each module takes its own pitch.
		/// </para>
		/// <para>A dry channel (no water at the site) is measured from its bed.</para>
		/// </remarks>
		public static Crossing MeasureCrossing(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			SceneWater water = context.Water;
			float level = water != null ? water.SurfaceAt(record.Position.x, record.Position.z) : float.NegativeInfinity;
			if (float.IsNegativeInfinity(level))
			{
				level = context.OnGround(Vector2.zero).y;
			}
			return MeasureCrossing(t => context.OnGround(new Vector2(0f, t)).y, level, Mathf.Max(8f, record.Radius * 2f));
		}

		/// <summary>The steepest a bank may climb (rise per metre) and still be the channel's wall the deck reaches over.</summary>
		public const float ChannelWallGrade = 0.6f;

		/// <summary>
		/// <see cref="MeasureCrossing(PointOfInterestSiteContext)"/> on a profile: the ground's height at a signed distance
		/// along the heading, and the water's surface (or a dry bed's height).
		/// </summary>
		public static Crossing MeasureCrossing(Func<float, float> groundAlong, float waterLevel, float reach)
		{
			const float Step = 0.5f, MaxWallRun = 6f, Bearing = 0.5f, LowBankRun = 3f;
			float need = waterLevel + DeckClearance;
			var ends = new float[2];
			var decks = new float[2];
			var lips = new float[2];
			var edges = new float[2];
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				// The water's edge: the first ground above it.
				float edge = Step;
				while (edge < reach && groundAlong(side * edge) <= waterLevel + 0.05f)
				{
					edge += Step;
				}
				edges[i] = edge;
				float t = edge;
				bool clear = false;
				for (; t <= reach; t += Step)
				{
					if (groundAlong(side * t) >= need)
					{
						clear = true;
						break;
					}
				}
				if (!clear)
				{
					// A bank too low ever to clear the water: the deck comes down onto it a few metres past the edge.
					t = Mathf.Min(reach, edge + LowBankRun);
				}
				else
				{
					// Up a steep wall to its lip, not along a gentle rise.
					for (float run = 0f; t + Step <= reach && run < MaxWallRun && groundAlong(side * (t + Step)) - groundAlong(side * t) > ChannelWallGrade * Step; run += Step)
					{
						t += Step;
					}
				}
				lips[i] = t;
				ends[i] = t + Bearing;
				decks[i] = groundAlong(side * t);
			}

			// Not too steep end to end: the higher end comes back down its wall (still clear of the water) until it is not.
			float tan = Mathf.Tan(MaxDeckSlopeDegrees * Mathf.Deg2Rad);
			for (int guard = 0; guard < 64; guard++)
			{
				float length = ends[0] + ends[1];
				float rise = decks[1] - decks[0];
				if (Mathf.Abs(rise) <= tan * length)
				{
					break;
				}
				int high = rise > 0f ? 1 : 0;
				float side = high == 0 ? -1f : 1f;
				float t = lips[high] - Step;
				if (t < edges[high] || groundAlong(side * t) < need)
				{
					// No lower footing that clears the water: the deck rotates to meet both ends as they are.
					break;
				}
				lips[high] = t;
				ends[high] = t + Bearing;
				decks[high] = groundAlong(side * t);
			}

			var crossing = new Crossing
			{
				From = -ends[0],
				To = ends[1],
				DeckFrom = decks[0],
				DeckTo = decks[1],
				WaterFrom = -edges[0],
				WaterTo = edges[1],
				Level = need,
			};
			// Each ramp exactly long enough to bring the deck down from over the water onto its end.
			crossing.RampFrom = Mathf.Max(MinRampGrade, (need - decks[0]) / Mathf.Max(0.5f, ends[0] - edges[0]));
			crossing.RampTo = Mathf.Max(MinRampGrade, (need - decks[1]) / Mathf.Max(0.5f, ends[1] - edges[1]));
			return crossing;
		}

		/// <summary>
		/// How far ahead of the site's centre the water begins (river, lake or sea), metres, and the deck a pier there
		/// stands at (a metre over the water); NaN when there is no water within two and a half radii ahead.
		/// </summary>
		public static float MeasureShore(PointOfInterestSiteContext context, out float deck)
		{
			PointOfInterestRecord record = context.Record;
			deck = record.Position.y;
			float sea = SeaLevelOf(context);
			float reach = Mathf.Max(10f, record.Radius * 2.5f);
			for (float t = 0f; t <= reach; t += 1f)
			{
				Vector3 at = context.OnGround(new Vector2(0f, t));
				float level = context.Water != null ? context.Water.SurfaceAt(at.x, at.z) : float.NegativeInfinity;
				if (float.IsNegativeInfinity(level) && !float.IsNegativeInfinity(sea) && at.y < sea)
				{
					level = sea;
				}
				if (!float.IsNegativeInfinity(level) && level > at.y + 0.3f)
				{
					deck = level + 1f;
					return t;
				}
			}
			return float.NaN;
		}

		/// <summary>The scene's sea level, scene metres; negative infinity for a scene with no sea.</summary>
		private static float SeaLevelOf(PointOfInterestSiteContext context)
		{
			if (context.Water != null && !float.IsNegativeInfinity(context.Water.SeaLevel))
			{
				return context.Water.SeaLevel;
			}
			SceneGenerationRequest request = context.Request;
			if (request == null || request.Body == null || (request.Layer != null && request.Layer.Underground))
			{
				return float.NegativeInfinity;
			}
			SurfaceLiquid liquid = SurfaceLiquids.For(SolarSystemProfile.Resolve(request.Body), request.Body, out float level);
			return liquid != SurfaceLiquid.None ? level * request.VerticalScale : float.NegativeInfinity;
		}
	}
}
#endif
