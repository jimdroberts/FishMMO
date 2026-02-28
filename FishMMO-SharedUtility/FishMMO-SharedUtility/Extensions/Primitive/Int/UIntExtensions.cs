using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic unsigned integer utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class UIntExtensions
	{
		/// <summary>
		/// Returns the absolute difference between two unsigned values.
		/// Prevents underflow by checking which value is larger before subtraction.
		/// </summary>
		/// <param name="number">The first value.</param>
		/// <param name="other">The value to subtract.</param>
		/// <returns>The positive difference between the two values.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint AbsoluteSubtract(this uint number, uint other)
		{
			return (number > other) ? (number - other) : (other - number);
		}

		/// <summary>
		/// Returns the value clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lowest possible result.</param>
		/// <param name="maximum">The highest possible result.</param>
		/// <returns>The clamped unsigned integer value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint Clamp(this uint number, uint minimum, uint maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the current value using deterministic range checks.
		/// </summary>
		/// <param name="number">The unsigned integer to check.</param>
		/// <returns>The number of digits (e.g., 100 returns 3, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this uint number)
		{
			if (number < 10) return 1;
			if (number < 100) return 2;
			if (number < 1000) return 3;
			if (number < 10000) return 4;
			if (number < 100000) return 5;
			if (number < 1000000) return 6;
			if (number < 10000000) return 7;
			if (number < 100000000) return 8;
			if (number < 1000000000) return 9;
			return 10;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The unsigned integer to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit value (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetDigit(this uint number, int digitIndex)
		{
			// Ensure the index isn't negative; if it's too high, result will naturally be 0
			if (digitIndex < 0) return 0;

			for (int i = 0; i < digitIndex; i++)
			{
				number /= 10;
			}
			return number % 10;
		}

		/// <summary>
		/// Calculates a percentage of a value using integer math with standard rounding (0.5 rounds up).
		/// Formula: (number * percent + 50) / 100
		/// </summary>
		/// <param name="number">The base value.</param>
		/// <param name="percent">The percentage to calculate (e.g., 20 for 20%).</param>
		/// <returns>The rounded result.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static uint GetPercentOf(this uint number, uint percent)
		{
			// Use ulong to prevent overflow during multiplication (uint.Max * 100 exceeds uint.Max)
			ulong result = ((ulong)number * percent);
			return (uint)((result + 50) / 100);
		}
	}
}