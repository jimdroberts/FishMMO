using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Toggle component.
	/// </summary>
	public class ToggleCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Toggle component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Toggle).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Toggle
			Toggle toggle = component as Toggle;
			if (toggle == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// Parse colors from configuration
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			// Apply color settings to the toggle's ColorBlock
			ColorBlock cb = toggle.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			toggle.colors = cb;
		}
	}
}