using UnityEngine;

namespace FishMMO.Shared
{
	public class RegionAudioTriggerAction : RegionAction
	{
		public AudioClip clip;

		public override void Invoke(Character character, Region region)
		{
			if (clip == null || character == null)
			{
				return;
			}
			// play audio clip?
		}
	}
}