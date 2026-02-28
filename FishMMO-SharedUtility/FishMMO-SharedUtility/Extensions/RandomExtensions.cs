using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Random, providing support for high-precision 64-bit random generation.
	/// </summary>
	public static class RandomExtensions
	{
		/// <summary>
		/// Returns a random unsigned 64-bit integer.
		/// Optimized to use Span to avoid heap allocations.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong NextULong(this System.Random random)
		{
			// Using Span on the stack prevents 'new byte[8]' allocations and GC pressure
			Span<byte> buffer = stackalloc byte[8];
			random.NextBytes(buffer);
			return BitConverter.ToUInt64(buffer);
		}

		/// <summary>
		/// Returns a random long integer between min (inclusive) and max (exclusive).
		/// Implements rejection sampling to eliminate modulo bias.
		/// </summary>
		public static long Next(this System.Random random, long min, long max)
		{
			if (min == max) return min;

			if (min > max)
			{
				(min, max) = (max, min);
			}

			// Using ulong for range to handle the full span of long.MinValue to long.MaxValue
			ulong range = (ulong)(max - min);

			// Calculate limit for rejection sampling to ensure perfectly uniform distribution
			// This prevents certain numbers from appearing slightly more often than others
			ulong limit = ulong.MaxValue - (ulong.MaxValue % range);

			ulong result;
			do
			{
				result = random.NextULong();
			} while (result >= limit);

			return (long)(result % range) + min;
		}
	}
}