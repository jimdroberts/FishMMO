using System;

namespace FishMMO.Shared
{
	public static class UShortExtensions
	{

		/// <summary>
		/// Returns the number clamped to the specified minimum and maximum value.
		/// </summary>
		public static ushort Clamp(this ushort number, ushort minimum, ushort maximum)
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
		public static int DigitCount(this ushort number)
		{
			if (number != 0)
			{
				return ((int)Math.Log10(number)) + 1;
			}
			return 1;
		}

		/// <summary>
		/// Returns the specified digit of the number. Where zero is the least significant digit.
		/// </summary>
		public static ushort GetDigit(this ushort number, int digit)
		{
			const byte MIN_DIGITS = 0;
			const byte BASE_TEN = 10;

			digit = digit.Clamp(MIN_DIGITS, number.DigitCount());
			for (int i = MIN_DIGITS; i < digit; ++i)
			{
				number /= BASE_TEN;
			}
			return (ushort)(number % BASE_TEN);
		}
	}
}