namespace FishMMO.Shared
{
	public class QuestObjectiveInstance
	{
		public QuestObjective template;

		private long value;

		public bool IsComplete()
		{
			return template != null && this.value >= template.RequiredValue;
		}
	}
}