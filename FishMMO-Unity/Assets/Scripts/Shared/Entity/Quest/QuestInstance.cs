using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an active instance of a quest for a character, including its template, objectives, and current status.
	/// </summary>
	public class QuestInstance
	{
		/// <summary>
		/// The quest template that defines this quest instance.
		/// </summary>
		public QuestTemplate template;

		/// <summary>
		/// The list of objectives for this quest instance.
		/// </summary>
		public List<QuestObjective> Objectives;

		/// <summary>
		/// The current status of the quest (e.g., Inactive, Active, Completed).
		/// </summary>
		private QuestStatus status = QuestStatus.Inactive;

		/// <summary>
		/// Public accessor for the quest's current status.
		/// </summary>
		public QuestStatus Status
		{
			get { return this.status; }
		}
	}
}