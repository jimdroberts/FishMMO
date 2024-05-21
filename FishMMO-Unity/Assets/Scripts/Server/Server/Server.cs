using FishNet.Connection;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using StackExchange.Redis;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Redis;
using FishMMO.Shared;
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

		public Configuration Configuration { get; private set; }
		public NpgsqlDbContextFactory NpgsqlDbContextFactory { get; private set; }
		public RedisDbContextFactory RedisDbContextFactory { get; private set; }
		public NetworkManager NetworkManager { get; private set; }
		public string RemoteAddress { get; private set; }
		public string Address { get; private set; }
		public ushort Port { get; private set; }

		public Action OnLoginServerInitialized;
		public Action OnWorldServerInitialized;
		public Action OnSceneServerInitialized;

		public ServerWindowTitleUpdater ServerWindowTitleUpdater { get; private set; }

		public bool LogToDisk = false;
		private string logFilePath;
		private DateTime startTime;

		private LocalConnectionState serverState = LocalConnectionState.Stopped;
		private string serverTypeName;

		void Start()
		{
			startTime = DateTime.UtcNow;

			// validate server type
			if (GetServerType() == ServerType.Invalid)
			{
				Server.Quit();
			}

			if (LogToDisk)
			{
				logFilePath = Path.Combine(GetWorkingDirectory(), "Logs", serverTypeName + "_DebugLog_" + startTime.ToString("yyyy-MM-dd") + ".txt");

				Application.logMessageReceived += this.Application_logMessageReceived;
			}

			Debug.Log("Server: " + serverTypeName + " is starting[" + DateTime.UtcNow + "]");

			StartCoroutine(FetchExternalIPAddress("https://checkip.amazonaws.com/", OnFinalizeSetup));
		}

		private void OnFinalizeSetup(string remoteAddress)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
			{
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");
			}

			RemoteAddress = remoteAddress;

			string workingDirectory = Server.GetWorkingDirectory();
			Debug.Log("Server: Current working directory[" + workingDirectory + "]");

			// load configuration
			Configuration = new Configuration(workingDirectory);
			if (!Configuration.Load(serverTypeName + Configuration.EXTENSION))
			{
				// if we failed to load the file.. save a new one
				Configuration.Set("ServerName", "TestName");
				Configuration.Set("MaximumClients", 4000);
				Configuration.Set("Address", "0.0.0.0");
				Configuration.Set("Port", 7781);
#if !UNITY_EDITOR
				Configuration.Save();
#endif
			}
			Debug.Log(Configuration.ToString());

			// initialize the DB contexts
#if UNITY_EDITOR
			string dbConfigurationPath = Path.Combine(Path.Combine(workingDirectory, Constants.Configuration.SetupDirectory), "Development");

			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(dbConfigurationPath, false);
			RedisDbContextFactory = new RedisDbContextFactory(dbConfigurationPath);
#else
			NpgsqlDbContextFactory = new NpgsqlDbContextFactory(workingDirectory, false);
			RedisDbContextFactory = new RedisDbContextFactory(workingDirectory);
#endif
			// ensure our NetworkManager exists in the scene
			if (NetworkManager == null)
			{
				NetworkManager = FindObjectOfType<NetworkManager>();

				if (NetworkManager == null)
				{
					throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");
				}
			}

			// initialize server behaviours
			Debug.Log("Server: Initializing Components");


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

			Debug.Log("Server: Initialization Complete");

			// start the local server connection
			if (NetworkManager.ServerManager != null && LoadTransportServerDetails())
			{
				NetworkManager.ServerManager.OnServerConnectionState += ServerManager_OnServerConnectionState;

				NetworkManager.ServerManager.StartConnection();
				
				StartCoroutine(OnAwaitingConnectionReady());
			}
			else
			{
				Server.Quit();
			}

			Debug.Log("Server: " + serverTypeName + " is running[" + DateTime.UtcNow + "]");
		}

		private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
		{
			try
			{
				// Ensure the directory exists
				string logDirectory = Path.GetDirectoryName(logFilePath);
				if (!Directory.Exists(logDirectory))
				{
					Directory.CreateDirectory(logDirectory);
				}

				// Append the log to the file
				File.AppendAllText(logFilePath, $"{type}: {condition}\r\n{stackTrace}\r\n");
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to write to log file: {e.Message}");
			}
		}

		public static string GetWorkingDirectory()
		{
#if UNITY_EDITOR
			return Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
#else
			return AppDomain.CurrentDomain.BaseDirectory;
#endif
		}

		public void OnDestroy()
		{
			if (LogToDisk)
			{
				Application.logMessageReceived -= this.Application_logMessageReceived;
			}

			RedisDbContextFactory.CloseRedis();
		}

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
			Scene scene = gameObject.scene;
			if (!scene.path.Contains("Bootstraps"))
			{
				throw new UnityException("Server: Active scene is not in the bootstraps folder.");
			}
			serverTypeName = scene.name;
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
				Debug.Log("Server: " + serverTypeName +
						  " Local:" + transport.GetServerBindAddress(IPAddressType.IPv4) + ":" + transport.GetPort() +
						  " Remote:" + RemoteAddress + ":" + transport.GetPort() + 
						  " - " + serverState);
			}
		}

		public static IEnumerator FetchExternalIPAddress(string serviceUrl, Action<string> onSuccess = null, Action<string> onError = null)
		{
			Debug.Log($"Server: Fetching Remote IP Address from \"{serviceUrl}\"");
			using (UnityWebRequest webRequest = UnityWebRequest.Get(serviceUrl))
			{
				yield return webRequest.SendWebRequest();

				if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
				{
					Debug.Log($"Server: Request error: {webRequest.error}");
					onError?.Invoke(webRequest.error);
				}
				else
				{
					string ipAddress = webRequest.downloadHandler.text;
					ipAddress = ipAddress.Replace("\r\n", "").Replace("\n", "").Trim();

					if (IsValidIPAddress(ipAddress))
					{
						Debug.Log($"Server: External IP: {ipAddress}");
						onSuccess?.Invoke(ipAddress);
					}
					else
					{
						Debug.Log("Server: Invalid IP address format.");
						onError?.Invoke("Server: Invalid IP address format.");
					}
				}
			}
		}

		public static bool IsValidIPAddress(string ipAddress)
		{
			if (string.IsNullOrEmpty(ipAddress))
				return false;

			string[] splitValues = ipAddress.Split('.');
			if (splitValues.Length != 4)
				return false;

			foreach (string item in splitValues)
			{
				if (!int.TryParse(item, out int num) || num < 0 || num > 255)
					return false;
			}

			return true;
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
			Debug.Log($"[Broadcast] Sending: " + typeof(T));
			conn.Broadcast(broadcast, requireAuthentication, channel);
		}

		/// <summary>
		/// Loads transport server details from the configuration file.
		/// </summary>
		private bool LoadTransportServerDetails()
		{
			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null &&
				Configuration.TryGetString("Address", out string address) &&
				Configuration.TryGetUShort("Port", out ushort port) &&
				Configuration.TryGetInt("MaximumClients", out int maximumClients))
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
					address = transport.GetServerBindAddress(IPAddressType.IPv4),
					port = transport.GetPort(),
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
					address = transport.GetServerBindAddress(IPAddressType.IPv6),
					port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}

		public bool TryGetServerIPAddress(out ServerAddress address)
		{
			const string LoopBack = "127.0.0.1";
			const string LocalHost = "localhost";

			Transport transport = NetworkManager.TransportManager.Transport;
			if (transport != null)
			{
				// if our assigned address is localhost, use localhost
				// otherwise try external address
				// if remote address is null we fall back to localhost
				string actualAddress = !string.IsNullOrWhiteSpace(Address) && (Address.Equals(LoopBack) || Address.Equals(LocalHost)) ? Address :
										!string.IsNullOrWhiteSpace(RemoteAddress) ? RemoteAddress : LoopBack;

				address = new ServerAddress()
				{
					address = actualAddress,
					port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}
	}
}