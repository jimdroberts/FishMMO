using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for string, including high-performance deterministic hash code generation.
	/// </summary>
	public static class StringExtensions
	{
		/// <summary>
		/// Computes a deterministic 32-bit hash code for a string. 
		/// This remains consistent across different .NET versions, architectures, and app restarts.
		/// </summary>
		/// <param name="text">The string to hash.</param>
		/// <returns>A deterministic 32-bit integer hash code.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetDeterministicHashCode(this string text)
		{
			if (text == null) return 0;

			unchecked
			{
				// A slightly more robust version of the djb2 algorithm
				// Starting with a large prime helps distribute bits better
				int hash1 = (5381 << 16) + 5381;
				int hash2 = hash1;

				for (int i = 0; i < text.Length; i += 2)
				{
					hash1 = ((hash1 << 5) + hash1) ^ text[i];
					if (i + 1 < text.Length)
					{
						hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
					}
				}

				return hash1 + (hash2 * 1566083941);
			}
		}
	}
}