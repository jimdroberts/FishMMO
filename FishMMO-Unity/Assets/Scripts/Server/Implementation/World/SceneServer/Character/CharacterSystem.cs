using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Implementation.World.SceneServer.Character;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Core character management system handling character spawning, despawning, saving, and lifecycle events for player characters.
	/// </summary>
	[CreateAssetMenu(fileName = "CharacterSystem", menuName = "FishMMO/Server/SceneServer/Character System", order = 1)]
	[RequiresDataContainer(typeof(CharacterMappingData))]
	[RequiresDataContainer(typeof(CharacterSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class CharacterSystem : ServerBehaviour, ICharacterSystem<NetworkConnection, Scene>
	{
		/// <summary>
		/// Authenticator for login and character loading.
		/// </summary>
		private SceneServerAuthenticator loginAuthenticator;

		/// <summary>
		/// Prevents overlapping async periodic save operations.
		/// </summary>
		private volatile bool _isProcessing;

		/// <summary>
		/// Interval in seconds between periodic character saves.
		/// </summary>
		public float SaveRate = 60.0f;

		/// <summary>
		/// Interval in seconds between out-of-bounds checks for characters.
		/// </summary>
		public float OutOfBoundsCheckRate = 2.5f;

		/// <summary>
		/// Triggered before a character is loaded from the database. <conn, CharacterID>
		/// </summary>
		public event Action<NetworkConnection, long> OnBeforeLoadCharacter;
		/// <summary>
		/// Triggered after a character is loaded from the database. <conn, Character>
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnAfterLoadCharacter;
		/// <summary>
		/// Triggered immediately after a character is added to their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnConnect;
		/// <summary>
		/// Triggered immediately after a character is removed from their respective cache.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDisconnect;
		/// <summary>
		/// Triggered immediately after a character is spawned in the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter, Scene> OnSpawnCharacter;
		/// <summary>
		/// Triggered immediately after a character is despawned from the scene.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnDespawnCharacter;
		/// <summary>
		/// Triggered immediately after a pet is killed.
		/// </summary>
		public event Action<NetworkConnection, IPlayerCharacter> OnPetKilled;

		/// <summary>
		/// Initializes the character system, registers event handlers, and sets up character authentication and broadcast handling.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CharacterSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (ServerManager == null)
			{
				Log.Error("CharacterSystem", "InitializeOnce: ServerManager is null");
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ISceneServerSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("CharacterSystem", "Failed to initialize: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out _))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ICharacterService not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
			{
				Log.Error("CharacterSystem", "Failed to initialize: SceneServerAuthenticator not found");
				throw new UnityException("SceneServerAuthenticator not found!");
			}

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Character events
			IPlayerCharacter.OnTeleport += IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(SaveRate, OnPeriodicSave);
				periodicSystem.RegisterPeriodicCallback(OutOfBoundsCheckRate, OnPeriodicOutOfBoundsCheck);
			}

			Log.Debug("CharacterSystem", $"Initialized (SaveRate={SaveRate}s, OutOfBoundsCheckRate={OutOfBoundsCheckRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the character system, unregisters event handlers, and saves all characters to the database before shutdown.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("CharacterSystem", "OnDeinitialize: Server is null");
				return;
			}

			if (ServerManager == null)
			{
				Log.Error("CharacterSystem", "OnDeinitialize: ServerManager is null");
				return;
			}

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
			Server.NetworkWrapper.UnregisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

			// Character events
			IPlayerCharacter.OnTeleport -= IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicSave);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicOutOfBoundsCheck);
			}

			// Save all characters before shutdown
			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				{
					var characters = new List<IPlayerCharacter>(data.CharactersByID.Values);
					Task.Run(async () =>
					{
						foreach (var character in characters)
						{
							try
							{
								CharacterData charData = BuildCharacterData(character);
								await characterService.PersistAsync(charData);
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to save character {character.ID}: {ex.Message}");
							}
						}
					}).GetAwaiter().GetResult();
				}
			}
		}

		/// <summary>
		/// Periodic callback for out-of-bounds character checks.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicOutOfBoundsCheck(float deltaTime)
		{
			if (!Initialized || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			if (sceneServerSystem.WorldSceneDetailsCache == null || data.ConnectionCharacters == null)
			{
				return;
			}

			// TODO: Should the character be doing this and more often?
			// They'd need a cached world boundaries to check themselves against
			// which would prevent the need to do all of this lookup stuff.
			foreach (IPlayerCharacter character in data.ConnectionCharacters.Values)
			{
				if (character == null || string.IsNullOrWhiteSpace(character.SceneName))
				{
					continue;
				}

				var sceneName = string.IsNullOrWhiteSpace(character.InstanceSceneName)
							? character.SceneName
							: character.InstanceSceneName;

				if (sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
				{
					// Check if they are within some bounds, if not we need to move them to a respawn location!
					// TODO: Try to prevent combat escape, maybe this needs to be handled on the game design level?
					if (!details.Boundaries.PointContainedInBoundaries(character.Transform.position))
					{
						CharacterRespawnPositionDetails spawnPoint = details.RespawnPositions.Values.ToList().GetRandom();
						if (spawnPoint == null ||
							character == null ||
							character.Motor == null)
						{
							continue;
						}

						Log.Debug("CharacterSystem", $"{character.CharacterName} is out of bounds.");

						character.Motor.SetPositionAndRotationAndVelocity(spawnPoint.Position, spawnPoint.Rotation, Vector3.zero);
					}
				}
			}
		}

		/// <summary>
		/// Periodic callback for saving all characters to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicSave(float deltaTime)
		{
			if (!Initialized)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			if (data.CharactersByID.Count == 0)
			{
				return;
			}

			Log.Debug("CharacterSystem", "Save" + "[" + DateTime.UtcNow + "]");

			if (_isProcessing)
			{
				return;
			}

			// Snapshot character data on the main thread
			var characterDataList = new List<CharacterData>(data.CharactersByID.Count);
			foreach (var character in data.CharactersByID.Values)
			{
				characterDataList.Add(BuildCharacterData(character));
			}

			EnqueueAsyncWork(() => SaveAllCharactersAsync(characterDataList));
		}

		/// <summary>
		/// When a connection disconnects the server removes all known instances of the character and saves it to the database.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped &&
				Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				RemoveCharacterConnectionMapping(conn);
			}
		}

		/// <summary>
		/// Removes the character connection mapping and saves the character state to the database.
		/// </summary>
		/// <param name="conn">Network connection to remove.</param>
		/// <param name="skipOnDisconnect">If true, skips OnDisconnect event invocation.</param>
		private void RemoveCharacterConnectionMapping(NetworkConnection conn, bool skipOnDisconnect = false)
		{
			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			// Remove the waiting scene load character if it exists, these characters exist but are not spawned
			if (data.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter waitingSceneCharacter))
			{
				data.WaitingSceneLoadCharacters.Remove(conn);

				OnDisconnect?.Invoke(conn, waitingSceneCharacter);

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(waitingSceneCharacter.NetworkObject, true);
			}

			if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			// Remove the connection->character entry
			data.ConnectionCharacters.Remove(conn);

			// Remove the characterID->character entry
			data.CharactersByID.Remove(character.ID);
			// Remove the characterName->character entry
			data.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
			// Remove the worldid<characterID->character> entry
			if (data.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
			{
				characters.Remove(character.ID);
			}

			if (!skipOnDisconnect)
			{
				OnDisconnect?.Invoke(conn, character);
			}

			SaveAndDespawnCharacter(conn, character);
		}

		/// <summary>
		/// Saves the character state and despawns the character from the scene.
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">Player character to save and despawn.</param>
		private void SaveAndDespawnCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			// Snapshot character data on the main thread and fire-and-forget save
			CharacterData charData = BuildCharacterData(character);
			EnqueueAsyncWork(() => SaveCharacterAsync(charData));

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				OnDespawnCharacter?.Invoke(conn, character);

				ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
			}
		}

		/// <summary>
		/// Handles client authentication results, loads character data and initiates scene loading.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			// Is the character already loading?
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (mappingData.WaitingSceneLoadCharacters.ContainsKey(conn))
			{
				return;
			}
			if (!authenticated ||
				!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			EnqueueAsyncWork(() => LoadCharacterAsync(conn, accountName, characterService));
		}

		/// <summary>
		/// Asynchronously fetches the selected character and ALL related sub-entity data
		/// from the database using a Unit of Work for a consistent snapshot, then queues
		/// the main-thread instantiation and scene loading.
		/// </summary>
		private async Task LoadCharacterAsync(NetworkConnection conn, string accountName, ICharacterService characterService)
		{
			try
			{
				// Fetch all characters for the account and find the selected one
				DatabaseResult<IReadOnlyList<CharacterData>> listResult = await characterService.FetchManyAsync(accountName);
				if (!listResult.IsSuccess || listResult.Data == null)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to fetch character list.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				CharacterData? selectedChar = null;
				foreach (CharacterData cd in listResult.Data)
				{
					if (cd.Selected)
					{
						selectedChar = cd;
						break;
					}
				}

				if (!selectedChar.HasValue)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to fetch character ID.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				CharacterData charData = selectedChar.Value;
				long characterID = charData.ID;
				var serviceRegistry = Server.Database.ServiceRegistry;

				if (!serviceRegistry.TryGet<IUnitOfWorkService>(out var unitOfWorkService))
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to resolve IUnitOfWorkService.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				// --- Fetch all sub-entity data within a single Unit of Work (consistent snapshot) ---
				IReadOnlyList<CharacterInventoryData> inventoryData = null;
				IReadOnlyList<CharacterBankData> bankData = null;
				IReadOnlyList<CharacterEquipmentData> equipmentData = null;
				IReadOnlyList<CharacterAttributeData> attributeData = null;
				IReadOnlyList<CharacterAbilityData> abilityData = null;
				IReadOnlyList<CharacterKnownAbilityData> knownAbilityData = null;
				IReadOnlyList<CharacterAchievementData> achievementData = null;
				IReadOnlyList<CharacterFriendData> friendData = null;
				CharacterGuildData? guildData = null;
				CharacterPartyData? partyData = null;
				IReadOnlyList<CharacterHotkeyData> hotkeyData = null;
				IReadOnlyList<CharacterBuffData> buffData = null;
				IReadOnlyList<CharacterFactionData> factionData = null;

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					EnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to begin UnitOfWork for character load.");
							conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
						}
					});
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// All fetches share the same ambient DbContext/transaction (sequential, consistent snapshot)
					if (serviceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService))
					{
						var result = await inventoryService.FetchAsync(characterID);
						if (result.IsSuccess) inventoryData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBankService>(out var bankService))
					{
						var result = await bankService.FetchAsync(characterID);
						if (result.IsSuccess) bankData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterEquipmentService>(out var equipmentService))
					{
						var result = await equipmentService.FetchAsync(characterID);
						if (result.IsSuccess) equipmentData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAttributeService>(out var attributeService))
					{
						var result = await attributeService.FetchAsync(characterID);
						if (result.IsSuccess) attributeData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
					{
						var result = await abilityService.FetchAsync(characterID);
						if (result.IsSuccess) abilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
					{
						var result = await knownAbilityService.FetchAsync(characterID);
						if (result.IsSuccess) knownAbilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAchievementService>(out var achievementService))
					{
						var result = await achievementService.FetchAsync(characterID);
						if (result.IsSuccess) achievementData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFriendService>(out var friendService))
					{
						var result = await friendService.FetchAsync(characterID);
						if (result.IsSuccess) friendData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterGuildService>(out var guildService))
					{
						var result = await guildService.FetchAsync(characterID);
						if (result.IsSuccess) guildData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterPartyService>(out var partyService))
					{
						var result = await partyService.FetchAsync(characterID);
						if (result.IsSuccess) partyData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterHotkeyService>(out var hotkeyService))
					{
						var result = await hotkeyService.FetchAsync(characterID);
						if (result.IsSuccess) hotkeyData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBuffService>(out var buffService))
					{
						var result = await buffService.FetchAsync(characterID);
						if (result.IsSuccess) buffData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFactionService>(out var factionService))
					{
						var result = await factionService.FetchAsync(characterID);
						if (result.IsSuccess) factionData = result.Data;
					}

					// Read-only: commit to cleanly close the transaction
					await uow.CommitAsync();
				}

				// Marshal back to main thread for Unity object instantiation
				EnqueueMainThread(() => InstantiateAndLoadCharacter(
					conn, charData,
					inventoryData, bankData, equipmentData,
					attributeData, abilityData, knownAbilityData,
					achievementData, friendData,
					guildData, partyData,
					hotkeyData, buffData, factionData));
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"LoadCharacterAsync failed: {ex.Message}");
				EnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive)
					{
						conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					}
				});
			}
		}

		/// <summary>
		/// Instantiates the player character from CharacterData, populates all controllers
		/// with pre-fetched sub-entity data, and initiates scene loading.
		/// Must be called on the main Unity thread.
		/// </summary>
		private void InstantiateAndLoadCharacter(
			NetworkConnection conn, CharacterData charData,
			IReadOnlyList<CharacterInventoryData> inventoryData,
			IReadOnlyList<CharacterBankData> bankData,
			IReadOnlyList<CharacterEquipmentData> equipmentData,
			IReadOnlyList<CharacterAttributeData> attributeData,
			IReadOnlyList<CharacterAbilityData> abilityData,
			IReadOnlyList<CharacterKnownAbilityData> knownAbilityData,
			IReadOnlyList<CharacterAchievementData> achievementData,
			IReadOnlyList<CharacterFriendData> friendData,
			CharacterGuildData? guildData,
			CharacterPartyData? partyData,
			IReadOnlyList<CharacterHotkeyData> hotkeyData,
			IReadOnlyList<CharacterBuffData> buffData,
			IReadOnlyList<CharacterFactionData> factionData)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (mappingData.CharactersByID.ContainsKey(charData.ID))
			{
				Log.Debug("CharacterSystem", $"{charData.ID} is already loaded or loading.");
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.UnusualActivity);
				return;
			}

			OnBeforeLoadCharacter?.Invoke(conn, charData.ID);

			// Look up the race template and instantiate the character prefab
			RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(charData.RaceID);
			if (raceTemplate == null || raceTemplate.Prefab == null)
			{
				Log.Debug("CharacterSystem", "Failed to fetch character: invalid race template.");
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Vector3 position = new Vector3(charData.X, charData.Y, charData.Z);
			Quaternion rotation = new Quaternion(charData.RotX, charData.RotY, charData.RotZ, charData.RotW);

			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(
				raceTemplate.Prefab, position, rotation, true);
			if (nob == null)
			{
				Log.Debug("CharacterSystem", "Failed to instantiate character prefab.");
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			IPlayerCharacter character = nob.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				Log.Debug("CharacterSystem", "Failed to get IPlayerCharacter from instantiated prefab.");
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Populate character fields from CharacterData
			character.ID = charData.ID;
			character.CharacterName = charData.Name;
			character.CharacterNameLower = charData.NameLowercase;
			character.Account = charData.Account;
			character.Version = charData.Version;
			character.WorldServerID = charData.WorldServerID;
			character.AccessLevel = (AccessLevel)(int)charData.AccessLevel;
			character.TimeCreated = charData.TimeCreated;
			character.RaceID = charData.RaceID;
			character.ModelIndex = charData.ModelIndex;
			character.SceneName = charData.SceneName;
			character.SceneHandle = charData.SceneHandle;
			character.BindScene = charData.BindScene;
			character.BindPosition = new Vector3(charData.BindX, charData.BindY, charData.BindZ);
			character.InstanceID = charData.InstanceID;
			character.Flags = charData.Flags;

			if (character.IsInInstance())
			{
				character.InstancePosition = new Vector3(charData.InstanceX, charData.InstanceY, charData.InstanceZ);
				character.InstanceRotation = new Quaternion(charData.InstanceRotX, charData.InstanceRotY, charData.InstanceRotZ, charData.InstanceRotW);
			}

			// --- Populate all controllers from pre-fetched DB data ---

			// Attributes
			if (attributeData != null && attributeData.Count > 0 &&
				character.TryGet(out ICharacterAttributeController attrController))
			{
				foreach (CharacterAttributeData attr in attributeData)
				{
					if (attr.CurrentValue > 0)
					{
						attrController.SetResourceAttribute(attr.TemplateID, attr.Value, attr.CurrentValue);
					}
					else
					{
						attrController.SetAttribute(attr.TemplateID, attr.Value);
					}
				}
			}

			// Inventory
			if (inventoryData != null && inventoryData.Count > 0 &&
				character.TryGet(out IInventoryController inventoryController))
			{
				foreach (CharacterInventoryData inv in inventoryData)
				{
					Item item = new Item(inv.ID, inv.Seed, inv.TemplateID, inv.Amount);
					inventoryController.SetItemSlot(item, inv.Slot);
				}
			}

			// Bank
			if (bankData != null && bankData.Count > 0 &&
				character.TryGet(out IBankController bankController))
			{
				foreach (CharacterBankData bank in bankData)
				{
					Item item = new Item(bank.ID, bank.Seed, bank.TemplateID, bank.Amount);
					bankController.SetItemSlot(item, bank.Slot);
				}
			}

			// Equipment
			if (equipmentData != null && equipmentData.Count > 0 &&
				character.TryGet(out IEquipmentController equipmentController))
			{
				foreach (CharacterEquipmentData equip in equipmentData)
				{
					Item item = new Item(equip.ID, equip.Seed, equip.TemplateID, equip.Amount);
					equipmentController.SetItemSlot(item, equip.Slot);
				}
			}

			// Abilities (crafted ability instances)
			if (abilityData != null && abilityData.Count > 0 &&
				character.TryGet(out IAbilityController abilityController))
			{
				foreach (CharacterAbilityData ability in abilityData)
				{
					Ability newAbility = new Ability(ability.ID, ability.TemplateID, ability.AbilityEvents);
					abilityController.LearnAbility(newAbility, ability.Cooldown);
				}

				// Known base abilities
				if (knownAbilityData != null && knownAbilityData.Count > 0)
				{
					List<BaseAbilityTemplate> knownTemplates = new List<BaseAbilityTemplate>();
					foreach (CharacterKnownAbilityData known in knownAbilityData)
					{
						BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(known.TemplateID);
						if (template != null)
						{
							knownTemplates.Add(template);
						}
					}
					if (knownTemplates.Count > 0)
					{
						abilityController.LearnBaseAbilities(knownTemplates);
					}
				}
			}

			// Achievements
			if (achievementData != null && achievementData.Count > 0 &&
				character.TryGet(out IAchievementController achievementController))
			{
				foreach (CharacterAchievementData achievement in achievementData)
				{
					achievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value, true);
				}
			}

			// Friends
			if (friendData != null && friendData.Count > 0 &&
				character.TryGet(out IFriendController friendController))
			{
				foreach (CharacterFriendData friend in friendData)
				{
					friendController.AddFriend(friend.FriendCharacterID);
				}
			}

			// Guild
			if (guildData.HasValue &&
				character.TryGet(out IGuildController guildController))
			{
				guildController.ID = guildData.Value.GuildID;
				guildController.Rank = (GuildRank)guildData.Value.Rank;
			}

			// Party
			if (partyData.HasValue &&
				character.TryGet(out IPartyController partyController))
			{
				partyController.ID = partyData.Value.PartyID;
				partyController.Rank = (PartyRank)partyData.Value.Rank;
			}

			// Hotkeys
			if (hotkeyData != null && hotkeyData.Count > 0)
			{
				foreach (CharacterHotkeyData hotkey in hotkeyData)
				{
					if (hotkey.Slot >= 0 && hotkey.Slot < character.Hotkeys.Count)
					{
						character.Hotkeys[hotkey.Slot] = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						};
					}
				}
			}

			// Buffs
			if (buffData != null && buffData.Count > 0 &&
				character.TryGet(out IBuffController buffController))
			{
				foreach (CharacterBuffData buff in buffData)
				{
					Buff newBuff = new Buff(buff.TemplateID, buff.RemainingTime, buff.TickTime, buff.Stacks);
					buffController.Apply(newBuff);
				}
			}

			// Factions
			if (factionData != null && factionData.Count > 0 &&
				character.TryGet(out IFactionController factionController))
			{
				foreach (CharacterFactionData faction in factionData)
				{
					factionController.SetFaction(faction.TemplateID, faction.Value, true);
				}
			}

			string sceneName = character.SceneName;
			int sceneHandle = character.SceneHandle;

			// Check if the character is in an instance or not.
			if (character.IsInInstance())
			{
				// Have the player enter the instance.
				sceneName = character.InstanceSceneName;
				sceneHandle = character.InstanceSceneHandle;
			}

			// Check if the scene is valid, loaded, and cached properly
			if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneHandle, out ISceneInstanceDetails instance) &&
				sceneServerSystem.TryLoadSceneForConnection(conn, instance))
			{
				OnAfterLoadCharacter?.Invoke(conn, character);

				mappingData.WaitingSceneLoadCharacters.Add(conn, character);
			}
			else
			{
				Log.Debug("CharacterSystem", "Failed to load scene for connection.");

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				conn.Disconnect(false);
			}
		}

		/// <summary>
		/// Called when a client loads world scenes after connecting. Validates character and scene, then notifies client.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="asServer">True if loaded as server.</param>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			// Validate the connection has a character ready to play.
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) ||
				!mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			// Get the characters scene
			Scene scene;
			if (character.IsInInstance())
			{
				scene = SceneManager.GetScene(character.InstanceSceneHandle);
			}
			else
			{
				scene = SceneManager.GetScene(character.SceneHandle);
			}

			// Validate the characters scene.
			if (scene == null ||
				!scene.IsValid() ||
				!scene.isLoaded)
			{
				Log.Debug("CharacterSystem", "Scene is not valid.");

				mappingData.WaitingSceneLoadCharacters.Remove(conn);

				Destroy(character.GameObject);
				conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ClientValidatedSceneBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Called when a client completely finishes loading into a world scene. Spawns character and sets online status.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientValidatedSceneBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientValidatedSceneBroadcastReceived(NetworkConnection conn, ClientValidatedSceneBroadcast msg, Channel channel)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			if (mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				if (character == null)
				{
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Remove the waiting scene load character
				mappingData.WaitingSceneLoadCharacters.Remove(conn);

				// Add a connection->character map for ease of use
				mappingData.ConnectionCharacters[conn] = character;
				// Add a characterName->character map for ease of use
				mappingData.CharactersByID[character.ID] = character;
				mappingData.CharactersByLowerCaseName[character.CharacterNameLower] = character;
				// Add a worldID<characterID->character> map for ease of use
				if (!mappingData.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
				{
					mappingData.CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, IPlayerCharacter>());
				}
				characters[character.ID] = character;

				// Get the characters scene
				Scene scene;
				if (character.IsInInstance())
				{
					scene = SceneManager.GetScene(character.InstanceSceneHandle);
				}
				else
				{
					scene = SceneManager.GetScene(character.SceneHandle);
				}

				// Validate the scene
				if (scene == null ||
					!scene.IsValid() ||
					!scene.isLoaded)
				{
					Log.Debug("CharacterSystem", "Scene is not valid.");
					Destroy(character.GameObject);
					conn.Kick(FishNet.Managing.Server.KickReason.MalformedData);
					return;
				}

				// Set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor?.SetPhysicsScene(scene.GetPhysicsScene());

				// Character becomes mortal when loaded into the scene
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// Ensure the game object is active, pooled objects are disabled
				character.GameObject.SetActive(true);

				// Spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				OnSpawnCharacter?.Invoke(conn, character, scene);

				// Set the character status to online
				EnqueueAsyncWork(() => ClaimCharacterSessionAsync(character.ID));

				OnConnect?.Invoke(conn, character);

				// Send server character local observer data to the client.
				EnqueueAsyncWork(() => SendAllCharacterDataAsync(character));

				//Log.Debug("CharacterSystem", character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
		}

		/// <summary>
		/// The client notified the server it unloaded scenes. Disconnects connection if character is not loaded.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientScenesUnloadedBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientScenesUnloadedBroadcastReceived(NetworkConnection conn, ClientScenesUnloadedBroadcast msg, Channel channel)
		{
			if (msg.UnloadedScenes == null || msg.UnloadedScenes.Count == 0)
			{
				Log.Debug("CharacterSystem", "No unloaded scenes received.");
				return;
			}

			// Check if the connection has a character loaded.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.ConnectionCharacters.TryGetValue(conn, out var character))
			{
				return;
			}

			//Log.Debug($"Connection unloaded scene: {msg.UnloadedScenes[0].Name}|{msg.UnloadedScenes[0].Handle}");

			// Otherwise disconnect the connection.
			conn.Disconnect(false);
		}
		/// <summary>
		/// Sends all server-side character data to the owner. Non-DB data is sent immediately
		/// on the main thread. Guild, party, and friend data is fetched asynchronously and
		/// marshalled back to the main thread for broadcast.
		/// </summary>
		/// <param name="character">Player character to send data for.</param>
		private async Task SendAllCharacterDataAsync(IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			// --- Main-thread-only broadcasts (no DB calls needed) ---
			// These read from in-memory character state and broadcast via FishNet (main thread required)
			EnqueueMainThread(() => SendNonDbCharacterData(character));

			// --- Async DB-dependent broadcasts ---
			// Capture IDs on calling context (safe since we're called from main thread callback)
			long guildID = 0;
			if (character.TryGet(out IGuildController guildController))
			{
				guildID = guildController.ID;
			}

			long partyID = 0;
			if (character.TryGet(out IPartyController partyController))
			{
				partyID = partyController.ID;
			}

			List<long> friendIDs = null;
			if (character.TryGet(out IFriendController friendController) &&
				friendController.Friends != null &&
				friendController.Friends.Count > 0)
			{
				friendIDs = new List<long>(friendController.Friends);
			}

			NetworkConnection owner = character.Owner;

			if (Server.Database?.ServiceRegistry == null)
			{
				return;
			}

			try
			{
				// Guild members
				if (guildID > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterGuildService>(out var guildService))
				{
					DatabaseResult<IReadOnlyList<CharacterGuildData>> guildResult = await guildService.FetchManyAsync(guildID);
					if (guildResult.IsSuccess && guildResult.Data != null && guildResult.Data.Count > 0)
					{
						var addBroadcasts = guildResult.Data.Select(x => new GuildAddBroadcast()
						{
							GuildID = x.GuildID,
							CharacterID = x.CharacterID,
							Rank = (GuildRank)x.Rank,
							Location = x.Location,
						}).ToList();

						EnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new GuildAddMultipleBroadcast()
								{
									Members = addBroadcasts,
								}, true, Channel.Reliable);
							}
						});
					}
				}

				// Party members
				if (partyID > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var partyService))
				{
					DatabaseResult<IReadOnlyList<CharacterPartyData>> partyResult = await partyService.FetchManyAsync(partyID);
					if (partyResult.IsSuccess && partyResult.Data != null && partyResult.Data.Count > 0)
					{
						var addBroadcasts = partyResult.Data.Select(x => new PartyAddBroadcast()
						{
							PartyID = x.PartyID,
							CharacterID = x.CharacterID,
							Rank = (PartyRank)x.Rank,
							HealthPCT = x.HealthPCT,
						}).ToList();

						EnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new PartyAddMultipleBroadcast()
								{
									Members = addBroadcasts,
								}, true, Channel.Reliable);
							}
						});
					}
				}

				// Friends online status
				if (friendIDs != null && friendIDs.Count > 0 &&
					Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					List<FriendAddBroadcast> friends = new List<FriendAddBroadcast>();
					foreach (long friendID in friendIDs)
					{
						DatabaseResult<CharacterData?> friendResult = await characterService.FetchAsync(friendID);
						bool online = friendResult.IsSuccess && friendResult.Data.HasValue && friendResult.Data.Value.Online;
						friends.Add(new FriendAddBroadcast()
						{
							CharacterID = friendID,
							Online = online,
						});
					}

					if (friends.Count > 0)
					{
						EnqueueMainThread(() =>
						{
							if (owner != null && owner.IsActive)
							{
								Server.NetworkWrapper.Broadcast(owner, new FriendAddMultipleBroadcast()
								{
									Friends = friends,
								}, true, Channel.Reliable);
							}
						});
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SendAllCharacterDataAsync failed: {ex.Message}");
			}
		}

		/// <summary>
		/// Sends non-database character data (abilities, achievements, inventory, bank, hotkeys) to the owner.
		/// Must be called on the main thread.
		/// </summary>
		private void SendNonDbCharacterData(IPlayerCharacter character)
		{
			if (character == null || character.Owner == null || !character.Owner.IsActive)
			{
				return;
			}

			#region Abilities
			if (character.TryGet(out IAbilityController abilityController))
			{
				List<KnownAbilityAddBroadcast> knownAbilityBroadcasts = new List<KnownAbilityAddBroadcast>();
				List<KnownAbilityEventAddBroadcast> knownAbilityEventBroadcasts = new List<KnownAbilityEventAddBroadcast>();

				if (abilityController.KnownBaseAbilities != null)
				{
					// get base ability templates
					foreach (int templateID in abilityController.KnownBaseAbilities)
					{
						knownAbilityBroadcasts.Add(new KnownAbilityAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				if (abilityController.KnownAbilityEvents != null)
				{
					// and event templates
					foreach (int templateID in abilityController.KnownAbilityEvents)
					{
						knownAbilityEventBroadcasts.Add(new KnownAbilityEventAddBroadcast()
						{
							TemplateID = templateID,
						});
					}
				}

				// tell the client they have known abilities
				if (knownAbilityBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityAddMultipleBroadcast()
					{
						Abilities = knownAbilityBroadcasts,
					}, true, Channel.Reliable);

					Server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityEventAddMultipleBroadcast()
					{
						AbilityEvents = knownAbilityEventBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Achievements
			if (character.TryGet(out IAchievementController achievementController))
			{
				List<AchievementUpdateBroadcast> achievements = new List<AchievementUpdateBroadcast>();
				foreach (Achievement achievement in achievementController.Achievements.Values)
				{
					achievements.Add(new AchievementUpdateBroadcast()
					{
						TemplateID = achievement.Template.ID,
						Value = achievement.CurrentValue,
						Tier = achievement.CurrentTier,
					});
				}
				if (achievements.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new AchievementUpdateMultipleBroadcast()
					{
						Achievements = achievements,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region InventoryItems
			if (character.TryGet(out IInventoryController inventoryController))
			{
				List<InventorySetItemBroadcast> itemBroadcasts = new List<InventorySetItemBroadcast>();

				foreach (Item item in inventoryController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new InventorySetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new InventorySetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region BankItems
			if (character.TryGet(out IBankController bankController))
			{
				List<BankSetItemBroadcast> itemBroadcasts = new List<BankSetItemBroadcast>();

				foreach (Item item in bankController.Items)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}
					// create the new item broadcast
					itemBroadcasts.Add(new BankSetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}

				// tell the client they have items
				if (itemBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new BankSetMultipleItemsBroadcast()
					{
						Items = itemBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion

			#region Hotkeys
			if (character.Hotkeys != null)
			{
				List<HotkeySetBroadcast> hotkeyBroadcasts = new List<HotkeySetBroadcast>();

				foreach (HotkeyData hotkey in character.Hotkeys)
				{
					// just in case..
					if (hotkey == null)
					{
						continue;
					}
					// create the new hotkey broadcast
					hotkeyBroadcasts.Add(new HotkeySetBroadcast()
					{
						HotkeyData = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						}
					});
				}

				// tell the client they have hotkeys
				if (hotkeyBroadcasts.Count > 0)
				{
					Server.NetworkWrapper.Broadcast(character.Owner, new HotkeySetMultipleBroadcast()
					{
						Hotkeys = hotkeyBroadcasts,
					}, true, Channel.Reliable);
				}
			}
			#endregion
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character name.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterName">Name of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(string characterName, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByLowerCaseName.TryGetValue(characterName.ToLower(), out var character))
			{
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Allows sending a broadcast to a specific character by their character ID.
		/// Returns true if the broadcast was sent successfully, false otherwise.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="characterID">ID of the character to send to.</param>
		/// <param name="msg">Broadcast message to send.</param>
		public bool SendBroadcastToCharacter<T>(long characterID, T msg) where T : struct, IBroadcast
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				mappingData.CharactersByID.TryGetValue(characterID, out var character))
			{
				character.Owner.Broadcast(msg);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Handles character teleport events, validates teleporter and scene, updates position, and saves state.
		/// </summary>
		/// <param name="character">Player character to teleport.</param>
		public void IPlayerCharacter_OnTeleport(IPlayerCharacter character)
		{
			if (character == null)
			{
				Log.Debug("CharacterSystem", "Character doesn't exist..");
				return;
			}

			if (!character.IsTeleporting)
			{
				Log.Debug("CharacterSystem", "Character is not teleporting..");
				return;
			}

			// Should we prevent players from moving to a different scene if they are in combat?
			/*if (character.TryGet(out CharacterDamageController damageController) &&
				  damageController.Attackers.Count > 0)
			{
				return;
			}*/

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				Log.Debug("CharacterSystem", "SceneServerSystem not found!");
				return;
			}

			// Cache the current scene name
			string currentScene = character.SceneName;

			if (sceneServerSystem.WorldSceneDetailsCache == null ||
				!sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails details))
			{
				Log.Debug("CharacterSystem", currentScene + " not found!");
				return;
			}

			// Check if teleporter is a valid scene teleporter
			if (details.Teleporters.TryGetValue(character.TeleporterName, out SceneTeleporterDetails teleporter))
			{
				//Log.Debug("CharacterSystem", $"Teleporter: {character.TeleporterName} found! Teleporting {character.CharacterName} to {teleporter.ToScene}.");

				//Log.Debug("CharacterSystem", $"Unloading scene for {character.CharacterName}: {character.SceneName}|{character.SceneHandle}");

				// Tell the connection to unload their current world scene.
				sceneServerSystem.UnloadSceneForConnection(character.Owner, character.SceneName);

				// Character becomes immortal when teleporting
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					//Log.Debug("CharacterSystem", $"{character.CharacterName} is now immortal.");
					damageController.Immortal = true;
				}

				// Invoke disconnect early when teleporting because we require the scene the character is in.
				OnDisconnect?.Invoke(character.Owner, character);

				character.SceneName = teleporter.ToScene;
				character.Motor.SetPositionAndRotationAndVelocity(teleporter.ToPosition, teleporter.ToRotation, Vector3.zero);

				// Remove the character from an instance if it was in one.
				character.DisableFlags(CharacterFlags.IsInInstance);

				// Save the character and remove it from the scene
				RemoveCharacterConnectionMapping(character.Owner, true);
			}
			else
			{
				Log.Debug("CharacterSystem", $"{character.TeleporterName} not found!");
			}
		}

		/// <summary>
		/// Handles character killed events, processes player and NPC deaths, respawns, and updates state.
		/// </summary>
		/// <param name="killer">Character that performed the kill.</param>
		/// <param name="defender">Character that was killed.</param>
		private void CharacterDamageController_OnKilled(ICharacter killer, ICharacter defender)
		{
			if (defender == null)
			{
				return;
			}

			if (defender.TryGet(out IBuffController buffController))
			{
				buffController.RemoveAll(true);
			}

			// Handle Player deaths
			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;
			if (playerCharacter != null)
			{
				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				if (playerCharacter.TryGet(out ICharacterDamageController damageController))
				{
					// Full heal the character
					damageController.Heal(null, 999999, true);
				}

				if (playerCharacter.IsInInstance() && playerCharacter.InstanceSceneName != playerCharacter.BindScene ||
					playerCharacter.SceneName != playerCharacter.BindScene)
				{
					playerCharacter.SceneName = playerCharacter.BindScene;
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, playerCharacter.Motor.Transform.rotation, Vector3.zero);
					playerCharacter.NetworkObject.Owner.Disconnect(false);

					// Remove the character from the instance if it dies.
					playerCharacter.DisableFlags(CharacterFlags.IsInInstance);
				}
				else
				{
					playerCharacter.Motor.SetPositionAndRotationAndVelocity(playerCharacter.BindPosition, Quaternion.identity, Vector3.zero);
				}
			}
			else
			{
				// Handle NPC deaths
				NPC npc = defender as NPC;
				if (npc != null)
				{
					Pet pet = defender as Pet;
					if (pet != null)
					{
						//Log.Debug("CharacterSystem", $"Pet: {pet.GameObject.name} Died");

						IPlayerCharacter petOwner = pet.PetOwner as IPlayerCharacter;
						OnPetKilled?.Invoke(petOwner.NetworkObject.Owner, petOwner);
					}
					else
					{
						//Log.Debug("CharacterSystem", $"NPC: {npc.GameObject.name} Died");
						npc.Despawn();
					}
				}
			}
		}

		#region Async Helpers

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		public override void OnLateUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterSystemMainThreadQueueData>(out var queueData))
			{
				queueData.Drain();
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main Unity thread.
		/// </summary>
		private void EnqueueMainThread(Action action)
		{
			if (Server.DataContainerRegistry.TryGet<ICharacterSystemMainThreadQueueData>(out var queueData))
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Builds a CharacterData DTO from an IPlayerCharacter, capturing all fields on the main thread.
		/// Uses DateTimeOffset.UtcNow.Ticks as the version for optimistic concurrency.
		/// </summary>
		private CharacterData BuildCharacterData(IPlayerCharacter character)
		{
			Vector3 pos = character.Transform.position;
			Quaternion rot = character.Motor != null ? character.Motor.Transform.rotation : character.Transform.rotation;

			return new CharacterData(
				id: character.ID,
				name: character.CharacterName,
				nameLowercase: character.CharacterNameLower,
				account: character.Account,
				selected: true,
				worldServerID: character.WorldServerID,
				sceneName: character.SceneName,
				sceneHandle: character.SceneHandle,
				bindScene: character.BindScene,
				bindX: character.BindPosition.x,
				bindY: character.BindPosition.y,
				bindZ: character.BindPosition.z,
				instanceID: character.InstanceID,
				instanceX: character.InstancePosition.x,
				instanceY: character.InstancePosition.y,
				instanceZ: character.InstancePosition.z,
				instanceRotX: character.InstanceRotation.x,
				instanceRotY: character.InstanceRotation.y,
				instanceRotZ: character.InstanceRotation.z,
				instanceRotW: character.InstanceRotation.w,
				raceID: character.RaceID,
				modelIndex: character.ModelIndex,
				x: pos.x,
				y: pos.y,
				z: pos.z,
				rotX: rot.x,
				rotY: rot.y,
				rotZ: rot.z,
				rotW: rot.w,
				accessLevel: (byte)(int)character.AccessLevel,
				online: true,
				flags: character.Flags,
				version: DateTimeOffset.UtcNow.Ticks,
				timeCreated: character.TimeCreated,
				lastSaved: DateTime.UtcNow
			);
		}

		/// <summary>
		/// Saves a single character asynchronously via the database service.
		/// </summary>
		private async Task SaveCharacterAsync(CharacterData charData)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				await characterService.PersistAsync(charData);
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveCharacterAsync failed for character {charData.ID}: {ex.Message}");
			}
		}

		/// <summary>
		/// Saves all characters asynchronously with a processing guard to prevent overlapping saves.
		/// </summary>
		private async Task SaveAllCharactersAsync(List<CharacterData> characterDataList)
		{
			if (_isProcessing)
			{
				return;
			}
			_isProcessing = true;

			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				foreach (CharacterData charData in characterDataList)
				{
					try
					{
						await characterService.PersistAsync(charData);
					}
					catch (Exception ex)
					{
						await Log.Error("CharacterSystem", $"SaveAllCharactersAsync failed for character {charData.ID}: {ex.Message}");
					}
				}
			}
			finally
			{
				_isProcessing = false;
			}
		}

		/// <summary>
		/// Claims the character session asynchronously via TryClaimAsync.
		/// </summary>
		private async Task ClaimCharacterSessionAsync(long characterID)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				long serverID = 0;
				if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
				{
					serverID = runtimeData.ID;
				}

				await characterService.TryClaimAsync(characterID, serverID);
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"ClaimCharacterSessionAsync failed for character {characterID}: {ex.Message}");
			}
		}

		#endregion

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
