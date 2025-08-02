using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for IList, providing randomization and shuffling utilities.
	/// </summary>
	public static class IListExtensions
	{
		/// <summary>
		/// Lock object for thread-safe random number generation.
		/// </summary>
		internal static readonly object LockObj = new object();

		/// <summary>
		/// Shared random number generator for all extension methods.
		/// </summary>
		internal static Random Random = new Random();

		/// <summary>
		/// Returns a random integer between min (inclusive) and max (exclusive) in a thread-safe manner.
		/// </summary>
		/// <param name="min">Minimum value (inclusive).</param>
		/// <param name="max">Maximum value (exclusive).</param>
		/// <returns>A random integer in the specified range.</returns>
		internal static int GetNext(int min, int max)
		{
			lock (LockObj)
			{
				return Random.Next(min, max);
			}
		}

		/// <summary>
		/// Shuffles the elements of the list in place using the Fisher-Yates algorithm.
		/// </summary>
		/// <typeparam name="T">Type of list element.</typeparam>
		/// <param name="list">The list to shuffle.</param>
		public static void Shuffle<T>(this IList<T> list)
		{
			int i = list.Count - 1;
			while (i > 1)
			{
				--i;
				int j = GetNext(0, i + 1);
				T random = list[j];
				list[j] = list[i];
				list[i] = random;
			}
		}

		/// <summary>
		/// Randomizes the order of elements in the list by swapping each element with a random index.
		/// </summary>
		/// <typeparam name="T">Type of list element.</typeparam>
		/// <param name="list">The list to randomize.</param>
		public static void Randomize<T>(this IList<T> list)
		{
			for (int i = 0; i < list.Count; ++i)
			{
				int j = GetNext(0, list.Count - 1);
				T element = list[j];
				list[j] = list[i];
				list[i] = element;
			}
		}

		/// <summary>
		/// Returns a random element from the list, or default if the list is null or empty.
		/// </summary>
		/// <typeparam name="T">Type of list element.</typeparam>
		/// <param name="list">The list to select from.</param>
		/// <returns>A random element from the list, or default(T) if empty.</returns>
		public static T GetRandom<T>(this IList<T> list)
		{
			if (list == null || list.Count < 1)
			{
				return default;
			}
			return list[GetNext(0, list.Count - 1)];
		}
	}
}