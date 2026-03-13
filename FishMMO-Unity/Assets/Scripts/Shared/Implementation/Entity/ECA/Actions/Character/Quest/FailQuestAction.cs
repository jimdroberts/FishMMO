using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that fails an active quest.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class FailQuestAction : BaseAction
	{
		/// <summary>
		/// The quest template to fail.
		/// </summary>
		[Tooltip("The quest template to fail.")]
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

			questController.FailQuest(QuestTemplate.Name);
#endif
		}
	}
}