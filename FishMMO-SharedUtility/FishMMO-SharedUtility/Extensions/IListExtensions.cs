using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for IList, providing high-performance randomization and shuffling utilities.
	/// Utilizes Thread-Local Storage (TLS) to ensure high concurrency without lock contention.
	/// </summary>
	public static class IListExtensions
	{
		/// <summary>
		/// Individual Random instance for the current thread to prevent lock contention.
		/// </summary>
		[ThreadStatic]
		private static Random? localRandom;

		/// <summary>
		/// Gets a thread-safe Random instance for the current thread.
		/// Initializes a new instance if one does not exist for the calling thread.
		/// Uses a cryptographically random seed for better distribution across threads.
		/// </summary>
		private static Random Instance
		{
			get
			{
				if (localRandom == null)
				{
					// Use RandomNumberGenerator to get a full 4-byte seed instead of
					// relying on Guid.NewGuid().GetHashCode() which only uses 4 bytes
					// from a GUID and may produce correlated seeds across rapid
					// consecutive calls on different threads.
					Span<byte> seedBytes = stackalloc byte[4];
					RandomNumberGenerator.Fill(seedBytes);
					int seed = BitConverter.ToInt32(seedBytes);
					localRandom = new Random(seed);
				}
				return localRandom;
			}
		}

		/// <summary>
		/// Shuffles the elements of the list in place using the Fisher-Yates algorithm.
		/// This ensures a mathematically uniform distribution (O(n) complexity).
		/// </summary>
		/// <typeparam name="T">The type of elements in the list.</typeparam>
		/// <param name="list">The list to be shuffled.</param>
		public static void Shuffle<T>(this IList<T>? list)
		{
			if (list == null || list.Count <= 1) return;

			int n = list.Count;
			for (int i = n - 1; i > 0; i--)
			{
				// Select a random index from 0 to i inclusive
				int j = Instance.Next(0, i + 1);

				// Fisher-Yates shuffle using thread-local Random. Each element is swapped with a randomly selected element from the remaining unshuffled portion.
				T temp = list[j];
				list[j] = list[i];
				list[i] = temp;
			}
		}

		/// <summary>
		/// Returns a random element from the list without modifying the collection.
		/// </summary>
		/// <typeparam name="T">The type of elements in the list.</typeparam>
		/// <param name="list">The list to select from.</param>
		/// <returns>A random element, or default(T) if the list is null or empty.</returns>
		public static T? GetRandom<T>(this IList<T>? list)
		{
			if (list == null || list.Count == 0)
			{
				return default;
			}
			return list[Instance.Next(0, list.Count)];
		}

		/// <summary>
		/// Selects a random element and removes it from the list in a single operation.
		/// Useful for unique loot drops or exhausting a deck of cards.
		/// </summary>
		/// <typeparam name="T">The type of elements in the list.</typeparam>
		/// <param name="list">The list to modify.</param>
		/// <returns>The removed random element, or default(T) if the list is null or empty.</returns>
		public static T? TakeRandom<T>(this IList<T>? list)
		{
			if (list == null || list.Count == 0) return default;

			int index = Instance.Next(0, list.Count);
			T element = list[index];
			list.RemoveAt(index);
			return element;
		}
	}
}
