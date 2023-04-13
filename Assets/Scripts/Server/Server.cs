using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Server
{
	// Main Server class, handles configuration and starting connections.
	public class Server : MonoBehaviour
	{
		public string configurationFileName;
		public Configuration configuration = null;
		public string address;
		public ushort port;
		public string relayAddress;
		public ushort relayPort;
		public ServerDbContextFactory DbContextFactory;

		public NetworkManager NetworkManager { get; private set; }
		//public DbContextFactory DBContextFactory { get; private set; }

		#region LOGIN
		public CharacterSelectSystem CharacterSelectSystem { get; private set; }
		public CharacterCreateSystem CharacterCreateSystem { get; private set; }
		public ServerSelectSystem ServerSelectSystem { get; private set; }
		public DatabaseInitializerSystem DatabaseInitializerSystem { get; private set; }
		#endregion

		#region WORLD
		public WorldServerSystem WorldServerSystem { get; private set; }
		public WorldSceneSystem WorldSceneSystem { get; private set; }
		public WorldChatSystem WorldChatSystem { get; private set; }
		#endregion

		#region SCENE
		public SceneServerSystem SceneServerSystem { get; private set; }
		public CharacterSystem CharacterSystem { get; private set; }
		public CharacterInventorySystem CharacterInventorySystem { get; private set; }
		public ChatSystem ChatSystem { get; private set; }
		public GuildSystem GuildSystem { get; private set; }
		public PartySystem PartySystem { get; private set; }
		#endregion

		public ServerWindowTitleUpdater ServerWindowTitleUpdater { get; private set; }

		private LocalConnectionState serverState = LocalConnectionState.Stopped;

		void Awake()
		{
			// load configuration first
			configuration = new Configuration();
			if (!configuration.Load(configurationFileName))
			{
				// if we failed to load the file.. save a new one
				configuration.Set("ServerName", "TestName");
				configuration.Set("MaximumClients", 4000);
				configuration.Set("Address", "127.0.0.1");
				configuration.Set("Port", 7770);
				configuration.Set("RelayAddress", "");
				configuration.Set("RelayPort", 0);
				configuration.Save();
			}

			// ensure our NetworkManager exists in the scene
			NetworkManager = FindObjectOfType<NetworkManager>();
			if (NetworkManager == null)
			{
				throw new UnityException("[" + DateTime.UtcNow + "] Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");
			}

			string serverType = GetServerType();

			//DBContextFactory = new DbContextFactory();

			InternalInitializeOnce(serverType);

			// automatically start the server
			if (NetworkManager.ServerManager != null && LoadTransportServerDetails())
			{
				// start the local server connection
				NetworkManager.ServerManager.StartConnection();

				Transport transport = NetworkManager.TransportManager.Transport;
				if (transport != null)
				{
					Debug.Log(transport.GetServerBindAddress(IPAddressType.IPv4) + ":" + transport.GetPort());
				}
				NetworkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				StartCoroutine(OnAwaitingConnectionReady());
			}
			else
			{
#if UNITY_EDITOR
				EditorApplication.ExitPlaymode();
#else
				Application.Quit();
#endif
			}
		}

		private string GetServerType()
		{
			Scene scene = SceneManager.GetActiveScene();
			if (!scene.path.Contains("Bootstraps"))
			{
				throw new UnityException("Active scene is not in the bootstraps folder.");
			}

			string name = scene.name.ToUpper();
			if (name.StartsWith("LOGIN"))
			{
				return "LOGIN";
			}
			if (name.StartsWith("WORLD"))
			{
				return "WORLD";
			}
			if (name.StartsWith("SCENE"))
			{
				return "SCENE";
			}
			return "Invalid";
		}

		internal void InternalInitializeOnce(string serverType)
		{
			ServerWindowTitleUpdater = GetComponent<ServerWindowTitleUpdater>();
			if (ServerWindowTitleUpdater != null)
				ServerWindowTitleUpdater.InternalInitializeOnce(this, NetworkManager.ServerManager);
			
			// setup the DB context and ensure that it's been created
			DbContextFactory = new ServerDbContextFactory();

			LoginServerAuthenticator authenticator = NetworkManager.Authenticator as LoginServerAuthenticator;
			if (authenticator != null)
			{
				//authenticator.DBContextFactory = DbContextFactory;
			}

			switch (serverType)
			{
				case "LOGIN":
					CharacterSelectSystem = GetOrCreateComponent<CharacterSelectSystem>();
					CharacterSelectSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					CharacterCreateSystem = GetOrCreateComponent<CharacterCreateSystem>();
					CharacterCreateSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					ServerSelectSystem = GetOrCreateComponent<ServerSelectSystem>();
					ServerSelectSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);
					
					// TODO: where should this behavior live?
					DatabaseInitializerSystem = GetOrCreateComponent<DatabaseInitializerSystem>();
					DatabaseInitializerSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);
					break;
				case "WORLD":
					WorldServerSystem = GetOrCreateComponent<WorldServerSystem>();
					WorldServerSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					WorldSceneSystem = GetOrCreateComponent<WorldSceneSystem>();
					WorldSceneSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					WorldChatSystem = GetOrCreateComponent<WorldChatSystem>();
					WorldChatSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					if (ServerWindowTitleUpdater != null)
						ServerWindowTitleUpdater.WorldSceneSystem = WorldSceneSystem;
					break;
				case "SCENE":
					SceneServerSystem = GetOrCreateComponent<SceneServerSystem>();
					SceneServerSystem.SceneManager = NetworkManager.SceneManager;
					SceneServerSystem.InternalInitializeOnce(this, NetworkManager.ServerManager, NetworkManager.ClientManager);

					CharacterSystem = GetOrCreateComponent<CharacterSystem>();
					CharacterSystem.SceneServerSystem = SceneServerSystem;
					CharacterSystem.InternalInitializeOnce(this, NetworkManager.ServerManager, NetworkManager.ClientManager);

					CharacterInventorySystem = GetOrCreateComponent<CharacterInventorySystem>();
					CharacterInventorySystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					ChatSystem = GetOrCreateComponent<ChatSystem>();
					ChatSystem.SceneManager = NetworkManager.SceneManager;
					ChatSystem.InternalInitializeOnce(this, NetworkManager.ServerManager, NetworkManager.ClientManager);

					GuildSystem = GetOrCreateComponent<GuildSystem>();
					GuildSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					PartySystem = GetOrCreateComponent<PartySystem>();
					PartySystem.InternalInitializeOnce(this, NetworkManager.ServerManager);
					break;
				default:
#if UNITY_EDITOR
					EditorApplication.ExitPlaymode();
#else
					Application.Quit();
#endif
					return;
			}
		}

		/// <summary>
		/// Gets a component, creating and adding it if it does not exist.
		/// </summary>
		private T GetOrCreateComponent<T>() where T : UnityEngine.Component
		{
			if (gameObject.TryGetComponent<T>(out T result))
				return result;
			else
				return gameObject.AddComponent<T>();
		}

		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			serverState = obj.ConnectionState;
		}

		IEnumerator OnAwaitingConnectionReady()
		{
			// wait for the connection to the current server to start before we connect the client
			while (serverState != LocalConnectionState.Started)
			{
				yield return new WaitForSeconds(.5f);
			}

			// attempt to connect to a relay server, cluster nodes handle internal systems
			if (NetworkManager.ClientManager != null && LoadRelayServerAddress())
			{
				NetworkManager.ClientManager.StartConnection(relayAddress, relayPort);
				Debug.Log(relayAddress + ":" + relayPort);
			}

			yield return null;
		}

		private bool LoadTransportServerDetails()
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null &&
				configuration.TryGetString("Address", out address) &&
				configuration.TryGetUShort("Port", out port) &&
				configuration.TryGetInt("MaximumClients", out int maximumClients))
			{
				transport.SetServerBindAddress(address, IPAddressType.IPv4);
				transport.SetPort(port);
				transport.SetMaximumClients(maximumClients);
				return true;
			}
			return false;
		}

		private bool LoadRelayServerAddress()
		{
			if (configuration.TryGetString("RelayAddress", out relayAddress) &&
				address.Length > 0 &&
				IsRelayAddressValid(address) &&
				configuration.TryGetUShort("RelayPort", out relayPort))
			{
				return true;
			}
			return false;
		}

		public bool IsRelayAddressValid(string address)
		{
			const string ValidIpAddressRegex = "^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$";
			Match match = Regex.Match(address, ValidIpAddressRegex);
			return match.Success;
		}
	}
}