using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's VerticalLayoutGroup component.
	/// </summary>
	public class VerticalLayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies settings from configuration to the VerticalLayoutGroup component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be VerticalLayoutGroup).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to VerticalLayoutGroup
			VerticalLayoutGroup verticalLayoutGroup = component as VerticalLayoutGroup;
			if (verticalLayoutGroup == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// No settings are currently applied for VerticalLayoutGroup.
		}
	}
}