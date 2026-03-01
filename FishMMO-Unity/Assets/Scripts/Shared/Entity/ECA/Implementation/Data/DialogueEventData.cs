namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for dialogue interactions, carrying the NPC speaker reference and dialogue node context.
	/// Used by ECA conditions and actions that operate within a dialogue tree.
	/// </summary>
	public class DialogueEventData : EventData
	{
		/// <summary>
		/// The NPC or character that is speaking in the dialogue.
		/// </summary>
		public ICharacter Speaker { get; }

		/// <summary>
		/// The current dialogue node ID in the dialogue tree.
		/// </summary>
		public int NodeId { get; }

		/// <summary>
		/// The index of the choice selected by the player, or -1 if no choice was made (e.g., on node entry).
		/// </summary>
		public int ChoiceIndex { get; }

		/// <summary>
		/// Constructs a new <see cref="DialogueEventData"/>.
		/// </summary>
		/// <param name="initiator">The player character participating in the dialogue.</param>
		/// <param name="speaker">The NPC or character speaking.</param>
		/// <param name="nodeId">The current dialogue node ID.</param>
		/// <param name="choiceIndex">The selected choice index, or -1 if none.</param>
		public DialogueEventData(ICharacter initiator, ICharacter speaker, int nodeId, int choiceIndex = -1)
			: base(initiator)
		{
			Speaker = speaker;
			NodeId = nodeId;
			ChoiceIndex = choiceIndex;
		}
	}
}
