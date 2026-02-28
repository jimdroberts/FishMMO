using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic integer utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class IntExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// Handles <see cref="int.MinValue"/> by returning <see cref="int.MaxValue"/>.
		/// </summary>
		/// <param name="number">The integer to process.</param>
		/// <returns>The positive representation of the number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Absolute(this int number)
		{
			if (number == int.MinValue) return int.MaxValue;
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Returns the number clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lowest possible result.</param>
		/// <param name="maximum">The highest possible result.</param>
		/// <returns>The clamped integer value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Clamp(this int number, int minimum, int maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the current value.
		/// </summary>
		/// <param name="number">The integer to check.</param>
		/// <returns>The number of digits (e.g., -100 returns 3, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this int number)
		{
			if (number == 0) return 1;
			if (number == int.MinValue) return 10;

			// Integer-only log10 approach
			int abs = (number < 0) ? -number : number;
			if (abs < 10) return 1;
			if (abs < 100) return 2;
			if (abs < 1000) return 3;
			if (abs < 10000) return 4;
			if (abs < 100000) return 5;
			if (abs < 1000000) return 6;
			if (abs < 10000000) return 7;
			if (abs < 100000000) return 8;
			if (abs < 1000000000) return 9;
			return 10;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The integer to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit integer (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetDigit(this int number, int digitIndex)
		{
			long val = (number < 0) ? -(long)number : (long)number;
			for (int i = 0; i < digitIndex; i++) val /= 10;
			return (int)(val % 10);
		}

		/// <summary>
		/// Calculates a percentage of a number using integer math with standard rounding (0.5 rounds up).
		/// Formula: (number * percent + 50) / 100
		/// </summary>
		/// <param name="number">The base value.</param>
		/// <param name="percent">The percentage to calculate (e.g., 25 for 25%).</param>
		/// <returns>The rounded result.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetPercentOf(this int number, int percent)
		{
			// We use long to prevent overflow during the multiplication step
			long result = ((long)number * percent);

			// Standard rounding adjustment for integer division
			if (result >= 0)
				return (int)((result + 50) / 100);
			else
				return (int)((result - 50) / 100);
		}

		/// <summary>
		/// Subtracts a percentage from the number using deterministic integer math.
		/// Useful for calculating discounted prices or damage reduction.
		/// </summary>
		/// <param name="number">The starting value.</param>
		/// <param name="percent">The percentage to subtract.</param>
		/// <returns>The rounded result after subtraction.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int SubtractPercent(this int number, int percent)
		{
			int amountToSubtract = number.GetPercentOf(percent);
			return number - amountToSubtract;
		}
	}
}