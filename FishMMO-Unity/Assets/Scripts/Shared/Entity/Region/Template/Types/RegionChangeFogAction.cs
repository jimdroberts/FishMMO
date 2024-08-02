using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Change Fog Action", menuName = "Region/Region Change Fog", order = 1)]
	public class RegionChangeFogAction : RegionAction
	{
        public static event Action<bool, float, FogMode, Color, float, float, float> OnChangeFog;

        public bool fogEnabled = false;
		public float fogChangeRate = 1.0f;
		public FogMode fogMode = FogMode.Exponential;
		public Color fogColor = Color.gray;
		public float fogDensity = 0.0f;
		public float fogStartDistance = 0.0f;
		public float fogEndDistance = 0.0f;

		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
#if !UNITY_SERVER
			if (!character.NetworkObject.IsOwner ||
				isReconciling)
			{
				return;
			}
			OnChangeFog?.Invoke(fogEnabled, fogChangeRate, fogMode, fogColor, fogDensity, fogStartDistance, fogEndDistance);
#endif
		}
	}
}