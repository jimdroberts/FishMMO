using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that makes an NPC face the interacting player and transition to idle state.
	/// Add this to any NPC's <see cref="Interactable.OnInteractTriggers"/> alongside the primary
	/// interaction action (e.g., <see cref="SendMerchantBroadcastAction"/>) to achieve the
	/// classic "NPC turns to greet the player" behaviour.
	/// </summary>
	[Serializable]
	public class NPCLookAtInteractorAction : BaseAction
	{
		/// <summary>
		/// Rotates the NPC toward the interacting player and transitions its AI to idle.
		/// No-op on the client (server-only AI state).
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (initiator == null) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			AIController aiController = data.Interactable.Transform.GetComponent<AIController>();
			if (aiController == null) return;

			aiController.LookTarget = initiator.Transform;
			aiController.TransitionToIdleState();
#endif
		}
	}
}