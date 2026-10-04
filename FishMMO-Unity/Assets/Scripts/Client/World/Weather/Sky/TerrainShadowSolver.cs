using System;

namespace FishMMO.Client
{
	/// <summary>
	/// Where the terrain shades the air over it from one light: for each cell of a grid, the height below
	/// which a point there is in the shadow of some ground between it and the light.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Mist lies on the ground in the valleys, and a valley at evening is in its mountain's shadow — which
	/// the mist did not know: lit as the fog is (FishFogLight), it glowed in the sun under a ridge that had
	/// put the ground beneath it in shade. A shadow map does not reach: the main light's cascades end a few
	/// hundred metres out, and a mountain throws its evening shadow kilometres.
	/// </para>
	/// <para>
	/// So, once per light direction, this walks from each cell toward the light over the heights, and
	/// keeps the highest the ground stands above a ray rising at the light's elevation: the shadow's top
	/// is max over d of (H(d) − d·tan e). Anything in the column under that is in shadow, anything above
	/// it in the light — one number a cell, one texture read for the shader (FishMist.hlsl). Two grids:
	/// a fine one round the camera for the ridges near it, and a coarse one over the whole scene for the
	/// mountains kilometres off; outside both the ground is sea level. Each is walked a cell at a time, the
	/// walk stops when no ground anywhere could rise above the shadow found so far, and it is plain C#, run
	/// on a worker whenever the light has moved enough to matter.
	/// </para>
	/// </remarks>
	public static class TerrainShadowSolver
	{
		/// <summary>A grid of heights: n × n, row-major (z·n + x), cell-centred over [x0, x0 + size] × [z0, z0 + size].</summary>
		public struct Grid
		{
			public float[] Heights;
			public int N;
			public float X0, Z0, Size;
			public float Highest;

			public bool Valid => Heights != null && N > 1 && Size > 0f;

			public static Grid Of(float[] heights, int n, float x0, float z0, float size)
			{
				float highest = float.MinValue;
				if (heights != null)
				{
					foreach (float h in heights)
					{
						highest = Math.Max(highest, h);
					}
				}
				return new Grid { Heights = heights, N = n, X0 = x0, Z0 = z0, Size = size, Highest = highest };
			}

			/// <summary>The height at a point, bilinear; false outside the grid.</summary>
			public bool TryHeight(float x, float z, out float height)
			{
				height = 0f;
				if (!Valid)
				{
					return false;
				}
				float cell = Size / N;
				float u = (x - X0) / cell - 0.5f;
				float v = (z - Z0) / cell - 0.5f;
				if (u < -0.5f || v < -0.5f || u > N - 0.5f || v > N - 0.5f)
				{
					return false;
				}
				u = Math.Clamp(u, 0f, N - 1.001f);
				v = Math.Clamp(v, 0f, N - 1.001f);
				int i = (int)u, j = (int)v;
				float fu = u - i, fv = v - j;
				float a = Heights[j * N + i], b = Heights[j * N + i + 1];
				float c = Heights[(j + 1) * N + i], d = Heights[(j + 1) * N + i + 1];
				height = (a + (b - a) * fu) + ((c + (d - c) * fu) - (a + (b - a) * fu)) * fv;
				return true;
			}
		}

		/// <summary>What a cell holds when the light is down: everything under the sky is in shadow.</summary>
		public const float AllShade = 1e5f;
		/// <summary>What a cell holds when nothing shades it.</summary>
		public const float NoShade = -1e5f;

		/// <summary>
		/// The shadow's top (m) over each cell of <paramref name="fine"/>, for the light toward
		/// (<paramref name="lx"/>, <paramref name="ly"/>, <paramref name="lz"/>), looking up to
		/// <paramref name="reach"/> metres toward it over <paramref name="fine"/> and then <paramref name="coarse"/>.
		/// </summary>
		public static float[] ShadowTops(Grid fine, Grid coarse, float lx, float ly, float lz, float reach)
		{
			int n = fine.N;
			var tops = new float[n * n];
			float flat = (float)Math.Sqrt(lx * lx + lz * lz);
			// The light down (or straight overhead, which shades nothing).
			if (ly <= 0.005f)
			{
				Array.Fill(tops, AllShade);
				return tops;
			}
			if (flat < 1e-4f)
			{
				Array.Fill(tops, NoShade);
				return tops;
			}
			float hx = lx / flat, hz = lz / flat;
			float rise = ly / flat;
			float cell = fine.Size / n;
			float coarseCell = coarse.Valid ? coarse.Size / coarse.N : cell;
			float highest = Math.Max(fine.Highest, coarse.Valid ? coarse.Highest : float.MinValue);
			highest = Math.Max(highest, 0f);
			for (int z = 0; z < n; z++)
			{
				for (int x = 0; x < n; x++)
				{
					float px = fine.X0 + (x + 0.5f) * cell;
					float pz = fine.Z0 + (z + 0.5f) * cell;
					float top = NoShade;
					float d = cell;
					while (d < reach)
					{
						// Nothing anywhere stands high enough to rise above the shadow already found.
						if (highest - d * rise <= top)
						{
							break;
						}
						float qx = px + hx * d, qz = pz + hz * d;
						// Stepped at the resolution of whichever grid answers, never more coarsely: a step
						// that grew with distance walked straight over a ridge one cell thick.
						float step;
						if (fine.TryHeight(qx, qz, out float h))
						{
							step = cell;
						}
						else if (coarse.TryHeight(qx, qz, out h))
						{
							step = coarseCell;
						}
						else
						{
							h = 0f;
							step = Math.Max(coarseCell, 100f);
						}
						top = Math.Max(top, h - d * rise);
						d += step;
					}
					tops[z * n + x] = top;
				}
			}
			return tops;
		}
	}
}
