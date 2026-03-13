using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for quest-related ECA actions and conditions.
	/// Carries the quest template and optional objective index for context.
	/// </summary>
	public class QuestEventData : EventData
	{
		/// <summary>
		/// The quest template associated with this event.
		/// </summary>
		public QuestTemplate QuestTemplate { get; }

		/// <summary>
		/// The objective index within the quest, or -1 if not applicable.
		/// </summary>
		public int ObjectiveIndex { get; }

		/// <summary>
		/// The amount to advance the objective by, or 0 if not applicable.
		/// </summary>
		public long Amount { get; }

		/// <summary>
		/// Constructs a new QuestEventData.
		/// </summary>
		/// <param name="initiator">The character involved in the quest event.</param>
		/// <param name="questTemplate">The quest template.</param>
		/// <param name="objectiveIndex">The objective index, or -1.</param>
		/// <param name="amount">The amount to advance, or 0.</param>
		public QuestEventData(ICharacter initiator, QuestTemplate questTemplate, int objectiveIndex = -1, long amount = 0)
			: base(initiator)
		{
			QuestTemplate = questTemplate;
			ObjectiveIndex = objectiveIndex;
			Amount = amount;
		}
	}
}