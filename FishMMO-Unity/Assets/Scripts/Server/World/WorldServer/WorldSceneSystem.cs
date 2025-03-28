using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using System.Linq;

namespace FishMMO.Server
{
	// World scene system handles the node services
	public class WorldSceneSystem : ServerBehaviour
	{
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		private WorldServerAuthenticator loginAuthenticator;

		public WorldSceneDetailsCache WorldSceneDetailsCache;

		// sceneName, waitingConnections
		/// <summary>
		/// Connections waiting for a scene to finish loading.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingConnections = new Dictionary<string, HashSet<NetworkConnection>>();
		public Dictionary<NetworkConnection, string> WaitingConnectionsScenes = new Dictionary<NetworkConnection, string>();

		public int ConnectionCount { get; private set; }

		private float waitQueueRate = 2.0f;
		private float nextWaitQueueUpdate = 0.0f;

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

		public override void Destroying()
		{
			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;

				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}

			if (Server != null &&
				Server.NpgsqlDbContextFactory != null &&
				ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
			{
				using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
				SceneService.WorldDelete(dbContext, worldServerSystem.ID);
			}
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				if (WaitingConnectionsScenes.TryGetValue(conn, out string sceneName))
				{
					if (WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
					{
						connections.Remove(conn);
						if (connections.Count < 1)
						{
							WaitingConnections.Remove(sceneName);
						}
					}
					WaitingConnectionsScenes.Remove(conn);
				}
			}
		}

		private void LateUpdate()
		{
			if (nextWaitQueueUpdate < 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				if (Server != null && Server.NpgsqlDbContextFactory != null)
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
					foreach (string sceneName in new List<string>(WaitingConnections.Keys))
					{
						TryClearWaitQueues(dbContext, sceneName);
					}

					UpdateConnectionCount(dbContext);
				}
			}
			nextWaitQueueUpdate -= Time.deltaTime;
		}

		private void TryClearWaitQueues(NpgsqlDbContext dbContext, string sceneName)
		{
			if (!WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
			{
				return;
			}

			if (connections == null ||
				connections.Count < 1 ||
				!ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
			{
				WaitingConnections.Remove(sceneName);
				return;
			}

			int maxClientsPerInstance = MAX_CLIENTS_PER_INSTANCE;

			// See if we can get a per scene max client count
			if (WorldSceneDetailsCache != null &&
				WorldSceneDetailsCache.Scenes != null &&
				WorldSceneDetailsCache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
			{
				maxClientsPerInstance = details.MaxClients;
			}

			// Clamp at 1 to MAX_CLIENTS_PER_INSTANCE
			maxClientsPerInstance = maxClientsPerInstance.Clamp(1, MAX_CLIENTS_PER_INSTANCE);

			// Try and get an existing scene
			List<SceneEntity> loadedScenes = SceneService.GetServerList(dbContext, worldServerSystem.ID, sceneName, maxClientsPerInstance);
			if (loadedScenes != null && loadedScenes.Count() > 0)
			{
				foreach (SceneEntity loadedScene in loadedScenes)
				{
					if (connections.Count < 1)
					{
						break;
					}

					SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, loadedScene.SceneServerID);
					if (sceneServer == null)
					{
						continue;
					}

					foreach (NetworkConnection connection in new List<NetworkConnection>(connections))
					{
						// If we are at maximum capacity on this server move to the next one
						if (loadedScene.CharacterCount >= maxClientsPerInstance)
						{
							break;
						}

						// Clear the connection from our wait queues
						connections.Remove(connection);
						WaitingConnectionsScenes.Remove(connection);

						// If the connection is no longer active
						if (connection == null || !connection.IsActive || !AccountManager.GetAccountNameByConnection(connection, out string accountName))
						{
							continue;
						}

						// Successfully found a scene to connect to
						CharacterService.SetSceneHandle(dbContext, accountName, loadedScene.SceneHandle);

						// Tell the client to connect to the scene
						Server.Broadcast(connection, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					}
				}
			}

			// Check if we still have some players that are waiting for a scene
			if (connections.Count < 1)
			{
				WaitingConnections.Remove(sceneName);
			}
			// Enqueue a new pending scene load request to the database, we need a new scene
			else if (!SceneService.PendingExists(dbContext, worldServerSystem.ID, sceneName))
			{
				Debug.Log("World Scene System: Enqueing new PendingSceneLoadRequest: " + worldServerSystem.ID + ":" + sceneName);
				SceneService.Enqueue(dbContext, worldServerSystem.ID, sceneName, SceneType.OpenWorld);
			}
		}

		private void UpdateConnectionCount(NpgsqlDbContext dbContext)
		{
			if (dbContext == null || !ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
			{
				return;
			}

			// Get the scene data from each of our worlds scenes
			IQueryable<SceneEntity> sceneServerCount = SceneService.GetServerList(dbContext, worldServerSystem.ID);
			if (sceneServerCount != null)
			{
				// count the total
				ConnectionCount = WaitingConnections != null ? WaitingConnections.Count : 0;
				foreach (SceneEntity scene in sceneServerCount)
				{
					ConnectionCount += scene.CharacterCount;
				}
			}
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
			{
				return;
			}

			// Get the scene for the selected character
			if (!AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				Debug.Log("World Scene System: " + conn.ClientId + " failed to get account name.");
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();

			if (dbContext == null || !ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
			{
				Debug.Log("World Scene System: " + conn.ClientId + " failed to access database context or world server system.");
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			// Check if the selected character has an instance
			if (CharacterService.TryGetSelectedDetails(dbContext, accountName, out long characterID))
			{
				SceneEntity sceneEntity = SceneService.GetCharacterInstance(dbContext, characterID, worldServerSystem.ID);
				if (sceneEntity != null)
				{

				}
			}

			if (!CharacterService.TryGetSelectedSceneName(dbContext, accountName, out string sceneName))
			{
				Debug.Log("World Scene System: " + conn.ClientId + " failed to get selected scene.");
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			// Add the client to the wait queue, when a scene server loads the scene the client will be connected when it's ready
			WaitingConnectionsScenes[conn] = sceneName;
			if (!WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
			{
				WaitingConnections.Add(sceneName, connections = new HashSet<NetworkConnection>());
			}
			if (!connections.Contains(conn))
			{
				connections.Add(conn);
			}
		}
	}
}