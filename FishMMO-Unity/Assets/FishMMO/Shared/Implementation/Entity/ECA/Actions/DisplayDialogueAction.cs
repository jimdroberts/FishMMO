using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that triggers a server-authoritative dialogue session.
	/// On the server, raises <see cref="IDialogueInteractable.OnServerDialogueRequested"/> which
	/// the InteractableSystem subscribes to. On the client, this action is a no-op.
	/// </summary>
	[Serializable]
	public class DisplayDialogueAction : BaseAction
	{
		/// <summary>
		/// The dialogue template to start when this action executes on the server.
		/// </summary>
		public DialogueTemplate DialogueTemplate;

		/// <summary>
		/// Optional speaker name override. If empty, resolves from event data.
		/// </summary>
		public string SpeakerName;

		/// <summary>
		/// On the server: raises <see cref="IDialogueInteractable.OnServerDialogueRequested"/> to start a dialogue session.
		/// On the client: no-op (dialogue is driven by server broadcasts).
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data used for context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			 * editor, where the scene server also runs — see BaseAction.IsServer.
			 *
			 * This action was the last one still using the compile-time gate, and it cost the
			 * whole feature. Outside a dedicated server build the body was not compiled at all, so
			 * Execute was an empty method: no dialogue in the editor, and none in a host build.
			 * Every other action in this folder had already been converted for exactly this
			 * reason. */
			if (!IsServer(initiator))
			{
				return;
			}

			if (DialogueTemplate == null)
			{
				return;
			}

			/* Pass the interactable along when there is one. The session records its scene object
			 * ID, and that ID is what the choice handler range-checks on every subsequent choice —
			 * without it the check is skipped entirely and the conversation follows the player
			 * wherever they go. An ECA dialogue raised from something other than an interaction
			 * legitimately has none, and stays unanchored. */
			IInteractable interactable = null;
			if (eventData != null &&
				eventData.TryGet(out PlayerInteractionEventData interactionData))
			{
				interactable = interactionData.Interactable;
			}

			IDialogueInteractable.OnServerDialogueRequested?.Invoke(initiator, DialogueTemplate, interactable);
		}
	}
}