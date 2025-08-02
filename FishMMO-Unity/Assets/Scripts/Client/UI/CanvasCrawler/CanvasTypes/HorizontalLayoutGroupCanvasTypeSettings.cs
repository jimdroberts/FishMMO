using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's HorizontalLayoutGroup component.
	/// </summary>
	public class HorizontalLayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies settings from configuration to the HorizontalLayoutGroup component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be HorizontalLayoutGroup).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to HorizontalLayoutGroup
			HorizontalLayoutGroup horizontalLayoutGroup = component as HorizontalLayoutGroup;
			if (horizontalLayoutGroup == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// No settings are currently applied for HorizontalLayoutGroup.
		}
	}
}