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

		/// <summary>Four walls round a rectangle: front and back the full width, the sides between them.</summary>
		internal static void Walls(StructureDraft d, Vector3 centre, float width, float depth, float thick, float y0, float y1, StructureMaterial material, int key)
		{
			float h = y1 - y0, ym = (y0 + y1) * 0.5f;
			d.Add(StructureShapes.Box(centre + new Vector3(0f, ym, -depth * 0.5f + thick * 0.5f), new Vector3(width, h, thick), material, key));
			d.Add(StructureShapes.Box(centre + new Vector3(0f, ym, depth * 0.5f - thick * 0.5f), new Vector3(width, h, thick), material, key + 1));
			d.Add(StructureShapes.Box(centre + new Vector3(-width * 0.5f + thick * 0.5f, ym, 0f), new Vector3(thick, h, depth - 2f * thick), material, key + 2));
			d.Add(StructureShapes.Box(centre + new Vector3(width * 0.5f - thick * 0.5f, ym, 0f), new Vector3(thick, h, depth - 2f * thick), material, key + 3));
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
			/// <summary>What the door is: timber planks (the default), or a dark opening (<see cref="StructureMaterial.Ash"/>) for a tent's flap.</summary>
			public StructureMaterial DoorMaterial;
			/// <summary>A second storey this much taller, jettied out over the first; 0 = one storey.</summary>
			public float Upper;
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
			Walls(d, Vector3.zero, h.Width, h.Depth, h.Wall, floor, top, h.WallMaterial, key + 1);
			float width = h.Width, depth = h.Depth;
			if (h.Upper > 0f)
			{
				// A jettied upper storey: a floor band, then walls a little wider front and back.
				const float jetty = 0.25f;
				d.Add(StructureShapes.Box(new Vector3(0f, top + 0.12f, 0f), new Vector3(width + 0.1f, 0.24f, depth + 2f * jetty + 0.1f), StructureMaterial.Timber, key + 5));
				depth += 2f * jetty;
				float upper0 = top + 0.24f;
				top = upper0 + h.Upper;
				Walls(d, Vector3.zero, width, depth, h.Wall, upper0, top, h.WallMaterial, key + 6);
				if (h.Frame && d.Mid)
				{
					Framing(d, width, depth, upper0, top, key + 300);
				}
			}
			Matrix4x4 place = h.RidgeAlongX ? Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), Vector3.one) : Matrix4x4.identity;
			float span = h.RidgeAlongX ? depth : width, length = h.RidgeAlongX ? width : depth;
			float rise = h.Rise;
			GableRoof(d, span, length, top, rise, h.Overhang, h.RoofMaterial == StructureMaterial.Thatch ? 0.32f : 0.14f, h.RoofMaterial, key + 10, place);
			Gable(d, span - 0.02f, top, rise - 0.02f, -length * 0.5f + h.Wall * 0.5f, h.Wall, h.WallMaterial, key + 12, place);
			Gable(d, span - 0.02f, top, rise - 0.02f, length * 0.5f - h.Wall * 0.5f, h.Wall, h.WallMaterial, key + 13, place);
			if (d.Fine)
			{
				// The ridge piece: a beam along the top, under the thatch's crown or the shingles' cap.
				var r0 = new Vector3(0f, top + rise + 0.1f, -length * 0.5f - h.Overhang);
				var r1 = new Vector3(0f, top + rise + 0.1f, length * 0.5f + h.Overhang);
				d.Add(StructureShapes.Beam(place.MultiplyPoint3x4(r0), place.MultiplyPoint3x4(r1), 0.22f, 0.22f,
					h.RoofMaterial == StructureMaterial.Thatch ? StructureMaterial.Thatch : StructureMaterial.Timber, key + 14, StructureRole.Roof));
			}

			// Door (front, −z) with a lintel; the door is proud of the wall, never a hole, so the house stays one closed body.
			float front = -h.Depth * 0.5f;
			float doorX = h.DoorX;
			d.Add(StructureShapes.Box(new Vector3(doorX, floor + h.DoorHeight * 0.5f, front - 0.04f), new Vector3(h.DoorWidth, h.DoorHeight, 0.1f),
				h.DoorMaterial, key + 20, StructureRole.Detail)).Grain = Vector3.up;
			if (d.Mid)
			{
				d.Add(StructureShapes.Beam(new Vector3(doorX - h.DoorWidth * 0.5f - 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f),
					new Vector3(doorX + h.DoorWidth * 0.5f + 0.15f, floor + h.DoorHeight + 0.1f, front - 0.06f), 0.2f, 0.18f,
					h.WallMaterial == StructureMaterial.Stone ? StructureMaterial.Stone : StructureMaterial.Timber, key + 21, StructureRole.Detail));
			}
			if (d.Mid && h.Windows > 0)
			{
				int n = h.Windows;
				for (int i = 0; i < n; i++)
				{
					// Spread across the front, skipping the door.
					float t = (i + 0.5f) / n;
					float x = Mathf.Lerp(-h.Width * 0.5f + 0.8f, h.Width * 0.5f - 0.8f, t);
					if (Mathf.Abs(x - doorX) < h.DoorWidth * 0.5f + 0.6f)
					{
						x = x < doorX ? doorX - h.DoorWidth * 0.5f - 0.75f : doorX + h.DoorWidth * 0.5f + 0.75f;
					}
					if (Mathf.Abs(x) > h.Width * 0.5f - 0.5f)
					{
						continue;
					}
					Window(d, new Vector3(x, floor + h.DoorHeight * 0.62f, front), Vector3.back, key + 30 + i * 4);
				}
				if (h.Depth > 4f)
				{
					Window(d, new Vector3(-h.Width * 0.5f, floor + h.DoorHeight * 0.62f, 0f), Vector3.left, key + 60);
					Window(d, new Vector3(h.Width * 0.5f, floor + h.DoorHeight * 0.62f, 0f), Vector3.right, key + 64);
				}
			}
			if (h.Frame && d.Mid)
			{
				Framing(d, h.Width, h.Depth, floor, floor + h.WallHeight, key + 200);
			}
			if (h.Chimney)
			{
				float cx = h.RidgeAlongX ? h.Width * 0.5f - 0.9f : h.Width * 0.25f;
				float cz = h.RidgeAlongX ? 0.35f : h.Depth * 0.5f - 0.9f;
				float chimneyTop = top + rise + 0.9f;
				d.Add(StructureShapes.ChamferBox(new Vector3(cx, (floor + chimneyTop) * 0.5f, cz), new Vector3(0.7f, chimneyTop - floor, 0.7f), d.Bevel(0.03f),
					StructureMaterial.Fieldstone, key + 15));
			}
		}

		/// <summary>A window on a wall face at <paramref name="at"/>, facing <paramref name="outward"/>: a dark pane, a sill, a lintel.</summary>
		internal static void Window(StructureDraft d, Vector3 at, Vector3 outward, int key)
		{
			Quaternion turn = Quaternion.FromToRotation(Vector3.back, outward);
			d.Add(StructureShapes.Block(at + outward * 0.02f, new Vector3(0.6f, 0.75f, 0.06f), turn, StructureMaterial.Iron, key, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.06f + Vector3.down * 0.42f, new Vector3(0.8f, 0.08f, 0.14f), turn, StructureMaterial.Timber, key + 1, StructureRole.Detail));
			d.Add(StructureShapes.Block(at + outward * 0.05f + Vector3.up * 0.42f, new Vector3(0.8f, 0.1f, 0.1f), turn, StructureMaterial.Timber, key + 2, StructureRole.Detail));
			if (d.Fine)
			{
				// One shutter swung open against the wall.
				d.Add(StructureShapes.Block(at + outward * 0.05f + turn * new Vector3(0.62f, 0f, 0f), new Vector3(0.32f, 0.75f, 0.04f), turn,
					StructureMaterial.Timber, key + 3, StructureRole.Detail)).Grain = Vector3.up;
			}
		}

		/// <summary>Half-timbering on four walls: corner posts, sill and head plates, studs and braces at level 0.</summary>
		internal static void Framing(StructureDraft d, float width, float depth, float y0, float y1, int key)
		{
			const float post = 0.2f, proud = 0.04f;
			float hx = width * 0.5f + proud - post * 0.5f, hz = depth * 0.5f + proud - post * 0.5f;
			int k = key;
			for (int sx = -1; sx <= 1; sx += 2)
			{
				for (int sz = -1; sz <= 1; sz += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(sx * hx, y0, sz * hz), new Vector3(sx * hx, y1, sz * hz), post, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				}
			}
			foreach (float y in new[] { y0 + 0.1f, y1 - 0.1f })
			{
				d.Add(StructureShapes.Beam(new Vector3(-hx, y, -hz), new Vector3(hx, y, -hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-hx, y, hz), new Vector3(hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(-hx, y, -hz), new Vector3(-hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(hx, y, -hz), new Vector3(hx, y, hz), 0.18f, post, StructureMaterial.Timber, k++, StructureRole.Detail));
			}
			if (!d.Fine)
			{
				return;
			}
			// Studs and a brace in each bay of the long walls.
			int bays = Mathf.Max(1, Mathf.RoundToInt(width / 1.8f));
			for (int side = -1; side <= 1; side += 2)
			{
				float z = side * hz;
				for (int b = 1; b < bays; b++)
				{
					float x = -hx + 2f * hx * b / bays;
					d.Add(StructureShapes.Beam(new Vector3(x, y0 + 0.2f, z), new Vector3(x, y1 - 0.2f, z), 0.14f, post * 0.9f, StructureMaterial.Timber, k++, StructureRole.Detail));
				}
				d.Add(StructureShapes.Beam(new Vector3(-hx + 0.1f, y0 + 0.2f, z), new Vector3(-hx + 2f * hx / bays - 0.1f, y1 - 0.25f, z), 0.12f, post * 0.85f,
					StructureMaterial.Timber, k++, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(hx - 0.1f, y0 + 0.2f, z), new Vector3(hx - 2f * hx / bays + 0.1f, y1 - 0.25f, z), 0.12f, post * 0.85f,
					StructureMaterial.Timber, k++, StructureRole.Detail));
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
