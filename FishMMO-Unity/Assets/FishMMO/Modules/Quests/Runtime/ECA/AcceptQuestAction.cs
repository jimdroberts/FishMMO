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
			/* Server only, decided at runtime rather than by a build define.
			 *
			 * This body was wrapped in `#if UNITY_SERVER`, which is a BUILD TARGET define and is
			 * undefined in the editor the scene server is developed in — so the action compiled away
			 * entirely there and did nothing on the server either. That failure is invisible: the
			 * action still exists, still serialises, and its trigger still fires; it simply never has
			 * an effect, which reads as "the quest/item/achievement hook is broken" rather than as a
			 * build-configuration problem. EcaAuthority asks the question that was meant all along,
			 * of the peer the character actually belongs to. See EcaAuthority's own remarks. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

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
		}
	}
}