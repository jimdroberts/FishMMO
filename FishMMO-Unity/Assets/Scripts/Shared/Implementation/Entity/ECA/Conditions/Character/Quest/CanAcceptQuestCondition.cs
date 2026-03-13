using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether the character can accept a specific quest.
	/// Evaluates attribute requirements, prerequisite quests, and whether it is already acquired.
	/// </summary>
	[Serializable]
	public class CanAcceptQuestCondition : BaseCondition
	{
		/// <summary>
		/// The quest template to check acceptance eligibility for.
		/// </summary>
		[Tooltip("The quest template to check acceptance eligibility for.")]
		public QuestTemplate QuestTemplate;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			ICharacter target = ResolveTarget(initiator, eventData);
			if (target == null || QuestTemplate == null)
			{
				return false;
			}

			IPlayerCharacter player = target as IPlayerCharacter;
			if (player == null)
			{
				return false;
			}

			if (!player.TryGet(out IQuestController questController))
			{
				return false;
			}

			// Already has this quest
			if (questController.TryGetQuest(QuestTemplate.Name, out _))
			{
				return false;
			}

			return QuestTemplate.CanAcceptQuest(player);
		}
	}
}