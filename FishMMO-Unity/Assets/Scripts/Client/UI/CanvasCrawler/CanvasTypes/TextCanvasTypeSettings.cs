using FishMMO.Shared;
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

			if (text.name.Contains("Placeholder"))
			{
				text.color = ParseColor("Primary", configuration);
			}
			else
			{
				text.color = ParseColor("Text", configuration);
			}
		}
	}
}