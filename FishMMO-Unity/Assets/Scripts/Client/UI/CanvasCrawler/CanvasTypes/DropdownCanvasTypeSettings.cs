using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Dropdown component.
	/// </summary>
	public class DropdownCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Dropdown component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Dropdown).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Dropdown
			Dropdown dropDown = component as Dropdown;
			if (dropDown == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// Parse colors from configuration
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			// Apply color settings to the dropdown's ColorBlock
			ColorBlock cb = dropDown.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			dropDown.colors = cb;
		}
	}
}