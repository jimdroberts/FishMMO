using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Server.Implementation;
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
		/// Maximum number of clients allowed per scene instance.
		/// </summary>
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		/// <summary>
		/// Prevents overlapping async queue processing cycles.
		/// </summary>
		private int _isProcessing;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache;

		/// <summary>
		/// Interval (in seconds) between wait queue updates.
		/// </summary>
		private float waitQueueRate = 2.0f;

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

			Log.Debug("WorldSceneSystem", $"Initialized (WaitQueueRate={waitQueueRate}s, MaxClientsPerInstance={MAX_CLIENTS_PER_INSTANCE})");
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
			DrainMainThreadQueue();

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
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<IWorldSceneSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Drain();
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
			// Drain queued main-thread actions from async operations
			DrainMainThreadQueue();

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (runtimeData.NextWaitQueueUpdate <= 0)
			{
				runtimeData.NextWaitQueueUpdate = waitQueueRate;

				if (Initialized &&
					Server.Database?.ServiceRegistry != null &&
					Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					// Snapshot the scene names and connections to process before going async
					List<string> openWorldSceneNames = mappingData.WaitingOpenWorldConnections.Keys.ToList();
					List<NetworkConnection> instanceConns = mappingData.InstanceConnectionScenes.Keys.ToList();

					if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 0)
					{
						if (!TryEnqueueAsyncWork(() => ProcessQueuesAsync(openWorldSceneNames, instanceConns)))
						{
							Interlocked.Exchange(ref _isProcessing, 0);
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
				Interlocked.Exchange(ref _isProcessing, 0);
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

							foreach (NetworkConnection connection in connections.ToList())
							{
								if (currentCount >= maxClientsPerInstance)
								{
									break;
								}

								connections.Remove(connection);
								mappingData.OpenWorldConnectionScenes.Remove(connection);

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
		private async Task ProcessInstanceConnectionAsync(NetworkConnection conn)
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
			if (!TryEnqueueAsyncWork(() => ProcessInstanceConnectionAsync(conn)))
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
			reverseMap[conn] = key;
			if (!queue.TryGetValue(key, out var set))
			{
				queue[key] = set = new HashSet<NetworkConnection>();
			}
			set.Add(conn);
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
