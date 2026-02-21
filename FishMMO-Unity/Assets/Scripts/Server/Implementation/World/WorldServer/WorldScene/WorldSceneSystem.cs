using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;
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

		/// <summary>
		/// Maximum number of clients allowed per scene instance.
		/// </summary>
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		/// <summary>
		/// Maximum total connections allowed across all waiting queues.
		/// Defense-in-depth cap to prevent unbounded memory growth.
		/// </summary>
		private const int MAX_WAITING_QUEUE_SIZE = 5000;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache;

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
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

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
			runtimeData.WaitQueueRateSeconds = Mathf.Max(0.1f, runtimeData.WaitQueueRateSeconds);
			runtimeData.IsProcessingQueue = 0;
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

			// Connection state events
			ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

			// Authentication events
			if (runtimeData.LoginAuthenticator != null)
			{
				runtimeData.LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			// Delete world scene data from database
			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService) &&
				Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData))
			{
				Log.Debug("WorldSceneSystem", $"Deinitializing: Deleting world scenes (WorldServerID={worldData.ID})");
				// Blocking call during shutdown to ensure cleanup completes
				Task.Run(() => sceneService.DeleteByWorldServerAsync(worldData.ID)).GetAwaiter().GetResult();
			}
		}

		/// <summary>
		/// Drains queued main-thread actions from the IWorldSceneSystemMainThreadQueueData container.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<IWorldSceneSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadActionsPerFrame);
				}
			}
		}

		/// <summary>
		/// Enqueues an action to be executed on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IWorldSceneSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}

		/// <summary>
		/// Handles remote connection state changes. Removes connections from queues when they disconnect.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="args">Remote connection state arguments.</param>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState != RemoteConnectionState.Stopped)
			{
				return;
			}

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
		public override void OnLateUpdate(float deltaTime)
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
				PurgeExpiredWaitingConnections();
			}

			runtimeData.NextDebounceCleanup -= deltaTime;
			if (runtimeData.NextDebounceCleanup <= 0f)
			{
				runtimeData.NextDebounceCleanup = debounceCleanupIntervalSeconds;
				CleanupExpiredDebounceEntries();
			}

			if (runtimeData.NextWaitQueueUpdate <= 0)
			{
				runtimeData.NextWaitQueueUpdate = runtimeData.WaitQueueRateSeconds;

				if (Initialized &&
					Server.Database?.ServiceRegistry != null &&
					Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					// Snapshot the scene names and connections to process before going async
					List<string> openWorldSceneNames = mappingData.WaitingOpenWorldConnections.Keys.ToList();
					List<NetworkConnection> instanceConns = mappingData.InstanceConnectionScenes.Keys.ToList();

					bool beginProcessing = false;
					lock (runtimeData)
					{
						if (runtimeData.IsProcessingQueue == 0)
						{
							runtimeData.IsProcessingQueue = 1;
							beginProcessing = true;
						}
					}

					if (beginProcessing)
					{
						if (!TryEnqueueAsyncWork(() => ProcessQueuesAsync(openWorldSceneNames, instanceConns)))
						{
							lock (runtimeData)
							{
								runtimeData.IsProcessingQueue = 0;
							}
							Log.Warning("WorldSceneSystem", "Failed to enqueue world scene queue processing work item.");
						}
					}
				}
			}
			runtimeData.NextWaitQueueUpdate -= deltaTime;
		}

		/// <summary>
		/// Asynchronously processes open world and instance queues, then updates connection count.
		/// All main-thread state changes and Broadcasts are marshalled via EnqueueMainThread.
		/// </summary>
		private async Task ProcessQueuesAsync(List<string> openWorldSceneNames, List<NetworkConnection> instanceConns)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}

				foreach (string sceneName in openWorldSceneNames)
				{
					await ProcessOpenWorldQueueAsync(sceneName);
				}
				foreach (NetworkConnection conn in instanceConns)
				{
					await ProcessInstanceConnectionAsync(conn);
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
					lock (runtimeData)
					{
						runtimeData.IsProcessingQueue = 0;
					}
				}
			}
		}

		/// <summary>
		/// Processes the queue for open world scenes, assigning connections to available scenes or enqueuing new scene requests.
		/// </summary>
		/// <param name="sceneName">Name of the scene to process.</param>
		private async Task ProcessOpenWorldQueueAsync(string sceneName)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService) ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var charService))
			{
				return;
			}

			int maxClientsPerInstance = GetMaxClients(sceneName);
			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;

			var loadedScenesResult = await sceneService.FetchAvailableAsync(worldServerID, sceneName, maxClientsPerInstance);
			if (loadedScenesResult.IsSuccess && loadedScenesResult.Data != null && loadedScenesResult.Data.Count > 0)
			{
				foreach (var loadedScene in loadedScenesResult.Data)
				{
					var sceneServerResult = await sceneServerService.FetchAsync(loadedScene.SceneServerID);
					if (!sceneServerResult.IsSuccess)
					{
						continue;
					}
					var sceneServer = sceneServerResult.Data;

					// Snapshot connections on main thread, process assignments
					List<(NetworkConnection conn, string accountName)> validConnections = new List<(NetworkConnection, string)>();
					int currentCount = loadedScene.CharacterCount;

					var snapshotTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
					EnqueueMainThread(() =>
					{
						try
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

							Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var snapshotRuntimeData);

							foreach (NetworkConnection connection in connections.ToList())
							{
								if (currentCount >= maxClientsPerInstance)
								{
									break;
								}

								connections.Remove(connection);
								mappingData.OpenWorldConnectionScenes.Remove(connection);
								snapshotRuntimeData?.WaitingQueueEnteredUtcByClientId.Remove(connection.ClientId);

								if (!IsValidConnection(connection, out string accountName))
								{
									continue;
								}

								validConnections.Add((connection, accountName));
								currentCount++;
							}
						}
						finally
						{
							snapshotTcs.TrySetResult(true);
						}
					});

					// Wait until the main thread has actually processed the snapshot
					await snapshotTcs.Task;

					// Now update the DB and broadcast for each valid connection
					foreach (var (conn, accountName) in validConnections)
					{
						// Update the character's scene handle in the database
						var fetchResult = await charService.FetchByAccountAsync(accountName, selected: true);
						if (fetchResult.IsSuccess && fetchResult.Data.HasValue && fetchResult.Data.Value.ID > 0)
						{
							await charService.UpdateSceneAsync(fetchResult.Data.Value.ID, sceneName, loadedScene.SceneHandle);
						}

						// Tell the client to connect to the scene
						EnqueueMainThread(() =>
						{
							Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
							{
								Address = sceneServer.Address,
								Port = sceneServer.Port,
							});
						});
					}
				}
			}

			// Check if we still have some players that are waiting for a scene
			EnqueueMainThread(() =>
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
			});

			// Also check if we need to enqueue a new scene load request
			// We need to check the waiting count on the main thread
			bool needsNewScene = false;
			var checkTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			EnqueueMainThread(() =>
			{
				try
				{
					if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData) &&
						mappingData.WaitingOpenWorldConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections) &&
						connections != null &&
						connections.Count > 0)
					{
						needsNewScene = true;
					}
				}
				finally
				{
					checkTcs.TrySetResult(true);
				}
			});
			await checkTcs.Task;

			if (needsNewScene)
			{
				await sceneService.EnqueueAsync(worldServerID, sceneName, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.OpenWorld);
			}
		}

		/// <summary>
		/// Tries to process an Instance scene for the connection character otherwise falls back to the world scene.
		/// </summary>
		/// <param name="conn">Network connection to process.</param>
		/// <param name="skipDebounce">When true, skips per-account debounce check because the caller already reserved the lookup window.</param>
		private async Task ProcessInstanceConnectionAsync(NetworkConnection conn, bool skipDebounce = false)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var charService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
			{
				return;
			}

			// Validate the connection on main thread
			string accountName = null;
			var validateTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			EnqueueMainThread(() =>
			{
				try
				{
					if (!IsValidConnection(conn, out string acct))
					{
						Kick(conn, "Failed to get account name");
					}
					else
					{
						accountName = acct;
					}
				}
				finally
				{
					validateTcs.TrySetResult(true);
				}
			});
			await validateTcs.Task;

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
				EnqueueMainThread(() => Kick(conn, "invalid character ID"));
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
				// Clear instance flag
				characterFlags.DisableBit(CharacterFlags.IsInInstance);
				var updatedChar = new CharacterData(
					id: charData.ID,
					name: charData.Name,
					nameLowercase: charData.NameLowercase,
					account: charData.Account,
					selected: charData.Selected,
					worldServerID: charData.WorldServerID,
					sceneName: charData.SceneName,
					sceneHandle: charData.SceneHandle,
					bindScene: charData.BindScene,
					bindX: charData.BindX,
					bindY: charData.BindY,
					bindZ: charData.BindZ,
					instanceID: charData.InstanceID,
					instanceX: charData.InstanceX,
					instanceY: charData.InstanceY,
					instanceZ: charData.InstanceZ,
					instanceRotX: charData.InstanceRotX,
					instanceRotY: charData.InstanceRotY,
					instanceRotZ: charData.InstanceRotZ,
					instanceRotW: charData.InstanceRotW,
					raceID: charData.RaceID,
					modelIndex: charData.ModelIndex,
					x: charData.X,
					y: charData.Y,
					z: charData.Z,
					rotX: charData.RotX,
					rotY: charData.RotY,
					rotZ: charData.RotZ,
					rotW: charData.RotW,
					accessLevel: charData.AccessLevel,
					online: charData.Online,
					flags: characterFlags,
					version: charData.Version + 1,
					timeCreated: charData.TimeCreated,
					lastSaved: DateTime.UtcNow
				);
				await charService.PersistAsync(updatedChar);

				await FallbackToWorldSceneAsync(conn, accountName);
				return;
			}

			var sceneResult = await sceneService.FetchAsync(instanceID);
			if (!sceneResult.IsSuccess)
			{
				// Clear instance flag
				characterFlags.DisableBit(CharacterFlags.IsInInstance);
				var updatedChar = new CharacterData(
					id: charData.ID,
					name: charData.Name,
					nameLowercase: charData.NameLowercase,
					account: charData.Account,
					selected: charData.Selected,
					worldServerID: charData.WorldServerID,
					sceneName: charData.SceneName,
					sceneHandle: charData.SceneHandle,
					bindScene: charData.BindScene,
					bindX: charData.BindX,
					bindY: charData.BindY,
					bindZ: charData.BindZ,
					instanceID: charData.InstanceID,
					instanceX: charData.InstanceX,
					instanceY: charData.InstanceY,
					instanceZ: charData.InstanceZ,
					instanceRotX: charData.InstanceRotX,
					instanceRotY: charData.InstanceRotY,
					instanceRotZ: charData.InstanceRotZ,
					instanceRotW: charData.InstanceRotW,
					raceID: charData.RaceID,
					modelIndex: charData.ModelIndex,
					x: charData.X,
					y: charData.Y,
					z: charData.Z,
					rotX: charData.RotX,
					rotY: charData.RotY,
					rotZ: charData.RotZ,
					rotW: charData.RotW,
					accessLevel: charData.AccessLevel,
					online: charData.Online,
					flags: characterFlags,
					version: charData.Version + 1,
					timeCreated: charData.TimeCreated,
					lastSaved: DateTime.UtcNow
				);
				await charService.PersistAsync(updatedChar);

				await FallbackToWorldSceneAsync(conn, accountName);
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
					EnqueueMainThread(() =>
					{
						Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					});
				}
				else
				{
					// Delete the Scene entry
					await sceneService.DeleteByHandleAsync(sceneData.SceneServerID, sceneData.SceneHandle);
				}
			}
			else if (sceneStatus == FishMMO.Shared.SceneStatus.Pending ||
					 sceneStatus == FishMMO.Shared.SceneStatus.Loading)
			{
				EnqueueMainThread(() =>
				{
					if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
					{
						AddToQueue(conn, sceneData.ID, mappingData.WaitingInstanceConnections, mappingData.InstanceConnectionScenes);
					}
				});
			}
		}

		/// <summary>
		/// Updates the total connection count by summing waiting and active connections across all scenes.
		/// </summary>
		private async Task UpdateConnectionCountAsync()
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				return;
			}

			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) ? worldData.ID : 0;
			var scenesResult = await sceneService.FetchManyAsync(worldServerID);
			int sceneCharacterCount = 0;
			if (scenesResult.IsSuccess && scenesResult.Data != null)
			{
				sceneCharacterCount = scenesResult.Data.Sum(scene => scene.CharacterCount);
			}

			EnqueueMainThread(() =>
			{
				if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					return;
				}

				int waitingOpenWorldCount = mappingData.WaitingOpenWorldConnections?.Sum(kvp => kvp.Value.Count) ?? 0;
				int waitingInstanceCount = mappingData.WaitingInstanceConnections?.Sum(kvp => kvp.Value.Count) ?? 0;
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
			if (conn != null && Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.WaitingQueueEnteredUtcByClientId[conn.ClientId] = DateTime.UtcNow;
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
			if (conn != null && Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.WaitingQueueEnteredUtcByClientId.Remove(conn.ClientId);
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
		private void CleanupExpiredDebounceEntries()
		{
			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			runtimeData.InstanceLookupDebounce.SweepExpired(
				DateTime.UtcNow,
				debounceCleanupMaxScanPerSweep,
				debounceCleanupMaxRemovalsPerSweep);
		}

		/// <summary>
		/// Purges stale waiting-queue connections by TTL and removes inactive entries.
		/// Active connections that exceed TTL are kicked after queue removal.
		/// </summary>
		private void PurgeExpiredWaitingConnections()
		{
			if (waitingQueuePurgeMaxPerSweep <= 0)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			var staleConnections = new List<NetworkConnection>(waitingQueuePurgeMaxPerSweep);

			CollectStaleQueuedConnections(mappingData.OpenWorldConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep);
			if (staleConnections.Count < waitingQueuePurgeMaxPerSweep)
			{
				CollectStaleQueuedConnections(mappingData.InstanceConnectionScenes.Keys, staleConnections, now, waitingQueuePurgeMaxPerSweep - staleConnections.Count);
			}

			if (staleConnections.Count == 0)
			{
				return;
			}

			foreach (NetworkConnection conn in staleConnections.Distinct())
			{
				if (conn == null)
				{
					continue;
				}

				bool shouldKick = false;
				if (conn != null && conn.IsActive &&
					runtimeData.WaitingQueueEnteredUtcByClientId.TryGetValue(conn.ClientId, out DateTime queuedAt))
				{
					shouldKick = (now - queuedAt).TotalSeconds >= waitingQueueTtlSeconds;
				}

				RemoveFromQueue(conn, mappingData.OpenWorldConnectionScenes, mappingData.WaitingOpenWorldConnections);
				RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);

				if (conn != null && conn.IsActive && shouldKick)
				{
					Kick(conn, "Waiting queue TTL exceeded");
				}
			}
		}

		/// <summary>
		/// Collects stale queued connections from a source set based on activity and queue age.
		/// </summary>
		/// <param name="source">Source connection set snapshot input.</param>
		/// <param name="staleConnections">Output list to append stale connections into.</param>
		/// <param name="now">Current UTC timestamp used for TTL comparisons.</param>
		private void CollectStaleQueuedConnections(IEnumerable<NetworkConnection> source,
			List<NetworkConnection> staleConnections,
			DateTime now,
			int maxToCollect)
		{
			if (source == null || staleConnections == null || maxToCollect <= 0)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<WorldSceneSystemRuntimeData>(out var runtimeData))
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
			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var charService))
			{
				return;
			}

			var fetchResult = await charService.FetchByAccountAsync(accountName, selected: true);
			if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
			{
				EnqueueMainThread(() => Kick(conn, "Failed to get selected scene"));
				return;
			}
			var selectedChar = fetchResult.Data.Value;
			if (selectedChar.ID <= 0 || string.IsNullOrEmpty(selectedChar.SceneName))
			{
				EnqueueMainThread(() => Kick(conn, "Failed to get selected scene"));
				return;
			}
			string sceneName = selectedChar.SceneName;

			EnqueueMainThread(() =>
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
		/// Gets the maximum number of clients allowed for a given scene, using cached details if available.
		/// </summary>
		/// <param name="sceneName">Name of the scene.</param>
		/// <returns>Maximum number of clients for the scene.</returns>
		public int GetMaxClients(string sceneName)
		{
			if (WorldSceneDetailsCache?.Scenes?.TryGetValue(sceneName, out var details) == true)
			{
				return Mathf.Clamp(details.MaxClients, 1, MAX_CLIENTS_PER_INSTANCE);
			}
			return MAX_CLIENTS_PER_INSTANCE;
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
	}
}