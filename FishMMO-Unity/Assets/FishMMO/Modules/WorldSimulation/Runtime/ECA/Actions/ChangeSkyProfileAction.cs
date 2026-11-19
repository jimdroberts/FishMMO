using System;
using UnityEngine;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that blends the sky to another profile (entering a haunted valley, leaving a
	/// storm god's arena). Client-only: suppressed on the server and during reconciliation.
	/// </summary>
	/// <remarks>
	/// The sky has one owner on the client; this only asks it to blend. A null profile returns
	/// to the scene's own sky.
	/// </remarks>
	[Serializable]
	public class ChangeSkyProfileAction : BaseAction
	{
		[Tooltip("The sky to blend to. Empty: back to the scene's own sky.")]
		public SkyProfile Profile;
		[Tooltip("Seconds the blend takes.")]
		[Min(0f)] public float BlendSeconds = 3f;

		/// <summary>Raised on the owning client with the profile and blend time.</summary>
		public static event Action<SkyProfile, float> OnChangeSkyProfile;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
			if (initiator != null && !initiator.NetworkObject.IsOwner)
			{
				return;
			}
			if (eventData != null && eventData.TryGet(out RegionEventData regionData) && regionData.IsReconciling)
			{
				return;
			}
			OnChangeSkyProfile?.Invoke(Profile, BlendSeconds);
#endif
		}
	}
}
