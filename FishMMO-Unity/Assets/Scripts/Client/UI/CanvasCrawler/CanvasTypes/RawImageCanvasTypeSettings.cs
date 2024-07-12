using FishMMO.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class RawImageCanvasTypeSettings : BaseCanvasTypeSettings
	{
		public override void ApplySettings(object component, Configuration configuration)
		{
			RawImage rawImage = component as RawImage;
			if (rawImage == null)
			{
				return;
			}
		}
	}
}