using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Region Audio Action", menuName = "Region/Region Audio", order = 1)]
	public class RegionAudioTriggerAction : RegionAction
	{
		public AudioClip clip;

		public override void Invoke(IPlayerCharacter character, Region region)
		{
#if !UNITY_SERVER
			if (clip == null || character == null || !character.NetworkObject.IsOwner)
			{
				return;
			}
			// play audio clip?
#endif
		}
	}
}