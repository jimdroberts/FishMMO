#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Closed, validated meshes from a signed field: manifold surface nets for the full-detail surface,
	/// <see cref="MeshDecimator"/> for the game levels, and a strict check of each level.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Manifold surface nets.</b> The plain net puts one vertex in every cell the surface crosses, and so pinches two
	/// sheets that pass through the same cell into one vertex. Here each cell has one vertex per separate sheet: its
	/// crossed edges are grouped across the cell's faces, an ambiguous face (all four edges crossed) settled by its
	/// centre value, the same way from both cells that share it. Quads are wound by the grid — the four cells round a
	/// crossed edge are listed counter-clockwise about its +axis — never by geometry, which a vertex projected onto the
	/// surface can fold.
	/// </para>
	/// <para>
	/// <b>What is left.</b> A tunnel thinner than a cell can still join two sheets inside one cell; the edge where they
	/// touch is given to each sheet by splitting its vertices per fan, and the cell-sized crumbs that splitting can pinch
	/// off are dropped. A wedge (two such edges at one vertex) is not resolved by fans; <see cref="BuildLevels"/> catches
	/// it in validation and re-meshes on a shifted grid. Fields should keep features thicker than a few cells: blend
	/// creases that would be thinner (a crack belongs in the material, not the game mesh).
	/// </para>
	/// </remarks>
	public static class ProceduralSurfaceNets
	{
		// Cell edges by corner pair (corner c = di | dj << 1 | dk << 2): 0–3 along x, 4–7 along y, 8–11 along z.
		private static readonly int[,] CellEdges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
		// The six cell faces: corners in cyclic order, and the edge between each corner and the next.
		private static readonly int[,] FaceCorners = { { 0, 2, 6, 4 }, { 1, 3, 7, 5 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 1, 3, 2 }, { 4, 5, 7, 6 } };
		private static readonly int[,] FaceEdges = { { 4, 10, 6, 8 }, { 5, 11, 7, 9 }, { 0, 9, 2, 8 }, { 1, 11, 3, 10 }, { 0, 5, 1, 4 }, { 2, 7, 3, 6 } };

		/// <summary>Grid offsets (in cells) tried in turn when a level fails validation.</summary>
		private static readonly Vector3[] Shifts =
		{
			Vector3.zero, new Vector3(0.37f, 0.21f, 0.13f), new Vector3(0.11f, 0.53f, 0.29f),
			new Vector3(0.61f, 0.07f, 0.47f), new Vector3(0.23f, 0.71f, 0.59f), new Vector3(0.83f, 0.41f, 0.19f),
		};

		/// <summary>
		/// The surface of <paramref name="field"/> (negative inside) within <paramref name="box"/>, which must hold the whole
		/// solid: the field is forced outside on the grid's skin. Only the largest piece is kept.
		/// </summary>
		public static MeshBuilder Extract(Func<Vector3, float> field, Func<Vector3, Vector3> gradient, Bounds box, float h, Vector3 shift = default)
		{
			Vector3 o = box.min - Vector3.one * h + shift * h;
			int nx = Mathf.CeilToInt(box.size.x / h) + 3, ny = Mathf.CeilToInt(box.size.y / h) + 3, nz = Mathf.CeilToInt(box.size.z / h) + 3;
			var v = new float[nx * ny * nz];
			Parallel.For(0, nz, k =>
			{
				for (int j = 0; j < ny; j++)
					for (int i = 0; i < nx; i++)
					{
						bool skin = i == 0 || j == 0 || k == 0 || i == nx - 1 || j == ny - 1 || k == nz - 1;
						float f = skin ? 1f : field(o + new Vector3(i * h, j * h, k * h));
						// Never exactly zero: a zero corner is neither side and makes a sliver.
						v[(k * ny + j) * nx + i] = f == 0f ? 1e-7f : f;
					}
			});
			int Id(int i, int j, int k) => (k * ny + j) * nx + i;
			int cx = nx - 1, cy = ny - 1;
			var edgeVertex = new Dictionary<long, int>(EdgeKeys.Instance);
			long Key(int i, int j, int k, int e) => (((long)k * cy + j) * cx + i) * 12 + e;
			var positions = new List<Vector3>();
			var cv = new float[8];
			var cp = new Vector3[8];
			var parent = new int[12];
			int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
			for (int k = 0; k < nz - 1; k++)
				for (int j = 0; j < ny - 1; j++)
					for (int i = 0; i < nx - 1; i++)
					{
						int inside = 0;
						for (int c = 0; c < 8; c++)
						{
							int di = c & 1, dj = (c >> 1) & 1, dk = (c >> 2) & 1;
							cv[c] = v[Id(i + di, j + dj, k + dk)];
							cp[c] = o + new Vector3((i + di) * h, (j + dj) * h, (k + dk) * h);
							if (cv[c] < 0f) inside++;
						}
						if (inside == 0 || inside == 8) continue;
						for (int e = 0; e < 12; e++) parent[e] = e;
						bool Crossed(int e) => (cv[CellEdges[e, 0]] < 0f) != (cv[CellEdges[e, 1]] < 0f);
						for (int f = 0; f < 6; f++)
						{
							int crossed = 0;
							for (int s = 0; s < 4; s++) if (Crossed(FaceEdges[f, s])) crossed++;
							if (crossed == 2)
							{
								int first = -1;
								for (int s = 0; s < 4; s++)
								{
									int e = FaceEdges[f, s];
									if (!Crossed(e)) continue;
									if (first < 0) first = e; else parent[Find(e)] = Find(first);
								}
							}
							else if (crossed == 4)
							{
								// The side the face's centre is on joins its two corners of that sign, so the sheets wrap
								// round the other two corners, one each.
								float centre = (cv[FaceCorners[f, 0]] + cv[FaceCorners[f, 1]] + cv[FaceCorners[f, 2]] + cv[FaceCorners[f, 3]]) * 0.25f;
								bool centreIn = centre < 0f;
								for (int s = 0; s < 4; s++)
								{
									if ((cv[FaceCorners[f, s]] < 0f) == centreIn) continue;
									parent[Find(FaceEdges[f, (s + 3) % 4])] = Find(FaceEdges[f, s]);
								}
							}
						}
						var sum = new Dictionary<int, (Vector3 p, int n)>();
						for (int e = 0; e < 12; e++)
						{
							if (!Crossed(e)) continue;
							float a = cv[CellEdges[e, 0]], b = cv[CellEdges[e, 1]];
							Vector3 x = cp[CellEdges[e, 0]] + (cp[CellEdges[e, 1]] - cp[CellEdges[e, 0]]) * (a / (a - b));
							int r = Find(e);
							sum[r] = sum.TryGetValue(r, out var acc) ? (acc.p + x, acc.n + 1) : (x, 1);
						}
						var vertexOf = new Dictionary<int, int>();
						foreach (var kv in sum)
						{
							vertexOf[kv.Key] = positions.Count;
							positions.Add(kv.Value.p / kv.Value.n);
						}
						for (int e = 0; e < 12; e++)
						{
							if (Crossed(e)) edgeVertex[Key(i, j, k, e)] = vertexOf[Find(e)];
						}
					}
			// Onto the surface: Newton steps along the gradient, kept within the cell's reach.
			var normals = new Vector3[positions.Count];
			Parallel.For(0, positions.Count, idx =>
			{
				Vector3 p = positions[idx], start = p;
				for (int s = 0; s < 3; s++)
				{
					Vector3 g = gradient(p);
					p -= g * (field(p) / Mathf.Max(1e-6f, g.sqrMagnitude));
				}
				if ((p - start).sqrMagnitude > h * h * 0.75f) p = start;
				positions[idx] = p;
				normals[idx] = gradient(p).normalized;
			});
			var mesh = new MeshBuilder(1);
			var white = new Color32(255, 255, 255, 0);
			for (int idx = 0; idx < positions.Count; idx++)
			{
				mesh.AddVertex(positions[idx], normals[idx], Vector2.zero, white);
			}
			List<int> tris = mesh.Submeshes[0];
			int V(int i, int j, int k, int e) => edgeVertex.TryGetValue(Key(i, j, k, e), out int id) ? id : -1;
			void Quad(int a, int b, int c, int d, bool positiveAxis)
			{
				if (a < 0 || b < 0 || c < 0 || d < 0) return;
				if (!positiveAxis) { int t = b; b = d; d = t; }
				if ((positions[a] - positions[c]).sqrMagnitude <= (positions[b] - positions[d]).sqrMagnitude)
				{
					tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d);
				}
				else
				{
					tris.Add(a); tris.Add(b); tris.Add(d); tris.Add(b); tris.Add(c); tris.Add(d);
				}
			}
			// Every grid edge, the outermost layers included (an edge needs its four cells).
			for (int k = 0; k < nz - 1; k++)
				for (int j = 0; j < ny - 1; j++)
					for (int i = 0; i < nx - 1; i++)
					{
						bool ain = v[Id(i, j, k)] < 0f;
						if (j > 0 && k > 0 && (v[Id(i + 1, j, k)] < 0f) != ain)
							Quad(V(i, j - 1, k - 1, 3), V(i, j, k - 1, 2), V(i, j, k, 0), V(i, j - 1, k, 1), ain);
						if (i > 0 && k > 0 && (v[Id(i, j + 1, k)] < 0f) != ain)
							Quad(V(i - 1, j, k - 1, 7), V(i - 1, j, k, 5), V(i, j, k, 4), V(i, j, k - 1, 6), ain);
						if (i > 0 && j > 0 && (v[Id(i, j, k + 1)] < 0f) != ain)
							Quad(V(i - 1, j - 1, k, 11), V(i, j - 1, k, 10), V(i, j, k, 8), V(i - 1, j, k, 9), ain);
					}
			return KeepLargestPiece(SplitNonManifold(KeepLargestPiece(mesh)));
		}

		/// <summary>
		/// Where two sheets touch along an edge, gives each vertex one copy per fan of triangles joined through ordinary
		/// two-triangle edges. Decided on the original topology, applied after: rewriting while walking hides the edges
		/// later vertices look up.
		/// </summary>
		public static MeshBuilder SplitNonManifold(MeshBuilder mesh)
		{
			List<int> tris = mesh.Submeshes[0];
			int n = mesh.VertexCount, nt = tris.Count / 3;
			var edgeUse = new Dictionary<long, int>(EdgeKeys.Instance);
			long EKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
			for (int t = 0; t < nt; t++)
				for (int e = 0; e < 3; e++)
				{
					long k = EKey(tris[t * 3 + e], tris[t * 3 + (e + 1) % 3]);
					edgeUse[k] = edgeUse.TryGetValue(k, out int c) ? c + 1 : 1;
				}
			bool any = false;
			foreach (int c in edgeUse.Values) if (c > 2) { any = true; break; }
			if (!any) return mesh;
			var around = new List<int>[n];
			for (int t = 0; t < nt; t++)
				for (int e = 0; e < 3; e++)
					(around[tris[t * 3 + e]] ??= new List<int>()).Add(t);
			var rewrites = new List<(int slot, int target)>();
			for (int v = 0; v < n; v++)
			{
				List<int> fan = around[v];
				if (fan == null || fan.Count < 2) continue;
				var group = new int[fan.Count];
				for (int i = 0; i < group.Length; i++) group[i] = i;
				int Find(int x) { while (group[x] != x) x = group[x] = group[group[x]]; return x; }
				for (int i = 0; i < fan.Count; i++)
					for (int j = i + 1; j < fan.Count; j++)
						for (int a = 0; a < 3; a++)
						{
							int w = tris[fan[i] * 3 + a];
							if (w == v) continue;
							bool shared = tris[fan[j] * 3] == w || tris[fan[j] * 3 + 1] == w || tris[fan[j] * 3 + 2] == w;
							if (shared && edgeUse[EKey(v, w)] == 2) group[Find(i)] = Find(j);
						}
				var copyOf = new Dictionary<int, int>();
				for (int i = 0; i < fan.Count; i++)
				{
					int root = Find(i);
					if (copyOf.Count == 0) copyOf[root] = v;
					if (!copyOf.TryGetValue(root, out int target))
					{
						target = mesh.AddVertex(mesh.Positions[v], mesh.Normals[v], mesh.UVs[v], mesh.Colors[v]);
						copyOf[root] = target;
					}
					if (target == v) continue;
					int t = fan[i];
					for (int a = 0; a < 3; a++) if (tris[t * 3 + a] == v) rewrites.Add((t * 3 + a, target));
				}
			}
			foreach (var (slot, target) in rewrites) tris[slot] = target;
			return mesh;
		}

		/// <summary>Drops every connected piece but the largest (crumbs, slivers on the floor).</summary>
		public static MeshBuilder KeepLargestPiece(MeshBuilder mesh)
		{
			List<int> tris = mesh.Submeshes[0];
			var parent = new int[mesh.VertexCount];
			for (int i = 0; i < parent.Length; i++) parent[i] = i;
			int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
			for (int t = 0; t < tris.Count; t += 3)
			{
				int a = Find(tris[t]), b = Find(tris[t + 1]), c = Find(tris[t + 2]);
				parent[b] = a; parent[Find(c)] = a;
			}
			var count = new Dictionary<int, int>();
			for (int t = 0; t < tris.Count; t += 3)
			{
				int r = Find(tris[t]);
				count[r] = count.TryGetValue(r, out int c) ? c + 1 : 1;
			}
			int best = -1, bestCount = -1;
			foreach (var kv in count) if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
			var kept = new List<int>(tris.Count);
			for (int t = 0; t < tris.Count; t += 3)
			{
				if (Find(tris[t]) == best) { kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]); }
			}
			tris.Clear();
			tris.AddRange(kept);
			return mesh;
		}

		/// <summary>
		/// What is wrong with a decimated level, or null. Crease copies are welded by their source vertex first, then:
		/// every edge on exactly two faces, no sliver, no face too small for <see cref="MeshBuilder.Validate"/>, every
		/// vertex normal on its face's side, one piece.
		/// </summary>
		public static string Validate(MeshBuilder m, List<int> source)
		{
			List<int> t = m.Submeshes[0];
			var use = new Dictionary<long, int>(EdgeKeys.Instance);
			var parent = new Dictionary<int, int>();
			int Find(int x) { if (!parent.ContainsKey(x)) parent[x] = x; while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
			for (int i = 0; i < t.Count; i += 3)
			{
				int a = source[t[i]], b = source[t[i + 1]], c = source[t[i + 2]];
				Vector3 pa = m.Positions[t[i]], pb = m.Positions[t[i + 1]], pc = m.Positions[t[i + 2]];
				Vector3 cross = Vector3.Cross(pb - pa, pc - pa);
				float l2 = Mathf.Max((pb - pa).sqrMagnitude, Mathf.Max((pc - pb).sqrMagnitude, (pa - pc).sqrMagnitude));
				if (a == b || b == c || a == c) return $"triangle {i / 3} repeats a vertex";
				// MeshBuilder.Validate's own floor (|cross|² under 1e-14), and no needles.
				if (cross.sqrMagnitude < 1e-13f || cross.magnitude * 0.5f < 1e-4f * l2) return $"sliver triangle {i / 3}";
				int[] v = { a, b, c };
				for (int e = 0; e < 3; e++)
				{
					int x = v[e], y = v[(e + 1) % 3];
					long k = x < y ? ((long)x << 32) | (uint)y : ((long)y << 32) | (uint)x;
					use[k] = use.TryGetValue(k, out int n) ? n + 1 : 1;
					parent[Find(x)] = Find(y);
				}
				double cl = Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
				var fn = new Vector3((float)(cross.x / cl), (float)(cross.y / cl), (float)(cross.z / cl));
				for (int j = 0; j < 3; j++) if (Vector3.Dot(m.Normals[t[i + j]], fn) < 0.2f) return $"vertex normal against face {i / 3}";
			}
			foreach (var kv in use) if (kv.Value != 2) return $"edge used by {kv.Value} faces";
			var roots = new HashSet<int>();
			foreach (int x in new List<int>(parent.Keys)) roots.Add(Find(x));
			return roots.Count == 1 ? null : $"{roots.Count} pieces";
		}

		/// <summary>
		/// The game levels of a closed field: one full-detail net, decimated to each budget in
		/// <paramref name="targetTriangles"/>, every level validated and given tangents. A failed level re-meshes the
		/// whole set on a shifted grid; if no shift passes this throws, so a broken mesh is never written.
		/// </summary>
		public static MeshBuilder[] BuildLevels(Func<Vector3, float> field, Bounds box, float h, int[] targetTriangles, float creaseDegrees, out string report)
		{
			const float e = 0.004f;
			Func<Vector3, Vector3> gradient = q => new Vector3(
				field(q + new Vector3(e, 0, 0)) - field(q - new Vector3(e, 0, 0)),
				field(q + new Vector3(0, e, 0)) - field(q - new Vector3(0, e, 0)),
				field(q + new Vector3(0, 0, e)) - field(q - new Vector3(0, 0, e))) / (2f * e);
			var failures = new List<string>();
			foreach (Vector3 shift in Shifts)
			{
				MeshBuilder high = Extract(field, gradient, box, h, shift);
				var levels = new MeshBuilder[targetTriangles.Length];
				string problem = null;
				// Each level from the one before, welded (far fewer triangles to simplify than the full net).
				IReadOnlyList<Vector3> positions = high.Positions;
				IReadOnlyList<int> triangles = high.Submeshes[0];
				for (int l = 0; l < levels.Length && problem == null; l++)
				{
					levels[l] = MeshDecimator.Decimate(positions, triangles, targetTriangles[l], creaseDegrees, out List<int> source);
					problem = Validate(levels[l], source);
					if (problem != null) problem = $"level {l}: {problem}";
					else Weld(levels[l], source, out positions, out triangles);
				}
				if (problem == null)
				{
					foreach (MeshBuilder level in levels) level.RecalculateTangents();
					report = failures.Count == 0 ? "valid" : $"valid after {failures.Count} re-mesh(es): {string.Join("; ", failures)}";
					return levels;
				}
				failures.Add(problem);
			}
			throw new InvalidOperationException("No valid mesh on any grid shift: " + string.Join("; ", failures));
		}

		/// <summary>A decimated level with its crease copies merged back by source vertex: the closed mesh again.</summary>
		private static void Weld(MeshBuilder level, List<int> source, out IReadOnlyList<Vector3> positions, out IReadOnlyList<int> triangles)
		{
			var index = new Dictionary<int, int>();
			var p = new List<Vector3>();
			var t = new List<int>(level.Submeshes[0].Count);
			foreach (int v in level.Submeshes[0])
			{
				if (!index.TryGetValue(source[v], out int w))
				{
					w = index[source[v]] = p.Count;
					p.Add(level.Positions[v]);
				}
				t.Add(w);
			}
			positions = p;
			triangles = t;
		}
	}

	/// <summary>
	/// Hashing for edge keys packed as (a &lt;&lt; 32) | b. The default long hash XORs the halves, so edges between
	/// nearby vertex indices — every edge of a mesh built in order — pile into a few buckets (23 s of a 24 s mesh).
	/// </summary>
	internal sealed class EdgeKeys : IEqualityComparer<long>
	{
		public static readonly EdgeKeys Instance = new EdgeKeys();

		public bool Equals(long a, long b) => a == b;

		public int GetHashCode(long k) => (int)(((ulong)k * 0x9E3779B97F4A7C15UL) >> 32);
	}
}
#endif
