namespace FishMMO.Shared
{
	public static class ArrayExtensions
	{
		public static T[] Populate<T>(this T[] array) where T : new()
		{
			for (int i = 0; i < array.Length; ++i)
			{
				array[i] = new T();
			}
			return array;
		}

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