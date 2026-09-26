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
		/// Seconds an account's place in the open-world queue is held after its wait ended without
		/// its say, for a reconnect or a "Try again" to resume. 0 holds nothing.
		/// </summary>
		/// <remarks>See <see cref="WorldQueuePlaceMemory"/>.</remarks>
		[Tooltip("Seconds an account's place in the open-world queue is held after a disconnect or a stalled-line purge, so rejoining the same scene's queue resumes it. 0 disables.")]
		[SerializeField] private float queuePlaceGraceSeconds = 60.0f;

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
		/// Seconds between scene-routing queue position broadcasts.
		/// </summary>
		/// <remarks>
		/// Mirrors the LoginServer's <c>LoginQueueUpdateRateSeconds</c>. Positions are sent on
		/// the unreliable channel and re-sent every sweep, so this is a display cadence rather
		/// than a delivery guarantee.
		/// </remarks>
		[Tooltip("Seconds between scene-routing queue position broadcasts sent to waiting clients")]
		[SerializeField] private float queuePositionUpdateRateSeconds = 2.0f;

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
		/// <remarks>
		/// Deferred to <see cref="WorldSceneSettings.MaximumClientsPerScene"/> rather than declared
		/// independently: this value and the scene server's admission cap have to agree, or the
		/// world server routes players into a scene the scene server will then refuse.
		/// </remarks>
		private const int MaxClientsPerInstance = WorldSceneSettings.MaximumClientsPerScene;

		/// <summary>
		/// Ceiling on how many instances of one open-world scene may be loading at the same time.
		/// </summary>
		/// <remarks>
		/// The routing pass derives the number it wants from the waiting population, and that
		/// population is not always a real demand signal — a scene that fails to load keeps its
		/// queue, and the queue keeps growing while it does. This bounds what a pathological
		/// queue can ask a scene server pool to do in one go.
		/// </remarks>
		private const int MaxOutstandingSceneLoads = 8;

		/// <summary>
		/// How many instance-queue connections one routing batch takes on.
		/// </summary>
		/// <remarks>
		/// A batch costs the same few batched reads and main-thread hops whatever its size (see
		/// <see cref="ProcessInstanceBatchAsync"/>), so this bounds how much one batch holds and
		/// how much main-thread work its outcome is, not how many round trips it makes. The queue
		/// can legitimately hold thousands.
		/// </remarks>
		private const int InstanceRoutingBatchSize = 256;

		/// <summary>
		/// How long an instance scene row may sit in a non-ready state before a character bound
		/// to it is released and routed to the open world instead.
		/// </summary>
		/// <remarks>
		/// Measured from the row's creation, by the database clock that stamped it, so the decision
		/// survives the client's reconnect cycle and no host clock can shift it. See
		/// <see cref="WorldSceneRoutingRules.DecideInstanceRoute"/> for why a per-connection timeout
		/// cannot do this job.
		/// <para>
		/// Comfortably above the scene server's own pending-scene timeout (60s) plus a cold
		/// scene load, so a merely slow load is never mistaken for a dead one.
		/// </para>
		/// </remarks>
		private const double InstanceReadyGraceSeconds = 180.0;

		/// <summary>
		/// Age at which a scene row that never reached Ready is deleted outright.
		/// </summary>
		/// <remarks>
		/// Strictly greater than <see cref="InstanceReadyGraceSeconds"/> so the routing path
		/// gets the chance to release characters itself, with a specific log line, before the
		/// sweep removes the evidence. Both outcomes are correct — a deleted row makes the
		/// instance fetch fail, which routes to the same fallback — but the ordering makes
		/// operational diagnosis possible.
		/// </remarks>
		private const double StaleSceneRowGraceSeconds = 300.0;

		/// <summary>Seconds between stale scene-row sweeps.</summary>
		private const float StaleSceneRowSweepIntervalSeconds = 60.0f;

		/// <summary>Maximum stale scene rows deleted per sweep.</summary>
		private const int StaleSceneRowMaxPerSweep = 256;

		/// <summary>
		/// How long a scene server may go without pulsing before its scene rows are reaped.
		/// </summary>
		/// <remarks>
		/// An order of magnitude above the default 5s scene-server pulse rate, so a slow pulse or
		/// a brief database stall never deletes the scenes of a healthy server, and under the
		/// two-minute session lease so a dead server's characters are freed rather than left
		/// pointing at scenes that no longer exist.
		/// <para>
		/// Internal because the scene servers' channel list reads it too: offering a channel the
		/// world server will not route to lets a player spend their switch on a destination they
		/// are then sent away from. It was two copies of the same number (issue #267).
		/// </para>
		/// </remarks>
		internal const double SceneServerPulseStaleSeconds = 60.0;

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

			runtimeData.LoginAuthenticator = FindAnyObjectByType<WorldServerAuthenticator>();
			if (runtimeData.LoginAuthenticator == null)
			{
				Log.Error("WorldSceneSystem", "Failed to initialize: WorldServerAuthenticator not found");
				throw new UnityException("WorldServerAuthenticator not found!");
			}

			// Connection state events
			SubscribeToConnectionEvents();

			// Authentication events
			runtimeData.LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

			// A player leaving the queue on purpose, as opposed to a dropped connection.
			Server.NetworkWrapper?.RegisterBroadcast<WorldSceneQueueLeaveBroadcast>(OnWorldSceneQueueLeave, true);

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			instanceLookupDebounceSeconds = Mathf.Max(0.1f, instanceLookupDebounceSeconds);
			waitingQueueTtlSeconds = Mathf.Max(5.0f, waitingQueueTtlSeconds);
			queuePlaceGraceSeconds = Mathf.Max(0.0f, queuePlaceGraceSeconds);
			queuePlaces.Clear();
			queuePlaces.GraceSeconds = queuePlaceGraceSeconds;
			waitingQueueSweepIntervalSeconds = Mathf.Max(1.0f, waitingQueueSweepIntervalSeconds);
			debounceCleanupIntervalSeconds = Mathf.Max(5.0f, debounceCleanupIntervalSeconds);
			debounceCleanupMaxScanPerSweep = Mathf.Max(1, debounceCleanupMaxScanPerSweep);
			debounceCleanupMaxRemovalsPerSweep = Mathf.Max(1, debounceCleanupMaxRemovalsPerSweep);
			waitingQueuePurgeMaxPerSweep = Mathf.Max(1, waitingQueuePurgeMaxPerSweep);
			queuePositionUpdateRateSeconds = Mathf.Max(0.5f, queuePositionUpdateRateSeconds);
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
			// Before any early return: this is process-local state with no dependencies.
			ClearResidencyWatchdog();
			combatLogoutRoutingDeferredSince.Clear();
			queueReasonByClientId.Clear();
			queueReasonByScene.Clear();
			lastPlacementAtByScene.Clear();
			waitingSinceByClientId.Clear();
			routedLastCycleByScene.Clear();
			queuePlaces.Clear();
			openWorldPlaceByClientId.Clear();
			leftQueueClientIds.Clear();

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
			runtimeData.WaitingQueueEnteredAtByClientId?.Clear();
			runtimeData.AvailableSceneCache?.Clear();
			runtimeData.SceneServerAddressCache?.Clear();

			// Connection state events
			UnsubscribeFromConnectionEvents();
			Server.NetworkWrapper?.UnregisterBroadcast<WorldSceneQueueLeaveBroadcast>(OnWorldSceneQueueLeave);

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

		/// <summary>Drops all residency-watchdog state. Called during teardown.</summary>
		private void ClearResidencyWatchdog() => worldResidencyDeadlineByClientId.Clear();

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IWorldSceneSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Seconds an async worker will wait for a queued main-thread action before giving up.
		/// </summary>
		/// <remarks>
		/// A successful enqueue only promises the action is <em>queued</em>; it is run by
		/// <see cref="OnUpdate"/>, which stops being called the moment this behaviour is
		/// deinitialized or the server stops. Anything queued after the single drain in
		/// <see cref="OnDeinitialize"/> therefore never runs, and a caller awaiting it waited
		/// forever — holding an async-worker slot for the life of the process. Enough of those
		/// (one per queue-processing cycle that straddled a shutdown) starve the shared worker
		/// pool, at which point every system's database work is silently rejected.
		/// <para>
		/// The bound is well beyond any legitimate main-thread stall; reaching it means the
		/// queue is not being drained at all, and the caller's own failure path (abandon this
		/// routing pass, connections stay queued for the next one) is the correct response.
		/// </para>
		/// </remarks>
		private const int MainThreadDispatchTimeoutMs = 30_000;

		/// <summary>
		/// Enqueues an action on the main thread and returns a task that completes when the action finishes.
		/// If the enqueue fails (queue full or unavailable), the returned task completes immediately with false.
		/// If the queued action is never drained, the task completes with false after
		/// <see cref="MainThreadDispatchTimeoutMs"/> rather than hanging.
		/// </summary>
		private async Task<bool> RunOnMainThreadAsync(Action action)
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
				return false;
			}

			using (var timeoutCts = new System.Threading.CancellationTokenSource())
			{
				Task timeoutTask = Task.Delay(MainThreadDispatchTimeoutMs, timeoutCts.Token);
				Task completed = await Task.WhenAny(tcs.Task, timeoutTask);
				if (!ReferenceEquals(completed, tcs.Task))
				{
					await Log.Error("WorldSceneSystem",
						$"Main-thread dispatch was not drained within {MainThreadDispatchTimeoutMs}ms; abandoning the operation. " +
						"The world server's main-thread queue is stalled or the system has been deinitialized.");
					return false;
				}
				timeoutCts.Cancel();
			}

			// Awaited rather than returned so an exception thrown by the action surfaces here
			// (the caller's try/catch), exactly as it did before the timeout was added.
			return await tcs.Task;
		}

		/// <summary>
		/// Handles remote connection disconnects. Removes connections from queues when they disconnect.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			worldResidencyDeadlineByClientId.TryRemove(conn.ClientId, out _);
			leftQueueClientIds.Remove(conn.ClientId);

			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
			}

			// Terminal: the wait is over however it ended. RemoveFromQueue deliberately leaves
			// the wait clock alone so re-queue cycles do not reset the TTL, so it is cleared here.
			// A drop is not the player's choice, so an open-world place is held for a reconnect.
			EndWaitKeepingPlace(conn.ClientId);
		}

		/// <summary>
		/// Disconnects connections that authenticated but were never routed to a scene server.
		/// </summary>
		/// <remarks>See <see cref="worldResidencyDeadlineByClientId"/>.</remarks>
		private void SweepStrandedResidents()
		{
			if (worldResidencyDeadlineByClientId.IsEmpty)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			var serverManager = Server?.NetworkWrapper?.NetworkManager?.ServerManager;
			Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData);

			foreach (var kvp in worldResidencyDeadlineByClientId)
			{
				if (now < kvp.Value)
				{
					continue;
				}

				NetworkConnection conn = null;
				serverManager?.Clients.TryGetValue(kvp.Key, out conn);
				if (conn == null || !conn.IsActive)
				{
					worldResidencyDeadlineByClientId.TryRemove(kvp.Key, out _);
					continue;
				}

				/* A connection sitting in a waiting queue is not stranded — it is waiting, and
				 * PurgeExpiredWaitingConnections owns that case with its own (shorter) TTL.
				 * Push the deadline out instead of kicking, so this watchdog measures only
				 * continuous time spent outside every queue.
				 *
				 * This matters for the combat-logout deferral, which deliberately holds a
				 * connection in the open-world queue for up to CombatLogoutRoutingGraceSeconds
				 * (150s) waiting for its body's scene instance to come back. Kicking at 90s
				 * would interrupt a wait that is working as designed. */
				if (mappingData != null &&
					(mappingData.OpenWorldConnectionScenes.ContainsKey(conn) ||
					 mappingData.InstanceConnectionScenes.ContainsKey(conn)))
				{
					worldResidencyDeadlineByClientId[kvp.Key] = now + WorldResidencyGraceSeconds;
					continue;
				}

				worldResidencyDeadlineByClientId.TryRemove(kvp.Key, out _);

				Log.Warning("WorldSceneSystem",
					$"Connection {kvp.Key} spent {WorldResidencyGraceSeconds:F0}s on the world server without being " +
					"queued or routed to a scene server; disconnecting so the client can retry.");
				// Non-terminal on purpose: retrying is the designed recovery here, and the
				// notice only has to explain the wait if the retries also run out.
				DisconnectWithNotice(conn, DisconnectNoticeReason.RoutingTimedOut);
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
				SweepStrandedResidents();
				queuePlaces.Sweep(MonotonicClock.NowSeconds);
			}

			runtimeData.NextQueuePositionUpdate -= deltaTime;
			if (runtimeData.NextQueuePositionUpdate <= 0f)
			{
				runtimeData.NextQueuePositionUpdate = queuePositionUpdateRateSeconds;
				BroadcastQueuePositions();
			}

			runtimeData.NextDebounceCleanup -= deltaTime;
			if (runtimeData.NextDebounceCleanup <= 0f)
			{
				runtimeData.NextDebounceCleanup = debounceCleanupIntervalSeconds;
				CleanupExpiredDebounceEntries(runtimeData);
				SweepSceneCaches(runtimeData);
			}

			nextStaleSceneRowSweep -= deltaTime;
			if (nextStaleSceneRowSweep <= 0f)
			{
				nextStaleSceneRowSweep = StaleSceneRowSweepIntervalSeconds;
				QueueStaleSceneRowSweep();
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

				/* Open-world work is one task per distinct scene name, which is bounded by the
				 * world's zone count and small. */
				var sceneTasks = new List<Task>(openWorldSceneNames.Count);
				foreach (string sceneName in openWorldSceneNames)
				{
					sceneTasks.Add(ProcessOpenWorldQueueAsync(sceneName));
				}
				await Task.WhenAll(sceneTasks);

				/* Instance work is bounded only by MAX_WAITING_QUEUE_SIZE (2500). Each batch is a
				 * fixed handful of batched reads and one main-thread hop each way, whatever its
				 * size (see ProcessInstanceBatchAsync), and the batches run one after another so a
				 * full queue never puts more than one batch's queries on the connection pool at
				 * once. */
				for (int start = 0; start < instanceConns.Count; start += InstanceRoutingBatchSize)
				{
					int count = Math.Min(InstanceRoutingBatchSize, instanceConns.Count - start);
					await ProcessInstanceBatchAsync(instanceConns.GetRange(start, count), skipDebounce: false);
				}

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
		/// As many waiting connections as there are free slots are taken, oldest first, and their
		/// character data is fetched in one read to learn each saved <c>SceneHandle</c>.
		/// If a matching available instance exists with capacity, the character is routed there.
		/// This enables channel switching: <c>SceneChannelSystem</c> sets the target handle before
		/// disconnect, and this method honours that preference. If no match is found, the character
		/// falls back to any available instance with capacity. Every placed character is bound to its
		/// instance in one write before any client is sent on.
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
			if (availableScenes == null)
			{
				/* The read failed (logged by the helper), which is not the same as there being no
				 * instance. Reading it as "none" requested a fresh instance of a zone whose running
				 * ones were merely unreadable — a spare empty copy that then sat for minutes. The
				 * waiting connections have not been snapshotted yet, so they stay queued as they are
				 * and the next cycle reads again. */
				routedLastCycleByScene[sceneName] = 0;
				return;
			}
			if (availableScenes.Count < 1)
			{
				// Nothing to route to and nothing placed: the wait is on a scene instance being
				// created, which is what CleanupAndEnqueueNewSceneIfNeededAsync requests below.
				queueReasonByScene[sceneName] = WorldSceneQueueReason.SceneLoading;
				routedLastCycleByScene[sceneName] = 0;
				await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
				return;
			}

			/* Resolve all scene server addresses upfront and build a capacity tracker.
			 *
			 * Keyed by scene row ID, not by the hosting process's local scene handle. Handles are
			 * only unique within the process that allocated them, so two scene servers hosting
			 * the same scene routinely produce the same one — and these maps then silently
			 * collapsed their two instances into a single entry, sending everyone to whichever
			 * one happened to be written last and double-counting its capacity.
			 *
			 * instanceIDs preserves insertion order for deterministic fallback assignment. */
			var instanceIDs = new List<long>(availableScenes.Count);
			var serverInfoByHandle = new Dictionary<long, ushort>(availableScenes.Count);
			var capacityByHandle = new Dictionary<long, int>(availableScenes.Count);

			// Resolve scene server addresses: check cache first, batch-fetch any misses.
			TimeSpan serverTtl = TimeSpan.FromSeconds(sceneServerCacheTtlSeconds);
			var uncachedServerIds = new List<long>();
			var scenesByServerId = new Dictionary<long, List<SceneData>>();

			foreach (var sceneData in availableScenes)
			{
				/* Open-world routing may only ever place a player in an OpenWorld instance.
				 *
				 * FetchAvailableAsync selects on (world, scene name, capacity, Ready) and says
				 * nothing about SceneType, so a Group row for a scene that is also reachable as
				 * an ordinary destination — a dungeon named as a teleporter's ToScene, or as a
				 * character's BindScene — came back here as a routing candidate. Routing to it
				 * drops a player into somebody else's private instance, with that instance's
				 * character_id still naming its owner. SceneChannelSystem already applies this
				 * filter when it builds a channel list; this is the same rule on the path that
				 * actually places people. */
				if ((SceneType)sceneData.SceneType != SceneType.OpenWorld)
				{
					continue;
				}

				long serverId = sceneData.SceneServerID;

				// Try cache first
				if (serverTtl > TimeSpan.Zero &&
					runtimeData.SceneServerAddressCache.TryGet(serverId, serverTtl, out var cachedAddr))
				{
					long instanceID = sceneData.ID;
					instanceIDs.Add(instanceID);
					serverInfoByHandle[instanceID] = cachedAddr;
					capacityByHandle[instanceID] = maxClientsPerInstance - sceneData.CharacterCount;
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
			bool serverFetchFailed = false;
			if (uncachedServerIds.Count > 0)
			{
				var batchResult = await sceneServerService.FetchSceneServersByIDsAsync(uncachedServerIds);
				if (batchResult.IsSuccess && batchResult.Data != null)
				{
					foreach (var serverData in batchResult.Data)
					{
						/* Skip scene servers that have stopped pulsing.
						 *
						 * A crashed scene server leaves both its own registration and its scene
						 * rows behind, so without this check its scenes stay in the routing pool
						 * and players are sent to an address that no longer serves them — or, once
						 * a replacement reuses the port, to a server that does not have the scene
						 * and returns them here to be routed at the same rows again.
						 * SweepStaleSceneRowsAsync deletes those rows, but only on its own timer;
						 * this keeps the window between the crash and the sweep from routing
						 * anyone into it. */
						if (!IsSceneServerRoutable(serverData))
						{
							continue;
						}

						ushort serverPort = (ushort)serverData.Port;
						if (serverTtl > TimeSpan.Zero)
						{
							runtimeData.SceneServerAddressCache.Set(serverData.ID, serverPort);
						}

						if (scenesByServerId.TryGetValue(serverData.ID, out var scenes))
						{
							foreach (var sd in scenes)
							{
								long instanceID = sd.ID;
								instanceIDs.Add(instanceID);
								serverInfoByHandle[instanceID] = serverPort;
								capacityByHandle[instanceID] = maxClientsPerInstance - sd.CharacterCount;
							}
						}
					}
				}
				else
				{
					serverFetchFailed = true;
					await Log.Warning("WorldSceneSystem", $"Scene server batch fetch failed for {uncachedServerIds.Count} server(s) hosting '{sceneName}': [{batchResult.ErrorCode}] {batchResult.ErrorMessage}");

					// Invalidate stale entries for any servers that failed to fetch
					foreach (long serverId in uncachedServerIds)
					{
						runtimeData.SceneServerAddressCache?.Invalidate(serverId);
					}
				}

				// Any server that was requested but did not produce a handle above — missing from
				// the result, or skipped as not pulsing — must not keep a cached address either.
				foreach (long serverId in uncachedServerIds)
				{
					if (!scenesByServerId.TryGetValue(serverId, out var requestedScenes))
					{
						continue;
					}
					bool resolved = requestedScenes.Count > 0 && serverInfoByHandle.ContainsKey(requestedScenes[0].ID);
					if (!resolved)
					{
						runtimeData.SceneServerAddressCache?.Invalidate(serverId);
					}
				}
			}

			if (instanceIDs.Count < 1)
			{
				if (serverFetchFailed)
				{
					// Unresolved because the read failed, not because the servers are gone: as with
					// a failed instance fetch above, leave the queue as it is rather than request a
					// fresh instance of a zone that may be running fine.
					routedLastCycleByScene[sceneName] = 0;
					return;
				}

				// Scene rows exist but none resolved to a reachable scene server, so a fresh
				// instance is what these clients are waiting for.
				queueReasonByScene[sceneName] = WorldSceneQueueReason.SceneLoading;
				routedLastCycleByScene[sceneName] = 0;
				await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
				return;
			}

			/* Take only as many connections as there is room for.
			 *
			 * A pass cannot place more than the free capacity, and it used to snapshot and empty the
			 * whole queue anyway: every waiting player's character row was read, and everyone it
			 * could not place was put back one queued main-thread action at a time — 2,500 rows and
			 * 2,490 re-queues every cycle to route ten. The rest now simply stay queued. See
			 * WorldSceneRoutingRules.SelectForRouting for which ones are taken. */
			int freeCapacity = 0;
			for (int i = 0; i < instanceIDs.Count; ++i)
			{
				if (capacityByHandle.TryGetValue(instanceIDs[i], out int free) && free > 0)
				{
					freeCapacity += free;
				}
			}

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

				TakeOpenWorldConnectionsToRoute(connections, freeCapacity, mappingData, waitingConnections);
			}))
			{
				return;
			}

			// Everything this pass decides is applied on the main thread in one action at the end.
			var outcome = new RoutingOutcome();

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
				else
				{
					/* The whole query failed, which says nothing about any individual character.
					 *
					 * The routing loop below reads a missing entry as "this account has no
					 * selected character" and kicks — so one database hiccup emptied the entire
					 * queue, kicking every player waiting on this scene back to the login screen
					 * at once, right when the database is already struggling. Put them back on
					 * the queue instead: the next cycle re-reads, and a transient failure costs a
					 * few seconds of waiting rather than everyone's session. */
					await Log.Warning("WorldSceneSystem",
						$"Batch character fetch failed for {accountNames.Count} connection(s) waiting on '{sceneName}': " +
						$"{batchResult.ErrorCode} - {batchResult.ErrorMessage}. Re-queuing them for the next routing cycle.");

					for (int i = 0; i < waitingConnections.Count; ++i)
					{
						outcome.RequeueOpenWorld(waitingConnections[i].conn, sceneName, WorldSceneQueueReason.Capacity);
					}

					ApplyRoutingOutcome(outcome);
					routedLastCycleByScene[sceneName] = 0;
					return;
				}
			}

			// --- Routing phase: two-pass assignment ---
			// Pass 1: Preferred handle assignments via O(1) dictionary lookup.
			// This supports channel switching: SceneChannelSystem updates the handle
			// before disconnect so the world server routes back to the chosen channel.
			// Nothing is written or sent yet: every placement is collected, the character rows are
			// bound in one statement below, and only then are the clients told where to go.
			var unassigned = new List<(NetworkConnection conn, string accountName, CharacterData charData)>();
			var assignments = new List<(NetworkConnection Conn, CharacterData Character, long Handle, ushort Port)>();
			for (int i = 0; i < waitingConnections.Count; ++i)
			{
				var (conn, accountName) = waitingConnections[i];
				if (!charDataByConn.TryGetValue(conn, out var charData))
				{
					/* The snapshot above took the connection off the queue, so falling through
					 * here dropped it out of routing entirely: no scene assignment, no retry, and
					 * no message. The client sat on the world server until the residency watchdog
					 * disconnected it a minute and a half later, which the player experiences as
					 * a loading screen that hangs and then throws them back to login.
					 *
					 * There is nothing to route without a character row — the destination scene
					 * server is chosen from it — so say so and disconnect now. The client's
					 * reconnect loop retries from the world server, which re-reads the row; a
					 * transient database failure therefore recovers in seconds instead of
					 * ninety, and a genuinely missing selection fails fast the same way the
					 * instance route already does. */
					await Log.Warning("WorldSceneSystem",
						$"No selected character data for account '{accountName}' (conn {conn?.ClientId}); cannot route to a scene server.");
					outcome.Kick(conn, "Failed to get selected character", DisconnectNoticeReason.CharacterUnavailable);
					continue;
				}

				long preferredHandle = charData.SceneHandle;
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
					double deferredSince = combatLogoutRoutingDeferredSince.GetOrAdd(charData.ID, _ => MonotonicClock.NowSeconds);
					if (MonotonicClock.NowSeconds - deferredSince < CombatLogoutRoutingGraceSeconds)
					{
						await Log.Debug("WorldSceneSystem",
							$"Holding character {charData.ID} for scene handle {preferredHandle}: its combat-logout body lives there.");

						/* This wait is bounded by CombatLogoutRoutingGraceSeconds rather than by
						 * the queue TTL, so it restarts the TTL clock each cycle it holds — and
						 * once the hold ends, the ordinary TTL starts counting from there. It
						 * also reports its own reason, because "waiting for capacity" would be
						 * a lie: the scene may be half empty and this character still cannot go
						 * anywhere but the one instance holding its body. The reason is also what
						 * makes the next pass look at this connection whatever the capacity. */
						outcome.RequeueOpenWorld(conn, sceneName,
							WorldSceneQueueReason.CombatLogoutBody, restartWaitClock: true);
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
					assignments.Add((conn, charData, preferredHandle, prefServer));
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
				var capacityHeap = new InstanceCapacityHeap(instanceIDs.Count);
				for (int i = 0; i < instanceIDs.Count; ++i)
				{
					long h = instanceIDs[i];
					if (capacityByHandle.TryGetValue(h, out int cap) && cap > 0)
					{
						capacityHeap.Push(h, cap);
					}
				}

				for (int i = 0; i < unassigned.Count; ++i)
				{
					var (conn, accountName, charData) = unassigned[i];

					if (!capacityHeap.TryAssignFromTop(out long assignedHandle) ||
						!serverInfoByHandle.TryGetValue(assignedHandle, out var serverPort))
					{
						// No capacity anywhere — re-queue for the next processing cycle, so the wait
						// keeps its TTL and the client is told why it is still waiting.
						outcome.RequeueOpenWorld(conn, sceneName, WorldSceneQueueReason.Capacity);
						continue;
					}

					assignments.Add((conn, charData, assignedHandle, serverPort));
				}
			}

			/* Bind every placed character to its instance, in one statement, before any client is
			 * told where to go.
			 *
			 * After a world restart every character's world_server_id is stale, so every one of them
			 * needs this write; one awaited round trip each held this scene's task, and with it the
			 * whole routing cycle, for seconds. The write still has to land before the connect:
			 * the scene server matches the arriving client on the (world_server_id, scene_name,
			 * scene_handle) it reads back from the row, and a client sent ahead of its rebind is
			 * refused as a mismatched scene handle. So a character whose bind did not land is not
			 * sent. It goes back on the queue for the next pass, and the slot it was given is
			 * simply spent for this one. */
			List<(long CharacterId, string SceneName, long SceneHandle)> binds = null;
			for (int i = 0; i < assignments.Count; ++i)
			{
				var a = assignments[i];
				if (WorldSceneRoutingRules.NeedsSceneBind(a.Character.WorldServerID, a.Character.SceneHandle, worldServerID, a.Handle))
				{
					(binds ??= new List<(long, string, long)>(assignments.Count)).Add((a.Character.ID, sceneName, a.Handle));
				}
			}

			HashSet<long> bound = null;
			if (binds != null)
			{
				DatabaseResult<IReadOnlyList<long>> bindResult = await charService.UpdateSceneBatchAsync(worldServerID, binds);
				if (bindResult.IsSuccess)
				{
					bound = new HashSet<long>(bindResult.Data);
					await Log.Info("WorldSceneSystem", $"Bound {bound.Count} of {binds.Count} character(s) to world server {worldServerID}, scene '{sceneName}'.");
				}
				else
				{
					await Log.Warning("WorldSceneSystem", $"Scene bind failed for {binds.Count} character(s) routed to '{sceneName}': [{bindResult.ErrorCode}] {bindResult.ErrorMessage}. Re-queuing them for the next routing cycle.");
				}
			}

			// Feeds the client-facing wait estimate. See routedLastCycleByScene.
			int routedThisCycle = 0;
			for (int i = 0; i < assignments.Count; ++i)
			{
				var a = assignments[i];
				if (WorldSceneRoutingRules.NeedsSceneBind(a.Character.WorldServerID, a.Character.SceneHandle, worldServerID, a.Handle) &&
					(bound == null || !bound.Contains(a.Character.ID)))
				{
					if (bound != null)
					{
						// The statement ran and this row was not in it: missing or deleted, the
						// per-row NotFound of the single-row write.
						await Log.Warning("WorldSceneSystem", $"Scene bind wrote no row for character {a.Character.ID}. Re-queuing for the next routing cycle.");
					}
					outcome.RequeueOpenWorld(a.Conn, sceneName);
					continue;
				}

				outcome.Connect(a.Conn, a.Port);
				routedThisCycle++;
			}

			ApplyRoutingOutcome(outcome);

			// Publish what this pass achieved so the position sweep can estimate a wait, and
			// record that anything still queued is queued for capacity rather than for a load.
			routedLastCycleByScene[sceneName] = routedThisCycle;
			queueReasonByScene[sceneName] = WorldSceneQueueReason.Capacity;
			if (routedThisCycle > 0)
			{
				lastPlacementAtByScene[sceneName] = MonotonicClock.NowSeconds;
			}

			// Clean up empty queue entries and request a new scene if connections are still waiting
			await CleanupAndEnqueueNewSceneIfNeededAsync(sceneName, worldServerID, sceneService);
		}

		/// <summary>
		/// Takes the connections one open-world routing pass will try to place off the scene's
		/// waiting queue. Main thread only.
		/// </summary>
		/// <param name="connections">The scene's waiting set.</param>
		/// <param name="freeCapacity">Free slots across the scene's routable instances.</param>
		/// <param name="mappingData">Queue maps.</param>
		/// <param name="taken">Receives each connection taken, with its account name.</param>
		/// <remarks>See <see cref="WorldSceneRoutingRules.SelectForRouting{T}"/> for which are taken.</remarks>
		private void TakeOpenWorldConnectionsToRoute(
			HashSet<NetworkConnection> connections,
			int freeCapacity,
			IWorldSceneMappingData<NetworkConnection> mappingData,
			List<(NetworkConnection conn, string accountName)> taken)
		{
			double now = MonotonicClock.NowSeconds;
			var candidates = new List<WorldSceneRoutingRules.QueueCandidate<NetworkConnection>>(connections.Count);
			List<NetworkConnection> gone = null;
			foreach (NetworkConnection connection in connections)
			{
				if (connection == null || !connection.IsActive)
				{
					(gone ??= new List<NetworkConnection>()).Add(connection);
					continue;
				}

				// The reporting clock, so the order matches the positions clients are told.
				double waitingSince = waitingSinceByClientId.TryGetValue(connection.ClientId, out double since)
					? since
					: now;
				bool held = queueReasonByClientId.TryGetValue(connection.ClientId, out WorldSceneQueueReason reason) &&
					reason == WorldSceneQueueReason.CombatLogoutBody;
				candidates.Add(new WorldSceneRoutingRules.QueueCandidate<NetworkConnection>(connection, waitingSince, connection.ClientId, held));
			}

			// A connection that has gone is dropped, as the whole-queue snapshot always did.
			if (gone != null)
			{
				for (int i = 0; i < gone.Count; ++i)
				{
					NetworkConnection connection = gone[i];
					connections.Remove(connection);
					if (connection != null)
					{
						mappingData.OpenWorldConnectionScenes.Remove(connection);
						// Gone without leaving: the connection dropped, so its place is held.
						EndWaitKeepingPlace(connection.ClientId);
					}
				}
			}

			var selected = new List<NetworkConnection>(Math.Min(candidates.Count, freeCapacity + 1));
			WorldSceneRoutingRules.SelectForRouting(candidates, freeCapacity, selected);

			for (int i = 0; i < selected.Count; ++i)
			{
				NetworkConnection connection = selected[i];
				connections.Remove(connection);
				mappingData.OpenWorldConnectionScenes.Remove(connection);

				/* The wait clock deliberately survives this. Taking a connection off the queue here
				 * is a routing cycle, not a departure — anything not placed is put straight back —
				 * so clearing it restarted the TTL every cycle and the purge could never fire. Only
				 * a connection that is genuinely leaving clears it. */
				if (!IsValidConnection(connection, out string accountName))
				{
					EndWaitKeepingPlace(connection.ClientId);
					continue;
				}

				taken.Add((connection, accountName));
			}
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
			int waitingCount = 0;
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
					waitingCount = connections.Count;
				}
			}))
			{
				return;
			}

			if (needsNewScene)
			{
				/* Ask only if one is not already on its way.
				 *
				 * This runs on every routing cycle for as long as anyone is still waiting on this
				 * scene, and an unconditional enqueue therefore requested a fresh instance every
				 * WaitQueueRateSeconds (2s) throughout the load it was waiting for. A cold
				 * open-world zone that takes twenty seconds to come up collected ten requests;
				 * scene servers dequeued and loaded all of them, so one zone became ten stacked
				 * copies, each with its own physics scene, nine of them empty. An empty scene is
				 * not eligible for stale unload until StaleSceneTimeout (five minutes), so they
				 * stayed for minutes rather than being reclaimed on the next pulse.
				 *
				 * EnqueueIfUnderOutstandingLimitAsync counts and inserts in a single statement,
				 * bounded by how many instances the waiting population could fill. One player
				 * waiting produces one load; a surge that genuinely needs several gets them in
				 * parallel; nobody gets a tenth empty copy of a zone because the first one was
				 * slow to boot. Capped so a queue inflated by a stuck scene cannot ask for an
				 * unbounded number of loads at once. */
				int perInstance = Math.Max(1, GetMaxClients(sceneName));
				int maxOutstanding = Mathf.Clamp((waitingCount + perInstance - 1) / perInstance, 1, MaxOutstandingSceneLoads);

				DatabaseResult<long> enqueueResult = await sceneService.EnqueueIfUnderOutstandingLimitAsync(worldServerID, sceneName, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.OpenWorld, maxOutstanding);
				if (!enqueueResult.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem", $"CleanupAndEnqueueNewSceneIfNeededAsync DB error (Scene={sceneName}): {enqueueResult.ErrorCode} - {enqueueResult.ErrorMessage}");
				}
				else if (enqueueResult.Data > 0)
				{
					await Log.Info("WorldSceneSystem", $"Requested a new instance of '{sceneName}' (SceneID={enqueueResult.Data}) for waiting connections.");
				}
			}
		}

		/// <summary>
		/// Releases a character from an instance that cannot be entered and persists the change.
		/// The caller routes it to the open world.
		/// </summary>
		/// <remarks>
		/// Every caller has established that the instance is gone or will never arrive: the row
		/// was reaped, its scene server stopped pulsing, or it sat un-ready past
		/// <see cref="InstanceReadyGraceSeconds"/>. That conclusion covers the character's
		/// combat-logout body too, which is why the flag is cleared here as well — see below.
		/// </remarks>
		private async Task ReleaseFromInstanceAsync(ICharacterService charService, CharacterData charData)
		{
			int characterFlags = charData.Flags;
			characterFlags.DisableBit(CharacterFlags.IsInInstance);

			/* The body went with the instance, so the flag that says one is waiting must go too.
			 *
			 * A character that combat-logged inside an instance left its body there, and the
			 * body is only ever in the instance — AdjustLingeringSceneCount books it against the
			 * instance scene precisely because that is where it stands. Reaching this method
			 * means that instance is not coming back, so the body is gone with it.
			 *
			 * Left set, the flag follows the character onto the open-world path, where the
			 * routing pass reads it as "this character's body is on one specific instance" and
			 * holds the connection for up to CombatLogoutRoutingGraceSeconds (150s) waiting for
			 * a scene instance that no longer exists — behind a loading screen — before giving
			 * up and clearing the flag itself. Clearing it here skips a wait whose answer is
			 * already known. */
			characterFlags.DisableBit(CharacterFlags.IsCombatLogged);
			combatLogoutRoutingDeferredSince.TryRemove(charData.ID, out _);
			var updatedChar = charData.WithFlagsVersionAndTimestamp(
				characterFlags,
				charData.Version + 1,
				DateTime.UtcNow
			);
			DatabaseResult persistResult = await charService.PersistAsync(updatedChar);
			if (!persistResult.IsSuccess)
			{
				await Log.Warning("WorldSceneSystem", $"ReleaseFromInstanceAsync DB error (CharID={charData.ID}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
			}
		}

		/// <summary>
		/// How many instance releases one batch persists at once.
		/// </summary>
		/// <remarks>
		/// Each is a full character write, one per character. They are rare — an instance has to be
		/// gone — except after a world server restart, which deletes every scene row it owned and so
		/// releases every instanced character at once. This keeps that from putting a whole batch's
		/// writes on the connection pool together.
		/// </remarks>
		private const int MaxConcurrentInstanceReleases = 32;

		/// <summary>
		/// Routes a batch of connections waiting on the instance queue: to their instance's scene
		/// server, back onto the queue, or to the open world.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The whole batch costs one main-thread hop to take it off the queue, one read of every
		/// selected character, one read of every distinct instance, one read of every distinct
		/// scene server, at most one bind write, and one main-thread action to apply what was
		/// decided. It used to cost all of that per connection — a hop with its own timer, a
		/// character read, a scene read and a scene-server read each, even for the members of one
		/// party waiting on the same instance — so a scene server restart with 500 players in
		/// dungeons put hundreds of single queries a second on the database for minutes.
		/// </para>
		/// <para>
		/// A connection is taken off the queue before any database work and is always either sent
		/// on, put back, moved to the open-world queue, or kicked. That prevents re-processing,
		/// erroneous TTL kicks, and stale routing during the async window.
		/// </para>
		/// </remarks>
		/// <param name="conns">Connections to route.</param>
		/// <param name="skipDebounce">When true, skips the per-account debounce because the caller already reserved the lookup window.</param>
		private async Task ProcessInstanceBatchAsync(List<NetworkConnection> conns, bool skipDebounce)
		{
			if (conns == null || conns.Count == 0 ||
				!TryGetDbService(out ICharacterService charService) ||
				!TryGetDbService(out ISceneService sceneService) ||
				!TryGetDbService(out ISceneServerService sceneServerService))
			{
				return;
			}

			// Validate the connections and take them off the instance queue, in one main-thread hop.
			var waiting = new List<(NetworkConnection Conn, string AccountName)>(conns.Count);
			if (!await RunOnMainThreadAsync(() =>
			{
				Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData);
				for (int i = 0; i < conns.Count; ++i)
				{
					NetworkConnection conn = conns[i];
					if (conn == null)
					{
						continue;
					}

					if (!IsValidConnection(conn, out string acct))
					{
						Kick(conn, "Failed to get account name", DisconnectNoticeReason.ProtocolViolation, terminal: true);
					}
					else
					{
						waiting.Add((conn, acct));
					}

					// Remove from queue before async work to prevent re-processing on the next cycle
					if (mappingData != null)
					{
						RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
					}
				}
			}))
			{
				return;
			}

			if (waiting.Count == 0)
			{
				return;
			}

			var outcome = new RoutingOutcome();
			try
			{
				var lookups = new List<(NetworkConnection Conn, string AccountName)>(waiting.Count);
				for (int i = 0; i < waiting.Count; ++i)
				{
					if (skipDebounce || TryBeginInstanceLookup(waiting[i].AccountName))
					{
						lookups.Add(waiting[i]);
					}
					else
					{
						// Rate-limited. The connection was already taken off the queue above; put it
						// back so the next processing cycle picks it up.
						outcome.RequeueInstance(waiting[i].Conn, 0L);
					}
				}

				if (lookups.Count > 0)
				{
					await RouteInstanceLookupsAsync(lookups, outcome, charService, sceneService, sceneServerService);
				}
			}
			catch (Exception ex)
			{
				/* Put back everything not yet decided. The connections were taken off the queue
				 * before the async work began, so anything left undecided when this throws would
				 * otherwise be orphaned; the next cycle retries it. */
				await Log.Error("WorldSceneSystem", $"Instance routing failed for a batch of {waiting.Count} connection(s): {ex}");
				for (int i = 0; i < waiting.Count; ++i)
				{
					if (!outcome.IsDecided(waiting[i].Conn))
					{
						outcome.RequeueInstance(waiting[i].Conn, 0L);
					}
				}
			}

			ApplyRoutingOutcome(outcome);
		}

		/// <summary>
		/// Reads what a batch of instance-queue connections needs and decides each one. See
		/// <see cref="ProcessInstanceBatchAsync"/>.
		/// </summary>
		private async Task RouteInstanceLookupsAsync(
			List<(NetworkConnection Conn, string AccountName)> lookups,
			RoutingOutcome outcome,
			ICharacterService charService,
			ISceneService sceneService,
			ISceneServerService sceneServerService)
		{
			// Every selected character in the batch, in one read.
			var accountNames = new List<string>(lookups.Count);
			for (int i = 0; i < lookups.Count; ++i)
			{
				accountNames.Add(lookups[i].AccountName);
			}

			var charResult = await charService.FetchSelectedCharactersByAccountsAsync(accountNames);
			if (!charResult.IsSuccess || charResult.Data == null)
			{
				/* A failed read says nothing about the characters, so it is not a reason to kick.
				 * The open-world path learned the same lesson (see ProcessOpenWorldQueueAsync's
				 * batch fetch): kicking here sent every reconnecting player back through the login
				 * pipeline on a database hiccup. Wait a routing cycle instead. */
				await Log.Warning("WorldSceneSystem", $"Selected character fetch failed for {lookups.Count} instance-queue connection(s): [{charResult.ErrorCode}] {charResult.ErrorMessage}. Re-queuing them for the next routing cycle.");
				for (int i = 0; i < lookups.Count; ++i)
				{
					outcome.RequeueInstance(lookups[i].Conn, 0L);
				}
				return;
			}

			var charByAccount = new Dictionary<string, CharacterData>(charResult.Data.Count, StringComparer.OrdinalIgnoreCase);
			foreach (CharacterData charData in charResult.Data)
			{
				if (charData.ID > 0)
				{
					charByAccount[charData.Account] = charData;
				}
			}

			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;

			var instanceBound = new List<(NetworkConnection Conn, CharacterData Character)>(lookups.Count);
			var instanceIDs = new HashSet<long>();
			var releases = new List<(NetworkConnection Conn, CharacterData Character)>();
			for (int i = 0; i < lookups.Count; ++i)
			{
				var (conn, accountName) = lookups[i];
				if (!charByAccount.TryGetValue(accountName, out CharacterData charData))
				{
					outcome.Kick(conn, "invalid character ID", DisconnectNoticeReason.CharacterUnavailable);
					continue;
				}

				/* Not bound to an instance, so an open-world character: the open-world pass routes
				 * it, and binds it to this world server itself when it places it. The scene it
				 * waits for is on the row just read. The fallback used to read the row again to
				 * find it, the fourth read of the same row in one login, and every one of these
				 * characters was written a world bind here that only the instance route needs. */
				if (!charData.Flags.IsFlagged(CharacterFlags.IsInInstance))
				{
					outcome.MoveToOpenWorld(conn, charData);
					continue;
				}

				/* IsCombatLogged is deliberately NOT diverted here, unlike on the open-world path.
				 *
				 * That check exists because an open-world character's body can be left on a scene
				 * instance other than the one the router would otherwise pick, and only the server
				 * holding the body can hand it back. An instanced character has no such ambiguity:
				 * its body is in its instance, on the scene server that hosts it, which is exactly
				 * where the routing below sends it — and TryReattachLingeringCharacter reclaims the
				 * body on arrival. Diverting to the open-world queue would send the client to a
				 * server that does not hold the body, which then loses the claim race and kicks it
				 * on every retry until the linger expires.
				 *
				 * If the instance is gone (its scene server died, taking the row with it) the read
				 * below reports the row missing and the release clears the flag, which is the
				 * correct outcome: the body went with the server. */
				if (charData.InstanceID <= 0)
				{
					releases.Add((conn, charData));
					continue;
				}

				instanceBound.Add((conn, charData));
				instanceIDs.Add(charData.InstanceID);
			}

			// Every instance the batch is bound to, in one read: a party waiting on one dungeon is one row.
			Dictionary<long, (SceneData Scene, double AgeSeconds)> sceneByID = null;
			if (instanceIDs.Count > 0)
			{
				var sceneResult = await sceneService.FetchWithAgesAsync(instanceIDs);
				if (sceneResult.IsSuccess && sceneResult.Data != null)
				{
					sceneByID = new Dictionary<long, (SceneData Scene, double AgeSeconds)>(sceneResult.Data.Count);
					foreach (var row in sceneResult.Data)
					{
						sceneByID[row.Scene.ID] = row;
					}
				}
				else
				{
					await Log.Warning("WorldSceneSystem", $"Instance scene fetch failed for {instanceIDs.Count} instance(s): [{sceneResult.ErrorCode}] {sceneResult.ErrorMessage}. Re-queuing their characters for the next routing cycle.");
				}
			}

			/* And every scene server hosting a Ready one, in one read. "Registered" is not the same
			 * as "running": a crashed scene server's registration outlives it, so the pulse is what
			 * says. */
			Dictionary<long, SceneServerData> serverByID = null;
			bool serverReadFailed = false;
			if (sceneByID != null)
			{
				var serverIDs = new List<long>();
				var seenServers = new HashSet<long>();
				foreach (var row in sceneByID.Values)
				{
					if ((FishMMO.Shared.SceneStatus)row.Scene.SceneStatus == FishMMO.Shared.SceneStatus.Ready &&
						seenServers.Add(row.Scene.SceneServerID))
					{
						serverIDs.Add(row.Scene.SceneServerID);
					}
				}

				if (serverIDs.Count > 0)
				{
					var serverResult = await sceneServerService.FetchSceneServersByIDsAsync(serverIDs);
					if (serverResult.IsSuccess && serverResult.Data != null)
					{
						serverByID = new Dictionary<long, SceneServerData>(serverResult.Data.Count);
						foreach (SceneServerData server in serverResult.Data)
						{
							serverByID[server.ID] = server;
						}
					}
					else
					{
						serverReadFailed = true;
						await Log.Warning("WorldSceneSystem", $"Scene server fetch failed for {serverIDs.Count} server(s) hosting instances: [{serverResult.ErrorCode}] {serverResult.ErrorMessage}. Re-queuing their characters for the next routing cycle.");
					}
				}
			}

			var connects = new List<(NetworkConnection Conn, CharacterData Character, ushort Port)>(instanceBound.Count);
			HashSet<long> deadScenes = null;
			for (int i = 0; i < instanceBound.Count; ++i)
			{
				var (conn, charData) = instanceBound[i];
				long instanceID = charData.InstanceID;

				var sceneLookup = WorldSceneRoutingRules.RowLookup.Failed;
				FishMMO.Shared.SceneStatus status = default;
				SceneData sceneData = default;
				double ageSeconds = 0.0;
				if (sceneByID != null)
				{
					if (sceneByID.TryGetValue(instanceID, out var row))
					{
						sceneLookup = WorldSceneRoutingRules.RowLookup.Found;
						sceneData = row.Scene;
						status = (FishMMO.Shared.SceneStatus)row.Scene.SceneStatus;
						ageSeconds = row.AgeSeconds;
					}
					else
					{
						// Only a row that is actually missing releases the character. That includes
						// the row having been reaped by SweepStaleSceneRowsAsync, which is the
						// ordinary end state for an instance that never became ready.
						sceneLookup = WorldSceneRoutingRules.RowLookup.Absent;
					}
				}

				var serverLookup = WorldSceneRoutingRules.RowLookup.Absent;
				bool serverLive = false;
				ushort port = 0;
				if (sceneLookup == WorldSceneRoutingRules.RowLookup.Found && status == FishMMO.Shared.SceneStatus.Ready)
				{
					if (serverReadFailed)
					{
						serverLookup = WorldSceneRoutingRules.RowLookup.Failed;
					}
					else if (serverByID != null && serverByID.TryGetValue(sceneData.SceneServerID, out SceneServerData server))
					{
						serverLookup = WorldSceneRoutingRules.RowLookup.Found;
						// Live, not routable: a locked scene server still hands back the instances it
						// is hosting. See IsSceneServerRoutable for why a lock must not evict from a dungeon.
						serverLive = IsSceneServerLive(server);
						port = (ushort)server.Port;
					}
				}

				switch (WorldSceneRoutingRules.DecideInstanceRoute(sceneLookup, status, ageSeconds, serverLookup, serverLive, InstanceReadyGraceSeconds))
				{
					case WorldSceneRoutingRules.InstanceRoute.Connect:
						connects.Add((conn, charData, port));
						break;

					case WorldSceneRoutingRules.InstanceRoute.WaitForLoad:
					case WorldSceneRoutingRules.InstanceRoute.Retry:
						outcome.RequeueInstance(conn, instanceID);
						break;

					case WorldSceneRoutingRules.InstanceRoute.Release:
						if (sceneLookup == WorldSceneRoutingRules.RowLookup.Found &&
							(status == FishMMO.Shared.SceneStatus.Pending || status == FishMMO.Shared.SceneStatus.Loading))
						{
							/* Bounded by the row's own age, measured by the database clock that
							 * stamped it, so the give-up decision survives the reconnect and no host
							 * clock can shift it. See WorldSceneRoutingRules.DecideInstanceRoute. */
							await Log.Warning("WorldSceneSystem",
								$"Instance scene {sceneData.ID} ({sceneData.SceneName}) has been {status} for {ageSeconds:F0}s; " +
								$"releasing character {charData.ID} from it and routing to the open world.");
						}
						releases.Add((conn, charData));
						break;

					case WorldSceneRoutingRules.InstanceRoute.ReleaseDeletingScene:
						// Scene server unreachable — delete the stale scene entry and fall back.
						(deadScenes ??= new HashSet<long>()).Add(instanceID);
						releases.Add((conn, charData));
						break;
				}
			}

			if (deadScenes != null)
			{
				DatabaseResult<int> deleteResult = await sceneService.DeleteManyAsync(deadScenes);
				if (!deleteResult.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem", $"Instance scene delete failed for {deadScenes.Count} scene(s) on stopped scene servers: {deleteResult.ErrorCode} - {deleteResult.ErrorMessage}");
				}
			}

			/* Bind each character being sent to its instance to this world server first.
			 *
			 * The instance route sends the client straight to the scene server hosting the
			 * instance, which matches the arriving character on (world_server_id, scene, handle)
			 * and rejects it as mismatched when world_server_id is stale from a previous world
			 * instance, or still 0 from character creation. Only this route needs the write, and
			 * only once it is actually sending the client: an open-world character is bound by the
			 * open-world pass when it is placed, and one waiting on a load or being released is not
			 * going anywhere yet. It used to be written for every character this path read. A bind
			 * that did not land holds the character for a routing cycle rather than sending it to
			 * a scene server that has to refuse it. */
			List<(long CharacterId, string SceneName, long SceneHandle)> binds = null;
			for (int i = 0; i < connects.Count; ++i)
			{
				CharacterData charData = connects[i].Character;
				if (WorldSceneRoutingRules.NeedsWorldBind(charData.WorldServerID, worldServerID))
				{
					(binds ??= new List<(long, string, long)>(connects.Count)).Add((charData.ID, charData.SceneName, charData.SceneHandle));
				}
			}

			HashSet<long> bound = null;
			if (binds != null)
			{
				DatabaseResult<IReadOnlyList<long>> bindResult = await charService.UpdateSceneBatchAsync(worldServerID, binds);
				if (bindResult.IsSuccess)
				{
					bound = new HashSet<long>(bindResult.Data);
				}
				else
				{
					await Log.Warning("WorldSceneSystem", $"Failed to bind {binds.Count} instanced character(s) to world server {worldServerID}: [{bindResult.ErrorCode}] {bindResult.ErrorMessage}. Re-queuing them for the next routing cycle.");
				}
			}

			for (int i = 0; i < connects.Count; ++i)
			{
				var (conn, charData, port) = connects[i];
				if (WorldSceneRoutingRules.NeedsWorldBind(charData.WorldServerID, worldServerID) &&
					(bound == null || !bound.Contains(charData.ID)))
				{
					if (bound != null)
					{
						await Log.Warning("WorldSceneSystem", $"World bind wrote no row for character {charData.ID}. Re-queuing for the next routing cycle.");
					}
					outcome.RequeueInstance(conn, 0L);
					continue;
				}

				outcome.Connect(conn, port);
			}

			// Releases last: each is its own character write, and they are rare.
			for (int start = 0; start < releases.Count; start += MaxConcurrentInstanceReleases)
			{
				int end = Math.Min(start + MaxConcurrentInstanceReleases, releases.Count);
				var writes = new Task[end - start];
				for (int i = start; i < end; ++i)
				{
					writes[i - start] = ReleaseFromInstanceAsync(charService, releases[i].Character);
				}
				await Task.WhenAll(writes);

				for (int i = start; i < end; ++i)
				{
					outcome.MoveToOpenWorld(releases[i].Conn, releases[i].Character);
				}
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
			double countCacheTtlSeconds = sceneInstanceCacheTtlSeconds;
			double now = MonotonicClock.NowSeconds;
			int sceneCharacterCount;

			if (countCacheTtlSeconds > 0.0 &&
				now - runtimeData.CachedSceneCharacterCountAt < countCacheTtlSeconds)
			{
				sceneCharacterCount = runtimeData.CachedSceneCharacterCount;
			}
			else
			{
				long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;

				// Summed by the database: every scene row used to come back here only to be added up.
				var scenesResult = await sceneService.SumCharacterCountAsync(worldServerID);
				if (scenesResult.IsSuccess)
				{
					sceneCharacterCount = scenesResult.Data;
					runtimeData.CachedSceneCharacterCount = sceneCharacterCount;
					runtimeData.CachedSceneCharacterCountAt = now;
				}
				else
				{
					/* Keep the last known count and leave its timestamp alone, so the next cycle
					 * asks again. Counting a failed read as zero, and caching that for the TTL,
					 * dropped every player in a scene out of ConnectionCount: the MaxPlayers gate
					 * in WorldServerAuthenticator admitted past the cap and the world's pulse
					 * published a near-empty population to the server list. */
					await Log.Warning("WorldSceneSystem", $"Scene population fetch failed (WorldServerID={worldServerID}): [{scenesResult.ErrorCode}] {scenesResult.ErrorMessage}. Keeping the last known count.");
					sceneCharacterCount = runtimeData.CachedSceneCharacterCount;
				}
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
				Kick(conn, "Failed to get account name", DisconnectNoticeReason.ProtocolViolation, terminal: true);
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				Kick(conn, "Failed to access database or world server system", DisconnectNoticeReason.ServerError);
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				Kick(conn, "Failed to get world scene mapping data", DisconnectNoticeReason.ServerError);
				return;
			}

			// Arm the residency watchdog: from here the client must be routed and gone.
			worldResidencyDeadlineByClientId[conn.ClientId] =
				MonotonicClock.NowSeconds + WorldResidencyGraceSeconds;

			/* Rate-limited means "not yet", not "go away".
			 *
			 * This debounce is per account and lasts instanceLookupDebounceSeconds (3s), and the
			 * ordinary way to trip it is a client that has just been here: a scene server that
			 * refuses a handoff — the instance is hosted elsewhere, its scene is not loaded, the
			 * claim was contended — sends the client straight back, and it re-authenticates well
			 * inside the window. Kicking there turned one recoverable routing hiccup into two
			 * round trips through the login pipeline, with a "you are doing that too often"
			 * notice for a player who did nothing but reconnect.
			 *
			 * Queue it instead. The routing cycle calls ProcessInstanceBatchAsync for
			 * everything on this queue, and that applies the same debounce itself and puts the
			 * connection back if it is still inside the window — so the limit is enforced exactly
			 * as before, it just costs a wait rather than a session. The wait is bounded from both
			 * ends: the queue TTL purges a connection that never routes, and the residency
			 * watchdog armed above covers a connection that ends up in no queue at all. */
			if (!TryBeginInstanceLookup(accountName))
			{
				Log.Debug("WorldSceneSystem",
					$"World Scene System: {conn.ClientId} instance lookup debounced; queued for the next routing cycle.");
				AddToQueue(conn, 0L, mappingData.WaitingInstanceConnections, mappingData.InstanceConnectionScenes);
				return;
			}

			if (!TryEnqueueAsyncWork(() => ProcessInstanceBatchAsync(new List<NetworkConnection>(1) { conn }, skipDebounce: true), conn.ClientId))
			{
				Kick(conn, "Failed to enqueue instance connection processing", DisconnectNoticeReason.ServerError);
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
				Kick(conn, "Waiting queue capacity exceeded", DisconnectNoticeReason.RoutingFailed);
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
			// The queue-entry clock is intentionally left alone. See ClearQueueTracking.
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

			// On the monotonic clock: a debounce is a duration. See ExpiringKeyTracker.TryBegin(TKey, double, TimeSpan).
			return runtimeData.InstanceLookupDebounce.TryBegin(
				accountName,
				MonotonicClock.NowSeconds,
				TimeSpan.FromSeconds(instanceLookupDebounceSeconds));
		}

		/// <summary>
		/// Removes expired account debounce entries to bound dictionary growth.
		/// </summary>
		/// <remarks>
		/// Repeated until the tracker is drained rather than capped at one pass.
		/// <para>
		/// The per-pass limits exist to bound how long a single sweep holds the tracker's lock, but
		/// applied once per <see cref="debounceCleanupIntervalSeconds"/> they also bounded the
		/// <em>rate</em> at which entries could be reclaimed — at the shipped values, 128 every 60
		/// seconds. An entry is added for every authentication and expires after
		/// <see cref="instanceLookupDebounceSeconds"/> (3s), so any world server sustaining more
		/// than about two logins a second accumulated expired entries faster than it could remove
		/// them and the tracker grew without bound for the life of the process.
		/// </para>
		/// <para>
		/// The loop is not unbounded work: <c>SweepExpired</c> walks an expiry-ordered queue and
		/// stops at the first entry that is still live, so each pass costs only what it actually
		/// reclaims. The total is capped anyway, at the tracker's own size, so a pathological
		/// sweep cannot monopolise the frame.
		/// </para>
		/// </remarks>
		/// <param name="runtimeData">Cached runtime data from the caller to avoid redundant TryGet.</param>
		private void CleanupExpiredDebounceEntries(WorldSceneSystemRuntimeData runtimeData)
		{
			var tracker = runtimeData.InstanceLookupDebounce;
			if (tracker == null)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			int budget = Math.Max(debounceCleanupMaxRemovalsPerSweep, tracker.Count);
			int reclaimed = 0;

			while (reclaimed < budget)
			{
				int removed = tracker.SweepExpired(
					now,
					debounceCleanupMaxScanPerSweep,
					debounceCleanupMaxRemovalsPerSweep);

				if (removed < 1)
				{
					break;
				}

				reclaimed += removed;
			}
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

			double now = MonotonicClock.NowSeconds;
			var staleConnections = new List<NetworkConnection>(waitingQueuePurgeMaxPerSweep);

			CollectStaleQueuedConnections(runtimeData, mappingData.OpenWorldConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep, mappingData);
			if (staleConnections.Count < waitingQueuePurgeMaxPerSweep)
			{
				CollectStaleQueuedConnections(runtimeData, mappingData.InstanceConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep - staleConnections.Count, mappingData);
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

				// Read before RemoveFromQueue below, which takes the connection out of the maps
				// EffectiveQueueTtlSeconds needs to tell a scene-load wait from a capacity one.
				bool shouldKick = false;
				WorldSceneQueueReason purgedReason = WorldSceneQueueReason.Capacity;
				if (conn.IsActive &&
					runtimeData.WaitingQueueEnteredAtByClientId.TryGetValue(conn.ClientId, out double queuedAt))
				{
					shouldKick = IsQueueWaitExpired(conn, queuedAt, now, mappingData);
					purgedReason = CurrentQueueReason(conn, mappingData);
				}

				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);

				if (conn.IsActive && shouldKick)
				{
					/* Tell the client the wait was abandoned, then close the connection in a way
					 * that lets that message out. Kick() calls Disconnect(true), which drops the
					 * transport immediately and discards anything still queued for send —
					 * including this notice, which is the only thing that dismisses the
					 * "waiting for a world slot" dialog. The client would otherwise be left
					 * looking at a stale position until its own reconnect logic noticed.
					 * LoginQueueSystem's purge closes the same way for the same reason.
					 *
					 * The reason is what the connection was waiting for when its time ran out,
					 * so the client can say which wait failed: a full world and a zone that never
					 * finished loading are different advice to the player. */
					SendQueuePosition(conn, -1, 0, 0, purgedReason);
					Log.Debug("WorldSceneSystem", $"World Scene System: {conn.ClientId} waiting queue TTL exceeded.");
					conn.Disconnect(false);
				}

				/* Terminal either way: purged, or already gone. Neither was the player's choice,
				 * so an open-world place is held for the grace window: the purge sends a player
				 * back because the line stalled, and "Try again" should not cost them their place
				 * in a line that was not moving for anyone. */
				EndWaitKeepingPlace(conn.ClientId);
			}
		}

		/// <summary>
		/// Multiplier applied to <see cref="waitingQueueTtlSeconds"/> while a connection is
		/// waiting for a scene instance that is still being created.
		/// </summary>
		/// <remarks>
		/// Waiting for capacity and waiting for a zone to boot are not the same wait. The
		/// capacity TTL is a product decision — after so long with the world full, send the
		/// player back to pick again — but a large world scene can take longer than that to load
		/// on a scene server, and purging then bounces a player whose zone was seconds from
		/// ready, straight into a reconnect that lands them in the same queue. The scene-load
		/// wait is separately bounded on the far end by the scene server's own pending-scene
		/// timeout, so a scene that never arrives still ends this wait.
		/// </remarks>
		private const float SceneLoadWaitTtlMultiplier = 4.0f;

		/// <summary>
		/// The TTL that applies to one waiting connection, which depends on what it is waiting
		/// for. See <see cref="SceneLoadWaitTtlMultiplier"/>.
		/// </summary>
		/// <remarks>
		/// The combat-logout hold is not represented here: it keeps the TTL at bay by restarting
		/// the wait clock every cycle it holds (see <see cref="ResetQueueEntryTime"/>), because
		/// it is bounded by <see cref="CombatLogoutRoutingGraceSeconds"/> instead.
		/// </remarks>
		/// <param name="conn">The waiting connection.</param>
		/// <param name="mappingData">Queue maps, used to find which scene the connection waits on.</param>
		private float EffectiveQueueTtlSeconds(NetworkConnection conn, IWorldSceneMappingData<NetworkConnection> mappingData)
		{
			// An instance queue only ever waits on the instance scene becoming ready.
			bool waitingOnSceneLoad = mappingData == null ||
				mappingData.InstanceConnectionScenes.ContainsKey(conn);

			if (!waitingOnSceneLoad &&
				mappingData.OpenWorldConnectionScenes.TryGetValue(conn, out string sceneName) &&
				queueReasonByScene.TryGetValue(sceneName, out WorldSceneQueueReason reason))
			{
				waitingOnSceneLoad = reason == WorldSceneQueueReason.SceneLoading;
			}

			return waitingOnSceneLoad
				? waitingQueueTtlSeconds * SceneLoadWaitTtlMultiplier
				: waitingQueueTtlSeconds;
		}

		/// <summary>
		/// Whether one waiting connection's wait has run out.
		/// </summary>
		/// <remarks>
		/// An open-world wait is timed from the later of joining the queue and the scene's last
		/// placement (<see cref="lastPlacementAtByScene"/>), so the TTL fires when the line has
		/// stalled rather than while it is moving. The rule itself is
		/// <see cref="WorldSceneRoutingRules.QueueWaitExpired"/>.
		/// </remarks>
		/// <param name="conn">The waiting connection.</param>
		/// <param name="queuedAt">When it joined the queue (<see cref="MonotonicClock"/> seconds).</param>
		/// <param name="now">The current <see cref="MonotonicClock"/> reading.</param>
		/// <param name="mappingData">Queue maps, used to find which scene the connection waits on.</param>
		private bool IsQueueWaitExpired(NetworkConnection conn, double queuedAt, double now, IWorldSceneMappingData<NetworkConnection> mappingData)
		{
			double? lastPlacementAt = null;
			if (mappingData != null &&
				mappingData.OpenWorldConnectionScenes.TryGetValue(conn, out string sceneName) &&
				lastPlacementAtByScene.TryGetValue(sceneName, out double placedAt))
			{
				lastPlacementAt = placedAt;
			}
			return WorldSceneRoutingRules.QueueWaitExpired(queuedAt, lastPlacementAt, now, EffectiveQueueTtlSeconds(conn, mappingData));
		}

		/// <summary>
		/// What a waiting connection is waiting for right now, as its position reports say.
		/// </summary>
		/// <remarks>
		/// The same precedence <see cref="BroadcastQueuePositions"/> uses: a connection-specific
		/// reason (the combat-logout hold) outranks its scene's, and the instance queue only ever
		/// waits on a load. Read before the connection leaves the queue maps.
		/// </remarks>
		/// <param name="conn">The waiting connection.</param>
		/// <param name="mappingData">Queue maps, used to find which scene the connection waits on.</param>
		private WorldSceneQueueReason CurrentQueueReason(NetworkConnection conn, IWorldSceneMappingData<NetworkConnection> mappingData)
		{
			if (queueReasonByClientId.TryGetValue(conn.ClientId, out WorldSceneQueueReason perConn))
			{
				return perConn;
			}

			if (mappingData == null || mappingData.InstanceConnectionScenes.ContainsKey(conn))
			{
				return WorldSceneQueueReason.SceneLoading;
			}

			return mappingData.OpenWorldConnectionScenes.TryGetValue(conn, out string sceneName) &&
				queueReasonByScene.TryGetValue(sceneName, out WorldSceneQueueReason sceneReason)
				? sceneReason
				: WorldSceneQueueReason.Capacity;
		}

		/// <summary>
		/// Collects stale queued connections from a source set based on activity and queue age.
		/// </summary>
		/// <param name="runtimeData">Cached runtime data from the caller to avoid redundant TryGet.</param>
		/// <param name="source">Source connection set snapshot input.</param>
		/// <param name="staleConnections">Output list to append stale connections into.</param>
		/// <param name="now">Current <see cref="MonotonicClock"/> reading used for TTL comparisons.</param>
		/// <param name="maxToCollect">Maximum stale connections to collect in this pass.</param>
		private void CollectStaleQueuedConnections(
			WorldSceneSystemRuntimeData runtimeData,
			IEnumerable<NetworkConnection> source,
			List<NetworkConnection> staleConnections,
			double now,
			int maxToCollect,
			IWorldSceneMappingData<NetworkConnection> mappingData)
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

				if (!runtimeData.WaitingQueueEnteredAtByClientId.TryGetValue(conn.ClientId, out double queuedAt))
				{
					runtimeData.WaitingQueueEnteredAtByClientId[conn.ClientId] = now;
					continue;
				}

				if (IsQueueWaitExpired(conn, queuedAt, now, mappingData))
				{
					staleConnections.Add(conn);
				}
			}
		}

		/// <summary>
		/// Moves a connection from the instance queue to the open-world queue for its scene. Main
		/// thread only.
		/// </summary>
		/// <param name="conn">Network connection.</param>
		/// <param name="sceneName">Open-world scene it now waits for: the character's own scene, as read by the pass that decided this.</param>
		private void MoveToOpenWorldQueueNow(NetworkConnection conn, string sceneName)
		{
			if (conn == null || !conn.IsActive || HasLeftQueue(conn))
			{
				return;
			}

			bool queued = false;
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
				AddToQueue(conn, sceneName, mappingData.WaitingOpenWorldConnections, mappingData.OpenWorldConnectionScenes);
				// AddToQueue refuses, and kicks, when the queue is at its cap.
				queued = mappingData.OpenWorldConnectionScenes.ContainsKey(conn);
			}

			/* A different queue, waiting on a different thing: start the expiry clock again.
			 *
			 * The clock deliberately survives a re-queue, because a routing cycle that puts
			 * a connection straight back is the same wait continuing. This is not that. The
			 * ordinary way to arrive here is having waited out InstanceReadyGraceSeconds
			 * (180s) for an instance that never became ready — which already exceeds the
			 * open-world TTL (45s), so the very next purge sweep kicked the character the
			 * system had just gone to the trouble of rescuing, with "the world server could
			 * not find room for your character". The instance wait is bounded by the scene
			 * row's own age; the open-world wait it hands over to must be measured from
			 * here.
			 *
			 * The stale per-connection reason goes too: whatever this client was previously
			 * told it was waiting for, it is now waiting for open-world capacity. */
			ResetQueueEntryTime(conn.ClientId);
			queueReasonByClientId.TryRemove(conn.ClientId, out _);

			/* This is where a connection joins an open-world line, so it is where a place held for
			 * its account is taken back. Only the reporting clock moves: it is what the line is
			 * ordered by. The expiry clock was just reset above and stays reset, because a
			 * resumed place is a new visit to a line that may still be stalled, and timing it
			 * from the old start would purge it on the next sweep. */
			if (queued && Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				ResumeOrBeginPlace(conn.ClientId, accountName, sceneName);
			}
		}

		/// <summary>
		/// Records which account and scene an open-world wait belongs to, and resumes the place
		/// that account holds in that scene's line, if any. Main thread only.
		/// </summary>
		/// <param name="clientId">The joining connection.</param>
		/// <param name="accountName">Its account.</param>
		/// <param name="sceneName">The open-world scene it now waits for.</param>
		/// <remarks>
		/// The record is what lets <see cref="EndWaitKeepingPlace"/> remember the place later: by
		/// the time a connection is noticed gone the account manager may already have dropped it,
		/// and a routing pass may have it off the queue maps. See <see cref="WorldQueuePlaceMemory"/>.
		/// </remarks>
		private void ResumeOrBeginPlace(int clientId, string accountName, string sceneName)
		{
			openWorldPlaceByClientId[clientId] = (accountName, sceneName);

			double now = MonotonicClock.NowSeconds;
			if (!queuePlaces.TryResume(accountName, sceneName, now, out double heldSince))
			{
				return;
			}

			double current = waitingSinceByClientId.TryGetValue(clientId, out double since) ? since : now;
			if (heldSince < current)
			{
				waitingSinceByClientId[clientId] = heldSince;
				Log.Debug("WorldSceneSystem",
					$"World Scene System: {clientId} resumed its account's place in the '{sceneName}' queue ({now - heldSince:F0}s of waiting kept).");
			}
		}

		/// <summary>
		/// Ends a wait that ended without the player's say: remembers the open-world place for the
		/// grace window, then drops the connection's tracking. Main thread only.
		/// </summary>
		/// <remarks>
		/// For a dropped connection and a purged one. A connection that was routed, kicked for a
		/// routing failure, or that left on purpose ends through <see cref="ClearQueueTracking"/>
		/// and keeps nothing. A connection that was only ever on the instance queue has no
		/// open-world place and keeps nothing either: an instance wait is for one instance to load,
		/// and everyone waiting on it goes in together.
		/// </remarks>
		/// <param name="clientId">FishNet client ID.</param>
		private void EndWaitKeepingPlace(int clientId)
		{
			if (openWorldPlaceByClientId.TryGetValue(clientId, out var place) &&
				waitingSinceByClientId.TryGetValue(clientId, out double waitingSince))
			{
				queuePlaces.Remember(place.Account, place.SceneName, waitingSince, MonotonicClock.NowSeconds);
			}
			ClearQueueTracking(clientId);
		}

		/// <summary>
		/// A player chose to leave the scene-routing queue. Their place goes with them.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Sent by the client just before it closes the connection, which from here is otherwise
		/// indistinguishable from a drop, and a drop keeps the place. See
		/// <see cref="WorldSceneQueueLeaveBroadcast"/>.
		/// </para>
		/// <para>
		/// The connection is also marked as leaving until it is gone. A routing pass may have it
		/// off the queue maps right now, working asynchronously, and without the mark that pass
		/// would put it back on a queue, or send it a scene server to connect to, in the half
		/// second the client spends closing.
		/// </para>
		/// </remarks>
		private void OnWorldSceneQueueLeave(NetworkConnection conn, WorldSceneQueueLeaveBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			leftQueueClientIds.Add(conn.ClientId);

			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
			}

			string accountName = openWorldPlaceByClientId.TryGetValue(conn.ClientId, out var place)
				? place.Account
				: null;
			if (accountName == null)
			{
				Server.AccountManager.GetAccountNameByConnection(conn, out accountName);
			}
			queuePlaces.Forget(accountName);

			ClearQueueTracking(conn.ClientId);
			Log.Debug("WorldSceneSystem", $"World Scene System: {conn.ClientId} left the queue.");
		}

		/// <summary>
		/// Whether a connection has told this server it is leaving the queue. See
		/// <see cref="OnWorldSceneQueueLeave"/>.
		/// </summary>
		private bool HasLeftQueue(NetworkConnection conn) => conn != null && leftQueueClientIds.Contains(conn.ClientId);

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
		/// Closes a connection, logging the internal reason and telling the client a
		/// player-facing one.
		/// </summary>
		/// <remarks>
		/// This used to call <c>conn.Kick(KickReason.UnexpectedProblem)</c>, which FishNet does
		/// not relay to the client at all — every routing failure here therefore put the player
		/// back on the login screen with no explanation, and with no way to tell "try again in a
		/// second" apart from "this character cannot be logged in". The <paramref name="reason"/>
		/// string stays server-side for operators; the client is sent a
		/// <see cref="DisconnectNoticeReason"/> it can word for itself.
		/// </remarks>
		/// <param name="conn">Network connection.</param>
		/// <param name="reason">Internal reason, for the server log.</param>
		/// <param name="notice">What to tell the client.</param>
		/// <param name="terminal">True when reconnecting cannot help.</param>
		private void Kick(NetworkConnection conn, string reason,
			DisconnectNoticeReason notice = DisconnectNoticeReason.ServerError, bool terminal = false)
		{
			Log.Debug("WorldSceneSystem", $"World Scene System: {conn.ClientId} {reason}.");
			DisconnectWithNotice(conn, notice, terminal);
		}

		/// <summary>
		/// Deadline by which each authenticated connection must have left this world server,
		/// keyed by ClientId.
		/// </summary>
		/// <remarks>
		/// The world server is a router: a client authenticates, gets a
		/// <see cref="WorldSceneConnectBroadcast"/>, and disconnects on its own to dial the
		/// scene server. Every step in between can drop the client without leaving it anywhere
		/// a sweep would find it — a routing pass takes connections off the waiting queues before
		/// going async, and delivers what it decided through <c>TryEnqueueMainThread</c> (see
		/// <see cref="ApplyRoutingOutcome"/>), which returns false and discards the action when the
		/// main-thread queue is saturated. A connection lost that way is in no queue, so
		/// <see cref="PurgeExpiredWaitingConnections"/> never sees it, and it sits authenticated
		/// on the world server forever with no scene and no error.
		/// <para>
		/// This bounds every such path at once rather than patching each delivery site: staying
		/// here past the deadline means routing failed, whatever the cause. Disconnecting hands
		/// the client back to its own reconnect loop, which returns to this world server and
		/// tries again — the same recovery a dropped connection already gets.
		/// </para>
		/// <para>
		/// Deadlines are <see cref="MonotonicClock"/> readings. On the wall clock, a host clock
		/// stepped forward put every authenticated connection past its deadline at once, and the
		/// next sweep disconnected the lot.
		/// </para>
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<int, double> worldResidencyDeadlineByClientId =
			new System.Collections.Concurrent.ConcurrentDictionary<int, double>();

		/// <summary>
		/// How long an authenticated connection may remain on the world server before it is
		/// treated as failed routing.
		/// </summary>
		/// <remarks>
		/// Comfortably above <see cref="waitingQueueTtlSeconds"/> (45s), which already kicks a
		/// connection that legitimately waits too long in a queue. This is the backstop for
		/// connections that are not in a queue at all, so it must not fire first and pre-empt
		/// the more specific check.
		/// </remarks>
		private const double WorldResidencyGraceSeconds = 90.0;

		/// <summary>
		/// When each character was first deferred because its combat-logout body's scene
		/// instance was unavailable, keyed by character ID.
		/// </summary>
		/// <remarks>
		/// Needed because the combat-logout hold restarts <c>WaitingQueueEnteredAtByClientId</c>
		/// on every cycle it holds, so the queue's own timestamp cannot measure how long we have
		/// been holding out for a specific scene instance.
		/// <para>On <see cref="MonotonicClock"/>, like every other wait this system measures.</para>
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<long, double> combatLogoutRoutingDeferredSince =
			new System.Collections.Concurrent.ConcurrentDictionary<long, double>();

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

		#region Queue Position Feedback

		/// <summary>
		/// Per-connection wait reason, when it is specific to that connection rather than to the
		/// scene it is waiting for. Only the combat-logout deferral sets one.
		/// </summary>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<int, WorldSceneQueueReason> queueReasonByClientId =
			new System.Collections.Concurrent.ConcurrentDictionary<int, WorldSceneQueueReason>();

		/// <summary>
		/// When a routing pass last placed anyone waiting on a given open-world scene, as a
		/// <see cref="MonotonicClock"/> reading. Written from the routing worker, read from the
		/// main-thread sweep.
		/// </summary>
		/// <remarks>
		/// The queue is first in, first out, and a capacity wait's TTL is measured from the later
		/// of joining the queue and the scene's last placement (see <see cref="IsQueueWaitExpired"/>).
		/// Measured from joining alone, a long queue purged players while the line ahead of them
		/// was still moving, and "try again" put them at the back, so they cycled without ever
		/// reaching the front. The purge exists to send players back to choose again when the
		/// world is not draining, and a stalled line is exactly that.
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> lastPlacementAtByScene =
			new System.Collections.Concurrent.ConcurrentDictionary<string, double>();

		/// <summary>
		/// Why clients waiting on a given open-world scene name are waiting, as of the last
		/// routing pass. Written from the routing worker, read from the main-thread sweep.
		/// </summary>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, WorldSceneQueueReason> queueReasonByScene =
			new System.Collections.Concurrent.ConcurrentDictionary<string, WorldSceneQueueReason>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// How many connections the last routing pass placed for each open-world scene name.
		/// </summary>
		/// <remarks>
		/// The only honest basis for an estimate. The queue drains in bulk whenever capacity
		/// exists, so there is no fixed admission rate to divide by the way the login queue has;
		/// what the client can be told is "this is how fast the queue actually moved last
		/// cycle". Zero means nothing moved, which is reported as unknown rather than as no
		/// wait — the client must not render "0s" over a queue that is not draining.
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> routedLastCycleByScene =
			new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Scratch list reused by the position sweep to avoid per-sweep allocation.</summary>
		private readonly List<(NetworkConnection Conn, double EnteredAt)> queuePositionScratch =
			new List<(NetworkConnection, double)>();

		/// <summary>
		/// How long a connection must have been waiting before it is told it is waiting.
		/// </summary>
		/// <remarks>
		/// Every routed client passes through the queue — it is enqueued on authentication and
		/// emptied by the next routing cycle — so a position sweep landing in that gap would
		/// flash "Queue position: 1 of 1" over a login that was never queued in any meaningful
		/// sense. The delay has to exceed one full routing cycle for that to be impossible,
		/// which is what <see cref="QueueFeedbackDelayCycles"/> guarantees.
		/// <para>
		/// Positions are still ranked across the whole group, so a client that crosses the
		/// threshold sees its true position rather than one computed from the subset being
		/// notified.
		/// </para>
		/// </remarks>
		private const float QueueFeedbackDelayCycles = 1.5f;

		/// <summary>Floor on the feedback delay, independent of the routing cycle rate.</summary>
		private const float QueueFeedbackDelayMinSeconds = 3.0f;

		/// <summary>
		/// Tells every waiting connection where it stands in the scene-routing queue.
		/// </summary>
		/// <remarks>
		/// The World → Scene hop is the one leg of the pipeline that can legitimately wait a
		/// long time — for capacity, for a scene instance to finish loading, or for a
		/// combat-logout body's own instance to come back — and it did all of that in complete
		/// silence behind a loading overlay. That is indistinguishable from a hang, and it is
		/// the one wait a player cannot reason about, because unlike the login queue there was
		/// nothing on screen at all. This is the login queue's feedback channel applied to the
		/// same problem one hop later.
		/// <para>
		/// Positions are ranked by queue-entry time within each scene name (or instance id), so
		/// they are stable and monotonic for a client that keeps waiting while others ahead of
		/// it are placed.
		/// </para>
		/// </remarks>
		private void BroadcastQueuePositions()
		{
			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}
			if (mappingData.OpenWorldConnectionScenes.Count == 0 &&
				mappingData.InstanceConnectionScenes.Count == 0)
			{
				return;
			}
			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			foreach (var kvp in mappingData.WaitingOpenWorldConnections)
			{
				string sceneName = kvp.Key;
				if (!queueReasonByScene.TryGetValue(sceneName, out WorldSceneQueueReason sceneReason))
				{
					sceneReason = WorldSceneQueueReason.Capacity;
				}
				routedLastCycleByScene.TryGetValue(sceneName, out int routedLastCycle);

				BroadcastGroupPositions(runtimeData, kvp.Value, sceneReason, routedLastCycle);
			}

			foreach (var kvp in mappingData.WaitingInstanceConnections)
			{
				// An instance queue is only ever waiting on the instance scene itself to become
				// ready; there is no capacity dimension to it.
				BroadcastGroupPositions(runtimeData, kvp.Value, WorldSceneQueueReason.SceneLoading, 0);
			}
		}

		/// <summary>
		/// Ranks one waiting group by arrival and sends each member its position.
		/// </summary>
		private void BroadcastGroupPositions(
			WorldSceneSystemRuntimeData runtimeData,
			HashSet<NetworkConnection> group,
			WorldSceneQueueReason groupReason,
			int routedLastCycle)
		{
			if (group == null || group.Count == 0)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			float feedbackDelaySeconds = Math.Max(
				QueueFeedbackDelayMinSeconds,
				Math.Max(0.5f, runtimeData.WaitQueueRateSeconds) * QueueFeedbackDelayCycles);

			queuePositionScratch.Clear();
			foreach (NetworkConnection conn in group)
			{
				if (conn == null || !conn.IsActive)
				{
					continue;
				}
				// The reporting clock, not the expiry clock — see waitingSinceByClientId.
				double enteredAt = waitingSinceByClientId.TryGetValue(conn.ClientId, out double waitingSince)
					? waitingSince
					: now;
				queuePositionScratch.Add((conn, enteredAt));
			}

			if (queuePositionScratch.Count == 0)
			{
				return;
			}

			// Ties broken by ClientId so the ordering is total and does not flip between sweeps.
			queuePositionScratch.Sort((a, b) =>
			{
				int byTime = a.EnteredAt.CompareTo(b.EnteredAt);
				return byTime != 0 ? byTime : a.Conn.ClientId.CompareTo(b.Conn.ClientId);
			});

			int total = queuePositionScratch.Count;
			for (int i = 0; i < total; ++i)
			{
				NetworkConnection conn = queuePositionScratch[i].Conn;
				int position = i + 1;

				// Ranked, but not yet told. See QueueFeedbackDelayCycles.
				if (now - queuePositionScratch[i].EnteredAt < feedbackDelaySeconds)
				{
					continue;
				}

				// A connection-specific reason outranks the group's: the combat-logout hold is
				// about this character's body, not about the scene everyone else is waiting for.
				WorldSceneQueueReason reason = queueReasonByClientId.TryGetValue(conn.ClientId, out WorldSceneQueueReason perConn)
					? perConn
					: groupReason;

				SendQueuePosition(conn, position, EstimateQueueWaitSeconds(position, routedLastCycle), total, reason);
			}

			queuePositionScratch.Clear();
		}

		/// <summary>
		/// Estimates the remaining wait from how many connections the last routing pass placed.
		/// </summary>
		/// <returns>Seconds, or 0 when the queue is not draining and no estimate is honest.</returns>
		private int EstimateQueueWaitSeconds(int position, int routedLastCycle)
		{
			if (position <= 0 || routedLastCycle <= 0)
			{
				return 0;
			}

			float cycleSeconds = Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData)
				? Mathf.Max(0.5f, runtimeData.WaitQueueRateSeconds)
				: 2.0f;

			double cycles = Math.Ceiling(position / (double)routedLastCycle);
			return (int)Math.Ceiling(cycles * cycleSeconds);
		}

		/// <summary>
		/// Sends one scene-routing queue position update.
		/// </summary>
		/// <remarks>
		/// Channel selection matches the login queue and for the same reason. A positive
		/// position is a periodic progress report that the next sweep corrects, so Unreliable
		/// keeps a large queue off the reliable channel. Position 0 (routed) and -1 (cancelled)
		/// are one-shot transitions with nothing behind them — losing one strands the wait UI on
		/// screen — so they go reliably.
		/// </remarks>
		private void SendQueuePosition(NetworkConnection conn, int position, int estimatedWaitSeconds, int totalQueued, WorldSceneQueueReason reason)
		{
			if (conn == null || !conn.IsActive)
			{
				return;
			}

			Server?.NetworkWrapper?.Broadcast(conn,
				new WorldSceneQueuePositionBroadcast
				{
					QueuePosition = position,
					EstimatedWaitSeconds = estimatedWaitSeconds,
					TotalQueued = totalQueued,
					Reason = reason,
				},
				true,
				position > 0 ? FishNet.Transporting.Channel.Unreliable : FishNet.Transporting.Channel.Reliable);
		}

		#endregion

		/// <summary>
		/// What one routing pass decided for each connection it took off a queue, applied on the
		/// main thread in one queued action by <see cref="ApplyRoutingOutcome"/>.
		/// </summary>
		/// <remarks>
		/// Every connection a pass takes off a queue ends in exactly one of these: sent on, put back,
		/// moved to the open-world queue, or kicked. Each used to be its own queued main-thread
		/// action, so a pass paid a closure and a queue slot per connection — thousands for a full
		/// queue that could place a handful. Filled on one worker, read on the main thread after it
		/// has been handed over, never both at once.
		/// </remarks>
		private sealed class RoutingOutcome
		{
			public readonly List<(NetworkConnection Conn, ushort Port)> Connects = new List<(NetworkConnection, ushort)>();
			public readonly List<(NetworkConnection Conn, long InstanceKey)> InstanceRequeues = new List<(NetworkConnection, long)>();
			public readonly List<(NetworkConnection Conn, string SceneName, WorldSceneQueueReason? Reason, bool RestartWaitClock)> OpenWorldRequeues =
				new List<(NetworkConnection, string, WorldSceneQueueReason?, bool)>();
			public readonly List<(NetworkConnection Conn, string SceneName)> OpenWorldMoves = new List<(NetworkConnection, string)>();
			public readonly List<(NetworkConnection Conn, string Reason, DisconnectNoticeReason Notice)> Kicks =
				new List<(NetworkConnection, string, DisconnectNoticeReason)>();

			private readonly HashSet<NetworkConnection> decided = new HashSet<NetworkConnection>();

			/// <summary>Connections with an outcome recorded.</summary>
			public int Count => decided.Count;

			/// <summary>Whether a connection already has an outcome.</summary>
			public bool IsDecided(NetworkConnection conn) => conn != null && decided.Contains(conn);

			/// <summary>Send the client to a scene server. See <see cref="ApplySceneConnect"/>.</summary>
			public void Connect(NetworkConnection conn, ushort port)
			{
				if (conn != null && decided.Add(conn))
				{
					Connects.Add((conn, port));
				}
			}

			/// <summary>Put it back on the instance queue. See <see cref="RequeueInstanceNow"/>.</summary>
			public void RequeueInstance(NetworkConnection conn, long instanceKey)
			{
				if (conn != null && decided.Add(conn))
				{
					InstanceRequeues.Add((conn, instanceKey));
				}
			}

			/// <summary>Put it back on the open-world queue. See <see cref="RequeueOpenWorldNow"/>.</summary>
			public void RequeueOpenWorld(NetworkConnection conn, string sceneName,
				WorldSceneQueueReason? reason = null, bool restartWaitClock = false)
			{
				if (conn != null && decided.Add(conn))
				{
					OpenWorldRequeues.Add((conn, sceneName, reason, restartWaitClock));
				}
			}

			/// <summary>
			/// Move it to the open-world queue for its character's scene, or kick it when the
			/// character names none. See <see cref="MoveToOpenWorldQueueNow"/>.
			/// </summary>
			public void MoveToOpenWorld(NetworkConnection conn, CharacterData character)
			{
				if (character.ID <= 0 || string.IsNullOrEmpty(character.SceneName))
				{
					Kick(conn, "Failed to get selected scene", DisconnectNoticeReason.CharacterUnavailable);
					return;
				}
				if (conn != null && decided.Add(conn))
				{
					OpenWorldMoves.Add((conn, character.SceneName));
				}
			}

			/// <summary>Disconnect it with a notice.</summary>
			public void Kick(NetworkConnection conn, string reason, DisconnectNoticeReason notice)
			{
				if (conn != null && decided.Add(conn))
				{
					Kicks.Add((conn, reason, notice));
				}
			}
		}

		/// <summary>
		/// Hands a routing pass's decisions to the main thread as one queued action.
		/// </summary>
		/// <remarks>
		/// If the main-thread queue is saturated and refuses it, the connections are in no queue
		/// and on their way nowhere, which is exactly the case the residency watchdog exists for:
		/// it disconnects them into their clients' reconnect loops. See
		/// <see cref="worldResidencyDeadlineByClientId"/>.
		/// </remarks>
		private void ApplyRoutingOutcome(RoutingOutcome outcome)
		{
			if (outcome == null || outcome.Count == 0)
			{
				return;
			}

			if (!TryEnqueueMainThread(() => ApplyRoutingOutcomeNow(outcome)))
			{
				Log.Warning("WorldSceneSystem",
					$"The main-thread queue refused the routing outcome for {outcome.Count} connection(s); " +
					"the residency watchdog will return them to their reconnect loops.");
			}
		}

		/// <summary>Applies a routing pass's decisions. Main thread only.</summary>
		private void ApplyRoutingOutcomeNow(RoutingOutcome outcome)
		{
			for (int i = 0; i < outcome.Kicks.Count; ++i)
			{
				var (conn, reason, notice) = outcome.Kicks[i];
				if (conn.IsActive)
				{
					Kick(conn, reason, notice);
				}
				// Terminal: stop tracking the wait so a recycled ClientId cannot inherit it.
				ClearQueueTracking(conn.ClientId);
			}

			for (int i = 0; i < outcome.InstanceRequeues.Count; ++i)
			{
				RequeueInstanceNow(outcome.InstanceRequeues[i].Conn, outcome.InstanceRequeues[i].InstanceKey);
			}

			for (int i = 0; i < outcome.OpenWorldRequeues.Count; ++i)
			{
				var (conn, sceneName, reason, restartWaitClock) = outcome.OpenWorldRequeues[i];
				RequeueOpenWorldNow(conn, sceneName, reason, restartWaitClock);
			}

			for (int i = 0; i < outcome.OpenWorldMoves.Count; ++i)
			{
				MoveToOpenWorldQueueNow(outcome.OpenWorldMoves[i].Conn, outcome.OpenWorldMoves[i].SceneName);
			}

			for (int i = 0; i < outcome.Connects.Count; ++i)
			{
				ApplySceneConnect(outcome.Connects[i].Conn, outcome.Connects[i].Port);
			}
		}

		/// <summary>
		/// Puts a connection back on the open-world waiting queue for another routing pass. Main
		/// thread only.
		/// </summary>
		/// <remarks>
		/// A routing pass takes the connections it tries to place off the queue, so any it declines
		/// to route has to be put back or it is dropped entirely — it would sit on the world server
		/// with no scene assignment and no retry, which the client cannot recover from on its own.
		/// </remarks>
		/// <param name="conn">Connection to put back on the queue.</param>
		/// <param name="sceneName">Open-world scene it is waiting for.</param>
		/// <param name="reason">
		/// Connection-specific wait reason to report, or <c>null</c> to fall back to whatever the
		/// scene as a whole is waiting on.
		/// </param>
		/// <param name="restartWaitClock">
		/// True only for a wait that is bounded by something other than the queue TTL — see
		/// <see cref="ResetQueueEntryTime"/>.
		/// </param>
		private void RequeueOpenWorldNow(NetworkConnection conn, string sceneName,
			WorldSceneQueueReason? reason, bool restartWaitClock)
		{
			if (conn == null || !conn.IsActive || HasLeftQueue(conn))
			{
				return;
			}
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				AddToQueue(conn, sceneName, mappingData.WaitingOpenWorldConnections, mappingData.OpenWorldConnectionScenes);
			}

			if (reason.HasValue)
			{
				queueReasonByClientId[conn.ClientId] = reason.Value;
			}
			else
			{
				queueReasonByClientId.TryRemove(conn.ClientId, out _);
			}

			// Ordered after AddToQueue, which records an arrival time only when there is not
			// one already; the reset has to win over that.
			if (restartWaitClock)
			{
				ResetQueueEntryTime(conn.ClientId);
			}
		}

		/// <summary>
		/// Puts a connection back on the instance waiting queue for another routing pass. Main
		/// thread only.
		/// </summary>
		/// <remarks>
		/// <see cref="ProcessInstanceBatchAsync"/> takes the connection off the queue before its
		/// database work, so a pass that cannot decide — a read that failed rather than answered —
		/// has to put it back or the connection is dropped from routing entirely. The wait clock
		/// survives the round trip (see <see cref="RemoveFromQueue"/>), so the queue TTL still
		/// bounds how many passes a database outage can cost before the client is let go.
		/// </remarks>
		/// <param name="conn">Connection to put back on the queue.</param>
		/// <param name="instanceKey">Instance scene row it waits on, or 0 when not yet known.</param>
		private void RequeueInstanceNow(NetworkConnection conn, long instanceKey)
		{
			if (conn != null && conn.IsActive && !HasLeftQueue(conn) &&
				Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				AddToQueue(conn, instanceKey, mappingData.WaitingInstanceConnections, mappingData.InstanceConnectionScenes);
			}
		}

		/// <summary>
		/// Sends a connection its <see cref="WorldSceneConnectBroadcast"/>, unless it has been
		/// re-queued meanwhile. Main thread only.
		/// </summary>
		/// <param name="conn">Target network connection.</param>
		/// <param name="port">Scene server port to broadcast (address is always GameHost).</param>
		private void ApplySceneConnect(NetworkConnection conn, ushort port)
		{
			// A connection that has left the queue is closing; it is not sent anywhere.
			if (conn == null || !conn.IsActive || HasLeftQueue(conn))
			{
				return;
			}

			/* Race guard: if the connection was re-queued during async work, skip stale
			 * routing. Both queues are checked — a connection can be put back on either,
			 * and routing one that is waiting on the other sends it somewhere the later
			 * decision did not choose. */
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var guardMd) &&
				(guardMd.OpenWorldConnectionScenes.ContainsKey(conn) ||
				 guardMd.InstanceConnectionScenes.ContainsKey(conn)))
			{
				return;
			}

			Log.Info("WorldSceneSystem", $"BroadcastSceneConnect conn={conn.ClientId} -> port={port}");

			/* Position 0 first: the wait is over, and the client is about to tear this
			 * connection down to hop. Sent before the connect broadcast so the wait dialog
			 * is dismissed by the same message pass that starts the transition rather than
			 * being left on screen over the loading overlay of the scene it is entering. */
			SendQueuePosition(conn, 0, 0, 0, WorldSceneQueueReason.Capacity);

			Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
			{
				Port = port,
			});

			// Terminal: routed. Stop tracking the wait so a recycled ClientId cannot inherit it,
			// and give up any place still held for the account: it has been placed.
			if (openWorldPlaceByClientId.TryGetValue(conn.ClientId, out var place))
			{
				queuePlaces.Forget(place.Account);
			}
			ClearQueueTracking(conn.ClientId);
		}

		/// <summary>
		/// Records when a client entered a waiting queue, preserving the original arrival across
		/// re-queues.
		/// </summary>
		/// <remarks>
		/// This is what makes <see cref="waitingQueueTtlSeconds"/> mean anything. Routing empties
		/// the queue and puts back everything it could not place, so re-stamping on every add
		/// restarted the TTL on every cycle and the purge could never fire: a client waiting on
		/// capacity that never arrived waited forever, behind a loading screen, with the
		/// residency watchdog also pushing its own deadline out because the connection *was*
		/// queued. The TTL now measures the total wait, and the one wait that is legitimately
		/// longer — the combat-logout deferral, which is separately bounded by
		/// <see cref="CombatLogoutRoutingGraceSeconds"/> — asks for a reset explicitly via
		/// <see cref="ResetQueueEntryTime"/>.
		/// </remarks>
		/// <param name="clientId">FishNet client ID.</param>
		private void RecordQueueEntryTime(int clientId)
		{
			double now = MonotonicClock.NowSeconds;
			if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData) &&
				runtimeData.WaitingQueueEnteredAtByClientId != null &&
				!runtimeData.WaitingQueueEnteredAtByClientId.ContainsKey(clientId))
			{
				runtimeData.WaitingQueueEnteredAtByClientId[clientId] = now;
			}

			// The reporting clock, which nothing may reset. See waitingSinceByClientId.
			waitingSinceByClientId.TryAdd(clientId, now);
		}

		/// <summary>
		/// When each client first began waiting, for reporting rather than for expiry.
		/// </summary>
		/// <remarks>
		/// Deliberately a second clock. <c>WaitingQueueEnteredAtByClientId</c> answers "should
		/// this wait be cut short", and the combat-logout hold restarts it every cycle precisely
		/// so that it is not — which makes it useless for saying how long the player has been
		/// waiting, and worse than useless for ranking, since a held connection would keep
		/// sorting to the back of its own queue.
		/// <para>
		/// Using it for both is what suppressed the one message that matters most: a
		/// combat-logout hold resets the clock every routing cycle, so the reporting delay was
		/// never satisfied and a player waiting up to
		/// <see cref="CombatLogoutRoutingGraceSeconds"/> for their own body was told nothing at
		/// all — the exact silent wait this feedback exists to end.
		/// </para>
		/// <para>
		/// Both clocks are <see cref="MonotonicClock"/> readings, never the wall clock: they time
		/// waits, and a stepped host clock must not age every waiting connection at once.
		/// </para>
		/// </remarks>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<int, double> waitingSinceByClientId =
			new System.Collections.Concurrent.ConcurrentDictionary<int, double>();

		/// <summary>
		/// Places in the open-world queue held for accounts whose wait ended without their say.
		/// Main thread only. See <see cref="WorldQueuePlaceMemory"/>.
		/// </summary>
		private readonly WorldQueuePlaceMemory queuePlaces = new WorldQueuePlaceMemory();

		/// <summary>
		/// The account and open-world scene of each connection waiting in the open-world queue,
		/// from the moment it joins until its wait ends. Main thread only.
		/// </summary>
		/// <remarks>
		/// Kept apart from the queue maps because it has to outlive them: a routing pass takes a
		/// connection off the maps while it works, and the account manager may have forgotten a
		/// dropped connection before this system hears of the drop. Either way the place is still
		/// the account's. See <see cref="EndWaitKeepingPlace"/>.
		/// </remarks>
		private readonly Dictionary<int, (string Account, string SceneName)> openWorldPlaceByClientId =
			new Dictionary<int, (string Account, string SceneName)>();

		/// <summary>
		/// Connections that have said they are leaving the queue and have not gone yet. Main
		/// thread only. See <see cref="OnWorldSceneQueueLeave"/>.
		/// </summary>
		private readonly HashSet<int> leftQueueClientIds = new HashSet<int>();

		/// <summary>
		/// Restarts a client's queue-entry clock.
		/// </summary>
		/// <remarks>
		/// Only for a wait that is bounded by something other than the queue TTL. The
		/// combat-logout deferral is the sole caller: it holds a connection for up to
		/// <see cref="CombatLogoutRoutingGraceSeconds"/> waiting for the one scene instance that
		/// can hand its body back, then gives up deterministically — so it must not be cut short
		/// by the TTL, and the ordinary TTL should start counting again from the moment that
		/// wait ends.
		/// </remarks>
		/// <param name="clientId">FishNet client ID.</param>
		private void ResetQueueEntryTime(int clientId)
		{
			if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData) &&
				runtimeData.WaitingQueueEnteredAtByClientId != null)
			{
				runtimeData.WaitingQueueEnteredAtByClientId[clientId] = MonotonicClock.NowSeconds;
			}
		}

		/// <summary>
		/// Drops every piece of per-connection queue tracking.
		/// </summary>
		/// <remarks>
		/// Deliberately not called from <see cref="RemoveFromQueue"/>. That runs on re-queue
		/// cycles and on the instance-to-open-world fallback, both of which are the same client
		/// continuing the same wait — clearing there is what reset the TTL. Only the three
		/// terminal outcomes call this: routed, purged, or disconnected.
		/// </remarks>
		/// <param name="clientId">FishNet client ID.</param>
		private void ClearQueueTracking(int clientId)
		{
			if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.WaitingQueueEnteredAtByClientId?.Remove(clientId);
			}
			waitingSinceByClientId.TryRemove(clientId, out _);
			queueReasonByClientId.TryRemove(clientId, out _);
			openWorldPlaceByClientId.Remove(clientId);
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
		/// <returns>
		/// The available <see cref="SceneData"/> (empty when there is none), or <c>null</c> when the
		/// read failed — which the caller must not mistake for "none".
		/// </returns>
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
			if (!result.IsSuccess)
			{
				await Log.Warning("WorldSceneSystem", $"Available scene fetch failed for '{sceneName}' (WorldServerID={worldServerID}): [{result.ErrorCode}] {result.ErrorMessage}");
				return null;
			}
			if (result.Data == null || result.Data.Count < 1)
			{
				return Array.Empty<SceneData>();
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
			if (!result.IsSuccess || !IsSceneServerRoutable(result.Data))
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
			runtimeData.AvailableSceneCache?.SweepExpired(
				TimeSpan.FromSeconds(sceneInstanceCacheTtlSeconds), 64, 32);
			runtimeData.SceneServerAddressCache?.SweepExpired(
				TimeSpan.FromSeconds(sceneServerCacheTtlSeconds), 64, 32);
			SweepCombatLogoutRoutingDeferrals(MonotonicClock.NowSeconds);
		}

		/// <summary>Countdown to the next stale scene-row sweep.</summary>
		private float nextStaleSceneRowSweep = StaleSceneRowSweepIntervalSeconds;

		/// <summary>
		/// Whether a scene server is still pulsing, and so is a legitimate routing destination.
		/// </summary>
		/// <remarks>
		/// A scene server deletes its registration only on a graceful shutdown, so the row's
		/// existence proves nothing after a crash. See <see cref="SceneServerPulseStaleSeconds"/>.
		/// <para>
		/// Judged by the pulse's age as the database measured it when the row was read
		/// (<see cref="SceneServerData.PulseAgeSeconds"/>), never by subtracting the stamped
		/// <c>last_pulse</c> from this host's clock: that compared the database clock with this
		/// one, so a world host running a minute fast routed nobody anywhere and one running a
		/// minute slow kept routing players to a crashed scene server.
		/// </para>
		/// </remarks>
		private static bool IsSceneServerLive(SceneServerData serverData)
		{
			return serverData.PulseAgeSeconds < SceneServerPulseStaleSeconds;
		}

		/// <summary>
		/// Whether a scene server may be given new open-world arrivals.
		/// </summary>
		/// <remarks>
		/// Live and not locked. A locked scene server is being drained for maintenance: it keeps
		/// the players it has and stops receiving more, which only works if the thing that hands
		/// out players honours it.
		/// <para>
		/// Deliberately not applied to instance routing. A character bound to an instance can go
		/// to exactly one scene server — the one hosting it — so refusing on a lock would not
		/// drain that character, it would evict them from their dungeon and drop them in the open
		/// world with the instance abandoned. Draining new arrivals is what a lock is for;
		/// clearing people out is what a scheduled shutdown is for, and that one warns them first.
		/// </para>
		/// </remarks>
		private static bool IsSceneServerRoutable(SceneServerData serverData)
		{
			return IsSceneServerLive(serverData) && !serverData.Locked;
		}

		/// <summary>
		/// Kicks off a bounded deletion of this world server's scene rows that never became
		/// ready.
		/// </summary>
		/// <remarks>
		/// Nothing else removes a Pending, Loading or Failed row, and each one is a trap rather
		/// than merely clutter: the row keeps the <c>character_id</c> it was created for, so
		/// <c>ISceneService.FetchCharacterInstanceAsync</c> keeps handing it back and the
		/// character it belongs to keeps being routed at an instance that will never exist.
		/// <list type="bullet">
		/// <item><description>A Failed row made its dungeon permanently unenterable for that character — every attempt cost a full disconnect and put them back where they started.</description></item>
		/// <item><description>A Loading row orphaned by a scene server that died between dequeue and load still has <c>scene_server_id = 0</c>, so that server's restart cleanup (which matches on its own id) never removes it.</description></item>
		/// <item><description>A Pending row that no scene server ever dequeues — every scene server down, or at its per-pulse load cap — has no owner at all.</description></item>
		/// </list>
		/// The world server owns this because it is the only party that outlives any particular
		/// scene server and still knows which rows are its own.
		/// </remarks>
		private void QueueStaleSceneRowSweep()
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ||
				worldData.ID <= 0)
			{
				return;
			}

			long worldServerID = worldData.ID;
			if (!TryEnqueueAsyncWork(() => SweepStaleSceneRowsAsync(worldServerID), worldServerID))
			{
				Log.Warning("WorldSceneSystem", "Failed to enqueue the stale scene-row sweep.");
			}
		}

		/// <summary>
		/// Deletes this world server's non-ready scene rows older than
		/// <see cref="StaleSceneRowGraceSeconds"/>. See <see cref="QueueStaleSceneRowSweep"/>.
		/// </summary>
		private async Task SweepStaleSceneRowsAsync(long worldServerID)
		{
			try
			{
				if (!TryGetDbService(out ISceneService sceneService))
				{
					return;
				}

				// An age, not an instant: the rows are stamped by the database clock, and the cutoff
				// is taken from it too, so this host's clock cannot shift what counts as stale.
				DatabaseResult<int> result = await sceneService.DeleteStaleUnreadyAsync(
					worldServerID, StaleSceneRowGraceSeconds, StaleSceneRowMaxPerSweep);

				if (!result.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem",
						$"Stale scene-row sweep failed (WorldServerID={worldServerID}): {result.ErrorCode} - {result.ErrorMessage}");
					return;
				}

				if (result.Data > 0)
				{
					await Log.Info("WorldSceneSystem",
						$"Reaped {result.Data} scene row(s) that never became ready (WorldServerID={worldServerID}).");
				}

				/* Ready rows whose scene server has gone are the other half of the problem, and
				 * the more damaging half: this system routes players at them. A crashed scene
				 * server deletes nothing on its way out, so every scene it hosted stays
				 * advertised as available, and clients sent there are refused — or, once a
				 * replacement claims the same port, are accepted by a server that does not have
				 * the scene and bounces them straight back here to be routed at the same row
				 * again. Nothing in that cycle ages out, so it has to be broken from this end.
				 *
				 * The staleness window is handed to the database as an age, and the database
				 * compares it with the clock that stamped each pulse. */
				DatabaseResult<int> orphanResult = await sceneService.DeleteByStaleSceneServersAsync(
					worldServerID, SceneServerPulseStaleSeconds, StaleSceneRowMaxPerSweep);

				if (!orphanResult.IsSuccess)
				{
					await Log.Warning("WorldSceneSystem",
						$"Orphaned scene-row sweep failed (WorldServerID={worldServerID}): {orphanResult.ErrorCode} - {orphanResult.ErrorMessage}");
					return;
				}

				if (orphanResult.Data > 0)
				{
					await Log.Warning("WorldSceneSystem",
						$"Reaped {orphanResult.Data} scene row(s) belonging to scene servers that stopped pulsing (WorldServerID={worldServerID}).");

					// Those rows are in the routing caches too, and a cache hit would keep
					// sending players to them for the rest of the TTL.
					if (Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
					{
						runtimeData.AvailableSceneCache?.Clear();
						runtimeData.SceneServerAddressCache?.Clear();
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("WorldSceneSystem", $"Error sweeping stale scene rows: {ex}");
			}
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
		/// <param name="now">Current <see cref="MonotonicClock"/> reading.</param>
		private void SweepCombatLogoutRoutingDeferrals(double now)
		{
			if (combatLogoutRoutingDeferredSince.IsEmpty)
			{
				return;
			}

			foreach (var kvp in combatLogoutRoutingDeferredSince)
			{
				if (now - kvp.Value >= CombatLogoutRoutingGraceSeconds * 2.0)
				{
					combatLogoutRoutingDeferredSince.TryRemove(kvp.Key, out _);
				}
			}
		}

		#endregion
	}
}