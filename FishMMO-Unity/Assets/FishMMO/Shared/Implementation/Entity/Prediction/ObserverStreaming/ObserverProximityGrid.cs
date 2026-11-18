using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A uniform horizontal grid over one scene's registered objects, so ranking a viewer reads only the objects that
	/// could be within reach of it instead of every object in the scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A superset, never a filter of its own.</b> <see cref="Query"/> returns every object whose cell overlaps the
	/// square round the point, padded by <see cref="QueryMargin"/>, so anything within the radius in 3D (horizontal
	/// distance is never more than 3D distance) is always in it, rounding included. The caller still measures each one
	/// exactly as before and applies the same tests; the grid only skips objects no test could pass.
	/// </para>
	/// <para>
	/// <b>Dense, sorted by cell.</b> The objects' bounding box of cells is one array, and the object indices are sorted
	/// by cell (a counting sort), row by row, so each row of a query's square is one contiguous run: a query costs a few
	/// array reads per row plus the objects it returns, with no hashing. A box that would be too many cells for the
	/// object count (a few far-flung objects) doubles the cell size until it is not.
	/// </para>
	/// </remarks>
	public sealed class ObserverProximityGrid
	{
		/// <summary>Padding round every query, metres: far more than any float rounding at world coordinates.</summary>
		public const float QueryMargin = 1f;

		private int[] cellStart = new int[0];
		private int[] order = new int[0];
		private int[] cellOf = new int[0];
		private int minX, minZ, width, height;

		/// <summary>Cell side, metres.</summary>
		public float CellSize { get; private set; } = 1f;

		/// <summary>Objects in the grid.</summary>
		public int Count { get; private set; }

		/// <summary>Files each position by index, replacing whatever the grid held.</summary>
		public void Build(List<Vector3> positions, float cellSize)
		{
			Count = positions.Count;
			CellSize = Mathf.Max(1f, float.IsNaN(cellSize) || float.IsInfinity(cellSize) ? 1f : cellSize);
			if (Count == 0)
			{
				width = height = 0;
				return;
			}

			// The box of occupied cells, kept to a size proportional to the objects.
			long budget = System.Math.Max(4096L, 8L * Count);
			while (true)
			{
				int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
				for (int i = 0; i < Count; ++i)
				{
					int x = Mathf.FloorToInt(positions[i].x / CellSize), z = Mathf.FloorToInt(positions[i].z / CellSize);
					if (x < x0) x0 = x;
					if (x > x1) x1 = x;
					if (z < z0) z0 = z;
					if (z > z1) z1 = z;
				}
				long cells = ((long)x1 - x0 + 1) * ((long)z1 - z0 + 1);
				if (cells <= budget || CellSize > 1e6f)
				{
					minX = x0;
					minZ = z0;
					width = x1 - x0 + 1;
					height = z1 - z0 + 1;
					break;
				}
				CellSize *= 2f;
			}

			int cellCount = width * height;
			if (cellStart.Length < cellCount + 1)
			{
				cellStart = new int[System.Math.Max(cellCount + 1, cellStart.Length * 2)];
			}
			if (order.Length < Count)
			{
				order = new int[System.Math.Max(Count, order.Length * 2)];
				cellOf = new int[order.Length];
			}
			System.Array.Clear(cellStart, 0, cellCount + 1);
			for (int i = 0; i < Count; ++i)
			{
				int x = Mathf.FloorToInt(positions[i].x / CellSize) - minX, z = Mathf.FloorToInt(positions[i].z / CellSize) - minZ;
				int cell = z * width + x;
				cellOf[i] = cell;
				cellStart[cell + 1]++;
			}
			for (int c = 0; c < cellCount; ++c)
			{
				cellStart[c + 1] += cellStart[c];
			}
			// Fill each cell's run from its end, so the starts are left as they were.
			for (int i = Count - 1; i >= 0; --i)
			{
				order[--cellStart[cellOf[i] + 1]] = i;
			}
			// cellStart[c + 1] now holds the start of cell c: shift the starts back into place.
			for (int c = 0; c < cellCount; ++c)
			{
				cellStart[c] = cellStart[c + 1];
			}
			cellStart[cellCount] = Count;
		}

		/// <summary>
		/// Every object that could be within <paramref name="radius"/> of <paramref name="at"/>, by index, in no
		/// particular order. Every object for an infinite or NaN radius.
		/// </summary>
		public void Query(Vector3 at, float radius, List<int> into)
		{
			into.Clear();
			if (Count == 0)
			{
				return;
			}
			float reach = radius + QueryMargin;
			if (float.IsNaN(reach) || float.IsInfinity(reach) || reach > 1e7f)
			{
				for (int i = 0; i < Count; ++i)
				{
					into.Add(i);
				}
				return;
			}
			int x0 = Mathf.FloorToInt((at.x - reach) / CellSize) - minX, x1 = Mathf.FloorToInt((at.x + reach) / CellSize) - minX;
			int z0 = Mathf.FloorToInt((at.z - reach) / CellSize) - minZ, z1 = Mathf.FloorToInt((at.z + reach) / CellSize) - minZ;
			if (x0 < 0) x0 = 0;
			if (z0 < 0) z0 = 0;
			if (x1 >= width) x1 = width - 1;
			if (z1 >= height) z1 = height - 1;
			if (x0 > x1 || z0 > z1)
			{
				return;
			}
			for (int z = z0; z <= z1; ++z)
			{
				int row = z * width;
				int end = cellStart[row + x1 + 1];
				for (int k = cellStart[row + x0]; k < end; ++k)
				{
					into.Add(order[k]);
				}
			}
		}
	}
}
