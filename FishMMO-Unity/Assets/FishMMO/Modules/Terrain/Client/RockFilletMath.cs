using System;
using System.Collections.Generic;
using System.Numerics;

namespace FishMMO.Client
{
	/// <summary>
	/// The arithmetic of the rock seam fillets (<see cref="RockFillets"/>), free of Unity and the GPU so the tests and the
	/// CPU reference share it: the blended distance the fillet is the surface of, which seams it fills, the cell test, the
	/// tetrahedra the cells are cut into and the rule for which triangles are kept. FishRockFillets.compute mirrors every
	/// function here line for line; change both together.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The surface.</b> Every rock keeps its own mesh and its own collider; nothing about a rock moves. Where two rocks
	/// meet, the seam between them is filled by a separate surface: the zero set of F = smin(d₁, d₂, k) + inset, d₁ and d₂
	/// the signed distances (metres, negative inside) to the NEAREST TWO rocks there and smin the polynomial smooth
	/// minimum of width k. Away from a seam (|d₁ − d₂| ≥ k) smin is the plain minimum, so F = 0 lies <c>inset</c> under the
	/// nearer rock's own surface, hidden by it; in a seam smin dips under the minimum by up to k/4, and the surface comes
	/// out of the rocks into the crease between them, meeting each face tangentially. The two nearest, not all, so the
	/// field is symmetric in the rocks (the same whatever order the GPU lists them in) and its offset is bounded.
	/// </para>
	/// <para>
	/// <b>How far the fillet strays from the colliders.</b> smin(a, b, k) ≥ min(a, b) − k/4, so on the surface the hard
	/// union's distance H = d₁ is at most k/4 − inset: the fillet never stands more than that outside the rocks it joins,
	/// and only inside their crease (<see cref="MaxOffset"/>). Everything it adds is in front of a concave corner a
	/// player's capsule could not reach the bottom of anyway.
	/// </para>
	/// <para>
	/// <b>What is drawn.</b> The surface is cut into triangles by marching tetrahedra over a <see cref="SubCell"/> m
	/// lattice (each 2 m grid cell is <see cref="Sub"/>³ cubes, each cube the six Kuhn tetrahedra about its main
	/// diagonal, so neighbouring cubes and cells agree on every shared face and the surface has no cracks). A triangle is
	/// kept only when one of its corners is no deeper than <c>tuck</c> inside the rocks (<see cref="KeepTriangle"/>):
	/// the rest of the surface lies under the rocks' own faces, and its kept edge is always tucked under them.
	/// </para>
	/// </remarks>
	public static class RockFilletMath
	{
		/// <summary>Metres per side of a fillet cell: the rock grid's cell (RockContactField.CellSize).</summary>
		public const float CellSize = 2f;

		/// <summary>Cubes along each side of a cell (FISH_FILLET_SUB): 0.25 m.</summary>
		public const int Sub = 8;

		/// <summary>Metres per side of one cube.</summary>
		public const float SubCell = CellSize / Sub;

		/// <summary>Samples along each side of a cell's lattice: its <see cref="Sub"/> + 1 corners and one more each side, for the normals' central differences (FISH_FILLET_SAMPLES).</summary>
		public const int Samples = Sub + 3;

		/// <summary>The most rocks one cell blends (FISH_FILLET_ROCKS): the nearest at its centre.</summary>
		public const int MaxCellRocks = 8;

		/// <summary>
		/// The widest seam a fillet fills, metres. The cell's rocks are gathered from it and its 26 neighbours, which
		/// reaches every rock within a cell plus the grid's own reach (2.35 m) of any point in it; a fillet point needs
		/// every rock within 1.25·k of it, and its normal one lattice step more.
		/// </summary>
		public const float MaxWidth = 1.5f;

		/// <summary>The six tetrahedra of a cube about its main diagonal (corner 0 → 7), as corner indices (bit 0 x, bit 1 y, bit 2 z).</summary>
		public static readonly int[] Tets =
		{
			0, 1, 3, 7,
			0, 1, 5, 7,
			0, 2, 3, 7,
			0, 2, 6, 7,
			0, 4, 5, 7,
			0, 4, 6, 7,
		};

		/// <summary>A cube corner's lattice offset.</summary>
		public static void Corner(int c, out int x, out int y, out int z)
		{
			x = c & 1;
			y = (c >> 1) & 1;
			z = (c >> 2) & 1;
		}

		/// <summary>The polynomial smooth minimum: min(a, b) where they differ by k or more, up to k/4 under it where they meet.</summary>
		public static float SmoothMin(float a, float b, float k)
		{
			if (k <= 0f)
			{
				return MathF.Min(a, b);
			}
			float h = MathF.Max(k - MathF.Abs(a - b), 0f) / k;
			return MathF.Min(a, b) - h * h * k * 0.25f;
		}

		/// <summary>
		/// The seam width two rocks blend with: the profile's width, but never more than <paramref name="ratio"/> of the
		/// smaller rock's size (a fist-sized stone against a wall is not buried in a mound), nor more than <see cref="MaxWidth"/>.
		/// </summary>
		public static float PairWidth(float width, float sizeA, float sizeB, float ratio)
		{
			return MathF.Max(0f, MathF.Min(MathF.Min(width, MaxWidth), ratio * MathF.Min(sizeA, sizeB)));
		}

		/// <summary>The fillet's field at a point from its two nearest rocks' distances (d1 ≤ d2) and their pair width.</summary>
		public static float Field(float d1, float d2, float pairWidth, float inset)
		{
			return SmoothMin(d1, d2, pairWidth) + inset;
		}

		/// <summary>How far the fillet can stand outside the rocks it joins (the hard union): k/4 − inset, metres.</summary>
		public static float MaxOffset(float width, float inset) => MathF.Max(0f, MathF.Min(width, MaxWidth) * 0.25f - inset);

		/// <summary>The two smallest of <paramref name="d"/>'s first <paramref name="count"/> values and which entry the smallest is (ties: the lower index).</summary>
		public static void TwoSmallest(float[] d, int count, out float d1, out float d2, out int first, out int second)
		{
			d1 = 1e4f;
			d2 = 1e4f;
			first = -1;
			second = -1;
			for (int i = 0; i < count; i++)
			{
				if (d[i] < d1)
				{
					d2 = d1;
					second = first;
					d1 = d[i];
					first = i;
				}
				else if (d[i] < d2)
				{
					d2 = d[i];
					second = i;
				}
			}
		}

		/// <summary>
		/// Whether a triangle is drawn: one of its corners at most <paramref name="tuck"/> inside the rocks (the hard
		/// union's distance h). A triangle wholly deeper lies under the rocks' faces; the kept surface's edge is then
		/// between tuck and inset deep, never in the open.
		/// </summary>
		public static bool KeepTriangle(float h0, float h1, float h2, float tuck)
		{
			return MathF.Max(h0, MathF.Max(h1, h2)) > -tuck;
		}

		/// <summary>
		/// Whether a cell can hold any kept fillet, from its rocks' distances at its centre (the cell test, conservative:
		/// distances are 1-Lipschitz, so no point of the cell is nearer a rock than its centre's distance less the cell's
		/// half-diagonal, grown 10 % for the field's interpolation). A kept fillet point has d₁ in (−inset, k/4] and
		/// d₂ ≤ d₁ + k, so two rocks must reach that band and one the nearer one.
		/// </summary>
		public static bool CellMayHoldFillet(float[] centreDistances, int count, float width, float inset)
		{
			float reach = 1.1f * MathF.Sqrt(3f) * CellSize * 0.5f;
			float k = MathF.Min(width, MaxWidth);
			int near = 0;
			bool nearest = false;
			for (int i = 0; i < count; i++)
			{
				float d = centreDistances[i];
				if (d >= -inset - reach && d <= 1.25f * k + reach)
				{
					near++;
					nearest |= d <= 0.25f * k + reach;
				}
			}
			return near >= 2 && nearest;
		}

		/// <summary>One lattice sample: the field, the hard union's distance and which of the cell's rocks is nearest.</summary>
		public struct Sample
		{
			public float F;
			public float H;
			public int Nearest;
		}

		/// <summary>An emitted triangle, as FishRockFillets.compute writes it: corners in drawing order, each corner's hard-union distance, and the rock it belongs to.</summary>
		public struct Triangle
		{
			public Vector3 A, B, C;
			public float HA, HB, HC;
			public int Rock;
		}

		/// <summary>
		/// The CPU reference of one cell's fillet (FishFilletMesh): samples the field at the (<see cref="Sub"/> + 1)³ lattice
		/// corners from <paramref name="min"/>, cuts every cube into its tetrahedra and appends the kept triangles, wound so
		/// their front faces outward (out of the rocks).
		/// </summary>
		public static void MeshCell(Func<Vector3, Sample> field, Vector3 min, float tuck, List<Triangle> output)
		{
			int n = Sub + 1;
			var samples = new Sample[n * n * n];
			for (int z = 0; z < n; z++)
			{
				for (int y = 0; y < n; y++)
				{
					for (int x = 0; x < n; x++)
					{
						samples[x + n * (y + n * z)] = field(min + new Vector3(x, y, z) * SubCell);
					}
				}
			}
			var corners = new int[4];
			for (int z = 0; z < Sub; z++)
			{
				for (int y = 0; y < Sub; y++)
				{
					for (int x = 0; x < Sub; x++)
					{
						for (int t = 0; t < 6; t++)
						{
							for (int c = 0; c < 4; c++)
							{
								Corner(Tets[t * 4 + c], out int cx, out int cy, out int cz);
								corners[c] = (x + cx) + n * ((y + cy) + n * (z + cz));
							}
							Tetrahedron(samples, corners, n, min, tuck, output);
						}
					}
				}
			}
		}

		private static Vector3 Position(int index, int n, Vector3 min)
		{
			int x = index % n;
			int y = (index / n) % n;
			int z = index / (n * n);
			return min + new Vector3(x, y, z) * SubCell;
		}

		/// <summary>The crossing on the edge from sample i (inside, F &lt; 0) to sample o (outside): its position, hard-union distance, and the inside end's rock.</summary>
		private static void Crossing(Sample[] s, int i, int o, int n, Vector3 min, out Vector3 p, out float h, out int rock)
		{
			float t = s[i].F / (s[i].F - s[o].F);
			p = Vector3.Lerp(Position(i, n, min), Position(o, n, min), t);
			h = s[i].H + (s[o].H - s[i].H) * t;
			rock = s[i].Nearest;
		}

		/// <summary>One tetrahedron (FishFilletTet): none, one or two triangles where F changes sign along its edges.</summary>
		private static void Tetrahedron(Sample[] s, int[] corners, int n, Vector3 min, float tuck, List<Triangle> output)
		{
			int mask = 0;
			for (int c = 0; c < 4; c++)
			{
				mask |= s[corners[c]].F < 0f ? 1 << c : 0;
			}
			if (mask == 0 || mask == 15)
			{
				return;
			}
			// Inside and outside corners, in corner order.
			int insideCount = 0;
			var inside = new int[4];
			var outside = new int[4];
			int outsideCount = 0;
			for (int c = 0; c < 4; c++)
			{
				if ((mask & (1 << c)) != 0)
				{
					inside[insideCount++] = corners[c];
				}
				else
				{
					outside[outsideCount++] = corners[c];
				}
			}
			// Out of the rocks: from the inside corners' centroid toward the outside ones'.
			Vector3 inC = Vector3.Zero, outC = Vector3.Zero;
			for (int i = 0; i < insideCount; i++)
			{
				inC += Position(inside[i], n, min);
			}
			for (int i = 0; i < outsideCount; i++)
			{
				outC += Position(outside[i], n, min);
			}
			Vector3 outward = outC / outsideCount - inC / insideCount;
			if (insideCount == 1 || outsideCount == 1)
			{
				// One corner apart: a triangle across its three edges.
				bool lone = insideCount == 1;
				int apex = lone ? inside[0] : outside[0];
				int[] others = lone ? outside : inside;
				Vector3 p0, p1, p2;
				float h0, h1, h2;
				int r0, r1, r2;
				if (lone)
				{
					Crossing(s, apex, others[0], n, min, out p0, out h0, out r0);
					Crossing(s, apex, others[1], n, min, out p1, out h1, out r1);
					Crossing(s, apex, others[2], n, min, out p2, out h2, out r2);
				}
				else
				{
					Crossing(s, others[0], apex, n, min, out p0, out h0, out r0);
					Crossing(s, others[1], apex, n, min, out p1, out h1, out r1);
					Crossing(s, others[2], apex, n, min, out p2, out h2, out r2);
				}
				Emit(p0, p1, p2, h0, h1, h2, r0, r1, r2, outward, tuck, output);
				return;
			}
			// Two and two: a quad across the four edges between them, ac → ad → bd → bc, split along ac–bd.
			Crossing(s, inside[0], outside[0], n, min, out Vector3 ac, out float hac, out int rac);
			Crossing(s, inside[0], outside[1], n, min, out Vector3 ad, out float had, out int rad);
			Crossing(s, inside[1], outside[1], n, min, out Vector3 bd, out float hbd, out int rbd);
			Crossing(s, inside[1], outside[0], n, min, out Vector3 bc, out float hbc, out int rbc);
			Emit(ac, ad, bd, hac, had, hbd, rac, rad, rbd, outward, tuck, output);
			Emit(ac, bd, bc, hac, hbd, hbc, rac, rbd, rbc, outward, tuck, output);
		}

		/// <summary>Keeps a triangle (<see cref="KeepTriangle"/>), wound to face <paramref name="outward"/>, of the rock most of its corners belong to.</summary>
		private static void Emit(Vector3 p0, Vector3 p1, Vector3 p2, float h0, float h1, float h2, int r0, int r1, int r2, Vector3 outward, float tuck, List<Triangle> output)
		{
			if (!KeepTriangle(h0, h1, h2, tuck))
			{
				return;
			}
			int rock = MajorityRock(r0, r1, r2);
			if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0f)
			{
				output.Add(new Triangle { A = p0, B = p2, C = p1, HA = h0, HB = h2, HC = h1, Rock = rock });
			}
			else
			{
				output.Add(new Triangle { A = p0, B = p1, C = p2, HA = h0, HB = h1, HC = h2, Rock = rock });
			}
		}

		/// <summary>The rock two or three of a triangle's corners belong to, else its first corner's.</summary>
		public static int MajorityRock(int r0, int r1, int r2) => r1 == r2 ? r1 : r0;
	}
}
