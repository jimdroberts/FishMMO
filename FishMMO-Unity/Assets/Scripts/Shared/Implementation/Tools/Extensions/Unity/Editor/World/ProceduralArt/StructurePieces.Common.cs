#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The parts more than one piece is made of: roofs, walls, houses, columns, rough stones.</summary>
	public static partial class StructurePieces
	{
		/// <summary>How far below the ground line a building's plinth reaches, so it sits on a slope without a gap.</summary>
		public const float FoundationDepth = 0.5f;

		/// <summary>A gable roof's two slabs, ridge along z, over a span of <paramref name="span"/> (x) and <paramref name="length"/> (z), centred on the origin.</summary>
		internal static void GableRoof(StructureDraft d, float span, float length, float eaveY, float rise, float overhang, float thick,
			StructureMaterial material, int key, Matrix4x4 place)
		{
			float half = span * 0.5f;
			float k = rise / half;
			float slope = Mathf.Sqrt(1f + k * k);
			float vertical = thick * slope;
			float outX = half + overhang, outY = eaveY - k * overhang;
			float ridgeY = eaveY + rise;
			float z0 = -length * 0.5f - overhang, z1 = length * 0.5f + overhang;
			for (int side = -1; side <= 1; side += 2)
			{
				var outline = new[]
				{
					new Vector2(side * outX, outY),
					new Vector2(0f, ridgeY),
					new Vector2(0f, ridgeY + vertical),
					new Vector2(side * outX, outY + vertical),
				};
				StructureSolid slab = StructureShapes.Prism(outline, z0, z1, material, key + (side > 0 ? 1 : 0), StructureRole.Roof);
				// Thatch runs down the slope, shingles and planks along the eave.
				slab.Grain = material == StructureMaterial.Thatch ? new Vector3(side, -k, 0f).normalized : Vector3.forward;
				d.Add(slab.Transform(place));
			}
		}

		/// <summary>The triangle of wall under a gable, ridge along z, at z = <paramref name="z"/>.</summary>
		internal static void Gable(StructureDraft d, float span, float eaveY, float rise, float z, float thick, StructureMaterial material, int key, Matrix4x4 place)
		{
			float half = span * 0.5f;
			var outline = new[] { new Vector2(-half, eaveY), new Vector2(half, eaveY), new Vector2(0f, eaveY + rise) };
			d.Add(StructureShapes.Prism(outline, z - thick * 0.5f, z + thick * 0.5f, material, key).Transform(place));
		}

		/// <summary>
		/// A gable (ridge along z, at z = <paramref name="z"/>) with a doorway's head cut out of it: the triangle less the
		/// rectangle from <paramref name="x0"/> to <paramref name="x1"/> up to <paramref name="doorTop"/> — a piece either
		/// side and the gable's peak over it.
		/// </summary>
		internal static void GableWithDoor(StructureDraft d, float span, float eaveY, float rise, float z, float thick, StructureMaterial material, int key,
			float x0, float x1, float doorTop)
		{
			float half = span * 0.5f;
			float Roof(float x) => eaveY + rise * (1f - Mathf.Abs(x) / half);
			x0 = Mathf.Clamp(x0, -half + 0.05f, half - 0.05f);
			x1 = Mathf.Clamp(x1, x0 + 0.05f, half - 0.05f);
			float z0 = z - thick * 0.5f, z1 = z + thick * 0.5f;
			d.Add(StructureShapes.Prism(new[] { new Vector2(-half, eaveY), new Vector2(x0, eaveY), new Vector2(x0, Roof(x0)) }, z0, z1, material, key));
			d.Add(StructureShapes.Prism(new[] { new Vector2(x1, eaveY), new Vector2(half, eaveY), new Vector2(x1, Roof(x1)) }, z0, z1, material, PieceKey(key, 0)));
			float headTop = Mathf.Min(Roof(x0), Roof(x1));
			if (doorTop < headTop - 0.02f)
			{
				var peak = new List<Vector2> { new Vector2(x0, doorTop), new Vector2(x1, doorTop), new Vector2(x1, Roof(x1)) };
				if (x0 < 0f && x1 > 0f)
				{
					peak.Add(new Vector2(0f, eaveY + rise));
				}
				peak.Add(new Vector2(x0, Roof(x0)));
				d.Add(StructureShapes.Prism(peak, z0, z1, material, PieceKey(key, 1)));
			}
		}

		/// <summary>Four walls round a rectangle: front and back the full width, the sides between them.</summary>
		internal static void Walls(StructureDraft d, Vector3 centre, float width, float depth, float thick, float y0, float y1, StructureMaterial material, int key)
		{
			float h = y1 - y0, ym = (y0 + y1) * 0.5f;
			d.Add(StructureShapes.Box(centre + new Vector3(0f, ym, -depth * 0.5f + thick * 0.5f), new Vector3(width, h, thick), material, key));
			d.Add(StructureShapes.Box(centre + new Vector3(0f, ym, depth * 0.5f - thick * 0.5f), new Vector3(width, h, thick), material, key + 1));
			d.Add(StructureShapes.Box(centre + new Vector3(-width * 0.5f + thick * 0.5f, ym, 0f), new Vector3(thick, h, depth - 2f * thick), material, key + 2));
			d.Add(StructureShapes.Box(centre + new Vector3(width * 0.5f - thick * 0.5f, ym, 0f), new Vector3(thick, h, depth - 2f * thick), material, key + 3));
		}

		/// <summary>A hole through a wall: its centre along the wall from the wall's start, its width, and its sill and head heights.</summary>
		internal struct WallOpening
		{
			public float Along, Width, Bottom, Top;
		}

		/// <summary>
		/// A straight wall from <paramref name="a"/> to <paramref name="b"/> (the ends of its centre line, heights ignored),
		/// <paramref name="thick"/> thick, from <paramref name="y0"/> to <paramref name="y1"/>, with holes cut through it: the
		/// wall is the closed boxes round them — piers between the holes, sills under them, heads over them. The holes are
		/// at every level, since the last level is the collider (a doorway a player walks through).
		/// </summary>
		internal static void WallRun(StructureDraft d, Vector3 a, Vector3 b, float thick, float y0, float y1, List<WallOpening> openings,
			StructureMaterial material, int key)
		{
			Vector3 run = new Vector3(b.x - a.x, 0f, b.z - a.z);
			float length = run.magnitude;
			if (length < 1e-3f)
			{
				return;
			}
			Vector3 dir = run / length;
			Quaternion turn = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);
			Vector3 start = new Vector3(a.x, 0f, a.z);
			void Piece(float from, float to, float bottom, float top, int k)
			{
				if (to - from < 1e-3f || top - bottom < 1e-3f)
				{
					return;
				}
				Vector3 centre = start + dir * ((from + to) * 0.5f) + Vector3.up * ((bottom + top) * 0.5f);
				d.Add(StructureShapes.Block(centre, new Vector3(to - from, top - bottom, thick), turn, material, k));
			}
			if (openings == null || openings.Count == 0)
			{
				Piece(0f, length, y0, y1, key);
				return;
			}
			openings.Sort((x, y) => x.Along.CompareTo(y.Along));
			float at = 0f;
			int n = 0;
			foreach (WallOpening o in openings)
			{
				float left = Mathf.Clamp(o.Along - o.Width * 0.5f, at, length), right = Mathf.Clamp(o.Along + o.Width * 0.5f, left, length);
				Piece(at, left, y0, y1, PieceKey(key, n++));
				Piece(left, right, y0, Mathf.Clamp(o.Bottom, y0, y1), PieceKey(key, n++));
				Piece(left, right, Mathf.Clamp(o.Top, y0, y1), y1, PieceKey(key, n++));
				at = right;
			}
			Piece(at, length, y0, y1, PieceKey(key, n));
		}

		/// <summary>A key for the n-th piece a cut wall is split into, apart from every other part's.</summary>
		private static int PieceKey(int key, int n) => 100000 + key * 32 + n;

		/// <summary>
		/// Four walls round a rectangle about <paramref name="centre"/> (front toward −z) with one doorway through the front:
		/// <paramref name="doorX"/> across, <paramref name="doorWidth"/> wide, from <paramref name="doorBottom"/> to
		/// <paramref name="doorTop"/>.
		/// </summary>
		internal static void WallsWithDoor(StructureDraft d, Vector3 centre, float width, float depth, float thick, float y0, float y1,
			float doorX, float doorWidth, float doorBottom, float doorTop, StructureMaterial material, int key)
		{
			float hw = width * 0.5f, hd = depth * 0.5f, t = thick * 0.5f;
			var hole = new List<WallOpening> { new WallOpening { Along = doorX + hw, Width = doorWidth, Bottom = doorBottom, Top = doorTop } };
			WallRun(d, centre + new Vector3(-hw, 0f, -hd + t), centre + new Vector3(hw, 0f, -hd + t), thick, y0, y1, hole, material, key);
			WallRun(d, centre + new Vector3(-hw, 0f, hd - t), centre + new Vector3(hw, 0f, hd - t), thick, y0, y1, null, material, key + 1);
			WallRun(d, centre + new Vector3(-hw + t, 0f, -hd + thick), centre + new Vector3(-hw + t, 0f, hd - thick), thick, y0, y1, null, material, key + 2);
			WallRun(d, centre + new Vector3(hw - t, 0f, -hd + thick), centre + new Vector3(hw - t, 0f, hd - thick), thick, y0, y1, null, material, key + 3);
		}

		/// <summary>
		/// A round wall with a doorway: as <see cref="RingWall"/>, but the blocks are cut away radially either side of
		/// <paramref name="doorAngle"/> so the hole is <paramref name="doorWidth"/> wide at the inner face, the wall over it
		/// from <paramref name="doorTop"/> up. One block is centred on the door, so the hole is the same at every level.
		/// </summary>
		internal static void RingWallWithDoor(StructureDraft d, Vector3 centre, float inner, float outer, float y0, float y1, int segments,
			float doorAngle, float doorWidth, float doorTop, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			RingWallWithOpenings(d, centre, inner, outer, y0, y1, segments, doorAngle,
				new List<(float, float, float, float)> { (doorAngle, doorWidth, y0, doorTop) }, material, key, role);
		}

		/// <summary>
		/// A round wall with holes through it, each (angle, width at the inner face, bottom, top); one block is centred on
		/// <paramref name="phaseAngle"/> (the main door's), so that hole is the same at every level.
		/// </summary>
		internal static void RingWallWithOpenings(StructureDraft d, Vector3 centre, float inner, float outer, float y0, float y1, int segments, float phaseAngle,
			List<(float angle, float width, float bottom, float top)> holes, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			float stepA = 2f * Mathf.PI / segments;
			float phaseA = phaseAngle - stepA * 0.5f;
			for (int k = 0; k < segments; k++)
			{
				float a0 = phaseA + k * stepA, a1 = a0 + stepA;
				// The cuts in this block, along it: where each hole starts and stops, and its sill and head.
				var cuts = new List<(float lo, float hi, float bottom, float top)>();
				foreach ((float angle, float width, float bottom, float top) in holes)
				{
					float halfA = Mathf.Asin(Mathf.Clamp(width * 0.5f / Mathf.Max(0.1f, inner), 0f, 0.95f));
					foreach (float shift in new[] { 0f, 2f * Mathf.PI, -2f * Mathf.PI, 4f * Mathf.PI, -4f * Mathf.PI })
					{
						float l = Mathf.Max(a0, angle - halfA + shift), h = Mathf.Min(a1, angle + halfA + shift);
						if (h > l)
						{
							cuts.Add((l, h, bottom, top));
						}
					}
				}
				if (cuts.Count == 0)
				{
					d.Add(RingBlock(centre, inner, outer, y0, y1, a0, a1, material, key + k, role));
					continue;
				}
				cuts.Sort((x, y) => x.lo.CompareTo(y.lo));
				float at = a0;
				int n = 0;
				foreach ((float lo, float hi, float bottom, float top) in cuts)
				{
					if (lo - at > 1e-4f)
					{
						d.Add(RingBlock(centre, inner, outer, y0, y1, at, lo, material, PieceKey(key, k * 8 + n++), role));
					}
					if (bottom - y0 > 1e-3f)
					{
						d.Add(RingBlock(centre, inner, outer, y0, bottom, lo, hi, material, PieceKey(key, k * 8 + n++), role));
					}
					if (y1 - top > 1e-3f)
					{
						d.Add(RingBlock(centre, inner, outer, top, y1, lo, hi, material, PieceKey(key, k * 8 + n++), role));
					}
					at = Mathf.Max(at, hi);
				}
				if (a1 - at > 1e-4f)
				{
					d.Add(RingBlock(centre, inner, outer, y0, y1, at, a1, material, PieceKey(key, k * 8 + n), role));
				}
			}
		}

		/// <summary>Merlons along a line from <paramref name="a"/> to <paramref name="b"/> (their feet), centred, gaps between.</summary>
		internal static void Crenellate(StructureDraft d, Vector3 a, Vector3 b, float thick, float height, float merlon, float gap,
			StructureMaterial material, int key, StructureRole role = StructureRole.Detail)
		{
			Vector3 dir = b - a;
			float length = dir.magnitude;
			dir /= Mathf.Max(1e-5f, length);
			int n = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (merlon + gap)));
			float used = n * merlon + (n - 1) * gap;
			float start = (length - used) * 0.5f + merlon * 0.5f;
			Quaternion turn = Quaternion.FromToRotation(Vector3.right, new Vector3(dir.x, 0f, dir.z).normalized);
			for (int i = 0; i < n; i++)
			{
				Vector3 c = a + dir * (start + i * (merlon + gap)) + Vector3.up * (height * 0.5f);
				d.Add(StructureShapes.Block(c, new Vector3(merlon, height, thick), turn, material, key + i, role, d.Bevel(0.03f)));
			}
		}

		/// <summary>
		/// A round wall built of <paramref name="segments"/> blocks (each convex, so a ruin can break each its own way),
		/// smooth outside; with <paramref name="inner"/> zero, solid wedges meeting at the axis.
		/// </summary>
		internal static void RingWall(StructureDraft d, Vector3 centre, float inner, float outer, float y0, float y1, int segments,
			StructureMaterial material, int key, StructureRole role = StructureRole.Structure, float phase = 0f)
		{
			for (int k = 0; k < segments; k++)
			{
				float a0 = phase + k * 2f * Mathf.PI / segments, a1 = phase + (k + 1) * 2f * Mathf.PI / segments;
				d.Add(RingBlock(centre, inner, outer, y0, y1, a0, a1, material, key + k, role));
			}
		}

		/// <summary>One block of a round wall: from angle a0 to a1, radii inner..outer, smooth on its outer face.</summary>
		internal static StructureSolid RingBlock(Vector3 centre, float inner, float outer, float y0, float y1, float a0, float a1,
			StructureMaterial material, int key, StructureRole role)
		{
			var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			Vector3 up = Vector3.up;
			Vector3 i0 = centre + d0 * inner, i1 = centre + d1 * inner, o0 = centre + d0 * outer, o1 = centre + d1 * outer;
			var s = new StructureSolid { Material = material, Role = role, Key = key };
			Vector3 mid = (d0 + d1).normalized;
			StructureFace outside = StructureShapes.Face(mid, o0 + up * y0, o1 + up * y0, o1 + up * y1, o0 + up * y1);
			outside.Normals = new List<Vector3>();
			foreach (Vector3 p in outside.Points)
			{
				outside.Normals.Add(new Vector3(p.x - centre.x, 0f, p.z - centre.z).normalized);
			}
			s.Faces.Add(outside);
			s.Faces.Add(StructureShapes.Face(Vector3.up, o0 + up * y1, o1 + up * y1, i1 + up * y1, i0 + up * y1));
			s.Faces.Add(StructureShapes.Face(Vector3.down, o0 + up * y0, o1 + up * y0, i1 + up * y0, i0 + up * y0));
			Vector3 side0 = Vector3.Cross(Vector3.up, d0), side1 = Vector3.Cross(d1, Vector3.up);
			s.Faces.Add(StructureShapes.Face(side0, o0 + up * y0, o0 + up * y1, i0 + up * y1, i0 + up * y0));
			s.Faces.Add(StructureShapes.Face(side1, o1 + up * y0, o1 + up * y1, i1 + up * y1, i1 + up * y0));
			if (inner > 1e-4f)
			{
				s.Faces.Add(StructureShapes.Face(-mid, i0 + up * y0, i1 + up * y0, i1 + up * y1, i0 + up * y1));
			}
			// Faces that collapse at the axis (no inner radius) are dropped: the wedge closes on its edge there.
			s.Faces.RemoveAll(f => StructureShapes.Newell(f.Points).sqrMagnitude < 1e-12f);
			return s;
		}

		/// <summary>The house kit: everything a gabled building is, from the plinth to the chimney.</summary>
		internal struct House
		{
			public float Width, Depth, WallHeight, Rise, Overhang, Wall, Plinth, Floor;
			public StructureMaterial WallMaterial, RoofMaterial, PlinthMaterial;
			/// <summary>Half-timbering over the walls.</summary>
			public bool Frame;
			/// <summary>Ridge along x: the long side faces front, the gables left and right.</summary>
			public bool RidgeAlongX;
			public bool Chimney;
			/// <summary>Windows along the front (and as many down each side when there is room).</summary>
			public int Windows;
			public float DoorWidth, DoorHeight, DoorX;
			/// <summary>The door hung in the doorway (its leaf piece's variant).</summary>
			public StructureDoorKind DoorKind;
			/// <summary>What the ground floor is furnished as.</summary>
			public StructureRoomKind RoomKind;
			/// <summary>What the door is: timber planks (the default), or a dark opening (<see cref="StructureMaterial.Ash"/>) for a tent's flap.</summary>
			public StructureMaterial DoorMaterial;
			/// <summary>A second storey this much taller, jettied out over the first; 0 = one storey.</summary>
			public float Upper;
			/// <summary>What the upper storey is furnished as (a house's chamber unless said).</summary>
			public StructureRoomKind UpperRoomKind;
			/// <summary>The upper storey's walls when not the ground floor's (a stone ground floor under a framed plaster storey), and whether they are framed.</summary>
			public StructureMaterial? UpperWallMaterial;
			public bool? UpperFrame;
			/// <summary>How far the upper storey is jettied out over the front and back walls, metres; 0 takes <see cref="DefaultJetty"/>.</summary>
			public float Jetty;
			/// <summary>Keys from here up.</summary>
			public int Key;
		}

		/// <summary>Builds a house: plinth (none when it stands on a platform, <see cref="House.Floor"/> &gt; 0), walls, gables, roof, door, windows, frame, chimney.</summary>
		internal static void BuildHouse(StructureDraft d, House h)
		{
			int key = h.Key;
			float floor = h.Floor + h.Plinth;
			if (h.Plinth > 0f && h.Floor <= 0f)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, (h.Plinth - FoundationDepth) * 0.5f, 0f),
					new Vector3(h.Width + 0.24f, h.Plinth + FoundationDepth, h.Depth + 0.24f), d.Bevel(0.04f), h.PlinthMaterial, key));
			}
			float top = floor + h.WallHeight;
			// A real doorway (a tent's flap stays a dark panel): the door itself is a scene object of its own.
			bool doorway = h.DoorMaterial != StructureMaterial.Ash && h.DoorWidth > 0f;
			if (doorway && (h.RidgeAlongX || h.Upper > 0f))
			{
				// In a long wall (or under an upper floor) the door stops short of the wall's head.
				h.DoorHeight = Mathf.Min(h.DoorHeight, h.WallHeight - 0.15f);
			}
			float front = -h.Depth * 0.5f;
			float doorX = h.DoorX;
			var frontHoles = new List<WallOpening>();
			var leftHoles = new List<WallOpening>();
			var rightHoles = new List<WallOpening>();
			if (doorway)
			{
				frontHoles.Add(new WallOpening { Along = doorX + h.Width * 0.5f, Width = h.DoorWidth, Bottom = floor - 0.01f, Top = floor + h.DoorHeight });
			}
			// The windows, worked out at every level: each is a hole through the wall with its pane in it.
			var windows = new List<(Vector3 at, Vector3 outward)>();
			if (h.Windows > 0)
			{
				for (int i = 0; i < h.Windows; i++)
				{
					// Spread across the front, skipping the door, and never quite in a row: a medieval front was opened where a
					// room wanted light, not to a grid (2026-10-12: "Are these proper medieval fantasy houses?").
					float t = (i + 0.5f) / h.Windows;
					float x = Mathf.Lerp(-h.Width * 0.5f + 0.8f, h.Width * 0.5f - 0.8f, t) + (d.Rand(key + 600, i) - 0.5f) * 0.5f;
					float sill = floor + h.DoorHeight * 0.62f + (d.Rand(key + 601, i) - 0.5f) * 0.16f;
					if (Mathf.Abs(x - doorX) < h.DoorWidth * 0.5f + 0.6f)
					{
						x = x < doorX ? doorX - h.DoorWidth * 0.5f - 0.75f : doorX + h.DoorWidth * 0.5f + 0.75f;
					}
					if (Mathf.Abs(x) > h.Width * 0.5f - 0.5f || windows.Exists(w => w.outward == Vector3.back && Mathf.Abs(w.at.x - x) < 0.8f))
					{
						continue;
					}
					windows.Add((new Vector3(x, sill, front), Vector3.back));
					frontHoles.Add(new WallOpening { Along = x + h.Width * 0.5f, Width = WindowWidth, Bottom = sill - WindowHeight * 0.5f, Top = sill + WindowHeight * 0.5f });
				}
				if (h.Depth > 4f)
				{
					float sill = floor + h.DoorHeight * 0.62f;
					float sideLength = h.Depth - 2f * h.Wall;
					var hole = new WallOpening { Along = sideLength * 0.5f, Width = WindowWidth, Bottom = sill - WindowHeight * 0.5f, Top = sill + WindowHeight * 0.5f };
					// The stair up to an upper storey climbs the left wall (UpperStorey): no window behind it.
					if (h.Upper <= 0f)
					{
						windows.Add((new Vector3(-h.Width * 0.5f, sill, 0f), Vector3.left));
						leftHoles.Add(hole);
					}
					// The right gable is the chimney's when the ridge runs along the front (ChimneyStack): no window there.
					if (!(h.Chimney && h.RidgeAlongX))
					{
						windows.Add((new Vector3(h.Width * 0.5f, sill, 0f), Vector3.right));
						rightHoles.Add(hole);
					}
				}
			}
			float halfW = h.Width * 0.5f, halfD = h.Depth * 0.5f, t2 = h.Wall * 0.5f;
			WallRun(d, new Vector3(-halfW, 0f, -halfD + t2), new Vector3(halfW, 0f, -halfD + t2), h.Wall, floor, top, frontHoles, h.WallMaterial, key + 1);
			WallRun(d, new Vector3(-halfW, 0f, halfD - t2), new Vector3(halfW, 0f, halfD - t2), h.Wall, floor, top, null, h.WallMaterial, key + 2);
			WallRun(d, new Vector3(-halfW + t2, 0f, -halfD + h.Wall), new Vector3(-halfW + t2, 0f, halfD - h.Wall), h.Wall, floor, top, leftHoles, h.WallMaterial, key + 3);
			WallRun(d, new Vector3(halfW - t2, 0f, -halfD + h.Wall), new Vector3(halfW - t2, 0f, halfD - h.Wall), h.Wall, floor, top, rightHoles, h.WallMaterial, key + 4);
			float width = h.Width, depth = h.Depth;
			if (h.Upper > 0f)
			{
				top = UpperStorey(d, h, floor, top, doorway, ref depth);
			}
			Matrix4x4 place = h.RidgeAlongX ? Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one) : Matrix4x4.identity;
			float span = h.RidgeAlongX ? depth : width, length = h.RidgeAlongX ? width : depth;
			// Pitched as medieval roofs were, to shed rain and snow (thatch steepest); a turf roof lies low, as the turf needs.
			float pitch = MinimumPitch(h.RoofMaterial);
			float rise = Mathf.Max(h.Rise, Mathf.Tan(pitch * Mathf.Deg2Rad) * span * 0.5f);
			GableRoof(d, span, length, top, rise, h.Overhang, h.RoofMaterial == StructureMaterial.Thatch ? 0.32f : 0.14f, h.RoofMaterial, key + 10, place);
			Bargeboards(d, span, length, top, rise, h.Overhang, h.RoofMaterial, key + 700, place);
			// A door taller than the wall, in a gable end, runs up into the gable: the gable is cut round it.
			bool doorInGable = doorway && !h.RidgeAlongX && h.Upper <= 0f && floor + h.DoorHeight > top + 0.01f;
			if (doorInGable)
			{
				GableWithDoor(d, span - 0.02f, top, rise - 0.02f, -length * 0.5f + h.Wall * 0.5f, h.Wall, h.WallMaterial, key + 12,
					doorX - h.DoorWidth * 0.5f, doorX + h.DoorWidth * 0.5f, floor + h.DoorHeight);
			}
			else
			{
				Gable(d, span - 0.02f, top, rise - 0.02f, -length * 0.5f + h.Wall * 0.5f, h.Wall, h.WallMaterial, key + 12, place);
			}
			Gable(d, span - 0.02f, top, rise - 0.02f, length * 0.5f - h.Wall * 0.5f, h.Wall, h.WallMaterial, key + 13, place);
			if (d.Fine)
			{
				// The ridge piece: a beam along the top, under the thatch's crown or the shingles' cap.
				var r0 = new Vector3(0f, top + rise + 0.1f, -length * 0.5f - h.Overhang);
				var r1 = new Vector3(0f, top + rise + 0.1f, length * 0.5f + h.Overhang);
				d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(r0), place.MultiplyPoint3x4(r1), 0.22f, 0.22f,
					h.RoofMaterial == StructureMaterial.Thatch ? StructureMaterial.Thatch : StructureMaterial.Timber, key + 14, StructureRole.Roof));
			}

			// The door (front, −z): a doorway through the wall, its door hung on the inside face and opening inward (a scene
			// object of its own, PointOfInterestDoors); a step up to a raised floor. A tent's flap is still a dark panel.
			if (doorway)
			{
				HangDoor(d, new Vector3(doorX, floor, front + h.Wall - 0.05f), Vector3.right, Vector3.forward, h.DoorWidth, h.DoorHeight, h.DoorKind);
				if (h.Upper <= 0f)
				{
					// (Under an upper storey the ground floor's room is marked with its stair kept clear: UpperStorey.)
					d.Room(new Vector3(0f, floor, 0f), new Vector2(h.Width - 2f * h.Wall, h.Depth - 2f * h.Wall), h.WallHeight, h.RoomKind);
				}
				if (h.Floor <= 0f)
				{
					Steps(d, new Vector3(doorX, 0f, front - 0.12f), Vector3.back, Mathf.Max(h.DoorWidth + 0.4f, StairWidth), floor, h.PlinthMaterial, key + 22);
				}
			}
			else
			{
				d.Add(StructureShapes.Box(new Vector3(doorX, floor + h.DoorHeight * 0.5f, front - 0.04f), new Vector3(h.DoorWidth, h.DoorHeight, 0.1f),
					h.DoorMaterial, key + 20, StructureRole.Detail)).Grain = Vector3.up;
			}
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(doorX - h.DoorWidth * 0.5f - 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f),
					new Vector3(doorX + h.DoorWidth * 0.5f + 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f), 0.2f, 0.18f,
					h.WallMaterial == StructureMaterial.Stone ? StructureMaterial.Stone : StructureMaterial.Timber, key + 21, StructureRole.Detail));
			}
			for (int i = 0; i < windows.Count; i++)
			{
				Window(d, windows[i].at, windows[i].outward, h.Wall, key + 30 + i * 8);
			}
			if (h.Frame && d.Mid)
			{
				Framing(d, h.Width, h.Depth, floor, floor + h.WallHeight, key + 200, frontHoles);
			}
			if (h.Chimney)
			{
				ChimneyStack(d, h, floor, top + rise, doorway, key + 15);
			}
		}

		/// <summary>How far an upper storey is jettied out front and back when a house does not say, metres: enough to read as one.</summary>
		internal const float DefaultJetty = 0.6f;

		/// <summary>
		/// The least pitch a roof of a material is built to, degrees: thatch steepest (it sheds water only when steep),
		/// shingles and planks steep, turf low (it slides off a steep roof). The houses were pitched at 30–40°, which read as
		/// modern (2026-10-12: "Are these proper medieval fantasy houses?").
		/// </summary>
		internal static float MinimumPitch(StructureMaterial roof)
		{
			switch (roof)
			{
				case StructureMaterial.Thatch:
					return 50f;
				case StructureMaterial.Earth:
					return 0f;
				default:
					return 45f;
			}
		}

		/// <summary>
		/// The bargeboards under a gable roof's verges, a finial at each gable's peak, and on a thatched roof the ridge's
		/// bound crest: the edges a medieval roof shows (ridge along z in the roof's frame, as <see cref="GableRoof"/>).
		/// </summary>
		internal static void Bargeboards(StructureDraft d, float span, float length, float eaveY, float rise, float overhang, StructureMaterial roof, int key, Matrix4x4 place)
		{
			// Roof parts (a ruin loses them with its roof), shown from the middle level in.
			if (!d.Mid || roof == StructureMaterial.Earth)
			{
				return;
			}
			float half = span * 0.5f, k = rise / half;
			float outX = half + overhang, outY = eaveY - k * overhang, ridgeY = eaveY + rise;
			int n = 0;
			for (int end = -1; end <= 1; end += 2)
			{
				float z = end * (length * 0.5f + overhang - 0.04f);
				for (int side = -1; side <= 1; side += 2)
				{
					var eave = new Vector3(side * outX, outY - 0.12f, z);
					var peak = new Vector3(0f, ridgeY - 0.1f, z);
					d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(eave), place.MultiplyPoint3x4(peak), 0.06f, 0.24f, StructureMaterial.Timber, key + n++, StructureRole.Roof));
				}
				if (d.Fine)
				{
					// A carved finial standing over the gable's peak.
					Vector3 foot = place.MultiplyPoint3x4(new Vector3(0f, ridgeY - 0.1f, z));
					d.Add(StructureShapes.Beam(foot, foot + Vector3.up * 0.7f, 0.1f, 0.1f, StructureMaterial.Timber, key + n++, StructureRole.Roof));
					d.Add(StructureShapes.Sphere(foot + Vector3.up * 0.75f, new Vector3(0.09f, 0.12f, 0.09f), 6, 4, StructureMaterial.Timber, key + n++, StructureRole.Roof));
				}
			}
		}

		/// <summary>
		/// What shows under a jettied storey (its floor overhanging the walls front and back by <paramref name="jetty"/>):
		/// the bressummer, the heavy beam along the overhang's edge the upper wall stands on; the joists' ends between it and the
		/// wall; and a brace up under each corner and the middle of each overhang.
		/// </summary>
		private static void JettyDetail(StructureDraft d, float width, float depth, float jetty, float wallTop, int key)
		{
			if (!d.Mid || jetty <= 0.05f)
			{
				return;
			}
			float halfW = width * 0.5f, halfD = depth * 0.5f;
			int n = 0;
			for (int side = -1; side <= 1; side += 2)
			{
				float edge = side * (halfD + jetty - 0.1f);
				d.Add(StructureShapes.Beam(new Vector3(-halfW - 0.05f, wallTop + 0.02f, edge), new Vector3(halfW + 0.05f, wallTop + 0.02f, edge), 0.28f, 0.22f,
					StructureMaterial.Timber, key + n++, StructureRole.Detail)).Grain = Vector3.right;
				if (d.Fine)
				{
					int joists = Mathf.Max(3, Mathf.RoundToInt(width / 1.1f));
					for (int j = 1; j < joists; j++)
					{
						float x = -halfW + width * j / joists;
						d.Add(StructureShapes.Box(new Vector3(x, wallTop + 0.06f, side * (halfD + jetty * 0.45f)), new Vector3(0.13f, 0.15f, jetty * 0.9f),
							StructureMaterial.Timber, key + n++, StructureRole.Detail));
					}
				}
				foreach (float x in new[] { -halfW + 0.15f, 0f, halfW - 0.15f })
				{
					var foot = new Vector3(x, wallTop - 0.75f, side * (halfD + 0.02f));
					var head = new Vector3(x, wallTop - 0.08f, side * (halfD + jetty - 0.15f));
					d.Add(StructureShapes.Beam(foot, head, 0.12f, 0.12f, StructureMaterial.Timber, key + n++, StructureRole.Detail));
				}
			}
		}

		/// <summary>
		/// A stair's width, metres: two people abreast, one going up while another comes down (the character capsule is
		/// 0.6 m across; 0.95 m let only one at a time).
		/// </summary>
		internal const float StairWidth = 1.5f;
		/// <summary>The clear height kept over every step of a stair, metres.</summary>
		internal const float StairHeadroom = 2.05f;

		/// <summary>
		/// A house's jettied upper storey (2026-10-12: "2 story buildings have no stairways up and the second floor is
		/// empty?"): the floor over the ground floor with a well cut through it, a stair up the left wall to it, a rail round
		/// the well, walls with windows, and the room it is (<see cref="House.UpperRoomKind"/>) for the interior to furnish.
		/// The floor used to be one solid slab, with nothing up to it and nothing on it. Returns the storey's top; widens
		/// <paramref name="depth"/> by the jetty.
		/// </summary>
		/// <remarks>
		/// The stair is a solid flight (<see cref="Steps"/>: the last level is the collider, and a riser the controller can't
		/// climb is a wall) of <see cref="StepRise"/> risers at most, its foot clear of the door's way in, climbing toward the back;
		/// the well over it is cut from where a climber's head would meet the floor to the top step, so there is
		/// <see cref="StairHeadroom"/> over every step. Its rail stands along the well's open side and across its foot end,
		/// the top left open to step off. The ground floor's room keeps the flight and its landing clear, the upper one the well.
		/// </remarks>
		private static float UpperStorey(StructureDraft d, House h, float floor, float top, bool doorway, ref float depth)
		{
			const float slab = 0.24f;
			float jetty = h.Jetty > 0f ? h.Jetty : DefaultJetty;
			int key = h.Key;
			float width = h.Width;
			float halfW = width * 0.5f, halfD = h.Depth * 0.5f;
			float upper0 = top + slab;
			float rise = upper0 - floor;
			// As Steps lays them: risers of at most StepRise, treads of StepTread.
			int steps = Mathf.CeilToInt(rise / StepRise);
			float run = (steps - 1) * StepTread;
			// Along the left wall, its foot clear of the front wall's door, climbing toward the back.
			float x0 = -halfW + h.Wall, x1 = x0 + StairWidth;
			float zMin = -halfD + h.Wall, zMax = halfD - h.Wall;
			float foot = Mathf.Min(zMin + 1.4f, zMax - run - 0.9f);
			float head = foot + run;
			// The first step whose tread a climber's head would strike the floor over: the well opens from there.
			int firstOpen = Mathf.Clamp(Mathf.CeilToInt((top - StairHeadroom - floor) / rise * steps), 1, steps);
			float wellFrom = Mathf.Max(zMin, head - (steps - firstOpen + 1) * StepTread - 0.1f);
			float wellX1 = x1 + 0.05f;

			// The floor: a slab over the whole house, jettied front and back, less the well.
			float sx = width + 0.1f, sz = h.Depth + 2f * jetty + 0.1f, y = top + slab * 0.5f;
			void Slab(float ax, float bx, float az, float bz, int n)
			{
				if (bx - ax > 1e-3f && bz - az > 1e-3f)
				{
					d.Add(StructureShapes.Box(new Vector3((ax + bx) * 0.5f, y, (az + bz) * 0.5f), new Vector3(bx - ax, slab, bz - az), StructureMaterial.Timber, PieceKey(key + 5, n)));
				}
			}
			float sx0 = -sx * 0.5f, sx1 = sx * 0.5f, sz0 = -sz * 0.5f, sz1 = sz * 0.5f;
			Slab(sx0, sx1, sz0, wellFrom, 0);
			Slab(sx0, sx1, head, sz1, 1);
			Slab(sx0, x0, wellFrom, head, 2);
			Slab(wellX1, sx1, wellFrom, head, 3);
			JettyDetail(d, width, h.Depth, jetty, top, key + 7200);

			// The stair: solid steps from the ground floor up to the top step, which is the upper floor at the well's head.
			Steps(d, new Vector3((x0 + x1) * 0.5f, 0f, head), Vector3.back, StairWidth, upper0, StructureMaterial.Timber, key + 7000, floor);
			if (d.Mid)
			{
				// A handrail up the flight's open side, on posts at its foot and its head.
				var railFoot = new Vector3(x1 + 0.04f, floor + 0.9f + rise / steps, foot);
				var railHead = new Vector3(x1 + 0.04f, upper0 + 0.9f, head);
				d.Add(StructureShapes.Beam(railFoot, railHead, 0.06f, 0.06f, StructureMaterial.Timber, key + 7100, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(x1 + 0.04f, floor, foot), railFoot + Vector3.up * 0.05f, 0.08f, 0.08f, StructureMaterial.Timber, key + 7101, StructureRole.Detail));
			}

			// The rail round the well upstairs: along its open side and across its foot end.
			float railY = upper0 + 0.95f;
			d.Add(StructureShapes.Beam(new Vector3(wellX1 + 0.04f, railY, wellFrom), new Vector3(wellX1 + 0.04f, railY, head), 0.07f, 0.07f, StructureMaterial.Timber, key + 7110));
			d.Add(StructureShapes.Beam(new Vector3(x0, railY, wellFrom - 0.04f), new Vector3(wellX1 + 0.04f, railY, wellFrom - 0.04f), 0.07f, 0.07f, StructureMaterial.Timber, key + 7111));
			int posts = Mathf.Max(2, Mathf.RoundToInt((head - wellFrom) / 0.9f) + 1);
			for (int i = 0; i < posts; i++)
			{
				float z = Mathf.Lerp(wellFrom, head - 0.05f, i / (float)(posts - 1));
				d.Add(StructureShapes.Box(new Vector3(wellX1 + 0.04f, upper0 + 0.475f, z), new Vector3(0.07f, 0.95f, 0.07f), StructureMaterial.Timber, key + 7120 + i,
					i == 0 || i == posts - 1 ? StructureRole.Structure : StructureRole.Detail));
			}

			// The upper walls, jettied front and back, with windows along the front and one in each free gable.
			depth = h.Depth + 2f * jetty;
			float roof = upper0 + h.Upper;
			float sill = upper0 + h.DoorHeight * 0.62f;
			float hd = depth * 0.5f, t2 = h.Wall * 0.5f;
			var frontHoles = new List<WallOpening>();
			var leftHoles = new List<WallOpening>();
			var rightHoles = new List<WallOpening>();
			var windows = new List<(Vector3 at, Vector3 outward)>();
			int count = Mathf.Max(1, h.Windows);
			for (int i = 0; i < count; i++)
			{
				// Not over the ground floor's, and not in a row.
				float x = Mathf.Lerp(-halfW + 0.9f, halfW - 0.9f, (i + 0.5f) / count) + (d.Rand(key + 610, i) - 0.5f) * 0.6f;
				float wy = sill + (d.Rand(key + 611, i) - 0.5f) * 0.14f;
				windows.Add((new Vector3(x, wy, -hd), Vector3.back));
				frontHoles.Add(new WallOpening { Along = x + halfW, Width = WindowWidth, Bottom = wy - WindowHeight * 0.5f, Top = wy + WindowHeight * 0.5f });
			}
			float sideLength = depth - 2f * h.Wall;
			var sideHole = new WallOpening { Along = sideLength * 0.5f + 0.9f, Width = WindowWidth, Bottom = sill - WindowHeight * 0.5f, Top = sill + WindowHeight * 0.5f };
			windows.Add((new Vector3(-halfW, sill, 0.9f), Vector3.left));
			leftHoles.Add(sideHole);
			// The chimney's gable (the right one when the ridge runs along the front) keeps its wall whole.
			if (!(h.Chimney && h.RidgeAlongX))
			{
				windows.Add((new Vector3(halfW, sill, 0.9f), Vector3.right));
				rightHoles.Add(sideHole);
			}
			StructureMaterial upperWall = h.UpperWallMaterial ?? h.WallMaterial;
			WallRun(d, new Vector3(-halfW, 0f, -hd + t2), new Vector3(halfW, 0f, -hd + t2), h.Wall, upper0, roof, frontHoles, upperWall, key + 6);
			WallRun(d, new Vector3(-halfW, 0f, hd - t2), new Vector3(halfW, 0f, hd - t2), h.Wall, upper0, roof, null, upperWall, key + 7);
			WallRun(d, new Vector3(-halfW + t2, 0f, -hd + h.Wall), new Vector3(-halfW + t2, 0f, hd - h.Wall), h.Wall, upper0, roof, leftHoles, upperWall, key + 8);
			WallRun(d, new Vector3(halfW - t2, 0f, -hd + h.Wall), new Vector3(halfW - t2, 0f, hd - h.Wall), h.Wall, upper0, roof, rightHoles, upperWall, key + 9);
			for (int i = 0; i < windows.Count; i++)
			{
				Window(d, windows[i].at, windows[i].outward, h.Wall, key + 500 + i * 8);
			}
			if ((h.UpperFrame ?? h.Frame) && d.Mid)
			{
				Framing(d, width, depth, upper0, roof, key + 300, frontHoles);
			}

			// The rooms: the ground floor (marked by the doorway, BuildHouse) keeps the flight and its landing clear; the
			// upper storey is a room of its own, its well kept clear with a step's room round it.
			if (doorway)
			{
				d.Room(new Vector3(0f, floor, 0f), new Vector2(width - 2f * h.Wall, h.Depth - 2f * h.Wall), h.WallHeight, h.RoomKind);
				d.RoomClear(new Vector2(x0, foot - 1f), new Vector2(x1 + 0.35f, head));
			}
			d.Room(new Vector3(0f, upper0, 0f), new Vector2(width - 2f * h.Wall, depth - 2f * h.Wall), h.Upper,
				h.UpperRoomKind == StructureRoomKind.Dwelling ? StructureRoomKind.Chamber : h.UpperRoomKind);
			d.RoomClear(new Vector2(x0, wellFrom - 0.5f), new Vector2(wellX1 + 0.45f, head + 1f));
			return roof;
		}

		/// <summary>
		/// A house's chimney (2026-10-12: "where are the chimneys??"): a stone stack centred on a gable wall — the back
		/// gable, or the right one when the ridge runs along the front — half proud of it, from the floor to clear of the
		/// ridge, with a cap and pots; its smoke from the top and nowhere else; and its breast inside the room the hearth is
		/// built against (<see cref="StructureDraft.RoomHearth"/>). It used to be a pillar standing in the room with the fire
		/// on whichever wall the furniture found, so the smoke rose from a roof with no fire under it.
		/// </summary>
		private static void ChimneyStack(StructureDraft d, House h, float floor, float ridge, bool hasRoom, int key)
		{
			const float breadth = 1.1f, through = 0.8f;
			float halfW = h.Width * 0.5f, halfD = h.Depth * 0.5f;
			// The wall line's middle, and the way into the room from it.
			Vector3 wallAt = h.RidgeAlongX ? new Vector3(halfW - h.Wall * 0.5f, 0f, 0f) : new Vector3(0f, 0f, halfD - h.Wall * 0.5f);
			Vector3 inward = h.RidgeAlongX ? Vector3.left : Vector3.back;
			Vector3 along = h.RidgeAlongX ? Vector3.forward : Vector3.right;
			float chimneyTop = ridge + 0.75f;
			Vector3 centre = wallAt - inward * 0.12f;
			Vector3 size = along * breadth + Vector3.Scale(new Vector3(1f, 0f, 1f), inward * through);
			size = new Vector3(Mathf.Abs(size.x), chimneyTop - floor + FoundationDepth, Mathf.Abs(size.z));
			d.Add(StructureShapes.ChamferBox(new Vector3(centre.x, (floor - FoundationDepth + chimneyTop) * 0.5f, centre.z), size, d.Bevel(0.03f), StructureMaterial.Fieldstone, key));
			// The breast round the fireplace, broader and deeper outside the wall to the first floor's height, stepping in
			// to the stack over a sloped weathering: a medieval stack, not a box (its inner face, the hearth's, is the stack's).
			const float breastBreadth = 1.6f, breastOut = 0.35f;
			float breastTop = floor + 2.4f;
			Vector3 breastCentre = centre - inward * (breastOut * 0.5f);
			Vector3 breastSize = along * breastBreadth + Vector3.Scale(new Vector3(1f, 0f, 1f), inward * (through + breastOut));
			breastSize = new Vector3(Mathf.Abs(breastSize.x), breastTop - floor + FoundationDepth, Mathf.Abs(breastSize.z));
			d.Add(StructureShapes.ChamferBox(new Vector3(breastCentre.x, (floor - FoundationDepth + breastTop) * 0.5f, breastCentre.z), breastSize, d.Bevel(0.03f),
				StructureMaterial.Fieldstone, key + 900));
			if (d.Mid)
			{
				// The weathering: a sloped step from the breast in to the stack.
				Vector3 w = breastCentre + Vector3.up * (breastTop + 0.12f);
				d.Add(StructureShapes.Block(w, new Vector3(breastSize.x - 0.12f, 0.24f, breastSize.z - 0.12f), Quaternion.identity, StructureMaterial.Stone, key + 901, StructureRole.Detail));
				// A drip course round the stack under its cap.
				d.Add(StructureShapes.Box(new Vector3(centre.x, chimneyTop - 0.55f, centre.z), new Vector3(size.x + 0.1f, 0.09f, size.z + 0.1f), StructureMaterial.Stone, key + 902, StructureRole.Detail));
			}
			// A dressed cap a little wider than the stack, and its pots.
			d.Add(StructureShapes.Box(new Vector3(centre.x, chimneyTop + 0.06f, centre.z), new Vector3(size.x + 0.14f, 0.12f, size.z + 0.14f), StructureMaterial.Stone, key + 1, StructureRole.Detail));
			if (d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					Vector3 pot = centre + along * (s * breadth * 0.22f);
					d.Add(StructureShapes.Cylinder(0.11f, chimneyTop + 0.12f, chimneyTop + 0.42f, d.Seg(10, 6), StructureMaterial.Fieldstone, key + 2 + (s > 0 ? 1 : 0), false, StructureRole.Detail)
						.Transform(Matrix4x4.Translate(new Vector3(pot.x, 0f, pot.z))));
				}
			}
			d.Emit(new Vector3(centre.x, chimneyTop + 0.45f, centre.z), SceneEmitterKind.Smoke);
			if (hasRoom)
			{
				// The hearth's foot: in front of the breast's inside face, its back to it (furniture faces −z, back +z).
				Vector3 breastFace = centre + inward * (through * 0.5f);
				Vector3 hearth = breastFace + inward * 0.47f;
				float yaw = Mathf.Atan2(-inward.x, -inward.z) * Mathf.Rad2Deg;
				d.RoomHearth(new Vector2(hearth.x, hearth.z), yaw, floor);
			}
		}

		/// <summary>The height of a curtain wall's walk: the walls, the towers' upper doors and the wall stairs all meet it.</summary>
		internal const float WallWalkHeight = 5f;

		/// <summary>The highest riser a character steps up without jumping (the controller's MaxStepHeight is 0.25 m).</summary>
		internal const float StepRise = 0.2f;
		/// <summary>A step's tread, metres.</summary>
		internal const float StepTread = 0.3f;

		/// <summary>
		/// Steps up to a threshold at <paramref name="toY"/> from <paramref name="fromY"/>: the top tread is the threshold at
		/// <paramref name="edge"/> (on the building's face), the flight running out along <paramref name="outward"/>, no riser
		/// higher than <see cref="StepRise"/>. Solid to below the ground, the same at every level: the last level is the
		/// collider, and a riser the controller can't climb is a wall.
		/// </summary>
		internal static void Steps(StructureDraft d, Vector3 edge, Vector3 outward, float width, float toY, StructureMaterial material, int key, float fromY = 0f)
		{
			float rise = toY - fromY;
			if (rise <= StepRise + 1e-3f)
			{
				return;
			}
			int n = Mathf.CeilToInt(rise / StepRise);
			outward = new Vector3(outward.x, 0f, outward.z).normalized;
			Quaternion turn = Quaternion.LookRotation(outward, Vector3.up);
			float bottom = Mathf.Min(fromY, 0f) - FoundationDepth;
			for (int i = 1; i < n; i++)
			{
				float top = fromY + rise * i / n, run = (n - i) * StepTread;
				Vector3 centre = new Vector3(edge.x, 0f, edge.z) + outward * (run * 0.5f) + Vector3.up * ((top + bottom) * 0.5f);
				d.Add(StructureShapes.Block(centre, new Vector3(width, top - bottom, run), turn, material, PieceKey(key, i)));
			}
		}

		/// <summary>A doorway wider than this gets a pair of doors, metres.</summary>
		internal const float DoubleDoorWidth = 1.7f;

		/// <summary>
		/// Records the door (or pair of doors) hung in a doorway centred at <paramref name="foot"/> (its threshold, on the
		/// inside face), running <paramref name="across"/>, opening <paramref name="inward"/>.
		/// </summary>
		internal static void HangDoor(StructureDraft d, Vector3 foot, Vector3 across, Vector3 inward, float width, float height, StructureDoorKind kind)
		{
			across = new Vector3(across.x, 0f, across.z).normalized;
			if (width <= DoubleDoorWidth)
			{
				d.Door(foot - across * (width * 0.5f), across, inward, width, height, kind);
				return;
			}
			float leaf = width * 0.5f;
			d.Door(foot - across * (width * 0.5f), across, inward, leaf, height, kind);
			d.Door(foot + across * (width * 0.5f), -across, inward, leaf, height, kind);
		}

		/// <summary>A window's hole through the wall, metres.</summary>
		internal const float WindowWidth = 0.6f, WindowHeight = 0.75f;

		/// <summary>
		/// A window in a wall whose outer face is at <paramref name="at"/>, facing <paramref name="outward"/>, the wall
		/// <paramref name="wall"/> thick: the pane set in the hole (WallRun cuts it) halfway through the wall, a sill and a
		/// lintel outside. The pane is at every level, so a house far off shows no holes.
		/// </summary>
		/// <summary>A window with no hole behind it: the pane proud of an unbroken wall face (towers whose walls are not yet cut).</summary>
		internal static void Window(StructureDraft d, Vector3 at, Vector3 outward, int key) => Window(d, at, outward, -0.04f, key);

		/// <remarks>
		/// Medieval, not Georgian (2026-10-12: "Are these proper medieval fantasy houses?"): a timber surround and a
		/// mullion splitting it into two lights, and by the window's own draw either leaded lights (the glass crossed by
		/// its cames), shutters swung open against the wall, or shutters closed over it, barred. The glass stays behind every
		/// one (a closed window still shows its light round the shutters at night, and from inside).
		/// </remarks>
		internal static void Window(StructureDraft d, Vector3 at, Vector3 outward, float wall, int key)
		{
			Quaternion turn = Quaternion.FromToRotation(Vector3.back, outward);
			Vector3 right = turn * Vector3.right;
			// The pane: glass, lit from inside at night, in the middle of the wall's thickness.
			d.Add(StructureShapes.Block(at - outward * (wall * 0.5f), new Vector3(WindowWidth, WindowHeight, 0.04f), turn, StructureMaterial.Glass, key, StructureRole.Detail));
			if (!d.Mid)
			{
				return;
			}
			// The surround: a sill, a heavier head, and the mullion between the two lights.
			d.Add(StructureShapes.Block(at + outward * 0.06f + Vector3.down * (WindowHeight * 0.5f + 0.05f), new Vector3(WindowWidth + 0.24f, 0.09f, 0.16f), turn, StructureMaterial.Timber, key + 1, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.05f + Vector3.up * (WindowHeight * 0.5f + 0.07f), new Vector3(WindowWidth + 0.3f, 0.14f, 0.12f), turn, StructureMaterial.Timber, key + 2, StructureRole.Detail));
			d.Add(StructureShapes.Block(at - outward * (wall * 0.5f - 0.04f), new Vector3(0.07f, WindowHeight, 0.1f), turn, StructureMaterial.Timber, key + 3, StructureRole.Detail));
			float kind = d.Rand(key, 77);
			const float leaf = WindowWidth * 0.5f;
			if (kind < 0.5f)
			{
				// Leaded lights: each light crossed corner to corner by its cames.
				if (d.Fine)
				{
					Vector3 glass = at - outward * (wall * 0.5f - 0.03f);
					for (int l = -1; l <= 1; l += 2)
					{
						Vector3 c = glass + right * (l * leaf * 0.5f);
						Vector3 u = Vector3.up * (WindowHeight * 0.5f - 0.02f), r = right * (leaf * 0.5f - 0.04f);
						d.Add(StructureShapes.Beam(c - u - r, c + u + r, 0.014f, 0.014f, StructureMaterial.Iron, key + 4 + (l > 0 ? 2 : 0), StructureRole.Detail));
						d.Add(StructureShapes.Beam(c - u + r, c + u - r, 0.014f, 0.014f, StructureMaterial.Iron, key + 5 + (l > 0 ? 2 : 0), StructureRole.Detail));
					}
				}
			}
			else if (kind < 0.8f)
			{
				// Board shutters swung open flat against the wall, one either side.
				for (int l = -1; l <= 1; l += 2)
				{
					d.Add(StructureShapes.Block(at + outward * 0.05f + right * (l * (WindowWidth * 0.5f + leaf * 0.5f + 0.06f)), new Vector3(leaf, WindowHeight + 0.04f, 0.04f), turn,
						StructureMaterial.Timber, key + 4 + (l > 0 ? 1 : 0), StructureRole.Detail)).Grain = Vector3.up;
				}
			}
			else
			{
				// Shutters closed over it, a bar across them.
				d.Add(StructureShapes.Block(at + outward * 0.04f, new Vector3(WindowWidth + 0.04f, WindowHeight + 0.04f, 0.05f), turn, StructureMaterial.Timber, key + 4, StructureRole.Detail)).Grain = Vector3.up;
				d.Add(StructureShapes.Block(at + outward * 0.08f, new Vector3(WindowWidth + 0.12f, 0.07f, 0.04f), turn, StructureMaterial.Iron, key + 5, StructureRole.Detail));
			}
		}

		/// <summary>Half-timbering on four walls: corner posts, sill and head plates, studs and braces at level 0.</summary>
		internal static void Framing(StructureDraft d, float width, float depth, float y0, float y1, int key, List<WallOpening> frontHoles = null)
		{
			// Nothing crosses a hole in the front wall (a doorway, a window): studs and braces there are left out, plates and
			// rails broken round it.
			bool Clear(float x0, float x1)
			{
				if (frontHoles == null)
				{
					return true;
				}
				foreach (WallOpening o in frontHoles)
				{
					float c = o.Along - width * 0.5f;
					if (x1 > c - o.Width * 0.5f - 0.12f && x0 < c + o.Width * 0.5f + 0.12f)
					{
						return false;
					}
				}
				return true;
			}
			const float post = 0.2f, proud = 0.04f;
			float hx = width * 0.5f + proud - post * 0.5f, hz = depth * 0.5f + proud - post * 0.5f;
			int k = key;
			// A plate or rail along the front wall, broken where a hole's height crosses it (a doorway's foot, a window): it
			// butts into the hole's sides instead of running across it.
			void FrontRail(float y, float thick, float tall, int railKey)
			{
				float from = -hx;
				if (frontHoles != null)
				{
					var cuts = new List<Vector2>();
					foreach (WallOpening o in frontHoles)
					{
						if (o.Top > y - tall * 0.5f && o.Bottom < y + tall * 0.5f)
						{
							float c = o.Along - width * 0.5f;
							cuts.Add(new Vector2(c - o.Width * 0.5f, c + o.Width * 0.5f));
						}
					}
					cuts.Sort((a, b) => a.x.CompareTo(b.x));
					foreach (Vector2 cut in cuts)
					{
						if (cut.x - from > 0.05f)
						{
							d.Add(StructureShapes.Beam(new Vector3(from, y, -hz), new Vector3(cut.x, y, -hz), thick, tall, StructureMaterial.Timber, railKey, StructureRole.Detail));
						}
						from = Mathf.Max(from, cut.y);
					}
				}
				if (hx - from > 0.05f)
				{
					d.Add(StructureShapes.Beam(new Vector3(from, y, -hz), new Vector3(hx, y, -hz), thick, tall, StructureMaterial.Timber, railKey, StructureRole.Detail));
				}
			}
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * hx, y0, sz * hz), new Vector3(sx * hx, y1, sz * hz), post, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				}
			}
			foreach (float y in new[] { y0 + 0.1f, y1 - 0.1f })
			{
				FrontRail(y, 0.18f, post, k++);
				d.Add(StructureShapes.Beam(new Vector3(-hx, y, hz), new Vector3(hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-hx, y, -hz), new Vector3(-hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(hx, y, -hz), new Vector3(hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
			}
			// A middle rail round the long walls: the box frame's panels.
			if (d.Mid)
			{
				float rail = (y0 + y1) * 0.5f;
				FrontRail(rail, 0.13f, post * 0.9f, k++);
				d.Add(StructureShapes.Beam(new Vector3(-hx, rail, hz), new Vector3(hx, rail, hz), 0.13f, post * 0.9f, StructureMaterial.Timber, k++, StructureRole.Detail));
			}
			if (!d.Fine)
			{
				return;
			}
			// Studs close-set along the long walls (a medieval frame's close studding, not a stud a bay), a brace in each end bay.
			int bays = Mathf.Max(1, Mathf.RoundToInt(width / 0.85f));
			for (int side = -1; side <= 1; side += 2)
			{
				float z = side * hz;
				bool front = side < 0;
				for (int b = 1; b < bays; b++)
				{
					float x = -hx + 2f * hx * b / bays;
					if (front && !Clear(x - 0.07f, x + 0.07f))
					{
						k++;
						continue;
					}
					d.Add(StructureShapes.Beam(new Vector3(x, y0 + 0.2f, z), new Vector3(x, y1 - 0.2f, z), 0.14f, post * 0.9f, StructureMaterial.Timber, k++, StructureRole.Detail));
				}
				float bay = Mathf.Min(2f * hx, 2.2f);
				if (!front || Clear(-hx, -hx + bay))
				{
					d.Add(StructureShapes.Beam(new Vector3(-hx + 0.1f, y0 + 0.2f, z), new Vector3(-hx + bay - 0.1f, y1 - 0.25f, z), 0.12f, post * 0.85f,
						StructureMaterial.Timber, k, StructureRole.Detail));
				}
				k++;
				if (!front || Clear(hx - bay, hx))
				{
					d.Add(StructureShapes.Beam(new Vector3(hx - 0.1f, y0 + 0.2f, z), new Vector3(hx - bay + 0.1f, y1 - 0.25f, z), 0.12f, post * 0.85f,
						StructureMaterial.Timber, k, StructureRole.Detail));
				}
				k++;
			}
		}

		/// <summary>
		/// A classical column on the ground: square plinth, a torus at its foot, a shaft that narrows (entasis), an echinus
		/// and an abacus. <paramref name="height"/> is to the top of the abacus.
		/// </summary>
		internal static void Column(StructureDraft d, Vector3 at, float height, float radius, StructureMaterial material, int key)
		{
			float plinth = radius * 0.55f;
			d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, plinth * 0.5f - 0.15f, 0f), new Vector3(radius * 2.5f, plinth + 0.3f, radius * 2.5f), d.Bevel(0.03f), material, key));
			int seg = d.Seg(16, 6);
			float capital = radius * 0.9f;
			float shaftTop = height - capital;
			var profile = new List<Vector2>
			{
				new Vector2(radius * 1.12f, plinth),
				new Vector2(radius * 1.12f, plinth + radius * 0.18f),
				new Vector2(radius, plinth + radius * 0.3f),
			};
			if (d.Mid)
			{
				profile.Add(new Vector2(radius * 0.97f, Mathf.Lerp(plinth, shaftTop, 0.4f)));
			}
			profile.Add(new Vector2(radius * 0.84f, shaftTop));
			profile.Add(new Vector2(radius * 1.15f, shaftTop + capital * 0.45f));
			profile.Add(new Vector2(radius * 1.15f, shaftTop + capital * 0.5f));
			profile.Add(new Vector2(0f, shaftTop + capital * 0.5f));
			StructureSolid shaft = StructureShapes.Lathe(profile, seg, material, key + 1);
			d.Add(shaft.Transform(Matrix4x4.Translate(at)));
			d.Add(StructureShapes.ChamferBox(at + new Vector3(0f, height - capital * 0.25f, 0f), new Vector3(radius * 2.6f, capital * 0.5f, radius * 2.6f), d.Bevel(0.025f), material, key + 2));
		}

		/// <summary>
		/// An undressed stone: a bevelled block cut by a few seeded planes across its corners and top, so no two are alike.
		/// <paramref name="size"/> is the block before the cuts; the foot is at y = 0 about <paramref name="at"/>.
		/// </summary>
		internal static StructureSolid RoughStone(StructureDraft d, Vector3 at, Vector3 size, float yaw, int key, int cuts = 4,
			StructureMaterial material = StructureMaterial.Fieldstone, StructureRole role = StructureRole.Structure)
		{
			// Bevelled only when big enough for the bevel to show (a fire-ring stone's would cost more than the stone).
			float bevel = Mathf.Min(size.x, size.z) >= 0.6f ? d.Bevel(Mathf.Min(size.x, size.z) * 0.08f) : 0f;
			StructureSolid s = StructureShapes.ChamferBox(new Vector3(0f, size.y * 0.5f, 0f), size, bevel, material, key, role);
			for (int i = 0; i < cuts; i++)
			{
				float az = d.Rand(key, 10 + i) * 2f * Mathf.PI;
				float tilt = d.Range(key, 20 + i, 25f, 70f) * Mathf.Deg2Rad;
				var n = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Cos(tilt), Mathf.Sin(tilt) * Mathf.Sin(az));
				// Through a point on the upper part of the block, short of its corner by a seeded share.
				var corner = new Vector3(Mathf.Cos(az) * size.x * 0.5f, size.y, Mathf.Sin(az) * size.z * 0.5f);
				float depth = d.Range(key, 30 + i, 0.08f, 0.3f) * Mathf.Min(size.x, size.z);
				s.Clip(n, Vector3.Dot(n, corner) - depth);
			}
			return s.Transform(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
		}

		/// <summary>A log or pole from <paramref name="a"/> to <paramref name="b"/>, grain along it, pointed when <paramref name="tip"/> &gt; 0.</summary>
		internal static StructureSolid Log(StructureDraft d, Vector3 a, Vector3 b, float radius, int key, float tip = 0f,
			StructureMaterial material = StructureMaterial.Timber, int segments = 8, StructureRole role = StructureRole.Structure)
		{
			return d.Add(StructureShapes.Rod(a, b, radius, d.Seg(segments, 4), material, key, role, tip));
		}

		/// <summary>A lean of up to <paramref name="degrees"/> about a seeded horizontal axis, pivoting at the foot.</summary>
		internal static Matrix4x4 Lean(StructureDraft d, int key, float degrees, Vector3 foot)
		{
			float az = d.Rand(key, 70) * 360f;
			float angle = d.Range(key, 71, -degrees, degrees);
			Quaternion q = Quaternion.AngleAxis(angle, Quaternion.Euler(0f, az, 0f) * Vector3.right);
			return Matrix4x4.Translate(foot) * Matrix4x4.Rotate(q) * Matrix4x4.Translate(-foot);
		}

		/// <summary>Transforms every solid added since <paramref name="first"/>.</summary>
		internal static void TransformSince(StructureDraft d, int first, Matrix4x4 m)
		{
			for (int i = first; i < d.Solids.Count; i++)
			{
				d.Solids[i].Transform(m);
			}
		}
	}
}
#endif
