using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Change Skybox Action", menuName = "Region/Region Change Skybox", order = 1)]
	public class RegionChangeSkyboxAction : RegionAction
	{
		public Material Material;

		public override void Invoke(IPlayerCharacter character, Region region)
		{
#if !UNITY_SERVER
			if (!character.NetworkObject.IsOwner)
			{
				return;
			}
			if (Material != null)
			{
				RenderSettings.skybox = Material;
			}
#endif
		}
	}
}