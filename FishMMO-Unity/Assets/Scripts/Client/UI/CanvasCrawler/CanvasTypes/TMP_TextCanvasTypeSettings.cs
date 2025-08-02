using FishMMO.Shared;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for TextMeshPro's TMP_Text component.
	/// </summary>
	public class TMP_TextCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the TMP_Text component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be TMP_Text).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to TMP_Text
			TMP_Text text = component as TMP_Text;
			if (text == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}

			// If the text object is a placeholder, use the primary color; otherwise, use the text color
			if (text.name.Contains("Placeholder"))
			{
				text.color = ParseColor("Primary", configuration);
			}
			else
			{
				text.color = ParseColor("Text", configuration);
			}
		}
	}
}