using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Server.Services;
using UnityEngine;

namespace FishMMO.Server
{
	public class SceneWaitQueueData
	{
		public string address = "";
		public ushort port = 0;
		public bool ready = false;
	}

	// World scene system handles the node services
	public class WorldSceneSystem : ServerBehaviour
	{
		private const int MAX_CLIENTS_PER_INSTANCE = 100;

		private WorldServerAuthenticator loginAuthenticator;

		// sceneConnection, sceneDetails
		/// <summary>
		/// Active scene servers.
		/// </summary>
		public Dictionary<NetworkConnection, SceneServerDetails> sceneServers = new Dictionary<NetworkConnection, SceneServerDetails>();

		// characterName, sceneConnection
		/// <summary>
		/// Active client connections.
		/// </summary>
		public Dictionary<long, NetworkConnection> sceneCharacters = new Dictionary<long, NetworkConnection>();

		// sceneName, waitingConnections
		/// <summary>
		/// Connections waiting for a scene to finish loading.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> waitingConnections = new Dictionary<string, HashSet<NetworkConnection>>();

		// sceneName, waitingSceneServer
		/// <summary>
		/// Scenes that are waiting to be loaded fully.
		/// </summary>
		public Dictionary<string, SceneWaitQueueData> waitingScenes = new Dictionary<string, SceneWaitQueueData>();

		public int ConnectionCount
		{
			get
			{
				return waitingConnections.Count + sceneCharacters.Count;
			}
		}

		private float waitQueueRate = 2.0f;
		private float nextWaitQueueUpdate = 0.0f;

		public override void InitializeOnce()
		{
			if (ServerManager != null)
			{
				ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;
				ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			}
			else
			{
				enabled = false;
			}
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			loginAuthenticator = FindObjectOfType<WorldServerAuthenticator>();
			if (loginAuthenticator == null)
				return;

			if (args.ConnectionState == LocalConnectionState.Started)
			{
				loginAuthenticator.OnRelayAuthenticationResult += Authenticator_OnRelayServerAuthenticationResult;
				loginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

				ServerManager.RegisterBroadcast<ScenePulseBroadcast>(OnServerScenePulseBroadcastReceived, true);
				ServerManager.RegisterBroadcast<SceneServerDetailsBroadcast>(OnServerSceneServerDetailsBroadcast, true);
				ServerManager.RegisterBroadcast<SceneListBroadcast>(OnServerSceneListBroadcastReceived, true);
				ServerManager.RegisterBroadcast<SceneLoadBroadcast>(OnServerSceneLoadBroadcastReceived, true);
				ServerManager.RegisterBroadcast<SceneUnloadBroadcast>(OnServerSceneUnloadBroadcastReceived, true);
				ServerManager.RegisterBroadcast<SceneCharacterConnectedBroadcast>(OnServerSceneCharacterConnectedBroadcastReceived, true);
				ServerManager.RegisterBroadcast<SceneCharacterDisconnectedBroadcast>(OnServerSceneCharacterDisconnectedBroadcastReceived, true);
			}
			else if (args.ConnectionState == LocalConnectionState.Stopped)
			{
				loginAuthenticator.OnRelayAuthenticationResult -= Authenticator_OnRelayServerAuthenticationResult;
				loginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;

				ServerManager.UnregisterBroadcast<ScenePulseBroadcast>(OnServerScenePulseBroadcastReceived);
				ServerManager.UnregisterBroadcast<SceneServerDetailsBroadcast>(OnServerSceneServerDetailsBroadcast);
				ServerManager.UnregisterBroadcast<SceneListBroadcast>(OnServerSceneListBroadcastReceived);
				ServerManager.UnregisterBroadcast<SceneLoadBroadcast>(OnServerSceneLoadBroadcastReceived);
				ServerManager.UnregisterBroadcast<SceneUnloadBroadcast>(OnServerSceneUnloadBroadcastReceived);
				ServerManager.UnregisterBroadcast<SceneCharacterConnectedBroadcast>(OnServerSceneCharacterConnectedBroadcastReceived);
				ServerManager.UnregisterBroadcast<SceneCharacterDisconnectedBroadcast>(OnServerSceneCharacterDisconnectedBroadcastReceived);
			}
		}

		private void Update()
		{
			nextWaitQueueUpdate -= Time.deltaTime;
			if (nextWaitQueueUpdate < 0)
			{
				nextWaitQueueUpdate = waitQueueRate;

				foreach (string sceneName in new List<string>(waitingConnections.Keys))
				{
					TryClearWaitQueues(sceneName);
				}
			}
		}

		private void TryClearWaitQueues(string sceneName)
		{
			if (waitingScenes.TryGetValue(sceneName, out SceneWaitQueueData waitData) &&
				waitData.ready)
			{
				if (waitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
				{
					foreach (NetworkConnection conn in connections)
					{
						// tell the character to reconnect to the scene server
						conn.Broadcast(new WorldSceneConnectBroadcast()
						{
							address = waitData.address,
							port = waitData.port,
						});
					}

					connections.Clear();
					waitingConnections.Remove(sceneName);
				}

				waitingScenes.Remove(sceneName);
			}
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				sceneServers.Remove(conn);
			}
		}

		// add our node server to the relay
		private void Authenticator_OnRelayServerAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
				return;

			if (!sceneServers.ContainsKey(conn))
			{
				sceneServers.Add(conn, new SceneServerDetails()
				{
					locked = true,
				});
			}
		}

		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
				return;

			if (AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				SendWorldSceneConnectBroadcast(conn);
			}
		}

		public Dictionary<int, SceneInstanceDetails> RebuildSceneInstanceDetails(List<SceneInstanceDetails> sceneInstanceDetails)
		{
			Dictionary<int, SceneInstanceDetails> newDetails = new Dictionary<int, SceneInstanceDetails>();
			if (sceneInstanceDetails != null)
			{
				foreach (SceneInstanceDetails instance in sceneInstanceDetails)
				{
					newDetails.Add(instance.handle, instance);
				}
			}
			return newDetails;
		}

		/// <summary>
		/// The scene server sent a pulse, update last pulse time and character count
		/// </summary>
		private void OnServerScenePulseBroadcastReceived(NetworkConnection conn, ScenePulseBroadcast msg)
		{
			Debug.Log("[" + DateTime.UtcNow + "] Pulse Received from " + msg.name + ":" + conn.GetAddress());

			//set sceneservers last pulse time
			if (sceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.lastPulse = DateTime.UtcNow;
				if (details.scenes != null)
					details.scenes.Clear();
				details.scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
			}
		}

		/// <summary>
		/// The scene server sent its details
		/// </summary>
		private void OnServerSceneServerDetailsBroadcast(NetworkConnection conn, SceneServerDetailsBroadcast msg)
		{
			Debug.Log("[" + DateTime.UtcNow + "] SceneServer Details Received from " + msg.address + ":" + msg.port);

			if (sceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.connection = conn;
				details.lastPulse = DateTime.UtcNow;
				details.address = msg.address;
				details.port = msg.port;
				if (details.scenes != null)
					details.scenes.Clear();
				details.scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
				details.locked = false; // scene server is now alive
			}
		}

		/// <summary>
		/// The scene server sent us the loaded scene list
		/// </summary>
		private void OnServerSceneListBroadcastReceived(NetworkConnection conn, SceneListBroadcast msg)
		{
			if (sceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				if (details.scenes != null)
					details.scenes.Clear();
				details.scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
			}
		}

		/// <summary>
		/// The scene server loaded a scene
		/// </summary>
		private void OnServerSceneLoadBroadcastReceived(NetworkConnection conn, SceneLoadBroadcast msg)
		{
			if (sceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				if (!details.scenes.ContainsKey(msg.handle))
				{
					details.scenes.Add(msg.handle, new SceneInstanceDetails()
					{
						name = msg.sceneName,
						handle = msg.handle,
						clientCount = 0,
					});
				}
			}

			if (waitingScenes.TryGetValue(msg.sceneName, out SceneWaitQueueData waitData))
			{
				waitData.ready = true;
			}
		}

		/// <summary>
		/// The scene server unloaded a scene
		/// </summary>
		private void OnServerSceneUnloadBroadcastReceived(NetworkConnection conn, SceneUnloadBroadcast msg)
		{
			if (sceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.scenes.Remove(msg.handle);
			}
		}

		/// <summary>
		/// Tell the connection to reconnect to the specified Scene Server
		/// </summary>
		private void SendWorldSceneConnectBroadcast(NetworkConnection conn)
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			// get the scene for the selected character
			if (sceneServers.Count < 1 ||
				!AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!CharacterService.TryGetSelectedCharacterSceneName(dbContext, accountName, out string sceneName))
			{
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			string address = "";
			ushort port = 0;

			// check if any scene servers are running an instance of the scene with fewer than max clients
			foreach (SceneServerDetails details in sceneServers.Values)
			{
				foreach (SceneInstanceDetails instance in details.scenes.Values)
				{
					if (instance.name.Equals(sceneName) &&
						instance.clientCount < MAX_CLIENTS_PER_INSTANCE)
					{
						address = details.address;
						port = details.port;
						break;
					}
				}
			}

			// tell the client to connect to the scene server
			if (!string.IsNullOrWhiteSpace(address))
			{
				// tell the character to reconnect to the scene server for further load balancing
				conn.Broadcast(new WorldSceneConnectBroadcast()
				{
					address = address,
					port = port,
				});
			}
			// load the scene on a scene server if we didn't get a valid address
			else
			{
				// add the connection to the waiting list until the scene is loaded
				if (!waitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
				{
					waitingConnections.Add(sceneName, connections = new HashSet<NetworkConnection>());
				}

				if (!connections.Contains(conn))
				{
					connections.Add(conn);
				}

				// !!! load balance here !!!

				if (waitingScenes.ContainsKey(sceneName))
				{
					// we are waiting for a scene server to load the scene already!
					return;
				}

				SceneServerDetails sceneServer = null;
				// find the scene server with the fewest number of characters
				foreach (SceneServerDetails sceneConn in sceneServers.Values)
				{
					if (sceneServer == null)
					{
						sceneServer = sceneConn;
						continue;
					}
					if (sceneServer.scenes == null || sceneServer.scenes.Count < 1)
					{
						break;
					}
					if (sceneServer.TotalClientCount > sceneConn.TotalClientCount)
					{
						sceneServer = sceneConn;
					}
				}

				// tell the scene server to load the scene
				sceneServer.connection.Broadcast(new SceneLoadBroadcast()
				{
					sceneName = sceneName,
				});

				waitingScenes.Add(sceneName, new SceneWaitQueueData()
				{
					address = sceneServer.address,
					port = sceneServer.port,
					ready = false,
				});
			}
		}

		/// <summary>
		/// A character connected to a scene server. Add them to the world character list so we can relay broadcasts.
		/// </summary>
		private void OnServerSceneCharacterConnectedBroadcastReceived(NetworkConnection conn, SceneCharacterConnectedBroadcast msg)
		{
			if (sceneCharacters.ContainsKey(msg.characterId))
			{
				sceneCharacters[msg.characterId] = conn;
			}
			else
			{
				sceneCharacters.Add(msg.characterId, conn);
			}
		}

		/// <summary>
		/// A character disconnected from a scene server.
		/// </summary>
		private void OnServerSceneCharacterDisconnectedBroadcastReceived(NetworkConnection conn, SceneCharacterDisconnectedBroadcast msg)
		{
			sceneCharacters.Remove(msg.characterId);
		}

		public void BroadcastToAllScenes<T>(T msg) where T : struct, IBroadcast
		{
			foreach (NetworkConnection serverConn in sceneServers.Keys)
			{
				serverConn.Broadcast(msg);
			}
		}

		public void BroadcastToCharacter<T>(long characterId, T msg) where T : struct, IBroadcast
		{
			if (sceneCharacters.TryGetValue(characterId, out NetworkConnection sceneConn))
			{
				sceneConn.Broadcast(msg);
			}
		}
	}
}