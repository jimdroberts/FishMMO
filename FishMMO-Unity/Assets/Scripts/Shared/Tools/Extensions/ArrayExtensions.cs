namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for populating arrays and multi-dimensional arrays with new instances of type T.
	/// </summary>
	public static class ArrayExtensions
	{
		/// <summary>
		/// Populates a 1D array with new instances of type T.
		/// </summary>
		/// <typeparam name="T">Type of array element (must have a parameterless constructor).</typeparam>
		/// <param name="array">The array to populate.</param>
		/// <returns>The populated array.</returns>
		public static T[] Populate<T>(this T[] array) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T();
			}
			return array;
		}

		/// <summary>
		/// Populates a 2D array with new instances of type T. Each sub-array is sized to the outer array's length.
		/// </summary>
		/// <typeparam name="T">Type of array element (must have a parameterless constructor).</typeparam>
		/// <param name="array">The 2D array to populate.</param>
		/// <returns>The populated 2D array.</returns>
		public static T[][] Populate<T>(this T[][] array) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[array.Length];
				for (int j = 0; j < array[i].Length; ++j)
				{
					array[i][j] = new T();
				}
			}
			return array;
		}

		/// <summary>
		/// Populates a 3D array with new instances of type T. Each sub-array is sized to the outer array's length.
		/// </summary>
		/// <typeparam name="T">Type of array element (must have a parameterless constructor).</typeparam>
		/// <param name="array">The 3D array to populate.</param>
		/// <returns>The populated 3D array.</returns>
		public static T[][][] Populate<T>(this T[][][] array) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[array.Length][];
				for (int j = 0; j < array[i].Length; ++j)
				{
					array[i][j] = new T[array[i].Length];
					for (int k = 0; k < array[k].Length; ++k)
					{
						array[i][j][k] = new T();
					}
				}
			}
			return array;
		}


		/// <summary>
		/// Populates a 2D array with new instances of type T, specifying the height of each sub-array.
		/// </summary>
		/// <typeparam name="T">Type of array element (must have a parameterless constructor).</typeparam>
		/// <param name="array">The 2D array to populate.</param>
		/// <param name="height">The length of each sub-array.</param>
		/// <returns>The populated 2D array.</returns>
		public static T[][] Populate<T>(this T[][] array, int height) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[height];
				for (int j = 0; j < array[i].Length; ++j)
				{
					array[i][j] = new T();
				}
			}
			return array;
		}

		/// <summary>
		/// Populates a 3D array with new instances of type T, specifying the height and depth of each sub-array.
		/// </summary>
		/// <typeparam name="T">Type of array element (must have a parameterless constructor).</typeparam>
		/// <param name="array">The 3D array to populate.</param>
		/// <param name="height">The length of each 2D sub-array.</param>
		/// <param name="depth">The length of each 1D sub-array.</param>
		/// <returns>The populated 3D array.</returns>
		public static T[][][] Populate<T>(this T[][][] array, int height, int depth) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T[height][];
				for (int j = 0; j < array[i].Length; ++j)
				{
					array[i][j] = new T[depth];
					for (int k = 0; k < array[k].Length; ++k)
					{
						array[i][j][k] = new T();
					}
				}
			}
			return array;
		}
	}
}