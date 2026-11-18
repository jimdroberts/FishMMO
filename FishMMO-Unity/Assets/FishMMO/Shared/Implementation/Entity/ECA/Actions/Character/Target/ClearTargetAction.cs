using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that clears the initiator's current target.
	/// Suppressed during client-side prediction replay to prevent redundant
	/// target-clearing on every replay frame.
	/// </summary>
	[Serializable]
	public class ClearTargetAction : BaseAction
	{
		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Suppress during client-side prediction replay to prevent redundant
			// target-clearing on every replay frame. Target state is client-predicted
			// and reconcile will restore the authoritative value if needed.
			if (eventData != null && eventData.TryGet(out TickEventData tickData) && tickData.IsReplicateTick)
			{
				return;
			}

			if (initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out ITargetController targetController))
			{
				return;
			}

			targetController.UpdateTarget(Vector3.zero, Vector3.zero, 0f);
		}
	}
}