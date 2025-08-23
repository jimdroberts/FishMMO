using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using FishNet.Managing;
using FishNet.Transporting;
using KinematicCharacterController;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Server.Core.Account;
using FishNet.Connection;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Composition root: orchestrates Core and Implementation into a running server.
	/// </summary>
	public class Server : MonoBehaviour
	{
		/// <summary>
		/// Optional override for the server's bind address.
		/// </summary>
		[Header("Overrides")]
		public string AddressOverride;

		/// <summary>
		/// Optional override for the server's bind port.
		/// </summary>
		public ushort PortOverride;

		/// <summary>
		/// Gets the core server logic instance.
		/// </summary>
		public ICoreServer CoreServer { get; private set; }

		/// <summary>
		/// Gets the network manager wrapper instance.
		/// </summary>
		public INetworkManagerWrapper NetworkWrapper { get; private set; }

		/// <summary>
		/// Gets the server address provider instance.
		/// </summary>
		public IServerAddressProvider AddressProvider { get; private set; }

		/// <summary>
		/// Gets the server configuration instance.
		/// </summary>
		public IServerConfiguration Configuration { get; private set; }

		/// <summary>
		/// Gets the account manager instance.
		/// </summary>
		public IAccountManager<NetworkConnection> AccountManager { get; private set; }

		/// <summary>
		/// Gets the server events instance.
		/// </summary>
		public IServerEvents ServerEvents { get; private set; }

		/// <summary>
		/// Unity Start method. Initializes and composes all server components.
		/// </summary>
		void Start()
		{
			Log.Debug("Server", "Server is starting...");

			NetworkManager networkManager = FindFirstObjectByType<NetworkManager>();
			if (networkManager == null)
				throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");

			Configuration = new FileServerConfiguration();
			ServerEvents = new ServerEvents();

			CoreServer = new CoreServer(Configuration, ServerEvents);
			NetworkWrapper = new FishNetNetworkWrapper(networkManager, Configuration, this);

			ServerEvents.OnLoginServerInitialized += () => Log.Debug("Server", "LoginServer initialized.");
			ServerEvents.OnWorldServerInitialized += () => Log.Debug("Server", "WorldServer initialized.");
			ServerEvents.OnSceneServerInitialized += () => Log.Debug("Server", "SceneServer initialized.");

			StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup));
		}

		/// <summary>
		/// Finalizes server setup after fetching the external IP address.
		/// </summary>
		/// <param name="remoteAddress">The external remote address of the server.</param>
		private void OnFinalizeSetup(string remoteAddress)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");

			CoreServer.Initialize(remoteAddress, gameObject.scene.name);

			AddressProvider = new ServerAddressProvider(
				NetworkWrapper.NetworkManager.TransportManager.Transport,
				AddressOverride,
				PortOverride,
				CoreServer.Address,
				CoreServer.RemoteAddress);

			NetworkWrapper.ApplyTransportConfiguration();
			NetworkWrapper.AttachLoginAuthenticator(this);
			NetworkWrapper.AttachServerConnectionStateEventHandler(ServerManager_OnServerConnectionState);

			AccountManager = new AccountManager();

			// Initialize all registered server behaviours
			ServerBehaviourRegistry.InitializeOnceInternal(this);

			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			NetworkWrapper.StartServer();

			Log.Debug("Server", "Initialization Complete");
		}

		/// <summary>
		/// Handles server connection state changes and logs address information.
		/// </summary>
		/// <param name="obj">The server connection state arguments.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs obj)
		{
			if (AddressProvider.TryGetServerIPAddress(out ServerAddress address))
			{
				Log.Debug("Server",
					$"Local: {address.Address}:{address.Port} Remote: {CoreServer.RemoteAddress}:{address.Port} - {obj.ConnectionState}");
			}
		}

		/// <summary>
		/// Quits the application or exits play mode in the Unity Editor.
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
	}
}