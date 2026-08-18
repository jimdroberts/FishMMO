using FishNet.Connection;
using FishNet.Managing.Server;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
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

		/// <summary>
		/// Maximum time a shutdown database call may block the main thread. Process exit must
		/// not wait on an unresponsive database.
		/// </summary>
		private const int dbShutdownTimeoutMs = 5_000;

		/// <summary>
		/// Minimum seconds between instance DB routing lookups for the same account.
		/// </summary>
		[Header("Connection Routing Hardening")]
		[Tooltip("Minimum seconds between instance DB routing lookups for the same account")]
		[SerializeField] private float instanceLookupDebounceSeconds = 3.0f;

		/// <summary>
		/// Maximum seconds a connection may remain in waiting queues before being purged.
		/// </summary>
		[Tooltip("Maximum seconds a connection may remain in waiting queues before being purged")]
		[SerializeField] private float waitingQueueTtlSeconds = 45.0f;

		/// <summary>
		/// Seconds between stale waiting-queue purge sweeps.
		/// </summary>
		[Tooltip("Seconds between stale waiting-queue purge sweeps")]
		[SerializeField] private float waitingQueueSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Seconds between stale debounce-entry cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between stale debounce-entry cleanup sweeps")]
		[SerializeField] private float debounceCleanupIntervalSeconds = 60.0f;

		/// <summary>
		/// Max account debounce entries scanned per cleanup sweep.
		/// </summary>
		[Tooltip("Max account debounce entries scanned per cleanup sweep")]
		[SerializeField] private int debounceCleanupMaxScanPerSweep = 256;

		/// <summary>
		/// Max account debounce entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Max account debounce entries removed per cleanup sweep")]
		[SerializeField] private int debounceCleanupMaxRemovalsPerSweep = 128;

		/// <summary>
		/// Max stale queued connections purged per waiting-queue sweep.
		/// </summary>
		[Tooltip("Max stale queued connections purged per waiting-queue sweep")]
		[SerializeField] private int waitingQueuePurgeMaxPerSweep = 128;

		/// <summary>
		/// Seconds before cached scene-instance query results expire and are re-fetched from the database.
		/// Set to 0 to disable caching.
		/// </summary>
		[Header("Scene Instance Cache")]
		[Tooltip("Seconds before cached scene-instance query results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneInstanceCacheTtlSeconds = 5.0f;

		/// <summary>
		/// Seconds before cached scene-server address results expire and are re-fetched from the database.
		/// Set to 0 to disable caching.
		/// </summary>
		[Tooltip("Seconds before cached scene-server address results expire and are re-fetched from the database. Set to 0 to disable caching.")]
		[SerializeField] private float sceneServerCacheTtlSeconds = 10.0f;

		/// <summary>
		/// Maximum number of clients allowed per scene instance.
		/// </summary>
		private const int MaxClientsPerInstance = 500;

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

			Log.Debug("WorldSceneSystem", $"Initialized (WaitQueueRate={runtimeData.WaitQueueRateSeconds}s, MaxClientsPerInstance={MaxClientsPerInstance})");
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
					if (UnitySyncOverAsync.TryRun(
						cancellationToken => sceneService.DeleteByWorldServerAsync(worldData.ID, cancellationToken),
						out DatabaseResult<int> deleteResult,
						dbShutdownTimeoutMs))
					{
						if (!deleteResult.IsSuccess)
						{
							Log.Warning("WorldSceneSystem", $"Failed to delete world scenes during shutdown (WorldServerID={worldData.ID}): [{deleteResult.ErrorCode}] {deleteResult.ErrorMessage}");
						}
					}
					else
					{
						Log.Warning("WorldSceneSystem", $"World scene deletion timed out after {dbShutdownTimeoutMs}ms (WorldServerID={worldData.ID})");
					}
				}
				catch (Exception ex)
				{
					Log.Error("WorldSceneSystem", $"Failed to delete world scenes during shutdown: {ex}");
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
					tcs.TrySetResult(true);
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
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

			runtimeData.NextWaitQueueUpdate -= deltaTime;
			if (runtimeData.NextWaitQueueUpdate <= 0f)
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
			var serverInfoByHandle = new Dictionary<int, ushort>(availableScenes.Count);
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
						ushort serverPort = (ushort)serverData.Port;
						if (serverTtl > TimeSpan.Zero)
						{
							runtimeData.SceneServerAddressCache.Set(serverData.ID, serverPort);
						}

						if (scenesByServerId.TryGetValue(serverData.ID, out var scenes))
						{
							foreach (var sd in scenes)
							{
								int handle = sd.SceneHandle;
								instanceHandles.Add(handle);
								serverInfoByHandle[handle] = serverPort;
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
				bool preferredAvailable = preferredHandle != 0 &&
					capacityByHandle.TryGetValue(preferredHandle, out int prefCheck) &&
					prefCheck > 0 &&
					serverInfoByHandle.ContainsKey(preferredHandle);

				/* A combat-logout body exists on exactly ONE scene server, and only that server
				 * can hand it back — it still holds the character's session claim. Falling back
				 * to a different instance sends the player somewhere that cannot claim them, so
				 * they are kicked, reconnect, and are kicked again until the linger expires.
				 * Hold them in the queue instead of routing them somewhere useless.
				 *
				 * The wait is bounded from both ends: a live scene server clears the flag when
				 * the linger ends, and if the server died instead the grace below expires. The
				 * grace deliberately exceeds the database session lease, so by the time we give
				 * up, a dead server's claim has lapsed and the destination can actually take
				 * the character rather than kicking it for contention. */
				if (!preferredAvailable && charData.Flags.IsFlagged(CharacterFlags.IsCombatLogged))
				{
					DateTime deferredSince = combatLogoutRoutingDeferredSince.GetOrAdd(charData.ID, _ => DateTime.UtcNow);
					if ((DateTime.UtcNow - deferredSince).TotalSeconds < CombatLogoutRoutingGraceSeconds)
					{
						await Log.Debug("WorldSceneSystem",
							$"Holding character {charData.ID} for scene handle {preferredHandle}: its combat-logout body lives there.");
						RequeueOpenWorldConnection(conn, sceneName);
						continue;
					}

					// The owning scene instance never came back. Treat the body as gone — the
					// whole scene was scrubbed with the server — and let the character in.
					combatLogoutRoutingDeferredSince.TryRemove(charData.ID, out _);
					await Log.Warning("WorldSceneSystem",
						$"Character {charData.ID} waited {CombatLogoutRoutingGraceSeconds}s for scene handle {preferredHandle} and it never returned; " +
						"clearing its combat-logout flag and routing normally.");

					DatabaseResult clearResult = await charService.ClearCombatLoggedAsync(charData.ID);
					if (!clearResult.IsSuccess)
					{
						await Log.Warning("WorldSceneSystem",
							$"Failed to clear combat-logout flag for character {charData.ID}: {clearResult.ErrorCode} - {clearResult.ErrorMessage}");
					}
				}
				else if (preferredAvailable)
				{
					combatLogoutRoutingDeferredSince.TryRemove(charData.ID, out _);
				}

				if (preferredHandle != 0 &&
					capacityByHandle.TryGetValue(preferredHandle, out int prefRemaining) &&
					prefRemaining > 0 &&
					serverInfoByHandle.TryGetValue(preferredHandle, out var prefServer))
				{
					capacityByHandle[preferredHandle] = prefRemaining - 1;

					// After a World restart, the character's saved world_server_id or scene
					// may be stale. Rebind to the current world instance before broadcasting
					// so the Scene server accepts the connection (world+scene+handle must match).
					// This mirrors the Pass 2 rebind logic below.
					if (charData.SceneHandle != preferredHandle || charData.WorldServerID != worldServerID)
					{
						// Awaited, not fire-and-forget: BroadcastSceneConnect below sends the
						// client straight to the Scene Server, which matches it on the
						// (world_server_id, scene_name, scene_handle) tuple read back from this
						// row. Racing the write against that lookup rebinds the character too
						// late and the Scene Server rejects it as a mismatched scene handle.
						DatabaseResult rebindResult = await charService.UpdateSceneAsync(charData.ID, worldServerID, sceneName, preferredHandle);
						if (!rebindResult.IsSuccess)
						{
							await Log.Warning("WorldSceneSystem", $"Pass1 rebind DB error (CharID={charData.ID}): {rebindResult.ErrorCode} - {rebindResult.ErrorMessage}");
						}
						await Log.Info("WorldSceneSystem", $"Pass1 rebind: Character {charData.ID} world={charData.WorldServerID}->{worldServerID} scene={charData.SceneHandle}->{preferredHandle}");
					}

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
						!serverInfoByHandle.TryGetValue(assignedHandle, out var serverPort))
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
					if (charData.SceneHandle != assignedHandle || charData.WorldServerID != worldServerID)
					{
						DatabaseResult updateResult = await charService.UpdateSceneAsync(charData.ID, worldServerID, sceneName, assignedHandle);
						if (!updateResult.IsSuccess)
						{
							await Log.Warning("WorldSceneSystem", $"UpdateSceneAsync DB error (CharID={charData.ID}): {updateResult.ErrorCode} - {updateResult.ErrorMessage}");
						}
					}

					BroadcastSceneConnect(conn, serverPort);
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
				DatabaseResult<long> enqueueResult = await sceneService.EnqueueAsync(worldServerID, sceneName, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.OpenWorld);
				if (!enqueueResult.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem", $"CleanupAndEnqueueNewSceneIfNeededAsync DB error (Scene={sceneName}): {enqueueResult.ErrorCode} - {enqueueResult.ErrorMessage}");
				}
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
			DatabaseResult persistResult = await charService.PersistAsync(updatedChar);
			if (!persistResult.IsSuccess)
			{
				await Log.Warning("WorldSceneSystem", $"ClearInstanceFlagAndFallbackAsync DB error (CharID={charData.ID}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
			}
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
				// Re-queue: instance lookup was rate-limited. The connection was
				// already removed from the queue above; re-add it so the next
				// processing cycle picks it up.
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive &&
						Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var md))
					{
						AddToQueue(conn, 0L, md.WaitingInstanceConnections, md.InstanceConnectionScenes);
					}
				});
				return;
			}

			try
			{
			// Get the selected character data (single-row fetch, includes flags and instance info)
			var charResult = await charService.FetchByAccountAsync(accountName, selected: true);
			if (!charResult.IsSuccess || !charResult.Data.HasValue)
			{
				TryEnqueueMainThread(() => Kick(conn, "invalid character ID"));
				return;
			}
			var charData = charResult.Data.Value;
			int characterFlags = charData.Flags;

			// Bind the character to this world server before any routing decision.
			// The instance path below broadcasts WorldSceneConnectBroadcast without touching
			// the character row, so unlike the open-world path (which rebinds through
			// UpdateSceneAsync) nothing else would refresh world_server_id here — and the
			// Scene Server matches the arriving character on (world_server_id, scene,
			// handle), rejecting it as mismatched when world_server_id is stale from a
			// previous world instance or still 0 from character creation.
			long currentWorldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;
			if (currentWorldServerID > 0 && charData.WorldServerID != currentWorldServerID)
			{
				DatabaseResult bindResult = await charService.UpdateSceneAsync(charData.ID, currentWorldServerID, charData.SceneName, charData.SceneHandle);
				if (!bindResult.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem", $"Failed to bind character {charData.ID} to world server {currentWorldServerID}: {bindResult.ErrorCode} - {bindResult.ErrorMessage}");
				}
			}

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
							Port = (ushort)sceneServer.Port,
						});
					});
				}
				else
				{
					// Scene server unreachable — delete stale scene entry and fall back
					DatabaseResult deleteResult = await sceneService.DeleteByHandleAsync(sceneData.SceneServerID, sceneData.SceneHandle);
					if (!deleteResult.IsSuccess)
					{
						await Log.Warning("WorldSceneSystem", $"ProcessInstanceConnectionAsync scene delete failed (SceneServerID={sceneData.SceneServerID}, Handle={sceneData.SceneHandle}): {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
					}
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
			catch (Exception ex)
			{
				// Re-queue on unexpected async failure. The connection was removed
				// from the instance queue before async work began. If anything
				// throws during processing, re-add to the queue so the connection
				// is not orphaned and will be retried on the next cycle.
				await Log.Error("WorldSceneSystem", $"ProcessInstanceConnectionAsync error for {accountName}: {ex}");
				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive &&
						Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var md))
					{
						AddToQueue(conn, 0L, md.WaitingInstanceConnections, md.InstanceConnectionScenes);
					}
				});
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
		/// When each character was first deferred because its combat-logout body's scene
		/// instance was unavailable, keyed by character ID.
		/// </summary>
		/// <remarks>
		/// Needed because the queue snapshot clears <c>WaitingQueueEnteredUtcByClientId</c> and
		/// re-queuing resets it, so the queue's own timestamp cannot measure how long we have
		/// been holding out for a specific scene instance.
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> combatLogoutRoutingDeferredSince =
			new System.Collections.Concurrent.ConcurrentDictionary<long, DateTime>();

		/// <summary>
		/// How long to hold a combat-logged character waiting for its own scene instance before
		/// concluding the owning scene server is gone.
		/// </summary>
		/// <remarks>
		/// Must exceed the database session lease (2 minutes) so that when we do give up, the
		/// dead server's claim has already expired and the character can be claimed elsewhere
		/// instead of being kicked for contention.
		/// </remarks>
		private const double CombatLogoutRoutingGraceSeconds = 150.0;

		/// <summary>
		/// Puts a connection back on the open-world waiting queue for another routing pass.
		/// </summary>
		/// <remarks>
		/// The routing snapshot empties the queue, so any connection this pass declines to route
		/// is dropped entirely — it would sit on the world server with no scene assignment and
		/// no retry, which the client cannot recover from on its own.
		/// </remarks>
		private void RequeueOpenWorldConnection(NetworkConnection conn, string sceneName)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn == null || !conn.IsActive)
				{
					return;
				}
				if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					AddToQueue(conn, sceneName, mappingData.WaitingOpenWorldConnections, mappingData.OpenWorldConnectionScenes);
				}
			});
		}

		/// <summary>
		/// Enqueues a race-guarded <see cref="WorldSceneConnectBroadcast"/> for a connection.
		/// Skips the broadcast if the connection has been re-queued during async processing.
		/// </summary>
		/// <param name="conn">Target network connection.</param>
		/// <param name="port">Scene server port to broadcast (address is always GameHost).</param>
		private void BroadcastSceneConnect(NetworkConnection conn, ushort port)
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

				Log.Info("WorldSceneSystem", $"BroadcastSceneConnect conn={conn.ClientId} -> port={port}");
				Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
				{
					Port = port,
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
				return Mathf.Clamp(details.MaxClients, 1, MaxClientsPerInstance);
			}
			return MaxClientsPerInstance;
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
		private async Task<ushort?> FetchSceneServerAddressAsync(
			ISceneServerService sceneServerService,
			WorldSceneSystemRuntimeData runtimeData,
			long sceneServerID)
		{
			TimeSpan ttl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			if (ttl > TimeSpan.Zero &&
				runtimeData.SceneServerAddressCache.TryGet(sceneServerID, ttl, out ushort cached))
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

			ushort port = (ushort)result.Data.Port;
			if (ttl > TimeSpan.Zero)
			{
				runtimeData.SceneServerAddressCache.Set(sceneServerID, port);
			}
			return port;
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
			SweepCombatLogoutRoutingDeferrals(now);
		}

		/// <summary>
		/// Drops combat-logout routing deferrals that can no longer be acted on.
		/// </summary>
		/// <remarks>
		/// Entries are normally removed the moment a character is routed or its grace expires,
		/// but a player who gives up and closes the client mid-deferral leaves theirs behind with
		/// nothing to clear it. Each is only a character ID and a timestamp, yet on a long-lived
		/// world server the map would accumulate one per character that ever combat-logged and
		/// walked away, and never shrink.
		/// <para>
		/// Anything older than twice the grace has already had its decision made — the routing
		/// pass either honoured it or timed it out — so it cannot influence a future pass and is
		/// safe to forget. A returning player simply starts a fresh deferral.
		/// </para>
		/// </remarks>
		private void SweepCombatLogoutRoutingDeferrals(DateTime nowUtc)
		{
			if (combatLogoutRoutingDeferredSince.IsEmpty)
			{
				return;
			}

			foreach (var kvp in combatLogoutRoutingDeferredSince)
			{
				if ((nowUtc - kvp.Value).TotalSeconds >= CombatLogoutRoutingGraceSeconds * 2.0)
				{
					combatLogoutRoutingDeferredSince.TryRemove(kvp.Key, out _);
				}
			}
		}

		#endregion
	}
}