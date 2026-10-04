using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FishMMO.Client
{
	/// <summary>
	/// How the air at one height flows round the ground that stands above it: the potential flow past
	/// the terrain, as the coordinates the cloud and fog fields are read in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The sky is a noise field carried on the wind, read at a point's position along the wind and
	/// across it. A mountain did nothing to that: the field passed straight through the rock. The old
	/// remedy moved the lookup sideways by up to 250 m, and could move it no further, because a
	/// displacement that changes faster than the distance it moves folds the field onto itself.
	/// </para>
	/// <para>
	/// This reads the field along the streamlines instead. For flow past obstacles with no
	/// circulation, the velocity potential φ (how far downstream a point is) and the stream function ψ
	/// (which streamline it is on) are a pair of coordinates that bend round every obstacle and never
	/// fold, and far from any obstacle they are exactly "along the wind" and "across it". Read at
	/// (φ, ψ), a cloud splits round a peak, squeezes through a pass, slows against a face and closes up
	/// in the lee — and nothing of it is ever inside the rock, since no streamline goes there.
	/// </para>
	/// <para>
	/// Both are harmonic, so the flow for any wind direction is the flow for a wind along +x times its
	/// cosine plus the flow for a wind along +z times its sine: two solutions per height cover every
	/// wind the weather can bring, and nothing is solved again when it turns.
	/// </para>
	/// <para>
	/// Stored as offsets from the undisturbed coordinates, in metres, four to a cell: φx − x, ψx − z,
	/// φz − z, ψz + x (the stream functions taken so that u = ∂ψ/∂z, w = −∂ψ/∂x, which makes the
	/// undisturbed ψx = z and ψz = −x). For a wind (c, s) the reading point moves by
	/// wind·(c·dφx + s·dφz) + across·(c·dψx + s·dψz), across = (−s, c) (FishFlowAround).
	/// </para>
	/// <para>
	/// The air at a height is blocked by every column of ground higher than it. Each solve is for one
	/// such level; which level a sample reads is its own height plus the height its air can climb
	/// (FishFlowAround), so air with the energy to go over a hill is not turned aside by it.
	/// </para>
	/// <para>
	/// Plain C# and nothing of Unity's, so it runs on a worker thread and its tests run anywhere.
	/// Solved coarse to fine (a quarter, a half, then the whole grid, each started from the last) by
	/// successive over-relaxation: about a quarter of a second per level on one core at 256 cells.
	/// </para>
	/// </remarks>
	public static class TerrainFlowSolver
	{
		/// <summary>dφx, dψx, dφz, dψz per cell.</summary>
		public const int Channels = 4;

		/// <summary>The coarsest grid the cascade starts from.</summary>
		private const int CoarsestCells = 32;

		/// <summary>
		/// The flow at each of <paramref name="levels"/> past <paramref name="ground"/>, an n × n grid of
		/// heights (m, row-major: index z·n + x) whose cells are <paramref name="cell"/> metres across.
		/// Each result is n·n·<see cref="Channels"/> offsets in metres. The outermost ring of cells is
		/// the undisturbed wind (all zeros), so the grid wants a margin of open air round the terrain.
		/// </summary>
		public static float[][] Solve(float[] ground, int n, float cell, float[] levels, bool parallel = true)
		{
			if (ground == null || ground.Length != n * n)
			{
				throw new ArgumentException("ground must hold n * n heights");
			}
			var result = new float[levels.Length][];
			if (parallel)
			{
				Parallel.For(0, levels.Length, k => result[k] = SolveLevel(ground, n, cell, levels[k]));
			}
			else
			{
				for (int k = 0; k < levels.Length; k++)
				{
					result[k] = SolveLevel(ground, n, cell, levels[k]);
				}
			}
			return result;
		}

		/// <summary>The flow at one level: n·n·<see cref="Channels"/> offsets, metres.</summary>
		public static float[] SolveLevel(float[] ground, int n, float cell, float level)
		{
			// The cascade's grids, finest first: each a pooled copy of the one before.
			var grounds = new List<float[]> { ground };
			var sizes = new List<int> { n };
			while (sizes[sizes.Count - 1] % 2 == 0 && sizes[sizes.Count - 1] / 2 >= CoarsestCells)
			{
				int m = sizes[sizes.Count - 1];
				grounds.Add(Pool(grounds[grounds.Count - 1], m));
				sizes.Add(m / 2);
			}

			float[] previous = null;
			int previousSize = 0;
			for (int g = sizes.Count - 1; g >= 0; g--)
			{
				int m = sizes[g];
				float h = cell * n / m;
				float[] start = previous != null ? Upsample(previous, previousSize, m) : null;
				// The coarsest grid starts from nothing and needs the most sweeps; each finer one starts
				// from the coarser answer and only has to put the detail in.
				int sweeps = previous == null ? 4 * m : m;
				previous = SolveGrid(grounds[g], m, h, level, start, sweeps);
				previousSize = m;
			}
			return previous;
		}

		/// <summary>One grid's four fields, each started from <paramref name="start"/> (or zero).</summary>
		private static float[] SolveGrid(float[] ground, int m, float h, float level, float[] start, int sweeps)
		{
			int count = m * m;
			// 0 open air reached from the edge, 1 rock, 2 air shut in by rock (a hollow the wind cannot
			// reach: left undisturbed, all zeros).
			var kind = new byte[count];
			for (int i = 0; i < count; i++)
			{
				kind[i] = ground[i] > level ? (byte)1 : (byte)2;
			}
			FloodOpen(kind, m);

			// Every body of rock and where its middle is: a streamline that meets a body of rock runs
			// round it, so the whole of its outline is one streamline, and taken as the one through its
			// middle the wind divides about evenly either side of it.
			int[] label = new int[count];
			var centres = LabelRock(kind, m, h, label);

			var phiX = new float[count];
			var psiX = new float[count];
			var phiZ = new float[count];
			var psiZ = new float[count];
			if (start != null)
			{
				for (int i = 0; i < count; i++)
				{
					if (kind[i] == 0)
					{
						phiX[i] = start[i * Channels];
						psiX[i] = start[i * Channels + 1];
						phiZ[i] = start[i * Channels + 2];
						psiZ[i] = start[i * Channels + 3];
					}
				}
			}
			// The rock's own stream function: constant over each body, so its offset from the
			// undisturbed one is the body's middle less the cell's own place.
			for (int z = 0; z < m; z++)
			{
				for (int x = 0; x < m; x++)
				{
					int i = z * m + x;
					if (kind[i] != 1)
					{
						continue;
					}
					Centre c = centres[label[i]];
					psiX[i] = (float)(c.Z - (z + 0.5) * h);
					psiZ[i] = (float)((x + 0.5) * h - c.X);
				}
			}

			float omega = (float)(2.0 / (1.0 + Math.Sin(Math.PI / m)));
			RelaxPotential(phiX, kind, m, h, omega, sweeps, true);
			RelaxPotential(phiZ, kind, m, h, omega, sweeps, false);
			RelaxStream(psiX, kind, m, omega, sweeps);
			RelaxStream(psiZ, kind, m, omega, sweeps);
			ExtendIntoRock(phiX, kind, m, h, true);
			ExtendIntoRock(phiZ, kind, m, h, false);

			var result = new float[count * Channels];
			for (int i = 0; i < count; i++)
			{
				if (kind[i] == 2)
				{
					continue;
				}
				result[i * Channels] = phiX[i];
				result[i * Channels + 1] = psiX[i];
				result[i * Channels + 2] = phiZ[i];
				result[i * Channels + 3] = psiZ[i];
			}
			return result;
		}

		/// <summary>
		/// The potential's offset, d = φ − (the undisturbed coordinate): harmonic in the open air, zero at
		/// the edge, and with no flow into the rock. φ's gradient normal to a face is zero, so a rock
		/// neighbour stands in as a ghost with φ equal to the cell's own — which for the offset is the
		/// cell's own d plus the step in the undisturbed coordinate toward it.
		/// </summary>
		private static void RelaxPotential(float[] d, byte[] kind, int m, float h, float omega, int sweeps, bool alongX)
		{
			for (int sweep = 0; sweep < sweeps; sweep++)
			{
				for (int z = 1; z < m - 1; z++)
				{
					int row = z * m;
					for (int x = 1; x < m - 1; x++)
					{
						int i = row + x;
						if (kind[i] != 0)
						{
							continue;
						}
						float sum = 0f;
						int open = 0;
						// +x, −x, +z, −z: the ghost for rock is d_i − h toward +axis and d_i + h toward −axis
						// along the wind's own axis, and d_i across it; the d_i parts move to the left side.
						float lean = 0f;
						if (kind[i + 1] != 1) { sum += d[i + 1]; open++; } else if (alongX) { lean -= h; }
						if (kind[i - 1] != 1) { sum += d[i - 1]; open++; } else if (alongX) { lean += h; }
						if (kind[i + m] != 1) { sum += d[i + m]; open++; } else if (!alongX) { lean -= h; }
						if (kind[i - m] != 1) { sum += d[i - m]; open++; } else if (!alongX) { lean += h; }
						if (open == 0)
						{
							d[i] = 0f;
							continue;
						}
						float settled = (sum + lean) / open;
						d[i] += omega * (settled - d[i]);
					}
				}
			}
		}

		/// <summary>
		/// The stream function's offset: harmonic in the open air, zero at the edge, and fixed over the
		/// rock (set by the caller), so every outline of rock is a streamline.
		/// </summary>
		private static void RelaxStream(float[] d, byte[] kind, int m, float omega, int sweeps)
		{
			for (int sweep = 0; sweep < sweeps; sweep++)
			{
				for (int z = 1; z < m - 1; z++)
				{
					int row = z * m;
					for (int x = 1; x < m - 1; x++)
					{
						int i = row + x;
						if (kind[i] != 0)
						{
							continue;
						}
						float settled = 0.25f * (d[i + 1] + d[i - 1] + d[i + m] + d[i - m]);
						d[i] += omega * (settled - d[i]);
					}
				}
			}
		}

		/// <summary>
		/// Carries the potential a few cells into the rock, keeping φ level across each face, so a
		/// bilinear read just outside a face does not mix in a zero from inside it.
		/// </summary>
		private static void ExtendIntoRock(float[] d, byte[] kind, int m, float h, bool alongX)
		{
			var known = new bool[m * m];
			for (int i = 0; i < known.Length; i++)
			{
				known[i] = kind[i] != 1;
			}
			var next = new List<(int index, float value)>();
			for (int pass = 0; pass < 3; pass++)
			{
				next.Clear();
				for (int z = 0; z < m; z++)
				{
					for (int x = 0; x < m; x++)
					{
						int i = z * m + x;
						if (known[i])
						{
							continue;
						}
						float sum = 0f;
						int n = 0;
						// φ equal to the neighbour's: d here = d there + (its coordinate − this one's).
						if (x + 1 < m && known[i + 1]) { sum += d[i + 1] + (alongX ? h : 0f); n++; }
						if (x > 0 && known[i - 1]) { sum += d[i - 1] - (alongX ? h : 0f); n++; }
						if (z + 1 < m && known[i + m]) { sum += d[i + m] + (alongX ? 0f : h); n++; }
						if (z > 0 && known[i - m]) { sum += d[i - m] - (alongX ? 0f : h); n++; }
						if (n > 0)
						{
							next.Add((i, sum / n));
						}
					}
				}
				foreach ((int index, float value) in next)
				{
					d[index] = value;
					known[index] = true;
				}
			}
		}

		/// <summary>Marks the air the edge can reach as open (0), leaving shut-in air (2) and rock (1).</summary>
		private static void FloodOpen(byte[] kind, int m)
		{
			var stack = new Stack<int>();
			for (int k = 0; k < m; k++)
			{
				Seed(k);
				Seed((m - 1) * m + k);
				Seed(k * m);
				Seed(k * m + m - 1);
			}
			while (stack.Count > 0)
			{
				int i = stack.Pop();
				int x = i % m, z = i / m;
				if (x + 1 < m) Seed(i + 1);
				if (x > 0) Seed(i - 1);
				if (z + 1 < m) Seed(i + m);
				if (z > 0) Seed(i - m);
			}

			void Seed(int i)
			{
				if (kind[i] == 2)
				{
					kind[i] = 0;
					stack.Push(i);
				}
			}
		}

		private struct Centre
		{
			public double X, Z;
		}

		/// <summary>Labels each body of rock and returns where each one's middle is (m).</summary>
		private static List<Centre> LabelRock(byte[] kind, int m, float h, int[] label)
		{
			var centres = new List<Centre>();
			var stack = new Stack<int>();
			for (int i = 0; i < label.Length; i++)
			{
				label[i] = -1;
			}
			for (int start = 0; start < kind.Length; start++)
			{
				if (kind[start] != 1 || label[start] >= 0)
				{
					continue;
				}
				int id = centres.Count;
				double sx = 0.0, sz = 0.0;
				int cells = 0;
				label[start] = id;
				stack.Push(start);
				while (stack.Count > 0)
				{
					int i = stack.Pop();
					int x = i % m, z = i / m;
					sx += (x + 0.5) * h;
					sz += (z + 0.5) * h;
					cells++;
					if (x + 1 < m) Visit(i + 1);
					if (x > 0) Visit(i - 1);
					if (z + 1 < m) Visit(i + m);
					if (z > 0) Visit(i - m);
				}
				centres.Add(new Centre { X = sx / cells, Z = sz / cells });

				void Visit(int j)
				{
					if (kind[j] == 1 && label[j] < 0)
					{
						label[j] = id;
						stack.Push(j);
					}
				}
			}
			return centres;
		}

		/// <summary>Halves a grid of heights, each coarse cell the highest of its four.</summary>
		private static float[] Pool(float[] fine, int m)
		{
			int half = m / 2;
			var coarse = new float[half * half];
			for (int z = 0; z < half; z++)
			{
				for (int x = 0; x < half; x++)
				{
					int i = 2 * z * m + 2 * x;
					coarse[z * half + x] = Math.Max(Math.Max(fine[i], fine[i + 1]), Math.Max(fine[i + m], fine[i + m + 1]));
				}
			}
			return coarse;
		}

		/// <summary>Doubles a grid of offsets bilinearly, cell centre to cell centre.</summary>
		private static float[] Upsample(float[] coarse, int half, int m)
		{
			var fine = new float[m * m * Channels];
			for (int z = 0; z < m; z++)
			{
				float cz = Math.Clamp((z + 0.5f) * half / m - 0.5f, 0f, half - 1);
				int z0 = Math.Min((int)cz, half - 2 < 0 ? 0 : half - 2);
				float fz = cz - z0;
				for (int x = 0; x < m; x++)
				{
					float cx = Math.Clamp((x + 0.5f) * half / m - 0.5f, 0f, half - 1);
					int x0 = Math.Min((int)cx, half - 2 < 0 ? 0 : half - 2);
					float fx = cx - x0;
					for (int c = 0; c < Channels; c++)
					{
						float a = coarse[(z0 * half + x0) * Channels + c];
						float b = coarse[(z0 * half + x0 + 1) * Channels + c];
						float d = coarse[((z0 + 1) * half + x0) * Channels + c];
						float e = coarse[((z0 + 1) * half + x0 + 1) * Channels + c];
						fine[(z * m + x) * Channels + c] = (a + (b - a) * fx) + ((d + (e - d) * fx) - (a + (b - a) * fx)) * fz;
					}
				}
			}
			return fine;
		}
	}
}
