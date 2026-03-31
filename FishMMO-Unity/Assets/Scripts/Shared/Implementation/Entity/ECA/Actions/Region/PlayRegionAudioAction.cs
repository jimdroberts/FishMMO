using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that triggers audio playback when a character enters a region.
	/// Client-only: suppressed on server and during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class PlayRegionAudioAction : BaseAction
	{
		/// <summary>
		/// Addressable reference to the audio clip to play.
		/// </summary>
		[Tooltip("Addressable reference to the audio clip to play.")]
		public AssetReference ClipReference;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
			if (initiator == null ||
				ClipReference == null ||
				!ClipReference.RuntimeKeyIsValid())
			{
				return;
			}

			if (!initiator.NetworkObject.IsOwner)
			{
				return;
			}

			if (eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling)
			{
				return;
			}

			// TODO: Implement audio playback for the character here.
			// Use AddressableLoadProcessor or Addressables.LoadAssetAsync<AudioClip>(ClipReference) to load and play.
#endif
		}
	}
}