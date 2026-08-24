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
		/// <param name="initiator">The character interacting with the NPC.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator == null) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			AIController aiController = data.Interactable.Transform.GetComponent<AIController>();
			if (aiController == null) return;

			aiController.LookTarget = initiator.Transform;
			aiController.TransitionToIdleState();
		}
	}
}