#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A cave system's rock as meshes of 32 m pieces: a sparse, chunked surface net over its field
	/// (<see cref="CaveSystem.Field"/>), sampled only where the system is.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not one mesh.</b> A deep system runs hundreds of metres in every direction; one dense grid over its box is
	/// tens of millions of samples, nearly all of them solid rock nowhere near it, and one mesh of it can be neither
	/// culled nor decimated sensibly. Here the grid is split into chunks of <see cref="ChunkCells"/> cells, and only the
	/// chunks the system's primitives reach (<see cref="CaveSystem.NearBox"/>) are sampled. The net itself is the same
	/// manifold surface net as <see cref="ProceduralSurfaceNets"/> (one vertex per sheet per cell), its quads wound by the
	/// grid; samples on a chunk's faces are computed from the same global grid index in both chunks, so every crossing is
	/// the same float from either side and there are no seams to stitch.
	/// </para>
	/// <para>
	/// <b>Only the faces that can be seen.</b> The shell's buried outer face and its top (cut under the ground by
	/// <see cref="CaveSystem.Skin"/>) are never seen from inside the cave nor through the terrain; away from the mouths
	/// they are dropped, halving the triangles. Within a mouth's reach everything is kept: there the shell is the lip
	/// round the opening and fills the terrain's holes.
	/// </para>
	/// <para>
	/// <b>Pieces.</b> Triangles go to the 32 m cube their centre is in (the scene's collider chunk size); each piece's
	/// vertices are its own copies, projected for metre UVs along their triangle's dominant axis. No piece is decimated on
	/// its own (it would crack against its neighbour); the whole welded net is thinned once before it is cut into pieces
	/// (<see cref="Simplify"/>, 2026-10-18), and the pieces are culled whole by <see cref="CaveVisibility"/>.
	/// </para>
	/// </remarks>
	public static partial class CaveMesher
	{
		/// <summary>Cells along each side of a sampled chunk.</summary>
		public const int ChunkCells = 24;
		/// <summary>The side of an output piece, metres.</summary>
		public const float PieceMetres = 32f;

		// Cell edges by corner pair (corner c = di | dj << 1 | dk << 2): 0–3 along x, 4–7 along y, 8–11 along z.
		private static readonly int[,] CellEdges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
		private static readonly int[,] FaceCorners = { { 0, 2, 6, 4 }, { 1, 3, 7, 5 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 1, 3, 2 }, { 4, 5, 7, 6 } };
		private static readonly int[,] FaceEdges = { { 4, 10, 6, 8 }, { 5, 11, 7, 9 }, { 0, 9, 2, 8 }, { 1, 11, 3, 10 }, { 0, 5, 1, 4 }, { 2, 7, 3, 6 } };

		/// <summary>One piece of a system's rock.</summary>
		public sealed class Piece
		{
			public Vector3Int Key;
			public MeshBuilder Mesh;
			/// <summary>Within reach of a mouth: seen from outside, so always drawn.</summary>
			public bool Mouth;
		}

		private sealed class Chunk
		{
			public Vector3Int Key;
			public float[] Samples;
			public readonly List<Vector3> Positions = new List<Vector3>();
			public readonly Dictionary<int, int> EdgeVertex = new Dictionary<int, int>();
			public int Base;
		}

		/// <summary>The system's rock in pieces, in its frame; <paramref name="report"/> says how many and how big.</summary>
		/// <param name="keepHidden">Keep the buried faces too (the whole closed shell): for checking the seams.</param>
		public static List<Piece> Build(CaveSystem system, CaveGround ground, out string report, bool keepHidden = false)
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			system.Prepare();
			float h = system.MeshCell;
			int n = ChunkCells;
			float side = n * h;
			Bounds extent = system.Extent;
			Vector3Int c0 = Vector3Int.FloorToInt(extent.min / side), c1 = Vector3Int.FloorToInt(extent.max / side);
			var chunks = new Dictionary<Vector3Int, Chunk>();
			for (int z = c0.z; z <= c1.z; z++)
			{
				for (int y = c0.y; y <= c1.y; y++)
				{
					for (int x = c0.x; x <= c1.x; x++)
					{
						var lo = new Vector3(x, y, z) * side - Vector3.one * h;
						if (system.NearBox(lo, lo + Vector3.one * (side + 2f * h)))
						{
							var key = new Vector3Int(x, y, z);
							chunks[key] = new Chunk { Key = key };
						}
					}
				}
			}
			var list = new List<Chunk>(chunks.Values);
			list.Sort((a, b) => a.Key.z != b.Key.z ? a.Key.z.CompareTo(b.Key.z) : a.Key.y != b.Key.y ? a.Key.y.CompareTo(b.Key.y) : a.Key.x.CompareTo(b.Key.x));
			int s1 = n + 1;
			float Field(Vector3 p) => system.Field(p, ground);

			// Samples: (n + 1)³ per chunk, the shared faces computed from the same global index on both sides.
			Parallel.ForEach(list, chunk =>
			{
				var v = new float[s1 * s1 * s1];
				Vector3Int b = chunk.Key * n;
				for (int k = 0; k < s1; k++)
				{
					for (int j = 0; j < s1; j++)
					{
						for (int i = 0; i < s1; i++)
						{
							float f = Field(new Vector3((b.x + i) * h, (b.y + j) * h, (b.z + k) * h));
							v[(k * s1 + j) * s1 + i] = f == 0f ? 1e-7f : f;
						}
					}
				}
				chunk.Samples = v;
			});
			double sampled = clock.Elapsed.TotalSeconds;

			// One vertex per sheet per cell, each chunk its own cells.
			Parallel.ForEach(list, chunk => Vertices(chunk, n, h));
			int total = 0;
			foreach (Chunk chunk in list)
			{
				chunk.Base = total;
				total += chunk.Positions.Count;
			}
			var positions = new Vector3[total];
			foreach (Chunk chunk in list)
			{
				chunk.Positions.CopyTo(positions, chunk.Base);
			}

			// Onto the surface: Newton steps along the gradient, kept within the cell's reach.
			const float e = 0.004f;
			Vector3 Gradient(Vector3 q) => new Vector3(
				Field(q + new Vector3(e, 0, 0)) - Field(q - new Vector3(e, 0, 0)),
				Field(q + new Vector3(0, e, 0)) - Field(q - new Vector3(0, e, 0)),
				Field(q + new Vector3(0, 0, e)) - Field(q - new Vector3(0, 0, e))) / (2f * e);
			var normals = new Vector3[total];
			Parallel.For(0, total, idx =>
			{
				Vector3 p = positions[idx], start = p;
				for (int s = 0; s < 3; s++)
				{
					Vector3 g = Gradient(p);
					p -= g * (Field(p) / Mathf.Max(1e-6f, g.sqrMagnitude));
				}
				if ((p - start).sqrMagnitude > h * h * 0.75f)
				{
					p = start;
				}
				positions[idx] = p;
				normals[idx] = Gradient(p).normalized;
			});
			double projected = clock.Elapsed.TotalSeconds;

			// Quads round every crossed grid edge, wound by the grid.
			int V(int gi, int gj, int gk, int edge)
			{
				var key = new Vector3Int(FloorDiv(gi, n), FloorDiv(gj, n), FloorDiv(gk, n));
				if (!chunks.TryGetValue(key, out Chunk c))
				{
					return -1;
				}
				int li = gi - key.x * n, lj = gj - key.y * n, lk = gk - key.z * n;
				return c.EdgeVertex.TryGetValue(((lk * n + lj) * n + li) * 12 + edge, out int id) ? c.Base + id : -1;
			}
			var tris = new List<int>();
			void Quad(int a, int b, int c, int d, bool positiveAxis)
			{
				if (a < 0 || b < 0 || c < 0 || d < 0)
				{
					return;
				}
				if (!positiveAxis)
				{
					(b, d) = (d, b);
				}
				if ((positions[a] - positions[c]).sqrMagnitude <= (positions[b] - positions[d]).sqrMagnitude)
				{
					tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(a); tris.Add(c); tris.Add(d);
				}
				else
				{
					tris.Add(a); tris.Add(b); tris.Add(d); tris.Add(b); tris.Add(c); tris.Add(d);
				}
			}
			foreach (Chunk chunk in list)
			{
				float[] v = chunk.Samples;
				Vector3Int b = chunk.Key * n;
				for (int k = 0; k < n; k++)
				{
					for (int j = 0; j < n; j++)
					{
						for (int i = 0; i < n; i++)
						{
							bool ain = v[(k * s1 + j) * s1 + i] < 0f;
							int gi = b.x + i, gj = b.y + j, gk = b.z + k;
							if ((v[(k * s1 + j) * s1 + i + 1] < 0f) != ain)
							{
								Quad(V(gi, gj - 1, gk - 1, 3), V(gi, gj, gk - 1, 2), V(gi, gj, gk, 0), V(gi, gj - 1, gk, 1), ain);
							}
							if ((v[(k * s1 + j + 1) * s1 + i] < 0f) != ain)
							{
								Quad(V(gi - 1, gj, gk - 1, 7), V(gi - 1, gj, gk, 5), V(gi, gj, gk, 4), V(gi, gj, gk - 1, 6), ain);
							}
							if ((v[((k + 1) * s1 + j) * s1 + i] < 0f) != ain)
							{
								Quad(V(gi - 1, gj - 1, gk, 11), V(gi, gj - 1, gk, 10), V(gi, gj, gk, 8), V(gi - 1, gj, gk, 9), ain);
							}
						}
					}
				}
				chunk.Samples = null;
			}
			KeepLargestPiece(total, tris);
			// Thinned where the rock is plain: the floors' flats and the vaults (CaveMesher.Simplify).
			int thinned = Simplify(system, ground, positions, normals, tris, h);
			double wound = clock.Elapsed.TotalSeconds;

			// Which triangles to keep: everything near a mouth; elsewhere only the void's own face.
			float near2 = 0f;
			foreach (CaveMouth m in system.Mouths)
			{
				near2 = Mathf.Max(near2, m.Zone + 2f * m.Radius + PieceMetres);
			}
			// The test reads the field once per triangle, so it runs on the workers into one flag per triangle; the pieces
			// are then filled in triangle order on this thread, exactly as before (each flag depends on its triangle alone).
			var buried = new bool[tris.Count / 3];
			if (!keepHidden)
			{
				Parallel.For(0, buried.Length, q =>
				{
					int t = 3 * q;
					Vector3 centre = (positions[tris[t]] + positions[tris[t + 1]] + positions[tris[t + 2]]) / 3f;
					buried[q] = !NearMouth(system, centre) && Buried(system, ground, centre, h);
				});
			}
			double tested = clock.Elapsed.TotalSeconds;
			var pieces = new Dictionary<Vector3Int, (MeshBuilder mesh, Dictionary<long, int> map)>();
			float inv = 1f / RockMeshes.TextureMetres;
			var white = new Color32(255, 255, 255, 0);
			int dropped = 0;
			for (int t = 0; t < tris.Count; t += 3)
			{
				int a = tris[t], b = tris[t + 1], c = tris[t + 2];
				Vector3 pa = positions[a], pb = positions[b], pc = positions[c];
				Vector3 centre = (pa + pb + pc) / 3f;
				if (buried[t / 3])
				{
					dropped++;
					continue;
				}
				// A sliver is kept (dropping it would open a pinhole between its neighbours); collider cooking discards it.
				Vector3 fn = Vector3.Cross(pb - pa, pc - pa);
				int axis = Mathf.Abs(fn.y) >= Mathf.Abs(fn.x) && Mathf.Abs(fn.y) >= Mathf.Abs(fn.z) ? 1 : Mathf.Abs(fn.x) >= Mathf.Abs(fn.z) ? 0 : 2;
				Vector3Int key = Vector3Int.FloorToInt(centre / PieceMetres);
				if (!pieces.TryGetValue(key, out var piece))
				{
					pieces[key] = piece = (new MeshBuilder(1), new Dictionary<long, int>());
				}
				int Map(int v)
				{
					long k = (long)v * 3 + axis;
					if (!piece.map.TryGetValue(k, out int id))
					{
						Vector3 p = positions[v];
						Vector3 w = p + system.Origin;
						Vector2 uv = axis == 1 ? new Vector2(w.x, w.z) : axis == 0 ? new Vector2(w.z, w.y) : new Vector2(w.x, w.y);
						id = piece.mesh.AddVertex(p, normals[v], uv * inv, white);
						piece.map[k] = id;
					}
					return id;
				}
				List<int> sub = piece.mesh.Submeshes[0];
				sub.Add(Map(a));
				sub.Add(Map(b));
				sub.Add(Map(c));
			}
			var result = new List<Piece>(pieces.Count);
			int triangles = 0;
			foreach (var kv in pieces)
			{
				if (kv.Value.mesh.TriangleCount == 0)
				{
					continue;
				}
				kv.Value.mesh.RecalculateTangents();
				Bounds bounds = kv.Value.mesh.Bounds;
				result.Add(new Piece { Key = kv.Key, Mesh = kv.Value.mesh, Mouth = NearMouth(system, bounds.center, PieceMetres) });
				triangles += kv.Value.mesh.TriangleCount;
			}
			result.Sort((x, y) => x.Key.z != y.Key.z ? x.Key.z.CompareTo(y.Key.z) : x.Key.y != y.Key.y ? x.Key.y.CompareTo(y.Key.y) : x.Key.x.CompareTo(y.Key.x));
			double built = clock.Elapsed.TotalSeconds;
			// The time each phase took, so the mesher's cost in a cut can be read from its notes.
			report = $"{list.Count} chunks sampled, {result.Count} pieces, {triangles} triangles ({thinned} thinned out, {dropped} buried dropped); meshed in {built:0.00} s "
				+ $"(field {sampled:0.00}, net and surface {projected - sampled:0.00}, faces and thinning {wound - projected:0.00}, buried test {tested - wound:0.00}, pieces {built - tested:0.00})";
			if (result.Count == 0)
			{
				throw new InvalidOperationException("the field has no surface");
			}
			return result;
		}

		/// <summary>A hash of the pieces' positions (rounded to 0.1 mm) and triangles, for determinism checks.</summary>
		public static ulong Hash(IEnumerable<Piece> pieces)
		{
			ulong h = 1469598103934665603UL;
			void Mix(long v)
			{
				unchecked
				{
					h ^= (ulong)v;
					h *= 1099511628211UL;
				}
			}
			foreach (Piece piece in pieces)
			{
				Mix(piece.Key.x);
				Mix(piece.Key.y);
				Mix(piece.Key.z);
				foreach (Vector3 p in piece.Mesh.Positions)
				{
					Mix(Mathf.RoundToInt(p.x * 10000f));
					Mix(Mathf.RoundToInt(p.y * 10000f));
					Mix(Mathf.RoundToInt(p.z * 10000f));
				}
				foreach (int i in piece.Mesh.Submeshes[0])
				{
					Mix(i);
				}
			}
			return h;
		}

		/// <summary>Within a mouth's reach (its zone and a bore past it, plus <paramref name="grow"/>).</summary>
		private static bool NearMouth(CaveSystem system, Vector3 p, float grow = 0f)
		{
			foreach (CaveMouth m in system.Mouths)
			{
				float reach = m.Zone + 2f * m.Radius + grow;
				if ((p - m.Floor).sqrMagnitude < reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>A point on the shell's outer face or its buried top, not the void's face: never seen.</summary>
		private static bool Buried(CaveSystem system, CaveGround ground, Vector3 p, float h)
			=> system.Void(p) > 1.5f * h && p.y < ground.At(p.x + system.Origin.x, p.z + system.Origin.z) - system.Origin.y - 0.5f * system.Skin;

		private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

		private static void Vertices(Chunk chunk, int n, float h)
		{
			int s1 = n + 1;
			float[] v = chunk.Samples;
			Vector3Int b = chunk.Key * n;
			var cv = new float[8];
			var cp = new Vector3[8];
			var parent = new int[12];
			var sumP = new Vector3[12];
			var sumN = new int[12];
			int Find(int x)
			{
				while (parent[x] != x)
				{
					x = parent[x] = parent[parent[x]];
				}
				return x;
			}
			bool Crossed(int edge) => (cv[CellEdges[edge, 0]] < 0f) != (cv[CellEdges[edge, 1]] < 0f);
			for (int k = 0; k < n; k++)
			{
				for (int j = 0; j < n; j++)
				{
					for (int i = 0; i < n; i++)
					{
						int inside = 0;
						for (int c = 0; c < 8; c++)
						{
							int di = c & 1, dj = (c >> 1) & 1, dk = (c >> 2) & 1;
							cv[c] = v[((k + dk) * s1 + j + dj) * s1 + i + di];
							cp[c] = new Vector3((b.x + i + di) * h, (b.y + j + dj) * h, (b.z + k + dk) * h);
							if (cv[c] < 0f)
							{
								inside++;
							}
						}
						if (inside == 0 || inside == 8)
						{
							continue;
						}
						for (int edge = 0; edge < 12; edge++)
						{
							parent[edge] = edge;
							sumN[edge] = 0;
							sumP[edge] = Vector3.zero;
						}
						for (int f = 0; f < 6; f++)
						{
							int crossed = 0;
							for (int s = 0; s < 4; s++)
							{
								if (Crossed(FaceEdges[f, s]))
								{
									crossed++;
								}
							}
							if (crossed == 2)
							{
								int first = -1;
								for (int s = 0; s < 4; s++)
								{
									int edge = FaceEdges[f, s];
									if (!Crossed(edge))
									{
										continue;
									}
									if (first < 0)
									{
										first = edge;
									}
									else
									{
										parent[Find(edge)] = Find(first);
									}
								}
							}
							else if (crossed == 4)
							{
								float centre = (cv[FaceCorners[f, 0]] + cv[FaceCorners[f, 1]] + cv[FaceCorners[f, 2]] + cv[FaceCorners[f, 3]]) * 0.25f;
								bool centreIn = centre < 0f;
								for (int s = 0; s < 4; s++)
								{
									if ((cv[FaceCorners[f, s]] < 0f) == centreIn)
									{
										continue;
									}
									parent[Find(FaceEdges[f, (s + 3) % 4])] = Find(FaceEdges[f, s]);
								}
							}
						}
						for (int edge = 0; edge < 12; edge++)
						{
							if (!Crossed(edge))
							{
								continue;
							}
							float a = cv[CellEdges[edge, 0]], bb = cv[CellEdges[edge, 1]];
							Vector3 x = cp[CellEdges[edge, 0]] + (cp[CellEdges[edge, 1]] - cp[CellEdges[edge, 0]]) * (a / (a - bb));
							int r = Find(edge);
							sumP[r] += x;
							sumN[r]++;
						}
						int cell = (k * n + j) * n + i;
						for (int edge = 0; edge < 12; edge++)
						{
							if (!Crossed(edge))
							{
								continue;
							}
							int r = Find(edge);
							if (sumN[r] > 0)
							{
								// The root's vertex, made once: its sum zeroed and its index kept negative in the count.
								chunk.Positions.Add(sumP[r] / sumN[r]);
								sumN[r] = -chunk.Positions.Count;
							}
							chunk.EdgeVertex[cell * 12 + edge] = -sumN[r] - 1;
						}
					}
				}
			}
		}

		/// <summary>Drops every connected piece but the largest (crumbs of rock in the void).</summary>
		private static void KeepLargestPiece(int vertices, List<int> tris)
		{
			var parent = new int[vertices];
			for (int i = 0; i < parent.Length; i++)
			{
				parent[i] = i;
			}
			int Find(int x)
			{
				while (parent[x] != x)
				{
					parent[x] = parent[parent[x]];
					x = parent[x];
				}
				return x;
			}
			for (int t = 0; t < tris.Count; t += 3)
			{
				int a = Find(tris[t]), b = Find(tris[t + 1]);
				parent[b] = a;
				parent[Find(tris[t + 2])] = a;
			}
			var count = new Dictionary<int, int>();
			for (int t = 0; t < tris.Count; t += 3)
			{
				int r = Find(tris[t]);
				count[r] = count.TryGetValue(r, out int c) ? c + 1 : 1;
			}
			int best = -1, bestCount = -1;
			foreach (var kv in count)
			{
				if (kv.Value > bestCount)
				{
					best = kv.Key;
					bestCount = kv.Value;
				}
			}
			var kept = new List<int>(tris.Count);
			for (int t = 0; t < tris.Count; t += 3)
			{
				if (Find(tris[t]) == best)
				{
					kept.Add(tris[t]);
					kept.Add(tris[t + 1]);
					kept.Add(tris[t + 2]);
				}
			}
			tris.Clear();
			tris.AddRange(kept);
		}
	}
}
#endif
