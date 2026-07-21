using System.Collections.Generic;
using FishNet.Transporting;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Manages quest instances for a character including acceptance, objective tracking,
	/// completion, turn-in, failure, and abandonment. Syncs state via broadcasts.
	/// </summary>
	public class QuestController : CharacterBehaviour, IQuestController
	{
		/// <summary>
		/// All quests for this character keyed by template name.
		/// </summary>
		private Dictionary<string, QuestInstance> quests = new Dictionary<string, QuestInstance>();

		/// <inheritdoc />
		public Dictionary<string, QuestInstance> Quests
		{
			get { return quests; }
		}

		/// <summary>
		/// Resets quest state on the server or client.
		/// </summary>
		/// <param name="asServer">Whether this reset is on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			quests.Clear();
		}

#if !UNITY_SERVER
		/// <summary>
		/// Registers broadcast handlers on the owning client.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<QuestUpdateBroadcast>(OnClientQuestUpdateReceived);
			ClientManager.RegisterBroadcast<QuestUpdateMultipleBroadcast>(OnClientQuestUpdateMultipleReceived);
			ClientManager.RegisterBroadcast<QuestRemoveBroadcast>(OnClientQuestRemoveReceived);
		}

		/// <summary>
		/// Unregisters broadcast handlers on the owning client.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<QuestUpdateBroadcast>(OnClientQuestUpdateReceived);
				ClientManager.UnregisterBroadcast<QuestUpdateMultipleBroadcast>(OnClientQuestUpdateMultipleReceived);
				ClientManager.UnregisterBroadcast<QuestRemoveBroadcast>(OnClientQuestRemoveReceived);
			}
		}

		/// <summary>
		/// Handles a single quest update broadcast from the server.
		/// </summary>
		private void OnClientQuestUpdateReceived(QuestUpdateBroadcast msg, Channel channel)
		{
			QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
			if (template == null)
			{
				return;
			}
			SetQuest(template, msg.Status, msg.ObjectiveValues);
		}

		/// <summary>
		/// Handles a batch quest update broadcast from the server.
		/// </summary>
		private void OnClientQuestUpdateMultipleReceived(QuestUpdateMultipleBroadcast msg, Channel channel)
		{
			if (msg.Quests == null)
			{
				return;
			}
			for (int i = 0; i < msg.Quests.Length; i++)
			{
				OnClientQuestUpdateReceived(msg.Quests[i], channel);
			}
		}

		/// <summary>
		/// Handles a quest removal broadcast from the server (abandon/complete removal).
		/// </summary>
		private void OnClientQuestRemoveReceived(QuestRemoveBroadcast msg, Channel channel)
		{
			QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
			if (template != null)
			{
				quests.Remove(template.Name);
			}
		}

		/// <summary>
		/// Sends a quest accept request to the server for the given quest at the given interactable.
		/// </summary>
		public void RequestAcceptQuest(int templateID, long interactableID)
		{
			ClientManager.Broadcast(new QuestAcceptBroadcast()
			{
				InteractableID = interactableID,
				TemplateID = templateID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Sends a quest turn-in request to the server for the given quest at the given interactable.
		/// </summary>
		public void RequestTurnInQuest(int templateID, long interactableID)
		{
			ClientManager.Broadcast(new QuestTurnInBroadcast()
			{
				InteractableID = interactableID,
				TemplateID = templateID,
			}, Channel.Reliable);
		}

		/// <summary>
		/// Sends a quest abandon request to the server for the given quest.
		/// </summary>
		public void RequestAbandonQuest(int templateID)
		{
			ClientManager.Broadcast(new QuestAbandonBroadcast()
			{
				TemplateID = templateID,
			}, Channel.Reliable);
		}
#endif

		/// <inheritdoc />
		public bool TryGetQuest(string name, out QuestInstance quest)
		{
			return quests.TryGetValue(name, out quest);
		}

		/// <inheritdoc />
		public void Acquire(QuestTemplate template)
		{
			if (template == null)
			{
				return;
			}

			if (quests.ContainsKey(template.Name))
			{
				return;
			}

			IQuestController.OnQuestAccepted?.Invoke(Character, template);
		}

		/// <inheritdoc />
		public void AdvanceObjective(string questName, int objectiveIndex, long amount)
		{
			if (!quests.TryGetValue(questName, out QuestInstance quest))
			{
				return;
			}
			if (quest.Status != QuestStatus.Active)
			{
				return;
			}
			if (objectiveIndex < 0 || objectiveIndex >= quest.Objectives.Count)
			{
				return;
			}
			if (quest.Objectives[objectiveIndex].IsComplete)
			{
				return;
			}

			IQuestController.OnObjectiveUpdated?.Invoke(Character, questName, objectiveIndex, amount);
		}

		/// <inheritdoc />
		public bool TryCompleteQuest(string questName)
		{
			if (!quests.TryGetValue(questName, out QuestInstance quest))
			{
				return false;
			}
			if (quest.Status != QuestStatus.Active)
			{
				return false;
			}
			if (!quest.AreAllObjectivesComplete())
			{
				return false;
			}

			IQuestController.OnQuestComplete?.Invoke(Character, questName);
			return true;
		}

		/// <inheritdoc />
		public bool TurnInQuest(string questName)
		{
			if (!quests.TryGetValue(questName, out QuestInstance quest))
			{
				return false;
			}
			if (quest.Status != QuestStatus.Complete)
			{
				return false;
			}

			IQuestController.OnQuestTurnedIn?.Invoke(Character, questName);
			return true;
		}

		/// <inheritdoc />
		public bool FailQuest(string questName)
		{
			if (!quests.TryGetValue(questName, out QuestInstance quest))
			{
				return false;
			}
			if (quest.Status != QuestStatus.Active)
			{
				return false;
			}

			IQuestController.OnQuestFailed?.Invoke(Character, questName);
			return true;
		}

		/// <inheritdoc />
		public bool AbandonQuest(string questName)
		{
			if (!quests.TryGetValue(questName, out QuestInstance quest))
			{
				return false;
			}

			IQuestController.OnQuestAbandoned?.Invoke(Character, questName);
			return true;
		}

		/// <inheritdoc />
		public void SetQuest(QuestTemplate template, QuestStatus status, long[] objectiveValues)
		{
			if (template == null)
			{
				return;
			}

			string questName = template.Name;
			if (quests.TryGetValue(questName, out QuestInstance existing))
			{
				existing.TrySetStatus(status);
				for (int i = 0; i < existing.Objectives.Count; i++)
				{
					long value = (objectiveValues != null && i < objectiveValues.Length) ? objectiveValues[i] : 0;
					existing.Objectives[i].SetValue(value);
				}
			}
			else
			{
				QuestInstance instance = new QuestInstance(template, status, objectiveValues);
				quests.Add(questName, instance);
			}
		}
	}
}