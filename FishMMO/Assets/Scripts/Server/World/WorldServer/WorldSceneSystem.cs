using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System.Collections.Generic;
using FishMMO.Server.Services;
using FishMMO_DB;
using FishMMO_DB.Entities;
using UnityEngine;

namespace FishMMO.Server
{
	// World scene system handles the node services
	public class WorldSceneSystem : ServerBehaviour
	{
		private const int MAX_CLIENTS_PER_INSTANCE = 100;

		private WorldServerAuthenticator loginAuthenticator;

		// sceneName, waitingConnections
		/// <summary>
		/// Connections waiting for a scene to finish loading.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingConnections = new Dictionary<string, HashSet<NetworkConnection>>();

		public int ConnectionCount
		{
			get
			{
				return WaitingConnections.Count;
			}
		}

		private float waitQueueRate = 2.0f;
		private float nextWaitQueueUpdate = 0.0f;

		public override void InitializeOnce()
		{
			loginAuthenticator = FindObjectOfType<WorldServerAuthenticator>();

			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			if (loginAuthenticator == null)
				return;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
			}
		}

		private void OnApplicationQuit()
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			PendingSceneService.Delete(dbContext, Server.WorldServerSystem.ID);
			dbContext.SaveChanges();
		}

		private void LateUpdate()
		{
			if (nextWaitQueueUpdate < 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				using var dbContext = Server.DbContextFactory.CreateDbContext();
				foreach (string sceneName in new List<string>(WaitingConnections.Keys))
				{
					TryClearWaitQueues(dbContext, sceneName);
				}
				dbContext.SaveChanges();
			}
			nextWaitQueueUpdate -= Time.deltaTime;
		}

		private void TryClearWaitQueues(ServerDbContext dbContext, string sceneName)
		{
			Dictionary<string, LoadedSceneEntity> loadedScenes = LoadedSceneService.GetServerList(dbContext, Server.WorldServerSystem.ID, sceneName, MAX_CLIENTS_PER_INSTANCE);
			if (loadedScenes == null || loadedScenes.Count < 1)
			{
				return;
			}

			if (WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
			{
				foreach (LoadedSceneEntity loadedScene in loadedScenes.Values)
				{
					SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, loadedScene.SceneServerID);
					if (sceneServer == null)
					{
						continue;
					}

					int clientCount = loadedScene.CharacterCount;
					foreach (NetworkConnection connection in new List<NetworkConnection>(connections))
					{
						// if we are at maximum capacity on this server move to the next one
						if (clientCount >= MAX_CLIENTS_PER_INSTANCE)
						{
							continue;
						}
						connections.Remove(connection);
						if (connection == null || !connection.IsActive || !AccountManager.GetAccountNameByConnection(connection, out string accountName))
						{
							continue;
						}

						// successfully found a scene to connect to
						CharacterService.SetSceneHandle(dbContext, accountName, loadedScene.SceneHandle);
						dbContext.SaveChanges();

						// tell the client to connect to the scene
						connection.Broadcast(new WorldSceneConnectBroadcast()
						{
							address = sceneServer.Address,
							port = sceneServer.Port,
						});
					}
				}

				if (connections.Count < 1)
				{
					WaitingConnections.Remove(sceneName);
				}
				else
				{
					Debug.Log("Enqueing new PendingSceneLoadRequest: " + Server.WorldServerSystem.ID + ":" + sceneName);
					// try again to enqueue the pending scene load to the database
					PendingSceneService.Enqueue(dbContext, Server.WorldServerSystem.ID, sceneName);
				}
			}
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			Debug.Log(conn.ClientId + " authentiated: " + authenticated);

			if (!authenticated)
				return;

			// if we are authenticated try and connect the client to a scene server
			TryConnectToSceneServer(conn);
		}

		/// <summary>
		/// Try to connect the client to a Scene Server.
		/// </summary>
		private void TryConnectToSceneServer(NetworkConnection conn)
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			// get the scene for the selected character
			if (!AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!CharacterService.TryGetSelectedSceneName(dbContext, accountName, out string sceneName))
			{
				Debug.Log(conn.ClientId + " failed to get selected scene or account name.");
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			Dictionary<string, LoadedSceneEntity> loadedScenes = LoadedSceneService.GetServerList(dbContext, Server.WorldServerSystem.ID, sceneName, MAX_CLIENTS_PER_INSTANCE);

			LoadedSceneEntity selectedScene = null;
			if (loadedScenes != null && loadedScenes.Count > 0)
			{
				Debug.Log("Scene Instance found: " + sceneName);
				foreach (LoadedSceneEntity sceneEntity in loadedScenes.Values)
				{
					selectedScene = sceneEntity;
				}
			}
			
			// if we found a valid scene server
			if (selectedScene != null)
			{
				Debug.Log("Scene found: " + sceneName);
				SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, selectedScene.SceneServerID);

				// successfully found a scene to connect to
				CharacterService.SetSceneHandle(dbContext, accountName, selectedScene.SceneHandle);
				dbContext.SaveChanges();

				// tell the character to reconnect to the scene server
				conn.Broadcast(new WorldSceneConnectBroadcast()
				{
					address = sceneServer.Address,
					port = sceneServer.Port,
				});
			}
			else
			{
				Debug.Log("Scene not found: " + sceneName + " adding " + conn.ClientId + " to wait queue and adding pending scene.");
				// add the client to the wait queue, when a scene server loads the scene the client will be connected when it's ready
				if (!WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
				{
					WaitingConnections.Add(sceneName, connections = new HashSet<NetworkConnection>());
				}
				if (!connections.Contains(conn))
				{
					connections.Add(conn);
				}

				// enqueue the pending scene load to the database
				Debug.Log("Enqueing new PendingSceneLoadRequest: " + Server.WorldServerSystem.ID + ":" + sceneName);
				PendingSceneService.Enqueue(dbContext, Server.WorldServerSystem.ID, sceneName);
				dbContext.SaveChanges();
			}
		}
	}
}