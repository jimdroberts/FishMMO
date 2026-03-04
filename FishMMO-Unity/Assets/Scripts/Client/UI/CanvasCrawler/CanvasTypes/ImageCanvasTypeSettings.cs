using System;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies themed color settings to Image components based on naming conventions.
	/// Supports special handling for Background, Crosshair, UI, Health, Mana, and Stamina images.
	/// </summary>
	public sealed class ImageCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies color settings to the Image component.
		/// Images with sprites are colored by name convention; resource bar images override with their specific colors.
		/// </summary>
		/// <param name="component">The UI component (must be an Image).</param>
		/// <param name="theme">The pre-parsed UI theme.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not Image image) return;

			if (image.sprite != null)
			{
				if (image.name.Contains("Cursor", StringComparison.Ordinal))
				{
					return;
				}

				if (image.sprite.name.Equals("Background", StringComparison.Ordinal) ||
					image.name.Equals("Background", StringComparison.Ordinal))
				{
					image.color = theme.Primary;
				}
				else if (image.name.Contains("Crosshair", StringComparison.Ordinal))
				{
					image.color = theme.Crosshair;
				}
				else if (image.name.Contains("UI", StringComparison.Ordinal))
				{
					image.color = theme.Primary;
				}
				else
				{
					image.color = Color.white;
				}
			}

			if (image.name.Contains("UI", StringComparison.Ordinal))
			{
				return;
			}

			if (image.name.Contains("Health", StringComparison.Ordinal))
			{
				image.color = theme.Health;
			}
			else if (image.name.Contains("Mana", StringComparison.Ordinal))
			{
				image.color = theme.Mana;
			}
			else if (image.name.Contains("Stamina", StringComparison.Ordinal))
			{
				image.color = theme.Stamina;
			}
		}
	}
}