using UnityEngine;

namespace FishMMO.Shared
{
	public class RegionChangeSkyboxAction : RegionAction
	{
		public Material material;

		public override void Invoke(Character character, Region region)
		{
			if (material != null)
				RenderSettings.skybox = material;
		}
	}
}