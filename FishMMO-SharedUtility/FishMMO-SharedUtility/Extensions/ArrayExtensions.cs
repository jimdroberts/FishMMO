using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for populating jagged arrays with new instances of type T.
	/// Useful for initializing grids, voxels, or nested data structures in FishMMO.
	/// </summary>
	public static class ArrayExtensions
	{
		/// <summary>
		/// Populates a 1D array with new instances of type T.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T[]? Populate<T>(this T[]? array) where T : new()
		{
			if (array == null) return null;
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T();
			}
			return array;
		}

		/// <summary>
		/// Populates a 2D jagged array. Each sub-array is sized to the outer array's length.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T[][]? Populate<T>(this T[][]? array) where T : new()
		{
			if (array == null) return null;
			return Populate(array, array.Length);
		}

		/// <summary>
		/// Populates a 2D jagged array specifying the height (inner dimension) of each sub-array.
		/// </summary>
		public static T[][]? Populate<T>(this T[][]? array, int height) where T : new()
		{
			if (array == null) return null;
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[height];
				for (int j = 0; j < height; ++j)
				{
					array[i][j] = new T();
				}
			}
			return array;
		}

		/// <summary>
		/// Populates a 3D jagged array using the outer dimension for all sizes.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T[][][]? Populate<T>(this T[][][]? array) where T : new()
		{
			if (array == null) return null;
			return Populate(array, array.Length, array.Length);
		}

		/// <summary>
		/// Populates a 3D jagged array with specific height and depth.
		/// </summary>
		public static T[][][]? Populate<T>(this T[][][]? array, int height, int depth) where T : new()
		{
			if (array == null) return null;
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[height][];
				for (int j = 0; j < height; ++j)
				{
					array[i][j] = new T[depth];
					for (int k = 0; k < depth; ++k)
					{
						// FIXED: Replaced array[k].Length with local depth variable
						array[i][j][k] = new T();
					}
				}
			}
			return array;
		}
	}
}