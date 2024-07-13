using FishMMO.Shared;
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