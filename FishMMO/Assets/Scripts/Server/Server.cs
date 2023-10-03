using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO_DB;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Server
{
	// Main Server class, handles configuration and starting connections.
	public class Server : MonoBehaviour
	{
		public Configuration Configuration = null;
		public string Address;
		public ushort Port;
		public ServerDbContextFactory DbContextFactory;
		public NetworkManager NetworkManager;

		#region LOGIN
		public CharacterSelectSystem CharacterSelectSystem { get; private set; }
		public CharacterCreateSystem CharacterCreateSystem { get; private set; }
		public ServerSelectSystem ServerSelectSystem { get; private set; }
		#endregion

		#region WORLD
		public WorldServerSystem WorldServerSystem { get; private set; }
		public WorldSceneSystem WorldSceneSystem { get; private set; }
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
		private string serverTypeName;

		void Awake()
		{
			// get the server type so we know how to configure
			string serverType = GetServerType();
			if (serverType.Equals("Invalid"))
			{
				Server.Quit();
			}
			Debug.Log(serverType + " is starting.");

			string path = Server.GetWorkingDirectory();
			Debug.Log("Current working directory: " + path);

			// load configuration
			Configuration = new Configuration(path);
			if (!Configuration.Load(serverTypeName + Configuration.EXTENSION))
			{
				// if we failed to load the file.. save a new one
				Configuration.Set("ServerName", "TestName");
				Configuration.Set("MaximumClients", 4000);
				Configuration.Set("Address", "127.0.0.1");
				Configuration.Set("Port", 7770);
				Configuration.Save();
			}

			// setup the DB context and ensure that it's been created
			DbContextFactory = new ServerDbContextFactory(path, false);

			// ensure our NetworkManager exists in the scene
			
			if (NetworkManager == null)
			{
				NetworkManager = FindObjectOfType<NetworkManager>();

				if (NetworkManager == null)
				{
					throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");
				}
			}

			// initialize required components for our specified server type
			InternalInitializeOnce(serverType);

			// automatically start the server
			if (NetworkManager.ServerManager != null && LoadTransportServerDetails())
			{
				NetworkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				// start the local server connection
				NetworkManager.ServerManager.StartConnection();
				

				StartCoroutine(OnAwaitingConnectionReady());
			}
			else
			{
				Server.Quit();
			}

			Debug.Log(serverType + " is running.");
		}

		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		public static void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		private string GetServerType()
		{
			Scene scene = gameObject.scene;
			if (!scene.path.Contains("Bootstraps"))
			{
				throw new UnityException("Active scene is not in the bootstraps folder.");
			}
			serverTypeName = scene.name;
			string upper = serverTypeName.ToUpper();
			if (upper.StartsWith("LOGIN"))
			{
				return "LOGIN";
			}
			if (upper.StartsWith("WORLD"))
			{
				return "WORLD";
			}
			if (upper.StartsWith("SCENE"))
			{
				return "SCENE";
			}
			return "Invalid";
		}

		/// <summary>
		/// Order of Execution, Dependency Injection, and all other server initialization should be handled here.
		/// </summary>
		internal void InternalInitializeOnce(string serverType)
		{
			Debug.Log("Server: Initializing Components");

			// only use title updater if it has been added to the scene
			ServerWindowTitleUpdater = GetComponent<ServerWindowTitleUpdater>();
			if (ServerWindowTitleUpdater != null)
			{
				ServerWindowTitleUpdater.InternalInitializeOnce(this, NetworkManager.ServerManager);
			}

			// database factory DI
			LoginServerAuthenticator authenticator = NetworkManager.ServerManager.GetAuthenticator() as LoginServerAuthenticator;
			if (authenticator != null)
			{
				authenticator.DBContextFactory = DbContextFactory;
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
					break;
				case "WORLD":
					WorldServerSystem = GetOrCreateComponent<WorldServerSystem>();
					WorldServerSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);

					WorldSceneSystem = GetOrCreateComponent<WorldSceneSystem>();
					WorldSceneSystem.InternalInitializeOnce(this, NetworkManager.ServerManager);
					WorldServerAuthenticator worldAuthenticator = NetworkManager.ServerManager.GetAuthenticator() as WorldServerAuthenticator;
					if (worldAuthenticator != null)
					{
						worldAuthenticator.WorldSceneSystem = WorldSceneSystem;
					}

					// world server has special title bar that handles relay information
					if (ServerWindowTitleUpdater != null)
					{
						ServerWindowTitleUpdater.WorldSceneSystem = WorldSceneSystem;
					}
					break;
				case "SCENE":
					SceneServerSystem = GetOrCreateComponent<SceneServerSystem>();
					SceneServerSystem.SceneManager = NetworkManager.SceneManager;
					SceneServerSystem.InternalInitializeOnce(this, NetworkManager.ServerManager, NetworkManager.ClientManager);

					CharacterSystem = GetOrCreateComponent<CharacterSystem>();
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
					Server.Quit();
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

			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				Debug.Log(serverTypeName + " " + transport.GetServerBindAddress(IPAddressType.IPv4) + ":" + transport.GetPort() + " - " + serverState);
			}
		}

		IEnumerator OnAwaitingConnectionReady()
		{
			// wait for the connection to the current server to start before we connect the client
			while (serverState != LocalConnectionState.Started)
			{
				yield return new WaitForSeconds(.5f);
			}

			yield return null;
		}

		/// <summary>
		/// Loads transport server details from the configuration file.
		/// </summary>
		private bool LoadTransportServerDetails()
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null &&
				Configuration.TryGetString("Address", out Address) &&
				Configuration.TryGetUShort("Port", out Port) &&
				Configuration.TryGetInt("MaximumClients", out int maximumClients))
			{
				transport.SetServerBindAddress(Address, IPAddressType.IPv4);
				transport.SetPort(Port);
				transport.SetMaximumClients(maximumClients);
				return true;
			}
			return false;
		}
	}
}