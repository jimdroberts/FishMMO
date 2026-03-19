using FishNet.Connection;
using FishNet.Managing.Server;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Manages world scene connections, queues, and scene assignment for players in the MMO server.
	/// Handles open world and instanced scene logic, connection authentication, and database updates.
	/// Game logic and Broadcasts run synchronously on the main thread.
	/// Database operations are async to avoid blocking the main thread.
	/// Results from async DB queries that require main-thread state changes are marshalled
	/// via IWorldSceneSystemMainThreadQueueData.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldSceneSystem", menuName = "FishMMO/Server/WorldServer/World Scene System", order = 1)]
	[RequiresDataContainer(typeof(WorldSceneSystemRuntimeData))]
	[RequiresDataContainer(typeof(WorldSceneMappingData))]
	[RequiresDataContainer(typeof(WorldSceneSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class WorldSceneSystem : ServerBehaviour, IWorldSceneSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max world-scene actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Connection Routing Hardening")]
		[Tooltip("Minimum seconds between instance DB routing lookups for the same account")]
		[SerializeField] private float instanceLookupDebounceSeconds = 3.0f;

		[Tooltip("Maximum seconds a connection may remain in waiting queues before being purged")]
		[SerializeField] private float waitingQueueTtlSeconds = 45.0f;

		[Tooltip("Seconds between stale waiting-queue purge sweeps")]
		[SerializeField] private float waitingQueueSweepIntervalSeconds = 5.0f;

		[Tooltip("Seconds between stale debounce-entry cleanup sweeps")]
		[SerializeField] private float debounceCleanupIntervalSeconds = 60.0f;

		[Tooltip("Max account debounce entries scanned per cleanup sweep")]
		[SerializeField] private int debounceCleanupMaxScanPerSweep = 256;

		[Tooltip("Max account debounce entries removed per cleanup sweep")]
		[SerializeField] private int debounceCleanupMaxRemovalsPerSweep = 128;

		[Tooltip("Max stale queued connections purged per waiting-queue sweep")]
		[SerializeField] private int waitingQueuePurgeMaxPerSweep = 128;

		[Header("Scene Instance Cache")]
		[Tooltip("Seconds before cached scene-instance query results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneInstanceCacheTtlSeconds = 5.0f;

		[Tooltip("Seconds before cached scene-server address results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneServerCacheTtlSeconds = 10.0f;

		/// <summary>
		/// Maximum number of clients allowed per scene instance.
		/// </summary>
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		/// <summary>
		/// Maximum connections allowed per waiting queue type (open-world or instance).
		/// Defense-in-depth cap to prevent unbounded memory growth.
		/// Effective global limit is 2 × this value (one cap per queue type).
		/// </summary>
		private const int MAX_WAITING_QUEUE_SIZE = 2500;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// FIXME: This should be injected via the data container registry instead of being a public field.
		/// </summary>
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>
		/// Called once to initialize the world scene system. Subscribes to authentication and connection events.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("WorldSceneSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (ServerManager == null)
			{
				Log.Error("WorldSceneSystem", "InitializeOnce: ServerManager is null");
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("WorldSceneSystem", "InitializeOnce: WorldSceneSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneSystemMainThreadQueueData>(out _))
			{
				Log.Error("WorldSceneSystem", "InitializeOnce: IWorldSceneSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("WorldSceneSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			runtimeData.LoginAuthenticator = FindFirstObjectByType<WorldServerAuthenticator>();
			if (runtimeData.LoginAuthenticator == null)
			{
				Log.Error("WorldSceneSystem", "Failed to initialize: WorldServerAuthenticator not found");
				throw new UnityException("WorldServerAuthenticator not found!");
			}

			// Connection state events
			SubscribeToConnectionEvents();

			// Authentication events
			runtimeData.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			instanceLookupDebounceSeconds = Mathf.Max(0.1f, instanceLookupDebounceSeconds);
			waitingQueueTtlSeconds = Mathf.Max(5.0f, waitingQueueTtlSeconds);
			waitingQueueSweepIntervalSeconds = Mathf.Max(1.0f, waitingQueueSweepIntervalSeconds);
			debounceCleanupIntervalSeconds = Mathf.Max(5.0f, debounceCleanupIntervalSeconds);
			debounceCleanupMaxScanPerSweep = Mathf.Max(1, debounceCleanupMaxScanPerSweep);
			debounceCleanupMaxRemovalsPerSweep = Mathf.Max(1, debounceCleanupMaxRemovalsPerSweep);
			waitingQueuePurgeMaxPerSweep = Mathf.Max(1, waitingQueuePurgeMaxPerSweep);
			runtimeData.WaitQueueRateSeconds = Mathf.Max(0.5f, runtimeData.WaitQueueRateSeconds);
			sceneInstanceCacheTtlSeconds = Mathf.Max(0f, sceneInstanceCacheTtlSeconds);
			sceneServerCacheTtlSeconds = Mathf.Max(0f, sceneServerCacheTtlSeconds);
			runtimeData.EndProcessing();
			runtimeData.NextWaitingQueueSweep = waitingQueueSweepIntervalSeconds;
			runtimeData.NextDebounceCleanup = debounceCleanupIntervalSeconds;

			Log.Debug("WorldSceneSystem", $"Initialized (WaitQueueRate={runtimeData.WaitQueueRateSeconds}s, MaxClientsPerInstance={MAX_CLIENTS_PER_INSTANCE})");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from events and deletes world scene data from the database.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("WorldSceneSystem", "OnDeinitialize: Server is null");
				return;
			}

			if (ServerManager == null)
			{
				Log.Error("WorldSceneSystem", "OnDeinitialize: ServerManager is null");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("WorldSceneSystem", "OnDeinitialize: WorldSceneSystemRuntimeData not found");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);
			runtimeData.InstanceLookupDebounce?.Clear();
			runtimeData.WaitingQueueEnteredUtcByClientId?.Clear();
			runtimeData.AvailableSceneCache?.Clear();
			runtimeData.SceneServerAddressCache?.Clear();

			// Connection state events
			UnsubscribeFromConnectionEvents();

			// Authentication events
			if (runtimeData.LoginAuthenticator != null)
			{
				runtimeData.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			// Delete world scene data from database
			if (TryGetDbService(out ISceneService sceneService) &&
				Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData))
			{
				Log.Debug("WorldSceneSystem", $"Deinitializing: Deleting world scenes (WorldServerID={worldData.ID})");
				// Blocking call during shutdown with timeout to prevent indefinite hang
				try
				{
					Task.Run(() => sceneService.DeleteByWorldServerAsync(worldData.ID)).Wait(5000);
				}
				catch (Exception ex)
				{
					Log.Error("WorldSceneSystem", $"Failed to delete world scenes during shutdown: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IWorldSceneSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IWorldSceneSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IWorldSceneSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Enqueues an action on the main thread and returns a task that completes when the action finishes.
		/// If the enqueue fails (queue full or unavailable), the returned task completes immediately with false.
		/// Prevents the caller from hanging forever on an unfulfilled TaskCompletionSource.
		/// </summary>
		private Task<bool> RunOnMainThreadAsync(Action action)
		{
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!TryEnqueueMainThread(() =>
			{
				try
				{
					action();
				}
				finally
				{
					tcs.TrySetResult(true);
				}
			}))
			{
				tcs.TrySetResult(false);
			}
			return tcs.Task;
		}

		/// <summary>
		/// Handles remote connection disconnects. Removes connections from queues when they disconnect.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
			}
		}

		/// <summary>
		/// Called by the server's LateUpdate. Periodically processes open world and instance queues, and updates connection count.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Drain queued main-thread actions from async operations
			DrainMainThreadQueue(drainAll: false);

			runtimeData.NextWaitingQueueSweep -= deltaTime;
			if (runtimeData.NextWaitingQueueSweep <= 0f)
			{
				runtimeData.NextWaitingQueueSweep = waitingQueueSweepIntervalSeconds;
				PurgeExpiredWaitingConnections(runtimeData);
			}

			runtimeData.NextDebounceCleanup -= deltaTime;
			if (runtimeData.NextDebounceCleanup <= 0f)
			{
				runtimeData.NextDebounceCleanup = debounceCleanupIntervalSeconds;
				CleanupExpiredDebounceEntries(runtimeData);
				SweepSceneCaches(runtimeData);
			}

			if (runtimeData.NextWaitQueueUpdate <= 0)
			{
				runtimeData.NextWaitQueueUpdate = runtimeData.WaitQueueRateSeconds;

				if (Server.Database?.ServiceRegistry != null &&
					Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					// Snapshot the scene names and connections to process before going async.
					// Capacity hints avoid resizes for typical queue sizes.
					List<string> openWorldSceneNames = new List<string>(mappingData.WaitingOpenWorldConnections.Count);
					openWorldSceneNames.AddRange(mappingData.WaitingOpenWorldConnections.Keys);

					List<NetworkConnection> instanceConns = new List<NetworkConnection>(mappingData.InstanceConnectionScenes.Count);
					instanceConns.AddRange(mappingData.InstanceConnectionScenes.Keys);

					if (runtimeData.TryBeginProcessing())
					{
						if (!TryEnqueueAsyncWork(() => ProcessQueuesAsync(openWorldSceneNames, instanceConns)))
						{
							runtimeData.EndProcessing();
							Log.Warning("WorldSceneSystem", "Failed to enqueue world scene queue processing work item.");
						}
					}
				}
			}
			runtimeData.NextWaitQueueUpdate -= deltaTime;
		}

		/// <summary>
		/// Asynchronously processes open world and instance queues, then updates connection count.
		/// All main-thread state changes and Broadcasts are marshalled via TryEnqueueMainThread.
		/// </summary>
		private async Task ProcessQueuesAsync(List<string> openWorldSceneNames, List<NetworkConnection> instanceConns)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				var tasks = new List<Task>(openWorldSceneNames.Count + instanceConns.Count);
				foreach (string sceneName in openWorldSceneNames)
				{
					tasks.Add(ProcessOpenWorldQueueAsync(sceneName));
				}
				foreach (NetworkConnection conn in instanceConns)
				{
					tasks.Add(ProcessInstanceConnectionAsync(conn));
				}
				await Task.WhenAll(tasks);

				await UpdateConnectionCountAsync();
			}
			catch (Exception ex)
			{
				await Log.Error("WorldSceneSystem", $"Error processing queues: {ex}");
			}
			finally
			{
				if (Server != null && Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
				{
					runtimeData.EndProcessing();
				}
			}
		}

		/// <summary>
		/// Processes the queue for open world scenes, assigning connections to available scene instances.
		/// <para>
		/// Each waiting connection's character data is fetched to read their saved <c>SceneHandle</c>.
		/// If a matching available instance exists with capacity, the character is routed there.
		/// This enables channel switching: <c>SceneChannelSystem</c> sets the target handle before
		/// disconnect, and this method honours that preference. If no match is found, the character
		/// falls back to any available instance with capacity.
		/// </para>
		/// </summary>
		/// <param name="sceneName">Name of the scene to process.</param>
		private async Task ProcessOpenWorldQueueAsync(string sceneName)
		{
			if (!TryGetDbService(out ISceneService sceneService) ||
				!TryGetDbService(out ISceneServerService sceneServerService) ||
				!TryGetDbService(out ICharacterService charService))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			int maxClientsPerInstance = GetMaxClients(sceneName);
			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;

			// Fetch available scene instances (cache-aware)
			var availableScenes = await FetchAvailableScenesAsync(sceneService, runtimeData, worldServerID, sceneName, maxClientsPerInstance);
			if (availableScenes == null || availableScenes.Count < 1)
			{
				await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
				return;
			}

			// Resolve all scene server addresses upfront and build a capacity tracker.
			// instanceHandles preserves insertion order for deterministic fallback assignment.
			var instanceHandles = new List<int>(availableScenes.Count);
			var serverInfoByHandle = new Dictionary<int, (string Address, ushort Port)>(availableScenes.Count);
			var capacityByHandle = new Dictionary<int, int>(availableScenes.Count);

			// Resolve scene server addresses: check cache first, batch-fetch any misses.
			TimeSpan serverTtl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			var uncachedServerIds = new List<long>();
			var scenesByServerId = new Dictionary<long, List<SceneData>>();

			foreach (var sceneData in availableScenes)
			{
				long serverId = sceneData.SceneServerID;

				// Try cache first
				if (serverTtl > TimeSpan.Zero &&
					runtimeData.SceneServerAddressCache.TryGet(serverId, serverTtl, out var cachedAddr))
				{
					int handle = sceneData.SceneHandle;
					instanceHandles.Add(handle);
					serverInfoByHandle[handle] = cachedAddr;
					capacityByHandle[handle] = maxClientsPerInstance - sceneData.CharacterCount;
					continue;
				}

				// Collect for batch fetch (deduplicate server IDs)
				if (!scenesByServerId.ContainsKey(serverId))
				{
					scenesByServerId[serverId] = new List<SceneData>();
					uncachedServerIds.Add(serverId);
				}
				scenesByServerId[serverId].Add(sceneData);
			}

			// Batch-fetch any cache-miss server addresses
			if (uncachedServerIds.Count > 0)
			{
				var batchResult = await sceneServerService.FetchSceneServersByIDsAsync(uncachedServerIds);
				if (batchResult.IsSuccess && batchResult.Data != null)
				{
					foreach (var serverData in batchResult.Data)
					{
						var addr = (serverData.Address, serverData.Port);
						if (serverTtl > TimeSpan.Zero)
						{
							runtimeData.SceneServerAddressCache.Set(serverData.ID, addr);
						}

						if (scenesByServerId.TryGetValue(serverData.ID, out var scenes))
						{
							foreach (var sd in scenes)
							{
								int handle = sd.SceneHandle;
								instanceHandles.Add(handle);
								serverInfoByHandle[handle] = addr;
								capacityByHandle[handle] = maxClientsPerInstance - sd.CharacterCount;
							}
						}
					}
				}
				else
				{
					// Invalidate stale entries for any servers that failed to fetch
					foreach (long serverId in uncachedServerIds)
					{
						runtimeData.SceneServerAddressCache?.Invalidate(serverId);
					}
				}
			}

			if (instanceHandles.Count < 1)
			{
				await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
				return;
			}

			// Snapshot ALL waiting connections for this scene name on the main thread
			List<(NetworkConnection conn, string accountName)> waitingConnections = new List<(NetworkConnection, string)>();

			if (!await RunOnMainThreadAsync(() =>
			{
				if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					return;
				}
				if (!mappingData.WaitingOpenWorldConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections) ||
					connections == null)
				{
					return;
				}

				// Copy to pre-sized list and clear set in bulk (avoids .ToList() allocation + per-element Remove)
				var snapshot = new List<NetworkConnection>(connections.Count);
				snapshot.AddRange(connections);
				connections.Clear();

				for (int i = 0; i < snapshot.Count; ++i)
				{
					NetworkConnection connection = snapshot[i];
					mappingData.OpenWorldConnectionScenes.Remove(connection);
					runtimeData.WaitingQueueEnteredUtcByClientId?.Remove(connection.ClientId);

					if (!IsValidConnection(connection, out string accountName))
					{
						continue;
					}

					waitingConnections.Add((connection, accountName));
				}
			}))
			{
				return;
			}

			// --- Fetch phase: batch-load character data before routing ---
			var charDataByConn = new Dictionary<NetworkConnection, CharacterData>(waitingConnections.Count);
			if (waitingConnections.Count > 0)
			{
				// Build account list and reverse lookup for mapping batch results back to connections
				var accountNames = new List<string>(waitingConnections.Count);
				var connByAccount = new Dictionary<string, NetworkConnection>(waitingConnections.Count, StringComparer.OrdinalIgnoreCase);
				foreach (var (conn, accountName) in waitingConnections)
				{
					accountNames.Add(accountName);
					connByAccount[accountName] = conn;
				}

				var batchResult = await charService.FetchSelectedCharactersByAccountsAsync(accountNames);
				if (batchResult.IsSuccess && batchResult.Data != null)
				{
					foreach (var charData in batchResult.Data)
					{
						if (charData.ID > 0 && connByAccount.TryGetValue(charData.Account, out var conn))
						{
							charDataByConn[conn] = charData;
						}
					}
				}
			}

			// --- Routing phase: two-pass assignment ---
			// Pass 1: Preferred handle assignments via O(1) dictionary lookup.
			// This supports channel switching: SceneChannelSystem updates the handle
			// before disconnect so the world server routes back to the chosen channel.
			// Preferred assignments never need a DB update (assignedHandle == charData.SceneHandle).
			var unassigned = new List<(NetworkConnection conn, string accountName, CharacterData charData)>();
			for (int i = 0; i < waitingConnections.Count; ++i)
			{
				var (conn, accountName) = waitingConnections[i];
				if (!charDataByConn.TryGetValue(conn, out var charData))
				{
					continue;
				}

				int preferredHandle = charData.SceneHandle;
				if (preferredHandle > 0 &&
					capacityByHandle.TryGetValue(preferredHandle, out int prefRemaining) &&
					prefRemaining > 0 &&
					serverInfoByHandle.TryGetValue(preferredHandle, out var prefServer))
				{
					capacityByHandle[preferredHandle] = prefRemaining - 1;
					BroadcastSceneConnect(conn, prefServer);
				}
				else
				{
					unassigned.Add((conn, accountName, charData));
				}
			}

			// Pass 2: Fallback assignments via capacity heap — O(log N) per connection.
			// Build the heap from remaining capacity after preferred assignments.
			if (unassigned.Count > 0)
			{
				var capacityHeap = new InstanceCapacityHeap(instanceHandles.Count);
				for (int i = 0; i < instanceHandles.Count; ++i)
				{
					int h = instanceHandles[i];
					if (capacityByHandle.TryGetValue(h, out int cap) && cap > 0)
					{
						capacityHeap.Push(h, cap);
					}
				}

				for (int i = 0; i < unassigned.Count; ++i)
				{
					var (conn, accountName, charData) = unassigned[i];

					if (!capacityHeap.TryAssignFromTop(out int assignedHandle) ||
						!serverInfoByHandle.TryGetValue(assignedHandle, out var server))
					{
						// No capacity anywhere — re-queue for the next processing cycle
						TryEnqueueMainThread(() =>
						{
							if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var md))
							{
								AddToQueue(conn, sceneName, md.WaitingOpenWorldConnections, md.OpenWorldConnectionScenes);
							}
						});
						continue;
					}

					// Fallback handles always differ from the character's saved handle
					if (charData.SceneHandle != assignedHandle)
					{
						await charService.UpdateSceneAsync(charData.ID, sceneName, assignedHandle);
					}

					BroadcastSceneConnect(conn, server);
				}
			}

			// Clean up empty queue entries and request a new scene if connections are still waiting
			await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
		}

		/// <summary>
		/// Cleans up empty waiting queue entries for a scene name and enqueues a new scene load
		/// request if connections are still waiting for capacity.
		/// </summary>
		/// <param name="sceneName">Name of the scene to check.</param>
		/// <param name="worldServerID">The world server ID for the scene enqueue request.</param>
		/// <param name="sceneService">The scene database service.</param>
		private async Task CleanupAndEnqueueNewSceneIfNeededAsync(string sceneName, long worldServerID, ISceneService sceneService)
		{
			bool needsNewScene = false;
			if (!await RunOnMainThreadAsync(() =>
			{
				if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					return;
				}
				if (!mappingData.WaitingOpenWorldConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections) ||
					connections == null ||
					connections.Count == 0)
				{
					mappingData.WaitingOpenWorldConnections.Remove(sceneName);
				}
				else
				{
					needsNewScene = true;
				}
			}))
			{
				return;
			}

			if (needsNewScene)
			{
				await sceneService.EnqueueAsync(worldServerID, sceneName, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.OpenWorld);
			}
		}

		/// <summary>
		/// Clears the IsInInstance flag on the character, persists the updated record, and falls back to the world scene.
		/// </summary>
		private async Task ClearInstanceFlagAndFallbackAsync(
			ICharacterService charService,
			CharacterData charData,
			int characterFlags,
			NetworkConnection conn,
			string accountName)
		{
			characterFlags.DisableBit(CharacterFlags.IsInInstance);
			var updatedChar = charData.WithFlagsVersionAndTimestamp(
				characterFlags,
				charData.Version + 1,
				DateTime.UtcNow
			);
			await charService.PersistAsync(updatedChar);
			await FallbackToWorldSceneAsync(conn, accountName);
		}

		/// <summary>
		/// Tries to process an Instance scene for the connection character otherwise falls back to the world scene.
		/// <para>
		/// The connection is removed from the instance queue immediately before async processing begins.
		/// This prevents the connection from being re-processed on the next cycle or receiving erroneous
		/// TTL kicks during the async window. If the instance is still loading, the connection is re-added
		/// to the queue. On failure, the connection falls back to the world scene queue.
		/// </para>
		/// </summary>
		/// <param name="conn">Network connection to process.</param>
		/// <param name="skipDebounce">When true, skips per-account debounce check because the caller already reserved the lookup window.</param>
		private async Task ProcessInstanceConnectionAsync(NetworkConnection conn, bool skipDebounce = false)
		{
			if (!TryGetDbService(out ICharacterService charService) ||
				!TryGetDbService(out ISceneService sceneService) ||
				!TryGetDbService(out ISceneServerService sceneServerService))
			{
				return;
			}

			// Validate the connection and remove from instance queue immediately on main thread.
			// This prevents re-processing, erroneous TTL kicks, and stale routing during the async window.
			string accountName = null;
			if (!await RunOnMainThreadAsync(() =>
			{
				if (!IsValidConnection(conn, out string acct))
				{
					Kick(conn, "Failed to get account name");
				}
				else
				{
					accountName = acct;
				}

				// Remove from queue before async work to prevent re-processing on the next cycle
				if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
				}
			}))
			{
				return;
			}

			if (string.IsNullOrEmpty(accountName))
			{
				return;
			}

			if (!skipDebounce && !TryBeginInstanceLookup(accountName))
			{
				return;
			}

			// Get the selected character data (single-row fetch, includes flags and instance info)
			var charResult = await charService.FetchByAccountAsync(accountName, selected: true);
			if (!charResult.IsSuccess || !charResult.Data.HasValue)
			{
				TryEnqueueMainThread(() => Kick(conn, "invalid character ID"));
				return;
			}
			var charData = charResult.Data.Value;
			int characterFlags = charData.Flags;

			if (!characterFlags.IsFlagged(CharacterFlags.IsInInstance))
			{
				await FallbackToWorldSceneAsync(conn, accountName);
				return;
			}

			long instanceID = charData.InstanceID;
			if (instanceID <= 0)
			{
				await ClearInstanceFlagAndFallbackAsync(charService, charData, characterFlags, conn, accountName);
				return;
			}

			var sceneResult = await sceneService.FetchAsync(instanceID);
			if (!sceneResult.IsSuccess)
			{
				await ClearInstanceFlagAndFallbackAsync(charService, charData, characterFlags, conn, accountName);
				return;
			}

			var sceneData = sceneResult.Data;
			FishMMO.Shared.SceneStatus sceneStatus = (FishMMO.Shared.SceneStatus)sceneData.SceneStatus;
			if (sceneStatus == FishMMO.Shared.SceneStatus.Ready)
			{
				// Ensure the Scene Server is running
				var sceneServerResult = await sceneServerService.FetchAsync(sceneData.SceneServerID);
				if (sceneServerResult.IsSuccess)
				{
					var sceneServer = sceneServerResult.Data;
					TryEnqueueMainThread(() =>
					{
						if (conn == null || !conn.IsActive)
						{
							return;
						}

						// Race guard: if connection was re-queued during async work, skip stale routing
						if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var guardMd) &&
							guardMd.InstanceConnectionScenes.ContainsKey(conn))
						{
							return;
						}

						Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					});
				}
				else
				{
					// Scene server unreachable — delete stale scene entry and fall back
					await sceneService.DeleteByHandleAsync(sceneData.SceneServerID, sceneData.SceneHandle);
					await ClearInstanceFlagAndFallbackAsync(charService, charData, characterFlags, conn, accountName);
				}
			}
			else if (sceneStatus == FishMMO.Shared.SceneStatus.Pending ||
					 sceneStatus == FishMMO.Shared.SceneStatus.Loading)
			{
				// Re-add to instance queue — scene is still loading
				TryEnqueueMainThread(() =>
				{
					if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
					{
						AddToQueue(conn, sceneData.ID, mappingData.WaitingInstanceConnections, mappingData.InstanceConnectionScenes);
					}
				});
			}
			else
			{
				// Unknown or terminal scene status — fall back to world scene
				await ClearInstanceFlagAndFallbackAsync(charService, charData, characterFlags, conn, accountName);
			}
		}

		/// <summary>
		/// Updates the total connection count by summing waiting and active connections across all scenes.
		/// </summary>
		private async Task UpdateConnectionCountAsync()
		{
			if (!TryGetDbService(out ISceneService sceneService))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Use cached count if still within TTL to avoid re-fetching all scene rows every cycle
			TimeSpan countCacheTtl = TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds);
			DateTime now = DateTime.UtcNow;
			int sceneCharacterCount;

			if (countCacheTtl > TimeSpan.Zero &&
				(now - runtimeData.CachedSceneCharacterCountUtc) < countCacheTtl)
			{
				sceneCharacterCount = runtimeData.CachedSceneCharacterCount;
			}
			else
			{
				long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;
				var scenesResult = await sceneService.FetchManyAsync(worldServerID);
				sceneCharacterCount = 0;
				if (scenesResult.IsSuccess && scenesResult.Data != null)
				{
					foreach (var scene in scenesResult.Data)
					{
						sceneCharacterCount += scene.CharacterCount;
					}
				}
				runtimeData.CachedSceneCharacterCount = sceneCharacterCount;
				runtimeData.CachedSceneCharacterCountUtc = now;
			}

			TryEnqueueMainThread(() =>
			{
				if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					return;
				}

				int waitingOpenWorldCount = 0;
				if (mappingData.WaitingOpenWorldConnections != null)
				{
					foreach (var kvp in mappingData.WaitingOpenWorldConnections)
					{
						waitingOpenWorldCount += kvp.Value.Count;
					}
				}
				int waitingInstanceCount = 0;
				if (mappingData.WaitingInstanceConnections != null)
				{
					foreach (var kvp in mappingData.WaitingInstanceConnections)
					{
						waitingInstanceCount += kvp.Value.Count;
					}
				}
				int totalCount = waitingOpenWorldCount + waitingInstanceCount + sceneCharacterCount;

				mappingData.ConnectionCount = totalCount;
			});
		}
		/// <summary>
		/// Handles authentication success callbacks and begins scene routing for authenticated clients.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="authenticated">True if client authenticated successfully.</param>
		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
			{
				return;
			}

			// Get the scene for the selected character
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				Kick(conn, "Failed to get account name");
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				Kick(conn, "Failed to access database or world server system");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				Kick(conn, "Failed to get world scene mapping data");
				return;
			}

			// Queue async instance connection processing
			if (!TryBeginInstanceLookup(accountName))
			{
				Kick(conn, "Instance routing rate limited");
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessInstanceConnectionAsync(conn, skipDebounce: true), conn.ClientId))
			{
				Kick(conn, "Failed to enqueue instance connection processing");
			}
		}

		/// <summary>
		/// Adds a connection to a queue for a scene or instance, updating both forward and reverse maps.
		/// </summary>
		/// <typeparam name="T">Type of the key (scene name or instance ID).</typeparam>
		/// <param name="conn">Network connection.</param>
		/// <param name="key">Scene name or instance ID.</param>
		/// <param name="queue">Queue mapping key to connections.</param>
		/// <param name="reverseMap">Reverse map from connection to key.</param>
		private void AddToQueue<T>(NetworkConnection conn, T key,
			Dictionary<T, HashSet<NetworkConnection>> queue,
			Dictionary<NetworkConnection, T> reverseMap)
		{
			// Defense-in-depth: cap total queue size to prevent unbounded memory growth.
			if (reverseMap.Count >= MAX_WAITING_QUEUE_SIZE)
			{
				Kick(conn, "Waiting queue capacity exceeded");
				return;
			}

			reverseMap[conn] = key;
			if (!queue.TryGetValue(key, out var set))
			{
				queue[key] = set = new HashSet<NetworkConnection>();
			}
			set.Add(conn);
			if (conn != null)
			{
				RecordQueueEntryTime(conn.ClientId);
			}
		}

		/// <summary>
		/// Removes a connection from a queue for a scene or instance, updating both forward and reverse maps.
		/// </summary>
		/// <typeparam name="T">Type of the key (scene name or instance ID).</typeparam>
		/// <param name="conn">Network connection.</param>
		/// <param name="reverseMap">Reverse map from connection to key.</param>
		/// <param name="queue">Queue mapping key to connections.</param>
		private void RemoveFromQueue<T>(NetworkConnection conn,
			Dictionary<NetworkConnection, T> reverseMap,
			Dictionary<T, HashSet<NetworkConnection>> queue)
		{
			if (!reverseMap.TryGetValue(conn, out var key)) return;
			if (queue.TryGetValue(key, out var set))
			{
				set.Remove(conn);
				if (set.Count == 0)
				{
					queue.Remove(key);
				}
			}
			reverseMap.Remove(conn);
			if (conn != null)
			{
				RemoveQueueEntryTime(conn.ClientId);
			}
		}

		/// <summary>
		/// Starts a debounced instance-lookup window for an account.
		/// </summary>
		/// <param name="accountName">Normalized account name key.</param>
		/// <returns><c>true</c> if lookup is allowed now; otherwise <c>false</c>.</returns>
		private bool TryBeginInstanceLookup(string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}

			return runtimeData.InstanceLookupDebounce.TryBegin(
				accountName,
				DateTime.UtcNow,
				TimeSpan.FromSeconds(instanceLookupDebounceSeconds));
		}

		/// <summary>
		/// Removes expired account debounce entries to bound dictionary growth.
		/// </summary>
		/// <param name="runtimeData">Cached runtime data from the caller to avoid redundant TryGet.</param>
		private void CleanupExpiredDebounceEntries(WorldSceneSystemRuntimeData runtimeData)
		{
			runtimeData.InstanceLookupDebounce.SweepExpired(
				DateTime.UtcNow,
				debounceCleanupMaxScanPerSweep,
				debounceCleanupMaxRemovalsPerSweep);
		}

		/// <summary>
		/// Purges stale waiting-queue connections by TTL and removes inactive entries.
		/// Active connections that exceed TTL are kicked after queue removal.
		/// </summary>
		/// <param name="runtimeData">Cached runtime data from the caller to avoid redundant TryGet.</param>
		private void PurgeExpiredWaitingConnections(WorldSceneSystemRuntimeData runtimeData)
		{
			if (waitingQueuePurgeMaxPerSweep <= 0)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			var staleConnections = new List<NetworkConnection>(waitingQueuePurgeMaxPerSweep);

			CollectStaleQueuedConnections(runtimeData, mappingData.OpenWorldConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep);
			if (staleConnections.Count < waitingQueuePurgeMaxPerSweep)
			{
				CollectStaleQueuedConnections(runtimeData, mappingData.InstanceConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep - staleConnections.Count);
			}

			if (staleConnections.Count == 0)
			{
				return;
			}

			var seen = new HashSet<NetworkConnection>(staleConnections.Count);
			for (int i = 0; i < staleConnections.Count; ++i)
			{
				NetworkConnection conn = staleConnections[i];
				if (conn == null || !seen.Add(conn))
				{
					continue;
				}

				bool shouldKick = false;
				if (conn.IsActive &&
					runtimeData.WaitingQueueEnteredUtcByClientId.TryGetValue(conn.ClientId, out DateTime queuedAt))
				{
					shouldKick = (now - queuedAt).TotalSeconds >= waitingQueueTtlSeconds;
				}

				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);

				if (conn.IsActive && shouldKick)
				{
					Kick(conn, "Waiting queue TTL exceeded");
				}
			}
		}

		/// <summary>
		/// Collects stale queued connections from a source set based on activity and queue age.
		/// </summary>
		/// <param name="runtimeData">Cached runtime data from the caller to avoid redundant TryGet.</param>
		/// <param name="source">Source connection set snapshot input.</param>
		/// <param name="staleConnections">Output list to append stale connections into.</param>
		/// <param name="now">Current UTC timestamp used for TTL comparisons.</param>
		/// <param name="maxToCollect">Maximum stale connections to collect in this pass.</param>
		private void CollectStaleQueuedConnections(
			WorldSceneSystemRuntimeData runtimeData,
			IEnumerable<NetworkConnection> source,
			List<NetworkConnection> staleConnections,
			DateTime now,
			int maxToCollect)
		{
			if (source == null || staleConnections == null || maxToCollect <= 0)
			{
				return;
			}

			foreach (NetworkConnection conn in source)
			{
				if (staleConnections.Count >= maxToCollect)
				{
					break;
				}

				if (conn == null)
				{
					continue;
				}

				if (!conn.IsActive)
				{
					staleConnections.Add(conn);
					continue;
				}

				if (!runtimeData.WaitingQueueEnteredUtcByClientId.TryGetValue(conn.ClientId, out DateTime queuedAt))
				{
					runtimeData.WaitingQueueEnteredUtcByClientId[conn.ClientId] = now;
					continue;
				}

				if ((now - queuedAt).TotalSeconds >= waitingQueueTtlSeconds)
				{
					staleConnections.Add(conn);
				}
			}
		}

		/// <summary>
		/// Fallbacks a connection to the world scene if instance scene assignment fails.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="accountName">Account name for the connection.</param>
		private async Task FallbackToWorldSceneAsync(NetworkConnection conn, string accountName)
		{
			if (!TryGetDbService(out ICharacterService charService))
			{
				return;
			}

			var fetchResult = await charService.FetchByAccountAsync(accountName, selected: true);
			if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
			{
				TryEnqueueMainThread(() => Kick(conn, "Failed to get selected scene"));
				return;
			}
			var selectedChar = fetchResult.Data.Value;
			if (selectedChar.ID <= 0 || string.IsNullOrEmpty(selectedChar.SceneName))
			{
				TryEnqueueMainThread(() => Kick(conn, "Failed to get selected scene"));
				return;
			}
			string sceneName = selectedChar.SceneName;

			TryEnqueueMainThread(() =>
			{
				if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
					AddToQueue(conn, sceneName, mappingData.WaitingOpenWorldConnections, mappingData.OpenWorldConnectionScenes);
				}
			});
		}

		/// <summary>
		/// Checks if a connection is valid and retrieves the account name.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="accountName">Output account name.</param>
		/// <returns>True if connection is valid and account name is found.</returns>
		private bool IsValidConnection(NetworkConnection conn, out string accountName)
		{
			accountName = null;
			return conn != null && conn.IsActive && Server.AccountManager.GetAccountNameByConnection(conn, out accountName);
		}

		/// <summary>
		/// Kicks a connection from the server with a specified reason.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="reason">Reason for kicking.</param>
		private void Kick(NetworkConnection conn, string reason)
		{
			Log.Debug("WorldSceneSystem", $"World Scene System: {conn.ClientId} {reason}.");
			conn.Kick(KickReason.UnexpectedProblem);
		}

		/// <summary>
		/// Enqueues a race-guarded <see cref="WorldSceneConnectBroadcast"/> for a connection.
		/// Skips the broadcast if the connection has been re-queued during async processing.
		/// </summary>
		/// <param name="conn">Target network connection.</param>
		/// <param name="server">Scene server address and port to broadcast.</param>
		private void BroadcastSceneConnect(NetworkConnection conn, (string Address, ushort Port) server)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive)
				{
					return;
				}

				// Race guard: if connection was re-queued during async work, skip stale routing
				if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var guardMd) &&
					guardMd.OpenWorldConnectionScenes.ContainsKey(conn))
				{
					return;
				}

				Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
				{
					Address = server.Address,
					Port = server.Port,
				});
			});
		}

		/// <summary>
		/// Records the UTC timestamp when a client entered a waiting queue.
		/// Uses a cached <see cref="WorldSceneSystemRuntimeData"/> reference to avoid repeated TryGet in loops.
		/// </summary>
		/// <param name="clientId">FishNet client ID.</param>
		private void RecordQueueEntryTime(int clientId)
		{
			if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.WaitingQueueEnteredUtcByClientId[clientId] = DateTime.UtcNow;
			}
		}

		/// <summary>
		/// Removes a client's queue-entry timestamp.
		/// </summary>
		/// <param name="clientId">FishNet client ID.</param>
		private void RemoveQueueEntryTime(int clientId)
		{
			if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.WaitingQueueEnteredUtcByClientId?.Remove(clientId);
			}
		}

		/// <summary>
		/// Gets the maximum number of clients allowed for a given scene, using cached details if available.
		/// </summary>
		/// <param name="sceneName">Name of the scene.</param>
		/// <returns>Maximum number of clients for the scene.</returns>
		public int GetMaxClients(string sceneName)
		{
			if (worldSceneDetailsCache?.Scenes?.TryGetValue(sceneName, out var details) == true)
			{
				return Mathf.Clamp(details.MaxClients, 1, MAX_CLIENTS_PER_INSTANCE);
			}
			return MAX_CLIENTS_PER_INSTANCE;
		}

		#region Scene Instance Cache

		/// <summary>
		/// Fetches available scene instances for a scene name, using the cache when valid.
		/// Falls through to <c>ISceneService.FetchAvailableAsync</c> on cache miss or when
		/// caching is disabled (<see cref="sceneInstanceCacheTtlSeconds"/> = 0).
		/// </summary>
		/// <returns>The list of available <see cref="SceneData"/>, or <c>null</c> on failure.</returns>
		private async Task<IReadOnlyList<SceneData>> FetchAvailableScenesAsync(
			ISceneService sceneService,
			WorldSceneSystemRuntimeData runtimeData,
			long worldServerID,
			string sceneName,
			int maxClients)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.AvailableSceneCache.TryGet(sceneName, ttl, out var cached))
			{
				return cached;
			}

			var result = await sceneService.FetchAvailableAsync(worldServerID, sceneName, maxClients);
			if (!result.IsSuccess || result.Data == null || result.Data.Count < 1)
			{
				return null;
			}

			if (ttl > TimeSpan.Zero)
			{
				runtimeData.AvailableSceneCache.Set(sceneName, result.Data);
			}
			return result.Data;
		}

		/// <summary>
		/// Fetches a scene server's address, using the cache when valid.
		/// Falls through to <c>ISceneServerService.FetchAsync</c> on cache miss or when
		/// caching is disabled (<see cref="sceneServerCacheTtlSeconds"/> = 0).
		/// </summary>
		/// <returns>The scene server address and port, or <c>null</c> on failure.</returns>
		private async Task<(string Address, ushort Port)?> FetchSceneServerAddressAsync(
			ISceneServerService sceneServerService,
			WorldSceneSystemRuntimeData runtimeData,
			long sceneServerID)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.SceneServerAddressCache.TryGet(sceneServerID, ttl, out var cached))
			{
				return cached;
			}

			var result = await sceneServerService.FetchAsync(sceneServerID);
			if (!result.IsSuccess)
			{
				// Invalidate any stale cached entry so subsequent calls re-fetch immediately
				runtimeData.SceneServerAddressCache?.Invalidate(sceneServerID);
				return null;
			}

			var addr = (result.Data.Address, result.Data.Port);
			if (ttl > TimeSpan.Zero)
			{
				runtimeData.SceneServerAddressCache.Set(sceneServerID, addr);
			}
			return addr;
		}

		/// <summary>
		/// Sweeps expired entries from both scene instance and scene server address caches.
		/// Called during the existing debounce cleanup cycle to avoid an extra timer.
		/// </summary>
		private void SweepSceneCaches(WorldSceneSystemRuntimeData runtimeData)
		{
			DateTime now = DateTime.UtcNow;
			runtimeData.AvailableSceneCache?.SweepExpired(
				now, TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds), 64, 32);
			runtimeData.SceneServerAddressCache?.SweepExpired(
				now, TimeSpan.FromSeconds(sceneServerCacheTtlSeconds), 64, 32);
		}

		#endregion
	}
}