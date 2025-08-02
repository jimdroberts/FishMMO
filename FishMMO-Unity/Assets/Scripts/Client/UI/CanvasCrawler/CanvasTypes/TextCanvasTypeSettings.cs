using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Text component.
	/// </summary>
	public class TextCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Text component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Text).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Text
			Text text = component as Text;
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