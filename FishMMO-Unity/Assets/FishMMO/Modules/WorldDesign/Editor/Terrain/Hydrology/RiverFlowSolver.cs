#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The water's flow down a river, solved: a lattice Boltzmann (D2Q9, BGK) steady flow on the river unrolled along
	/// its own line, metres along by cells from bank to bank, round its boulders. Eddies stand behind them, the current
	/// runs fastest mid-channel and slackens at the banks: what the water's foam and ripples ride (and, later, what
	/// carries anything floating).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Unrolled.</b> The grid is the river straightened: x along its line (one cell per <see cref="AlongMetres"/>), y
	/// across it (<see cref="Across"/> cells bank to bank). A bend's inside and outside run at the same length, which the
	/// eye does not miss; what it does see (a wake behind each rock, slack water at the banks, the current's core) the
	/// straightened channel has.
	/// </para>
	/// <para>
	/// <b>Windows.</b> A long river is solved in overlapping windows, each fed at its top by the flow leaving the window
	/// before: a window settles in a few passes of its own length, where the whole river would take as many of the
	/// river's. The overlap is thrown away, so no window's ends show.
	/// </para>
	/// <para>
	/// <b>Units.</b> Lattice units, inflow <see cref="InflowSpeed"/>; the answer is the velocity over that, so 1 is the
	/// section's mean speed and the runtime scales it by the river's own (discharge over area).
	/// </para>
	/// </remarks>
	public static class RiverFlowSolver
	{
		/// <summary>Cells across a river, bank to bank.</summary>
		public const int Across = 16;

		/// <summary>Metres of river per cell along it.</summary>
		public const float AlongMetres = 1f;

		/// <summary>The window solved at once, cells along, and how much of it overlaps the next.</summary>
		public const int Window = 192;
		public const int Overlap = 48;

		/// <summary>Inflow in lattice units: low, so the flow stays incompressible.</summary>
		public const float InflowSpeed = 0.06f;

		/// <summary>
		/// Relaxation time: the viscosity. Not water's own but the eddy viscosity of a turbulent river (its eddies mix fast and
		/// slow water across the current), still low enough that a slack wake stands behind each rock. At 0.56 the jets past
		/// the rocks were a cell or two wide beside dead water, up to 4.8 times the section's mean speed, and the ripples
		/// carried on them tore into bright lines along the flow (Flo Monolith, 2026-10-07).
		/// </summary>
		public const float Tau = 0.62f;

		/// <summary>
		/// How many times the solved field is mixed with its neighbours (open water only, a [1 2 1] across and along): the
		/// turbulence the lattice is too coarse to carry, spreading each shear layer over about a metre.
		/// </summary>
		public const int MixPasses = 2;

		private static readonly int[] Ex = { 0, 1, 0, -1, 0, 1, -1, -1, 1 };
		private static readonly int[] Ey = { 0, 0, 1, 0, -1, 1, 1, -1, -1 };
		private static readonly float[] W = { 4f / 9f, 1f / 9f, 1f / 9f, 1f / 9f, 1f / 9f, 1f / 36f, 1f / 36f, 1f / 36f, 1f / 36f };
		private static readonly int[] Opposite = { 0, 3, 4, 1, 2, 7, 8, 5, 6 };

		/// <summary>
		/// Solves a river's flow: <paramref name="length"/> cells along, <see cref="Across"/> across; <paramref name="solid"/>
		/// marks rock (boulders) in it, x fastest. Returns per cell (along, across) over the mean speed: 1 is the
		/// section's mean, 0 still water; inside rock, 0.
		/// </summary>
		public static float[] Solve(int length, bool[] solid, int passes = 4)
		{
			int ny = Across;
			var result = new float[length * ny * 2];
			if (length < 2)
			{
				return result;
			}
			// The inflow profile into the first window: plug flow, which the banks shape within a few widths.
			var inflow = new float[ny];
			for (int y = 0; y < ny; y++)
			{
				inflow[y] = InflowSpeed;
			}
			int start = 0;
			while (start < length)
			{
				int n = Math.Min(Window, length - start);
				bool last = start + n >= length;
				float[] window = SolveWindow(start, n, solid, length, inflow, passes, out float[] outflow);
				// Keep all but the overlap (the last window keeps everything).
				int keep = last ? n : Math.Max(1, n - Overlap);
				for (int x = 0; x < keep; x++)
				{
					/* Over the section's own mean along it: the runtime multiplies by the river's mean speed (discharge
					 * over area), so a section carries exactly its discharge however much the banks and rocks hold back. */
					float sum = 0f;
					int open = 0;
					for (int y = 0; y < ny; y++)
					{
						int src = (y * n + x) * 2;
						if (window[src] != 0f || window[src + 1] != 0f)
						{
							sum += window[src];
							open++;
						}
					}
					float mean = open > 0 ? Math.Max(1e-4f, sum / ny) : InflowSpeed;
					for (int y = 0; y < ny; y++)
					{
						int src = (y * n + x) * 2, dst = (y * length + start + x) * 2;
						result[dst] = window[src] / mean;
						result[dst + 1] = window[src + 1] / mean;
					}
				}
				if (last)
				{
					Mix(result, length, solid);
					break;
				}
				// The next window starts where this one's kept part ends, fed by the flow there.
				for (int y = 0; y < ny; y++)
				{
					inflow[y] = window[(y * n + keep) * 2];
				}
				start += keep;
			}
			return result;
		}

		/// <summary>
		/// The turbulent mixing the solve leaves out: each open cell averaged with its open neighbours ([1 2 1] across, then
		/// along), <see cref="MixPasses"/> times, rock left at 0; then each section put back to its own mean along (1), so it
		/// still carries exactly its discharge.
		/// </summary>
		private static void Mix(float[] field, int length, bool[] solid)
		{
			int ny = Across;
			var scratch = new float[field.Length];
			bool Open(int x, int y) => x >= 0 && x < length && y >= 0 && y < ny && !(solid != null && y * length + x < solid.Length && solid[y * length + x]);
			for (int pass = 0; pass < MixPasses; pass++)
			{
				for (int axis = 0; axis < 2; axis++)
				{
					for (int y = 0; y < ny; y++)
					{
						for (int x = 0; x < length; x++)
						{
							int c = (y * length + x) * 2;
							if (!Open(x, y))
							{
								scratch[c] = scratch[c + 1] = 0f;
								continue;
							}
							float u = 2f * field[c], v = 2f * field[c + 1], w = 2f;
							for (int side = -1; side <= 1; side += 2)
							{
								int nx = axis == 0 ? x : x + side, nyy = axis == 0 ? y + side : y;
								if (Open(nx, nyy))
								{
									int n = (nyy * length + nx) * 2;
									u += field[n];
									v += field[n + 1];
									w += 1f;
								}
							}
							scratch[c] = u / w;
							scratch[c + 1] = v / w;
						}
					}
					Array.Copy(scratch, field, field.Length);
				}
			}
			for (int x = 0; x < length; x++)
			{
				float sum = 0f;
				for (int y = 0; y < ny; y++)
				{
					sum += field[(y * length + x) * 2];
				}
				float mean = sum / ny;
				if (mean <= 1e-4f)
				{
					continue;
				}
				for (int y = 0; y < ny; y++)
				{
					int c = (y * length + x) * 2;
					field[c] /= mean;
					field[c + 1] /= mean;
				}
			}
		}

		/// <summary>One window: <paramref name="n"/> cells from <paramref name="start"/>, settled over <paramref name="passes"/> lengths. Returns (u, v) per cell, lattice units.</summary>
		private static float[] SolveWindow(int start, int n, bool[] solid, int length, float[] inflow, int passes, out float[] outflow)
		{
			int ny = Across;
			int cells = n * ny;
			var f = new float[cells * 9];
			var next = new float[cells * 9];
			var rock = new bool[cells];
			for (int y = 0; y < ny; y++)
			{
				for (int x = 0; x < n; x++)
				{
					int global = y * length + start + x;
					rock[y * n + x] = solid != null && global < solid.Length && solid[global];
				}
			}
			// Start at rest with the inflow everywhere: it settles far sooner than from still water.
			for (int c = 0; c < cells; c++)
			{
				float u = rock[c] ? 0f : inflow[c / n];
				Equilibrium(f, c, 1f, u, 0f);
			}
			float omega = 1f / Tau;
			int steps = Math.Max(200, passes * n);
			for (int step = 0; step < steps; step++)
			{
				// Collide and stream, in parallel by row.
				Parallel.For(0, ny, y =>
				{
					for (int x = 0; x < n; x++)
					{
						int c = y * n + x;
						if (rock[c])
						{
							continue;
						}
						int o = c * 9;
						float rho = 0f, ux = 0f, uy = 0f;
						for (int k = 0; k < 9; k++)
						{
							float fk = f[o + k];
							rho += fk;
							ux += fk * Ex[k];
							uy += fk * Ey[k];
						}
						if (rho > 1e-6f)
						{
							ux /= rho;
							uy /= rho;
						}
						float uu = 1.5f * (ux * ux + uy * uy);
						for (int k = 0; k < 9; k++)
						{
							float eu = Ex[k] * ux + Ey[k] * uy;
							float feq = W[k] * rho * (1f + 3f * eu + 4.5f * eu * eu - uu);
							float post = f[o + k] + omega * (feq - f[o + k]);
							int tx = x + Ex[k], ty = y + Ey[k];
							// The banks (past the grid's sides) and rock bounce it straight back.
							if (ty < 0 || ty >= ny || (tx >= 0 && tx < n && rock[ty * n + tx]))
							{
								next[o + Opposite[k]] = post;
								continue;
							}
							if (tx < 0 || tx >= n)
							{
								continue; // leaves the window: the ends are set below
							}
							next[(ty * n + tx) * 9 + k] = post;
						}
					}
				});
				// The ends: the inflow held at the top, the bottom left to flow out as it comes.
				for (int y = 0; y < ny; y++)
				{
					int top = y * n;
					if (!rock[top])
					{
						Equilibrium(next, top, 1f, inflow[y], 0f);
					}
					int bottom = y * n + n - 1, before = bottom - 1;
					if (!rock[bottom] && n > 1)
					{
						Array.Copy(next, before * 9, next, bottom * 9, 9);
					}
				}
				float[] swap = f;
				f = next;
				next = swap;
			}
			var velocity = new float[cells * 2];
			for (int c = 0; c < cells; c++)
			{
				if (rock[c])
				{
					continue;
				}
				int o = c * 9;
				float rho = 0f, ux = 0f, uy = 0f;
				for (int k = 0; k < 9; k++)
				{
					rho += f[o + k];
					ux += f[o + k] * Ex[k];
					uy += f[o + k] * Ey[k];
				}
				if (rho > 1e-6f)
				{
					velocity[c * 2] = ux / rho;
					velocity[c * 2 + 1] = uy / rho;
				}
			}
			outflow = new float[ny];
			for (int y = 0; y < ny; y++)
			{
				outflow[y] = velocity[(y * n + n - 1) * 2];
			}
			return velocity;
		}

		private static void Equilibrium(float[] f, int c, float rho, float ux, float uy)
		{
			float uu = 1.5f * (ux * ux + uy * uy);
			int o = c * 9;
			for (int k = 0; k < 9; k++)
			{
				float eu = Ex[k] * ux + Ey[k] * uy;
				f[o + k] = W[k] * rho * (1f + 3f * eu + 4.5f * eu * eu - uu);
			}
		}
	}
}
#endif
