using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies text color and font size settings to any text component (Text, TMP_Text).
	/// Placeholder-named objects receive the primary color; all others receive the text color.
	/// TMP_Text components also receive configurable font size limits when auto-sizing is enabled.
	/// </summary>
	public sealed class TextGraphicCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies text color and optional font size settings.
		/// </summary>
		/// <param name="component">The UI component (must be a Graphic).</param>
		/// <param name="theme">The pre-parsed UI theme.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not Graphic graphic) return;

			graphic.color = graphic.name.Contains("Placeholder", StringComparison.Ordinal)
				? theme.Primary
				: theme.Text;

			if (component is TMP_Text tmpText)
			{
				if (theme.FontSize > 0f)
				{
					tmpText.fontSize = theme.FontSize;
				}

				if (tmpText.enableAutoSizing && theme.FontSizeMin > 0f && theme.FontSizeMax > 0f)
				{
					tmpText.fontSizeMin = theme.FontSizeMin;
					tmpText.fontSizeMax = theme.FontSizeMax;
				}
			}
		}
	}
}