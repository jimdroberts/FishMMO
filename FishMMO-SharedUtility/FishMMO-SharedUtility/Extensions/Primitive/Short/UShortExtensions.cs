using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic 16-bit unsigned integer (ushort) utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class UShortExtensions
	{
		/// <summary>
		/// Returns the absolute difference between two unsigned 16-bit values.
		/// Prevents underflow by checking which value is larger before subtraction.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort AbsoluteSubtract(this ushort number, ushort other)
		{
			return (number > other) ? (ushort)(number - other) : (ushort)(other - number);
		}

		/// <summary>
		/// Returns the value clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort Clamp(this ushort number, ushort minimum, ushort maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the unsigned short using deterministic range checks.
		/// </summary>
		/// <param name="number">The ushort to check (0-65535).</param>
		/// <returns>The number of digits (e.g., 65535 returns 5, 0 returns 1).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this ushort number)
		{
			if (number < 10) return 1;
			if (number < 100) return 2;
			if (number < 1000) return 3;
			if (number < 10000) return 4;
			return 5;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		/// <param name="number">The unsigned short to extract from.</param>
		/// <param name="digitIndex">The zero-based index of the digit.</param>
		/// <returns>The single-digit value (0-9).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort GetDigit(this ushort number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			int val = number;
			for (int i = 0; i < digitIndex; i++)
			{
				val /= 10;
				if (val == 0) return 0;
			}
			return (ushort)(val % 10);
		}

		/// <summary>
		/// Calculates a percentage of an unsigned short using integer math with rounding.
		/// Formula: (number * percent + 50) / 100
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ushort GetPercentOf(this ushort number, ushort percent)
		{
			// Use long for intermediate calculation to avoid overflow (65535 * 65535 exceeds int.MaxValue)
			long result = ((long)number * percent + 50) / 100;
			return (ushort)(result > 65535 ? 65535 : result);
		}
	}
}
