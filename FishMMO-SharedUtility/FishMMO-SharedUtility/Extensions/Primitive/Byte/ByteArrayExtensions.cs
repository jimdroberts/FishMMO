using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for byte arrays, providing high-performance comparison functionality.
	/// </summary>
	public static class ByteArrayExtensions
	{
		/// <summary>
		/// Compares two byte arrays for equality. 
		/// Utilizes optimized memory comparison for high-performance MMO networking tasks.
		/// </summary>
		/// <param name="first">The first byte array.</param>
		/// <param name="second">The second byte array to compare against.</param>
		/// <returns>True if arrays are equal in length and content; otherwise, false.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool Compare(this byte[] first, byte[] second)
		{
			// Null checks to prevent NullReferenceException
			if (first == null || second == null) return first == second;

			// Length check is a fast O(1) operation
			if (first.Length != second.Length) return false;

			// MemoryExtensions.SequenceEqual is highly optimized in .NET Core/.NET 5+
			// It uses hardware acceleration (SIMD) where available.
			return first.AsSpan().SequenceEqual(second);
		}
	}
}