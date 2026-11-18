using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether a specific quest objective has been completed.
	/// </summary>
	[Serializable]
	public class QuestObjectiveCompleteCondition : BaseCondition
	{
		/// <summary>
		/// The quest template containing the objective.
		/// </summary>
		[Tooltip("The quest template containing the objective to check.")]
		public QuestTemplate QuestTemplate;

		/// <summary>
		/// The zero-based index of the objective within the quest template.
		/// </summary>
		[Tooltip("Zero-based index of the objective to check.")]
		public int ObjectiveIndex;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			ICharacter target = (eventData?.TargetCharacter ?? initiator);
			if (target == null || QuestTemplate == null)
			{
				return false;
			}
			if (!target.TryGet(out IQuestController questController))
			{
				return false;
			}
			if (!questController.TryGetQuest(QuestTemplate.Name, out QuestInstance quest))
			{
				return false;
			}
			if (ObjectiveIndex < 0 || ObjectiveIndex >= quest.Objectives.Count)
			{
				return false;
			}
			return quest.Objectives[ObjectiveIndex].IsComplete;
		}
	}
}