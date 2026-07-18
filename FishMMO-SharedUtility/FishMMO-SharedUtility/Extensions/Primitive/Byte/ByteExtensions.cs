using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic byte utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class ByteExtensions
	{
		/// <summary>
		/// Returns the byte clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lowest possible result.</param>
		/// <param name="maximum">The highest possible result.</param>
		/// <returns>The clamped byte value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte Clamp(this byte number, byte minimum, byte maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the byte value using deterministic range checks.
		/// </summary>
		/// <param name="number">The byte to check (0-255).</param>
		/// <returns>The number of digits (e.g., 255 returns 3, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this byte number)
		{
			if (number < 10) return 1;
			if (number < 100) return 2;
			return 3;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The byte to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit value (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte GetDigit(this byte number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			int val = number;
			for (int i = 0; i < digitIndex; i++)
			{
				val /= 10;
			}
			return (byte)(val % 10);
		}

		/// <summary>
		/// Calculates a percentage of a byte using integer math with standard rounding (0.5 rounds up).
		/// Useful for small-value modifiers like status effects.
		/// </summary>
		/// <param name="number">The base value.</param>
		/// <param name="percent">The percentage to calculate (0-100+).</param>
		/// <returns>The rounded result clamped back to a byte.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static byte GetPercentOf(this byte number, int percent)
		{
			// Use int to prevent overflow during calculation (255 * 100 exceeds byte.Max)
			int result = (number * percent + 50) / 100;
			return (byte)(result > 255 ? 255 : result);
		}
	}
}