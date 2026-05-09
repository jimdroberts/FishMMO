using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether the character has a quest at a specific status.
	/// Commonly used to check if a quest is completed/turned-in before unlocking dialogue choices.
	/// </summary>
	[Serializable]
	public class QuestStatusCondition : BaseCondition
	{
		/// <summary>
		/// The quest template to check.
		/// </summary>
		[Tooltip("The quest template to check.")]
		public QuestTemplate QuestTemplate;

		/// <summary>
		/// The required status for the condition to pass.
		/// </summary>
		[Tooltip("The required quest status.")]
		public QuestStatus RequiredStatus = QuestStatus.TurnedIn;

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
			return quest.Status == RequiredStatus;
		}
	}
}