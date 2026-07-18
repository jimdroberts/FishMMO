using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods and caching utilities for working with enums.
	/// </summary>
	public static class EnumExtensions
	{
		/// <summary>
		/// Returns an array of all values of the specified enum type.
		/// Uses an internal cache to avoid expensive reflection and array allocation on subsequent calls.
		/// </summary>
		/// <typeparam name="T">The enum type.</typeparam>
		/// <returns>A cached array of all enum values.</returns>
		public static T[] ToArray<T>() where T : Enum
		{
			// Return a copy of the cached array to prevent callers from mutating the shared cache.
			return (T[])EnumCache<T>.Values.Clone();
		}

		/// <summary>
		/// Internal cache to store enum values. Static constructors in generic classes 
		/// run once per unique type T.
		/// </summary>
		private static class EnumCache<T> where T : Enum
		{
			public static readonly T[] Values = (T[])Enum.GetValues(typeof(T));
		}
	}
}