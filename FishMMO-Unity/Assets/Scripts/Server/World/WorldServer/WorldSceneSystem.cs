using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Server
{
	// World scene system handles the node services
	public class WorldSceneSystem : ServerBehaviour
	{
		private const int MAX_CLIENTS_PER_INSTANCE = 500;

		private WorldServerAuthenticator loginAuthenticator;

		public WorldSceneDetailsCache WorldSceneDetailsCache;

		/// <summary>
		/// Connections waiting for a scene to finish loading.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingOpenWorldConnections = new Dictionary<string, HashSet<NetworkConnection>>();
		public Dictionary<NetworkConnection, string> OpenWorldConnectionScenes = new Dictionary<NetworkConnection, string>();

		/// <summary>
		/// Connections waiting for an instanced scene to finish loading.
		/// </summary>
		public Dictionary<long, HashSet<NetworkConnection>> WaitingInstanceConnections = new Dictionary<long, HashSet<NetworkConnection>>();
		public Dictionary<NetworkConnection, long> InstanceConnectionScenes = new Dictionary<NetworkConnection, long>();

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
			if (args.ConnectionState != RemoteConnectionState.Stopped)
			{
				return;
			}

			RemoveFromQueue(conn, OpenWorldConnectionScenes, WaitingOpenWorldConnections);
			RemoveFromQueue(conn, InstanceConnectionScenes, WaitingInstanceConnections);
		}

		private void LateUpdate()
		{
			if (nextWaitQueueUpdate <= 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				if (Server?.NpgsqlDbContextFactory != null)
				{
					using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
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

		private void ProcessOpenWorldQueue(NpgsqlDbContext dbContext, string sceneName)
		{
			if (!WaitingOpenWorldConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections) ||
				connections == null ||
				connections.Count == 0 ||
				!ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
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
						Server.Broadcast(connection, new WorldSceneConnectBroadcast()
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
				Kick(conn, "invalid character ID");
				return;
			}

			if (!characterFlags.IsFlagged(CharacterFlags.IsInInstance))
			{
				FallbackToWorldScene(dbContext, conn, accountName);
				return;
			}

			// Check if the selected character has a group instance.
			SceneEntity sceneEntity = SceneService.GetCharacterInstance(dbContext, characterID, SceneType.Group);
			if (sceneEntity != null)
			{
				SceneStatus sceneStatus = (SceneStatus)sceneEntity.SceneStatus;
				if (sceneStatus == SceneStatus.Ready)
				{
					// Ensure the Scene Server is running, if not the character will be returned to the world scene.
					SceneServerEntity sceneServer = SceneServerService.GetServer(dbContext, sceneEntity.SceneServerID);
					if (sceneServer != null)
					{
						// Successfully found a scene to connect to
						CharacterService.SetSceneHandle(dbContext, accountName, sceneEntity.SceneHandle);

						// Tell the client to connect to the scene
						Server.Broadcast(conn, new WorldSceneConnectBroadcast()
						{
							Address = sceneServer.Address,
							Port = sceneServer.Port,
						});
					}
					else
					{
						// Clear the characters Scene Instance and delete the Scene entry
						CharacterService.SetInstance(dbContext, characterID, 0, Vector3.zero);
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

		private void UpdateConnectionCount(NpgsqlDbContext dbContext)
		{
			if (dbContext == null || !ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem))
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

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
			{
				return;
			}

			// Get the scene for the selected character
			if (!AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				Kick(conn, "Failed to get account name");
				return;
			}

			using var dbContext = Server.NpgsqlDbContextFactory.CreateDbContext();
			if (dbContext == null)
			{
				Kick(conn, "Failed to access database context or world server system");
				return;
			}

			// Try to process the Instance otherwise it will fallback to world.
			ProcessInstanceConnection(dbContext, conn);
		}

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

		private bool IsValidConnection(NetworkConnection conn, out string accountName)
		{
			accountName = null;
			return conn != null && conn.IsActive && AccountManager.GetAccountNameByConnection(conn, out accountName);
		}

		private void Kick(NetworkConnection conn, string reason)
		{
			Debug.Log($"World Scene System: {conn.ClientId} {reason}.");
			conn.Kick(KickReason.UnexpectedProblem);
		}

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