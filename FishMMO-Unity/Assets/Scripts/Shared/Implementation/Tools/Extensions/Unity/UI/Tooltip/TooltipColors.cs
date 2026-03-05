namespace FishMMO.Shared
{
	/// <summary>
	/// Centralized color strings for tooltip rich text.
	/// Colors are runtime-configurable via <see cref="Initialize"/> and default to sensible fallback values.
	/// All hex color values include # prefix and alpha, compatible with Unity's rich text system.
	/// </summary>
	public static class TooltipColors
	{
		/// <summary>Default fallback: orange (#f5ad6eFF).</summary>
		public const string DEFAULT_TITLE = "#f5ad6eFF";

		/// <summary>Default fallback: purple (#a66ef5FF).</summary>
		public const string DEFAULT_LABEL = "#a66ef5FF";

		/// <summary>Default fallback: green (#32a879FF).</summary>
		public const string DEFAULT_VALUE = "#32a879FF";

		/// <summary>Default fallback: white (#FFFFFFFF).</summary>
		public const string DEFAULT_STAT = "#FFFFFFFF";

		private static string title = DEFAULT_TITLE;
		private static string label = DEFAULT_LABEL;
		private static string value = DEFAULT_VALUE;
		private static string stat = DEFAULT_STAT;

		/// <summary>
		/// Orange color for titles, names, and headers.
		/// </summary>
		public static string Title => title;

		/// <summary>
		/// Purple color for labels, descriptions, and secondary text.
		/// </summary>
		public static string Label => label;

		/// <summary>
		/// Green color for attribute values and positive numbers.
		/// </summary>
		public static string Value => value;

		/// <summary>
		/// White color for stat values and general numeric display.
		/// </summary>
		public static string Stat => stat;

		/// <summary>
		/// Initializes tooltip colors from hex strings (e.g., "#RRGGBBAA").
		/// Call after loading the UI theme. Null or empty values keep the current color.
		/// </summary>
		public static void Initialize(string titleColor, string labelColor, string valueColor, string statColor)
		{
			if (!string.IsNullOrEmpty(titleColor)) title = titleColor;
			if (!string.IsNullOrEmpty(labelColor)) label = labelColor;
			if (!string.IsNullOrEmpty(valueColor)) value = valueColor;
			if (!string.IsNullOrEmpty(statColor)) stat = statColor;
		}
	}
}