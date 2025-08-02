using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for System.Random, providing support for ulong and long random generation.
	/// </summary>
	public static class RandomExtensions
	{
		/// <summary>
		/// Returns a random unsigned 64-bit integer using the provided Random instance.
		/// </summary>
		/// <param name="random">The Random instance.</param>
		/// <returns>A random ulong value.</returns>
		public static ulong NextULong(this System.Random random)
		{
			byte[] bytes = new byte[8];
			random.NextBytes(bytes);
			return BitConverter.ToUInt64(bytes, 0);
		}

		/// <summary>
		/// Returns a random long integer between min (inclusive) and max (exclusive) using the provided Random instance.
		/// </summary>
		/// <param name="random">The Random instance.</param>
		/// <param name="min">Minimum value (inclusive).</param>
		/// <param name="max">Maximum value (exclusive).</param>
		/// <returns>A random long value in the specified range.</returns>
		public static long Next(this System.Random random, long min, long max)
		{
			if (min > max)
			{
				// Swap min and max if out of order
				long tmp = max;
				max = min;
				min = tmp;
			}
			ulong range = (ulong)(max - min);
			if (range == 0)
			{
				return max;
			}
			ulong result;
			// Calculate a limit to avoid modulo bias
			ulong limit = ulong.MaxValue - (((ulong.MaxValue % range) + 1) % range);
			do
			{
				result = NextULong(random);
			} while (result > limit);

			return (long)(result % range + (ulong)min);
		}
	}
}