using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Utility class for formatting rich text strings with color, size, and optional prefixes/suffixes for UI display.
	/// Provides both string-returning Format methods and zero-allocation AppendTo methods for StringBuilder use.
	/// </summary>
	public static class RichText
	{
		/// <summary>
		/// Formats a float value with optional name, color, size, prefix, and suffix for rich text display.
		/// Returns an empty string if value is zero.
		/// </summary>
		/// <param name="valueName">Label for the value (e.g., "Health").</param>
		/// <param name="value">The float value to display.</param>
		/// <param name="appendLine">If true, adds a line break before the value.</param>
		/// <param name="hexColor">Hex color code for the value text.</param>
		/// <param name="appendPrefix">Optional prefix to add before the value.</param>
		/// <param name="appendSuffix">Optional suffix to add after the value.</param>
		/// <param name="size">Font size for the value text.</param>
		/// <returns>Formatted rich text string.</returns>
		public static string Format(string valueName, float value, bool appendLine = false, string hexColor = null, string appendPrefix = null, string appendSuffix = null, string size = null)
		{
			if (value == 0.0f) return "";

			using (var sb = ZString.CreateStringBuilder())
			{
				AppendTo(ref sb, valueName, value, appendLine, hexColor, appendPrefix, appendSuffix, size);
				return sb.ToString();
			}
		}

		/// <summary>
		/// Formats a string value with optional color, size, and line break for rich text display.
		/// Returns an empty string if value is null or whitespace.
		/// </summary>
		/// <param name="value">The string value to display.</param>
		/// <param name="appendLine">If true, adds a line break before the value.</param>
		/// <param name="hexColor">Hex color code for the value text.</param>
		/// <param name="size">Font size for the value text.</param>
		/// <returns>Formatted rich text string.</returns>
		public static string Format(string value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (string.IsNullOrWhiteSpace(value)) return "";

			using (var sb = ZString.CreateStringBuilder())
			{
				AppendTo(ref sb, value, appendLine, hexColor, size);
				return sb.ToString();
			}
		}

		/// <summary>
		/// Appends a formatted float value with optional name, color, size, prefix, and suffix directly to an existing string builder.
		/// Avoids intermediate string allocations when building composite tooltips.
		/// </summary>
		/// <param name="sb">The string builder to append to.</param>
		/// <param name="valueName">Label for the value (e.g., "Health").</param>
		/// <param name="value">The float value to display.</param>
		/// <param name="appendLine">If true, adds a line break before the value.</param>
		/// <param name="hexColor">Hex color code for the value text.</param>
		/// <param name="appendPrefix">Optional prefix to add before the value.</param>
		/// <param name="appendSuffix">Optional suffix to add after the value.</param>
		/// <param name="size">Font size for the value text.</param>
		public static void AppendTo(ref Utf16ValueStringBuilder sb, string valueName, float value, bool appendLine = false, string hexColor = null, string appendPrefix = null, string appendSuffix = null, string size = null)
		{
			if (value == 0.0f) return;

			if (appendLine) sb.AppendLine();
			if (!string.IsNullOrWhiteSpace(size))
			{
				sb.Append("<size=");
				sb.Append(size);
				sb.Append('>');
			}
			if (!string.IsNullOrWhiteSpace(hexColor))
			{
				sb.Append("<color=");
				sb.Append(hexColor);
				sb.Append('>');
			}
			if (!string.IsNullOrWhiteSpace(valueName))
			{
				sb.Append(valueName);
				sb.Append(": ");
			}
			if (!string.IsNullOrWhiteSpace(appendPrefix)) sb.Append(appendPrefix);
			sb.Append(value);
			if (!string.IsNullOrWhiteSpace(appendSuffix)) sb.Append(appendSuffix);
			if (!string.IsNullOrWhiteSpace(hexColor)) sb.Append("</color>");
			if (!string.IsNullOrWhiteSpace(size)) sb.Append("</size>");
		}

		/// <summary>
		/// Appends a formatted string value with optional color, size, and line break directly to an existing string builder.
		/// Avoids intermediate string allocations when building composite tooltips.
		/// </summary>
		/// <param name="sb">The string builder to append to.</param>
		/// <param name="value">The string value to display.</param>
		/// <param name="appendLine">If true, adds a line break before the value.</param>
		/// <param name="hexColor">Hex color code for the value text.</param>
		/// <param name="size">Font size for the value text.</param>
		public static void AppendTo(ref Utf16ValueStringBuilder sb, string value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (string.IsNullOrWhiteSpace(value)) return;

			if (appendLine) sb.AppendLine();
			if (!string.IsNullOrWhiteSpace(size))
			{
				sb.Append("<size=");
				sb.Append(size);
				sb.Append('>');
			}
			if (!string.IsNullOrWhiteSpace(hexColor))
			{
				sb.Append("<color=");
				sb.Append(hexColor);
				sb.Append('>');
			}
			sb.Append(value);
			if (!string.IsNullOrWhiteSpace(hexColor)) sb.Append("</color>");
			if (!string.IsNullOrWhiteSpace(size)) sb.Append("</size>");
		}
	}
}