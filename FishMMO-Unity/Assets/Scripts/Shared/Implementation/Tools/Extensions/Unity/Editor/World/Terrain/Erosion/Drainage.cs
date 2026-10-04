#if UNITY_EDITOR
using System;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How much water runs through every cell of a grid: its own rain and everything upstream of it,
	/// routed over the whole grid.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why erosion needs it.</b> A droplet carries the water it fell with, so one on a valley
	/// floor that drains a square kilometre carries no more than one on the hillside above, and
	/// valley floors filled instead of cutting. Real rivers carry more because their water adds up;
	/// this is that sum, and droplets read it as their discharge.
	/// </para>
	/// <para>
	/// <b>Spread, not single file.</b> Water is shared among every lower neighbour in proportion to
	/// the slope towards it (multiple flow direction, Freeman 1991), so it spreads on hillsides and
	/// gathers in valleys, rather than running in the ruler-straight lines a single steepest
	/// neighbour draws on a grid.
	/// </para>
	/// <para>
	/// <b>Through hollows.</b> Routed over the ground with every hollow filled to its brim, by a
	/// priority flood from the grid's edge and the water (Barnes et al. 2014), so water reaching a
	/// hollow carries on over its lowest rim rather than stopping. The order the flood reaches cells
	/// in rises strictly, which is the order water is passed down in.
	/// </para>
	/// <para>Sequential and tie-broken by cell index, so deterministic.</para>
	/// </remarks>
	public static class Drainage
	{
		/// <summary>The rise forced on each cell of a filled hollow, so its water has a way out, in metres.</summary>
		private const float FillStep = 1e-3f;

		/// <summary>The exponent on slope when sharing water among lower neighbours: about 1 spreads it, higher concentrates it.</summary>
		public const float SpreadExponent = 1.1f;

		/// <summary>
		/// The area draining through every cell, in m², each cell's own weighted by its rain.
		/// </summary>
		/// <param name="grid">The ground.</param>
		/// <param name="ground">For the rain at each cell.</param>
		public static float[] Area(ErosionGrid grid, IErosionGround ground)
		{
			int width = grid.Width, depth = grid.Depth, count = width * depth;
			float[] height = grid.Height;
			var filled = new float[count];
			var order = new int[count];
			var queued = new bool[count];
			var heap = new CellHeap(count);

			for (int n = 0; n < count; n++)
			{
				filled[n] = height[n];
				int x = n % width, z = n / width;
				if (x == 0 || z == 0 || x == width - 1 || z == depth - 1 || height[n] < grid.BaseLevel)
				{
					queued[n] = true;
					heap.Push(height[n], n);
				}
			}
			int ordered = 0;
			while (heap.Count > 0)
			{
				int c = heap.Pop();
				order[ordered++] = c;
				int cx = c % width, cz = c / width;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int x = cx + dx, z = cz + dz;
						if ((dx == 0 && dz == 0) || x < 0 || z < 0 || x >= width || z >= depth)
						{
							continue;
						}
						int n = z * width + x;
						if (queued[n])
						{
							continue;
						}
						queued[n] = true;
						filled[n] = Math.Max(height[n], filled[c] + FillStep);
						heap.Push(filled[n], n);
					}
				}
			}

			// Each cell's own rain, then passed down from the highest cells to the lowest.
			float cellArea = grid.CellMetres * grid.CellMetres;
			var area = new float[count];
			for (int n = 0; n < count; n++)
			{
				area[n] = cellArea * Math.Max(0f, ground.Rain(n));
			}
			Span<int> lower = stackalloc int[8];
			Span<float> share = stackalloc float[8];
			for (int t = ordered - 1; t >= 0; t--)
			{
				int n = order[t];
				int x = n % width, z = n / width;
				if (x == 0 || z == 0 || x == width - 1 || z == depth - 1 || height[n] < grid.BaseLevel)
				{
					continue; // an outlet: its water leaves the grid
				}
				int found = 0;
				float total = 0f;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dz == 0)
						{
							continue;
						}
						int m = (z + dz) * width + x + dx;
						float drop = filled[n] - filled[m];
						if (drop <= 0f)
						{
							continue;
						}
						float slope = drop / (dx != 0 && dz != 0 ? 1.41421356f : 1f);
						float weight = MathF.Pow(slope, SpreadExponent);
						lower[found] = m;
						share[found] = weight;
						total += weight;
						found++;
					}
				}
				for (int k = 0; k < found; k++)
				{
					area[lower[k]] += area[n] * share[k] / total;
				}
			}
			return area;
		}
	}
}
#endif
