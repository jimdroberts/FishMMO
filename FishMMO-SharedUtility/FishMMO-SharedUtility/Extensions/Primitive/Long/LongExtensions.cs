using System.Numerics;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for 64-bit integers, providing deterministic absolute, clamping, 
	/// digit counting, extraction, and scaling functionality.
	/// </summary>
	public static class LongExtensions
	{
		/// <summary>
		/// Returns the absolute value of the long number.
		/// Handles <see cref="long.MinValue"/> by returning <see cref="long.MaxValue"/>.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long Absolute(this long number)
		{
			if (number == long.MinValue) return long.MaxValue;
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Clamps the long value to the specified inclusive minimum and maximum range.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long Clamp(this long number, long minimum, long maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the number of digits in the long value using a deterministic range check.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this long number)
		{
			if (number == 0) return 1;
			// Handle MinValue specifically to avoid Absolute() overflow
			if (number == long.MinValue) return 19;

			long abs = (number < 0) ? -number : number;

			// Branching range check is faster and more deterministic than Math.Log10
			if (abs < 10L) return 1;
			if (abs < 100L) return 2;
			if (abs < 1000L) return 3;
			if (abs < 10000L) return 4;
			if (abs < 100000L) return 5;
			if (abs < 1000000L) return 6;
			if (abs < 10000000L) return 7;
			if (abs < 100000000L) return 8;
			if (abs < 1000000000L) return 9;
			if (abs < 10000000000L) return 10;
			if (abs < 100000000000L) return 11;
			if (abs < 1000000000000L) return 12;
			if (abs < 10000000000000L) return 13;
			if (abs < 100000000000000L) return 14;
			if (abs < 1000000000000000L) return 15;
			if (abs < 10000000000000000L) return 16;
			if (abs < 100000000000000000L) return 17;
			if (abs < 1000000000000000000L) return 18;
			return 19;
		}

		/// <summary>
		/// Returns the specified digit of the long value. Zero is the least significant digit.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long GetDigit(this long number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			// Use BigInteger for the absolute value to correctly handle long.MinValue
			BigInteger absVal = number == long.MinValue ? new BigInteger(9223372036854775808) : (number < 0 ? -number : number);

			for (int i = 0; i < digitIndex; ++i)
			{
				absVal /= 10L;
			}
			return (long)(absVal % 10L);
		}

		/// <summary>
		/// Normalizes the long value to a double in the range [0, 1] by dividing by long.MaxValue.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Normalize(this long number)
		{
			// We use the absolute to ensure the range is [0, 1] as per your documentation
			return (double)number.Absolute() / long.MaxValue;
		}

		/// <summary>
		/// Scales a normalized value (assumed 0 to 1) to an integer in the specified range.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ScaleToInt(this long number, int min = int.MinValue, int max = int.MaxValue)
		{
			double normalized = number.Normalize();
			// Use double for the range calculation to prevent overflow (max - min)
			return (int)(normalized * ((double)max - min) + min);
		}
	}
}