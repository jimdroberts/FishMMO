using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Region action that triggers an audio clip for the player character when invoked.
	/// </summary>
	[CreateAssetMenu(fileName = "New Region Audio Action", menuName = "FishMMO/Region/Region Audio", order = 1)]
	public class RegionAudioTriggerAction : RegionAction
	{
		/// <summary>
		/// The audio clip to play when the region action is triggered.
		/// </summary>
		public AudioClip clip;

		/// <summary>
		/// Invokes the region action, playing the specified audio clip for the player character if conditions are met.
		/// </summary>
		/// <param name="character">The player character to play the audio for.</param>
		/// <param name="region">The region in which the action is triggered.</param>
		/// <param name="isReconciling">Indicates if the action is part of a reconciliation process.</param>
		public override void Invoke(IPlayerCharacter character, Region region, bool isReconciling)
		{
#if !UNITY_SERVER
			// Only play the audio if:
			// - The audio clip is assigned
			// - The character is valid
			// - The character is the owner of the network object (to avoid duplicate playback)
			// - The action is not part of a reconciliation process
			if (clip == null ||
				character == null ||
				!character.NetworkObject.IsOwner ||
				isReconciling)
			{
				return;
			}
			// TODO: Implement audio playback for the character here.
#endif
		}
	}
}