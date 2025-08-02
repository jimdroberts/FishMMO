using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for objects that provide tooltip information for UI display.
	/// Includes icon, name, description, and formatted tooltip text.
	/// </summary>
	public interface ITooltip : ICachedObject
	{
		/// <summary>
		/// Gets the icon sprite to display in the tooltip.
		/// </summary>
		Sprite Icon { get; }

		/// <summary>
		/// Gets the display name for the tooltip.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Returns a formatted description string for the tooltip, including rich text or color formatting.
		/// </summary>
		/// <returns>The formatted description string.</returns>
		string GetFormattedDescription();

		/// <summary>
		/// Returns the tooltip text for this object, typically including name, description, and stats.
		/// </summary>
		/// <returns>The tooltip text.</returns>
		string Tooltip();

		/// <summary>
		/// Returns a combined tooltip text for this object and a list of other tooltips, used for comparison or aggregation.
		/// </summary>
		/// <param name="combineList">A list of other ITooltip objects to combine with this tooltip.</param>
		/// <returns>The combined tooltip text.</returns>
		string Tooltip(List<ITooltip> combineList);
	}
}