using System;
using System.Collections.Generic;

namespace FishMMO.Logging
{
	/// <summary>
	/// Provides shared helper methods and constants for console formatting,
	/// used by both ConsoleFormatter and UnityConsoleFormatter to ensure consistent output.
	/// </summary>
	public static class ConsoleFormatterHelpers
	{
		// Define fixed widths for console columns
		public const int TimestampColumnWidth = 12; // [yyyy-MM-dd HH:mm:ss 'UTC']
		public const int LogLevelColumnWidth = 12;  // [LEVEL] - e.g., "CRITICAL "
		public const int SourceColumnWidth = 18;    // [Source] - e.g., "HealthMonitor-App1  "

		// Console Color Mapping
		public static readonly Dictionary<ConsoleColor, (byte r, byte g, byte b)> ConsoleColorRgbMap = new Dictionary<ConsoleColor, (byte r, byte g, byte b)>
		{
			{ ConsoleColor.Black, (0, 0, 0) },
			{ ConsoleColor.DarkBlue, (0, 0, 128) },
			{ ConsoleColor.DarkGreen, (0, 128, 0) },
			{ ConsoleColor.DarkCyan, (0, 128, 128) },
			{ ConsoleColor.DarkRed, (128, 0, 0) },
			{ ConsoleColor.DarkMagenta, (128, 0, 128) },
			{ ConsoleColor.DarkYellow, (128, 128, 0) }, // Often brown/orange
            { ConsoleColor.Gray, (192, 192, 192) },
			{ ConsoleColor.DarkGray, (128, 128, 128) },
			{ ConsoleColor.Blue, (0, 0, 255) },
			{ ConsoleColor.Green, (0, 255, 0) },
			{ ConsoleColor.Cyan, (0, 255, 255) },
			{ ConsoleColor.Red, (255, 0, 0) },
			{ ConsoleColor.Magenta, (255, 0, 255) },
			{ ConsoleColor.Yellow, (255, 255, 0) },
			{ ConsoleColor.White, (255, 255, 255) }
		};

		/// <summary>
		/// Escapes characters in a string that might be interpreted as Unity rich text tags.
		/// This prevents malformed tags from breaking the console output.
		/// This method is specific to Unity's rich text, but is placed here for shared access.
		/// </summary>
		/// <param name="text">The text to escape.</param>
		/// <returns>The escaped text.</returns>
		public static string EscapeUnityRichText(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}
			// Unity's rich text doesn't support HTML entities like &#60; or &#62;.
			// A common workaround is to replace the angle brackets with something that won't be parsed as a tag.
			// This simple replacement prevents tag interpretation.
			return text.Replace("<", "<_").Replace(">", "_>");
		}

		/// <summary>
		/// Pads a string to a specified total width.
		/// </summary>
		/// <param name="text">The string to pad.</param>
		/// <param name="totalWidth">The total width of the resulting string.</param>
		/// <returns>The padded string.</returns>
		public static string PadRight(string text, int totalWidth)
		{
			if (text.Length >= totalWidth)
			{
				return text;
			}
			return text.PadRight(totalWidth);
		}

		/// <summary>
		/// Gets a specific color for the log source (e.g., Daemon, Orchestration, App Name)
		/// or applies a default color based on the LogLevel if no specific source matches.
		/// </summary>
		public static ConsoleColor GetSourceColor(string source, LogLevel level)
		{
			// Prioritize specific source colors first
			if (source.StartsWith("Daemon"))
			{
				return ConsoleColor.DarkGreen;
			}
			else if (source.StartsWith("Orchestration"))
			{
				return ConsoleColor.Blue;
			}
			else if (source.StartsWith("HealthMonitor"))
			{
				return ConsoleColor.Magenta;
			}

			// If no specific source color, apply default colors based on log level.
			// Using a switch statement for clarity and explicit handling of each level.
			switch (level)
			{
				case LogLevel.Error:
					return ConsoleColor.Red; // Critical errors
				case LogLevel.Warning:
					return ConsoleColor.Yellow; // Warnings
				case LogLevel.Info:
					return ConsoleColor.Gray; // General information
				case LogLevel.Debug:
					return ConsoleColor.DarkCyan; // Debugging details
				case LogLevel.Verbose:
					return ConsoleColor.DarkGray; // Very fine-grained details
				case LogLevel.None:
				default:
					return ConsoleColor.White; // Default for unexpected or 'None' level
			}
		}

		/// <summary>
		/// Converts a hex color string or named color to the closest System.ConsoleColor.
		/// </summary>
		public static System.ConsoleColor GetClosestConsoleColor(string color)
		{
			// Try to parse hex color
			if (color.StartsWith("#") && color.Length >= 7)
			{
				if (byte.TryParse(color.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
					byte.TryParse(color.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
					byte.TryParse(color.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
				{
					return FindClosestConsoleColor((r, g, b));
				}
			}

			// Fallback to named colors if hex parsing fails or if it's a named color
			switch (color.ToLower())
			{
				case "red": return System.ConsoleColor.Red;
				case "green": return System.ConsoleColor.Green;
				case "blue": return System.ConsoleColor.Blue;
				case "yellow": return System.ConsoleColor.Yellow;
				case "cyan": return System.ConsoleColor.Cyan;
				case "magenta": return System.ConsoleColor.Magenta;
				case "white": return System.ConsoleColor.White;
				case "black": return System.ConsoleColor.Black;
				case "gray": return System.ConsoleColor.Gray;
				case "grey": return System.ConsoleColor.Gray;
				case "darkred": return System.ConsoleColor.DarkRed;
				case "darkgreen": return System.ConsoleColor.DarkGreen;
				case "darkblue": return System.ConsoleColor.DarkBlue;
				case "darkyellow": return System.ConsoleColor.DarkYellow;
				case "darkcyan": return System.ConsoleColor.DarkCyan;
				case "darkmagenta": return System.ConsoleColor.DarkMagenta;
				case "orange": return System.ConsoleColor.DarkYellow;
				case "purple": return System.ConsoleColor.DarkMagenta;
				case "lightblue": return System.ConsoleColor.Cyan;
				case "#add8e6": return System.ConsoleColor.Cyan;
				case "#8b4513": return System.ConsoleColor.DarkYellow;
				default: return System.ConsoleColor.White;
			}
		}

		/// <summary>
		/// Finds the closest System.ConsoleColor to a given RGB tuple using Euclidean distance.
		/// </summary>
		public static System.ConsoleColor FindClosestConsoleColor((byte r, byte g, byte b) targetRgb)
		{
			double minDistance = double.MaxValue;
			System.ConsoleColor closestColor = System.ConsoleColor.White;

			foreach (var entry in ConsoleColorRgbMap) // Use the public static map
			{
				double dr = entry.Value.r - targetRgb.r;
				double dg = entry.Value.g - targetRgb.g;
				double db = entry.Value.b - targetRgb.b;
				double distance = (dr * dr) + (dg * dg) + (db * db);

				if (distance < minDistance)
				{
					minDistance = distance;
					closestColor = entry.Key;
				}
			}
			return closestColor;
		}
	}
}