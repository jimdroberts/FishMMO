#if UNITY_EDITOR
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The noise every procedural art generator is built from: tileable 2D gradient noise and
	/// cellular noise for textures, and plain 3D gradient noise for meshes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stateless and integer-hashed.</b> Every value is a pure function of its coordinates and a
	/// seed, through an integer hash. Nothing reads <see cref="UnityEngine.Random"/>, nothing
	/// keeps a permutation table, and nothing depends on the order pixels are visited in — which is
	/// what lets a texture be filled from several threads and still come out bit-identical, and
	/// what lets a test regenerate an asset and compare.
	/// </para>
	/// <para>
	/// <b>Why not <see cref="Mathf.PerlinNoise"/>.</b> WorldEditor's generators seeded it by adding
	/// <c>seed * 1000</c> to the coordinates. A float has 24 bits of mantissa, so at seed 20 000 the
	/// sample point is 2×10⁷ and the spacing between representable values is 2 — every pixel of a
	/// texture lands on one of a handful of lattice points and the "noise" is flat bands. It also
	/// has no period control, so nothing built from it can tile, and no third dimension, which is
	/// why WorldEditor faked 3D noise by averaging six 2D planes (flattening the contrast and
	/// leaving axis-aligned streaks).
	/// </para>
	/// <para>
	/// <b>Tileable by construction.</b> The 2D functions take an integer period in lattice cells and
	/// wrap the lattice there, so the value at u = 1 equals the value at u = 0 exactly, not
	/// approximately. fBm doubles the period with the frequency (lacunarity is fixed at 2 for that
	/// reason: any non-integer ratio would break the wrap), and a domain warp built from periodic
	/// noise keeps periodic noise periodic.
	/// </para>
	/// </remarks>
	public static class ProceduralNoise
	{
		/// <summary>A well-mixed 32-bit hash (Wellons' lowbias32).</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Mix(uint x)
		{
			x ^= x >> 16;
			x *= 0x7feb352du;
			x ^= x >> 15;
			x *= 0x846ca68bu;
			x ^= x >> 16;
			return x;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Hash(int x, int y, int seed)
		{
			return Mix((uint)x * 0x8da6b343u ^ Mix((uint)y * 0xd8163841u ^ Mix((uint)seed * 0xcb1ab31fu)));
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Hash(int x, int y, int z, int seed)
		{
			return Mix((uint)x * 0x8da6b343u ^ Mix((uint)y * 0xd8163841u ^ Mix((uint)z * 0xcb1ab31fu ^ Mix((uint)seed * 0x165667b1u))));
		}

		/// <summary>A stable seed for a name, so every asset's randomness is its own and survives reordering.</summary>
		public static int SeedFor(string name, int seed)
		{
			// FNV-1a over UTF-16 code units: string.GetHashCode is randomised per process on .NET Core.
			uint h = 2166136261u;
			if (name != null)
			{
				for (int i = 0; i < name.Length; i++)
				{
					h ^= name[i];
					h *= 16777619u;
				}
			}
			return (int)Mix(h ^ (uint)seed * 0x9e3779b9u);
		}

		/// <summary>The hash as a float in [0, 1).</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float ToUnit(uint h) => (h >> 8) * (1f / 16777216f);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int Wrap(int i, int period)
		{
			int m = i % period;
			return m < 0 ? m + period : m;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

		// Sixteen unit gradients round the circle: enough directions that the lattice does not show,
		// few enough to be a table lookup.
		private static readonly float[] Grad2X = new float[16];
		private static readonly float[] Grad2Y = new float[16];

		static ProceduralNoise()
		{
			for (int i = 0; i < 16; i++)
			{
				float a = (i + 0.5f) * (Mathf.PI * 2f / 16f);
				Grad2X[i] = Mathf.Cos(a);
				Grad2Y[i] = Mathf.Sin(a);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static float Dot2(int ix, int iy, int seed, float fx, float fy)
		{
			uint g = Hash(ix, iy, seed) & 15u;
			return Grad2X[g] * fx + Grad2Y[g] * fy;
		}

		/// <summary>
		/// Gradient noise in [-1, 1] that repeats every <paramref name="periodX"/> by
		/// <paramref name="periodY"/> lattice cells.
		/// </summary>
		/// <param name="x">Position in lattice cells.</param>
		public static float Periodic(float x, float y, int periodX, int periodY, int seed)
		{
			periodX = Mathf.Max(1, periodX);
			periodY = Mathf.Max(1, periodY);
			int x0 = Mathf.FloorToInt(x);
			int y0 = Mathf.FloorToInt(y);
			float fx = x - x0;
			float fy = y - y0;
			int ix0 = Wrap(x0, periodX), ix1 = Wrap(x0 + 1, periodX);
			int iy0 = Wrap(y0, periodY), iy1 = Wrap(y0 + 1, periodY);

			float n00 = Dot2(ix0, iy0, seed, fx, fy);
			float n10 = Dot2(ix1, iy0, seed, fx - 1f, fy);
			float n01 = Dot2(ix0, iy1, seed, fx, fy - 1f);
			float n11 = Dot2(ix1, iy1, seed, fx - 1f, fy - 1f);
			float u = Fade(fx), v = Fade(fy);
			float nx0 = n00 + (n10 - n00) * u;
			float nx1 = n01 + (n11 - n01) * u;
			// √2 maps the theoretical ±1/√2 extreme of 2D gradient noise to ±1.
			return Mathf.Clamp((nx0 + (nx1 - nx0) * v) * 1.41421356f, -1f, 1f);
		}

		/// <summary>
		/// Tileable fractal noise in roughly [-1, 1]: <paramref name="octaves"/> layers of
		/// <see cref="Periodic"/>, each at twice the frequency (and so twice the period) of the last.
		/// </summary>
		/// <param name="u">Texture coordinate, 0..1 across one tile.</param>
		/// <param name="cells">Lattice cells across the tile at the first octave.</param>
		public static float PeriodicFbm(float u, float v, int cells, int octaves, float persistence, int seed)
		{
			float sum = 0f, amp = 1f, norm = 0f;
			int period = Mathf.Max(1, cells);
			for (int o = 0; o < octaves; o++)
			{
				sum += Periodic(u * period, v * period, period, period, seed + o * 1013) * amp;
				norm += amp;
				amp *= persistence;
				period *= 2;
			}
			return norm > 0f ? sum / norm : 0f;
		}

		/// <summary>
		/// Tileable fractal noise with independent periods across and down, for streaked patterns
		/// (bark furrows, peat fibres) that are long in one direction.
		/// </summary>
		public static float PeriodicFbm(float u, float v, int cellsX, int cellsY, int octaves, float persistence, int seed)
		{
			float sum = 0f, amp = 1f, norm = 0f;
			int px = Mathf.Max(1, cellsX), py = Mathf.Max(1, cellsY);
			for (int o = 0; o < octaves; o++)
			{
				sum += Periodic(u * px, v * py, px, py, seed + o * 1013) * amp;
				norm += amp;
				amp *= persistence;
				px *= 2;
				py *= 2;
			}
			return norm > 0f ? sum / norm : 0f;
		}

		/// <summary>Tileable ridged noise in [0, 1]: sharp crests where fBm crosses zero.</summary>
		public static float PeriodicRidged(float u, float v, int cells, int octaves, float persistence, int seed)
		{
			float sum = 0f, amp = 1f, norm = 0f, weight = 1f;
			int period = Mathf.Max(1, cells);
			for (int o = 0; o < octaves; o++)
			{
				float n = 1f - Mathf.Abs(Periodic(u * period, v * period, period, period, seed + o * 1013));
				n *= n;
				n *= weight;
				weight = Mathf.Clamp01(n * 2f);
				sum += n * amp;
				norm += amp;
				amp *= persistence;
				period *= 2;
			}
			return norm > 0f ? sum / norm : 0f;
		}

		/// <summary>The two nearest feature points of tileable cellular noise, and which cell the nearest belongs to.</summary>
		public struct Cell
		{
			/// <summary>Distance to the nearest feature point, in cells.</summary>
			public float F1;
			/// <summary>Distance to the second nearest, in cells.</summary>
			public float F2;
			/// <summary>A hash identifying the nearest cell, stable under wrapping: use it for per-stone colour.</summary>
			public uint Id;
			/// <summary>Offset from the sample to the nearest feature point, in cells.</summary>
			public float DX, DY;
		}

		/// <summary>
		/// Tileable cellular (Worley) noise with <paramref name="cells"/> cells across the tile.
		/// </summary>
		/// <param name="jitter">0 a regular grid, 1 fully random feature points.</param>
		public static Cell PeriodicCellular(float u, float v, int cells, float jitter, int seed)
		{
			cells = Mathf.Max(1, cells);
			float x = u * cells, y = v * cells;
			int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
			var result = new Cell { F1 = float.MaxValue, F2 = float.MaxValue };
			for (int oy = -1; oy <= 1; oy++)
			{
				for (int ox = -1; ox <= 1; ox++)
				{
					int nx = cx + ox, ny = cy + oy;
					int wx = Wrap(nx, cells), wy = Wrap(ny, cells);
					uint h = Hash(wx, wy, seed);
					float px = nx + 0.5f + (ToUnit(h) - 0.5f) * jitter;
					float py = ny + 0.5f + (ToUnit(Mix(h ^ 0x68e31da4u)) - 0.5f) * jitter;
					float dx = px - x, dy = py - y;
					float d = Mathf.Sqrt(dx * dx + dy * dy);
					if (d < result.F1)
					{
						result.F2 = result.F1;
						result.F1 = d;
						result.Id = h;
						result.DX = dx;
						result.DY = dy;
					}
					else if (d < result.F2)
					{
						result.F2 = d;
					}
				}
			}
			return result;
		}

		// ── 3D, for meshes ────────────────────────────────────────────

		// The twelve cube-edge gradients of improved Perlin noise.
		private static readonly Vector3[] Grad3 =
		{
			new Vector3(1, 1, 0), new Vector3(-1, 1, 0), new Vector3(1, -1, 0), new Vector3(-1, -1, 0),
			new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1),
			new Vector3(0, 1, 1), new Vector3(0, -1, 1), new Vector3(0, 1, -1), new Vector3(0, -1, -1),
		};

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static float Dot3(int ix, int iy, int iz, int seed, float fx, float fy, float fz)
		{
			Vector3 g = Grad3[Hash(ix, iy, iz, seed) % 12u];
			return g.x * fx + g.y * fy + g.z * fz;
		}

		/// <summary>3D gradient noise in roughly [-1, 1].</summary>
		public static float Gradient3(Vector3 p, int seed)
		{
			int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
			float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
			float u = Fade(fx), v = Fade(fy), w = Fade(fz);

			float n000 = Dot3(x0, y0, z0, seed, fx, fy, fz);
			float n100 = Dot3(x0 + 1, y0, z0, seed, fx - 1f, fy, fz);
			float n010 = Dot3(x0, y0 + 1, z0, seed, fx, fy - 1f, fz);
			float n110 = Dot3(x0 + 1, y0 + 1, z0, seed, fx - 1f, fy - 1f, fz);
			float n001 = Dot3(x0, y0, z0 + 1, seed, fx, fy, fz - 1f);
			float n101 = Dot3(x0 + 1, y0, z0 + 1, seed, fx - 1f, fy, fz - 1f);
			float n011 = Dot3(x0, y0 + 1, z0 + 1, seed, fx, fy - 1f, fz - 1f);
			float n111 = Dot3(x0 + 1, y0 + 1, z0 + 1, seed, fx - 1f, fy - 1f, fz - 1f);

			float x00 = n000 + (n100 - n000) * u;
			float x10 = n010 + (n110 - n010) * u;
			float x01 = n001 + (n101 - n001) * u;
			float x11 = n011 + (n111 - n011) * u;
			float y0v = x00 + (x10 - x00) * v;
			float y1v = x01 + (x11 - x01) * v;
			return Mathf.Clamp(y0v + (y1v - y0v) * w, -1f, 1f);
		}

		/// <summary>3D fractal noise in roughly [-1, 1].</summary>
		public static float Fbm3(Vector3 p, int octaves, float persistence, int seed)
		{
			float sum = 0f, amp = 1f, norm = 0f;
			for (int o = 0; o < octaves; o++)
			{
				sum += Gradient3(p, seed + o * 1013) * amp;
				norm += amp;
				amp *= persistence;
				p *= 2.03f; // Not exactly 2, so octaves' lattices never line up.
			}
			return norm > 0f ? sum / norm : 0f;
		}

		/// <summary>3D ridged noise in [0, 1]: crests for fracture lines on rock.</summary>
		public static float Ridged3(Vector3 p, int octaves, float persistence, int seed)
		{
			float sum = 0f, amp = 1f, norm = 0f;
			for (int o = 0; o < octaves; o++)
			{
				float n = 1f - Mathf.Abs(Gradient3(p, seed + o * 1013));
				sum += n * n * amp;
				norm += amp;
				amp *= persistence;
				p *= 2.03f;
			}
			return norm > 0f ? sum / norm : 0f;
		}
	}
}
#endif
