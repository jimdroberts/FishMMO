using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for objects that provide tooltip information for UI display.
	/// Provides icon, name, and formatted tooltip text.
	/// </summary>
	public interface ITooltip
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
		/// Returns the tooltip text for this object, typically including name, description, and stats.
		/// </summary>
		/// <returns>The tooltip text.</returns>
		string Tooltip();
	}
}