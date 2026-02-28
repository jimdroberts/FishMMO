using System.Runtime.CompilerServices;

namespace FishMMO.Shared
{
	/// <summary>
	/// Provides deterministic unsigned 64-bit integer utility methods for the FishMMO framework.
	/// All calculations use pure integer math to ensure cross-platform consistency.
	/// </summary>
	public static class ULongExtensions
	{
		/// <summary>
		/// Returns the absolute difference between two unsigned 64-bit values.
		/// Prevents underflow by checking which value is larger before subtraction.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong AbsoluteSubtract(this ulong number, ulong other)
		{
			return (number > other) ? (number - other) : (other - number);
		}

		/// <summary>
		/// Returns the value clamped to the specified inclusive minimum and maximum range.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong Clamp(this ulong number, ulong minimum, ulong maximum)
		{
			if (number < minimum) return minimum;
			if (number > maximum) return maximum;
			return number;
		}

		/// <summary>
		/// Returns the total count of digits in the unsigned 64-bit value using deterministic range checks.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int DigitCount(this ulong number)
		{
			if (number < 10UL) return 1;
			if (number < 100UL) return 2;
			if (number < 1000UL) return 3;
			if (number < 10000UL) return 4;
			if (number < 100000UL) return 5;
			if (number < 1000000UL) return 6;
			if (number < 10000000UL) return 7;
			if (number < 100000000UL) return 8;
			if (number < 1000000000UL) return 9;
			if (number < 10000000000UL) return 10;
			if (number < 100000000000UL) return 11;
			if (number < 1000000000000UL) return 12;
			if (number < 10000000000000UL) return 13;
			if (number < 100000000000000UL) return 14;
			if (number < 1000000000000000UL) return 15;
			if (number < 10000000000000000UL) return 16;
			if (number < 100000000000000000UL) return 17;
			if (number < 1000000000000000000UL) return 18;
			if (number < 10000000000000000000UL) return 19;
			return 20;
		}

		/// <summary>
		/// Returns the specific digit at the given index, where 0 is the least significant digit (ones place).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong GetDigit(this ulong number, int digitIndex)
		{
			if (digitIndex < 0) return 0;

			// Loop until we reach the desired digit place
			for (int i = 0; i < digitIndex; i++)
			{
				number /= 10UL;
				if (number == 0) return 0; // Optimization: exit early if number is exhausted
			}
			return number % 10UL;
		}

		/// <summary>
		/// Calculates a percentage of an unsigned 64-bit number using integer math with rounding.
		/// Formula: (number * percent + 50) / 100
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static ulong GetPercentOf(this ulong number, ulong percent)
		{
			// Note: If (number * percent) exceeds ulong.MaxValue, this will wrap. 
			// In an MMO, usually ulong represents XP or Currency where such massive numbers 
			// multiplied by percent are rare, but be aware of the limit.
			return (number * percent + 50UL) / 100UL;
		}
	}
}