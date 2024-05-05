using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Change Fog Action", menuName = "Region/Region Change Fog", order = 1)]
	public class RegionChangeFogAction : RegionAction
	{
		public bool fogEnabled = false;
		public FogMode fogMode = FogMode.Exponential;
		public Color fogColor = Color.gray;
		public float fogDensity = 0.0f;
		public float fogStartDistance = 0.0f;
		public float fogEndDistance = 0.0f;

		public override void Invoke(IPlayerCharacter character, Region region)
		{
#if !UNITY_SERVER
			if (!character.NetworkObject.IsOwner)
			{
				return;
			}
			RenderSettings.fog = fogEnabled;
			RenderSettings.fogMode = fogMode;
			RenderSettings.fogColor = fogColor;
			RenderSettings.fogDensity = fogDensity;
			RenderSettings.fogStartDistance = fogStartDistance;
			RenderSettings.fogEndDistance = fogEndDistance;
#endif
		}
	}
}