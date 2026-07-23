using System;
using System.Collections.Concurrent;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods and caching utilities for working with enums.
	/// </summary>
	public static class EnumExtensions
	{
		private static readonly ConcurrentDictionary<Type, Array> enumValueCache = new ConcurrentDictionary<Type, Array>();

		/// <summary>
		/// Returns an array of all values of the specified enum type.
		/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> to cache the results
		/// so that reflection is only performed once per enum type. Thread-safe without
		/// requiring an explicit lock.
		///
		/// NOTE: A new clone is returned on each call so callers cannot mutate the cached copy.
		/// </summary>
		/// <typeparam name="T">The enum type.</typeparam>
		/// <returns>A copied array of all enum values.</returns>
		public static T[] ToArray<T>() where T : Enum
		{
			Type currentType = typeof(T);
			Array values = enumValueCache.GetOrAdd(currentType, Enum.GetValues);
			return (T[])values.Clone();
		}
	}
}
