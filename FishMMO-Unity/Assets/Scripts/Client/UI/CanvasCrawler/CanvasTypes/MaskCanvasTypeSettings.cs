using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Mask component.
	/// </summary>
	public class MaskCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies settings from configuration to the Mask component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Mask).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Mask
			Mask mask = component as Mask;
			if (mask == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// No settings are currently applied for Mask.
		}
	}
}