using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic 16-bit signed integer (short) utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class ShortExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// Handles <see cref="short.MinValue"/> (-32768) by returning <see cref="short.MaxValue"/> (32767)
		/// to prevent overflow.
		/// </summary>
		/// <param name="number">The signed short to process.</param>
		/// <returns>The positive representation of the number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static short Absolute(this short number)
		{
			if (number == short.MinValue) return short.MaxValue;
			return (number < 0) ? (short)(-number) : number;
		}

		/// <summary>
		/// Returns the number clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lower bound.</param>
		/// <param name="maximum">The upper bound.</param>
		/// <returns>The clamped short value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static short Clamp(this short number, short minimum, short maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the signed short using deterministic range checks.
		/// </summary>
		/// <param name="number">The short to check.</param>
		/// <returns>The number of digits (e.g., -1000 returns 4, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this short number)
		{
			if (number == 0) return 1;

			// Handle MinValue specifically to avoid Absolute() overflow logic
			int abs = (number < 0) ? (number == short.MinValue ? 32768 : -number) : number;

			if (abs < 10) return 1;
			if (abs < 100) return 2;
			if (abs < 1000) return 3;
			if (abs < 10000) return 4;
			return 5;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The signed short to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit value (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static short GetDigit(this short number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			// Use int for calculation to safely handle 32768 (abs of MinValue)
			int val = (number < 0) ? (number == short.MinValue ? 32768 : -number) : number;

			for (int i = 0; i < digitIndex; i++)
			{
				val /= 10;
				if (val == 0) return 0;
			}
			return (short)(val % 10);
		}

		/// <summary>
		/// Calculates a percentage of a signed short using integer math with standard rounding (0.5 rounds up).
		/// </summary>
		/// <param name="number">The base value.</param>
		/// <param name="percent">The percentage to calculate (e.g. 10 for 10%).</param>
		/// <returns>The rounded result clamped to short boundaries.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static short GetPercentOf(this short number, int percent)
		{
			// Use long for intermediate calculation to avoid overflow
			// (short.MaxValue * int.MaxValue exceeds int.MaxValue)
			long result = (long)number * percent;

			if (result >= 0)
				result = (result + 50) / 100;
			else
				result = (result - 50) / 100;

			return (short)(result < short.MinValue ? short.MinValue : (result > short.MaxValue ? short.MaxValue : result));
		}
	}
}
