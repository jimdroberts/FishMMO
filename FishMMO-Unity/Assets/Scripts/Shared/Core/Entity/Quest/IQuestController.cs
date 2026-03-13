using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for controllers that manage quest instances and lifecycle for a character.
	/// </summary>
	public interface IQuestController : ICharacterBehaviour
	{
		/// <summary>
		/// Raised when a quest is accepted. Parameters: character, quest instance.
		/// </summary>
		static Action<ICharacter, QuestInstance> OnQuestAccepted;

		/// <summary>
		/// Raised when an objective is updated. Parameters: character, quest instance, objective index.
		/// </summary>
		static Action<ICharacter, QuestInstance, int> OnObjectiveUpdated;

		/// <summary>
		/// Raised when all objectives are met and the quest transitions to Complete.
		/// Parameters: character, quest instance.
		/// </summary>
		static Action<ICharacter, QuestInstance> OnQuestComplete;

		/// <summary>
		/// Raised when a quest is turned in and rewards are granted.
		/// Parameters: character, quest instance.
		/// </summary>
		static Action<ICharacter, QuestInstance> OnQuestTurnedIn;

		/// <summary>
		/// Raised when a quest is failed (timer expired, etc.).
		/// Parameters: character, quest instance.
		/// </summary>
		static Action<ICharacter, QuestInstance> OnQuestFailed;

		/// <summary>
		/// Read-only accessor for all active quests keyed by template name.
		/// </summary>
		Dictionary<string, QuestInstance> Quests { get; }

		/// <summary>
		/// Attempts to retrieve a quest instance by template name.
		/// </summary>
		/// <param name="name">The name of the quest to look up.</param>
		/// <param name="quest">The found quest instance, or null if not found.</param>
		/// <returns>True if the quest is found, false otherwise.</returns>
		bool TryGetQuest(string name, out QuestInstance quest);

		/// <summary>
		/// Accepts a new quest for the character. Ignored if already acquired.
		/// </summary>
		/// <param name="template">The quest template to accept.</param>
		void Acquire(QuestTemplate template);

		/// <summary>
		/// Advances a specific objective within a quest by the given amount.
		/// </summary>
		/// <param name="questName">The name of the quest.</param>
		/// <param name="objectiveIndex">The index of the objective to advance.</param>
		/// <param name="amount">The amount to increment.</param>
		void AdvanceObjective(string questName, int objectiveIndex, long amount);

		/// <summary>
		/// Attempts to complete a quest, transitioning it from Active to Complete.
		/// Returns false if objectives are not met or the quest is not active.
		/// </summary>
		/// <param name="questName">The name of the quest to complete.</param>
		/// <returns>True if the quest transitioned to Complete.</returns>
		bool TryCompleteQuest(string questName);

		/// <summary>
		/// Turns in a completed quest, granting rewards and transitioning to TurnedIn.
		/// </summary>
		/// <param name="questName">The name of the quest to turn in.</param>
		/// <returns>True if the quest was successfully turned in.</returns>
		bool TurnInQuest(string questName);

		/// <summary>
		/// Fails an active quest.
		/// </summary>
		/// <param name="questName">The name of the quest to fail.</param>
		/// <returns>True if the quest was successfully failed.</returns>
		bool FailQuest(string questName);

		/// <summary>
		/// Abandons a quest, removing it from the character's quest log.
		/// </summary>
		/// <param name="questName">The name of the quest to abandon.</param>
		/// <returns>True if the quest was found and removed.</returns>
		bool AbandonQuest(string questName);

		/// <summary>
		/// Sets quest state from broadcast or database load. Used by client-side sync.
		/// </summary>
		/// <param name="template">The quest template.</param>
		/// <param name="status">The status to set.</param>
		/// <param name="objectiveValues">Per-objective progress values.</param>
		void SetQuest(QuestTemplate template, QuestStatus status, long[] objectiveValues);
	}
}