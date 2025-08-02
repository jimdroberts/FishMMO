namespace FishMMO.Shared
{
	/// <summary>
	/// Represents an active instance of a quest objective, tracking progress and completion.
	/// </summary>
	public class QuestObjectiveInstance
	{
		/// <summary>
		/// The quest objective template that defines this instance.
		/// </summary>
		public QuestObjective template;

		/// <summary>
		/// The current progress value for this objective instance.
		/// </summary>
		private long value;

		/// <summary>
		/// Checks if the objective is complete based on the required value in the template.
		/// </summary>
		/// <returns>True if complete, false otherwise.</returns>
		public bool IsComplete()
		{
			return template != null && this.value >= template.RequiredValue;
		}
	}
}