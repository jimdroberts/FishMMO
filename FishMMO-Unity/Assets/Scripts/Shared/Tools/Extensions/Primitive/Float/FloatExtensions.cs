using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for float values, providing absolute, clamping, min/max, and digit counting functionality.
	/// </summary>
	public static class FloatExtensions
	{
		/// <summary>
		/// Returns the absolute value of the float number.
		/// </summary>
		/// <param name="number">Input float value.</param>
		/// <returns>Absolute value of the input.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Absolute(this float number)
		{
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Clamps the float value to the specified minimum and maximum range.
		/// </summary>
		/// <param name="number">Input float value.</param>
		/// <param name="minimum">Minimum allowed value.</param>
		/// <param name="maximum">Maximum allowed value.</param>
		/// <returns>Clamped value within the specified range.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Clamp(this float number, float minimum, float maximum)
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
		/// Returns the greater of the float value or the specified minimum.
		/// </summary>
		/// <param name="number">Input float value.</param>
		/// <param name="minimum">Minimum allowed value.</param>
		/// <returns>Minimum value if input is less, otherwise input value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Min(this float number, float minimum)
		{
			if (number < minimum)
			{
				return minimum;
			}
			return number;
		}

		/// <summary>
		/// Returns the lesser of the float value or the specified maximum.
		/// </summary>
		/// <param name="number">Input float value.</param>
		/// <param name="maximum">Maximum allowed value.</param>
		/// <returns>Maximum value if input is greater, otherwise input value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Max(this float number, float maximum)
		{
			if (number > maximum)
			{
				return maximum;
			}
			return number;
		}

		/// <summary>
		/// Returns the number of digits in the integer part of the float value.
		/// </summary>
		/// <param name="number">Input float value.</param>
		/// <returns>Number of digits in the integer part of the value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this float number)
		{
			if (number != 0)
			{
				return ((int)Math.Log10(number.Absolute())) + 1;
			}
			return 1;
		}
	}
}