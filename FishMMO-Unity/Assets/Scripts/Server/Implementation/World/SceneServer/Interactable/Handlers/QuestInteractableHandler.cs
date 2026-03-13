using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Transporting;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles the initial interaction with a <see cref="QuestInteractable"/>.
	/// Filters the quest list to those the player can accept or turn in,
	/// then broadcasts the available template IDs to the client.
	/// </summary>
	[HandlesInteractable(typeof(QuestInteractable))]
	public class QuestInteractableHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public QuestInteractableHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			IQuestInteractable questInteractable = interactable as IQuestInteractable;
			if (questInteractable == null || questInteractable.QuestTemplates == null)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			List<QuestTemplate> templates = questInteractable.QuestTemplates;
			List<int> availableIDs = new List<int>();

			for (int i = 0; i < templates.Count; i++)
			{
				QuestTemplate template = templates[i];
				if (template == null)
				{
					continue;
				}

				// Include quests the player can accept
				if (template.CanAcceptQuest(character))
				{
					availableIDs.Add(template.ID);
					continue;
				}

				// Include quests the player has completed and can turn in
				if (questController.TryGetQuest(template.Name, out QuestInstance quest) &&
					quest.Status == QuestStatus.Complete)
				{
					availableIDs.Add(template.ID);
				}
			}

			if (availableIDs.Count < 1)
			{
				return;
			}

			server.NetworkWrapper.Broadcast(character.Owner, new QuestOfferBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateIDs = availableIDs,
			}, true, Channel.Reliable);

			interactableSystem.OnInteractNPC(character, interactable);
		}
	}
}
