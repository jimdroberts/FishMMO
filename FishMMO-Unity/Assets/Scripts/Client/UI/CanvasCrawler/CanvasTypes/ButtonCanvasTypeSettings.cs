using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Button component.
	/// </summary>
	public class ButtonCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Button component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Button).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Button
			Button button = component as Button;
			if (button == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// Parse colors from configuration
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			// Apply color settings to the button's ColorBlock
			ColorBlock cb = button.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			cb.selectedColor = secondaryColor;
			button.colors = cb;
		}
	}
}