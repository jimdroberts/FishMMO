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
	// Main Server class, handles configuration and starting connections.
	public class Server : MonoBehaviour
	{
		private enum ServerType
		{
			Invalid = 0,
			Login,
			World,
			Scene,
		}

		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; private set; }
		public RedisDbContextFactory RedisDbContextFactory { get; private set; }
		public NetworkManager NetworkManager { get; private set; }
		public string RemoteAddress { get; private set; }
		public string AddressOverride;
		public ushort PortOverride;
		public string Address { get; private set; }
		public ushort Port { get; private set; }

		public Action OnLoginServerInitialized;
		public Action OnWorldServerInitialized;
		public Action OnSceneServerInitialized;

		public ServerWindowTitleUpdater ServerWindowTitleUpdater { get; private set; }

		private string logFilePath;
		private DateTime startTime;

		private LocalConnectionState serverState = LocalConnectionState.Stopped;
		private ServerType serverType = ServerType.Invalid;
		private string serverTypeName;

		void Start()
		{
			startTime = DateTime.UtcNow;

			serverType = GetServerType();
			// validate server type
			if (serverType == ServerType.Invalid)
			{
				Server.Quit();
			}

			Log.Debug("Server", $"{serverTypeName} is starting[{DateTime.UtcNow}]");

			StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup));
		}

		private void OnFinalizeSetup(string remoteAddress)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
			{
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");
			}

			RemoteAddress = remoteAddress;

			string workingDirectory = Constants.GetWorkingDirectory();
			Log.Debug("Server", "Current working directory[" + workingDirectory + "]");

			// load configuration
			Configuration.SetGlobalSettings(new Configuration(workingDirectory));
			if (!Configuration.GlobalSettings.Load(serverTypeName))
			{
				// if we failed to load the file.. save a new one
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

			// initialize the DB contexts
#if UNITY_EDITOR
			string dbConfigurationPath = Path.Combine(Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory), "Development");

			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(dbConfigurationPath, false);
			//RedisDbContextFactory = new RedisDbContextFactory(dbConfigurationPath);
#else
			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(workingDirectory, false);
			//RedisDbContextFactory = new RedisDbContextFactory(workingDirectory);
#endif
			// ensure our NetworkManager exists in the scene
			if (NetworkManager == null)
			{
				NetworkManager = FindFirstObjectByType<NetworkManager>();

				if (NetworkManager == null)
				{
					throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");
				}
			}

			// initialize server behaviours
			Log.Debug("Server", "Initializing Components");

			// Load Server Details
			if (!LoadTransportServerDetails())
			{
				throw new UnityException("Server: Failed to load Server Details.");
			}

			// Ensure the KCC System is created.
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			// database factory DI
			LoginServerAuthenticator authenticator = NetworkManager.ServerManager.GetAuthenticator() as LoginServerAuthenticator;
			if (authenticator != null)
			{
				authenticator.NpgsqlDbContextFactory = NpgsqlDbContextFactory;
			}

			// initialize our server behaviours
			ServerBehaviour.InitializeOnceInternal(this, NetworkManager.ServerManager);

			Log.Debug("Server", "Initialization Complete");

			// start the local server connection
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

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
		/// Gets a component, creating and adding it if it does not exist.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

			Transport transport = NetworkManager.TransportManager.GetTransport(obj.TransportIndex);
			if (transport != null)
			{
				Log.Debug("Server", $"{serverTypeName} Local: {transport.GetServerBindAddress(IPAddressType.IPv4)}:{transport.GetPort()} Remote: {RemoteAddress}:{transport.GetPort()} - {transport.GetType().Name} {serverState}");
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(NetworkConnection conn, T broadcast, bool requireAuthentication = true, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Sending: " + typeof(T));
			conn.Broadcast(broadcast, requireAuthentication, channel);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void RegisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Registered " + typeof(T));
			NetworkManager.ServerManager.RegisterBroadcast<T>(handler, requireAuthentication);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void UnregisterBroadcast<T>(Action<NetworkConnection, T, Channel> handler, bool requireAuthentication = true) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Unregistered " + typeof(T));
			NetworkManager.ServerManager.UnregisterBroadcast<T>(handler);
		}

		/// <summary>
		/// Loads transport server details from the configuration file.
		/// </summary>
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