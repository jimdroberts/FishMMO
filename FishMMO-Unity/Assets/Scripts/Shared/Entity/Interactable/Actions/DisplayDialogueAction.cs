using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Display Dialogue Action", menuName = "FishMMO/Actions/Display Dialogue Action", order = 0)]
	public class DisplayDialogueAction : BaseAction
	{
		[TextArea(3, 5)]
		public string DialogueText;
		public string SpeakerName;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// The speaker could be the eventTarget, or a predefined name.
			string speaker = SpeakerName;
			if (string.IsNullOrEmpty(speaker) && eventData != null && eventData.TryGet(out PlayerInteractionEventData playerEventData))
			{
				speaker = playerEventData.Target.name; // Use NPC name as speaker
			}
			//Debug.Log($"[{speaker}]: {DialogueText} (to {playerCharacter?.Name}. Event: {eventData})");
			//UIManager.Instance.ShowDialogue(speaker, DialogueText);
		}
	}
}