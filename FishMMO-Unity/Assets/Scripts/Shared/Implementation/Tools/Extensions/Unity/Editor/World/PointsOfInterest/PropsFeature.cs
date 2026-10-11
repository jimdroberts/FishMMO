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
				int n = Math.Max(1, Mathf.CeilToInt((to - from) / module - 1e-3f));
				float middle = 0.5f * (from + to);
				for (int i = 0; i < n; i++)
				{
					var at = new Vector2(0f, middle + (i - (n - 1) * 0.5f) * module);
					TryLay(s, -1, at, 0f, new Vector2(slot.Footprint.x, module), 0f, SpanGroup);
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
					// On the line from one bank top to the other, sloped with it: each end meets its own bank.
					at.y = crossing.DeckAt(piece.Offset.y);
					pitch = Quaternion.Euler(-crossing.SlopeDegrees, 0f, 0f);
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
				context.AddProp(prefab, at, rotation, Vector3.one * d.Scale);
				laid++;
			}
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

		/// <summary>A bridge's line across its water: where it starts and ends along the heading and its deck at each end.</summary>
		public struct Crossing
		{
			/// <summary>Metres along the heading (signed) of the start and the end.</summary>
			public float From, To;
			/// <summary>World height of the deck at the start and at the end.</summary>
			public float DeckFrom, DeckTo;

			/// <summary>The deck's height at a distance along the heading, on the straight line between the ends.</summary>
			public float DeckAt(float along) => To > From ? Mathf.Lerp(DeckFrom, DeckTo, Mathf.Clamp01((along - From) / (To - From))) : DeckFrom;

			/// <summary>The deck's slope, degrees, rising toward <see cref="To"/>.</summary>
			public float SlopeDegrees => To > From ? Mathf.Atan2(DeckTo - DeckFrom, To - From) * Mathf.Rad2Deg : 0f;
		}

		/// <summary>The steepest slope a bridge's deck takes between banks of different heights, degrees.</summary>
		public const float MaxDeckSlopeDegrees = 12f;

		/// <summary>Clearance the deck keeps over the water at the crossing's lowest point, metres.</summary>
		public const float DeckClearance = 0.6f;

		/// <summary>
		/// The bridge's line across the water at this site (Jim, 2026-10-10: bridges did not meet the terrain). Out from the
		/// centre along the heading each way it finds the bank's top (past the water, up the carved channel side while the
		/// ground still climbs). The deck is level at the LOWER top, so a hillside beyond one bank is not climbed, and each
		/// end is where that side's ground first reaches the deck, plus a metre's bearing: both ends meet the ground. A bank
		/// too low to reach the water's <see cref="DeckClearance"/> brings its end down to it, sloped to at most
		/// <see cref="MaxDeckSlopeDegrees"/>.
		/// </summary>
		/// <remarks>
		/// It used to stop at the first dry ground past the water's edge, part way up the channel side, and lay one flat deck
		/// at the site's own ground: the high bank swallowed one end and the other hung in the air.
		/// </remarks>
		public static Crossing MeasureCrossing(PointOfInterestSiteContext context)
		{
			PointOfInterestRecord record = context.Record;
			SceneWater water = context.Water;
			float centreLevel = water != null ? water.SurfaceAt(record.Position.x, record.Position.z) : float.NegativeInfinity;
			float reach = Mathf.Max(6f, record.Radius * 2f);
			const float Step = 0.5f;
			int samples = Mathf.Max(1, Mathf.FloorToInt(reach / Step));
			var ground = new float[2, samples];
			var wet = new bool[2, samples];
			var top = new float[2];
			var topT = new float[2];
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				bool dryYet = false, climbing = true;
				float firstDry = float.NegativeInfinity;
				topT[i] = Mathf.Min(reach, Mathf.Max(2f, record.Radius));
				top[i] = float.NegativeInfinity;
				for (int k = 0; k < samples; k++)
				{
					float t = (k + 1) * Step;
					Vector3 at = context.OnGround(new Vector2(0f, side * t));
					float level = water != null ? water.SurfaceAt(at.x, at.z) : float.NegativeInfinity;
					ground[i, k] = at.y;
					wet[i, k] = !float.IsNegativeInfinity(level) && at.y <= level + 0.3f;
					if (wet[i, k] || !climbing)
					{
						continue;
					}
					/* The bank's top: past the water, up the carved channel side while it still climbs (over 0.15 m a step).
					 * Flat ground before the climb starts (a dry channel's floor, a strip of shore) does not end the search. */
					if (!dryYet)
					{
						dryYet = true;
						firstDry = at.y;
						top[i] = at.y;
						topT[i] = t;
					}
					else if (at.y - top[i] > 0.15f || top[i] - firstDry < 0.3f && at.y >= top[i] - 0.05f)
					{
						if (at.y > top[i])
						{
							top[i] = at.y;
							topT[i] = t;
						}
					}
					else
					{
						climbing = false;
					}
				}
				if (float.IsNegativeInfinity(top[i]))
				{
					top[i] = context.OnGround(new Vector2(0f, side * topT[i])).y;
				}
			}

			// One level for the deck: the lower bank's top (a hillside beyond the other bank is not followed up), never under
			// the water's clearance. Each end is where its own ground first reaches that level, plus a metre's bearing.
			float level0 = Mathf.Min(top[0], top[1]);
			if (!float.IsNegativeInfinity(centreLevel))
			{
				level0 = Mathf.Max(level0, centreLevel + DeckClearance);
			}
			var ends = new float[2];
			var decks = new float[2];
			for (int i = 0; i < 2; i++)
			{
				float side = i == 0 ? -1f : 1f;
				float t = topT[i];
				float deck = top[i];
				bool past = false;
				for (int k = 0; k < samples; k++)
				{
					if (wet[i, k])
					{
						past = true;
						continue;
					}
					if ((past || k > 0) && ground[i, k] >= level0 - 0.05f)
					{
						t = (k + 1) * Step;
						deck = level0;
						// Up to a metre's bearing onto the bank, but only while it stays near the deck: never dug into a steep bank.
						for (int j = k + 1; j < samples && j <= k + 2 && ground[i, j] <= level0 + 0.3f; j++)
						{
							t = (j + 1) * Step;
						}
						break;
					}
				}
				ends[i] = side * t;
				// Where the ground never reaches the level (a low bank), the end comes down onto it.
				decks[i] = Mathf.Min(deck, level0);
			}
			var crossing = new Crossing { From = ends[0], To = ends[1], DeckFrom = decks[0], DeckTo = decks[1] };

			// Never steeper than a footbridge: past it the lower end comes up (it then stands on its trestle or a short gap).
			float length = Mathf.Max(1f, crossing.To - crossing.From);
			float rise = Mathf.Tan(MaxDeckSlopeDegrees * Mathf.Deg2Rad) * length;
			if (crossing.DeckTo - crossing.DeckFrom > rise)
			{
				crossing.DeckFrom = crossing.DeckTo - rise;
			}
			else if (crossing.DeckFrom - crossing.DeckTo > rise)
			{
				crossing.DeckTo = crossing.DeckFrom - rise;
			}
			// Clear of the water at the middle, where the river runs (the site's centre is on it).
			if (!float.IsNegativeInfinity(centreLevel))
			{
				float lack = centreLevel + DeckClearance - crossing.DeckAt(0f);
				if (lack > 0f)
				{
					crossing.DeckFrom += lack;
					crossing.DeckTo += lack;
				}
			}
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
