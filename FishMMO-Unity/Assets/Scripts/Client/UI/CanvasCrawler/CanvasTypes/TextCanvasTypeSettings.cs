using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class TextCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			Text text = component as Text;
			if (text == null)
			{
				return;
			}

			text.color = ParseColor("Highlight", configuration);
		}
	}
}