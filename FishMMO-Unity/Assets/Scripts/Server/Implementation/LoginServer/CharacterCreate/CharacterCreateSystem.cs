using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages character creation for player accounts, validates character data, and initializes starting equipment and abilities.
	/// All Unity object access (templates, prefabs) occurs on the main thread. Only database operations run asynchronously.
	/// Broadcast replies are marshalled back to the main thread via a thread-safe queue drained in OnLateUpdate.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterCreateSystem", menuName = "FishMMO/Server/LoginServer/Character Create System", order = 1)]
	[RequiresDataContainer(typeof(CharacterCreateSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(CharacterCreateSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterCreateSystem : ServerBehaviour, ICharacterCreateSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread response actions processed per frame.
		/// This time-slices response dispatch to avoid frame spikes during heavy login waves.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max character-create responses drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadResponsesPerFrame = 100;

		/// <summary>
		/// Maximum number of characters allowed per account.
		/// </summary>
		[SerializeField]
		private int maxCharacters = 8;

		/// <summary>
		/// Gets the maximum number of characters allowed per account.
		/// </summary>
		public int MaxCharacters => maxCharacters;
		/// <summary>
		/// Cached world scene details used for validating spawn positions and initial character creation.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache;
		/// <summary>
		/// List of ability templates to grant to new characters on creation.
		/// </summary>
		public List<AbilityTemplate> StartingAbilities = new List<AbilityTemplate>();
		/// <summary>
		/// List of item templates to add to new characters' inventory on creation.
		/// </summary>
		public List<BaseItemTemplate> StartingInventoryItems = new List<BaseItemTemplate>();
		/// <summary>
		/// List of equipment templates to equip on new characters at creation.
		/// </summary>
		public List<EquippableItemTemplate> StartingEquipment = new List<EquippableItemTemplate>();

		/// <summary>
		/// Initializes the character creation system, registering broadcast handlers for character creation requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterCreateSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Verify required data containers
			if (!Server.DataContainerRegistry.TryGet<ICharacterCreateSystemMainThreadQueueData>(out _))
			{
				Log.Error("CharacterCreateSystem", "Failed to initialize: ICharacterCreateSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out _))
			{
				Log.Error("CharacterCreateSystem", "Failed to initialize: CharacterCreateSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived, true);
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			maxCharacters = Mathf.Max(1, maxCharacters);
			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);

			Log.Debug("CharacterCreateSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character creation system, unregistering broadcast handlers for character creation requests.
		/// Drains remaining main-thread responses so clients get their final messages.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterCreateSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);
			if (Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.Clear();
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived);
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			}
		}

		/// <summary>
		/// Handles broadcast to create a new character. Performs Unity API validation
		/// (RaceTemplate.Get, GetComponent, SpawnablePrefabs) on the main thread, then dispatches
		/// DTO construction and database operations to an async worker. All immutable template data
		/// (WorldSceneDetailsCache, StartingAbilities, StartingInventoryItems, StartingEquipment)
		/// is safely read from the worker thread. Responses are marshalled back via the main-thread queue.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CharacterCreateBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCharacterCreateBroadcastReceived(NetworkConnection conn, CharacterCreateBroadcast msg, Channel channel)
		{
			if (!conn.IsActive)
			{
				return;
			}

			// --- Main-thread-only validation ---

			// Validate character name
			if (!Constants.Authentication.IsAllowedCharacterName(msg.CharacterName))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.InvalidCharacterName,
				}, true, Channel.Reliable);
				return;
			}

			// Validate account
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// Validate services are available
			var registry = Server.Database?.ServiceRegistry;
			if (registry == null ||
				!registry.TryGet<ICharacterService>(out var characterService) ||
				!registry.TryGet<ICharacterFactionService>(out var factionService) ||
				!registry.TryGet<ICharacterAbilityService>(out var abilityService) ||
				!registry.TryGet<ICharacterInventoryService>(out var inventoryService) ||
				!registry.TryGet<ICharacterEquipmentService>(out var equipmentService) ||
				!registry.TryGet<ICharacterAttributeService>(out var attributeService) ||
				!registry.TryGet<IUnitOfWorkService>(out var unitOfWorkService))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.Error,
				}, true, Channel.Reliable);
				return;
			}

			// --- Unity API calls that require the main thread ---

			// Validate race template via Unity ScriptableObject lookup
			RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(msg.RaceTemplateID);
			if (raceTemplate == null ||
				raceTemplate.Prefab == null ||
				raceTemplate.GetModelReference(msg.ModelIndex) == null)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// Validate spawnable prefab via Unity API
			IPlayerCharacter characterPrefab = raceTemplate.Prefab.GetComponent<IPlayerCharacter>();
			if (characterPrefab == null ||
				Server.NetworkWrapper.NetworkManager.SpawnablePrefabs.GetObject(true, characterPrefab.NetworkObject.PrefabId) == null)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// --- Dispatch DTO construction + DB work to async (all template data is immutable) ---
			if (!TryBeginCreateRequest(conn))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.Error,
				}, true, Channel.Reliable);
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessCharacterCreateAsync(
				conn, msg, accountName, raceTemplate,
				characterService, factionService, abilityService,
				inventoryService, equipmentService, attributeService,
				unitOfWorkService), conn.ClientId))
			{
				EndCreateRequest(conn);
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.Error,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Processes character creation on a background thread. Validates immutable spawn/race data,
		/// builds all DTOs, performs database operations within a Unit of Work for atomicity,
		/// and marshals responses to the main thread.
		/// All template data (WorldSceneDetailsCache, StartingAbilities, StartingInventoryItems,
		/// StartingEquipment, RaceTemplate) is immutable and safe to read from any thread.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">Original CharacterCreateBroadcast to echo back on success.</param>
		/// <param name="accountName">Validated account name.</param>
		/// <param name="raceTemplate">Validated race template (immutable, main-thread lookup already done).</param>
		/// <param name="characterService">Resolved character service.</param>
		/// <param name="factionService">Resolved faction service.</param>
		/// <param name="abilityService">Resolved ability service.</param>
		/// <param name="inventoryService">Resolved inventory service.</param>
		/// <param name="equipmentService">Resolved equipment service.</param>
		/// <param name="attributeService">Resolved attribute service.</param>
		/// <param name="unitOfWorkService">Resolved unit of work service for transactional consistency.</param>
		private async Task ProcessCharacterCreateAsync(
			NetworkConnection conn,
			CharacterCreateBroadcast msg,
			string accountName,
			RaceTemplate raceTemplate,
			ICharacterService characterService,
			ICharacterFactionService factionService,
			ICharacterAbilityService abilityService,
			ICharacterInventoryService inventoryService,
			ICharacterEquipmentService equipmentService,
			ICharacterAttributeService attributeService,
			IUnitOfWorkService unitOfWorkService)
		{
			try
			{
				// --- Validate immutable spawn data (safe to read off main thread) ---

				if (WorldSceneDetailsCache == null ||
					WorldSceneDetailsCache.Scenes == null ||
					WorldSceneDetailsCache.Scenes.Count < 1)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
							{
								Result = CharacterCreateResult.InvalidSpawn,
							}, true, Channel.Reliable);
						}
					});
					return;
				}

				if (!WorldSceneDetailsCache.Scenes.TryGetValue(msg.SceneName, out WorldSceneDetails details))
				{
					await Log.Debug("CharacterCreateSystem", "Unable to get World Scene Details.");
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
							{
								Result = CharacterCreateResult.InvalidSpawn,
							}, true, Channel.Reliable);
						}
					});
					return;
				}

				if (!details.InitialSpawnPositions.TryGetValue(msg.SpawnerName, out CharacterInitialSpawnPositionDetails initialSpawnPosition))
				{
					await Log.Debug("CharacterCreateSystem", "Unable to find initial spawn position for Spawner.");
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
							{
								Result = CharacterCreateResult.InvalidSpawn,
							}, true, Channel.Reliable);
						}
					});
					return;
				}

				// Validate allowed race against spawn position (immutable data)
				bool validateAllowedRace = false;
				foreach (RaceTemplate t in initialSpawnPosition.AllowedRaces)
				{
					if (t.Name == raceTemplate.Name)
					{
						validateAllowedRace = true;
						break;
					}
				}
				if (!validateAllowedRace)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
						}
					});
					return;
				}

				// --- Check character count limit ---

				DatabaseResult<int> countResult = await characterService.CountAsync(accountName);
				if (!countResult.IsSuccess || countResult.Data >= MaxCharacters)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
							{
								Result = CharacterCreateResult.TooMany,
							}, true, Channel.Reliable);
						}
					});
					return;
				}

				// --- Build all DTOs (immutable template data, safe off main thread) ---

				var characterData = new CharacterData(
					id: 0,
					name: msg.CharacterName,
					nameLowercase: msg.CharacterName?.ToLowerInvariant(),
					account: accountName,
					selected: false,
					worldServerID: 0,
					sceneName: initialSpawnPosition.SceneName,
					sceneHandle: 0,
					bindScene: msg.SceneName,
					bindX: initialSpawnPosition.Position.x,
					bindY: initialSpawnPosition.Position.y,
					bindZ: initialSpawnPosition.Position.z,
					instanceID: 0,
					instanceX: 0f,
					instanceY: 0f,
					instanceZ: 0f,
					instanceRotX: 0f,
					instanceRotY: 0f,
					instanceRotZ: 0f,
					instanceRotW: 0f,
					raceID: msg.RaceTemplateID,
					modelIndex: msg.ModelIndex,
					x: initialSpawnPosition.Position.x,
					y: initialSpawnPosition.Position.y,
					z: initialSpawnPosition.Position.z,
					rotX: initialSpawnPosition.Rotation.x,
					rotY: initialSpawnPosition.Rotation.y,
					rotZ: initialSpawnPosition.Rotation.z,
					rotW: initialSpawnPosition.Rotation.w,
					accessLevel: (byte)AccessLevel.Player,
					online: false,
					flags: 0,
					version: 0,
					timeCreated: DateTime.UtcNow,
					lastSaved: DateTime.UtcNow
				);

				// --- Precompute CPU-only payloads before opening DB transaction ---

				List<PreparedAttributeEntry> preparedAttributes = BuildStartingAttributeEntries(raceTemplate);
				List<PreparedFactionEntry> preparedFactions = BuildStartingFactionEntries(raceTemplate);

				List<PreparedAbilityEntry> preparedAbilities = new List<PreparedAbilityEntry>();
				BuildStartingAbilityEntries(StartingAbilities, preparedAbilities);
				BuildStartingAbilityEntries(raceTemplate.StartingAbilities, preparedAbilities);

				List<PreparedInventoryEntry> preparedInventoryItems = new List<PreparedInventoryEntry>();
				BuildStartingInventoryEntries(StartingInventoryItems, preparedInventoryItems);
				BuildStartingInventoryEntries(raceTemplate.StartingInventoryItems, preparedInventoryItems);

				List<PreparedEquipmentEntry> preparedEquipment = new List<PreparedEquipmentEntry>();
				BuildStartingEquipmentEntries(StartingEquipment, preparedEquipment);
				BuildStartingEquipmentEntries(raceTemplate.StartingEquipment, preparedEquipment);

				// --- Begin Unit of Work for atomic character creation ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterCreateSystem", $"Failed to begin unit of work: {uowResult.ErrorMessage}");
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
							{
								Result = CharacterCreateResult.Error,
							}, true, Channel.Reliable);
						}
					});
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// Create the character row — returns the new ID directly
					DatabaseResult<long> createResult = await characterService.CreateCharacterAsync(characterData);
					if (!createResult.IsSuccess || createResult.Data <= 0)
					{
						CharacterCreateResult clientResult = createResult.ErrorCode switch
						{
							DatabaseErrorCodes.AlreadyExists => CharacterCreateResult.CharacterNameTaken,
							DatabaseErrorCodes.ValidationError => CharacterCreateResult.InvalidCharacterName,
							_ => CharacterCreateResult.Error,
						};
						EnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
								{
									Result = clientResult,
								}, true, Channel.Reliable);
							}
						});
						return;
					}

					long characterID = createResult.Data;

					// Build sub-entity DTOs with the real character ID from precomputed payloads.
					Dictionary<int, CharacterAttributeData> initialAttributes = BuildStartingAttributes(characterID, preparedAttributes);
					List<CharacterFactionData> factions = BuildStartingFactions(characterID, preparedFactions);
					List<CharacterAbilityData> abilities = BuildStartingAbilities(characterID, preparedAbilities);
					List<CharacterInventoryData> inventoryItems = BuildStartingItems(characterID, preparedInventoryItems);
					List<CharacterEquipmentData> equipment = BuildStartingEquipment(characterID, preparedEquipment);

					// --- Persist all sub-entities within the same transaction ---

					if (factions.Count > 0)
					{
						await factionService.PersistAsync(factions);
					}
					if (abilities.Count > 0)
					{
						await abilityService.PersistAsync(abilities);
					}
					if (inventoryItems.Count > 0)
					{
						await inventoryService.PersistAsync(inventoryItems);
					}
					if (equipment.Count > 0)
					{
						await equipmentService.PersistAsync(equipment);
					}
					if (initialAttributes.Count > 0)
					{
						await attributeService.PersistAsync(initialAttributes.Values);
					}

					// All writes succeeded — commit the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterCreateSystem", $"Failed to commit unit of work: {commitResult.ErrorMessage}");
						EnqueueMainThread(() =>
						{
							if (conn != null && conn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
								{
									Result = CharacterCreateResult.Error,
								}, true, Channel.Reliable);
							}
						});
						return;
					}
				}

				// Marshal success response back to main thread
				EnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
						{
							Result = CharacterCreateResult.Success,
						}, true, Channel.Reliable);

						Server.NetworkWrapper.Broadcast(conn, msg, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterCreateSystem", $"ProcessCharacterCreateAsync failed: {ex.Message}");
				EnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
						{
							Result = CharacterCreateResult.Error,
						}, true, Channel.Reliable);
					}
				});
			}
			finally
			{
				EndCreateRequest(conn);
			}
		}

		/// <summary>
		/// Builds CPU-only prepared attribute entries from immutable templates.
		/// </summary>
		private List<PreparedAttributeEntry> BuildStartingAttributeEntries(RaceTemplate raceTemplate)
		{
			var attributes = new List<PreparedAttributeEntry>();
			if (raceTemplate?.InitialAttributes?.Attributes == null)
			{
				return attributes;
			}

			foreach (CharacterAttributeTemplate template in raceTemplate.InitialAttributes.Attributes)
			{
				attributes.Add(new PreparedAttributeEntry(template.ID, template.InitialValue, template.IsResourceAttribute));
			}

			return attributes;
		}

		/// <summary>
		/// Builds CPU-only prepared faction entries from immutable templates.
		/// </summary>
		private List<PreparedFactionEntry> BuildStartingFactionEntries(RaceTemplate raceTemplate)
		{
			var factions = new List<PreparedFactionEntry>();
			if (raceTemplate?.InitialFaction == null)
			{
				return factions;
			}

			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultAllied)
			{
				factions.Add(new PreparedFactionEntry(faction.ID, FactionTemplate.Maximum));
			}

			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultNeutral)
			{
				factions.Add(new PreparedFactionEntry(faction.ID, 0));
			}

			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultHostile)
			{
				factions.Add(new PreparedFactionEntry(faction.ID, FactionTemplate.Minimum));
			}

			return factions;
		}

		/// <summary>
		/// Builds CPU-only prepared ability entries from immutable templates.
		/// </summary>
		private void BuildStartingAbilityEntries(List<AbilityTemplate> startingAbilities, List<PreparedAbilityEntry> abilities)
		{
			if (startingAbilities == null)
			{
				return;
			}

			foreach (AbilityTemplate startingAbility in startingAbilities)
			{
				abilities.Add(new PreparedAbilityEntry(startingAbility.ID, startingAbility.GetAllAbilityEventIDs()));
			}
		}

		/// <summary>
		/// Builds CPU-only prepared inventory entries from immutable templates.
		/// </summary>
		private void BuildStartingInventoryEntries(List<BaseItemTemplate> startingItems, List<PreparedInventoryEntry> items)
		{
			if (startingItems == null)
			{
				return;
			}

			int slotOffset = items.Count;
			for (int i = 0; i < startingItems.Count; ++i)
			{
				BaseItemTemplate itemTemplate = startingItems[i];
				items.Add(new PreparedInventoryEntry(itemTemplate.ID, slotOffset + i));
			}
		}

		/// <summary>
		/// Builds CPU-only prepared equipment entries from immutable templates.
		/// Item seed generation occurs before opening the DB transaction.
		/// </summary>
		private void BuildStartingEquipmentEntries(List<EquippableItemTemplate> startingEquipment, List<PreparedEquipmentEntry> equipment)
		{
			if (startingEquipment == null)
			{
				return;
			}

			for (int i = 0; i < startingEquipment.Count; ++i)
			{
				EquippableItemTemplate itemTemplate = startingEquipment[i];

				ItemGenerator itemGenerator = new ItemGenerator();
				itemGenerator.Generate(1, itemTemplate);

				equipment.Add(new PreparedEquipmentEntry(itemTemplate.ID, (int)itemTemplate.Slot, itemGenerator.Seed));
			}
		}

		/// <summary>
		/// Builds starting attribute DTOs using prepared template data and a concrete character ID.
		/// </summary>
		private Dictionary<int, CharacterAttributeData> BuildStartingAttributes(long characterID, List<PreparedAttributeEntry> preparedAttributes)
		{
			Dictionary<int, CharacterAttributeData> attributes = new Dictionary<int, CharacterAttributeData>(preparedAttributes?.Count ?? 0);
			if (preparedAttributes == null)
			{
				return attributes;
			}

			for (int i = 0; i < preparedAttributes.Count; ++i)
			{
				PreparedAttributeEntry prepared = preparedAttributes[i];
				attributes[prepared.TemplateID] = new CharacterAttributeData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: prepared.TemplateID,
					value: prepared.Value,
					currentValue: prepared.IsResourceAttribute ? prepared.Value : 0.0f
				);
			}

			return attributes;
		}

		/// <summary>
		/// Builds starting faction data for a newly created character from prepared entries.
		/// </summary>
		/// <param name="characterID">ID of the newly created character.</param>
		/// <param name="preparedFactions">Prepared faction entries.</param>
		/// <returns>List of faction data objects to persist.</returns>
		private List<CharacterFactionData> BuildStartingFactions(long characterID, List<PreparedFactionEntry> preparedFactions)
		{
			var factions = new List<CharacterFactionData>(preparedFactions?.Count ?? 0);
			if (preparedFactions == null)
			{
				return factions;
			}

			for (int i = 0; i < preparedFactions.Count; ++i)
			{
				PreparedFactionEntry faction = preparedFactions[i];
				factions.Add(new CharacterFactionData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: faction.TemplateID,
					value: faction.Value
				));
			}

			return factions;
		}

		/// <summary>
		/// Builds starting ability data for a newly created character from prepared entries.
		/// </summary>
		/// <param name="characterID">ID of the character to add abilities to.</param>
		/// <param name="preparedAbilities">Prepared ability entries.</param>
		private List<CharacterAbilityData> BuildStartingAbilities(long characterID, List<PreparedAbilityEntry> preparedAbilities)
		{
			var abilities = new List<CharacterAbilityData>(preparedAbilities?.Count ?? 0);
			if (preparedAbilities == null)
			{
				return abilities;
			}

			for (int i = 0; i < preparedAbilities.Count; ++i)
			{
				PreparedAbilityEntry startingAbility = preparedAbilities[i];
				abilities.Add(new CharacterAbilityData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: startingAbility.TemplateID,
					abilityEvents: startingAbility.AbilityEvents,
					cooldown: 0f
				));
			}

			return abilities;
		}

		/// <summary>
		/// Builds starting inventory item data for a newly created character from prepared entries.
		/// </summary>
		/// <param name="characterID">ID of the character to add items to.</param>
		/// <param name="preparedItems">Prepared inventory entries.</param>
		private List<CharacterInventoryData> BuildStartingItems(long characterID, List<PreparedInventoryEntry> preparedItems)
		{
			var items = new List<CharacterInventoryData>(preparedItems?.Count ?? 0);
			if (preparedItems == null)
			{
				return items;
			}

			for (int i = 0; i < preparedItems.Count; ++i)
			{
				PreparedInventoryEntry itemTemplate = preparedItems[i];
				items.Add(new CharacterInventoryData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: itemTemplate.TemplateID,
					slot: itemTemplate.Slot,
					seed: 0,
					amount: 1
				));
			}

			return items;
		}

		/// <summary>
		/// Builds starting equipment data for a newly created character from prepared entries.
		/// Equipment attribute bonuses are not baked into base attribute values;
		/// they are applied at runtime via Equip() on character load.
		/// </summary>
		/// <param name="characterID">ID of the character to add equipment to.</param>
		/// <param name="preparedEquipment">Prepared equipment entries.</param>
		private List<CharacterEquipmentData> BuildStartingEquipment(long characterID, List<PreparedEquipmentEntry> preparedEquipment)
		{
			var equipment = new List<CharacterEquipmentData>(preparedEquipment?.Count ?? 0);
			if (preparedEquipment == null)
			{
				return equipment;
			}

			for (int i = 0; i < preparedEquipment.Count; ++i)
			{
				PreparedEquipmentEntry itemTemplate = preparedEquipment[i];
				equipment.Add(new CharacterEquipmentData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: itemTemplate.TemplateID,
					slot: itemTemplate.Slot,
					seed: itemTemplate.Seed,
					amount: 0
				));
			}

			return equipment;
		}

		/// <summary>
		/// Drains the main-thread response queue each frame.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<ICharacterCreateSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadResponsesPerFrame);
				}
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<ICharacterCreateSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		/// <param name="work">The async work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for consistent worker routing.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns><c>true</c> if the work item was enqueued; otherwise, <c>false</c>.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					return asyncWorker.Enqueue(work, entityKey, callerName);
				else
					return asyncWorker.Enqueue(work, callerName);
			}

			return false;
		}

		/// <summary>
		/// Attempts to mark a create request as in-flight for the connection.
		/// </summary>
		/// <param name="conn">Requesting connection.</param>
		/// <returns><c>true</c> if the in-flight slot was acquired; otherwise <c>false</c>.</returns>
		private bool TryBeginCreateRequest(NetworkConnection conn)
		{
			if (conn == null)
			{
				return false;
			}

			return Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData) &&
				runtimeData.InFlightRequests.TryAdd(conn.ClientId, 0);
		}

		/// <summary>
		/// Releases the in-flight create request slot for a connection.
		/// </summary>
		/// <param name="conn">Connection to release.</param>
		private void EndCreateRequest(NetworkConnection conn)
		{
			if (conn != null &&
				Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
			}
		}

		/// <summary>
		/// Releases per-connection in-flight create state when a client disconnects.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				conn != null &&
				Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
			}
		}

		/// <summary>
		/// Prepared immutable attribute payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedAttributeEntry
		{
			public readonly int TemplateID;
			public readonly int Value;
			public readonly bool IsResourceAttribute;

			public PreparedAttributeEntry(int templateID, int value, bool isResourceAttribute)
			{
				TemplateID = templateID;
				Value = value;
				IsResourceAttribute = isResourceAttribute;
			}
		}

		/// <summary>
		/// Prepared immutable faction payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedFactionEntry
		{
			public readonly int TemplateID;
			public readonly int Value;

			public PreparedFactionEntry(int templateID, int value)
			{
				TemplateID = templateID;
				Value = value;
			}
		}

		/// <summary>
		/// Prepared immutable ability payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedAbilityEntry
		{
			public readonly int TemplateID;
			public readonly List<int> AbilityEvents;

			public PreparedAbilityEntry(int templateID, List<int> abilityEvents)
			{
				TemplateID = templateID;
				AbilityEvents = abilityEvents;
			}
		}

		/// <summary>
		/// Prepared immutable inventory payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedInventoryEntry
		{
			public readonly int TemplateID;
			public readonly int Slot;

			public PreparedInventoryEntry(int templateID, int slot)
			{
				TemplateID = templateID;
				Slot = slot;
			}
		}

		/// <summary>
		/// Prepared immutable equipment payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedEquipmentEntry
		{
			public readonly int TemplateID;
			public readonly int Slot;
			public readonly int Seed;

			public PreparedEquipmentEntry(int templateID, int slot, int seed)
			{
				TemplateID = templateID;
				Slot = slot;
				Seed = seed;
			}
		}
	}
}