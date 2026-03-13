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
			for (int i = 0; i < msg.Quests.Count; i++)
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

			string questName = template.Name;
			if (quests.ContainsKey(questName))
			{
				return;
			}

			QuestInstance instance = new QuestInstance(template);
			quests.Add(questName, instance);

			IQuestController.OnQuestAccepted?.Invoke(Character, instance);
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

			QuestObjectiveInstance objective = quest.Objectives[objectiveIndex];
			if (objective.IsComplete)
			{
				return;
			}

			objective.Increment(amount);

			IQuestController.OnObjectiveUpdated?.Invoke(Character, quest, objectiveIndex);

			if (quest.AreAllObjectivesComplete())
			{
				quest.TrySetStatus(QuestStatus.Complete);
				IQuestController.OnQuestComplete?.Invoke(Character, quest);
			}
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

			quest.TrySetStatus(QuestStatus.Complete);
			IQuestController.OnQuestComplete?.Invoke(Character, quest);
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

			quest.TrySetStatus(QuestStatus.TurnedIn);
			IQuestController.OnQuestTurnedIn?.Invoke(Character, quest);
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

			quest.TrySetStatus(QuestStatus.Failed);
			IQuestController.OnQuestFailed?.Invoke(Character, quest);
			return true;
		}

		/// <inheritdoc />
		public bool AbandonQuest(string questName)
		{
			return quests.Remove(questName);
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