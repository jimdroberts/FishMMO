using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that triggers a server-authoritative dialogue session.
	/// On the server, raises <see cref="DialogueRequestEvents.OnServerDialogueRequested"/> which
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
		/// On the server: raises <see cref="DialogueRequestEvents.OnServerDialogueRequested"/> to start a dialogue session.
		/// On the client: no-op (dialogue is driven by server broadcasts).
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data used for context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (DialogueTemplate == null || initiator == null)
			{
				return;
			}

			DialogueRequestEvents.RaiseServerDialogueRequested(initiator, DialogueTemplate);
#endif
		}
	}
}