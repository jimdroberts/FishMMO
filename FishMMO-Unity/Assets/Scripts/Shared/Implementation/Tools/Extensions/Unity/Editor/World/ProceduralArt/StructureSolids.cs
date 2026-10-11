#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a structure part is made of: one material (and one submesh) each.</summary>
	/// <remarks>Append-only: the order is the submesh order of every structure mesh.</remarks>
	public enum StructureMaterial
	{
		Timber,
		Plaster,
		Thatch,
		/// <summary>Dressed stone: ashlar blocks in courses (masonry: walls, plinth bases, wells).</summary>
		Stone,
		/// <summary>Rough, undressed stone: megaliths, fire rings, rubble, cairns.</summary>
		Fieldstone,
		/// <summary>Undyed canvas or hide (cloth, first tint).</summary>
		Canvas,
		/// <summary>Dyed cloth (cloth, second tint): banners, awnings, pavilions.</summary>
		Dyed,
		Iron,
		/// <summary>Packed soil: grave mounds, crypt banks.</summary>
		Earth,
		Bone,
		/// <summary>Ash and charcoal in a fire pit.</summary>
		Ash,
		/// <summary>
		/// Stone carved or raised in one piece — statues, headstones, obelisks, columns, menhirs, altar slabs: a weathered
		/// face with no block joints, where ashlar's courses would read as a statue built of bricks.
		/// </summary>
		Monolith,
	}

	/// <summary>What a part does in its structure, which decides how a ruin treats it.</summary>
	public enum StructureRole
	{
		/// <summary>Load-bearing: walls, posts, columns, plinths. Cut and broken by a ruin, never simply dropped.</summary>
		Structure,
		/// <summary>A roof or canopy: the first thing a ruin loses.</summary>
		Roof,
		/// <summary>Trim, doors, shutters, railings: lost above a ruin's break line.</summary>
		Detail,
		/// <summary>Fallen pieces and rubble a ruin adds; never cut again.</summary>
		Rubble,
	}

	/// <summary>One flat face of a <see cref="StructureSolid"/>: a polygon wound counter-clockwise seen from outside.</summary>
	public sealed class StructureFace
	{
		public readonly List<Vector3> Points = new List<Vector3>();
		/// <summary>Per-point shading normals (a lathe's smooth sides), or null for a flat face that shades by <see cref="Normal"/>.</summary>
		public List<Vector3> Normals;
		/// <summary>Per-point texture coordinates in metres (a lathe's wrapped sides), or null: projected from the face's plane.</summary>
		public List<Vector2> UVs;
		/// <summary>The outward unit normal of the face's plane.</summary>
		public Vector3 Normal;
		/// <summary>True when the polygon may be concave (a cap across a concave section): triangulated from its centroid.</summary>
		public bool Concave;

		public StructureFace Clone()
		{
			var f = new StructureFace { Normal = Normal, Concave = Concave };
			f.Points.AddRange(Points);
			if (Normals != null) f.Normals = new List<Vector3>(Normals);
			if (UVs != null) f.UVs = new List<Vector2>(UVs);
			return f;
		}
	}

	/// <summary>
	/// A closed solid a structure is assembled from: a box, a prism, a lathe, or any of them cut by planes. Every face is
	/// planar and the faces meet edge to edge, so the solid is watertight on its own; a structure is a union of them, which
	/// may overlap (a beam through a post), as hand-made kit pieces do.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why solids, not a mesh.</b> A ruin is made FROM the intact piece (Jim, 2026-10-10: cut planes, tilt and missing
	/// chunks by a decay parameter), and a solid can be cut by a plane and stay closed: <see cref="Clip"/> keeps one side
	/// and caps the opening, so a broken wall shows a broken top, not a hole into nothing. Clipping a box by its twelve
	/// edge planes is also how the bevels are made (<see cref="StructureShapes.ChamferBox"/>).
	/// </para>
	/// <para>
	/// <b>Bit-identical seams.</b> Where a cut crosses an edge, both faces sharing the edge compute the crossing from the
	/// edge's endpoints in one canonical order, so the two get the same point to the last bit and the cap's outline
	/// chains exactly; nothing is welded by tolerance.
	/// </para>
	/// </remarks>
	public sealed class StructureSolid
	{
		public readonly List<StructureFace> Faces = new List<StructureFace>();
		public StructureMaterial Material;
		public StructureRole Role;
		/// <summary>
		/// The part's identity, the same at every level of detail: a ruin seeds each part's own decisions from it, so the
		/// levels of a ruin break alike although the lower ones have fewer parts.
		/// </summary>
		public int Key;
		/// <summary>The direction a planar face's texture u runs when the face allows it (wood grain along a beam); zero = the default.</summary>
		public Vector3 Grain;
		/// <summary>Added to every texture coordinate, so neighbouring parts do not repeat the same patch of texture.</summary>
		public Vector2 UvOffset;

		public StructureSolid Clone()
		{
			var s = new StructureSolid { Material = Material, Role = Role, Key = Key, Grain = Grain, UvOffset = UvOffset };
			foreach (StructureFace f in Faces)
			{
				s.Faces.Add(f.Clone());
			}
			return s;
		}

		public Bounds Bounds
		{
			get
			{
				bool any = false;
				Vector3 min = Vector3.zero, max = Vector3.zero;
				foreach (StructureFace f in Faces)
				{
					foreach (Vector3 p in f.Points)
					{
						if (!any) { min = max = p; any = true; continue; }
						min = Vector3.Min(min, p);
						max = Vector3.Max(max, p);
					}
				}
				var b = new Bounds();
				b.SetMinMax(min, max);
				return b;
			}
		}

		/// <summary>The mean of every face point: inside a convex solid, near the middle of any other.</summary>
		public Vector3 Centroid
		{
			get
			{
				Vector3 sum = Vector3.zero;
				int n = 0;
				foreach (StructureFace f in Faces)
				{
					foreach (Vector3 p in f.Points)
					{
						sum += p;
						n++;
					}
				}
				return n > 0 ? sum / n : Vector3.zero;
			}
		}

		/// <summary>Moves, turns or scales the solid; normals by the inverse transpose, so a non-uniform scale shades right.</summary>
		public StructureSolid Transform(Matrix4x4 m)
		{
			Matrix4x4 nm = m.inverse.transpose;
			foreach (StructureFace f in Faces)
			{
				for (int i = 0; i < f.Points.Count; i++)
				{
					f.Points[i] = m.MultiplyPoint3x4(f.Points[i]);
				}
				if (f.Normals != null)
				{
					for (int i = 0; i < f.Normals.Count; i++)
					{
						f.Normals[i] = Unit(nm.MultiplyVector(f.Normals[i]), f.Normals[i]);
					}
				}
				f.Normal = Unit(nm.MultiplyVector(f.Normal), f.Normal);
			}
			Grain = Grain.sqrMagnitude > 0f ? Unit(m.MultiplyVector(Grain), Grain) : Grain;
			// A reflection turns every polygon inside out.
			if (Determinant3(m) < 0f)
			{
				foreach (StructureFace f in Faces)
				{
					f.Points.Reverse();
					f.Normals?.Reverse();
					f.UVs?.Reverse();
				}
			}
			return this;
		}

		private static float Determinant3(Matrix4x4 m)
		{
			return m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);
		}

		private static Vector3 Unit(Vector3 v, Vector3 fallback)
		{
			float m = v.magnitude;
			return m > 1e-12f ? v / m : fallback;
		}

		// ── Cutting ───────────────────────────────────────────────────

		private const float OnPlane = 1e-5f;

		/// <summary>
		/// Keeps the part of the solid where <c>dot(normal, p) ≤ distance</c> and closes the cut with a cap facing along
		/// <paramref name="normal"/>. False when nothing is left (the solid is then empty).
		/// </summary>
		public bool Clip(Vector3 normal, float distance)
		{
			normal = normal.normalized;
			var kept = new List<StructureFace>(Faces.Count + 1);
			var capEdges = new List<KeyValuePair<Vector3, Vector3>>();
			bool cut = false;
			foreach (StructureFace face in Faces)
			{
				StructureFace clipped = ClipFace(face, normal, distance, out List<bool> on, out bool changed);
				cut |= changed;
				if (clipped == null)
				{
					cut = true;
					continue;
				}
				bool allOn = true;
				foreach (bool b in on)
				{
					allOn &= b;
				}
				if (allOn)
				{
					// A face lying in the cutting plane: the cap stands in for it.
					cut = true;
					continue;
				}
				int n = clipped.Points.Count;
				for (int i = 0; i < n; i++)
				{
					int j = (i + 1) % n;
					if (on[i] && on[j])
					{
						// The cap runs the shared edge the other way.
						capEdges.Add(new KeyValuePair<Vector3, Vector3>(clipped.Points[j], clipped.Points[i]));
					}
				}
				kept.Add(clipped);
			}
			Faces.Clear();
			if (kept.Count == 0)
			{
				return false;
			}
			Faces.AddRange(kept);
			if (cut && capEdges.Count >= 3)
			{
				foreach (List<Vector3> loop in CapLoops(capEdges, normal))
				{
					var cap = new StructureFace { Normal = normal };
					cap.Points.AddRange(loop);
					cap.Concave = !IsConvex(loop, normal);
					Faces.Add(cap);
				}
			}
			return Faces.Count >= 2;
		}

		private static StructureFace ClipFace(StructureFace face, Vector3 n, float d, out List<bool> on, out bool changed)
		{
			int count = face.Points.Count;
			var s = new float[count];
			bool anyOut = false;
			for (int i = 0; i < count; i++)
			{
				s[i] = Vector3.Dot(n, face.Points[i]) - d;
				anyOut |= s[i] > OnPlane;
			}
			var result = new StructureFace { Normal = face.Normal, Concave = face.Concave };
			if (face.Normals != null) result.Normals = new List<Vector3>(count + 2);
			if (face.UVs != null) result.UVs = new List<Vector2>(count + 2);
			on = new List<bool>(count + 2);
			changed = anyOut;
			for (int i = 0; i < count; i++)
			{
				int j = (i + 1) % count;
				bool inI = s[i] <= OnPlane;
				if (inI)
				{
					Add(result, on, face, i, Mathf.Abs(s[i]) <= OnPlane);
				}
				if ((s[i] < -OnPlane && s[j] > OnPlane) || (s[i] > OnPlane && s[j] < -OnPlane))
				{
					// Canonical order, so the face on the other side of this edge computes the same point.
					bool swap = Less(face.Points[j], face.Points[i]);
					int a = swap ? j : i, b = swap ? i : j;
					float t = s[a] / (s[a] - s[b]);
					Vector3 p = face.Points[a] + (face.Points[b] - face.Points[a]) * t;
					result.Points.Add(p);
					result.Normals?.Add((face.Normals[a] + (face.Normals[b] - face.Normals[a]) * t).normalized);
					result.UVs?.Add(face.UVs[a] + (face.UVs[b] - face.UVs[a]) * t);
					on.Add(true);
				}
			}
			Dedupe(result, on);
			return result.Points.Count >= 3 ? result : null;
		}

		private static void Add(StructureFace to, List<bool> on, StructureFace from, int i, bool onPlane)
		{
			to.Points.Add(from.Points[i]);
			to.Normals?.Add(from.Normals[i]);
			to.UVs?.Add(from.UVs[i]);
			on.Add(onPlane);
		}

		/// <summary>Drops a point equal to the one before it (a crossing exactly at a corner), keeping the on-plane mark.</summary>
		private static void Dedupe(StructureFace f, List<bool> on)
		{
			for (int i = f.Points.Count - 1; i >= 0 && f.Points.Count > 0; i--)
			{
				int prev = (i - 1 + f.Points.Count) % f.Points.Count;
				if (prev == i || (f.Points[i] - f.Points[prev]).sqrMagnitude > 1e-12f)
				{
					continue;
				}
				on[prev] = on[prev] || on[i];
				f.Points.RemoveAt(i);
				f.Normals?.RemoveAt(i);
				f.UVs?.RemoveAt(i);
				on.RemoveAt(i);
			}
		}

		private static bool Less(Vector3 a, Vector3 b)
		{
			if (a.x != b.x) return a.x < b.x;
			if (a.y != b.y) return a.y < b.y;
			return a.z < b.z;
		}

		private static (long, long, long) Key5(Vector3 p) => ((long)Math.Round(p.x * 1e5), (long)Math.Round(p.y * 1e5), (long)Math.Round(p.z * 1e5));

		/// <summary>The cap's outlines: the on-plane edges chained end to start; a convex hull of their points if they will not chain.</summary>
		private static List<List<Vector3>> CapLoops(List<KeyValuePair<Vector3, Vector3>> edges, Vector3 normal)
		{
			var loops = new List<List<Vector3>>();
			var byStart = new Dictionary<(long, long, long), List<int>>();
			for (int i = 0; i < edges.Count; i++)
			{
				(long, long, long) k = Key5(edges[i].Key);
				if (!byStart.TryGetValue(k, out List<int> list))
				{
					byStart[k] = list = new List<int>(1);
				}
				list.Add(i);
			}
			var used = new bool[edges.Count];
			bool broken = false;
			for (int start = 0; start < edges.Count && !broken; start++)
			{
				if (used[start])
				{
					continue;
				}
				var loop = new List<Vector3>();
				int e = start;
				(long, long, long) home = Key5(edges[start].Key);
				for (int guard = 0; guard <= edges.Count; guard++)
				{
					used[e] = true;
					loop.Add(edges[e].Key);
					(long, long, long) next = Key5(edges[e].Value);
					if (next.Equals(home))
					{
						break;
					}
					int found = -1;
					if (byStart.TryGetValue(next, out List<int> candidates))
					{
						foreach (int c in candidates)
						{
							if (!used[c]) { found = c; break; }
						}
					}
					if (found < 0)
					{
						broken = true;
						break;
					}
					e = found;
				}
				if (!broken && loop.Count >= 3)
				{
					loops.Add(loop);
				}
			}
			if (!broken)
			{
				return loops;
			}
			// A cut the edges do not close (a degenerate sliver): the hull of the points still seals a convex solid.
			var points = new List<Vector3>();
			foreach (KeyValuePair<Vector3, Vector3> edge in edges)
			{
				points.Add(edge.Key);
				points.Add(edge.Value);
			}
			List<Vector3> hull = PlanarHull(points, normal);
			loops.Clear();
			if (hull.Count >= 3)
			{
				loops.Add(hull);
			}
			return loops;
		}

		/// <summary>The convex hull of points on a plane, counter-clockwise about <paramref name="normal"/>.</summary>
		public static List<Vector3> PlanarHull(List<Vector3> points, Vector3 normal)
		{
			StructureShapes.Basis(normal, out Vector3 u, out Vector3 v);
			var sorted = new List<Vector3>(points);
			sorted.Sort((a, b) =>
			{
				float au = Vector3.Dot(a, u), bu = Vector3.Dot(b, u);
				if (au != bu) return au.CompareTo(bu);
				return Vector3.Dot(a, v).CompareTo(Vector3.Dot(b, v));
			});
			var hull = new List<Vector3>();
			for (int pass = 0; pass < 2; pass++)
			{
				int floor = hull.Count;
				for (int k = 0; k < sorted.Count; k++)
				{
					Vector3 p = sorted[pass == 0 ? k : sorted.Count - 1 - k];
					while (hull.Count >= floor + 2 && Cross2(hull[hull.Count - 2], hull[hull.Count - 1], p, u, v) <= 1e-10f)
					{
						hull.RemoveAt(hull.Count - 1);
					}
					hull.Add(p);
				}
				hull.RemoveAt(hull.Count - 1);
			}
			return hull;
		}

		private static float Cross2(Vector3 o, Vector3 a, Vector3 b, Vector3 u, Vector3 v)
		{
			float ax = Vector3.Dot(a - o, u), ay = Vector3.Dot(a - o, v);
			float bx = Vector3.Dot(b - o, u), by = Vector3.Dot(b - o, v);
			return ax * by - ay * bx;
		}

		private static bool IsConvex(List<Vector3> loop, Vector3 normal)
		{
			int n = loop.Count;
			for (int i = 0; i < n; i++)
			{
				Vector3 a = loop[i], b = loop[(i + 1) % n], c = loop[(i + 2) % n];
				if (Vector3.Dot(Vector3.Cross(b - a, c - b), normal) < -1e-9f)
				{
					return false;
				}
			}
			return true;
		}
	}

	/// <summary>
	/// The shapes structures are built from — boxes, bevelled boxes, prisms, lathes — and the one call that turns a list of
	/// solids into a mesh, one submesh per material.
	/// </summary>
	public static class StructureShapes
	{
		/// <summary>Two unit axes across a plane with this normal; the same pair for the same normal every time.</summary>
		public static void Basis(Vector3 normal, out Vector3 u, out Vector3 v)
		{
			Vector3 n = normal.normalized;
			Vector3 seed = Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
			u = Vector3.Cross(seed, n).normalized;
			v = Vector3.Cross(n, u);
		}

		/// <summary>The outward normal of a polygon by Newell's method, unnormalised (twice its area).</summary>
		public static Vector3 Newell(List<Vector3> p)
		{
			Vector3 n = Vector3.zero;
			for (int i = 0; i < p.Count; i++)
			{
				Vector3 a = p[i], b = p[(i + 1) % p.Count];
				n.x += (a.y - b.y) * (a.z + b.z);
				n.y += (a.z - b.z) * (a.x + b.x);
				n.z += (a.x - b.x) * (a.y + b.y);
			}
			return n;
		}

		/// <summary>A flat face through these points, wound to face <paramref name="outward"/>.</summary>
		public static StructureFace Face(Vector3 outward, params Vector3[] points)
		{
			var f = new StructureFace();
			foreach (Vector3 p in points)
			{
				// A corner given twice (a wedge closing on its axis) is one corner.
				if (f.Points.Count == 0 || (f.Points[f.Points.Count - 1] - p).sqrMagnitude > 1e-12f)
				{
					f.Points.Add(p);
				}
			}
			if (f.Points.Count > 1 && (f.Points[0] - f.Points[f.Points.Count - 1]).sqrMagnitude <= 1e-12f)
			{
				f.Points.RemoveAt(f.Points.Count - 1);
			}
			Vector3 n = Newell(f.Points);
			if (Vector3.Dot(n, outward) < 0f)
			{
				f.Points.Reverse();
				n = -n;
			}
			f.Normal = n.normalized;
			return f;
		}

		private static StructureSolid New(StructureMaterial material, StructureRole role, int key)
		{
			return new StructureSolid { Material = material, Role = role, Key = key };
		}

		/// <summary>An axis-aligned box from its centre and size.</summary>
		public static StructureSolid Box(Vector3 centre, Vector3 size, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			Vector3 h = size * 0.5f;
			var c = new Vector3[8];
			for (int i = 0; i < 8; i++)
			{
				c[i] = centre + new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
			}
			StructureSolid s = New(material, role, key);
			s.Faces.Add(Face(Vector3.left, c[0], c[2], c[6], c[4]));
			s.Faces.Add(Face(Vector3.right, c[1], c[3], c[7], c[5]));
			s.Faces.Add(Face(Vector3.down, c[0], c[1], c[5], c[4]));
			s.Faces.Add(Face(Vector3.up, c[2], c[3], c[7], c[6]));
			s.Faces.Add(Face(Vector3.back, c[0], c[1], c[3], c[2]));
			s.Faces.Add(Face(Vector3.forward, c[4], c[5], c[7], c[6]));
			return s;
		}

		/// <summary>A box spanning two corners.</summary>
		public static StructureSolid BoxBetween(Vector3 min, Vector3 max, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			return Box((min + max) * 0.5f, max - min, material, key, role);
		}

		/// <summary>
		/// A box with every edge chamfered by <paramref name="bevel"/> metres (none when it is zero or the box is too thin):
		/// the box clipped by its twelve edge planes, so the corners close themselves.
		/// </summary>
		public static StructureSolid ChamferBox(Vector3 centre, Vector3 size, float bevel, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			StructureSolid s = Box(centre, size, material, key, role);
			bevel = Mathf.Min(bevel, 0.45f * Mathf.Min(size.x, Mathf.Min(size.y, size.z)));
			if (bevel <= 0.005f)
			{
				return s;
			}
			Vector3 h = size * 0.5f;
			const float r = 0.70710678f;
			for (int axis = 0; axis < 3; axis++)
			{
				int a = (axis + 1) % 3, b = (axis + 2) % 3;
				for (int sa = -1; sa <= 1; sa += 2)
				{
					for (int sb = -1; sb <= 1; sb += 2)
					{
						var n = Vector3.zero;
						n[a] = sa * r;
						n[b] = sb * r;
						float d = (h[a] + h[b] - bevel) * r + Vector3.Dot(n, centre);
						s.Clip(n, d);
					}
				}
			}
			return s;
		}

		/// <summary>
		/// A prism: a polygon in the local x-y plane (counter-clockwise or not) extruded from z0 to z1. A concave outline is
		/// allowed for its end caps (triangulated from the centroid, so it must be star-shaped about it).
		/// </summary>
		public static StructureSolid Prism(IList<Vector2> outline, float z0, float z1, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			StructureSolid s = New(material, role, key);
			int n = outline.Count;
			var front = new Vector3[n];
			var back = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				front[i] = new Vector3(outline[i].x, outline[i].y, z0);
				back[i] = new Vector3(outline[i].x, outline[i].y, z1);
			}
			// Winding of the outline, so each side faces away from it.
			float area = 0f;
			for (int i = 0; i < n; i++)
			{
				Vector2 a = outline[i], b = outline[(i + 1) % n];
				area += a.x * b.y - b.x * a.y;
			}
			float sign = area >= 0f ? 1f : -1f;
			float zs = z1 >= z0 ? 1f : -1f;
			StructureFace f0 = Face(new Vector3(0f, 0f, -zs), front);
			StructureFace f1 = Face(new Vector3(0f, 0f, zs), back);
			bool convex = IsConvex(outline);
			f0.Concave = f1.Concave = !convex;
			s.Faces.Add(f0);
			s.Faces.Add(f1);
			for (int i = 0; i < n; i++)
			{
				int j = (i + 1) % n;
				Vector2 e = outline[j] - outline[i];
				var outward = new Vector3(e.y * sign, -e.x * sign, 0f);
				if (e.sqrMagnitude < 1e-12f)
				{
					continue;
				}
				s.Faces.Add(Face(outward, front[i], front[j], back[j], back[i]));
			}
			return s;
		}

		private static bool IsConvex(IList<Vector2> p)
		{
			int n = p.Count, sign = 0;
			for (int i = 0; i < n; i++)
			{
				Vector2 a = p[i], b = p[(i + 1) % n], c = p[(i + 2) % n];
				float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
				int s = cross > 1e-9f ? 1 : cross < -1e-9f ? -1 : 0;
				if (s == 0) continue;
				if (sign == 0) sign = s;
				else if (s != sign) return false;
			}
			return true;
		}

		/// <summary>
		/// A solid of revolution about the y axis: <paramref name="profile"/> is (radius, height) pairs from bottom to top,
		/// outside first. An end off the axis is capped flat; one on the axis closes to a point. A <paramref name="closed"/>
		/// profile is a loop (an annulus: up the outside, down the inside), with no caps.
		/// </summary>
		/// <param name="smooth">Smooth round the axis (a column); false shades every facet flat (an obelisk, a hut of planks).</param>
		/// <param name="phase">Turns the facets about the axis, radians.</param>
		/// <param name="grainAlongAxis">Texture u runs up the axis (a log's grain) rather than round it.</param>
		public static StructureSolid Lathe(IList<Vector2> profile, int segments, StructureMaterial material, int key,
			bool smooth = true, float phase = 0f, bool closed = false, bool grainAlongAxis = false, StructureRole role = StructureRole.Structure)
		{
			StructureSolid s = New(material, role, key);
			segments = Mathf.Max(3, segments);
			int n = profile.Count;
			int bands = closed ? n : n - 1;
			float along = 0f;
			for (int i = 0; i < bands; i++)
			{
				Vector2 p0 = profile[i], p1 = profile[(i + 1) % n];
				float length = (p1 - p0).magnitude;
				if (length < 1e-6f)
				{
					continue;
				}
				// Outward in the profile plane: the profile runs up the outside.
				var pn = new Vector2(p1.y - p0.y, -(p1.x - p0.x)) / length;
				float mean = 0.5f * (p0.x + p1.x);
				for (int k = 0; k < segments; k++)
				{
					float a0 = phase + k * 2f * Mathf.PI / segments, a1 = phase + (k + 1) * 2f * Mathf.PI / segments;
					float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
					var q00 = new Vector3(p0.x * c0, p0.y, p0.x * s0);
					var q01 = new Vector3(p0.x * c1, p0.y, p0.x * s1);
					var q11 = new Vector3(p1.x * c1, p1.y, p1.x * s1);
					var q10 = new Vector3(p1.x * c0, p1.y, p1.x * s0);
					float am = 0.5f * (a0 + a1);
					var outward = new Vector3(pn.x * Mathf.Cos(am), pn.y, pn.x * Mathf.Sin(am));
					var n0 = new Vector3(pn.x * c0, pn.y, pn.x * s0);
					var n1 = new Vector3(pn.x * c1, pn.y, pn.x * s1);
					float u0 = (a0 - phase) * mean, u1 = (a1 - phase) * mean;
					Vector2 Uv(float u, float v) => grainAlongAxis ? new Vector2(v, u) : new Vector2(u, v);
					var f = new StructureFace();
					var normals = new List<Vector3>(4);
					var uvs = new List<Vector2>(4);
					void Put(Vector3 p, Vector3 nrm, Vector2 uv)
					{
						f.Points.Add(p);
						normals.Add(nrm);
						uvs.Add(uv);
					}
					bool apex0 = p0.x < 1e-6f, apex1 = p1.x < 1e-6f;
					if (apex0 && apex1)
					{
						continue;
					}
					if (apex0)
					{
						Put(new Vector3(0f, p0.y, 0f), Vector3.Lerp(n0, n1, 0.5f).normalized, Uv(0.5f * (u0 + u1), along));
					}
					else
					{
						Put(q00, n0, Uv(u0, along));
						Put(q01, n1, Uv(u1, along));
					}
					if (apex1)
					{
						Put(new Vector3(0f, p1.y, 0f), Vector3.Lerp(n0, n1, 0.5f).normalized, Uv(0.5f * (u0 + u1), along + length));
					}
					else
					{
						Put(q11, n1, Uv(u1, along + length));
						Put(q10, n0, Uv(u0, along + length));
					}
					Vector3 nn = Newell(f.Points);
					if (nn.sqrMagnitude < 1e-16f)
					{
						continue;
					}
					if (Vector3.Dot(nn, outward) < 0f)
					{
						f.Points.Reverse();
						normals.Reverse();
						uvs.Reverse();
						nn = -nn;
					}
					f.Normal = nn.normalized;
					f.Normals = smooth ? normals : null;
					f.UVs = uvs;
					s.Faces.Add(f);
				}
				along += length;
			}
			if (!closed)
			{
				Cap(s, profile[0], segments, phase, false);
				Cap(s, profile[n - 1], segments, phase, true);
			}
			return s;
		}

		private static void Cap(StructureSolid s, Vector2 end, int segments, float phase, bool top)
		{
			if (end.x < 1e-6f)
			{
				return;
			}
			var points = new Vector3[segments];
			for (int k = 0; k < segments; k++)
			{
				float a = phase + k * 2f * Mathf.PI / segments;
				points[k] = new Vector3(end.x * Mathf.Cos(a), end.y, end.x * Mathf.Sin(a));
			}
			s.Faces.Add(Face(top ? Vector3.up : Vector3.down, points));
		}

		/// <summary>A cylinder about the y axis from <paramref name="y0"/> to <paramref name="y1"/>.</summary>
		public static StructureSolid Cylinder(float radius, float y0, float y1, int segments, StructureMaterial material, int key,
			bool grainAlongAxis = false, StructureRole role = StructureRole.Structure)
		{
			return Lathe(new[] { new Vector2(radius, y0), new Vector2(radius, y1) }, segments, material, key, true, 0f, false, grainAlongAxis, role);
		}

		/// <summary>A cylinder from point <paramref name="a"/> to point <paramref name="b"/> (a pole, a log, a bone).</summary>
		public static StructureSolid Rod(Vector3 a, Vector3 b, float radius, int segments, StructureMaterial material, int key,
			StructureRole role = StructureRole.Structure, float tipLength = 0f)
		{
			float length = (b - a).magnitude;
			var profile = new List<Vector2> { new Vector2(radius, 0f), new Vector2(radius, length - tipLength) };
			if (tipLength > 0f)
			{
				profile.Add(new Vector2(0f, length));
			}
			StructureSolid s = Lathe(profile, segments, material, key, true, 0f, false, true, role);
			Quaternion q = Quaternion.FromToRotation(Vector3.up, (b - a) / Mathf.Max(1e-6f, length));
			return s.Transform(Matrix4x4.TRS(a, q, Vector3.one));
		}

		/// <summary>A sphere (or, scaled, an ellipsoid) about a centre.</summary>
		public static StructureSolid Sphere(Vector3 centre, Vector3 radii, int segments, int rings, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			var profile = new List<Vector2>(rings + 1);
			for (int i = 0; i <= rings; i++)
			{
				float a = -0.5f * Mathf.PI + Mathf.PI * i / rings;
				profile.Add(new Vector2(i == 0 || i == rings ? 0f : Mathf.Cos(a), Mathf.Sin(a)));
			}
			return Lathe(profile, segments, material, key, true, 0f, false, false, role).Transform(Matrix4x4.TRS(centre, Quaternion.identity, radii));
		}

		/// <summary>An oriented box: <paramref name="size"/> about the origin, turned by <paramref name="rotation"/> and moved to <paramref name="centre"/>.</summary>
		public static StructureSolid Block(Vector3 centre, Vector3 size, Quaternion rotation, StructureMaterial material, int key, StructureRole role = StructureRole.Structure, float bevel = 0f)
		{
			StructureSolid s = bevel > 0f ? ChamferBox(Vector3.zero, size, bevel, material, key, role) : Box(Vector3.zero, size, material, key, role);
			return s.Transform(Matrix4x4.TRS(centre, rotation, Vector3.one));
		}

		/// <summary>A beam of square section from <paramref name="a"/> to <paramref name="b"/>, grain along it.</summary>
		public static StructureSolid Beam(Vector3 a, Vector3 b, float width, float depth, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			Vector3 d = b - a;
			float length = d.magnitude;
			Vector3 dir = d / Mathf.Max(1e-6f, length);
			Vector3 side = Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.Cross(Vector3.up, dir).normalized;
			Vector3 up = Vector3.Cross(dir, side);
			var m = new Matrix4x4();
			m.m00 = side.x; m.m10 = side.y; m.m20 = side.z;
			m.m01 = up.x; m.m11 = up.y; m.m21 = up.z;
			m.m02 = dir.x; m.m12 = dir.y; m.m22 = dir.z;
			Vector3 mid = (a + b) * 0.5f;
			m.m03 = mid.x; m.m13 = mid.y; m.m23 = mid.z; m.m33 = 1f;
			StructureSolid s = Box(Vector3.zero, new Vector3(width, depth, length), material, key, role).Transform(m);
			s.Grain = dir;
			return s;
		}

		// ── To a mesh ─────────────────────────────────────────────────

		/// <summary>
		/// The solids as one mesh with a submesh per material used, in <see cref="StructureMaterial"/> order;
		/// <paramref name="materials"/> answers which. Flat faces shade flat, lathe sides smooth; planar faces are
		/// textured by their plane in metres (u along the part's grain or the face's horizontal, v across it).
		/// </summary>
		public static MeshBuilder ToMesh(List<StructureSolid> solids, out StructureMaterial[] materials)
		{
			var used = new SortedSet<StructureMaterial>();
			foreach (StructureSolid s in solids)
			{
				if (s.Faces.Count > 0)
				{
					used.Add(s.Material);
				}
			}
			materials = new StructureMaterial[used.Count];
			used.CopyTo(materials);
			var builder = new MeshBuilder(Mathf.Max(1, materials.Length));
			var white = new Color32(255, 255, 255, 255);
			foreach (StructureSolid s in solids)
			{
				int submesh = Array.IndexOf(materials, s.Material);
				foreach (StructureFace f in s.Faces)
				{
					int count = f.Points.Count;
					if (count < 3)
					{
						continue;
					}
					PlanarAxes(f.Normal, s.Grain, out Vector3 ua, out Vector3 va);
					int first = builder.VertexCount;
					for (int i = 0; i < count; i++)
					{
						Vector3 p = f.Points[i];
						Vector2 uv = f.UVs != null ? f.UVs[i] : new Vector2(Vector3.Dot(p, ua), Vector3.Dot(p, va));
						Vector3 normal = f.Normals != null ? f.Normals[i] : f.Normal;
						builder.AddVertex(p, normal, uv + s.UvOffset, white);
					}
					if (f.Concave)
					{
						Vector3 c = Vector3.zero;
						foreach (Vector3 p in f.Points) c += p;
						c /= count;
						int centre = builder.AddVertex(c, f.Normal, new Vector2(Vector3.Dot(c, ua), Vector3.Dot(c, va)) + s.UvOffset, white);
						for (int i = 0; i < count; i++)
						{
							AddTriangle(builder, submesh, centre, first + i, first + (i + 1) % count, f.Normal);
						}
					}
					else
					{
						for (int i = 1; i + 1 < count; i++)
						{
							AddTriangle(builder, submesh, first, first + i, first + i + 1, f.Normal);
						}
					}
				}
			}
			builder.RecalculateTangents();
			return builder;
		}

		private static void AddTriangle(MeshBuilder b, int submesh, int i0, int i1, int i2, Vector3 facing)
		{
			Vector3 cross = Vector3.Cross(b.Positions[i1] - b.Positions[i0], b.Positions[i2] - b.Positions[i0]);
			// A sliver left by a cut through a corner: no area to draw, and the mesh check refuses it.
			if (cross.sqrMagnitude < 1e-11f)
			{
				return;
			}
			b.AddTriangle(submesh, i0, i1, i2, facing);
		}

		/// <summary>The texture axes of a flat face: u along the grain where the face allows, else along its horizontal.</summary>
		public static void PlanarAxes(Vector3 normal, Vector3 grain, out Vector3 u, out Vector3 v)
		{
			if (grain.sqrMagnitude > 0f)
			{
				Vector3 g = grain - normal * Vector3.Dot(grain, normal);
				if (g.sqrMagnitude > 0.09f)
				{
					u = g.normalized;
					v = Vector3.Cross(normal, u);
					return;
				}
			}
			if (Mathf.Abs(normal.y) < 0.9f)
			{
				u = Vector3.Cross(Vector3.up, normal).normalized;
				v = Vector3.Cross(normal, u);
				return;
			}
			u = Vector3.right;
			v = Vector3.forward;
		}
	}
}
#endif
