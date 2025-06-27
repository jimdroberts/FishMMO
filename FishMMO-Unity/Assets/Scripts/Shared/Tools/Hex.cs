using System.Runtime.CompilerServices;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A static utility class for converting between hexadecimal color strings and <see cref="UnityEngine.Color"/> objects,
	/// and for normalizing color component values.
	/// </summary>
	public static class Hex
	{
		/// <summary>
		/// Normalizes the RGBA components of a <see cref="UnityEngine.Color"/> struct.
		/// If any component (R, G, B, or A) is greater than 255, all components are scaled down
		/// such that the largest component becomes 255, and then divided by 255.0f to fit the 0-1 range expected by <see cref="UnityEngine.Color"/>.
		/// This is useful when converting 0-255 based integer color components to Unity's 0-1 float color space,
		/// especially if values might exceed 255 due to bitwise operations or other calculations.
		/// </summary>
		/// <param name="color">The color to normalize.</param>
		/// <returns>A new <see cref="UnityEngine.Color"/> with components normalized to the 0-1 range.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color ColorNormalize(Color color)
		{
			return ColorNormalize(color.r, color.g, color.b, color.a);
		}

		/// <summary>
		/// Normalizes the RGB components of a color, assuming an alpha of 1.0f (255).
		/// See <see cref="ColorNormalize(Color)"/> for normalization details.
		/// </summary>
		/// <param name="r">The red component (expected range 0-255 or greater).</param>
		/// <param name="g">The green component (expected range 0-255 or greater).</param>
		/// <param name="b">The blue component (expected range 0-255 or greater).</param>
		/// <returns>A new <see cref="UnityEngine.Color"/> with components normalized to the 0-1 range.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color ColorNormalize(float r, float g, float b)
		{
			return ColorNormalize(r, g, b, 255.0f); // Default alpha to 255 for normalization
		}

		/// <summary>
		/// Normalizes the RGBA components of a color.
		/// If any component (R, G, B, or A) is greater than 255, all components are scaled down
		/// such that the largest component becomes 255, and then divided by 255.0f to fit the 0-1 range expected by <see cref="UnityEngine.Color"/>.
		/// This is useful when converting 0-255 based integer color components to Unity's 0-1 float color space,
		/// especially if values might exceed 255 due to bitwise operations or other calculations.
		/// </summary>
		/// <param name="r">The red component (expected range 0-255 or greater).</param>
		/// <param name="g">The green component (expected range 0-255 or greater).</param>
		/// <param name="b">The blue component (expected range 0-255 or greater).</param>
		/// <param name="a">The alpha component (expected range 0-255 or greater).</param>
		/// <returns>A new <see cref="UnityEngine.Color"/> with components normalized to the 0-1 range.</returns>
		public static Color ColorNormalize(float r, float g, float b, float a)
		{
			float maxExpectedValue = 255.0f; // Standard maximum for 8-bit color components.
			float currentMax = Mathf.Max(r, Mathf.Max(g, Mathf.Max(b, a))); // Find the largest current component value.

			// If any component exceeds the standard 255, scale all components relative to the largest value.
			// This ensures that the brightest component becomes 255, preserving relative brightness.
			if (currentMax > maxExpectedValue)
			{
				maxExpectedValue = currentMax;
			}

			// Normalize each component by dividing by the effective maximum value (either 255.0f or currentMax).
			// Small values are explicitly set to 0.0f to avoid tiny floating point artifacts.
			r = (r < float.Epsilon) ? 0.0f : r / maxExpectedValue;
			g = (g < float.Epsilon) ? 0.0f : g / maxExpectedValue;
			b = (b < float.Epsilon) ? 0.0f : b / maxExpectedValue;
			a = (a < float.Epsilon) ? 0.0f : a / maxExpectedValue;

			return new Color(r, g, b, a);
		}

		/// <summary>
		/// Converts a hexadecimal string representation to an integer.
		/// For example, "FF" converts to 255, "0A" converts to 10.
		/// The parsing is invariant culture-specific and case-insensitive.
		/// If parsing fails (e.g., invalid hex characters), it returns 0.
		/// </summary>
		/// <param name="value">The hexadecimal string to convert.</param>
		/// <returns>The integer representation of the hex string, or 0 if parsing fails.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int ToInt(string value)
		{
			int result;
			// Uses NumberStyles.HexNumber for hex string parsing.
			// CultureInfo.InvariantCulture ensures consistent parsing regardless of system locale.
			if (int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
			{
				return result;
			}
			return 0; // Return 0 on failed parse.
		}

		/// <summary>
		/// Converts an integer value to its hexadecimal string representation.
		/// For example, 255 converts to "FF", 10 converts to "A".
		/// The "X" format specifier ensures uppercase hexadecimal output.
		/// </summary>
		/// <param name="value">The integer value to convert.</param>
		/// <returns>The hexadecimal string representation of the integer.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string ToHex(this int value)
		{
			return value.ToString("X"); // "X" format specifier converts to uppercase hex.
		}

		/// <summary>
		/// Converts a hexadecimal color string (e.g., "RRGGBB" or "RRGGBBAA") to a <see cref="UnityEngine.Color"/> object.
		/// If the string is shorter than 6 characters (e.g., "RRGGBB" format), it returns <see cref="Color.white"/>.
		/// If the string is 6 characters, it's parsed as "RRGGBB" with full opacity (alpha 255).
		/// If the string is 8 characters, it's parsed as "RRGGBBAA".
		/// Color components are normalized from 0-255 to 0-1 range using <see cref="ColorNormalize(float, float, float, float)"/>.
		/// </summary>
		/// <param name="s">The hexadecimal color string (e.g., "FF0000" for red, "00FF00FF" for green with full alpha).</param>
		/// <returns>A <see cref="UnityEngine.Color"/> object parsed from the hex string, or <see cref="Color.white"/> on invalid input length.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static Color ToColor(string s)
		{
			// Basic validation for string length. Hex color strings are typically 6 (RGB) or 8 (RGBA) characters long.
			if (s.Length < 6)
			{
				// Returning Color.white as a default for invalid input is a common fallback.
				return Color.white;
			}

			// Extract and convert red, green, and blue components.
			// Each component is 2 characters long (e.g., "FF").
			float r = ToInt(s.Substring(0, 2));
			float g = ToInt(s.Substring(2, 2));
			float b = ToInt(s.Substring(4, 2));

			// Check if an alpha component is present (8 characters total).
			if (s.Length < 8)
			{
				// If no alpha, assume full opacity (255).
				return ColorNormalize(r, g, b, 255.0f);
			}

			// Extract and convert the alpha component.
			float a = ToInt(s.Substring(6, 2));

			// Normalize all components to the 0-1 range for UnityEngine.Color.
			return ColorNormalize(r, g, b, a);
		}

		/// <summary>
		/// Converts a <see cref="UnityEngine.Color"/> object to its 8-character hexadecimal string representation (RRGGBBAA).
		/// Each color component (R, G, B, A) is scaled from its 0-1 float range to 0-255 integer,
		/// then formatted as a two-digit uppercase hexadecimal number.
		/// </summary>
		/// <param name="color">The <see cref="UnityEngine.Color"/> object to convert.</param>
		/// <returns>An 8-character hexadecimal string representing the color (e.g., "FF0000FF" for red).</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static string ToHex(this Color color)
		{
			// Scale color components from 0-1 float range to 0-255 integer range.
			int r = (int)(color.r * 255.0f);
			int g = (int)(color.g * 255.0f);
			int b = (int)(color.b * 255.0f);
			int a = (int)(color.a * 255.0f);

			// Format each integer as a two-digit uppercase hexadecimal string (X2).
			// Example: 255 -> "FF", 0 -> "00", 10 -> "0A"
			return string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", r, g, b, a);
		}
	}
}