using FishMMO.Shared;
using UnityEngine;
using TMPro;

namespace FishMMO.Client
{
	public class TMP_TextCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			TMP_Text text = component as TMP_Text;
			if (text == null)
			{
				return;
			}
			Color primaryColor = ParseColor("Primary", configuration);
			Color secondaryColor = ParseColor("Secondary", configuration);
			Color highlightColor = ParseColor("Highlight", configuration);

			text.color = highlightColor;
		}
	}
}