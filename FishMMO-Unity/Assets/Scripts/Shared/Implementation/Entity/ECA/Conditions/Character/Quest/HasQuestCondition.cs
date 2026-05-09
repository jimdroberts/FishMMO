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

		/// <summary>
		/// When true, requires the quest to NOT be present (inverse check).
		/// </summary>
		[Tooltip("When true, the condition passes if the character does NOT have this quest.")]
		public bool Invert;

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
				return Invert;
			}

			bool hasQuest = questController.TryGetQuest(QuestTemplate.Name, out _);
			return Invert ? !hasQuest : hasQuest;
		}
	}
}