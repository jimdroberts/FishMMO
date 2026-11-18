using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that changes fog settings when a character enters/exits a region.
	/// Client-only: suppressed on server and during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class ChangeFogAction : BaseAction
	{
		/// <summary>
		/// Raised on the client when fog settings should change. Subscribe in a MonoBehaviour to apply.
		/// </summary>
		public static event Action<FogSettings> OnChangeFog;

		/// <summary>
		/// The fog settings to apply.
		/// </summary>
		[Tooltip("The fog settings to apply when this action executes.")]
		public FogSettings FogSettings;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
			if (FogSettings == null)
			{
				return;
			}

			// When initiator is present, restrict to the owning client only.
			if (initiator != null && !initiator.NetworkObject.IsOwner)
			{
				return;
			}

			if (eventData != null &&
				eventData.TryGet(out RegionEventData regionData) &&
				regionData.IsReconciling)
			{
				return;
			}

			OnChangeFog?.Invoke(FogSettings);
#endif
		}
	}
}