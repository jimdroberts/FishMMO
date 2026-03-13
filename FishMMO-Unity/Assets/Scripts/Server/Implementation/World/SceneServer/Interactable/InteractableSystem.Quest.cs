using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Quest-related broadcast handling for the InteractableSystem.
	/// Processes client requests to accept, turn in, and abandon quests via QuestInteractables.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles a client request to accept a quest from a QuestInteractable.
		/// </summary>
		private void OnServerQuestAcceptBroadcastReceived(NetworkConnection conn, QuestAcceptBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					return;
				}

				// Validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
				if (interactable == null || !interactable.CanInteract(character))
				{
					return;
				}

				IQuestInteractable questInteractable = interactable as IQuestInteractable;
				if (questInteractable == null || questInteractable.QuestTemplates == null)
				{
					return;
				}

				// Resolve the quest template
				QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
				if (template == null)
				{
					return;
				}

				// Verify the interactable actually offers this quest
				bool offersQuest = false;
				for (int i = 0; i < questInteractable.QuestTemplates.Count; i++)
				{
					if (questInteractable.QuestTemplates[i] != null &&
						questInteractable.QuestTemplates[i].ID == msg.TemplateID)
					{
						offersQuest = true;
						break;
					}
				}
				if (!offersQuest)
				{
					return;
				}

				// Validate the player can accept
				if (!template.CanAcceptQuest(character))
				{
					return;
				}

				if (!character.TryGet(out IQuestController questController))
				{
					return;
				}

				questController.Acquire(template);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a client request to turn in a completed quest at a QuestInteractable.
		/// </summary>
		private void OnServerQuestTurnInBroadcastReceived(NetworkConnection conn, QuestTurnInBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					return;
				}

				// Validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
				if (interactable == null || !interactable.CanInteract(character))
				{
					return;
				}

				IQuestInteractable questInteractable = interactable as IQuestInteractable;
				if (questInteractable == null || questInteractable.QuestTemplates == null)
				{
					return;
				}

				// Resolve the quest template
				QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
				if (template == null)
				{
					return;
				}

				// Verify the interactable actually offers this quest
				bool offersQuest = false;
				for (int i = 0; i < questInteractable.QuestTemplates.Count; i++)
				{
					if (questInteractable.QuestTemplates[i] != null &&
						questInteractable.QuestTemplates[i].ID == msg.TemplateID)
					{
						offersQuest = true;
						break;
					}
				}
				if (!offersQuest)
				{
					return;
				}

				if (!character.TryGet(out IQuestController questController))
				{
					return;
				}

				questController.TurnInQuest(template.Name);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a client request to abandon a quest. No interactable proximity required.
		/// </summary>
		private void OnServerQuestAbandonBroadcastReceived(NetworkConnection conn, QuestAbandonBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{
				QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
				if (template == null)
				{
					return;
				}

				if (!character.TryGet(out IQuestController questController))
				{
					return;
				}

				questController.AbandonQuest(template.Name);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}
