using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Change Fog Action", menuName = "Region/Region Change Fog", order = 1)]
	public class RegionChangeFogAction : RegionAction
	{
        public static event Action<FogSettings> OnChangeFog;

        public FogSettings FogSettings;

		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
#if !UNITY_SERVER
			if (character != null)
			{
				if (!character.NetworkObject.IsOwner ||
					isReconciling)
				{
					return;
				}
			}

			OnChangeFog?.Invoke(FogSettings);
#endif
		}
	}
}