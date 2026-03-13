using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that grants a quest to the initiating character.
	/// On the server, adds the quest to the character's quest controller and
	/// raises <see cref="IQuestController.OnQuestAccepted"/>.
	/// On the client, this action is a no-op.
	/// </summary>
	[Serializable]
	public class AcceptQuestAction : BaseAction
	{
		/// <summary>
		/// The quest template to grant.
		/// </summary>
		[Tooltip("The quest template to grant to the character.")]
		public QuestTemplate QuestTemplate;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
#if UNITY_SERVER
			if (QuestTemplate == null || initiator == null)
			{
				return;
			}

			IPlayerCharacter player = initiator as IPlayerCharacter;
			if (player == null)
			{
				return;
			}

			if (!player.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!QuestTemplate.CanAcceptQuest(player))
			{
				return;
			}

			questController.Acquire(QuestTemplate);
#endif
		}
	}
}
