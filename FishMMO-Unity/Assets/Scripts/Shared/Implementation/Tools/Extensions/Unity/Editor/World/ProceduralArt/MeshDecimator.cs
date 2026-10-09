#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Quadric-error edge-collapse simplification (Garland and Heckbert): each vertex carries the sum of its faces'
	/// plane quadrics, and the cheapest edge collapses to the point that best keeps those planes until the triangle
	/// budget is met. Flat faces cost nothing to collapse, so they melt to a few triangles while steps, ridges and the
	/// silhouette are kept.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Guards.</b> The link condition in full: the ends of an edge share exactly the far corners of its two faces;
	/// no collapse may turn one of v's faces into a copy of one of u's (a tetrahedral pocket folding shut, which opens
	/// the surface); and the merged vertex keeps three neighbours. No face may flip or become a sliver. Later passes
	/// relax only the sliver guard, never the flip guard: relaxing it leaves inverted faces.
	/// </para>
	/// <para>
	/// <b>Afterwards</b> a needle (no area for its length) or a crumb (every edge under a millimetre) collapses along
	/// its shortest edge, or, where the link condition forbids that, has its longest edge flipped with the face across.
	/// Normals are split at creases and computed in double precision: a float normalise returns zero under 1e-5,
	/// which a millimetre face's cross product is.
	/// </para>
	/// </remarks>
	public static class MeshDecimator
	{
		private struct Entry
		{
			public double Cost;
			public int U, V, Su, Sv;
			public Vector3 At;
		}

		/// <summary>Min-heap on cost.</summary>
		private sealed class Heap
		{
			private readonly List<Entry> items = new List<Entry>();
			public int Count => items.Count;

			public void Push(Entry e)
			{
				items.Add(e);
				int i = items.Count - 1;
				while (i > 0)
				{
					int p = (i - 1) / 2;
					if (items[p].Cost <= items[i].Cost) break;
					(items[p], items[i]) = (items[i], items[p]);
					i = p;
				}
			}

			public Entry Pop()
			{
				Entry top = items[0];
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int i = 0;
				while (true)
				{
					int l = 2 * i + 1, r = l + 1, m = i;
					if (l < items.Count && items[l].Cost < items[m].Cost) m = l;
					if (r < items.Count && items[r].Cost < items[m].Cost) m = r;
					if (m == i) break;
					(items[m], items[i]) = (items[i], items[m]);
					i = m;
				}
				return top;
			}
		}

		/// <summary>The last result's source vertices (see <see cref="Decimate(IReadOnlyList{Vector3}, IReadOnlyList{int}, int, float, out List{int})"/>).</summary>
		[ThreadStatic] public static List<int> LastSource;

		/// <summary>Simplifies a closed triangle mesh; see the overload with the source map.</summary>
		public static MeshBuilder Decimate(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles, int targetTriangles, float creaseDegrees)
		{
			MeshBuilder result = Decimate(positions, triangles, targetTriangles, creaseDegrees, out List<int> source);
			LastSource = source;
			return result;
		}

		/// <summary>
		/// Simplifies a closed triangle mesh to at most <paramref name="targetTriangles"/>, normals split where faces meet
		/// at more than <paramref name="creaseDegrees"/>. <paramref name="source"/> gives each output vertex the simplified
		/// vertex it came from: crease copies share one, so welding by it recovers the topology.
		/// </summary>
		public static MeshBuilder Decimate(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles, int targetTriangles, float creaseDegrees, out List<int> source)
		{
			int nv = positions.Count, nf = triangles.Count / 3;
			var px = new double[nv]; var py = new double[nv]; var pz = new double[nv];
			for (int i = 0; i < nv; i++) { px[i] = positions[i].x; py[i] = positions[i].y; pz[i] = positions[i].z; }
			var face = new int[nf * 3];
			for (int i = 0; i < face.Length; i++) face[i] = triangles[i];
			var alive = new bool[nf];
			var faces = new List<int>[nv];
			for (int i = 0; i < nv; i++) faces[i] = new List<int>(6);
			var q = new double[nv * 10];
			var stamp = new int[nv];
			int aliveCount = 0;
			for (int f = 0; f < nf; f++)
			{
				int a = face[f * 3], b = face[f * 3 + 1], c = face[f * 3 + 2];
				if (a == b || b == c || a == c) continue;
				alive[f] = true;
				aliveCount++;
				faces[a].Add(f); faces[b].Add(f); faces[c].Add(f);
				Normal(px, py, pz, a, b, c, out double nx, out double ny, out double nz, out double area);
				if (area <= 0) continue;
				double d = -(nx * px[a] + ny * py[a] + nz * pz[a]);
				double[] k = { nx * nx, nx * ny, nx * nz, nx * d, ny * ny, ny * nz, ny * d, nz * nz, nz * d, d * d };
				foreach (int v in new[] { a, b, c })
					for (int j = 0; j < 10; j++) q[v * 10 + j] += k[j] * area;
			}

			double[] flipLimit = { 0.3, 0.15, 0.05 };
			float[] sliverLimit = { 0.02f, 0.005f, 0.0005f };
			for (int pass = 0; pass < flipLimit.Length && aliveCount > targetTriangles; pass++)
			{
				var heap = new Heap();
				var seen = new HashSet<long>(EdgeKeys.Instance);
				for (int f = 0; f < nf; f++)
				{
					if (!alive[f]) continue;
					for (int e = 0; e < 3; e++)
					{
						int u = face[f * 3 + e], v = face[f * 3 + (e + 1) % 3];
						long key = u < v ? ((long)u << 32) | (uint)v : ((long)v << 32) | (uint)u;
						if (seen.Add(key)) heap.Push(Candidate(q, px, py, pz, stamp, u, v));
					}
				}
				while (aliveCount > targetTriangles && heap.Count > 0)
				{
					Entry e = heap.Pop();
					if (stamp[e.U] != e.Su || stamp[e.V] != e.Sv) continue;
					int u = e.U, v = e.V;
					if (!LinkOk(face, faces, u, v)) continue;
					if (Flips(face, faces[u], u, v, e.At, px, py, pz, flipLimit[pass], sliverLimit[pass]) || Flips(face, faces[v], v, u, e.At, px, py, pz, flipLimit[pass], sliverLimit[pass])) continue;
					Collapse(face, faces, alive, ref aliveCount, q, px, py, pz, stamp, u, v, e.At);
					var around = new HashSet<int>();
					foreach (int f in faces[u]) for (int j = 0; j < 3; j++) { int w = face[f * 3 + j]; if (w != u) around.Add(w); }
					foreach (int w in around) heap.Push(Candidate(q, px, py, pz, stamp, u, w));
				}
			}

			for (int round = 0; round < 4; round++)
			{
				bool changed = false;
				for (int f = 0; f < nf; f++)
				{
					if (!alive[f]) continue;
					Normal(px, py, pz, face[f * 3], face[f * 3 + 1], face[f * 3 + 2], out _, out _, out _, out double area);
					double e0 = Len2(px, py, pz, face[f * 3], face[f * 3 + 1]), e1 = Len2(px, py, pz, face[f * 3 + 1], face[f * 3 + 2]), e2 = Len2(px, py, pz, face[f * 3 + 2], face[f * 3]);
					double longest = Math.Max(e0, Math.Max(e1, e2));
					if (area > 1e-4 * longest && longest > 1e-6) continue;
					int s0 = e0 <= e1 && e0 <= e2 ? 0 : e1 <= e2 ? 1 : 2;
					int u = face[f * 3 + s0], v = face[f * 3 + (s0 + 1) % 3];
					if (!LinkOk(face, faces, u, v))
					{
						int sl = e0 >= e1 && e0 >= e2 ? 0 : e1 >= e2 ? 1 : 2;
						if (TryFlip(face, faces, alive, f, sl)) changed = true;
						continue;
					}
					var at = new Vector3((float)((px[u] + px[v]) * 0.5), (float)((py[u] + py[v]) * 0.5), (float)((pz[u] + pz[v]) * 0.5));
					Collapse(face, faces, alive, ref aliveCount, q, px, py, pz, stamp, u, v, at);
					changed = true;
				}
				if (!changed) break;
			}

			var result = new MeshBuilder(1);
			source = new List<int>();
			var unit = new Dictionary<int, (double x, double y, double z, double w)>();
			for (int f = 0; f < nf; f++)
			{
				if (!alive[f]) continue;
				Normal(px, py, pz, face[f * 3], face[f * 3 + 1], face[f * 3 + 2], out double nx, out double ny, out double nz, out double area);
				unit[f] = (nx, ny, nz, Math.Max(area, 1e-12));
			}
			double cosCrease = Math.Cos(creaseDegrees * Math.PI / 180.0);
			var welded = new Dictionary<(int, int, int, int), int>();
			float inv = 1f / RockMeshes.TextureMetres;
			var white = new Color32(255, 255, 255, 0);
			foreach (var kv in unit)
			{
				int f = kv.Key;
				var fn = kv.Value;
				var corner = new int[3];
				for (int j = 0; j < 3; j++)
				{
					int v = face[f * 3 + j];
					double sx = 0, sy = 0, sz = 0;
					foreach (int g in faces[v])
					{
						if (!alive[g] || !unit.TryGetValue(g, out var gn)) continue;
						if (gn.x * fn.x + gn.y * fn.y + gn.z * fn.z >= cosCrease) { sx += gn.x * gn.w; sy += gn.y * gn.w; sz += gn.z * gn.w; }
					}
					double len = Math.Sqrt(sx * sx + sy * sy + sz * sz);
					Vector3 n = len > 0 ? new Vector3((float)(sx / len), (float)(sy / len), (float)(sz / len)) : new Vector3((float)fn.x, (float)fn.y, (float)fn.z);
					var p = new Vector3((float)px[v], (float)py[v], (float)pz[v]);
					var key = (v, Mathf.RoundToInt(n.x * 1000f), Mathf.RoundToInt(n.y * 1000f), Mathf.RoundToInt(n.z * 1000f));
					if (!welded.TryGetValue(key, out int id))
					{
						// Metres UVs projected along the dominant axis: the rock materials tile every TextureMetres.
						Vector2 uv = Mathf.Abs(n.y) > 0.6f ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
						id = result.AddVertex(p, n, uv * inv, white);
						source.Add(v);
						welded[key] = id;
					}
					corner[j] = id;
				}
				result.Submeshes[0].Add(corner[0]); result.Submeshes[0].Add(corner[1]); result.Submeshes[0].Add(corner[2]);
			}
			return result;
		}

		private static double Len2(double[] px, double[] py, double[] pz, int a, int b)
		{
			double x = px[a] - px[b], y = py[a] - py[b], z = pz[a] - pz[b];
			return x * x + y * y + z * z;
		}

		private static bool LinkOk(int[] face, List<int>[] faces, int u, int v)
		{
			var nbU = new HashSet<int>();
			var nbV = new HashSet<int>();
			int shared = 0;
			foreach (int f in faces[u]) for (int j = 0; j < 3; j++) nbU.Add(face[f * 3 + j]);
			foreach (int f in faces[v])
			{
				bool hasU = false;
				for (int j = 0; j < 3; j++) { nbV.Add(face[f * 3 + j]); hasU |= face[f * 3 + j] == u; }
				if (hasU) shared++;
			}
			if (shared != 2) return false;
			int common = 0;
			foreach (int w in nbU) if (w != u && w != v && nbV.Contains(w)) common++;
			if (common != 2) return false;
			foreach (int f in faces[v])
			{
				if (HasVertex(face, f, u)) continue;
				int a0 = face[f * 3] == v ? u : face[f * 3], a1 = face[f * 3 + 1] == v ? u : face[f * 3 + 1], a2 = face[f * 3 + 2] == v ? u : face[f * 3 + 2];
				foreach (int g in faces[u]) if (SameTriple(face, g, a0, a1, a2)) return false;
			}
			return nbU.Count + nbV.Count - 6 >= 3;
		}

		private static bool HasVertex(int[] face, int f, int a) => face[f * 3] == a || face[f * 3 + 1] == a || face[f * 3 + 2] == a;

		private static bool SameTriple(int[] face, int g, int a, int b, int c)
		{
			int x = face[g * 3], y = face[g * 3 + 1], z = face[g * 3 + 2];
			return (x == a || x == b || x == c) && (y == a || y == b || y == c) && (z == a || z == b || z == c);
		}

		private static void Collapse(int[] face, List<int>[] faces, bool[] alive, ref int aliveCount, double[] q, double[] px, double[] py, double[] pz, int[] stamp, int u, int v, Vector3 at)
		{
			px[u] = at.x; py[u] = at.y; pz[u] = at.z;
			for (int j = 0; j < 10; j++) q[u * 10 + j] += q[v * 10 + j];
			foreach (int f in faces[v])
			{
				if (HasVertex(face, f, u))
				{
					if (alive[f]) { alive[f] = false; aliveCount--; }
					for (int j = 0; j < 3; j++) { int w = face[f * 3 + j]; if (w != v) faces[w].Remove(f); }
				}
				else
				{
					for (int j = 0; j < 3; j++) if (face[f * 3 + j] == v) face[f * 3 + j] = u;
					faces[u].Add(f);
				}
			}
			faces[v].Clear();
			stamp[u]++;
			stamp[v]++;
		}

		/// <summary>Flips edge <paramref name="slot"/> of face f with the face across it, unless the new edge already exists.</summary>
		private static bool TryFlip(int[] face, List<int>[] faces, bool[] alive, int f, int slot)
		{
			int a = face[f * 3 + slot], b = face[f * 3 + (slot + 1) % 3], c = face[f * 3 + (slot + 2) % 3];
			int g = -1, d = -1;
			foreach (int h in faces[a])
			{
				if (h == f || !alive[h]) continue;
				for (int j = 0; j < 3; j++)
					if (face[h * 3 + j] == b && face[h * 3 + (j + 1) % 3] == a) { g = h; d = face[h * 3 + (j + 2) % 3]; }
			}
			if (g < 0 || d == c) return false;
			foreach (int h in faces[c]) if (HasVertex(face, h, d)) return false;
			// (a, b, c) + (b, a, d) → (c, a, d) + (c, d, b): f gives up b and takes d, g gives up a and takes c.
			faces[b].Remove(f); faces[a].Remove(g);
			face[f * 3] = c; face[f * 3 + 1] = a; face[f * 3 + 2] = d;
			face[g * 3] = c; face[g * 3 + 1] = d; face[g * 3 + 2] = b;
			faces[d].Add(f); faces[c].Add(g);
			return true;
		}

		private static void Normal(double[] px, double[] py, double[] pz, int a, int b, int c, out double nx, out double ny, out double nz, out double area)
		{
			double ux = px[b] - px[a], uy = py[b] - py[a], uz = pz[b] - pz[a];
			double vx = px[c] - px[a], vy = py[c] - py[a], vz = pz[c] - pz[a];
			nx = uy * vz - uz * vy; ny = uz * vx - ux * vz; nz = ux * vy - uy * vx;
			double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
			area = 0.5 * len;
			if (len > 1e-20) { nx /= len; ny /= len; nz /= len; }
		}

		private static double Error(double[] q, int o, double x, double y, double z)
		{
			return q[o] * x * x + 2 * q[o + 1] * x * y + 2 * q[o + 2] * x * z + 2 * q[o + 3] * x
				+ q[o + 4] * y * y + 2 * q[o + 5] * y * z + 2 * q[o + 6] * y
				+ q[o + 7] * z * z + 2 * q[o + 8] * z + q[o + 9];
		}

		private static Entry Candidate(double[] q, double[] px, double[] py, double[] pz, int[] stamp, int u, int v)
		{
			var s = new double[10];
			for (int j = 0; j < 10; j++) s[j] = q[u * 10 + j] + q[v * 10 + j];
			double a = s[0], b = s[1], c = s[2], d = s[4], e = s[5], f = s[7];
			double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
			double bx = -s[3], by = -s[6], bz = -s[8];
			double scale = Math.Abs(a) + Math.Abs(d) + Math.Abs(f);
			double best, ox, oy, oz;
			double mx = (px[u] + px[v]) * 0.5, my = (py[u] + py[v]) * 0.5, mz = (pz[u] + pz[v]) * 0.5;
			if (Math.Abs(det) > 1e-9 * scale * scale * scale)
			{
				ox = (bx * (d * f - e * e) - b * (by * f - e * bz) + c * (by * e - d * bz)) / det;
				oy = (a * (by * f - e * bz) - bx * (b * f - e * c) + c * (b * bz - by * c)) / det;
				oz = (a * (d * bz - by * e) - b * (b * bz - by * c) + bx * (b * e - d * c)) / det;
				// Never far from the edge: an ill-placed minimum on a nearly flat patch slides along it.
				double ex = px[v] - px[u], ey = py[v] - py[u], ez = pz[v] - pz[u];
				double dx = ox - mx, dy = oy - my, dz = oz - mz;
				if (dx * dx + dy * dy + dz * dz > ex * ex + ey * ey + ez * ez) { ox = mx; oy = my; oz = mz; }
				best = Error(s, 0, ox, oy, oz);
			}
			else
			{
				ox = mx; oy = my; oz = mz;
				best = Error(s, 0, mx, my, mz);
			}
			foreach (int w in new[] { u, v })
			{
				double err = Error(s, 0, px[w], py[w], pz[w]);
				if (err < best) { best = err; ox = px[w]; oy = py[w]; oz = pz[w]; }
			}
			return new Entry { Cost = Math.Max(0, best), U = u, V = v, Su = stamp[u], Sv = stamp[v], At = new Vector3((float)ox, (float)oy, (float)oz) };
		}

		/// <summary>True if moving <paramref name="moving"/> to <paramref name="to"/> flips or slivers any of its faces that do not also hold <paramref name="other"/>.</summary>
		private static bool Flips(int[] face, List<int> around, int moving, int other, Vector3 to, double[] px, double[] py, double[] pz, double flipLimit, float sliverLimit)
		{
			foreach (int f in around)
			{
				int a = face[f * 3], b = face[f * 3 + 1], c = face[f * 3 + 2];
				if (a == other || b == other || c == other) continue;
				Normal(px, py, pz, a, b, c, out double nx, out double ny, out double nz, out _);
				Vector3 pa = a == moving ? to : new Vector3((float)px[a], (float)py[a], (float)pz[a]);
				Vector3 pb = b == moving ? to : new Vector3((float)px[b], (float)py[b], (float)pz[b]);
				Vector3 pc = c == moving ? to : new Vector3((float)px[c], (float)py[c], (float)pz[c]);
				Vector3 n = Vector3.Cross(pb - pa, pc - pa);
				float len = n.magnitude;
				if (len < 1e-10f) return true;
				n /= len;
				if (n.x * nx + n.y * ny + n.z * nz < flipLimit) return true;
				float e2 = Mathf.Max((pb - pa).sqrMagnitude, Mathf.Max((pc - pb).sqrMagnitude, (pa - pc).sqrMagnitude));
				if (len * 0.5f < sliverLimit * e2) return true;
			}
			return false;
		}
	}
}
#endif
