using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that changes the skybox material when a character enters/exits a region.
	/// Client-only: suppressed on server and during prediction reconciliation.
	/// </summary>
	[Serializable]
	public class ChangeSkyboxAction : BaseAction
	{
		/// <summary>
		/// The skybox material to apply.
		/// </summary>
		[Tooltip("The skybox material to apply when this action executes.")]
		public Material Material;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if !UNITY_SERVER
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

			if (Material != null)
			{
				RenderSettings.skybox = Material;
			}
#endif
		}
	}
}