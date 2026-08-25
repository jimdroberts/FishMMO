using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
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
		/// Maximum allowed length for scene and spawner name fields from client messages.
		/// </summary>
		[Tooltip("Max allowed length for scene and spawner name fields")]
		[SerializeField] private int maxSceneFieldLength = 256;

		/// <summary>
		/// Maximum number of characters allowed per account.
		/// </summary>
		[SerializeField]
		[Tooltip("Maximum number of characters allowed per account.")]
		private int maxCharacters = 8;

		/// <summary>
		/// Cooldown in milliseconds between character-create requests per connection.
		/// Prevents sequential spam even after the in-flight guard releases.
		/// </summary>
		[Tooltip("Cooldown in milliseconds between create requests per connection")]
		[SerializeField] private int createRequestCooldownMilliseconds = 2000;

		/// <summary>
		/// Gets the maximum number of characters allowed per account.
		/// </summary>
		public int MaxCharacters => maxCharacters;
		/// <summary>
		/// Cached world scene details used for validating spawn positions and initial character creation.
		/// </summary>
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;
		/// <summary>
		/// List of ability template IDs to grant to new characters on creation.
		/// </summary>
		[SerializeField, TemplateReference(typeof(AbilityTemplate))] private List<int> startingAbilityIDs = new List<int>();
		/// <summary>
		/// List of item template IDs to add to new characters' inventory on creation.
		/// </summary>
		[SerializeField, TemplateReference(typeof(BaseItemTemplate))] private List<int> startingInventoryItemIDs = new List<int>();
		/// <summary>
		/// List of equipment template IDs to equip on new characters at creation.
		/// </summary>
		[SerializeField, TemplateReference(typeof(EquippableItemTemplate))] private List<int> startingEquipmentIDs = new List<int>();

		/// <summary>
		/// Read-only access to the cached world scene details.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache => worldSceneDetailsCache;
		/// <summary>
		/// Read-only view of the starting ability template IDs for new characters.
		/// </summary>
		public IReadOnlyList<int> StartingAbilityIDs => startingAbilityIDs;
		/// <summary>
		/// Read-only view of the starting inventory item template IDs for new characters.
		/// </summary>
		public IReadOnlyList<int> StartingInventoryItemIDs => startingInventoryItemIDs;
		/// <summary>
		/// Read-only view of the starting equipment template IDs for new characters.
		/// </summary>
		public IReadOnlyList<int> StartingEquipmentIDs => startingEquipmentIDs;

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
			SubscribeToConnectionEvents();

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
			UnsubscribeFromConnectionEvents();
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
			// Null guard: msg.CharacterName could be null if the client sends a
			// malformed broadcast.  Authentication.IsAllowedCharacterName would throw
			// NRE on null input, so we short-circuit here.
			if (msg.CharacterName == null || !Authentication.IsAllowedCharacterName(msg.CharacterName))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.InvalidCharacterName,
				}, true, Channel.Reliable);
				return;
			}

			// Validate string field lengths to prevent oversized allocations.
			if (string.IsNullOrWhiteSpace(msg.SceneName) || msg.SceneName.Length > maxSceneFieldLength ||
				string.IsNullOrWhiteSpace(msg.SpawnerName) || msg.SpawnerName.Length > maxSceneFieldLength)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);
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
				characterPrefab.NetworkObject == null ||
				Server.NetworkWrapper.NetworkManager.SpawnablePrefabs.GetObject(true, characterPrefab.NetworkObject.PrefabId) == null)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// --- Extract all template data on the main thread ---
			// ScriptableObject property accessors can trigger Unity's GetName()
			// main-thread assertion in certain Unity versions.  Extract everything
			// into plain C# objects now so the async worker never touches Unity types.
			var preparedAttributes = BuildStartingAttributeEntries(raceTemplate);
			var preparedFactions   = BuildStartingFactionEntries(raceTemplate);
			var preparedAbilities  = new List<PreparedAbilityEntry>();
			BuildStartingAbilityEntries(startingAbilityIDs, preparedAbilities);
			BuildStartingAbilityEntries(raceTemplate.StartingAbilities, preparedAbilities);
			var preparedInventory  = new List<PreparedInventoryEntry>();
			BuildStartingInventoryEntries(startingInventoryItemIDs, preparedInventory);
			BuildStartingInventoryEntries(raceTemplate.StartingInventoryItems, preparedInventory);
			var preparedEquipment  = new List<PreparedEquipmentEntry>();
			BuildStartingEquipmentEntries(startingEquipmentIDs, preparedEquipment);
			BuildStartingEquipmentEntries(raceTemplate.StartingEquipment, preparedEquipment);

			// Extract string data from Unity ScriptableObjects on the main thread
			// so the async worker never touches Unity types.
			string raceTemplateName = raceTemplate.Name;

			// --- Extract spawn position data on the main thread ---
			// worldSceneDetailsCache is a ScriptableObject; access it here rather
			// than in the async worker to avoid Unity main-thread assertions.
			if (worldSceneDetailsCache == null ||
				worldSceneDetailsCache.Scenes == null ||
				worldSceneDetailsCache.Scenes.Count < 1)
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.InvalidSpawn,
				}, true, Channel.Reliable);
				return;
			}

			if (!worldSceneDetailsCache.Scenes.TryGetValue(msg.SceneName, out WorldSceneDetails sceneDetails))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.InvalidSpawn,
				}, true, Channel.Reliable);
				return;
			}

			if (!sceneDetails.InitialSpawnPositions.TryGetValue(msg.SpawnerName, out CharacterInitialSpawnPositionDetails spawnDetails))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.InvalidSpawn,
				}, true, Channel.Reliable);
				return;
			}

			// Validate allowed race against spawn position (main thread).
			bool raceAllowed = false;
			foreach (RaceTemplate t in spawnDetails.AllowedRaces)
			{
				if (string.Equals(t.Name, raceTemplateName, StringComparison.Ordinal))
				{
					raceAllowed = true;
					break;
				}
			}
			if (!raceAllowed)
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			// Extract plain C# data from the ScriptableObject for the async worker.
			var preparedSpawn = new PreparedSpawnDetails(
				spawnDetails.SceneName,
				spawnDetails.Position.x, spawnDetails.Position.y, spawnDetails.Position.z,
				spawnDetails.Rotation.x, spawnDetails.Rotation.y, spawnDetails.Rotation.z, spawnDetails.Rotation.w
			);

			// --- Dispatch DTO construction + DB work to async ---
			if (!TryBeginCreateRequest(conn))
			{
				Server.NetworkWrapper.Broadcast(conn, new CharacterCreateResultBroadcast()
				{
					Result = CharacterCreateResult.Error,
				}, true, Channel.Reliable);
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessCharacterCreateAsync(
				conn, msg, accountName,
				raceTemplateName,
				preparedSpawn,
				preparedAttributes, preparedFactions, preparedAbilities,
				preparedInventory, preparedEquipment,
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
		/// All template data (Attributes, Factions, Abilities, Inventory, Equipment) is
		/// pre-extracted into plain C# objects on the main thread before dispatch.
		/// <paramref name="raceTemplateName"/> is the string name from the ScriptableObject,
		/// extracted on the main thread — Unity objects are never accessed from this worker.
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
			string raceTemplateName,
			PreparedSpawnDetails preparedSpawn,
			List<PreparedAttributeEntry> preparedAttributes,
			List<PreparedFactionEntry> preparedFactions,
			List<PreparedAbilityEntry> preparedAbilities,
			List<PreparedInventoryEntry> preparedInventory,
			List<PreparedEquipmentEntry> preparedEquipment,
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
				// --- Spawn position data was pre-extracted on the main thread ---
				// All ScriptableObject access (worldSceneDetailsCache, RaceTemplate lookups)
				// was completed before dispatch. Use the prepared plain-C# data.

				// --- Check character count limit ---

				DatabaseResult<int> countResult = await characterService.CountAsync(accountName);
				if (!countResult.IsSuccess || countResult.Data >= MaxCharacters)
				{
					TryEnqueueMainThread(() =>
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
					sceneName: preparedSpawn.SceneName,
					sceneHandle: 0,
					bindScene: msg.SceneName,
					bindX: preparedSpawn.PosX,
					bindY: preparedSpawn.PosY,
					bindZ: preparedSpawn.PosZ,
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
					x: preparedSpawn.PosX,
					y: preparedSpawn.PosY,
					z: preparedSpawn.PosZ,
					rotX: preparedSpawn.RotX,
					rotY: preparedSpawn.RotY,
					rotZ: preparedSpawn.RotZ,
					rotW: preparedSpawn.RotW,
					accessLevel: (byte)AccessLevel.Player,
					online: false,
					flags: 0,
					version: 0,
					timeCreated: DateTime.UtcNow,
					lastSaved: DateTime.UtcNow
				);

				// Precomputed payloads were extracted on the main thread before dispatch.

				// --- Begin Unit of Work for atomic character creation ---

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					await Log.Error("CharacterCreateSystem", $"Failed to begin unit of work: [{uowResult.ErrorCode}] {uowResult.ErrorMessage}");
					TryEnqueueMainThread(() =>
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
						TryEnqueueMainThread(() =>
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
					List<CharacterInventoryData> inventoryItems = BuildStartingItems(characterID, preparedInventory);
					List<CharacterEquipmentData> equipment = BuildStartingEquipment(characterID, preparedEquipment);

					// --- Persist all sub-entities within the same transaction ---

					if (factions.Count > 0)
					{
						/* Must land in full. These rows are being created for the first time, so
						 * nothing newer can exist to supersede them — a short write means rows
						 * were dropped, and a character handed to the player missing part of its
						 * starting factions is worse than a failed creation they can retry. */
						if (!await BulkWriteReporting.RequireCompleteAsync(
								"CharacterCreateSystem", "Starting factions", await factionService.PersistAsync(factions), $"character {characterID}"))
						{
							TryEnqueueMainThread(() =>
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
					if (abilities.Count > 0)
					{
						/* Must land in full. These rows are being created for the first time, so
						 * nothing newer can exist to supersede them — a short write means rows
						 * were dropped, and a character handed to the player missing part of its
						 * starting abilities is worse than a failed creation they can retry. */
						if (!await BulkWriteReporting.RequireCompleteAsync(
								"CharacterCreateSystem", "Starting abilities", await abilityService.PersistAsync(abilities), $"character {characterID}"))
						{
							TryEnqueueMainThread(() =>
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
					if (inventoryItems.Count > 0)
					{
						/* Must land in full. These rows are being created for the first time, so
						 * nothing newer can exist to supersede them — a short write means rows
						 * were dropped, and a character handed to the player missing part of its
						 * starting inventory is worse than a failed creation they can retry. */
						if (!await BulkWriteReporting.RequireCompleteAsync(
								"CharacterCreateSystem", "Starting inventory", await inventoryService.PersistAsync(inventoryItems), $"character {characterID}"))
						{
							TryEnqueueMainThread(() =>
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
					if (equipment.Count > 0)
					{
						/* Must land in full. These rows are being created for the first time, so
						 * nothing newer can exist to supersede them — a short write means rows
						 * were dropped, and a character handed to the player missing part of its
						 * starting equipment is worse than a failed creation they can retry. */
						if (!await BulkWriteReporting.RequireCompleteAsync(
								"CharacterCreateSystem", "Starting equipment", await equipmentService.PersistAsync(equipment), $"character {characterID}"))
						{
							TryEnqueueMainThread(() =>
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
					if (initialAttributes.Count > 0)
					{
						/* Must land in full. These rows are being created for the first time, so
						 * nothing newer can exist to supersede them — a short write means rows
						 * were dropped, and a character handed to the player missing part of its
						 * starting attributes is worse than a failed creation they can retry. */
						if (!await BulkWriteReporting.RequireCompleteAsync(
								"CharacterCreateSystem", "Starting attributes", await attributeService.PersistAsync(initialAttributes.Values), $"character {characterID}"))
						{
							TryEnqueueMainThread(() =>
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

					// All writes succeeded — commit the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Error("CharacterCreateSystem", $"Failed to commit unit of work: [{commitResult.ErrorCode}] {commitResult.ErrorMessage}");
						TryEnqueueMainThread(() =>
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
				TryEnqueueMainThread(() =>
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
				await Log.Error("CharacterCreateSystem", $"ProcessCharacterCreateAsync failed: {ex}");
				TryEnqueueMainThread(() =>
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
		/// Builds CPU-only prepared ability entries from template IDs.
		/// </summary>
		private void BuildStartingAbilityEntries(List<int> templateIDs, List<PreparedAbilityEntry> abilities)
		{
			if (templateIDs == null)
			{
				return;
			}

			foreach (int id in templateIDs)
			{
				AbilityTemplate startingAbility = AbilityTemplate.Get<AbilityTemplate>(id);
				if (startingAbility != null)
				{
					abilities.Add(new PreparedAbilityEntry(startingAbility.ID, startingAbility.GetAllAbilityEventIDs()));
				}
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
		/// Builds CPU-only prepared inventory entries from template IDs.
		/// </summary>
		private void BuildStartingInventoryEntries(List<int> templateIDs, List<PreparedInventoryEntry> items)
		{
			if (templateIDs == null)
			{
				return;
			}

			int slotOffset = items.Count;
			for (int i = 0; i < templateIDs.Count; ++i)
			{
				BaseItemTemplate itemTemplate = BaseItemTemplate.Get<BaseItemTemplate>(templateIDs[i]);
				if (itemTemplate != null)
				{
					items.Add(new PreparedInventoryEntry(itemTemplate.ID, slotOffset + i));
				}
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
		/// Builds CPU-only prepared equipment entries from template IDs.
		/// Item seed generation occurs before opening the DB transaction.
		/// </summary>
		private void BuildStartingEquipmentEntries(List<int> templateIDs, List<PreparedEquipmentEntry> equipment)
		{
			if (templateIDs == null)
			{
				return;
			}

			for (int i = 0; i < templateIDs.Count; ++i)
			{
				EquippableItemTemplate itemTemplate = EquippableItemTemplate.Get<EquippableItemTemplate>(templateIDs[i]);
				if (itemTemplate != null)
				{
					ItemGenerator itemGenerator = new ItemGenerator();
					itemGenerator.Generate(1, itemTemplate);

					equipment.Add(new PreparedEquipmentEntry(itemTemplate.ID, (int)itemTemplate.Slot, itemGenerator.Seed));
				}
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
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Drains the main-thread queue via the base class generic helper.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<ICharacterCreateSystemMainThreadQueueData>(maxMainThreadResponsesPerFrame, drainAll);
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the base class generic helper.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ICharacterCreateSystemMainThreadQueueData>(action);
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

			if (!Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}

			// Debounce — reject if cooldown hasn't elapsed since the last completed request.
			DateTime nowUtc = DateTime.UtcNow;
			if (runtimeData.NextAllowedCreateUtcByClientId.TryGetValue(conn.ClientId, out DateTime nextAllowed) && nowUtc < nextAllowed)
			{
				return false;
			}

			return runtimeData.InFlightRequests.TryAdd(conn.ClientId, 0);
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
				runtimeData.NextAllowedCreateUtcByClientId[conn.ClientId] = DateTime.UtcNow.AddMilliseconds(createRequestCooldownMilliseconds);
			}
		}

		/// <summary>
		/// Releases per-connection in-flight create state when a client disconnects.
		/// </summary>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.DataContainerRegistry.TryGet<CharacterCreateSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InFlightRequests.TryRemove(conn.ClientId, out _);
				runtimeData.NextAllowedCreateUtcByClientId.TryRemove(conn.ClientId, out _);
			}
		}

		/// <summary>
		/// Prepared immutable attribute payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedAttributeEntry
		{
			/// <summary>The character attribute template ID.</summary>
			public readonly int TemplateID;
			/// <summary>The initial value for this attribute.</summary>
			public readonly int Value;
			/// <summary>Whether this attribute is a resource attribute (e.g. health, mana).</summary>
			public readonly bool IsResourceAttribute;

			/// <summary>
			/// Initializes a new attribute entry with the given settings.
			/// </summary>
			/// <param name="templateID">The character attribute template ID.</param>
			/// <param name="value">The initial value for this attribute.</param>
			/// <param name="isResourceAttribute">Whether this attribute is a resource attribute.</param>
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
			/// <summary>The faction template ID.</summary>
			public readonly int TemplateID;
			/// <summary>The initial faction standing value.</summary>
			public readonly int Value;

			/// <summary>
			/// Initializes a new faction entry with the given template and value.
			/// </summary>
			/// <param name="templateID">The faction template ID.</param>
			/// <param name="value">The initial faction standing value.</param>
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
			/// <summary>The ability template ID.</summary>
			public readonly int TemplateID;
			/// <summary>The list of ability event IDs for this ability.</summary>
			public readonly List<int> AbilityEvents;

			/// <summary>
			/// Initializes a new ability entry with the given template and events.
			/// </summary>
			/// <param name="templateID">The ability template ID.</param>
			/// <param name="abilityEvents">The list of ability event IDs.</param>
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
			/// <summary>The item template ID.</summary>
			public readonly int TemplateID;
			/// <summary>The inventory slot index for this item.</summary>
			public readonly int Slot;

			/// <summary>
			/// Initializes a new inventory entry with the given template and slot.
			/// </summary>
			/// <param name="templateID">The item template ID.</param>
			/// <param name="slot">The inventory slot index.</param>
			public PreparedInventoryEntry(int templateID, int slot)
			{
				TemplateID = templateID;
				Slot = slot;
			}
		}

		/// <summary>
		/// Prepared immutable spawn position data extracted on the main thread
		/// so the async worker never touches Unity ScriptableObject types.
		/// </summary>
		private readonly struct PreparedSpawnDetails
		{
			/// <summary>The scene name to place the character in.</summary>
			public readonly string SceneName;
			/// <summary>Spawn position X.</summary>
			public readonly float PosX;
			/// <summary>Spawn position Y.</summary>
			public readonly float PosY;
			/// <summary>Spawn position Z.</summary>
			public readonly float PosZ;
			/// <summary>Spawn rotation X.</summary>
			public readonly float RotX;
			/// <summary>Spawn rotation Y.</summary>
			public readonly float RotY;
			/// <summary>Spawn rotation Z.</summary>
			public readonly float RotZ;
			/// <summary>Spawn rotation W.</summary>
			public readonly float RotW;

			/// <summary>
			/// Initializes a new spawn details entry with position and rotation data.
			/// </summary>
			public PreparedSpawnDetails(string sceneName, float posX, float posY, float posZ,
				float rotX, float rotY, float rotZ, float rotW)
			{
				SceneName = sceneName;
				PosX = posX; PosY = posY; PosZ = posZ;
				RotX = rotX; RotY = rotY; RotZ = rotZ; RotW = rotW;
			}
		}

		/// <summary>
		/// Prepared immutable equipment payload row for deferred persistence.
		/// </summary>
		private readonly struct PreparedEquipmentEntry
		{
			/// <summary>The equipment item template ID.</summary>
			public readonly int TemplateID;
			/// <summary>The equipment slot this item occupies.</summary>
			public readonly int Slot;
			/// <summary>The random seed used for item generation.</summary>
			public readonly int Seed;

			/// <summary>
			/// Initializes a new equipment entry with the given template, slot, and seed.
			/// </summary>
			/// <param name="templateID">The equipment item template ID.</param>
			/// <param name="slot">The equipment slot index.</param>
			/// <param name="seed">The random seed for item generation.</param>
			public PreparedEquipmentEntry(int templateID, int slot, int seed)
			{
				TemplateID = templateID;
				Slot = slot;
				Seed = seed;
			}
		}
	}
}