using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Canvas type settings handler for Unity's Image component.
	/// </summary>
	public class ImageCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings from configuration to the Image component.
		/// </summary>
		/// <param name="component">The component to apply settings to (should be Image).</param>
		/// <param name="configuration">The configuration object containing UI settings.</param>
		public override void ApplySettings(object component, Configuration configuration)
		{
			// Attempt to cast the component to Image
			Image image = component as Image;
			if (image == null)
			{
				// If the cast fails, exit without applying settings
				return;
			}
			// If the image has a sprite, apply color logic based on name
			if (image.sprite != null)
			{
				// Skip cursor images
				if (image.name.Contains("Cursor"))
				{
					return;
				}
				// If the sprite or object is named "Background", use primary color
				else if (image.sprite.name.Equals("Background"))
				{
					image.color = ParseColor("Primary", configuration);
				}
				else if (image.name.Equals("Background"))
				{
					image.color = ParseColor("Primary", configuration);
				}
				// If the image is a crosshair, use crosshair color
				else if (image.name.Contains("Crosshair"))
				{
					image.color = ParseColor("Crosshair", configuration);
				}
				// If the image is part of UI, use primary color
				else if (image.name.Contains("UI"))
				{
					image.color = ParseColor("Primary", configuration);
				}
				else
				{
					// Make the sprite fully bright for all other cases
					image.color = Color.white;
				}
			}

			// Overrides for health, mana, and stamina images (unless part of UI)
			if (image.name.Contains("Health") &&
				!image.name.Contains("UI"))
			{
				image.color = ParseColor("Health", configuration);
			}
			else if (image.name.Contains("Mana") &&
				!image.name.Contains("UI"))
			{
				image.color = ParseColor("Mana", configuration);
			}
			else if (image.name.Contains("Stamina") &&
				!image.name.Contains("UI"))
			{
				image.color = ParseColor("Stamina", configuration);
			}
		}
	}
}