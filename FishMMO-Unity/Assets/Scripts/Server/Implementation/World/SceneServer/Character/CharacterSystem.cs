using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Implementation.World.SceneServer.Character;
using FishMMO.Shared;
using FishMMO.Auth.Core;
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
	[RequiresDataContainer(typeof(CharacterSystemRuntimeData))]
	[RequiresDataContainer(typeof(CharacterSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class CharacterSystem : ServerBehaviour, ICharacterSystem<NetworkConnection, Scene>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max character-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 200;

		/// <summary>
		/// Interval in seconds between periodic character saves.
		/// </summary>
		[Tooltip("Interval in seconds between periodic character saves")]
		[SerializeField][Min(1f)] private float saveRate = 30.0f;

		/// <summary>
		/// Interval in seconds between out-of-bounds checks for characters.
		/// </summary>
		[Tooltip("Interval in seconds between out-of-bounds checks")]
		[SerializeField][Min(0.1f)] private float outOfBoundsCheckRate = 2.5f;

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

			SceneServerAuthenticator loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator == null)
			{
				Log.Error("CharacterSystem", "Failed to initialize: SceneServerAuthenticator not found");
				throw new UnityException("SceneServerAuthenticator not found!");
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("CharacterSystem", "Failed to initialize: ICharacterSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived, true);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			SubscribeToConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport += IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;

			// Periodic callbacks
			saveRate = Mathf.Max(1f, saveRate);
			outOfBoundsCheckRate = Mathf.Max(0.1f, outOfBoundsCheckRate);
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(saveRate, OnPeriodicSave);
				periodicSystem.RegisterPeriodicCallback(outOfBoundsCheckRate, OnPeriodicOutOfBoundsCheck);
				periodicSystem.RegisterPeriodicCallback(30f, OnPeriodicRespawnResurrectSweep);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			runtimeData.EndSave();

			Log.Debug("CharacterSystem", $"Initialized (SaveRate={saveRate}s, OutOfBoundsCheckRate={outOfBoundsCheckRate}s)");
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
			SceneServerAuthenticator loginAuthenticator = FindFirstObjectByType<SceneServerAuthenticator>();
			if (loginAuthenticator != null)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ClientScenesUnloadedBroadcast>(OnClientScenesUnloadedBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived);

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;

			// Connection state events
			UnsubscribeFromConnectionEvents();

			// Character events
			IPlayerCharacter.OnTeleport -= IPlayerCharacter_OnTeleport;
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicSave);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicOutOfBoundsCheck);
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicRespawnResurrectSweep);
			}

			// Save all characters and release all sessions before shutdown
			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
				{
					// Snapshot character data on the main thread (Unity API access required)
					var characterDataList = new List<CharacterData>();
					foreach (var character in data.CharactersByID.Values)
					{
						try
						{
							characterDataList.Add(BuildCharacterData(character));
						}
						catch (Exception ex)
						{
							Log.Error("CharacterSystem", $"OnDeinitialize: Failed to snapshot character {character.ID}: {ex.Message}");
						}
					}

					// Capture all session tokens (spawned + waiting-to-load characters)
					var sessionTokens = new Dictionary<long, CharacterSessionInfo>(data.SessionTokens);

					Task.Run(async () =>
					{
						// Save all spawned characters
						foreach (var charData in characterDataList)
						{
							try
							{
								DatabaseResult result = await characterService.PersistAsync(charData);
								if (!result.IsSuccess)
								{
									await Log.Warning("CharacterSystem", $"OnDeinitialize: DB error saving character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");
								}
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to save character {charData.ID}: {ex.Message}");
							}
						}

						// Release all claimed sessions (spawned + waiting-to-load characters)
						foreach (var kvp in sessionTokens)
						{
							try
							{
								await ReleaseCharacterSessionAsync(kvp.Key, kvp.Value.ServerID, kvp.Value.Token);
							}
							catch (Exception ex)
							{
								await Log.Error("CharacterSystem", $"OnDeinitialize: Failed to release session for character {kvp.Key}: {ex.Message}");
							}
						}
					}).GetAwaiter().GetResult();
				}
			}
		}

		#region Async Helpers

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue<ICharacterSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll: false);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main Unity thread.
		/// </summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ICharacterSystemMainThreadQueueData>(action);
		}

		#endregion

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		/// <param name="work">The async work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for consistent worker routing.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns><c>true</c> if the work item was enqueued; otherwise, <c>false</c>.</returns>
		private bool EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
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
	}
}