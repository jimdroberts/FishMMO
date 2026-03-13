using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server to Client broadcast for updating a single quest's state and objective progress.
	/// </summary>
	public struct QuestUpdateBroadcast : IBroadcast
	{
		/// <summary>CachedScriptableObject ID of the QuestTemplate.</summary>
		public int TemplateID;

		/// <summary>Current lifecycle status of the quest.</summary>
		public QuestStatus Status;

		/// <summary>Per-objective progress values in template order.</summary>
		public long[] ObjectiveValues;
	}

	/// <summary>
	/// Server to Client broadcast for updating multiple quests at once (login sync).
	/// </summary>
	public struct QuestUpdateMultipleBroadcast : IBroadcast
	{
		/// <summary>List of individual quest updates.</summary>
		public List<QuestUpdateBroadcast> Quests;
	}

	/// <summary>
	/// Server to Client broadcast for removing a quest from the client's quest log.
	/// </summary>
	public struct QuestRemoveBroadcast : IBroadcast
	{
		/// <summary>CachedScriptableObject ID of the QuestTemplate to remove.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Server to Client broadcast presenting available quests from a QuestInteractable.
	/// </summary>
	public struct QuestOfferBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the quest interactable.</summary>
		public long InteractableID;

		/// <summary>CachedScriptableObject IDs of the available QuestTemplates.</summary>
		public List<int> TemplateIDs;
	}

	/// <summary>
	/// Client to Server broadcast requesting to accept a quest from a quest interactable.
	/// </summary>
	public struct QuestAcceptBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the quest interactable.</summary>
		public long InteractableID;

		/// <summary>CachedScriptableObject ID of the QuestTemplate to accept.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Client to Server broadcast requesting to turn in a completed quest.
	/// </summary>
	public struct QuestTurnInBroadcast : IBroadcast
	{
		/// <summary>Scene object ID of the quest interactable.</summary>
		public long InteractableID;

		/// <summary>CachedScriptableObject ID of the QuestTemplate to turn in.</summary>
		public int TemplateID;
	}

	/// <summary>
	/// Client to Server broadcast requesting to abandon a quest.
	/// </summary>
	public struct QuestAbandonBroadcast : IBroadcast
	{
		/// <summary>CachedScriptableObject ID of the QuestTemplate to abandon.</summary>
		public int TemplateID;
	}
}