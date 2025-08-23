using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Server.Implementation.WorldServer
{
	/// <summary>
	/// Manages world scene connections, queues, and scene assignment for players in the MMO server.
	/// Handles open world and instanced scene logic, connection authentication, and database updates.
	/// </summary>
	public class WorldSceneSystem : ServerBehaviour
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
		/// Connections waiting for a scene to finish loading, mapped by scene name.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingOpenWorldConnections = new Dictionary<string, HashSet<NetworkConnection>>();
		/// <summary>
		/// Maps connections to the open world scene they are waiting for.
		/// </summary>
		public Dictionary<NetworkConnection, string> OpenWorldConnectionScenes = new Dictionary<NetworkConnection, string>();

		/// <summary>
		/// Connections waiting for an instanced scene to finish loading, mapped by instance ID.
		/// </summary>
		public Dictionary<long, HashSet<NetworkConnection>> WaitingInstanceConnections = new Dictionary<long, HashSet<NetworkConnection>>();
		/// <summary>
		/// Maps connections to the instance scene they are waiting for.
		/// </summary>
		public Dictionary<NetworkConnection, long> InstanceConnectionScenes = new Dictionary<NetworkConnection, long>();

		/// <summary>
		/// Total number of connections managed by this system (waiting + active).
		/// </summary>
		public int ConnectionCount { get; private set; }

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
		public override void InitializeOnce()
		{
			loginAuthenticator = FindFirstObjectByType<WorldServerAuthenticator>();
			if (loginAuthenticator == null)
			{
				throw new UnityException("WorldServerAuthenticator not found!");
			}

			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			}
			else
			{
				enabled = false;
			}
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from events and deletes world scene data from the database.
		/// </summary>
		public override void Destroying()
		{
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			if (Server != null &&
				Server.CoreServer.NpgsqlDbContextFactory != null &&
				Server.BehaviourRegistry.TryGet(out WorldServerSystem worldServerSystem))
			{
				using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
				SceneService.WorldDelete(dbContext, worldServerSystem.ID);
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

			RemoveFromQueue(conn, OpenWorldConnectionScenes, WaitingOpenWorldConnections);
			RemoveFromQueue(conn, InstanceConnectionScenes, WaitingInstanceConnections);
		}

		/// <summary>
		/// Unity LateUpdate callback. Periodically processes open world and instance queues, and updates connection count.
		/// </summary>
		private void LateUpdate()
		{
			if (nextWaitQueueUpdate <= 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				if (Server.CoreServer.NpgsqlDbContextFactory != null)
				{
					using var dbContext = Server.CoreServer.NpgsqlDbContextFactory.CreateDbContext();
					foreach (string sceneName in WaitingOpenWorldConnections.Keys.ToList())
					{
						ProcessOpenWorldQueue(dbContext, sceneName);
					}
					foreach (NetworkConnection conn in InstanceConnectionScenes.Keys.ToList())
					{
						ProcessInstanceConnection(dbContext, conn);
					}

					UpdateConnectionCount(dbContext);
				}
			}
			nextWaitQueueUpdate -= Time.deltaTime;
		}

		/// <summary>
		/// Processes the queue for open world scenes, assigning connections to available scenes or enqueuing new scene requests.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		/// <param name="sceneName">Name of the scene to process.</param>
		private void ProcessOpenWorldQueue(NpgsqlDbContext dbContext, string sceneName)
		{
			if (!WaitingOpenWorldConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections) ||
				connections == null ||
				connections.Count == 0 ||
				!Server.BehaviourRegistry.TryGet(out WorldServerSystem worldServerSystem))
			{
				WaitingOpenWorldConnections.Remove(sceneName);
				return;
			}

			int maxClientsPerInstance = GetMaxClients(sceneName);

			// Try and get an existing scene
			List<SceneEntity> loadedScenes = SceneService.GetServerList(dbContext, worldServerSystem.ID, sceneName, maxClientsPerInstance);
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
						OpenWorldConnectionScenes.Remove(connection);

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
				WaitingOpenWorldConnections.Remove(sceneName);
			}
			else
			{
				// Enqueue a new pending scene load request to the database if one doesn't already exist.
				SceneService.Enqueue(dbContext, worldServerSystem.ID, sceneName, SceneType.OpenWorld, out long sceneID);
			}
		}

		/// <summary>
		/// Tries to process an Instance scene for the connection character otherwise falls back to the world scene.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		/// <param name="conn">Network connection to process.</param>
		private void ProcessInstanceConnection(NpgsqlDbContext dbContext, NetworkConnection conn)
		{
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
				FallbackToWorldScene(dbContext, conn, accountName);
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
					AddToQueue(conn, sceneEntity.ID, WaitingInstanceConnections, InstanceConnectionScenes);
				}
			}
			else
			{
				// Clear instance flag
				characterFlags.DisableBit(CharacterFlags.IsInInstance);
				CharacterService.SetCharacterFlags(dbContext, characterID, characterFlags);

				FallbackToWorldScene(dbContext, conn, accountName);
			}
		}

		/// <summary>
		/// Updates the total connection count by summing waiting and active connections across all scenes.
		/// </summary>
		/// <param name="dbContext">Database context.</param>
		private void UpdateConnectionCount(NpgsqlDbContext dbContext)
		{
			if (dbContext == null || !Server.BehaviourRegistry.TryGet(out WorldServerSystem worldServerSystem))
			{
				return;
			}

			// Get the scene data from each of our worlds scenes
			List<SceneEntity> sceneServerCount = SceneService.GetServerList(dbContext, worldServerSystem.ID);
			if (sceneServerCount != null)
			{
				// Count the total connections
				ConnectionCount = (WaitingOpenWorldConnections?.Sum(kvp => kvp.Value.Count) ?? 0) +
								  (WaitingInstanceConnections?.Sum(kvp => kvp.Value.Count) ?? 0) +
								  sceneServerCount.Sum(scene => scene.CharacterCount);
			}
		}

		/// <summary>
		/// Handles client authentication results. Processes instance connection or falls back to world scene.
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
		private void FallbackToWorldScene(NpgsqlDbContext dbContext, NetworkConnection conn, string accountName)
		{
			// Fallback to the world scene
			if (!CharacterService.TryGetSelectedSceneName(dbContext, accountName, out string sceneName))
			{
				Kick(conn, "Failed to get selected scene");
				return;
			}
			RemoveFromQueue(conn, InstanceConnectionScenes, WaitingInstanceConnections);
			AddToQueue(conn, sceneName, WaitingOpenWorldConnections, OpenWorldConnectionScenes);
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
		private int GetMaxClients(string sceneName)
		{
			if (WorldSceneDetailsCache?.Scenes?.TryGetValue(sceneName, out var details) == true)
			{
				return Mathf.Clamp(details.MaxClients, 1, MAX_CLIENTS_PER_INSTANCE);
			}
			return MAX_CLIENTS_PER_INSTANCE;
		}
	}
}