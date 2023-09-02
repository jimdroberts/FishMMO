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
		public Dictionary<NetworkConnection, SceneServerDetails> SceneServers = new Dictionary<NetworkConnection, SceneServerDetails>();

		// characterName, sceneConnection
		/// <summary>
		/// Active client connections.
		/// </summary>
		public Dictionary<long, NetworkConnection> SceneCharacters = new Dictionary<long, NetworkConnection>();

		// sceneName, waitingConnections
		/// <summary>
		/// Connections waiting for a scene to finish loading.
		/// </summary>
		public Dictionary<string, HashSet<NetworkConnection>> WaitingConnections = new Dictionary<string, HashSet<NetworkConnection>>();

		// sceneName, waitingSceneServer
		/// <summary>
		/// Scenes that are waiting to be loaded fully.
		/// </summary>
		public Dictionary<string, SceneWaitQueueData> WaitingScenes = new Dictionary<string, SceneWaitQueueData>();

		public int ConnectionCount
		{
			get
			{
				return WaitingConnections.Count + SceneCharacters.Count;
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

				foreach (string sceneName in new List<string>(WaitingConnections.Keys))
				{
					TryClearWaitQueues(sceneName);
				}
			}
		}

		private void TryClearWaitQueues(string sceneName)
		{
			if (WaitingScenes.TryGetValue(sceneName, out SceneWaitQueueData waitData) &&
				waitData.ready)
			{
				if (WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
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
					WaitingConnections.Remove(sceneName);
				}

				WaitingScenes.Remove(sceneName);
			}
		}

		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				SceneServers.Remove(conn);
			}
		}

		// add our node server to the relay
		private void Authenticator_OnRelayServerAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			if (!authenticated)
				return;

			if (!SceneServers.ContainsKey(conn))
			{
				SceneServers.Add(conn, new SceneServerDetails()
				{
					Locked = true,
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
					newDetails.Add(instance.Handle, instance);
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
			if (SceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.LastPulse = DateTime.UtcNow;
				if (details.Scenes != null)
					details.Scenes.Clear();
				details.Scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
			}
		}

		/// <summary>
		/// The scene server sent its details
		/// </summary>
		private void OnServerSceneServerDetailsBroadcast(NetworkConnection conn, SceneServerDetailsBroadcast msg)
		{
			Debug.Log("[" + DateTime.UtcNow + "] SceneServer Details Received from " + msg.address + ":" + msg.port);

			if (SceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.Connection = conn;
				details.LastPulse = DateTime.UtcNow;
				details.Address = msg.address;
				details.Port = msg.port;
				if (details.Scenes != null)
					details.Scenes.Clear();
				details.Scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
				details.Locked = false; // scene server is now alive
			}
		}

		/// <summary>
		/// The scene server sent us the loaded scene list
		/// </summary>
		private void OnServerSceneListBroadcastReceived(NetworkConnection conn, SceneListBroadcast msg)
		{
			if (SceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				if (details.Scenes != null)
					details.Scenes.Clear();
				details.Scenes = RebuildSceneInstanceDetails(msg.sceneInstanceDetails);
			}
		}

		/// <summary>
		/// The scene server loaded a scene
		/// </summary>
		private void OnServerSceneLoadBroadcastReceived(NetworkConnection conn, SceneLoadBroadcast msg)
		{
			if (SceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				if (!details.Scenes.ContainsKey(msg.handle))
				{
					details.Scenes.Add(msg.handle, new SceneInstanceDetails()
					{
						Name = msg.sceneName,
						Handle = msg.handle,
						ClientCount = 0,
					});
				}
			}

			if (WaitingScenes.TryGetValue(msg.sceneName, out SceneWaitQueueData waitData))
			{
				waitData.ready = true;
			}
		}

		/// <summary>
		/// The scene server unloaded a scene
		/// </summary>
		private void OnServerSceneUnloadBroadcastReceived(NetworkConnection conn, SceneUnloadBroadcast msg)
		{
			if (SceneServers.TryGetValue(conn, out SceneServerDetails details))
			{
				details.Scenes.Remove(msg.handle);
			}
		}

		/// <summary>
		/// Tell the connection to reconnect to the specified Scene Server
		/// </summary>
		private void SendWorldSceneConnectBroadcast(NetworkConnection conn)
		{
			using var dbContext = Server.DbContextFactory.CreateDbContext();
			// get the scene for the selected character
			if (SceneServers.Count < 1 ||
				!AccountManager.GetAccountNameByConnection(conn, out string accountName) ||
				!CharacterService.TryGetSelectedCharacterSceneName(dbContext, accountName, out string sceneName))
			{
				conn.Kick(KickReason.UnexpectedProblem);
				return;
			}

			string address = "";
			ushort port = 0;

			// check if any scene servers are running an instance of the scene with fewer than max clients
			foreach (SceneServerDetails details in SceneServers.Values)
			{
				foreach (SceneInstanceDetails instance in details.Scenes.Values)
				{
					if (instance.Name.Equals(sceneName) &&
						instance.ClientCount < MAX_CLIENTS_PER_INSTANCE)
					{
						address = details.Address;
						port = details.Port;
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
				if (!WaitingConnections.TryGetValue(sceneName, out HashSet<NetworkConnection> connections))
				{
					WaitingConnections.Add(sceneName, connections = new HashSet<NetworkConnection>());
				}

				if (!connections.Contains(conn))
				{
					connections.Add(conn);
				}

				// !!! load balance here !!!

				if (WaitingScenes.ContainsKey(sceneName))
				{
					// we are waiting for a scene server to load the scene already!
					return;
				}

				SceneServerDetails sceneServer = null;
				// find the scene server with the fewest number of characters
				foreach (SceneServerDetails sceneConn in SceneServers.Values)
				{
					if (sceneServer == null)
					{
						sceneServer = sceneConn;
						continue;
					}
					if (sceneServer.Scenes == null || sceneServer.Scenes.Count < 1)
					{
						break;
					}
					if (sceneServer.TotalClientCount > sceneConn.TotalClientCount)
					{
						sceneServer = sceneConn;
					}
				}

				// tell the scene server to load the scene
				sceneServer.Connection.Broadcast(new SceneLoadBroadcast()
				{
					sceneName = sceneName,
				});

				WaitingScenes.Add(sceneName, new SceneWaitQueueData()
				{
					address = sceneServer.Address,
					port = sceneServer.Port,
					ready = false,
				});
			}
		}

		/// <summary>
		/// A character connected to a scene server. Add them to the world character list so we can relay broadcasts.
		/// </summary>
		private void OnServerSceneCharacterConnectedBroadcastReceived(NetworkConnection conn, SceneCharacterConnectedBroadcast msg)
		{
			if (SceneCharacters.ContainsKey(msg.characterId))
			{
				SceneCharacters[msg.characterId] = conn;
			}
			else
			{
				SceneCharacters.Add(msg.characterId, conn);
			}
		}

		/// <summary>
		/// A character disconnected from a scene server.
		/// </summary>
		private void OnServerSceneCharacterDisconnectedBroadcastReceived(NetworkConnection conn, SceneCharacterDisconnectedBroadcast msg)
		{
			SceneCharacters.Remove(msg.characterId);
		}

		public void BroadcastToAllScenes<T>(T msg) where T : struct, IBroadcast
		{
			foreach (NetworkConnection serverConn in SceneServers.Keys)
			{
				serverConn.Broadcast(msg);
			}
		}

		public void BroadcastToCharacter<T>(long characterId, T msg) where T : struct, IBroadcast
		{
			if (SceneCharacters.TryGetValue(characterId, out NetworkConnection sceneConn))
			{
				sceneConn.Broadcast(msg);
			}
		}
	}
}