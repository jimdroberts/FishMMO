namespace FishMMO.Shared
{
	/// <summary>
	/// Centralized color and formatting constants for tooltip rich text.
	/// All hex color values include alpha and are compatible with Unity's rich text system.
	/// </summary>
	public static class TooltipColors
	{
		/// <summary>
		/// Orange color for titles, names, and headers.
		/// </summary>
		public const string Title = "#f5ad6eFF";

		/// <summary>
		/// Purple color for labels, descriptions, and secondary text.
		/// </summary>
		public const string Label = "#a66ef5FF";

		/// <summary>
		/// Green color for attribute values and positive numbers.
		/// </summary>
		public const string Value = "#32a879FF";

		/// <summary>
		/// White color for stat values and general numeric display.
		/// </summary>
		public const string Stat = "#FFFFFFFF";
	}
}
