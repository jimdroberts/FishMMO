using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using UnityEngine;
using UnityEngine.SceneManagement;
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
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages scene server node services, scene loading/unloading, and heartbeat updates to the world server.
	/// Tracks scene instances, handles connection events, and synchronizes scene state with the database.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes are marshalled
	/// via ISceneServerSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "SceneServerSystem", menuName = "FishMMO/Server/SceneServer/Scene Server System", order = 1)]
	[RequiresDataContainer(typeof(SceneInstanceMappingData))]
	[RequiresDataContainer(typeof(SceneServerRuntimeData))]
	[RequiresDataContainer(typeof(SceneServerSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class SceneServerSystem : ServerBehaviour, ISceneServerSystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max queued scene-server actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum pending scene load age in seconds before failing and removing the request.
		/// </summary>
		[Header("Pending Scene Protection")]
		[Tooltip("Maximum pending scene load age in seconds before failing the request")]
		[SerializeField] private float pendingSceneTimeoutSeconds = 60.0f;

		/// <summary>
		/// Interval in seconds between bounded pending-scene cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded pending-scene cleanup sweeps")]
		[SerializeField] private float pendingSceneSweepIntervalSeconds = 2.0f;

		/// <summary>
		/// Maximum number of expired pending scenes removed per sweep pass.
		/// </summary>
		[Tooltip("Maximum expired pending scenes removed per sweep")]
		[SerializeField] private int pendingSceneSweepMaxRemovals = 64;

		/// <summary>
		/// Maximum number of scene load requests dequeued from the database per pulse.
		/// Prevents a flooded DB queue from loading hundreds of scenes in one cycle.
		/// </summary>
		[Header("Scene Load Throttling")]
		[Tooltip("Max scene load requests dequeued from DB per pulse")]
		[SerializeField] private int maxScenesLoadedPerPulse = 3;

		/// <summary>
		/// Interval (in seconds) between heartbeat pulses to the database.
		/// </summary>
		[SerializeField]
		private float pulseRate = 5.0f;

		/// <summary>
		/// Gets or sets the pulse rate for heartbeat updates.
		/// </summary>
		public float PulseRate { get { return pulseRate; } set { pulseRate = value; } }
		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		[SerializeField]
		private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache { get { return worldSceneDetailsCache; } }

		/// <summary>
		/// Called once to initialize the scene server system. Registers the server in the database and subscribes to connection and scene events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("SceneServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerSystemMainThreadQueueData>(out _))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneInstanceMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Error("SceneServerSystem", "InitializeOnce: ISceneServerRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.NetworkWrapper.NetworkManager.SceneManager == null)
			{
				Log.Error("SceneServerSystem", "Failed to initialize: SceneManager not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ICharacterMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("SceneServerSystem", "Failed to initialize: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				Log.Error("SceneServerSystem", "Failed to initialize: ISceneService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;

			// Character system events
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			characterSystem.OnAfterLoadCharacter += CharacterSystem_OnAfterLoadCharacter;

			// Register scene server in database (Task.Run avoids deadlock from
			// Unity's SynchronizationContext when blocking on async during init).
			// Capture Unity API values on the main thread before dispatching.
			string serverName = name;
			string serverAddress = server.Address;
			ushort serverPort = server.Port;
			int characterCount = characterMappingData.ConnectionCharacters.Count;
			DatabaseResult<(long ServerId, SceneServerData ServerData)> persistResult = Task.Run(() =>
				sceneServerService.PersistAsync(serverName, serverAddress, serverPort, characterCount, runtimeData.IsLocked))
				.GetAwaiter().GetResult();

			if (!persistResult.IsSuccess)
			{
				Log.Error("SceneServerSystem", $"Failed to register scene server in database: [{persistResult.ErrorCode}] {persistResult.ErrorMessage} (IsTransient={persistResult.IsTransient})");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			long id = persistResult.Data.ServerId;
			runtimeData.ID = id;

			// Delete any stale scenes for this server
			DatabaseResult<int> cleanupResult = Task.Run(() => sceneService.DeleteBySceneServerAsync(id)).GetAwaiter().GetResult();
			if (!cleanupResult.IsSuccess)
			{
				Log.Warning("SceneServerSystem", $"Failed to clean up stale scenes for server {id}: [{cleanupResult.ErrorCode}] {cleanupResult.ErrorMessage}");
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			pendingSceneTimeoutSeconds = Mathf.Max(5.0f, pendingSceneTimeoutSeconds);
			pendingSceneSweepIntervalSeconds = Mathf.Max(0.25f, pendingSceneSweepIntervalSeconds);
			pendingSceneSweepMaxRemovals = Mathf.Max(1, pendingSceneSweepMaxRemovals);
			maxScenesLoadedPerPulse = Mathf.Max(1, maxScenesLoadedPerPulse);
			runtimeData.EndPulse();
			runtimeData.NextPendingSceneSweepUtc = DateTime.UtcNow;

			Log.Debug("SceneServerSystem", $"Initialized (ServerID={id}, Address={server.Address}:{server.Port}, CharacterCount={characterCount})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from events and deletes scene data from the database.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("SceneServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			// Delete scene server data from database
			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Debug("SceneServerSystem", $"Deinitializing: Removing Scene Server scenes (ServerID={runtimeData.ID})");

				if (Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					// Blocking call during shutdown to ensure cleanup completes
					DatabaseResult<int> cleanupResult = Task.Run(() => sceneService.DeleteBySceneServerAsync(runtimeData.ID)).GetAwaiter().GetResult();
					if (!cleanupResult.IsSuccess)
					{
						Log.Warning("SceneServerSystem", $"Failed to clean up scene server from DB during shutdown (ServerID={runtimeData.ID}): [{cleanupResult.ErrorCode}] {cleanupResult.ErrorMessage}");
					}
				}
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}

			// Character system events
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
				characterSystem.OnAfterLoadCharacter -= CharacterSystem_OnAfterLoadCharacter;
			}

			// Scene manager events
			var sceneManager = Server.NetworkWrapper?.NetworkManager?.SceneManager;
			if (sceneManager != null)
			{
				sceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
				sceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;
			}

			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeState))
			{
				runtimeState.EndPulse();
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the ISceneServerSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<ISceneServerSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ISceneServerSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Drains the main-thread queue each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
		}

		/// <summary>
		/// Handles character disconnect events, adjusting scene character counts.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="character">Player character that disconnected.</param>
		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (character.IsInInstance())
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.InstanceSceneName, character.InstanceSceneHandle, -1);
			}
			else
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.SceneName, character.SceneHandle, -1);
			}
		}

		/// <summary>
		/// Handles character load events, adjusting scene character counts.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="character">Player character that loaded.</param>
		private void CharacterSystem_OnAfterLoadCharacter(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null)
			{
				return;
			}

			if (character.IsInInstance())
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.InstanceSceneName, character.InstanceSceneHandle, 1);
			}
			else
			{
				AdjustSceneCharacterCount(character.WorldServerID, character.SceneName, character.SceneHandle, 1);
			}
		}

		/// <summary>
		/// Adjusts the character count for a specific scene instance.
		/// </summary>
		/// <param name="worldServerID">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="amount">Amount to adjust by (+1 or -1).</param>
		private void AdjustSceneCharacterCount(long worldServerID, string sceneName, int sceneHandle, int amount)
		{
			if (TryGetSceneInstanceDetails(worldServerID,
											sceneName,
											sceneHandle,
											out ISceneInstanceDetails instance))
			{
				instance.AddCharacterCount(amount);
			}
			else
			{
				// Hard error: instance must exist after scene load. Silent failure
				// means character count stays 0, pulse reports StalePulse, and the
				// scene may be collected before the client finishes loading.
				Log.Error("SceneServerSystem",
					$"AdjustSceneCharacterCount: scene instance not found for " +
					$"World={worldServerID} Scene={sceneName} Handle={sceneHandle} " +
					$"Amount={amount:+0;-#}. Character count will be stale.");
			}
		}

		/// <summary>
		/// Periodic callback that sends heartbeat pulses and processes scene state.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (!Initialized ||
				Server == null ||
				Server.ServerState != ConnectionState.Started ||
				Server.BehaviourRegistry == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData) ||
				!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				return;
			}

			SweepExpiredPendingScenes(mappingData);

			// Fire-and-forget async DB operations — check early so reusable
			// buffers below are never cleared while a prior pulse is still reading them.
			if (!runtimeData.TryBeginPulse())
			{
				return;
			}

			int characterCount = characterMappingData.ConnectionCharacters.Count;

			// Collect scene pulse data on the main thread before async work.
			// Reuse runtime-data buffers to avoid per-pulse GC allocations (P9 fix).
			// Safe: TryBeginPulse above guarantees no concurrent pulse reads these buffers.
			runtimeData.ScenePulseDataBuffer.Clear();
			runtimeData.ScenesToUnloadBuffer.Clear();

			if (mappingData.WorldScenes != null)
			{
				foreach (var sceneGroup in mappingData.WorldScenes.Values)
				{
					runtimeData.SceneGroupValuesBuffer.Clear();
					foreach (var sceneGroupValue in sceneGroup.Values)
					{
						runtimeData.SceneGroupValuesBuffer.Add(sceneGroupValue);
					}

					for (int g = 0; g < runtimeData.SceneGroupValuesBuffer.Count; g++)
					{
						runtimeData.SceneDetailsValuesBuffer.Clear();
						foreach (var detailsValue in runtimeData.SceneGroupValuesBuffer[g].Values)
						{
							runtimeData.SceneDetailsValuesBuffer.Add(detailsValue);
						}

						for (int d = 0; d < runtimeData.SceneDetailsValuesBuffer.Count; d++)
						{
							ISceneInstanceDetails sceneDetails = runtimeData.SceneDetailsValuesBuffer[d];
							if (sceneDetails.StalePulse)
							{
								double timeSinceLastExit = DateTime.UtcNow.Subtract(sceneDetails.LastExit).TotalMinutes;

								// Resolve the stale scene timeout from config with a safe default.
								// When the config key is missing, TryGetInt returns false and
								// leaves result at 0 — without a default, ALL stale scenes would
								// be unloaded immediately (every scene is stale > 0 minutes).
								if (!Server.Configuration.TryGetInt("StaleSceneTimeout", out int staleSceneTimeoutMinutes))
								{
									staleSceneTimeoutMinutes = 60;
									Log.Warning("SceneServerSystem", "Configuration key 'StaleSceneTimeout' not found. Using default of 60 minutes.");
								}

								if (timeSinceLastExit < staleSceneTimeoutMinutes)
								{
									Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Stale Pulse");
									runtimeData.ScenePulseDataBuffer.Add((sceneDetails.Handle, sceneDetails.CharacterCount));
								}
								else
								{
									// Mark for unload on main thread
									runtimeData.ScenesToUnloadBuffer.Add(sceneDetails.Handle);
								}
							}
							else
							{
								runtimeData.ScenePulseDataBuffer.Add((sceneDetails.Handle, sceneDetails.CharacterCount));
							}
						}
					}
				}
			}

			// Unload stale scenes immediately on main thread
			for (int i = 0; i < runtimeData.ScenesToUnloadBuffer.Count; i++)
			{
				UnloadScene(runtimeData.ScenesToUnloadBuffer[i]);
			}

			// Snapshot pulse data before passing to async — avoids fragile shared-buffer pattern
			// where a future refactor could clear the reusable buffer while async is still reading it.
			var pulseSnapshot = new List<(int Handle, int CharacterCount)>(runtimeData.ScenePulseDataBuffer);

			if (!TryEnqueueAsyncWork(() => PeriodicPulseAsync(runtimeData.ID, characterCount, runtimeData.IsLocked, pulseSnapshot, maxScenesLoadedPerPulse), runtimeData.ID))
			{
				runtimeData.EndPulse();
			}
		}

		/// <summary>
		/// Performs a bounded TTL sweep of expired pending scene load requests.
		/// Expired requests are failed in the database and removed from local tracking.
		/// </summary>
		private void SweepExpiredPendingScenes(ISceneInstanceMappingData mappingData)
		{
			if (mappingData == null || mappingData.PendingScenes == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextPendingSceneSweepUtc)
			{
				return;
			}

			runtimeData.NextPendingSceneSweepUtc = nowUtc.AddSeconds(pendingSceneSweepIntervalSeconds);
			DateTime staleBeforeUtc = nowUtc.AddSeconds(-pendingSceneTimeoutSeconds);

			runtimeData.ExpiredSceneIdsBuffer.Clear();
			int removed = 0;
			// IMPORTANT: Do not remove from PendingScenes during enumeration.
			// IDs are collected first, then removed in the loop below.
			foreach (var kvp in mappingData.PendingScenes)
			{
				if (removed >= pendingSceneSweepMaxRemovals)
				{
					break;
				}

				if (kvp.Value.EnqueuedUtc <= staleBeforeUtc)
				{
					runtimeData.ExpiredSceneIdsBuffer.Add(kvp.Key);
					removed++;
				}
			}

			for (int i = 0; i < runtimeData.ExpiredSceneIdsBuffer.Count; ++i)
			{
				long sceneId = runtimeData.ExpiredSceneIdsBuffer[i];
				mappingData.PendingScenes.Remove(sceneId);

				Log.Warning("SceneServerSystem", $"Pending scene request timed out and was failed: SceneID={sceneId}");
				if (!TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneId, SceneStatus.Failed), sceneId))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async status update for expired scene: SceneID={sceneId}. Firing directly.");
					_ = UpdateSceneStatusAsync(sceneId, SceneStatus.Failed);
				}
			}
		}

		/// <summary>
		/// Asynchronously sends heartbeat pulses to the database and processes pending scene requests.
		/// Scene pulse data and dequeued scene requests are processed on the main thread via TryEnqueueMainThread.
		/// </summary>
		/// <param name="serverID">Scene server identifier.</param>
		/// <param name="characterCount">Current connected character count.</param>
		/// <param name="isLocked">Whether the server is currently locked.</param>
		/// <param name="scenePulseData">Collected pulse payload for tracked scenes.</param>
		/// <returns>Asynchronous pulse processing task.</returns>
		private async Task PeriodicPulseAsync(long serverID, int characterCount, bool isLocked,
			List<(int Handle, int CharacterCount)> pulseSnapshot,
			int maxScenesPerPulse)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				if (!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService) ||
					!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				// Send server heartbeat pulse
				DatabaseResult pulseResult = await sceneServerService.PulseAsync(serverID, characterCount, isLocked);
				if (!pulseResult.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"PulseAsync DB error (ServerID={serverID}): {pulseResult.ErrorCode} - {pulseResult.ErrorMessage}");
				}

				// Send scene heartbeat pulses (batched) — snapshot already in DB-ready format
				if (pulseSnapshot.Count > 0)
				{
					DatabaseResult<int> batchResult = await sceneService.PulseBatchAsync(pulseSnapshot);
					if (!batchResult.IsSuccess)
					{
						await Log.Warning("SceneServerSystem", $"PulseBatchAsync DB error ({pulseSnapshot.Count} scenes): {batchResult.ErrorCode} - {batchResult.ErrorMessage}");
					}
				}

				// Process pending scenes — bounded to maxScenesPerPulse to prevent DB flood
				for (int s = 0; s < maxScenesPerPulse; s++)
				{
					DatabaseResult<SceneData> dequeueResult = await sceneService.DequeueAsync();
					if (!dequeueResult.IsSuccess)
					{
						break;
					}

					SceneData pending = dequeueResult.Data;
					TryEnqueueMainThread(() =>
					{
						Log.Debug("SceneServerSystem", $"Scene Server System: Dequeued Pending Scene Load request World:{pending.WorldServerID} Scene:{pending.SceneName}");
						ProcessSceneLoadRequest(pending);
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error during periodic pulse: {ex}");
			}
			finally
			{
				if (Server != null &&
					Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
				{
					runtimeData.EndPulse();
				}
			}
		}

		/// <summary>
		/// Processes a single scene load request from the database, pre-caching and loading the scene.
		/// </summary>
		/// <param name="sceneEntity">Scene entity to process.</param>
		private void ProcessSceneLoadRequest(SceneData sceneData)
		{
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.Contains(sceneData.SceneName))
			{
				Log.Warning("SceneServerSystem", $"Scene is missing from the cache. Unable to load scene: SceneID={sceneData.ID} Scene={sceneData.SceneName}");
				if (!TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async status update for missing scene: SceneID={sceneData.ID}. Firing directly.");
					_ = UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed);
				}

				// Kick any connected characters assigned to this scene so they don't get stuck.
				// Characters arriving after this point are handled by CharacterSystem.Loading
				// which disconnects when TryGetSceneInstanceDetails returns false.
				if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMappingData))
				{
					foreach (var kvp in charMappingData.ConnectionCharacters)
					{
						if (kvp.Value.SceneName == sceneData.SceneName)
						{
							Log.Warning("SceneServerSystem", $"Kicking character '{kvp.Value.CharacterName}' — scene '{sceneData.SceneName}' failed to load.");
							kvp.Key.Disconnect(false);
						}
					}
				}
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return;
			}

			// Guard against duplicate load requests for the same scene ID.
			// TryAdd is atomic — prevents a race if two queued callbacks both try
			// to load the same scene before the first insert is visible.
			if (!mappingData.PendingScenes.TryAdd(sceneData.ID, new PendingSceneInfo(sceneData, DateTime.UtcNow)))
			{
				Log.Warning("SceneServerSystem", $"Duplicate scene load request ignored: SceneID={sceneData.ID} Scene={sceneData.SceneName}");
				return;
			}

			// Pre-cache the scene on the server
			SceneLookupData lookupData = new SceneLookupData(sceneData.SceneName);
			SceneLoadData sld = new SceneLoadData(lookupData)
			{
				ReplaceScenes = ReplaceOption.None,
				Options = new LoadOptions
				{
					AllowStacking = true,
					AutomaticallyUnload = false,
					LocalPhysics = LocalPhysicsMode.Physics3D,
				},
				Params = new LoadParams()
				{
					ServerParams = new object[]
					{
						sceneData.ID,
					},
				},
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.LoadConnectionScenes(sld);
		}

		/// <summary>
		/// Handles scene load completion events, updating mappings and database state.
		/// </summary>
		/// <param name="args">Scene load end event arguments.</param>
		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			const int UNKNOWN_WORLD_ID = -1;

			// If ServerParams are missing or there are no elements we should ignore processing this scene load.
			if (args.QueueData.SceneLoadData.Params.ServerParams == null)
			{
				Log.Warning("SceneServerSystem", "Failed to process scene. Invalid Server Parameters.");
				return;
			}

			if (args.QueueData.SceneLoadData.Params.ServerParams.Length < 1)
			{
				return;
			}

			if (!(args.QueueData.SceneLoadData.Params.ServerParams[0] is long sceneDataKey))
			{
				Log.Warning("SceneServerSystem", $"ServerParams[0] is not a long (actual type: {args.QueueData.SceneLoadData.Params.ServerParams[0]?.GetType()}).");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				!mappingData.PendingScenes.TryGetValue(sceneDataKey, out PendingSceneInfo pendingInfo))
			{
				Log.Warning("SceneServerSystem", "Pending Scene does not exist!");
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Warning("SceneServerSystem", "Runtime data missing while processing scene load end.");
				return;
			}

			SceneData sceneData = pendingInfo.SceneData;
			mappingData.PendingScenes.Remove(sceneData.ID);

			if (sceneData.WorldServerID == UNKNOWN_WORLD_ID)
			{
				Log.Warning("SceneServerSystem", "Failed to get World Server ID.");
				if (!TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async status update: SceneID={sceneData.ID}. Firing directly.");
					_ = UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed);
				}
				return;
			}

			SceneType sceneType = (SceneType)sceneData.SceneType;
			if (sceneType == SceneType.Unknown)
			{
				Log.Warning("SceneServerSystem", "Unknown scene type.");
				if (!TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async status update: SceneID={sceneData.ID}. Firing directly.");
					_ = UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed);
				}
				return;
			}

			// If the load was unsuccessful, args.LoadedScenes will be empty.
			if (args.LoadedScenes == null || args.LoadedScenes.Length < 1)
			{
				Log.Debug("SceneServerSystem", $"Failed to load Database Scene[{sceneData.ID}].");
				if (!TryEnqueueAsyncWork(() => UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed), sceneData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async status update: SceneID={sceneData.ID}. Firing directly.");
					_ = UpdateSceneStatusAsync(sceneData.ID, SceneStatus.Failed);
				}
			}
			else
			{
				Scene scene = args.LoadedScenes[0];

				// Process the scene by adding it to the world dictionary mappings.
				ProcessScene(scene, sceneType, sceneData.WorldServerID);

				// Capture Scene.name/handle on the main thread now. TryEnqueueAsyncWork
				// runs its lambda on a background worker thread, and Unity's Scene.name
				// getter (GetNameInternal) throws if called off the main thread — so the
				// lambda below must close over these local copies, never `scene` itself.
				string sceneName = scene.name;
				int sceneHandle = scene.handle;

				Log.Debug("SceneServerSystem", $"Saved {sceneType} scene {sceneName}:{sceneHandle} to the database.");
				if (!TryEnqueueAsyncWork(() => SetSceneReadyAsync(runtimeData.ID, sceneData.WorldServerID, sceneName, sceneHandle), runtimeData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async SetSceneReady: Scene={sceneName}:{sceneHandle}. Firing directly.");
					// Fire-and-forget is safe here: SetSceneReadyAsync has its own
					// try-catch with error logging (see method body). The task is
					// discarded with _ = to suppress the compiler warning; any DB
					// or network failure will be logged internally without crashing
					// the scene-load callback.
					_ = SetSceneReadyAsync(runtimeData.ID, sceneData.WorldServerID, sceneName, sceneHandle);
				}
			}
		}

		/// <summary>
		/// Asynchronously updates a scene's status in the database.
		/// </summary>
		/// <param name="sceneId">Database scene identifier.</param>
		/// <param name="status">Status value to apply.</param>
		/// <returns>Asynchronous update task.</returns>
		private async Task UpdateSceneStatusAsync(long sceneId, SceneStatus status)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				// Cast from FishMMO.Shared.SceneStatus to FishMMO.Database.Data.Enums.SceneStatus (same int values)
				DatabaseResult result = await sceneService.UpdateStatusAsync(sceneId, (FishMMO.Database.Data.Enums.SceneStatus)(int)status);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"UpdateSceneStatusAsync DB error (SceneID={sceneId}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error updating scene status (SceneID={sceneId}): {ex}");
			}
		}

		/// <summary>
		/// Asynchronously sets a scene to ready status in the database.
		/// </summary>
		/// <param name="sceneServerId">Owning scene server identifier.</param>
		/// <param name="worldServerId">Owning world server identifier.</param>
		/// <param name="sceneName">Scene name to mark ready.</param>
		/// <param name="sceneHandle">Scene handle to mark ready.</param>
		/// <returns>Asynchronous update task.</returns>
		private async Task SetSceneReadyAsync(long sceneServerId, long worldServerId, string sceneName, int sceneHandle)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				DatabaseResult result = await sceneService.SetReadyAsync(sceneServerId, worldServerId, sceneName, sceneHandle);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"SetSceneReadyAsync DB error (SceneServerID={sceneServerId}, Scene={sceneName}:{sceneHandle}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error setting scene ready (SceneServerID={sceneServerId}, Scene={sceneName}:{sceneHandle}): {ex}");
			}
		}

		/// <summary>
		/// Adds a loaded scene to the world scene mappings and sets up physics ticking.
		/// </summary>
		/// <param name="scene">The loaded Unity scene.</param>
		/// <param name="sceneType">Type of the scene.</param>
		/// <param name="worldServerID">World server ID.</param>
		private void ProcessScene(Scene scene, SceneType sceneType, long worldServerID)
		{
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Configure the mapping for this specific world scene
			if (!mappingData.WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<int, ISceneInstanceDetails>> scenes))
			{
				mappingData.WorldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<int, ISceneInstanceDetails>>());
			}
			if (!scenes.TryGetValue(scene.name, out Dictionary<int, ISceneInstanceDetails> handles))
			{
				scenes.Add(scene.name, handles = new Dictionary<int, ISceneInstanceDetails>());
			}
			if (!handles.ContainsKey(scene.handle))
			{
				// Ensure the scene has a physics ticker
				GameObject gob = new GameObject("PhysicsTicker");
				gob.hideFlags = HideFlags.DontSave;
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gob, scene);
				PhysicsTicker physicsTicker = gob.AddComponent<PhysicsTicker>();
				physicsTicker.InitializeOnce(scene.GetPhysicsScene(), Server.NetworkWrapper.NetworkManager.TimeManager);

				// Cache the newly loaded scene
				var details = new SceneInstanceDetails()
				{
					WorldServerID = worldServerID,
					SceneServerID = runtimeData.ID,
					Name = scene.name,
					SceneType = sceneType,
					Handle = scene.handle,
					CharacterCount = 0,
					LastExit = DateTime.UtcNow,
				};
				handles.Add(scene.handle, details);

				Log.Debug("SceneServerSystem", $"New scene handle added for {worldServerID}:{scene.name}:{scene.handle}");

				mappingData.SceneNameByHandle[scene.handle] = scene.name;
				// Unity scene handles may be reused after unload.
				// Always remove mappings in SceneManager_OnUnloadEnd to keep this map correct.
				mappingData.SceneInstanceByHandle[scene.handle] = details;
			}
			else
			{
				Log.Error("SceneServerSystem", $"Duplicate scene handle detected: {worldServerID}:{scene.name}:{scene.handle}. Ignoring duplicate.");
			}
		}

		/// <summary>
		/// Handles scene unload completion events, removing scene mappings and cleaning up.
		/// Uses the flat SceneInstanceByHandle map for O(1) lookup per unloaded scene,
		/// then removes from both the flat map and the nested WorldScenes hierarchy.
		/// </summary>
		/// <param name="args">Scene unload end event arguments.</param>
		public void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData) ||
				mappingData.WorldScenes == null)
			{
				Log.Warning("SceneServerSystem", "No World Scenes found.");
				return;
			}

			if (args.UnloadedScenesV2.Count < 1)
			{
				Log.Warning("SceneServerSystem", "UnloadedScenesV2 failed to unload any scenes.");
				return;
			}

			for (int i = 0; i < args.UnloadedScenesV2.Count; ++i)
			{
				int handle = args.UnloadedScenesV2[i].Handle;

				// O(1) lookup via flat map — no nested iteration required
				if (mappingData.SceneInstanceByHandle.TryGetValue(handle, out ISceneInstanceDetails details))
				{
					// Remove from nested WorldScenes hierarchy
					if (mappingData.WorldScenes.TryGetValue(details.WorldServerID, out var sceneGroup) &&
						sceneGroup.TryGetValue(details.Name, out var handles))
					{
						handles.Remove(handle);

						// Clean up empty containers to prevent memory leak from scene churn
						if (handles.Count == 0)
						{
							sceneGroup.Remove(details.Name);
						}
						if (sceneGroup.Count == 0)
						{
							mappingData.WorldScenes.Remove(details.WorldServerID);
						}
					}

					// Remove from flat maps.
					// Unity scene handles may be reused after unload — removal here
					// ensures stale handles never resolve to freed instance details.
					mappingData.SceneInstanceByHandle.Remove(handle);
					mappingData.SceneNameByHandle.Remove(handle);

					Log.Debug("SceneServerSystem", $"Unloaded scene handle: {handle}");
				}
				else
				{
					Log.Warning("SceneServerSystem", $"Unloaded scene handle {handle} not found in SceneInstanceByHandle.");
				}
			}
		}

		/// <summary>
		/// Attempts to get scene instance details for a given world server, scene name, and handle.
		/// Uses the flat SceneInstanceByHandle map for O(1) lookup, then validates the world server
		/// and scene name match as a correctness check.
		/// </summary>
		/// <param name="worldServerID">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="instanceDetails">Output instance details.</param>
		/// <returns>True if found, false otherwise.</returns>
		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, int sceneHandle, out ISceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return false;
			}

			// O(1) flat lookup by handle
			if (mappingData.SceneInstanceByHandle.TryGetValue(sceneHandle, out instanceDetails))
			{
				// Validate the caller's world/scene expectations match
				if (instanceDetails.WorldServerID == worldServerID &&
					string.Equals(instanceDetails.Name, sceneName, StringComparison.Ordinal))
				{
					return true;
				}

				Log.Warning("SceneServerSystem", $"Handle {sceneHandle} found but mismatched: expected {worldServerID}:{sceneName}, got {instanceDetails.WorldServerID}:{instanceDetails.Name}");
				instanceDetails = default;
			}

			return false;
		}

		/// <summary>
		/// Attempts to load a scene for a connection if it is valid and loaded.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="instance">Scene instance details.</param>
		/// <returns>True if scene was loaded for the connection, false otherwise.</returns>
		public bool TryLoadSceneForConnection(NetworkConnection connection, ISceneInstanceDetails instance)
		{
			Scene scene = SceneManager.GetScene(instance.Handle);
			if (scene != null && scene.IsValid() && scene.isLoaded)
			{
				SceneLookupData lookupData = new SceneLookupData(instance.Handle);
				SceneLoadData sld = new SceneLoadData(lookupData)
				{
					ReplaceScenes = ReplaceOption.None,
					Options = new LoadOptions
					{
						AutomaticallyUnload = false,
					},
					PreferredActiveScene = new PreferredScene(lookupData),
				};
				Server.NetworkWrapper.NetworkManager.SceneManager.LoadConnectionScenes(connection, sld);
				return true;
			}
			else
			{
				Log.Debug("SceneServerSystem", $"Scene: {instance.Name}|{instance.Handle} not found in SceneManager.");
			}
			return false;
		}

		/// <summary>
		/// Unloads a scene for a connection by scene name.
		/// </summary>
		/// <param name="connection">Network connection.</param>
		/// <param name="sceneName">Name of the scene to unload.</param>
		public void UnloadSceneForConnection(NetworkConnection connection, string sceneName)
		{
			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(sceneName),
				},
				Options = new UnloadOptions()
				{
					Mode = UnloadOptions.ServerUnloadMode.KeepUnused
				}
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(connection, sud);
		}

		/// <summary>
		/// Unloads a scene by handle and removes its details from the database and server.
		/// </summary>
		/// <param name="handle">Scene handle to unload.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UnloadScene(int handle)
		{
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				Log.Warning("SceneServerSystem", "Failed to get ISceneInstanceMappingData during Scene Unload.");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				Log.Warning("SceneServerSystem", "Failed to get ISceneServerRuntimeData during Scene Unload.");
				return;
			}

			// Remove the scene details from the database immediately upon an Unload request
			// to prevent new clients from connecting to it.
			long serverId = runtimeData.ID;
			if (!TryEnqueueAsyncWork(() => DeleteSceneByHandleAsync(serverId, handle), serverId))
			{
				Log.Warning("SceneServerSystem", $"Failed to enqueue async DeleteSceneByHandle: ServerID={serverId}, Handle={handle}. Firing directly.");
				_ = DeleteSceneByHandleAsync(serverId, handle);
			}

			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(handle),
				},
			};
			Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(sud);
		}

		/// <summary>
		/// Asynchronously deletes a specific scene by server ID and handle from the database.
		/// </summary>
		/// <param name="sceneServerId">Owning scene server identifier.</param>
		/// <param name="sceneHandle">Scene handle to delete.</param>
		/// <returns>Asynchronous delete task.</returns>
		private async Task DeleteSceneByHandleAsync(long sceneServerId, int sceneHandle)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}
				DatabaseResult result = await sceneService.DeleteByHandleAsync(sceneServerId, sceneHandle);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"DeleteSceneByHandleAsync DB error (ServerID={sceneServerId}, Handle={sceneHandle}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error deleting scene by handle (ServerID={sceneServerId}, Handle={sceneHandle}): {ex}");
			}
		}
	}
}