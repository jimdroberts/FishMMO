using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that turns in a completed quest, granting rewards.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class TurnInQuestAction : BaseAction
	{
		/// <summary>
		/// The quest template to turn in.
		/// </summary>
		[Tooltip("The quest template to turn in.")]
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

			questController.TurnInQuest(QuestTemplate.Name);
#endif
		}
	}
}