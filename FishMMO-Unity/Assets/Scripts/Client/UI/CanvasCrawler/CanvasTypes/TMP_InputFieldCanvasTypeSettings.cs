using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for TextMeshPro's TMP_InputField component.
	/// </summary>
	public class TMP_InputFieldCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the TMP_InputField component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be TMP_InputField).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to TMP_InputField
			TMP_InputField inputField = component as TMP_InputField;
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
			cb.selectedColor = secondaryColor;
			inputField.colors = cb;
		}
	}
}