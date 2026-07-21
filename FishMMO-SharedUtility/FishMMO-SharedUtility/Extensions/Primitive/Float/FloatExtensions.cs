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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Absolute(this float number)
		{
			return MathF.Abs(number);
		}

		/// <summary>
		/// Clamps the float value to the specified inclusive minimum and maximum range.
		/// </summary>
		/// <remarks>
		/// <b>NaN behavior:</b> IEEE 754 comparisons involving NaN always return false,
		/// so <see cref="float.NaN"/> passes through this clamp unmodified.
		/// If NaN should be replaced with <paramref name="minimum"/>, call
		/// <c>float.IsNaN()</c> before clamping.
		/// </remarks>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Clamp(this float number, float minimum, float maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the smaller of two single-precision floating-point numbers.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Min(this float number, float other)
		{
			return MathF.Min(number, other);
		}

		/// <summary>
		/// Returns the larger of two single-precision floating-point numbers.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Max(this float number, float other)
		{
			return MathF.Max(number, other);
		}

		/// <summary>
		/// Returns the number of digits in the integer part of the float value.
		/// Note: Accuracy is limited by the 7-digit precision of the float type.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this float number)
		{
			if (float.IsNaN(number)) return 0;

			float abs = MathF.Abs(number);
			if (abs < 1.0f) return 1;

			// MathF.Log10 is preferred for 32-bit floats to avoid double conversion overhead
			return (int)MathF.Log10(abs) + 1;
		}

		/// <summary>
		/// Determines if two floats are effectively equal within a specific tolerance (Epsilon).
		/// Standard for comparing physics or movement values in networked environments.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsNearlyEqual(this float a, float b, float epsilon = 0.0001f)
		{
			return MathF.Abs(a - b) < epsilon;
		}
	}
}