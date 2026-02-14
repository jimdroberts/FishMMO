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
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterCreateSystem : ServerBehaviour, ICharacterCreateSystem
	{
		/// <summary>
		/// Maximum number of characters allowed per account.
		/// </summary>
		[SerializeField]
		private int maxCharacters = 8;

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

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived, true);

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
			DrainMainThreadQueue();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<CharacterCreateBroadcast>(OnServerCharacterCreateBroadcastReceived);
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
			EnqueueAsyncWork(() => ProcessCharacterCreateAsync(
				conn, msg, accountName, raceTemplate,
				characterService, factionService, abilityService,
				inventoryService, equipmentService, attributeService,
				unitOfWorkService));
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
					return;
				}

				if (!details.InitialSpawnPositions.TryGetValue(msg.SpawnerName, out CharacterInitialSpawnPositionDetails initialSpawnPosition))
				{
					await Log.Debug("CharacterCreateSystem", "Unable to find initial spawn position for Spawner.");
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
					nameLowercase: msg.CharacterName?.ToLower(),
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

					// Build sub-entity DTOs with the real character ID (immutable templates)
					Dictionary<int, CharacterAttributeData> initialAttributes = new Dictionary<int, CharacterAttributeData>();
					if (raceTemplate.InitialAttributes != null &&
						raceTemplate.InitialAttributes.Attributes.Count > 0)
					{
						foreach (CharacterAttributeTemplate template in raceTemplate.InitialAttributes.Attributes)
						{
							initialAttributes.Add(template.ID, new CharacterAttributeData(
								id: 0,
								version: 1,
								characterID: characterID,
								templateID: template.ID,
								value: template.InitialValue,
								currentValue: template.IsResourceAttribute ? template.InitialValue : 0.0f
							));
						}
					}

					List<CharacterFactionData> factions = BuildStartingFactions(characterID, raceTemplate);

					List<CharacterAbilityData> abilities = new List<CharacterAbilityData>();
					BuildStartingAbilities(characterID, StartingAbilities, abilities);
					BuildStartingAbilities(characterID, raceTemplate.StartingAbilities, abilities);

					List<CharacterInventoryData> inventoryItems = new List<CharacterInventoryData>();
					BuildStartingItems(characterID, StartingInventoryItems, inventoryItems);
					BuildStartingItems(characterID, raceTemplate.StartingInventoryItems, inventoryItems);

					List<CharacterEquipmentData> equipment = new List<CharacterEquipmentData>();
					BuildStartingEquipment(characterID, StartingEquipment, equipment);
					BuildStartingEquipment(characterID, raceTemplate.StartingEquipment, equipment);

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
		}

		/// <summary>
		/// Builds starting faction data for a newly created character based on race template.
		/// </summary>
		/// <param name="characterID">ID of the newly created character.</param>
		/// <param name="raceTemplate">Race template containing initial faction definitions.</param>
		/// <returns>List of faction data objects to persist.</returns>
		private List<CharacterFactionData> BuildStartingFactions(long characterID, RaceTemplate raceTemplate)
		{
			var factions = new List<CharacterFactionData>();
			if (raceTemplate.InitialFaction == null)
			{
				return factions;
			}
			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultAllied)
			{
				factions.Add(new CharacterFactionData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: faction.ID,
					value: FactionTemplate.Maximum
				));
			}
			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultNeutral)
			{
				factions.Add(new CharacterFactionData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: faction.ID,
					value: 0
				));
			}
			foreach (FactionTemplate faction in raceTemplate.InitialFaction.DefaultHostile)
			{
				factions.Add(new CharacterFactionData(
					id: 0,
					version: 1,
					characterID: characterID,
					templateID: faction.ID,
					value: FactionTemplate.Minimum
				));
			}
			return factions;
		}

		/// <summary>
		/// Builds starting ability data for a newly created character.
		/// </summary>
		/// <param name="characterID">ID of the character to add abilities to.</param>
		/// <param name="startingAbilities">List of ability templates to add.</param>
		/// <param name="abilities">Target list to append ability data to.</param>
		private void BuildStartingAbilities(long characterID, List<AbilityTemplate> startingAbilities, List<CharacterAbilityData> abilities)
		{
			if (startingAbilities != null)
			{
				foreach (AbilityTemplate startingAbility in startingAbilities)
				{
					abilities.Add(new CharacterAbilityData(
						id: 0,
						version: 1,
						characterID: characterID,
						templateID: startingAbility.ID,
						abilityEvents: startingAbility.GetAllAbilityEventIDs(),
						cooldown: 0f
					));
				}
			}
		}

		/// <summary>
		/// Builds starting inventory item data for a newly created character.
		/// </summary>
		/// <param name="characterID">ID of the character to add items to.</param>
		/// <param name="startingItems">List of item templates to add.</param>
		/// <param name="items">Target list to append inventory data to.</param>
		private void BuildStartingItems(long characterID, List<BaseItemTemplate> startingItems, List<CharacterInventoryData> items)
		{
			if (startingItems != null)
			{
				int slotOffset = items.Count;
				for (int i = 0; i < startingItems.Count; ++i)
				{
					BaseItemTemplate itemTemplate = startingItems[i];
					items.Add(new CharacterInventoryData(
						id: 0,
						version: 1,
						characterID: characterID,
						templateID: itemTemplate.ID,
						slot: slotOffset + i,
						seed: 0,
						amount: 1
					));
				}
			}
		}

		/// <summary>
		/// Builds starting equipment data for a newly created character.
		/// Equipment attribute bonuses are not baked into base attribute values;
		/// they are applied at runtime via Equip() on character load.
		/// </summary>
		/// <param name="characterID">ID of the character to add equipment to.</param>
		/// <param name="startingEquipment">List of equipment templates to add.</param>
		/// <param name="equipment">Target list to append equipment data to.</param>
		private void BuildStartingEquipment(long characterID, List<EquippableItemTemplate> startingEquipment, List<CharacterEquipmentData> equipment)
		{
			if (startingEquipment != null)
			{
				for (int i = 0; i < startingEquipment.Count; ++i)
				{
					EquippableItemTemplate itemTemplate = startingEquipment[i];

					// Generate the item seed for deterministic attribute generation on load
					ItemGenerator itemGenerator = new ItemGenerator();
					itemGenerator.Generate(1, itemTemplate);

					// Add the equipped item data
					equipment.Add(new CharacterEquipmentData(
						id: 0,
						version: 1,
						characterID: characterID,
						templateID: itemTemplate.ID,
						slot: (int)itemTemplate.Slot,
						seed: itemGenerator.Seed,
						amount: 0
					));
				}
			}
		}

		/// <summary>
		/// Drains the main-thread response queue each frame.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue();
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<ICharacterCreateSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Drain();
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
		private void EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					asyncWorker.Enqueue(work, entityKey, callerName);
				else
					asyncWorker.Enqueue(work, callerName);
			}
		}
	}
}