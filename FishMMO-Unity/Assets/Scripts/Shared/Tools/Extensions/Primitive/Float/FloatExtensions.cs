using System;
using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	public static class FloatExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Absolute(this float number)
		{
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Returns the number clamped to the specified minimum and maximum value.
		/// </summary>
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static float Min(this float number, float minimum)
		{
			if (number < minimum)
			{
				return minimum;
			}
			return number;
		}

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
		/// Returns the number of digits of the current value.
		/// </summary>
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