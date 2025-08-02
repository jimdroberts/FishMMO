using Cysharp.Text;

namespace FishMMO.Shared
{
	/// <summary>
	/// Utility class for formatting rich text strings with color, size, and optional prefixes/suffixes for UI display.
	/// </summary>
	public static class RichText
	{
		/// <summary>
		/// Formats a float value with optional name, color, size, prefix, and suffix for rich text display.
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
			if (value == 0.0f)
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (appendLine)
				{
					sb.AppendLine();
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("<size=" + size + ">");
				}
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("<color=#" + hexColor + ">");
				}
				if (!string.IsNullOrWhiteSpace(valueName))
				{
					sb.Append(valueName);
					sb.Append(": ");
				}
				if (!string.IsNullOrWhiteSpace(appendPrefix))
				{
					sb.Append(appendPrefix);
				}
				sb.Append(value);
				if (!string.IsNullOrWhiteSpace(appendSuffix))
				{
					sb.Append(appendSuffix);
				}
				if (!string.IsNullOrWhiteSpace(hexColor))
				{
					sb.Append("</color>");
				}
				if (!string.IsNullOrWhiteSpace(size))
				{
					sb.Append("</size>");
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Formats a string value with optional color, size, and line break for rich text display.
		/// </summary>
		/// <param name="value">The string value to display.</param>
		/// <param name="appendLine">If true, adds a line break before the value.</param>
		/// <param name="hexColor">Hex color code for the value text.</param>
		/// <param name="size">Font size for the value text.</param>
		/// <returns>Formatted rich text string.</returns>
		public static string Format(string value, bool appendLine = false, string hexColor = null, string size = null)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "";
			}

			using (var sb = ZString.CreateStringBuilder())
			{
				if (!string.IsNullOrWhiteSpace(value))
				{
					if (appendLine)
					{
						sb.AppendLine();
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("<size=" + size + ">");
					}
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("<color=#" + hexColor + ">");
					}
					sb.Append(value);
					if (!string.IsNullOrWhiteSpace(hexColor))
					{
						sb.Append("</color>");
					}
					if (!string.IsNullOrWhiteSpace(size))
					{
						sb.Append("</size>");
					}
				}
				return sb.ToString();
			}
		}
	}
}