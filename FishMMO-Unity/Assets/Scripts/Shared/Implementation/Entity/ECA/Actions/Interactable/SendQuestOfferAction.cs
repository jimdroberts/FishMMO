using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that filters available quests and sends <see cref="QuestOfferBroadcast"/> to the player.
	/// Requires the interactable to implement <see cref="IQuestInteractable"/>.
	/// Only quests acceptable by or completable (turn-in ready) for the player are included.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class SendQuestOfferAction : BaseAction
	{
		/// <summary>
		/// Builds the list of available/turn-in quests and broadcasts them to the player.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IQuestInteractable questInteractable = data.Interactable as IQuestInteractable;
			if (questInteractable?.QuestTemplates == null) return;

			if (!player.TryGet(out IQuestController questController)) return;

			List<QuestTemplate> templates = questInteractable.QuestTemplates;
			List<int> availableIDs = new List<int>(templates.Count);

			for (int i = 0; i < templates.Count; i++)
			{
				QuestTemplate template = templates[i];
				if (template == null) continue;

				if (template.CanAcceptQuest(player))
				{
					availableIDs.Add(template.ID);
					continue;
				}

				if (questController.TryGetQuest(template.Name, out QuestInstance quest) &&
					quest.Status == QuestStatus.Complete)
				{
					availableIDs.Add(template.ID);
				}
			}

			if (availableIDs.Count < 1) return;

			initiator.NetworkObject.Broadcast(new QuestOfferBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateIDs = availableIDs.ToArray(),
			});
		}
	}
}