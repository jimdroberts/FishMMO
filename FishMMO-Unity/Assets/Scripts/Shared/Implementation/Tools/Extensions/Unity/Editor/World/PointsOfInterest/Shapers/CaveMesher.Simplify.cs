#if UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Thinning the net where the rock is plain (2026-10-18: "Caves need even bigger hallows"): a surface net lays two
	/// triangles a cell over every square metre of rock, and a hall 250 m across with its lobes is several hundred thousand of
	/// them, most on its vault, where no one walks, and on its floor, which is flat. Here edges are collapsed, cheapest first,
	/// while the rock stays within a tolerance of where the net put it: a little over a decimetre on anything walked on or
	/// within reach of a foot (so no step, lip or crack is made under one), more high on the walls and vault. Nothing is
	/// touched within a few metres of the ground's surface (a mouth's collar and the terrain's holes are cut to the net), at
	/// a chasm or a climb (their lips and walls are fitted to the field), or on the buried outer face (dropped anyway).
	/// Measured on the test escarpment: a halls cave with a hall 220 m across, 480k triangles as netted, 140k thinned.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Closed stays closed.</b> It runs on the whole welded net before it is cut into pieces, so the pieces' copies of a
	/// vertex are still one position and meet as before. Half-edge collapses only (a vertex moves onto a neighbour, never to a
	/// new point), each kept to the link condition (the two ends share exactly the two neighbours of the faces on their edge),
	/// so the surface stays a closed manifold; no face is folded over (its normal may turn 70° at most), and a vertex whose
	/// faces do not make one closed fan (the net's pinches) is never moved.
	/// </para>
	/// <para>
	/// <b>Bounded error.</b> Each vertex carries the squared distances to every plane of the net's faces that ever met at it
	/// (an error quadric, unweighted). Within reach of a foot their sum is held under the tolerance, so no original face is
	/// left further than it from the rock that replaces it; high on the walls and vaults their mean square is, which lets the
	/// rough rock there be thinned several times over. Deterministic: one thread, ties broken by index.
	/// </para>
	/// </remarks>
	public static partial class CaveMesher
	{
		/// <summary>The most the rock may move off the net where it is walked on or within reach of a foot, metres (every plane).</summary>
		public const float FloorTolerance = 0.12f;
		/// <summary>The most it may move on the walls high over every floor and on the vaults, metres (the mean of its planes, against six).</summary>
		public const float VaultTolerance = 0.3f;
		/// <summary>How far under the ground's surface the rock is left exactly as meshed, metres.</summary>
		private const float SurfaceKeep = 3f;
		/// <summary>How far over the floor under it the rock counts as out of reach (the walls' full tolerance), metres.</summary>
		private const float ReachHeight = 2.5f, VaultHeight = 5f;

		/// <summary>
		/// The tolerance each vertex of the net may move by (0: it stays): <see cref="FloorTolerance"/> on floors, ledges and
		/// the walls within reach over them, rising to <see cref="VaultTolerance"/> up the walls and over the vaults.
		/// </summary>
		private static float[] Tolerances(CaveSystem system, CaveGround ground, Vector3[] positions, Vector3[] normals, float h)
		{
			var tolerance = new float[positions.Length];
			Parallel.For(0, positions.Length, i =>
			{
				Vector3 p = positions[i];
				// Within a few metres of the ground's surface the rock meets the terrain (a mouth's collar, its holes): as meshed.
				float surface = ground.At(p.x + system.Origin.x, p.z + system.Origin.z) - system.Origin.y;
				if (p.y > surface - SurfaceKeep || system.Void(p) > 1.5f * h || system.NearChasmOrClimb(p, 3f))
				{
					return;
				}
				float height = p.y - system.FloorNear(p);
				if (normals[i].y > 0.55f || height < ReachHeight)
				{
					tolerance[i] = FloorTolerance;
					return;
				}
				float t = Mathf.Clamp01((height - ReachHeight) / (VaultHeight - ReachHeight));
				tolerance[i] = Mathf.Lerp(FloorTolerance, VaultTolerance, t * t * (3f - 2f * t));
			});
			return tolerance;
		}

		private struct Collapse
		{
			public float Cost;
			public int From, To, FromStamp, ToStamp;

			public bool Before(in Collapse o) => Cost != o.Cost ? Cost < o.Cost : From != o.From ? From < o.From : To < o.To;
		}

		/// <summary>A binary min-heap of collapses, cheapest first.</summary>
		private sealed class CollapseHeap
		{
			private readonly List<Collapse> items = new List<Collapse>();

			public int Count => items.Count;

			public void Push(Collapse c)
			{
				items.Add(c);
				int i = items.Count - 1;
				while (i > 0)
				{
					int parent = (i - 1) >> 1;
					if (!items[i].Before(items[parent]))
					{
						break;
					}
					(items[i], items[parent]) = (items[parent], items[i]);
					i = parent;
				}
			}

			public Collapse Pop()
			{
				Collapse top = items[0];
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int i = 0;
				while (true)
				{
					int l = 2 * i + 1, r = l + 1, best = i;
					if (l < items.Count && items[l].Before(items[best]))
					{
						best = l;
					}
					if (r < items.Count && items[r].Before(items[best]))
					{
						best = r;
					}
					if (best == i)
					{
						break;
					}
					(items[i], items[best]) = (items[best], items[i]);
					i = best;
				}
				return top;
			}
		}

		/// <summary>
		/// Collapses the net's edges where its vertices' tolerances allow (<see cref="Tolerances"/>), in place: the triangles
		/// left replace <paramref name="tris"/>, the positions are unchanged (a collapsed vertex is merely no longer used).
		/// Returns how many triangles went.
		/// </summary>
		private static int Simplify(CaveSystem system, CaveGround ground, Vector3[] positions, Vector3[] normals, List<int> tris, float h)
		{
			int faces = tris.Count / 3, vertices = positions.Length;
			if (faces == 0)
			{
				return 0;
			}
			float[] tolerance = Tolerances(system, ground, positions, normals, h);
			float longest = 6f * h;
			int[] f = tris.ToArray();
			var dead = new bool[faces];
			var gone = new bool[vertices];
			var stamp = new int[vertices];
			var around = new List<int>[vertices];
			for (int t = 0; t < faces; t++)
			{
				for (int c = 0; c < 3; c++)
				{
					int v = f[3 * t + c];
					(around[v] ??= new List<int>(6)).Add(t);
				}
			}

			// The planes of the faces at each vertex, summed (a symmetric 4×4 as ten numbers).
			var q = new double[vertices * 10];
			var planes = new int[vertices];
			for (int t = 0; t < faces; t++)
			{
				Vector3 a = positions[f[3 * t]], b = positions[f[3 * t + 1]], c = positions[f[3 * t + 2]];
				Vector3 n = Vector3.Cross(b - a, c - a);
				float m = n.magnitude;
				if (m < 1e-9f)
				{
					continue;
				}
				n /= m;
				double nx = n.x, ny = n.y, nz = n.z, d = -(nx * a.x + ny * a.y + nz * a.z);
				for (int k = 0; k < 3; k++)
				{
					int o = f[3 * t + k] * 10;
					q[o] += nx * nx; q[o + 1] += nx * ny; q[o + 2] += nx * nz; q[o + 3] += nx * d;
					q[o + 4] += ny * ny; q[o + 5] += ny * nz; q[o + 6] += ny * d;
					q[o + 7] += nz * nz; q[o + 8] += nz * d; q[o + 9] += d * d;
					planes[o / 10]++;
				}
			}
			double Error(int u, int v, Vector3 x)
			{
				int a = u * 10, b = v * 10;
				double X = x.x, Y = x.y, Z = x.z;
				double Q(int k) => q[a + k] + q[b + k];
				return Q(0) * X * X + 2 * Q(1) * X * Y + 2 * Q(2) * X * Z + 2 * Q(3) * X
					+ Q(4) * Y * Y + 2 * Q(5) * Y * Z + 2 * Q(6) * Y
					+ Q(7) * Z * Z + 2 * Q(8) * Z + Q(9);
			}

			var ring = new List<int>(16);
			var other = new List<int>(16);
			// A vertex's neighbours over its live faces.
			void Neighbours(int v, List<int> into)
			{
				into.Clear();
				foreach (int t in around[v])
				{
					if (dead[t])
					{
						continue;
					}
					for (int c = 0; c < 3; c++)
					{
						int w = f[3 * t + c];
						if (w != v && !into.Contains(w))
						{
							into.Add(w);
						}
					}
				}
			}
			// The net's pinches and any vertex whose faces are not one closed fan stay where they are.
			for (int v = 0; v < vertices; v++)
			{
				if (tolerance[v] <= 0f || around[v] == null)
				{
					tolerance[v] = 0f;
					continue;
				}
				Neighbours(v, ring);
				if (ring.Count != around[v].Count || ring.Count < 3)
				{
					tolerance[v] = 0f;
					continue;
				}
				foreach (int w in ring)
				{
					int shared = 0;
					foreach (int t in around[v])
					{
						if (f[3 * t] == w || f[3 * t + 1] == w || f[3 * t + 2] == w)
						{
							shared++;
						}
					}
					if (shared != 2)
					{
						tolerance[v] = 0f;
						break;
					}
				}
			}

			var heap = new CollapseHeap();
			void Offer(int u, int v)
			{
				if (tolerance[u] <= 0f || tolerance[v] <= 0f || gone[u] || gone[v])
				{
					return;
				}
				float limit = Mathf.Min(tolerance[u], tolerance[v]);
				double e = Error(u, v, positions[v]);
				// Within reach of a foot every plane the vertex stands for stays within the tolerance (their sum does); high on the
				// walls and vaults, where only the eye goes, their mean square does.
				double bound = limit * limit;
				if (limit > FloorTolerance + 1e-4f)
				{
					bound *= Mathf.Max(1, planes[u] + planes[v]) / 6.0;
				}
				if (e <= bound)
				{
					heap.Push(new Collapse { Cost = (float)e, From = u, To = v, FromStamp = stamp[u], ToStamp = stamp[v] });
				}
			}
			for (int u = 0; u < vertices; u++)
			{
				if (tolerance[u] <= 0f)
				{
					continue;
				}
				Neighbours(u, ring);
				foreach (int w in ring)
				{
					Offer(u, w);
				}
			}

			int removed = 0;
			var shared2 = new List<int>(2);
			while (heap.Count > 0)
			{
				Collapse c = heap.Pop();
				int u = c.From, v = c.To;
				if (gone[u] || gone[v] || stamp[u] != c.FromStamp || stamp[v] != c.ToStamp)
				{
					continue;
				}
				// The two faces on the edge, and the link condition: the ends share no neighbours but those faces' third corners.
				shared2.Clear();
				foreach (int t in around[u])
				{
					if (!dead[t] && (f[3 * t] == v || f[3 * t + 1] == v || f[3 * t + 2] == v))
					{
						shared2.Add(t);
					}
				}
				if (shared2.Count != 2)
				{
					continue;
				}
				Neighbours(u, ring);
				Neighbours(v, other);
				int common = 0;
				foreach (int w in ring)
				{
					if (w != v && other.Contains(w))
					{
						common++;
					}
				}
				if (common != 2 || ring.Count <= 3 || other.Count <= 3)
				{
					continue;
				}
				// No face folded over, none made long or degenerate.
				bool fits = true;
				Vector3 pv = positions[v];
				foreach (int t in around[u])
				{
					if (dead[t] || shared2.Contains(t))
					{
						continue;
					}
					int i0 = f[3 * t], i1 = f[3 * t + 1], i2 = f[3 * t + 2];
					Vector3 a = positions[i0], b = positions[i1], d = positions[i2];
					Vector3 before = Vector3.Cross(b - a, d - a);
					Vector3 na = i0 == u ? pv : a, nb = i1 == u ? pv : b, nd = i2 == u ? pv : d;
					Vector3 after = Vector3.Cross(nb - na, nd - na);
					float am = after.magnitude, bm = before.magnitude;
					if (am < 1e-6f || Vector3.Dot(after, before) < 0.34f * am * bm
						|| (nb - na).sqrMagnitude > longest * longest || (nd - nb).sqrMagnitude > longest * longest || (na - nd).sqrMagnitude > longest * longest)
					{
						fits = false;
						break;
					}
				}
				if (!fits)
				{
					continue;
				}
				foreach (int t in shared2)
				{
					dead[t] = true;
				}
				foreach (int t in around[u])
				{
					if (dead[t])
					{
						continue;
					}
					for (int k = 0; k < 3; k++)
					{
						if (f[3 * t + k] == u)
						{
							f[3 * t + k] = v;
						}
					}
					around[v].Add(t);
				}
				for (int k = 0; k < 10; k++)
				{
					q[v * 10 + k] += q[u * 10 + k];
				}
				planes[v] += planes[u];
				gone[u] = true;
				stamp[v]++;
				removed += 2;
				Neighbours(v, ring);
				foreach (int w in ring)
				{
					Offer(v, w);
					Offer(w, v);
				}
			}

			tris.Clear();
			for (int t = 0; t < faces; t++)
			{
				if (!dead[t])
				{
					tris.Add(f[3 * t]);
					tris.Add(f[3 * t + 1]);
					tris.Add(f[3 * t + 2]);
				}
			}
			return removed;
		}
	}
}
#endif
