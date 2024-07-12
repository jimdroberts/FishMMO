using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class HorizontalLayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			HorizontalLayoutGroup horizontalLayoutGroup = component as HorizontalLayoutGroup;
			if (horizontalLayoutGroup == null)
			{
				return;
			}
		}
	}
}