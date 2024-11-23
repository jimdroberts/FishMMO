using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FishMMO.Shared
{
	public static class IntExtensions
	{
		/// <summary>
		/// Returns the absolute value of the number.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Absolute(this int number)
		{
			return (number < 0) ? -number : number;
		}

		/// <summary>
		/// Returns the number clamped to the specified minimum and maximum value.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Clamp(this int number, int minimum, int maximum)
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
		/// Returns the number of digits of the current value.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this int number)
		{
			if (number != 0)
			{
				return ((int)Math.Log10(number.Absolute())) + 1;
			}
			return 1;
		}

		/// <summary>
		/// Returns the specified digit of the number. Where zero is the least significant digit.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetDigit(this int number, int digit)
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
		/// Returns a rounded percent of the number.
		/// For example, 100.GetPercentOf(25) returns 25. (25% of 100).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int GetPercentOf(this int number, int percent)
		{
			return Mathf.RoundToInt(number * (percent * 0.01f));
		}

		/// <summary>
		/// Subtracts a percentage from the number and returns the result, rounded to the nearest integer.
		/// For example, 100.SubtractPercent(25) returns 75. (100 - 25% of 100).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int SubtractPercent(this int number, int percent)
		{
			return Mathf.RoundToInt(number * (1 - (percent * 0.01f)));
		}
	}
}