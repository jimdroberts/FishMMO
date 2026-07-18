using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for double-precision floating-point numbers.
	/// </summary>
	public static class DoubleExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// </summary>
		/// <param name="number">The value to process.</param>
		/// <returns>The positive representation of the number.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Absolute(this double number)
		{
			return Math.Abs(number);
		}

		/// <summary>
		/// Returns the number clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <param name="number">The value to clamp.</param>
		/// <param name="minimum">The lower bound.</param>
		/// <param name="maximum">The upper bound.</param>
		/// <returns>The clamped value.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static double Clamp(this double number, double minimum, double maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the number of digits in the integer portion of the value.
		/// Note: For values larger than 10^15, precision limits of double may affect accuracy.
		/// </summary>
		/// <param name="number">The value to check.</param>
		/// <returns>The count of digits before the decimal point.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this double number)
		{
			if (double.IsNaN(number)) return 0;

			double abs = Math.Abs(number);

			// Handle zero and fractions between -1 and 1
			if (abs < 1.0) return 1;

			// Log10 of a double is natively supported and efficient for scientific calculations
			return (int)Math.Log10(abs) + 1;
		}

		/// <summary>
		/// Determines if two doubles are effectively equal within a specific tolerance.
		/// Crucial for avoiding precision errors in networked game logic.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsNearlyEqual(this double a, double b, double epsilon = 0.000001)
		{
			return Math.Abs(a - b) < epsilon;
		}
	}
}