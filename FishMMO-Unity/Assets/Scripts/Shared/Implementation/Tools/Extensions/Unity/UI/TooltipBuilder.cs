using Cysharp.Text;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single line of tooltip content with formatting and priority-based ordering.
	/// </summary>
	public struct TooltipLine : IComparable<TooltipLine>
	{
		/// <summary>
		/// The text content of this line. May contain rich text tags.
		/// </summary>
		public string Text;

		/// <summary>
		/// The sort priority for this line. Lower values appear first.
		/// </summary>
		public int Priority;

		/// <summary>
		/// The color to apply. Supports hex with # prefix (e.g., "#f5ad6eFF") or named colors (e.g., "white").
		/// Null or empty means no color wrapper is applied.
		/// </summary>
		public string Color;

		/// <summary>
		/// Whether this line should be bold.
		/// </summary>
		public bool IsBold;

		/// <summary>
		/// The font size for this line (e.g., "120%", "14"). Null or empty for default.
		/// </summary>
		public string FontSize;

		/// <summary>
		/// Sorts by priority (lowest first).
		/// </summary>
		public int CompareTo(TooltipLine other) => Priority.CompareTo(other.Priority);
	}

	/// <summary>
	/// Builder pattern class for constructing formatted tooltip strings.
	/// Supports priority-based line ordering, rich text colors, bold, and font sizes.
	/// Uses ZString for zero-allocation string building.
	/// </summary>
	public class TooltipBuilder : IDisposable
	{
		private readonly List<TooltipLine> lines = new List<TooltipLine>();

		/// <summary>
		/// Adds a formatted line to the tooltip.
		/// </summary>
		/// <param name="text">The text content. May contain rich text tags.</param>
		/// <param name="priority">Sort priority (lower = earlier). Default is 100.</param>
		/// <param name="color">Color string: hex with # (e.g., "#f5ad6eFF") or named (e.g., "white"). Null for no color.</param>
		/// <param name="bold">Whether to render in bold.</param>
		/// <param name="fontSize">Font size string (e.g., "120%", "14"). Null for default size.</param>
		/// <returns>This builder for fluent chaining.</returns>
		public TooltipBuilder AddLine(string text, int priority = 100, string color = null, bool bold = false, string fontSize = null)
		{
			lines.Add(new TooltipLine
			{
				Text = text,
				Priority = priority,
				Color = color,
				IsBold = bold,
				FontSize = fontSize,
			});
			return this;
		}

		/// <summary>
		/// Adds a separator line to the tooltip.
		/// </summary>
		/// <param name="priority">Sort priority for the separator.</param>
		/// <returns>This builder for fluent chaining.</returns>
		public TooltipBuilder AddSeparator(int priority = 50)
		{
			lines.Add(new TooltipLine
			{
				Text = "______________________________",
				Priority = priority,
			});
			return this;
		}

		/// <summary>
		/// Sorts all lines by priority and builds the final formatted rich text string.
		/// </summary>
		/// <returns>The complete tooltip string with rich text formatting.</returns>
		public string Build()
		{
			lines.Sort();

			using (var sb = ZString.CreateStringBuilder())
			{
				for (int i = 0; i < lines.Count; i++)
				{
					TooltipLine line = lines[i];
					if (i > 0) sb.AppendLine();

					bool hasSize = !string.IsNullOrEmpty(line.FontSize);
					bool hasColor = !string.IsNullOrEmpty(line.Color);

					if (hasSize)
					{
						sb.Append("<size=");
						sb.Append(line.FontSize);
						sb.Append('>');
					}
					if (line.IsBold) sb.Append("<b>");
					if (hasColor)
					{
						sb.Append("<color=");
						sb.Append(line.Color);
						sb.Append('>');
					}

					sb.Append(line.Text);

					if (hasColor) sb.Append("</color>");
					if (line.IsBold) sb.Append("</b>");
					if (hasSize) sb.Append("</size>");
				}
				return sb.ToString();
			}
		}

		/// <summary>
		/// Clears all lines from the builder for reuse.
		/// </summary>
		public void Clear() => lines.Clear();

		/// <summary>
		/// Disposes the builder by clearing all lines.
		/// </summary>
		public void Dispose() => lines.Clear();
	}
}
