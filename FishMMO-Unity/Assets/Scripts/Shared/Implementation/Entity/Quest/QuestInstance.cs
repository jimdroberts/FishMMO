using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Runtime instance of an accepted quest for a character.
	/// Holds the template reference, per-objective progress, and lifecycle status.
	/// </summary>
	public class QuestInstance
	{
		/// <summary>
		/// The quest template that defines this quest.
		/// </summary>
		public QuestTemplate Template { get; }

		/// <summary>
		/// Tracked objective instances with individual progress.
		/// </summary>
		public List<QuestObjectiveInstance> Objectives { get; }

		/// <summary>
		/// Current lifecycle status.
		/// </summary>
		public QuestStatus Status { get; private set; }

		/// <summary>
		/// Constructs a new quest instance from the given template, initializing all objective instances.
		/// </summary>
		/// <param name="template">The quest template.</param>
		public QuestInstance(QuestTemplate template)
		{
			Template = template;
			Status = QuestStatus.Active;

			if (template.Objectives != null && template.Objectives.Count > 0)
			{
				Objectives = new List<QuestObjectiveInstance>(template.Objectives.Count);
				for (int i = 0; i < template.Objectives.Count; i++)
				{
					Objectives.Add(new QuestObjectiveInstance(template.Objectives[i]));
				}
			}
			else
			{
				Objectives = new List<QuestObjectiveInstance>(0);
			}
		}

		/// <summary>
		/// Constructs a quest instance from a template with pre-existing objective values (for DB load).
		/// </summary>
		/// <param name="template">The quest template.</param>
		/// <param name="status">The saved status.</param>
		/// <param name="objectiveValues">Per-objective current values in template order.</param>
		public QuestInstance(QuestTemplate template, QuestStatus status, long[] objectiveValues)
		{
			Template = template;
			Status = status;

			int count = template.Objectives != null ? template.Objectives.Count : 0;
			Objectives = new List<QuestObjectiveInstance>(count);
			for (int i = 0; i < count; i++)
			{
				long value = (objectiveValues != null && i < objectiveValues.Length) ? objectiveValues[i] : 0;
				Objectives.Add(new QuestObjectiveInstance(template.Objectives[i], value));
			}
		}

		/// <summary>
		/// Transitions the quest to a new status. Invalid transitions are rejected.
		/// </summary>
		/// <param name="newStatus">The target status.</param>
		/// <returns>True if the transition was applied.</returns>
		public bool TrySetStatus(QuestStatus newStatus)
		{
			switch (newStatus)
			{
				case QuestStatus.Complete:
					if (Status != QuestStatus.Active) return false;
					break;
				case QuestStatus.TurnedIn:
					if (Status != QuestStatus.Complete) return false;
					break;
				case QuestStatus.Failed:
					if (Status != QuestStatus.Active) return false;
					break;
				default:
					return false;
			}

			Status = newStatus;
			return true;
		}

		/// <summary>
		/// Returns true when every objective in the quest has been completed.
		/// </summary>
		public bool AreAllObjectivesComplete()
		{
			for (int i = 0; i < Objectives.Count; i++)
			{
				if (!Objectives[i].IsComplete)
				{
					return false;
				}
			}
			return true;
		}
	}
}