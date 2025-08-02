using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that displays a dialogue message from a specified speaker, typically used for NPC or story interactions.
	/// </summary>
	[CreateAssetMenu(fileName = "New Display Dialogue Action", menuName = "FishMMO/Triggers/Actions/Display Dialogue Action", order = 0)]
	public class DisplayDialogueAction : BaseAction
	{
		/// <summary>
		/// The text of the dialogue to display.
		/// </summary>
		[TextArea(3, 5)]
		public string DialogueText;

		/// <summary>
		/// The name of the speaker. If empty, will attempt to use the event target's name.
		/// </summary>
		public string SpeakerName;

		/// <summary>
		/// Displays the dialogue message from the specified speaker.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data, which may contain the target for dynamic speaker assignment.</param>
		/// <remarks>
		/// If <see cref="SpeakerName"/> is empty, this method attempts to use the name of the event target as the speaker.
		/// The actual UI display logic is commented out and should be implemented as needed.
		/// </remarks>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Determine the speaker: use the provided name, or fall back to the event target's name if available.
			string speaker = SpeakerName;
			if (string.IsNullOrEmpty(speaker) && eventData != null && eventData.TryGet(out PlayerInteractionEventData playerEventData))
			{
				speaker = playerEventData.Target.name; // Use NPC name as speaker
			}
			// The actual dialogue display logic should be implemented here, e.g.:
			// UIManager.Instance.ShowDialogue(speaker, DialogueText);
		}

		/// <summary>
		/// Returns a formatted description of the display dialogue action for UI display.
		/// </summary>
		/// <returns>A string describing the dialogue and speaker.</returns>
		public override string GetFormattedDescription()
		{
			return $"Displays dialogue: <color=#FFD700>\"{DialogueText}\"</color> from <color=#FFD700>{SpeakerName}</color>.";
		}
	}
}