using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's RawImage component.
	/// </summary>
	public class RawImageCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies settings from configuration to the RawImage component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be RawImage).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to RawImage
			RawImage rawImage = component as RawImage;
			if (rawImage == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// No settings are currently applied for RawImage.
		}
	}
}