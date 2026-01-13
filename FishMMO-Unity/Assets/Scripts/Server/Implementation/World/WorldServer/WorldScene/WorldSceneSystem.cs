using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Server.Implementation.World.WorldServer;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Manages world scene connections, queues, and scene assignment for players in the MMO server.
	/// Handles open world and instanced scene logic, connection authentication, and database updates.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldSceneSystem", menuName = "FishMMO/Server/WorldServer/World Scene System", order = 1)]
	public class WorldSceneSystem : ServerBehaviour, IWorldSceneSystem
	{
		/// <summary>
		/// Maximum number of clients allowed per scene instance.
		/// </summary>
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		/// <summary>
		/// Reference to the world server authenticator for login/authentication events.
		/// </summary>
		private WorldServerAuthenticator loginAuthenticator;

		/// <summary>
		/// Cache of world scene details, including max clients per scene.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache;

		/// <summary>
		/// Interval (in seconds) between wait queue updates.
		/// </summary>
		private float waitQueueRate = 2.0f;
		/// <summary>
		/// Time remaining until the next wait queue update.
		/// </summary>
		private float nextWaitQueueUpdate = 0.0f;

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

			loginAuthenticator = FindFirstObjectByType<WorldServerAuthenticator>();
			if (loginAuthenticator == null)
			{
				Log.Error("WorldSceneSystem", "Failed to initialize: WorldServerAuthenticator not found");
				throw new UnityException("WorldServerAuthenticator not found!");
			}

			// Connection state events
			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

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

			// Connection state events
			ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

			// Authentication events
			loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

			// Delete world scene data from database
			if (Server.CoreServer.NpgsqlDbContextFactory != null &&
				Server.DataContainerRegistry.TryGet<IWorldServerRuntimeData>(out var worldData))
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				if (dbContext != null)
				{
					Log.Debug("WorldSceneSystem", $"Deinitializing: Deleting world scenes (WorldServerID={worldData.ID})");
					SceneService.WorldDelete(dbContext, worldData.ID);
				}
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
			if (nextWaitQueueUpdate <= 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				if (Initialized && Server.CoreServer.NpgsqlDbContextFactory != null &&
					Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
				{
					using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
					foreach (string sceneName in mappingData.WaitingOpenWorldConnections.Keys.ToList())
					{
						ProcessOpenWorldQueue(dbContext, sceneName);
					}
					foreach (NetworkConnection conn in mappingData.InstanceConnectionScenes.Keys.ToList())
					{
						ProcessInstanceConnection(dbContext, conn);
					}

					UpdateConnectionCount(dbContext);
				}
			}
			nextWaitQueueUpdate -= deltaTime;
		}

		/// <summary>
		/// Processes the queue for open world scenes, assigning connections to available scenes or enqueuing new scene requests.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		/// <param name="sceneName">Name of the scene to process.</param>
		private void ProcessOpenWorldQueue(NpgsqlDbContext dbContext, string sceneName)
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
				return;
			}

			int maxClientsPerInstance = GetMaxClients(sceneName);

			// Try and get an existing scene
			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerRuntimeData>(out var worldData) ? worldData.ID : 0;
			List<SceneEntity> loadedScenes = SceneService.GetServerList(dbContext, worldServerID, sceneName, maxClientsPerInstance);
			if (loadedScenes?.Count() > 0)
			{
				foreach (SceneEntity loadedScene in loadedScenes)
				{
					SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, loadedScene.SceneServerID);
					if (sceneServer == null)
					{
						continue;
					}

					foreach (NetworkConnection connection in connections.ToList())
					{
						// If we are at maximum capacity on this server move to the next one
						if (loadedScene.CharacterCount >= maxClientsPerInstance)
						{
							break;
						}

						// Clear the connection from our wait queues
						connections.Remove(connection);
						mappingData.OpenWorldConnectionScenes.Remove(connection);

						if (!IsValidConnection(connection, out string accountName))
						{
							continue;
						}

						// Successfully found a scene to connect to
						CharacterService.SetSceneHandle(dbContext, accountName, loadedScene.SceneHandle);

						// Tell the client to connect to the scene
						Server.NetworkWrapper.Broadcast(connection, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					}
				}
			}

			// Check if we still have some players that are waiting for a scene
			if (connections.Count == 0)
			{
				mappingData.WaitingOpenWorldConnections.Remove(sceneName);
			}
			else
			{
				// Enqueue a new pending scene load request to the database if one doesn't already exist.
				SceneService.Enqueue(dbContext, worldServerID, sceneName, SceneType.OpenWorld, out long sceneID);
			}
		}

		/// <summary>
		/// Tries to process an Instance scene for the connection character otherwise falls back to the world scene.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		/// <param name="conn">Network connection to process.</param>
		private void ProcessInstanceConnection(NpgsqlDbContext dbContext, NetworkConnection conn)
		{
			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			// Get the scene for the selected character
			if (!IsValidConnection(conn, out string accountName))
			{
				Kick(conn, "Failed to get account name");
				return;
			}

			if (!CharacterService.TryGetSelectedCharacterID(dbContext, accountName, out long characterID))
			{
				Kick(conn, "invalid character ID");
				return;
			}

			if (!CharacterService.GetCharacterFlags(dbContext, characterID, out int characterFlags))
			{
				Kick(conn, "invalid character flags");
				return;
			}

			if (!characterFlags.IsFlagged(CharacterFlags.IsInInstance))
			{
				FallbackToWorldScene(dbContext, conn, accountName, mappingData);
				return;
			}

			SceneEntity sceneEntity;

			// Check if the selected character has an instance available.
			if (CharacterService.GetInstanceID(dbContext, characterID, out long instanceID) &&
				(sceneEntity = SceneService.GetInstanceByID(dbContext, instanceID)) != null)
			{
				SceneStatus sceneStatus = (SceneStatus)sceneEntity.SceneStatus;
				if (sceneStatus == SceneStatus.Ready)
				{
					// Ensure the Scene Server is running, if not the character will be returned to the world scene.
					SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, sceneEntity.SceneServerID);
					if (sceneServer != null)
					{
						// Tell the client to connect to the scene
						Server.NetworkWrapper.Broadcast(conn, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					}
					else
					{
						// Delete the Scene entry
						SceneService.Delete(dbContext, sceneEntity.SceneServerID, sceneEntity.SceneHandle);
					}
				}
				else if (sceneStatus == SceneStatus.Pending ||
						 sceneStatus == SceneStatus.Loading)
				{
					AddToQueue(conn, sceneEntity.ID, mappingData.WaitingInstanceConnections, mappingData.InstanceConnectionScenes);
				}
			}
			else
			{
				// Clear instance flag
				characterFlags.DisableBit(CharacterFlags.IsInInstance);
				CharacterService.SetCharacterFlags(dbContext, characterID, characterFlags);

				FallbackToWorldScene(dbContext, conn, accountName, mappingData);
			}
		}

		/// <summary>
		/// Updates the total connection count by summing waiting and active connections across all scenes.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		private void UpdateConnectionCount(NpgsqlDbContext dbContext)
		{
			if (dbContext == null ||
				!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			// Get the scene data from each of our worlds scenes
			long worldServerID = Server.DataContainerRegistry.TryGet<IWorldServerRuntimeData>(out var worldData) ? worldData.ID : 0;
			List<SceneEntity> sceneServerCount = SceneService.GetServerList(dbContext, worldServerID);
			int waitingOpenWorldCount = mappingData.WaitingOpenWorldConnections?.Sum(kvp => kvp.Value.Count) ?? 0;
			int waitingInstanceCount = mappingData.WaitingInstanceConnections?.Sum(kvp => kvp.Value.Count) ?? 0;
			int totalCount = waitingOpenWorldCount + waitingInstanceCount + sceneServerCount.Sum(scene => scene.CharacterCount);

			mappingData.ConnectionCount = totalCount;
		}
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

			using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Kick(conn, "Failed to access database context or world server system");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var mappingData))
			{
				Kick(conn, "Failed to get world scene mapping data");
				return;
			}

			// Try to process the Instance otherwise it will fallback to world.
			ProcessInstanceConnection(dbContext, conn);
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
		/// <param name="dbContext">Database context.</param>
		/// <param name="conn">Network connection.</param>
		/// <param name="accountName">Account name for the connection.</param>
		/// <param name="mappingData">World scene mapping data.</param>
		private void FallbackToWorldScene(NpgsqlDbContext dbContext, NetworkConnection conn, string accountName, IWorldSceneMappingData<NetworkConnection> mappingData)
		{
			// Fallback to the world scene
			if (!CharacterService.TryGetSelectedSceneName(dbContext, accountName, out string sceneName))
			{
				Kick(conn, "Failed to get selected scene");
				return;
			}
			RemoveFromQueue(conn, mappingData.InstanceConnectionScenes, mappingData.WaitingInstanceConnections);
			AddToQueue(conn, sceneName, mappingData.WaitingOpenWorldConnections, mappingData.OpenWorldConnectionScenes);
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
	}
}