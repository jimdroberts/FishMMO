using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that abandons a quest, removing it from the character's quest log.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class AbandonQuestAction : BaseAction
	{
		/// <summary>
		/// The quest template to abandon.
		/// </summary>
		[Tooltip("The quest template to abandon.")]
		public QuestTemplate QuestTemplate;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (QuestTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IQuestController questController))
			{
				return;
			}

			questController.AbandonQuest(QuestTemplate.Name);
#endif
		}
	}
}