using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for long values, providing absolute, clamping, digit counting, digit extraction, normalization, and scaling functionality.
	/// </summary>
	public static class LongExtensions
	{
		/// <summary>
		/// Returns the absolute value of the long number.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <returns>Absolute value of the input.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long Absolute(this long number)
		{
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Clamps the long value to the specified minimum and maximum range.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <param name="minimum">Minimum allowed value.</param>
		/// <param name="maximum">Maximum allowed value.</param>
		/// <returns>Clamped value within the specified range.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long Clamp(this long number, long minimum, long maximum)
		{
			if (number < minimum)
			{
				return minimum;
			}
			if (number > maximum)
			{
				return maximum;
			}
			return number;
		}

		/// <summary>
		/// Returns the number of digits in the integer part of the long value.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <returns>Number of digits in the integer part of the value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this long number)
		{
			if (number != 0)
			{
				return ((int)Math.Log10(number.Absolute())) + 1;
			}
			return 1;
		}

		/// <summary>
		/// Returns the specified digit of the long value. Zero is the least significant digit.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <param name="digit">Digit position to extract (0 = least significant).</param>
		/// <returns>Value of the specified digit.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static long GetDigit(this long number, int digit)
		{
			const byte MIN_DIGITS = 0;
			const byte BASE_TEN = 10;

			number = number.Absolute();
			digit = digit.Clamp(MIN_DIGITS, number.DigitCount());
			for (int i = MIN_DIGITS; i < digit; ++i)
			{
				number /= BASE_TEN;
			}
			return number % BASE_TEN;
		}

		/// <summary>
		/// Normalizes the long value to a double in the range [0, 1] by dividing by long.MaxValue.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <returns>Normalized double value in [0, 1].</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Normalize(this long number)
		{
			return (double)number / long.MaxValue;
		}

		/// <summary>
		/// Scales the normalized long value to an integer in the specified range.
		/// </summary>
		/// <param name="number">Input long value.</param>
		/// <param name="min">Minimum integer value (default: int.MinValue).</param>
		/// <param name="max">Maximum integer value (default: int.MaxValue).</param>
		/// <returns>Scaled integer value in the specified range.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ScaleToInt(this long number, int min = int.MinValue, int max = int.MaxValue)
		{
			double normalized = number.Normalize();

			return (int)(normalized * (max - min) + min);
		}
	}
}