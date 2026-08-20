using FishNet.Connection;
using FishNet.Managing.Scened;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
	public partial class SceneServerSystem : ServerBehaviour, ISceneServerSystem<NetworkConnection>
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max queued scene-server actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Maximum time a startup database call may block the main thread before initialization
		/// fails. Matches the LoginServer registration timeout.
		/// </summary>
		private const int dbStartupTimeoutMs = 30_000;

		/// <summary>
		/// Maximum time a shutdown database call may block the main thread. Shorter than startup:
		/// process exit must not wait on an unresponsive database.
		/// </summary>
		private const int dbShutdownTimeoutMs = 5_000;

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
		/// <summary>
		/// Synchronous entry point. This system registers itself in the database, so it must be
		/// initialized through <see cref="InitializeOnceAsync"/>; reaching this method means the
		/// asynchronous startup chain was bypassed.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			Log.Error("SceneServerSystem",
				"InitializeOnce called directly. This system performs database I/O and must be " +
				"initialized via InitializeOnceAsync (Server drives this through the async startup chain).");
			return ServerComponentInitializationStatus.InitializationFailed;
		}

		/// <summary>
		/// Initializes the system and registers it in the database without blocking the Unity
		/// main thread.
		/// </summary>
		/// <remarks>
		/// Awaits here deliberately capture Unity's SynchronizationContext (no
		/// <c>ConfigureAwait(false)</c>), so execution resumes on the main thread and the Unity
		/// and FishNet APIs used below stay legal.
		/// </remarks>
		public override async Task<ServerComponentInitializationStatus> InitializeOnceAsync(CancellationToken cancellationToken)
		{
			if (Server == null)
			{
				_ = Log.Error("SceneServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerSystemMainThreadQueueData>(out _))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ISceneInstanceMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				_ = Log.Error("SceneServerSystem", "InitializeOnce: ISceneServerRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.NetworkWrapper.NetworkManager.SceneManager == null)
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: SceneManager not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ICharacterSystem not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ICharacterMappingData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ISceneServerService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				_ = Log.Error("SceneServerSystem", "Failed to initialize: ISceneService not found");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Scene manager events
			Server.NetworkWrapper.NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			Server.NetworkWrapper.NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;

			// Character system events
			characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			characterSystem.OnAfterLoadCharacter += CharacterSystem_OnAfterLoadCharacter;

			// Register scene server in database. UnitySyncOverAsync keeps the async work off
			// Unity's SynchronizationContext (blocking on it here would deadlock startup) and
			// bounds the wait so an unreachable database fails initialization instead of hanging.
			// Capture Unity API values on the main thread before dispatching.
			string serverName = name;
			string serverAddress = server.Address;
			ushort serverPort = server.Port;
			int characterCount = characterMappingData.ConnectionCharacters.Count;
			DatabaseResult<(long ServerId, SceneServerData ServerData)> persistResult =
				await sceneServerService.PersistAsync(serverName, serverAddress, serverPort, characterCount, runtimeData.IsLocked, cancellationToken);

			if (!persistResult.IsSuccess)
			{
				_ = Log.Error("SceneServerSystem", $"Failed to register scene server in database: [{persistResult.ErrorCode}] {persistResult.ErrorMessage} (IsTransient={persistResult.IsTransient})");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			long id = persistResult.Data.ServerId;
			runtimeData.ID = id;

			// Delete any stale scenes for this server
			DatabaseResult<int> cleanupResult = await sceneService.DeleteBySceneServerAsync(id, cancellationToken);
			if (!cleanupResult.IsSuccess)
			{
				_ = Log.Warning("SceneServerSystem", $"Failed to clean up stale scenes for server {id}: [{cleanupResult.ErrorCode}] {cleanupResult.ErrorMessage}");
			}

			// In-game operator commands. See SceneServerSystem.AdminCommands.
			RegisterAdminCommands();

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

			_ = Log.Debug("SceneServerSystem", $"Initialized (ServerID={id}, Address={server.Address}:{server.Port}, CharacterCount={characterCount})");
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
					// Blocking call during shutdown to ensure cleanup completes, bounded so an
					// unresponsive database cannot hold process exit open.
					if (UnitySyncOverAsync.TryRun(
						cancellationToken => sceneService.DeleteBySceneServerAsync(runtimeData.ID, cancellationToken),
						out DatabaseResult<int> cleanupResult,
						dbShutdownTimeoutMs))
					{
						if (!cleanupResult.IsSuccess)
						{
							Log.Warning("SceneServerSystem", $"Failed to clean up scene server from DB during shutdown (ServerID={runtimeData.ID}): [{cleanupResult.ErrorCode}] {cleanupResult.ErrorMessage}");
						}
					}
					else
					{
						Log.Warning("SceneServerSystem", $"Scene server DB cleanup timed out after {dbShutdownTimeoutMs}ms during shutdown (ServerID={runtimeData.ID})");
					}
				}
			}

			UnregisterAdminCommands();

			// A shutdown that has been carried out must not still be pending in the database, or
			// an automatic restart stops again immediately. See ClearConsumedShutdownOnTeardown.
			ClearConsumedShutdownOnTeardown();

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
		/// <param name="sceneID">Scene row ID identifying the instance.</param>
		/// <param name="amount">Amount to adjust by (+1 or -1).</param>
		public void AdjustSceneCharacterCount(long worldServerID, string sceneName, long sceneID, int amount)
		{
			if (TryGetSceneInstanceDetails(worldServerID,
											sceneName,
											sceneID,
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
					$"World={worldServerID} Scene={sceneName} SceneID={sceneID} " +
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

			/* Adopt whatever the last pulse read back, and act on any shutdown that is now due.
			 * Ahead of the pulse below so a server past its deadline stops rather than issuing
			 * another heartbeat and another round of scene work. */
			if (!ProcessControlState())
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

			// Include bodies left behind by a combat logout. They are not in ConnectionCharacters
			// — they have no connection — but they are still resident, still simulated and still
			// holding a session claim, so reporting without them understates this server's load.
			int characterCount = characterMappingData.ConnectionCharacters.Count;
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> lingerCharacterSystem))
			{
				characterCount += lingerCharacterSystem.LingeringCharacterCount;
			}

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

								int staleSceneTimeoutMinutes = ResolveStaleTimeoutMinutes(sceneDetails.SceneType);

								if (timeSinceLastExit < staleSceneTimeoutMinutes)
								{
									Log.Debug("SceneServerSystem", $"{sceneDetails.Name}:{sceneDetails.WorldServerID}{sceneDetails.Handle}:{sceneDetails.CharacterCount} Stale Pulse");
									runtimeData.ScenePulseDataBuffer.Add((sceneDetails.SceneID, sceneDetails.CharacterCount));
								}
								else
								{
									// Mark for unload on main thread
									runtimeData.ScenesToUnloadBuffer.Add(sceneDetails.SceneID);
								}
							}
							else
							{
								runtimeData.ScenePulseDataBuffer.Add((sceneDetails.SceneID, sceneDetails.CharacterCount));
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
			var pulseSnapshot = new List<(long SceneID, int CharacterCount)>(runtimeData.ScenePulseDataBuffer);

			/* The worlds this server currently hosts scenes for.
			 *
			 * A scene server is not owned by one world — DequeueAsync hands out whichever pending
			 * row is oldest, whatever world it belongs to — so a world-wide shutdown has to be
			 * looked up per world, and only for the worlds actually represented here. Collected
			 * on the main thread alongside everything else the pulse needs. */
			var hostedWorldIDs = new List<long>(mappingData.WorldScenes != null ? mappingData.WorldScenes.Count : 0);
			if (mappingData.WorldScenes != null)
			{
				foreach (long hostedWorldID in mappingData.WorldScenes.Keys)
				{
					hostedWorldIDs.Add(hostedWorldID);
				}
			}

			if (!TryEnqueueAsyncWork(() => PeriodicPulseAsync(runtimeData.ID, characterCount, pulseSnapshot, maxScenesLoadedPerPulse, hostedWorldIDs), runtimeData.ID))
			{
				runtimeData.EndPulse();
			}
		}

		/// <summary>
		/// How long an empty scene of this type may sit before it is unloaded.
		/// </summary>
		/// <remarks>
		/// Instanced content is not open world and must not be measured as if it were. An
		/// open-world zone is shared, expensive to spin up, and likely to be wanted again within
		/// the hour, so holding an empty one is a reasonable trade. A dungeon instance belongs to
		/// one character or party: once it empties, the odds of that exact group returning fall
		/// away quickly, and every abandoned one holds a full physics scene and a scene row for
		/// the same hour. On a busy shard that is the dominant source of idle scene load.
		/// <para>
		/// The instance timeout is still generous enough to be a grace period rather than a
		/// reclaim: a party that wipes and runs back, or a player who relogs, keeps the instance
		/// and its progress. It only has to outlast a disconnect-and-return, not a coffee break.
		/// </para>
		/// <para>
		/// Both values come from configuration, and both fall back to a safe default rather than
		/// to zero — <c>TryGetInt</c> leaves its out parameter at 0 when the key is missing, and
		/// a zero timeout unloads every empty scene on the next pulse.
		/// </para>
		/// </remarks>
		/// <param name="sceneType">Type of the scene being considered for unload.</param>
		/// <returns>Timeout in minutes.</returns>
		private int ResolveStaleTimeoutMinutes(SceneType sceneType)
		{
			const int DefaultOpenWorldStaleMinutes = 60;
			const int DefaultInstanceStaleMinutes = 5;

			bool instanced = sceneType != SceneType.OpenWorld;

			string key = instanced ? "StaleInstanceSceneTimeout" : "StaleSceneTimeout";
			int fallback = instanced ? DefaultInstanceStaleMinutes : DefaultOpenWorldStaleMinutes;

			if (!Server.Configuration.TryGetInt(key, out int minutes) || minutes < 1)
			{
				if (!loggedMissingStaleTimeoutKeys.Contains(key))
				{
					loggedMissingStaleTimeoutKeys.Add(key);
					Log.Warning("SceneServerSystem",
						$"Configuration key '{key}' is missing or not a positive number. Using the default of {fallback} minutes.");
				}
				return fallback;
			}

			return minutes;
		}

		/// <summary>
		/// Config keys already reported as missing, so the warning is logged once rather than on
		/// every stale scene on every pulse.
		/// </summary>
		private readonly HashSet<string> loggedMissingStaleTimeoutKeys = new HashSet<string>(StringComparer.Ordinal);

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
		/// <param name="scenePulseData">Collected pulse payload for tracked scenes.</param>
		/// <param name="hostedWorldServerIDs">World servers this scene server currently hosts scenes for.</param>
		/// <returns>Asynchronous pulse processing task.</returns>
		private async Task PeriodicPulseAsync(long serverID, int characterCount,
			List<(long SceneID, int CharacterCount)> pulseSnapshot,
			int maxScenesPerPulse,
			List<long> hostedWorldServerIDs)
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

				/* Send the server heartbeat, and read this server's control state back from the
				 * same statement. The pulse no longer writes `locked` from memory: the row is
				 * the authority for it, so that write was erasing whatever an operator set. */
				DatabaseResult<ServerControlState> pulseResult = await sceneServerService.PulseAsync(serverID, characterCount);
				if (!pulseResult.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"PulseAsync DB error (ServerID={serverID}): {pulseResult.ErrorCode} - {pulseResult.ErrorMessage}");
				}
				else
				{
					PublishControlState(pulseResult.Data);
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

				// What the worlds this server hosts scenes for have been told to do.
				await FetchWorldControlStatesAsync(hostedWorldServerIDs);

				/* A locked scene server takes no new scene-load requests.
				 *
				 * Locking is a drain: the world server stops routing players here, so a scene
				 * loaded now would be stranded — nobody would be sent to it and it would sit
				 * until the stale sweep removed it. Leaving the row in the queue lets a scene
				 * server that is still accepting work take it instead. */
				if (IsLockedNow())
				{
					return;
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

				/* Kick any connected characters assigned to this scene so they don't get stuck.
				 * Characters arriving after this point are handled by CharacterSystem.Loading,
				 * which disconnects when TryGetSceneInstanceDetails returns false.
				 *
				 * Collected before disconnecting rather than disconnected in place. Today
				 * Disconnect(false) is deferred, so mutating the map it is being read from does
				 * not happen synchronously — but that is a property of FishNet's scheduling, not
				 * of this loop, and if it ever stops holding the enumeration throws part-way and
				 * every character after the first is left stuck in a scene that will never
				 * exist. The snapshot costs one small list per failed scene load. */
				if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var charMappingData))
				{
					List<NetworkConnection> stranded = null;
					foreach (var kvp in charMappingData.ConnectionCharacters)
					{
						IPlayerCharacter resident = kvp.Value;
						if (resident == null)
						{
							continue;
						}

						/* A scene server hosts scenes for every world server — DequeueAsync hands
						 * out whichever pending row is oldest, whatever world it belongs to — so
						 * scene name alone does not identify who this failure concerns. Matching
						 * on the name by itself kicked residents of another world's scene that
						 * merely happens to share it. */
						if (resident.WorldServerID != sceneData.WorldServerID)
						{
							continue;
						}

						// An instanced character lives in InstanceSceneName; matching only
						// SceneName left it behind in a scene that failed to load. See
						// CurrentSceneName.
						string residentScene = resident.CurrentSceneName();

						if (string.Equals(residentScene, sceneData.SceneName, StringComparison.Ordinal))
						{
							Log.Warning("SceneServerSystem", $"Kicking character '{resident.CharacterName}' — scene '{sceneData.SceneName}' failed to load.");
							(stranded ??= new List<NetworkConnection>()).Add(kvp.Key);
						}
					}

					if (stranded != null)
					{
						for (int i = 0; i < stranded.Count; ++i)
						{
							stranded[i]?.Disconnect(false);
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
				ProcessScene(scene, sceneType, sceneData.WorldServerID, sceneData.ID);

				// Capture Scene.name on the main thread. TryEnqueueAsyncWork runs its lambda
				// on an AsyncWorkerData thread-pool worker, and Unity's Scene.name getter is
				// a main-thread-only native call — closing over `scene` and reading .name
				// inside the lambda throws there and aborts the scene-ready handling.
				// Scene.handle is a plain struct field and is safe either way; it is
				// captured alongside for symmetry.
				string sceneName = scene.name;
				// Recorded on the row for diagnostics; the row's own id is the instance identity.
				int sceneHandle = scene.handle;

				long readySceneId = sceneData.ID;

				Log.Debug("SceneServerSystem", $"Saved {sceneType} scene {sceneName}:{sceneHandle} (SceneID={readySceneId}) to the database.");
				if (!TryEnqueueAsyncWork(() => SetSceneReadyAsync(readySceneId, runtimeData.ID, sceneData.WorldServerID, sceneName, sceneHandle), runtimeData.ID))
				{
					Log.Warning("SceneServerSystem", $"Failed to enqueue async SetSceneReady: Scene={sceneName}:{sceneHandle}. Firing directly.");
					// Fire-and-forget is safe here: SetSceneReadyAsync has its own
					// try-catch with error logging (see method body). The task is
					// discarded with _ = to suppress the compiler warning; any DB
					// or network failure will be logged internally without crashing
					// the scene-load callback.
					_ = SetSceneReadyAsync(readySceneId, runtimeData.ID, sceneData.WorldServerID, sceneName, sceneHandle);
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
		/// <remarks>
		/// The scene row is named explicitly by <paramref name="sceneId"/> — the row this server
		/// dequeued and just finished loading. It used to be identified by (world, scene name)
		/// and the oldest loading row won, which silently mismatched rows to instances whenever
		/// two loads of the same scene overlapped: an instanced row carries the character it was
		/// created for, so the player was handed somebody else's dungeon.
		/// <para>
		/// A failure here leaves the row in Loading, where the world server's stale-scene sweep
		/// eventually reaps it and any character pointed at it is routed back to the open world,
		/// rather than waiting on an instance that will never become ready.
		/// </para>
		/// </remarks>
		/// <param name="sceneId">Database ID of the scene row being made ready.</param>
		/// <param name="sceneServerId">Owning scene server identifier.</param>
		/// <param name="worldServerId">Owning world server identifier.</param>
		/// <param name="sceneName">Scene name to mark ready.</param>
		/// <param name="sceneHandle">This process's scene-manager handle, recorded for diagnostics only.</param>
		/// <returns>Asynchronous update task.</returns>
		private async Task SetSceneReadyAsync(long sceneId, long sceneServerId, long worldServerId, string sceneName, int sceneHandle)
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
				DatabaseResult result = await sceneService.SetReadyAsync(sceneId, sceneServerId, worldServerId, sceneName, sceneHandle);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"SetSceneReadyAsync DB error (SceneID={sceneId}, SceneServerID={sceneServerId}, Scene={sceneName}:{sceneHandle}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error setting scene ready (SceneID={sceneId}, SceneServerID={sceneServerId}, Scene={sceneName}:{sceneHandle}): {ex}");
			}
		}

		/// <summary>
		/// Adds a loaded scene to the world scene mappings and sets up physics ticking.
		/// </summary>
		/// <param name="scene">The loaded Unity scene.</param>
		/// <param name="sceneType">Type of the scene.</param>
		/// <param name="worldServerID">World server ID.</param>
		private void ProcessScene(Scene scene, SceneType sceneType, long worldServerID, long sceneID)
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
			if (!mappingData.WorldScenes.TryGetValue(worldServerID, out Dictionary<string, Dictionary<long, ISceneInstanceDetails>> scenes))
			{
				mappingData.WorldScenes.Add(worldServerID, scenes = new Dictionary<string, Dictionary<long, ISceneInstanceDetails>>());
			}
			if (!scenes.TryGetValue(scene.name, out Dictionary<long, ISceneInstanceDetails> instances))
			{
				scenes.Add(scene.name, instances = new Dictionary<long, ISceneInstanceDetails>());
			}
			if (!instances.TryGetValue(sceneID, out ISceneInstanceDetails existing))
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
					SceneID = sceneID,
					Name = scene.name,
					SceneType = sceneType,
					Handle = scene.handle,
					CharacterCount = 0,
					LastExit = DateTime.UtcNow,
				};
				instances.Add(sceneID, details);

				Log.Debug("SceneServerSystem", $"New scene instance added for {worldServerID}:{scene.name} SceneID={sceneID} (local handle {scene.handle})");

				mappingData.SceneNameByHandle[scene.handle] = scene.name;
				// Unity scene handles may be reused after unload.
				// Always remove mappings in SceneManager_OnUnloadEnd to keep these maps correct.
				mappingData.SceneInstanceByHandle[scene.handle] = details;
				mappingData.SceneInstanceByID[sceneID] = details;
			}
			else
			{
				/* Two loads landed on one scene row. The row can only describe one of them, and
				 * the first is the one already wired up — physics ticker, instance registry,
				 * handle maps — so this second Unity scene is unreachable: nothing can route a
				 * character to it and nothing will ever mark it stale, because stale-pulse
				 * detection walks the instance registry it was never added to. Ignoring it
				 * leaked a fully simulated scene for the life of the process. Unload it.
				 *
				 * Deliberately not routed through UnloadScene: that resolves the row ID to the
				 * registered instance and would unload the scene that is actually in service,
				 * and it deletes the database row as well. Only this orphan goes. */
				Log.Error("SceneServerSystem", $"Duplicate scene load detected: {worldServerID}:{scene.name} SceneID={sceneID} (local handle {scene.handle}). Unloading the duplicate.");

				if (existing.Handle != scene.handle && scene.IsValid())
				{
					Server.NetworkWrapper.NetworkManager.SceneManager.UnloadConnectionScenes(new SceneUnloadData()
					{
						SceneLookupDatas = new SceneLookupData[]
						{
							new SceneLookupData(scene.handle),
						},
					});
				}
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
						sceneGroup.TryGetValue(details.Name, out var instances))
					{
						instances.Remove(details.SceneID);

						// Clean up empty containers to prevent memory leak from scene churn
						if (instances.Count == 0)
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
					mappingData.SceneInstanceByID.Remove(details.SceneID);

					Log.Debug("SceneServerSystem", $"Unloaded scene SceneID={details.SceneID} (local handle {handle})");
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
		public bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, long sceneID, out ISceneInstanceDetails instanceDetails)
		{
			instanceDetails = default;

			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				return false;
			}

			// O(1) flat lookup by scene row ID, which is unique across every scene server.
			if (mappingData.SceneInstanceByID.TryGetValue(sceneID, out instanceDetails))
			{
				// Validate the caller's world/scene expectations match
				if (instanceDetails.WorldServerID == worldServerID &&
					string.Equals(instanceDetails.Name, sceneName, StringComparison.Ordinal))
				{
					return true;
				}

				Log.Warning("SceneServerSystem", $"SceneID {sceneID} found but mismatched: expected {worldServerID}:{sceneName}, got {instanceDetails.WorldServerID}:{instanceDetails.Name}");
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
		public void UnloadScene(long sceneID)
		{
			if (!Server.DataContainerRegistry.TryGet<ISceneInstanceMappingData>(out var mappingData))
			{
				Log.Warning("SceneServerSystem", "Failed to get ISceneInstanceMappingData during Scene Unload.");
				return;
			}

			/* The scene manager only understands this process's own handle, so the row ID the
			 * caller names has to be translated here. Nothing outside this class holds a local
			 * handle any more, which is the point: it is not an identity anywhere else. */
			if (!mappingData.SceneInstanceByID.TryGetValue(sceneID, out ISceneInstanceDetails details))
			{
				Log.Warning("SceneServerSystem", $"Unload requested for SceneID={sceneID}, which this server does not host.");
				return;
			}

			// Remove the scene details from the database immediately upon an Unload request
			// to prevent new clients from connecting to it.
			if (!TryEnqueueAsyncWork(() => DeleteSceneAsync(sceneID), sceneID))
			{
				Log.Warning("SceneServerSystem", $"Failed to enqueue async DeleteScene: SceneID={sceneID}. Firing directly.");
				_ = DeleteSceneAsync(sceneID);
			}

			SceneUnloadData sud = new SceneUnloadData()
			{
				SceneLookupDatas = new SceneLookupData[]
				{
					new SceneLookupData(details.Handle),
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
		private async Task DeleteSceneAsync(long sceneID)
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
				DatabaseResult result = await sceneService.DeleteAsync(sceneID);
				if (!result.IsSuccess)
				{
					await Log.Warning("SceneServerSystem", $"DeleteSceneAsync DB error (SceneID={sceneID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("SceneServerSystem", $"Error deleting scene (SceneID={sceneID}): {ex}");
			}
		}
	}
}