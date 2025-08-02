using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Slider component.
	/// </summary>
	public class SliderCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Slider component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Slider).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Slider
			Slider slider = component as Slider;
			if (slider == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}

			// Parse colors from configuration
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			// Apply color settings to the slider's ColorBlock
			ColorBlock cb = slider.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			slider.colors = cb;
		}
	}
}