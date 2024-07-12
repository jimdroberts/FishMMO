using FishMMO.Shared;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class VerticalLayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			VerticalLayoutGroup verticalLayoutGroup = component as VerticalLayoutGroup;
			if (verticalLayoutGroup == null)
			{
				return;
			}
		}
	}
}