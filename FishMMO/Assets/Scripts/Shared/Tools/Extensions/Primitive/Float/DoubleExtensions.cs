using System;

public static class DoubleExtensions
{
	/// <summary>
	/// Returns the absolute value of the number.
	/// </summary>
	public static double Absolute(this double number)
	{
		return (number < 0) ? -number : number;
	}

	/// <summary>
	/// Returns the number clamped to the specified minimum and maximum value.
	/// </summary>
	public static double Clamp(this double number, double minimum, double maximum)
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
	public static int DigitCount(this double number)
	{
		if (number != 0)
		{
			return ((int)Math.Log10(number.Absolute())) + 1;
		}
		return 1;
	}
}