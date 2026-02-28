using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic signed byte utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class SByteExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// Handles <see cref="sbyte.MinValue"/> (-128) by returning <see cref="sbyte.MaxValue"/> (127) 
		/// to prevent overflow.
		/// </summary>
		/// <param name="number">The signed byte to process.</param>
		/// <returns>The positive representation of the number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static sbyte Absolute(this sbyte number)
		{
			if (number == sbyte.MinValue) return sbyte.MaxValue;
			return (number < 0) ? (sbyte)(-number) : number;
		}

		/// <summary>
		/// Returns the number clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lowest possible result.</param>
		/// <param name="maximum">The highest possible result.</param>
		/// <returns>The clamped signed byte value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static sbyte Clamp(this sbyte number, sbyte minimum, sbyte maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the current value using deterministic range checks.
		/// </summary>
		/// <param name="number">The signed byte to check.</param>
		/// <returns>The number of digits (e.g., -120 returns 3, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this sbyte number)
		{
			if (number == 0) return 1;

			// Absolute value handled manually to avoid sbyte.MinValue overflow
			int abs = (number < 0) ? (number == sbyte.MinValue ? 128 : -number) : number;

			if (abs < 10) return 1;
			if (abs < 100) return 2;
			return 3;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The signed byte to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit value (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static sbyte GetDigit(this sbyte number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			// Use int for internal calculation to safely handle sbyte.MinValue (128)
			int val = (number < 0) ? (number == sbyte.MinValue ? 128 : -number) : number;

			for (int i = 0; i < digitIndex; i++)
			{
				val /= 10;
			}
			return (sbyte)(val % 10);
		}

		/// <summary>
		/// Calculates a percentage of a signed byte using integer math with standard rounding (0.5 rounds up).
		/// Formula: (number * percent + 50) / 100
		/// </summary>
		/// <param name="number">The base value.</param>
		/// <param name="percent">The percentage to calculate (e.g., 50 for 50%).</param>
		/// <returns>The rounded result clamped to sbyte boundaries.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static sbyte GetPercentOf(this sbyte number, int percent)
		{
			int result = (number * percent);

			// Standard rounding adjustment for integer division
			if (result >= 0)
				result = (result + 50) / 100;
			else
				result = (result - 50) / 100;

			return (sbyte)(result < sbyte.MinValue ? sbyte.MinValue : (result > sbyte.MaxValue ? sbyte.MaxValue : result));
		}
	}
}