namespace FishMMO.Shared
{
	/// <summary>
	/// Tracks runtime progress for a single quest objective.
	/// </summary>
	public class QuestObjectiveInstance
	{
		/// <summary>
		/// The quest objective template that defines this instance.
		/// </summary>
		public QuestObjective Template { get; }

		/// <summary>
		/// The current progress value for this objective.
		/// </summary>
		public long CurrentValue { get; private set; }

		/// <summary>
		/// Constructs a new objective instance from a template with an optional starting value.
		/// </summary>
		/// <param name="template">The quest objective template.</param>
		/// <param name="initialValue">Optional starting progress value.</param>
		public QuestObjectiveInstance(QuestObjective template, long initialValue = 0)
		{
			Template = template;
			CurrentValue = initialValue;
		}

		/// <summary>
		/// Returns true when progress meets or exceeds the required value.
		/// </summary>
		public bool IsComplete
		{
			get { return Template != null && CurrentValue >= Template.RequiredValue; }
		}

		/// <summary>
		/// Increments progress by the given amount, clamping to the required value.
		/// </summary>
		/// <param name="amount">The amount to add.</param>
		public void Increment(long amount)
		{
			if (Template == null || amount <= 0)
			{
				return;
			}
			CurrentValue += amount;
			if (CurrentValue > Template.RequiredValue)
			{
				CurrentValue = Template.RequiredValue;
			}
		}

		/// <summary>
		/// Sets the current progress to an exact value, clamping between zero and required.
		/// </summary>
		/// <param name="value">The value to set.</param>
		public void SetValue(long value)
		{
			if (Template == null)
			{
				return;
			}
			if (value < 0)
			{
				value = 0;
			}
			if (value > Template.RequiredValue)
			{
				value = Template.RequiredValue;
			}
			CurrentValue = value;
		}
	}
}