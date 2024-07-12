using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class ButtonCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			Button button = component as Button;
			if (button == null)
			{
				return;
			}
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			ColorBlock cb = button.colors;
			cb.normalColor = primaryColor;
			cb.pressedColor = secondaryColor;
			cb.highlightedColor = highlightColor;
			button.colors = cb;
		}
	}
}