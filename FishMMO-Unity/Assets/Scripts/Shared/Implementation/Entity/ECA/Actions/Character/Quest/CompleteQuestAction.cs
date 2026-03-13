using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that attempts to complete a quest (transition from Active to Complete).
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class CompleteQuestAction : BaseAction
	{
		/// <summary>
		/// The quest template to complete.
		/// </summary>
		[Tooltip("The quest template to complete.")]
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

			questController.TryCompleteQuest(QuestTemplate.Name);
#endif
		}
	}
}