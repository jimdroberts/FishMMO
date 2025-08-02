using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's InputField component.
	/// </summary>
	public class InputFieldCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the InputField component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be InputField).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to InputField
			InputField inputField = component as InputField;
			if (inputField == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// Parse colors from configuration
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			// Apply color settings to the input field's ColorBlock
			ColorBlock cb = inputField.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			inputField.colors = cb;
		}
	}
}