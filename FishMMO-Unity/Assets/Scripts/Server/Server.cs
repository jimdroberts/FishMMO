using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Bayou;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Redis;
using FishMMO.Shared;
using FishMMO.Logging;
using Configuration = FishMMO.Shared.Configuration;
#if UNITY_EDITOR
using UnityEditor;
#endif
using KinematicCharacterController;

namespace FishMMO.Server
{
	/// <summary>
	/// Main Server class for FishMMO. Handles configuration, database context initialization, network setup, and server lifecycle management.
	/// </summary>
	public class Server : MonoBehaviour
	{
		/// <summary>
		/// Enum representing the type of server being launched.
		/// </summary>
		private enum ServerType
		{
			Invalid = 0,
			Login,
			World,
			Scene,
		}

		/// <summary>
		/// Factory for creating PostgreSQL database contexts.
		/// </summary>
		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; private set; }
		/// <summary>
		/// Factory for creating Redis database contexts.
		/// </summary>
		public RedisDbContextFactory RedisDbContextFactory { get; private set; }
		/// <summary>
		/// Reference to the FishNet NetworkManager in the scene.
		/// </summary>
		public NetworkManager NetworkManager { get; private set; }
		/// <summary>
		/// The external IP address of the server, as detected at startup.
		/// </summary>
		public string RemoteAddress { get; private set; }
		/// <summary>
		/// Optional override for the server bind address.
		/// </summary>
		public string AddressOverride;
		/// <summary>
		/// Optional override for the server bind port.
		/// </summary>
		public ushort PortOverride;
		/// <summary>
		/// The bind address used by the server transport.
		/// </summary>
		public string Address { get; private set; }
		/// <summary>
		/// The bind port used by the server transport.
		/// </summary>
		public ushort Port { get; private set; }

		/// <summary>
		/// Invoked when the LoginServer is initialized.
		/// </summary>
		public Action OnLoginServerInitialized;
		/// <summary>
		/// Invoked when the WorldServer is initialized.
		/// </summary>
		public Action OnWorldServerInitialized;
		/// <summary>
		/// Invoked when the SceneServer is initialized.
		/// </summary>
		public Action OnSceneServerInitialized;

		/// <summary>
		/// Reference to the window title updater for the server process.
		/// </summary>
		public ServerWindowTitleUpdater ServerWindowTitleUpdater { get; private set; }

		/// <summary>
		/// Path to the log file for this server instance.
		/// </summary>
		private string logFilePath;
		/// <summary>
		/// UTC time when the server started.
		/// </summary>
		private DateTime startTime;

		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private LocalConnectionState serverState = LocalConnectionState.Stopped;
		/// <summary>
		/// The type of server currently running.
		/// </summary>
		private ServerType serverType = ServerType.Invalid;
		/// <summary>
		/// The name of the server type, derived from the scene name.
		/// </summary>
		private string serverTypeName;

		/// <summary>
		/// Unity Start callback. Initializes server type, configuration, and network setup.
		/// </summary>
		void Start()
		{
			startTime = DateTime.UtcNow;

			serverType = GetServerType();
			// Validate server type
			if (serverType == ServerType.Invalid)
			{
				Server.Quit();
			}

			Log.Debug("Server", $"{serverTypeName} is starting[{DateTime.UtcNow}]");

			StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup));
		}

		/// <summary>
		/// Finalizes server setup after external IP address is fetched. Loads configuration, initializes database contexts, network manager, and server behaviours.
		/// </summary>
		/// <param name="remoteAddress">The detected external IP address.</param>
		private void OnFinalizeSetup(string remoteAddress)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
			{
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");
			}

			RemoteAddress = remoteAddress;

			string workingDirectory = Constants.GetWorkingDirectory();
			Log.Debug("Server", "Current working directory[" + workingDirectory + "]");

			// Load configuration
			Configuration.SetGlobalSettings(new Configuration(workingDirectory));
			if (!Configuration.GlobalSettings.Load(serverTypeName))
			{
				// If we failed to load the file, save a new one with defaults
				Configuration.GlobalSettings.Set("ServerName", "TestName");
				Configuration.GlobalSettings.Set("MaximumClients", 4000);
				Configuration.GlobalSettings.Set("Address", "127.0.0.1");
				Configuration.GlobalSettings.Set("Port", serverType == ServerType.Login ? "7770" : serverType == ServerType.World ? "7780" : "7781");
				Configuration.GlobalSettings.Set("StaleSceneTimeout", 5);
#if !UNITY_EDITOR
				Configuration.GlobalSettings.Save();
#endif
			}
			Log.Debug("Server", Configuration.GlobalSettings.ToString());

			// Initialize the DB contexts
#if UNITY_EDITOR
			string dbConfigurationPath = Path.Combine(Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory), "Development");

			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(dbConfigurationPath, false);
			//RedisDbContextFactory = new RedisDbContextFactory(dbConfigurationPath);
#else
			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(workingDirectory, false);
			//RedisDbContextFactory = new RedisDbContextFactory(workingDirectory);
#endif
			// Ensure our NetworkManager exists in the scene
			if (NetworkManager == null)
			{
				NetworkManager = FindFirstObjectByType<NetworkManager>();

				if (NetworkManager == null)
				{
					throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");
				}
			}

			// Initialize server behaviours
			Log.Debug("Server", "Initializing Components");

			// Load Server Details
			if (!LoadTransportServerDetails())
			{
				throw new UnityException("Server: Failed to load Server Details.");
			}

			// Ensure the Kinematic Character Controller System is created and configured
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			// Database factory dependency injection for login authenticator
			LoginServerAuthenticator authenticator = NetworkManager.ServerManager.GetAuthenticator() as LoginServerAuthenticator;
			if (authenticator != null)
			{
				authenticator.NpgsqlDbContextFactory = NpgsqlDbContextFactory;
			}

			// Initialize all registered server behaviours
			ServerBehaviour.InitializeOnceInternal(this, NetworkManager.ServerManager);

			Log.Debug("Server", "Initialization Complete");

			// Start the local server connection
			if (NetworkManager.ServerManager != null)
			{
				NetworkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				NetworkManager.ServerManager.StartConnection();

				StartCoroutine(OnAwaitingConnectionReady());
			}
			else
			{
				Server.Quit();
			}

			Log.Debug("Server", $"{serverTypeName} is running[{DateTime.UtcNow}]");
		}

		/// <summary>
		/// Quits the server application. Exits play mode in editor, or closes the application in build.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Determines the server type based on the current scene name.
		/// </summary>
		/// <returns>The detected ServerType.</returns>
		private ServerType GetServerType()
		{
			serverTypeName = gameObject.scene.name;
			string upper = serverTypeName.ToUpper();
			if (upper.StartsWith("LOGIN"))
			{
				return ServerType.Login;
			}
			if (upper.StartsWith("WORLD"))
			{
				return ServerType.World;
			}
			if (upper.StartsWith("SCENE"))
			{
				return ServerType.Scene;
			}
			return ServerType.Invalid;
		}

		/// <summary>
		/// Gets a component of type T, creating and adding it if it does not exist.
		/// </summary>
		/// <typeparam name="T">Type of the component.</typeparam>
		/// <returns>The component instance.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private T GetOrCreateComponent<T>() where T : UnityEngine.Component
		{
			if (gameObject.TryGetComponent<T>(out T result))
				return result;
			else
				return gameObject.AddComponent<T>();
		}

		/// <summary>
		/// Handles server connection state changes and logs transport details.
		/// </summary>
		/// <param name="obj">Connection state arguments.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			serverState = obj.ConnectionState;

			Transport transport = NetworkManager.TransportManager.GetTransport(obj.TransportIndex);
			if (transport != null)
			{
				Log.Debug("Server", $"{serverTypeName} Local: {transport.GetServerBindAddress(IPAddressType.IPv4)}:{transport.GetPort()} Remote: {RemoteAddress}:{transport.GetPort()} - {transport.GetType().Name} {serverState}");
			}
		}

		/// <summary>
		/// Coroutine that waits for the server connection to be ready before proceeding.
		/// </summary>
		/// <returns>IEnumerator for coroutine.</returns>
		IEnumerator OnAwaitingConnectionReady()
		{
			// Wait for the connection to the current server to start before we connect the client
			while (serverState != LocalConnectionState.Started)
			{
				yield return new WaitForSeconds(.5f);
			}

			yield return null;
		}

		/// <summary>
		/// Broadcasts a message to a network connection.
		/// </summary>
		/// <typeparam name="T">Type of broadcast struct.</typeparam>
		/// <param name="conn">The network connection.</param>
		/// <param name="broadcast">The broadcast message.</param>
		/// <param name="requireAuthentication">Whether authentication is required.</param>
		/// <param name="channel">The channel to use for broadcasting.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(NetworkConnection conn, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Sending: " + typeof(T));
			conn.Broadcast(broadcast, requireAuthentication, channel);
		}

		/// <summary>
		/// Registers a broadcast handler for a specific broadcast type.
		/// </summary>
		/// <typeparam name="T">Type of broadcast struct.</typeparam>
		/// <param name="handler">The handler to register.</param>
		/// <param name="requireAuthentication">Whether authentication is required.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RegisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Registered " + typeof(T));
			NetworkManager.ServerManager.RegisterBroadcast<T>(handler, requireAuthentication);
		}

		/// <summary>
		/// Unregisters a broadcast handler for a specific broadcast type.
		/// </summary>
		/// <typeparam name="T">Type of broadcast struct.</typeparam>
		/// <param name="handler">The handler to unregister.</param>
		/// <param name="requireAuthentication">Whether authentication is required.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UnregisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Unregistered " + typeof(T));
			NetworkManager.ServerManager.UnregisterBroadcast<T>(handler);
		}

		/// <summary>
		/// Loads transport server details from the configuration file and applies them to the transport.
		/// </summary>
		/// <returns>True if details were loaded and applied successfully, false otherwise.</returns>
		private bool LoadTransportServerDetails()
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (Configuration.GlobalSettings.TryGetString("Address", out string address) &&
				Configuration.GlobalSettings.TryGetUShort("Port", out ushort port) &&
				Configuration.GlobalSettings.TryGetInt("MaximumClients", out int maximumClients))
			{
				Address = address;
				Port = port;
				transport.SetServerBindAddress(Address, IPAddressType.IPv4);
				transport.SetPort(Port);
				transport.SetMaximumClients(maximumClients);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IPv4 address and port from the transport.
		/// </summary>
		/// <param name="address">Output server address struct.</param>
		/// <returns>True if successful, false otherwise.</returns>
		public bool TryGetServerIPv4AddressFromTransport(out ServerAddress address)
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				address = new ServerAddress()
				{
					Address = transport.GetServerBindAddress(IPAddressType.IPv4),
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IPv6 address and port from the transport.
		/// </summary>
		/// <param name="address">Output server address struct.</param>
		/// <returns>True if successful, false otherwise.</returns>
		public bool TryGetServerIPv6AddressFromTransport(out ServerAddress address)
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				address = new ServerAddress()
				{
					Address = transport.GetServerBindAddress(IPAddressType.IPv6),
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IP address and port, using overrides if set, otherwise using transport and remote address.
		/// </summary>
		/// <param name="address">Output server address struct.</param>
		/// <returns>True if successful, false otherwise.</returns>
		public bool TryGetServerIPAddress(out ServerAddress address)
		{
			if (!string.IsNullOrEmpty(AddressOverride))
			{
				address = new ServerAddress()
				{
					Address = AddressOverride,
					Port = PortOverride,
				};
				return true;
			}

			const string LoopBack = "127.0.0.1";
			const string LocalHost = "localhost";

			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				string actualAddress = LoopBack;
				if (!string.IsNullOrWhiteSpace(Address) &&
					(Address.Equals(LoopBack) || Address.Equals(LocalHost)))
				{
					actualAddress = Address;
				}
				else if (!string.IsNullOrWhiteSpace(RemoteAddress))
				{
					actualAddress = RemoteAddress;
				}

				address = new ServerAddress()
				{
					Address = actualAddress,
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}
	}
}