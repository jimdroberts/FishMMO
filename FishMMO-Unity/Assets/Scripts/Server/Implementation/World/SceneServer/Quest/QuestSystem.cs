using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages server-side quest event handling, client synchronisation, reward delivery,
	/// auto-progression, and database persistence for player characters.
	/// Game logic and broadcasts run synchronously on the main thread.
	/// Database persistence is fire-and-forget async via bounded channel workers.
	/// </summary>
	[CreateAssetMenu(fileName = "QuestSystem", menuName = "FishMMO/Server/SceneServer/Quest System", order = 1)]
	[RequiresDataContainer(typeof(QuestSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class QuestSystem : ServerBehaviour, IQuestSystem
	{
		/// <summary>
		/// Debounce window in milliseconds applied to quest operations.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between identical quest operations from the same connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 100;

		/// <summary>
		/// Interval in seconds between bounded ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum number of stale guard entries removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Global per-connection rate limit in milliseconds across all quest operations.
		/// </summary>
		private const int GlobalPerConnectionRateMilliseconds = 30;

		/// <summary>
		/// Cache of world scene details used for scene validation during quest accept/turn-in.
		/// </summary>
		[Header("Scene Validation")]
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>
		/// Operation codes used by ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			/// <summary>Quest accepted operation.</summary>
			QuestAccepted = 1,
			/// <summary>Quest objective updated operation.</summary>
			QuestObjectiveUpdated = 2,
			/// <summary>Quest completed operation.</summary>
			QuestComplete = 3,
			/// <summary>Quest turned in operation.</summary>
			QuestTurnedIn = 4,
			/// <summary>Quest failed operation.</summary>
			QuestFailed = 5,
			/// <summary>Quest abandoned operation.</summary>
			QuestAbandoned = 6,
		}

		/// <summary>
		/// Initializes the quest system, subscribing to all quest lifecycle events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("QuestSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database == null ||
				Server.Database.ServiceRegistry == null)
			{
				Log.Error("QuestSystem", "InitializeOnce: Database or ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ICharacterQuestService>(out _))
			{
				Log.Error("QuestSystem", "InitializeOnce: ICharacterQuestService could not be resolved");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IQuestSystemRuntimeData>(out _))
			{
				Log.Error("QuestSystem", "InitializeOnce: IQuestSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			IQuestController.OnQuestAccepted += OnQuestAccepted;
			IQuestController.OnObjectiveUpdated += OnObjectiveUpdated;
			IQuestController.OnQuestComplete += OnQuestComplete;
			IQuestController.OnQuestTurnedIn += OnQuestTurnedIn;
			IQuestController.OnQuestFailed += OnQuestFailed;
			IQuestController.OnQuestAbandoned += OnQuestAbandoned;

			Server.NetworkWrapper.RegisterBroadcast<QuestAcceptBroadcast>(OnServerQuestAcceptBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<QuestTurnInBroadcast>(OnServerQuestTurnInBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<QuestAbandonBroadcast>(OnServerQuestAbandonBroadcastReceived, true);

			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Log.Debug("QuestSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the quest system, unsubscribing from all quest lifecycle events.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("QuestSystem", "OnDeinitialize: Server is null");
				return;
			}

			IQuestController.OnQuestAccepted -= OnQuestAccepted;
			IQuestController.OnObjectiveUpdated -= OnObjectiveUpdated;
			IQuestController.OnQuestComplete -= OnQuestComplete;
			IQuestController.OnQuestTurnedIn -= OnQuestTurnedIn;
			IQuestController.OnQuestFailed -= OnQuestFailed;
			IQuestController.OnQuestAbandoned -= OnQuestAbandoned;

			Server.NetworkWrapper.UnregisterBroadcast<QuestAcceptBroadcast>(OnServerQuestAcceptBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<QuestTurnInBroadcast>(OnServerQuestTurnInBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<QuestAbandonBroadcast>(OnServerQuestAbandonBroadcastReceived);

			if (Server.DataContainerRegistry.TryGet<IQuestSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Sweeps stale ingress guard entries.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<IQuestSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IQuestSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey, GlobalPerConnectionRateMilliseconds);
		}

		/// <summary>
		/// Releases an ingress in-flight guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IQuestSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		#region Event Handlers

		/// <summary>
		/// Handles a quest being accepted. Creates the quest instance, adds it to the
		/// character's quest log, broadcasts to the client, and persists to DB.
		/// </summary>
		private void OnQuestAccepted(ICharacter character, QuestTemplate template)
		{
			if (character == null || template == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			string questName = template.Name;
			if (questController.Quests.ContainsKey(questName))
			{
				return;
			}

			QuestInstance instance = new QuestInstance(template);
			questController.Quests.Add(questName, instance);

			SendQuestUpdate(playerCharacter, instance);
			PersistQuest(playerCharacter, instance);
		}

		/// <summary>
		/// Handles an objective being updated. Increments the objective, broadcasts and
		/// persists the updated state. Auto-completes the quest if all objectives are met.
		/// </summary>
		private void OnObjectiveUpdated(ICharacter character, string questName, int objectiveIndex, long amount)
		{
			if (character == null || string.IsNullOrEmpty(questName))
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!questController.Quests.TryGetValue(questName, out QuestInstance quest))
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

			// Cap increment amount to prevent overflow or single-call completion
			// exploits. No legitimate gameplay scenario should require incrementing
			// more than the total required value in a single event.
			long requiredValue = objective.Template != null ? objective.Template.RequiredValue : long.MaxValue;
			if (amount > requiredValue)
			{
				amount = requiredValue;
			}

			objective.Increment(amount);

			SendQuestUpdate(playerCharacter, quest);
			PersistQuest(playerCharacter, quest);

			if (quest.AreAllObjectivesComplete())
			{
				quest.TrySetStatus(QuestStatus.Complete);
				SendQuestUpdate(playerCharacter, quest);
				PersistQuest(playerCharacter, quest);
			}
		}

		/// <summary>
		/// Handles all objectives being met. Sets quest status to Complete,
		/// broadcasts to the client, and persists to DB.
		/// </summary>
		private void OnQuestComplete(ICharacter character, string questName)
		{
			if (character == null || string.IsNullOrEmpty(questName))
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!questController.Quests.TryGetValue(questName, out QuestInstance quest))
			{
				return;
			}

			if (quest.Status != QuestStatus.Active)
			{
				return;
			}

			if (!quest.AreAllObjectivesComplete())
			{
				return;
			}

			quest.TrySetStatus(QuestStatus.Complete);
			SendQuestUpdate(playerCharacter, quest);
			PersistQuest(playerCharacter, quest);
		}

		/// <summary>
		/// Handles a quest being turned in. Grants rewards, removes the quest from the
		/// character's log, broadcasts the removal to the client, deletes from DB,
		/// and processes auto-progression.
		/// </summary>
		private void OnQuestTurnedIn(ICharacter character, string questName)
		{
			if (character == null || string.IsNullOrEmpty(questName))
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!questController.Quests.TryGetValue(questName, out QuestInstance quest))
			{
				return;
			}

			if (quest.Status != QuestStatus.Complete)
			{
				return;
			}

			quest.TrySetStatus(QuestStatus.TurnedIn);

			// Grant item rewards
			HandleItemRewards(playerCharacter, quest);

			// Remove the quest from the character's quest log
			questController.Quests.Remove(questName);

			// Remove the quest from the client's quest log
			Server.NetworkWrapper.Broadcast(playerCharacter.Owner, new QuestRemoveBroadcast()
			{
				TemplateID = quest.Template.ID,
			}, true, Channel.Reliable);

			// Delete quest from DB
			DeleteQuest(playerCharacter, quest);

			// Process auto-progression: offer follow-up quests automatically
			HandleAutoProgression(playerCharacter, quest.Template);
		}

		/// <summary>
		/// Handles a quest being failed. Sets quest status to Failed,
		/// broadcasts the updated status to the client, and persists to DB.
		/// </summary>
		private void OnQuestFailed(ICharacter character, string questName)
		{
			if (character == null || string.IsNullOrEmpty(questName))
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!questController.Quests.TryGetValue(questName, out QuestInstance quest))
			{
				return;
			}

			if (quest.Status != QuestStatus.Active)
			{
				return;
			}

			quest.TrySetStatus(QuestStatus.Failed);
			SendQuestUpdate(playerCharacter, quest);
			PersistQuest(playerCharacter, quest);
		}

		/// <summary>
		/// Handles a quest being abandoned. Removes the quest from the character's log,
		/// broadcasts the removal to the client, and deletes from DB.
		/// </summary>
		private void OnQuestAbandoned(ICharacter character, string questName)
		{
			if (character == null || string.IsNullOrEmpty(questName))
			{
				return;
			}

			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter == null || playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			if (!questController.Quests.TryGetValue(questName, out QuestInstance quest))
			{
				return;
			}

			if (quest.Template == null)
			{
				return;
			}

			questController.Quests.Remove(questName);

			Server.NetworkWrapper.Broadcast(playerCharacter.Owner, new QuestRemoveBroadcast()
			{
				TemplateID = quest.Template.ID,
			}, true, Channel.Reliable);

			// Delete quest from DB
			DeleteQuest(playerCharacter, quest);
		}

		#endregion

		#region Client Broadcast Handlers

		/// <summary>
		/// Handles a client request to accept a quest from a QuestInteractable.
		/// </summary>
		private void OnServerQuestAcceptBroadcastReceived(NetworkConnection conn, QuestAcceptBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.QuestAccepted, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				/* Resolve through the shared rule rather than GetComponent. A quest giver is an NPC,
				 * and an NPC is also its own lootable corpse — so component order decided which of
				 * the two answered, and a dead one could answer for a live quest. The resolver
				 * hands back the corpse while the NPC is dead, which is not an IQuestInteractable,
				 * so the request is refused below. CanInteract additionally refuses any non-corpse
				 * interactable on a body.
				 *
				 * The interact rate limit is deliberately not spent here: this path already holds
				 * its own ingress guard, and consuming the limiter would refuse a quest accepted
				 * moments after the interaction that offered it. */
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				if (interactable == null || !interactable.CanInteract(character))
				{
					return;
				}

				IQuestInteractable questInteractable = interactable as IQuestInteractable;
				if (questInteractable == null || questInteractable.QuestTemplates == null)
				{
					return;
				}

				QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
				if (template == null)
				{
					return;
				}

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

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.QuestTurnedIn, out long guardKey))
			{
				return;
			}

			try
			{
				// Validate the scene the character is actually in — see CurrentSceneName.
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.CurrentSceneName(), out _))
				{
					return;
				}

				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				/* Resolve through the shared rule rather than GetComponent. A quest giver is an NPC,
				 * and an NPC is also its own lootable corpse — so component order decided which of
				 * the two answered, and a dead one could answer for a live quest. The resolver
				 * hands back the corpse while the NPC is dead, which is not an IQuestInteractable,
				 * so the request is refused below. CanInteract additionally refuses any non-corpse
				 * interactable on a body.
				 *
				 * The interact rate limit is deliberately not spent here: this path already holds
				 * its own ingress guard, and consuming the limiter would refuse a quest accepted
				 * moments after the interaction that offered it. */
				IInteractable interactable = InteractableResolver.Resolve(sceneObject);
				if (interactable == null || !interactable.CanInteract(character))
				{
					return;
				}

				IQuestInteractable questInteractable = interactable as IQuestInteractable;
				if (questInteractable == null || questInteractable.QuestTemplates == null)
				{
					return;
				}

				QuestTemplate template = QuestTemplate.Get<QuestTemplate>(msg.TemplateID);
				if (template == null)
				{
					return;
				}

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

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();if (character == null)
			{
				return;
			}
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.QuestAbandoned, out long guardKey))
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool ValidateSceneObject(long sceneObjectID, int characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				return false;
			}
			if (sceneObject.GameObject.scene.handle != characterSceneHandle)
			{
				sceneObject = null;
				return false;
			}
			return true;
		}

		#endregion

		#region Broadcasting

		/// <summary>
		/// Broadcasts the current state of a quest to the owning client.
		/// </summary>
		private void SendQuestUpdate(IPlayerCharacter character, QuestInstance quest)
		{
			if (character == null || quest == null || quest.Template == null)
			{
				return;
			}

			long[] objectiveValues = BuildObjectiveValues(quest);

			Server.NetworkWrapper.Broadcast(character.Owner, new QuestUpdateBroadcast()
			{
				TemplateID = quest.Template.ID,
				Status = quest.Status,
				ObjectiveValues = objectiveValues,
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Builds an array of objective progress values from a quest instance.
		/// </summary>
		private long[] BuildObjectiveValues(QuestInstance quest)
		{
			if (quest.Objectives == null || quest.Objectives.Count < 1)
			{
				return Array.Empty<long>();
			}

			long[] values = new long[quest.Objectives.Count];
			for (int i = 0; i < quest.Objectives.Count; i++)
			{
				values[i] = quest.Objectives[i].CurrentValue;
			}
			return values;
		}

		#endregion

		#region DB Persistence

		/// <summary>
		/// Serializes objective values to a comma-separated string for DB storage.
		/// </summary>
		private static string SerializeObjectiveValues(long[] values)
		{
			if (values == null || values.Length == 0)
			{
				return "";
			}
			return string.Join(",", values);
		}

		/// <summary>
		/// Builds a CharacterQuestData DTO from a quest instance and enqueues async persistence.
		/// </summary>
		private void PersistQuest(IPlayerCharacter character, QuestInstance quest)
		{
			if (quest.Template == null)
			{
				return;
			}

			if (!TryGetDbService<ICharacterQuestService>(out var questService))
			{
				return;
			}

			long[] objectiveValues = BuildObjectiveValues(quest);
			quest.Version++;
			long characterID = character.ID;

			var dto = new CharacterQuestData(
				id: 0,
				version: quest.Version,
				characterID: characterID,
				templateID: quest.Template.ID,
				status: (byte)quest.Status,
				objectiveValues: SerializeObjectiveValues(objectiveValues)
			);

			EnqueuePersistence(() => PersistQuestAsync(questService, dto), characterID);
		}

		/// <summary>
		/// Deletes a quest from the DB (turn-in or abandon).
		/// </summary>
		private void DeleteQuest(IPlayerCharacter character, QuestInstance quest)
		{
			if (quest.Template == null)
			{
				return;
			}

			if (!TryGetDbService<ICharacterQuestService>(out var questService))
			{
				return;
			}

			quest.Version++;
			long characterID = character.ID;
			int templateID = quest.Template.ID;
			long version = quest.Version;

			EnqueuePersistence(() => DeleteQuestAsync(questService, characterID, templateID, version), characterID);
		}

		/// <summary>
		/// Asynchronously persists a quest to the database.
		/// </summary>
		private async Task PersistQuestAsync(ICharacterQuestService service, CharacterQuestData dto)
		{
			try
			{
				DatabaseResult result = await service.PersistAsync(new[] { dto });
				if (!result.IsSuccess)
				{
					await Log.Warning("QuestSystem", $"PersistQuestAsync DB error (CharID={dto.CharacterID}, TemplateID={dto.TemplateID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("QuestSystem", $"Error persisting quest (CharID={dto.CharacterID}, TemplateID={dto.TemplateID}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously deletes a quest from the database.
		/// </summary>
		private async Task DeleteQuestAsync(ICharacterQuestService service, long characterID, int templateID, long version)
		{
			try
			{
				DatabaseResult result = await service.DeleteQuestAsync(characterID, templateID, version);
				if (!result.IsSuccess)
				{
					await Log.Warning("QuestSystem", $"DeleteQuestAsync DB error (CharID={characterID}, TemplateID={templateID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("QuestSystem", $"Error deleting quest (CharID={characterID}, TemplateID={templateID}): {ex}");
			}
		}

		#endregion

		#region Rewards

		/// <summary>
		/// Collects all item rewards from the quest template and per-objective rewards,
		/// adds them to the player's inventory (or bank if full), and broadcasts updates.
		/// DB persistence is fire-and-forget async.
		/// </summary>
		private void HandleItemRewards(IPlayerCharacter character, QuestInstance quest)
		{
			QuestTemplate template = quest.Template;

			// Collect all item rewards: template-level + per-objective
			List<BaseItemTemplate> allRewards = new List<BaseItemTemplate>();

			if (template.Rewards != null)
			{
				for (int i = 0; i < template.Rewards.Count; i++)
				{
					if (template.Rewards[i] != null)
					{
						allRewards.Add(template.Rewards[i]);
					}
				}
			}

			if (template.Objectives != null)
			{
				for (int i = 0; i < template.Objectives.Count; i++)
				{
					QuestObjective objective = template.Objectives[i];
					if (objective != null && objective.Rewards != null)
					{
						for (int j = 0; j < objective.Rewards.Count; j++)
						{
							if (objective.Rewards[j] != null)
							{
								allRewards.Add(objective.Rewards[j]);
							}
						}
					}
				}
			}

			if (allRewards.Count < 1)
			{
				return;
			}

			// Resolve DB services for fire-and-forget persistence
			TryGetDbService<ICharacterInventoryService>(out var inventoryService);
			TryGetDbService<ICharacterBankService>(out var bankService);

			// Try inventory first
			if (character.TryGet(out IInventoryController inventoryController) &&
				inventoryController.FreeSlots() >= allRewards.Count)
			{
				GrantItemsToInventory(character, inventoryController, inventoryService, allRewards);
			}
			// Fall back to bank
			else if (character.TryGet(out IBankController bankController) &&
					 bankController.FreeSlots() >= allRewards.Count)
			{
				GrantItemsToBank(character, bankController, bankService, allRewards);
			}
			else
			{
				Log.Warning("QuestSystem", $"Quest item rewards dropped for CharID={character.ID}: both inventory and bank are full ({allRewards.Count} items lost).");
				Server.NetworkWrapper.Broadcast(character.Owner, new ChatBroadcast()
				{
					Channel = ChatChannel.System,
					Text = "Your inventory and bank are full. Quest item rewards could not be delivered.",
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Grants item rewards to the player's inventory and broadcasts updates.
		/// </summary>
		private void GrantItemsToInventory(IPlayerCharacter character, IInventoryController inventoryController,
			ICharacterInventoryService inventoryService, List<BaseItemTemplate> rewards)
		{
			List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();

			for (int i = 0; i < rewards.Count; i++)
			{
				Item newItem = new Item(rewards[i], 1);

				if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems))
				{
					for (int j = 0; j < modifiedItems.Count; j++)
					{
						Item item = modifiedItems[j];
						if (item == null)
						{
							continue;
						}

						if (inventoryService != null)
						{
							item.Version++;
							var dto = new CharacterInventoryData(
								id: item.ID,
								version: item.Version,
								characterID: character.ID,
								templateID: item.Template.ID,
								slot: item.Slot,
								seed: item.IsGenerated ? item.Generator.Seed : 0,
								amount: item.IsStackable ? item.Stackable.Amount : 0
							);
							EnqueuePersistence(() => PersistInventorySlotAsync(inventoryService, dto), character.ID);
						}

						modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
					}
				}
			}

			if (modifiedItemBroadcasts.Count > 0)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
				{
					Items = modifiedItemBroadcasts.ToArray(),
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Grants item rewards to the player's bank and broadcasts updates.
		/// </summary>
		private void GrantItemsToBank(IPlayerCharacter character, IBankController bankController,
			ICharacterBankService bankService, List<BaseItemTemplate> rewards)
		{
			List<BankSetItemBroadcast> modifiedItemBroadcasts = new List<BankSetItemBroadcast>();

			for (int i = 0; i < rewards.Count; i++)
			{
				Item newItem = new Item(rewards[i], 1);

				if (bankController.TryAddItem(newItem, out List<Item> modifiedItems))
				{
					for (int j = 0; j < modifiedItems.Count; j++)
					{
						Item item = modifiedItems[j];
						if (item == null)
						{
							continue;
						}

						if (bankService != null)
						{
							item.Version++;
							var dto = new CharacterBankData(
								id: item.ID,
								version: item.Version,
								characterID: character.ID,
								templateID: item.Template.ID,
								slot: item.Slot,
								seed: item.IsGenerated ? item.Generator.Seed : 0,
								amount: item.IsStackable ? item.Stackable.Amount : 0
							);
							EnqueuePersistence(() => PersistBankSlotAsync(bankService, dto), character.ID);
						}

						modifiedItemBroadcasts.Add(new BankSetItemBroadcast()
						{
							InstanceID = item.ID,
							TemplateID = item.Template.ID,
							Slot = item.Slot,
							Seed = item.IsGenerated ? item.Generator.Seed : 0,
							StackSize = item.IsStackable ? item.Stackable.Amount : 0,
						});
					}
				}
			}

			if (modifiedItemBroadcasts.Count > 0)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
				{
					Items = modifiedItemBroadcasts.ToArray(),
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Processes auto-progression for a turned-in quest, automatically accepting follow-up quests.
		/// </summary>
		private void HandleAutoProgression(IPlayerCharacter character, QuestTemplate template)
		{
			if (template.AutoProgression == null || template.AutoProgression.Count < 1)
			{
				return;
			}

			if (!character.TryGet(out IQuestController questController))
			{
				return;
			}

			for (int i = 0; i < template.AutoProgression.Count; i++)
			{
				QuestTemplate nextQuest = template.AutoProgression[i];
				if (nextQuest == null)
				{
					continue;
				}

				if (!nextQuest.CanAcceptQuest(character))
				{
					continue;
				}

				questController.Acquire(nextQuest);
			}
		}

		#endregion

		#region Async Persistence Helpers

		/// <summary>
		/// Asynchronously persists an inventory item slot to the database.
		/// </summary>
		private async Task PersistInventorySlotAsync(ICharacterInventoryService service, CharacterInventoryData dto)
		{
			try
			{
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("QuestSystem", $"PersistInventorySlotAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("QuestSystem", $"Error persisting inventory slot (CharID={dto.CharacterID}, Slot={dto.Slot}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously persists a bank item slot to the database.
		/// </summary>
		private async Task PersistBankSlotAsync(ICharacterBankService service, CharacterBankData dto)
		{
			try
			{
				DatabaseResult<long> result = await service.PersistAsync(dto);
				if (!result.IsSuccess)
				{
					await Log.Warning("QuestSystem", $"PersistBankSlotAsync DB error (CharID={dto.CharacterID}, Slot={dto.Slot}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("QuestSystem", $"Error persisting bank slot (CharID={dto.CharacterID}, Slot={dto.Slot}): {ex}");
			}
		}

		#endregion
	}
}