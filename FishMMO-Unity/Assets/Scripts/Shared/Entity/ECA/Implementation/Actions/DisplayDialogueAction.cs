using System;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that displays a dialogue message from a specified speaker.
	/// Supports both <see cref="DialogueEventData"/> and <see cref="PlayerInteractionEventData"/> for dynamic speaker resolution.
	/// </summary>
	[Serializable]
	public class DisplayDialogueAction : BaseAction
	{
		/// <summary>
		/// The text of the dialogue to display.
		/// </summary>
		[TextArea(3, 5)]
		public string DialogueText;

		/// <summary>
		/// The name of the speaker. If empty, will attempt to resolve from event data.
		/// </summary>
		public string SpeakerName;

		/// <summary>
		/// Displays the dialogue message from the resolved speaker.
		/// Speaker resolution order: <see cref="SpeakerName"/> field, then <see cref="DialogueEventData.Speaker"/>,
		/// then <see cref="PlayerInteractionEventData.Target"/>, then the initiator's name.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data used for dynamic speaker resolution.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			string speaker = SpeakerName;

			if (string.IsNullOrEmpty(speaker) && eventData != null)
			{
				if (eventData.TryGet(out DialogueEventData dialogueEventData) && dialogueEventData.Speaker != null)
				{
					speaker = dialogueEventData.Speaker.Name;
				}
				else if (eventData.TryGet(out PlayerInteractionEventData playerEventData) && playerEventData.Target != null)
				{
					speaker = playerEventData.Target.name;
				}
			}

			if (string.IsNullOrEmpty(speaker) && initiator != null)
			{
				speaker = initiator.Name;
			}

			Log.Debug("DisplayDialogueAction", $"[{speaker}]: {DialogueText}");
		}
	}
}