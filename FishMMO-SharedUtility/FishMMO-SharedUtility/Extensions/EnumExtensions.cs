using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods and caching utilities for working with enums.
	/// </summary>
	public static class EnumExtensions
	{
		private static readonly object cacheLock = new object();
		private static Array? lastValues;
		private static Type? lastEnumType;

		/// <summary>
		/// Returns an array of all values of the specified enum type.
		/// Uses an internal cache to avoid expensive reflection on subsequent calls.
		///
		/// NOTE: A new clone is returned on each call so callers cannot mutate the cached copy.
		/// The cache is invalidated when the enum type changes between calls.
		/// </summary>
		/// <typeparam name="T">The enum type.</typeparam>
		/// <returns>A copied array of all enum values.</returns>
		public static T[] ToArray<T>() where T : Enum
		{
			lock (cacheLock)
			{
				Type currentType = typeof(T);
				if (lastEnumType == currentType && lastValues != null)
				{
					// Return a clone of the cached array to prevent callers from mutating the shared cache.
					return (T[])lastValues.Clone();
				}

				lastValues = Enum.GetValues(currentType);
				lastEnumType = currentType;
				return (T[])lastValues.Clone();
			}
		}
	}
}
