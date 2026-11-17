using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether the character currently has a specific quest in their quest log.
	/// </summary>
	[Serializable]
	public class HasQuestCondition : BaseCondition
	{
		/// <summary>
		/// The quest template to check for.
		/// </summary>
		[Tooltip("The quest template to check for in the character's quest log.")]
		public QuestTemplate QuestTemplate;

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

			return questController.TryGetQuest(QuestTemplate.Name, out _);
		}
	}
}