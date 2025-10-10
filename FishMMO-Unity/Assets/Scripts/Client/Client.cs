using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Bayou;
using FishNet.Managing.Scened;
using FishMMO.Shared;
using FishMMO.Logging;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using KinematicCharacterController;

namespace FishMMO.Client
{
	/// <summary>
	/// Client controls connecting to servers, 
	/// </summary>
	public class Client : MonoBehaviour
	{
		/// <summary>
		/// Dictionary of loaded world scenes, keyed by scene handle.
		/// Used to track and unload scenes when changing worlds.
		/// </summary>
		private Dictionary<int, Scene> loadedWorldScenes = new Dictionary<int, Scene>();

		/// <summary>
		/// Current local connection state of the client.
		/// </summary>
		private LocalConnectionState clientState = LocalConnectionState.Stopped;
		/// <summary>
		/// Current type of server connection (login, world, scene, etc).
		/// </summary>
		private ServerConnectionType currentConnectionType = ServerConnectionType.None;

		/// <summary>
		/// Number of reconnect attempts made.
		/// </summary>
		private byte reconnectsAttempted = 0;
		/// <summary>
		/// Time remaining until next reconnect attempt.
		/// </summary>
		private float nextReconnect = 0;
		/// <summary>
		/// If true, forces the client to disconnect from the server.
		/// </summary>
		private bool forceDisconnect = false;
		/// <summary>
		/// Last world server address used for reconnect attempts.
		/// </summary>
		private string lastWorldAddress = "";
		/// <summary>
		/// Last world server port used for reconnect attempts.
		/// </summary>
		private ushort lastWorldPort = 0;

		/// <summary>
		/// List of login server addresses available to the client.
		/// </summary>
		public List<ServerAddress> LoginServerAddresses;
		/// <summary>
		/// List of scenes to preload when entering the world.
		/// </summary>
		public List<AddressableSceneLoadData> WorldPreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// Maximum number of reconnect attempts allowed.
		/// </summary>
		public byte MaxReconnectAttempts = 10;
		/// <summary>
		/// Time to wait between reconnect attempts (in seconds).
		/// </summary>
		public float ReconnectAttemptWaitTime = 5f;
		/// <summary>
		/// Reference to the client postboot system for scene management.
		/// </summary>
		public ClientPostbootSystem ClientPostbootSystem;

		/// <summary>
		/// Event triggered when a connection to the server is successful.
		/// </summary>
		public event Action OnConnectionSuccessful;
		/// <summary>
		/// Event triggered when a reconnect attempt is made.
		/// </summary>
		public event Action<byte, byte> OnReconnectAttempt;
		/// <summary>
		/// Event triggered when reconnect attempts fail.
		/// </summary>
		public event Action OnReconnectFailed;
		/// <summary>
		/// Event triggered when entering the game world.
		/// </summary>
		public event Action OnEnterGameWorld;
		/// <summary>
		/// Event triggered when quitting to the login screen.
		/// </summary>
		public event Action OnQuitToLogin;

		/// <summary>
		/// Returns true if the client can attempt to reconnect (only in world or scene connection states).
		/// </summary>
		public bool CanReconnect
		{
			get
			{
				return currentConnectionType == ServerConnectionType.World ||
					   currentConnectionType == ServerConnectionType.Scene;
			}
		}

		/// <summary>
		/// Static reference to the network manager instance.
		/// </summary>
		public static NetworkManager NetworkManager;
		/// <summary>
		/// Reference to the login authenticator for client authentication.
		/// </summary>
		public ClientLoginAuthenticator LoginAuthenticator;

		/// <summary>
		/// Reference to the audio listener for the client.
		/// </summary>
		public AudioListener AudioListener;

		/// <summary>
		/// Initializes the client, network manager, authenticator, and other systems.
		/// </summary>
		void Awake()
		{
			if (!TryInitializeNetworkManager() ||
				!TryInitializeLoginAuthenticator() ||
				!TryInitializeTransport())
			{
				Quit();
				return;
			}
			Application.logMessageReceived += this.Application_logMessageReceived;

			if (AudioListener == null && Camera.main != null)
			{
				AudioListener = Camera.main.gameObject.GetComponent<AudioListener>();
			}

			if (ClientPostbootSystem != null)
			{
				ClientPostbootSystem.SetClient(this);
			}

			// Set the UIManager Client
			UIManager.SetClient(this);

			// Initialize naming service
			ClientNamingSystem.Initialize(this);

			// Ensure the KCC System is created.
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

#if !UNITY_WEBGL
			/*if (Configuration.GlobalSettings.TryGetInt("Resolution Width", out int width) &&
				Configuration.GlobalSettings.TryGetInt("Resolution Height", out int height) &&
				Configuration.GlobalSettings.TryGetUInt("Refresh Rate", out uint refreshRate) &&
				Configuration.GlobalSettings.TryGetBool("Fullscreen", out bool fullscreen))
			{

				Screen.SetResolution(width, height, fullscreen ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed, new RefreshRate()
				{
					numerator = refreshRate,
					denominator = 1,
				});
			}*/
#endif

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload += Character_OnReadPayload;
			IPlayerCharacter.OnStartLocalClient += Character_OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient += Character_OnStopLocalClient;

			IGuildController.OnReadID += GuildController_OnReadID;

			Pet.OnReadID += Pet_OnReadID;

			ICharacterDamageController.OnDamaged += CharacterDamageController_OnDamaged;
			ICharacterDamageController.OnHealed += CharacterDamageController_OnHealed;

			IAchievementController.OnCompleteAchievement += AchievementController_OnCompleteAchievement;

			RegionNameLabel = UIAdvancedLabel.Create("", FontStyle.Normal, null, 0, Color.magenta, 0, false, false, Vector2.zero) as UIAdvancedLabel;
			RegionDisplayNameAction.OnDisplay2DLabel += RegionDisplayNameAction_OnDisplay2DLabel;
			RegionChangeFogAction.OnChangeFog += RegionChangeFogAction_OnChangeFog;
#endif
		}

		/// <summary>
		/// Attempts to initialize the network manager and register event handlers.
		/// </summary>
		/// <returns>True if successful, false otherwise.</returns>
		private bool TryInitializeNetworkManager()
		{
			if (NetworkManager == null)
			{
				NetworkManager = FindFirstObjectByType<NetworkManager>();
				if (NetworkManager == null)
				{
					Log.Error("Client", "NetworkManager not found.");
					return false;
				}
			}

			NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			NetworkManager.ClientManager.RegisterBroadcast<WorldSceneConnectBroadcast>(OnClientWorldSceneConnectBroadcastReceived);
			NetworkManager.ClientManager.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived);

			NetworkManager.SceneManager.OnLoadStart += SceneManager_OnLoadStart;
			NetworkManager.SceneManager.OnLoadPercentChange += SceneManager_OnLoadPercentChange;
			NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			NetworkManager.SceneManager.OnUnloadStart += SceneManager_OnUnloadStart;
			NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
			return true;
		}

		/// <summary>
		/// Deinitializes the network manager and unregisters event handlers.
		/// </summary>
		private void DeinitializeNetworkManager()
		{
			NetworkManager.ClientManager.OnClientConnectionState -= ClientManager_OnClientConnectionState;
			NetworkManager.ClientManager.UnregisterBroadcast<WorldSceneConnectBroadcast>(OnClientWorldSceneConnectBroadcastReceived);
			NetworkManager.ClientManager.UnregisterBroadcast<ClientValidatedSceneBroadcast>(OnClientValidatedSceneBroadcastReceived);

			NetworkManager.SceneManager.OnLoadStart -= SceneManager_OnLoadStart;
			NetworkManager.SceneManager.OnLoadPercentChange -= SceneManager_OnLoadPercentChange;
			NetworkManager.SceneManager.OnLoadEnd -= SceneManager_OnLoadEnd;
			NetworkManager.SceneManager.OnUnloadStart -= SceneManager_OnUnloadStart;
			NetworkManager.SceneManager.OnUnloadEnd -= SceneManager_OnUnloadEnd;
		}

		/// <summary>
		/// Attempts to initialize the login authenticator and register event handlers.
		/// </summary>
		/// <returns>True if successful, false otherwise.</returns>
		private bool TryInitializeLoginAuthenticator()
		{
			if (LoginAuthenticator == null)
			{
				LoginAuthenticator = FindFirstObjectByType<ClientLoginAuthenticator>();
				if (LoginAuthenticator == null)
				{
					Log.Error("Client", "LoginAuthenticator not found.");
					return false;
				}
			}
			LoginAuthenticator.SetClient(this);
			LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;
			return true;
		}

		/// <summary>
		/// Deinitializes the login authenticator and unregisters event handlers.
		/// </summary>
		private void DeinitializeLoginAuthenticator()
		{
			if (LoginAuthenticator == null)
			{
				return;
			}
			LoginAuthenticator.SetClient(null);
			LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		/// <summary>
		/// Attempts to initialize the transport layer for client networking.
		/// </summary>
		/// <returns>True if successful, false otherwise.</returns>
		private bool TryInitializeTransport()
		{
			TransportManager transportManager = NetworkManager.TransportManager;
			if (transportManager == null)
			{
				Log.Error("Client", "TransportManager not found.");
				return false;
			}
			Multipass multipass = transportManager.GetTransport<Multipass>();
			if (multipass == null)
			{
				Log.Error("Client", "Multipass not found.");
				return false;
			}
#if UNITY_WEBGL && !UNITY_EDITOR
			multipass.SetClientTransport<Bayou>();
#else
			multipass.SetClientTransport<Tugboat>();
#endif
			return true;
		}

		/// <summary>
		/// Handles per-frame client logic, including reconnect attempts.
		/// </summary>
		private void Update()
		{
			if (forceDisconnect ||
				reconnectsAttempted > MaxReconnectAttempts ||
				clientState != LocalConnectionState.Stopped)
			{
				return;
			}

			if (nextReconnect > 0)
			{
				nextReconnect -= Time.deltaTime;

				if (nextReconnect <= 0)
				{
					OnTryReconnect();
				}
			}
		}

		/// <summary>
		/// Handles log messages received by the application, disconnects on exceptions.
		/// </summary>
		private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
		{
			if (type == LogType.Exception)
			{
				Log.Error("Client", $"{stackTrace}");

				ForceDisconnect();
			}
		}

		/// <summary>
		/// Cleans up client resources and unregisters event handlers on destroy.
		/// </summary>
		void OnDestroy()
		{
#if UNITY_EDITOR
			InputManager.MouseMode = true;
#endif

#if !UNITY_EDITOR
			Configuration.GlobalSettings.Save();
#endif

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload -= Character_OnReadPayload;
			IPlayerCharacter.OnStartLocalClient -= Character_OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient -= Character_OnStopLocalClient;

			IGuildController.OnReadID -= GuildController_OnReadID;

			Pet.OnReadID -= Pet_OnReadID;

			ICharacterDamageController.OnDamaged -= CharacterDamageController_OnDamaged;
			ICharacterDamageController.OnHealed -= CharacterDamageController_OnHealed;

			IAchievementController.OnCompleteAchievement -= AchievementController_OnCompleteAchievement;

			if (RegionNameLabel != null)
			{
				Destroy(RegionNameLabel.gameObject);
				RegionNameLabel = null;
			}
			RegionDisplayNameAction.OnDisplay2DLabel -= RegionDisplayNameAction_OnDisplay2DLabel;
			RegionChangeFogAction.OnChangeFog -= RegionChangeFogAction_OnChangeFog;
#endif

			AudioListener = null;

			DeinitializeLoginAuthenticator();
			DeinitializeNetworkManager();

			ClientNamingSystem.Destroy();

			UIManager.SetClient(null);

			if (ClientPostbootSystem != null)
			{
				ClientPostbootSystem.UnsetClient(this);
			}

			Application.logMessageReceived -= this.Application_logMessageReceived;
		}

		/// <summary>
		/// Quits the application or play mode, depending on platform.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#elif UNITY_WEBGL
			WebGLKeyHijack webGLKeyHijack = gameObject.GetComponent<WebGLKeyHijack>();
			if (webGLKeyHijack != null)
			{
				webGLKeyHijack.ClientQuit();
			}
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Handles application pause events (useful for mobile/VR platforms).
		/// </summary>
		void OnApplicationPause(bool isPaused)
		{
			// Handle pause state here. (This is useful for VR Headsets/Android devices that suspend the application instead of exiting)
		}

		/// <summary>
		/// Quits to the login screen, disconnects from server, and unloads world scenes.
		/// </summary>
		/// <param name="forceDisconnect">If true, forces disconnect from server.</param>
		public void QuitToLogin(bool forceDisconnect = true)
		{
			StopAllCoroutines();

			AddressableLoadProcessor.UnloadSceneByLabelAsync(WorldPreloadScenes);
			UnloadWorldScenes();

			if (forceDisconnect)
			{
				ForceDisconnect();
			}

			reconnectsAttempted = 0;
			nextReconnect = -1;
			currentConnectionType = ServerConnectionType.None;
			lastWorldAddress = "";
			lastWorldPort = 0;

			OnQuitToLogin?.Invoke();

#if UNITY_EDITOR
			InputManager.MouseMode = true;
#endif
		}

		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady()
		{
			return IsConnectionReady(LocalConnectionState.Started, true);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(bool requireAuthentication)
		{
			return IsConnectionReady(LocalConnectionState.Started, requireAuthentication);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState = LocalConnectionState.Started)
		{
			return IsConnectionReady(clientState, false);
		}
		/// <summary>
		/// Checks if the current connection is valid and started. Optional authentication check (Default True).
		/// </summary>
		public bool IsConnectionReady(LocalConnectionState clientState, bool requireAuthentication)
		{
			if (LoginAuthenticator == null ||
				NetworkManager == null ||
				this.clientState != clientState)
			{
				return false;
			}

			if (requireAuthentication &&
				(!NetworkManager.ClientManager.Connection.IsValid ||
				!NetworkManager.ClientManager.Connection.IsAuthenticated))
			{
				return false;
			}

			return true;
		}

		/// <summary>
		/// Handles changes in client connection state, manages reconnect logic and triggers events.
		/// </summary>
		/// <param name="args">Arguments describing the connection state change.</param>
		private void ClientManager_OnClientConnectionState(ClientConnectionStateArgs args)
		{
			clientState = args.ConnectionState;

			switch (clientState)
			{
				case LocalConnectionState.Stopped:
					if (currentConnectionType == ServerConnectionType.Login)
					{
						QuitToLogin();
					}
					else if (!forceDisconnect)
					{
						// we can reconnect to the world server and scene servers
						if (CanReconnect)
						{
							// wait until we can reconnect again
							nextReconnect = ReconnectAttemptWaitTime;

							// show the reconnect screen?
							OnReconnectAttempt?.Invoke(reconnectsAttempted, MaxReconnectAttempts);
						}
					}
					currentConnectionType = ServerConnectionType.None;
					break;
				case LocalConnectionState.Started:
					OnConnectionSuccessful?.Invoke();
					reconnectsAttempted = 0;
					nextReconnect = -1;
					forceDisconnect = false;
					break;
			}
		}

		/// <summary>
		/// Handles authentication result from the login authenticator, updates connection type and triggers events.
		/// </summary>
		/// <param name="result">The authentication result.</param>
		private void Authenticator_OnClientAuthenticationResult(ClientAuthenticationResult result)
		{
			switch (result)
			{
				case ClientAuthenticationResult.AccountCreated:
					break;
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
					break;
				case ClientAuthenticationResult.AlreadyOnline:
					break;
				case ClientAuthenticationResult.Banned:
					break;
				case ClientAuthenticationResult.LoginSuccess:
					currentConnectionType = ServerConnectionType.Login;
					break;
				case ClientAuthenticationResult.WorldLoginSuccess:
					currentConnectionType = ServerConnectionType.World;
					break;
				case ClientAuthenticationResult.SceneLoginSuccess:
					currentConnectionType = ServerConnectionType.Scene;

					OnEnterGameWorld?.Invoke();
					break;
				case ClientAuthenticationResult.ServerFull:
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Connects to a server at the specified address and port. Optionally marks as world server.
		/// </summary>
		/// <param name="address">Server address.</param>
		/// <param name="port">Server port.</param>
		/// <param name="isWorldServer">True if connecting to a world server.</param>
		public void ConnectToServer(string address, ushort port, bool isWorldServer = false)
		{
			if (isWorldServer)
			{
				currentConnectionType = ServerConnectionType.ConnectingToWorld;
			}

			// stop current connection if any
			NetworkManager.ClientManager.StopConnection();

			// connect to the server
			StartCoroutine(OnAwaitingConnectionReady(address, port, isWorldServer));
		}

		/// <summary>
		/// Attempts to reconnect to the last known world server address and port.
		/// </summary>
		public void OnTryReconnect()
		{
			if (nextReconnect < 0)
			{
				nextReconnect = ReconnectAttemptWaitTime;
			}
			if (reconnectsAttempted < MaxReconnectAttempts)
			{
				if (Constants.IsAddressValid(lastWorldAddress) && lastWorldPort != 0)
				{
					++reconnectsAttempted;
					OnReconnectAttempt?.Invoke(reconnectsAttempted, MaxReconnectAttempts);
					ConnectToServer(lastWorldAddress, lastWorldPort);
				}
			}
			else
			{
				reconnectsAttempted = 0;
				nextReconnect = -1;
				OnReconnectFailed?.Invoke();
			}
		}

		/// <summary>
		/// Coroutine that waits for connection to stop before connecting to a new server.
		/// </summary>
		/// <param name="address">Server address.</param>
		/// <param name="port">Server port.</param>
		/// <param name="isWorldServer">True if connecting to a world server.</param>
		IEnumerator OnAwaitingConnectionReady(string address, ushort port, bool isWorldServer)
		{
			// wait for the connection to the current server to stop
			while (clientState != LocalConnectionState.Stopped)
			{
				yield return new WaitForSeconds(0.1f);
			}

			if (forceDisconnect)
			{
				forceDisconnect = false;
				yield return null;
			}

			if (isWorldServer)
			{
				lastWorldAddress = address;
				lastWorldPort = port;
			}

			// connect to the next server
			NetworkManager.ClientManager.StartConnection(address, port);

			yield return null;
		}

		/// <summary>
		/// Cancels reconnect attempts and quits to login screen.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReconnectCancel()
		{
			OnReconnectFailed?.Invoke();
			QuitToLogin();
		}

		/// <summary>
		/// Forces the client to disconnect from the server.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ForceDisconnect()
		{
			forceDisconnect = true;

			// stop current connection if any
			NetworkManager.ClientManager.StopConnection();
		}

		/// <summary>
		/// Broadcasts a message to the server using the network manager.
		/// </summary>
		/// <typeparam name="T">Type of broadcast message.</typeparam>
		/// <param name="broadcast">The broadcast message.</param>
		/// <param name="channel">The network channel to use.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(T broadcast, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Sending: " + typeof(T));
			NetworkManager.ClientManager.Broadcast(broadcast, channel);
		}

		/// <summary>
		/// Attempts to get a random login server address from the available list.
		/// </summary>
		/// <param name="serverAddress">Output parameter for the selected server address.</param>
		/// <returns>True if a server address was found, false otherwise.</returns>
		public bool TryGetRandomLoginServerAddress(out ServerAddress serverAddress)
		{
			if (LoginServerAddresses != null && LoginServerAddresses.Count > 0)
			{
				// pick a random login server
				serverAddress = LoginServerAddresses.GetRandom();
				return true;
			}
			serverAddress = default;
			return false;
		}

		/// <summary>
		/// Coroutine to fetch the login server list from a remote host or configuration.
		/// </summary>
		/// <param name="onFetchFail">Callback invoked on fetch failure.</param>
		/// <param name="onFetchComplete">Callback invoked on fetch success.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLoginServerList(Action<string> onFetchFail, Action<List<ServerAddress>> onFetchComplete)
		{
			if (LoginServerAddresses != null &&
				LoginServerAddresses.Count > 0)
			{
				onFetchComplete?.Invoke(LoginServerAddresses);
			}
			else if (Configuration.GlobalSettings.TryGetString("IPFetchHost", out string ipFetchHost))
			{
				// Pick a random IPFetch Host address if available.
				string[] ipFetchServers = ipFetchHost.Split(",");
				if (ipFetchServers != null && ipFetchServers.Length > 1)
				{
					ipFetchHost = ipFetchServers.GetRandom();
				}

				using (UnityWebRequest request = UnityWebRequest.Get(ipFetchHost + "loginserver"))
				{
					request.SetRequestHeader("X-FishMMO", "Client");
					request.certificateHandler = new ClientSSLCertificateHandler();

					yield return request.SendWebRequest();

					if (request.result == UnityWebRequest.Result.ConnectionError)
					{
						onFetchFail?.Invoke("Connection Error: " + request.error);
					}
					else if (request.result == UnityWebRequest.Result.ProtocolError)
					{
						onFetchFail?.Invoke("Protocol Error: " + request.error);
					}
					else if (request.result == UnityWebRequest.Result.DataProcessingError)
					{
						onFetchFail?.Invoke("Data Processing Error: " + request.error);
					}
					else
					{
						// Parse JSON response
						string jsonResponse = request.downloadHandler.text;

						// Replace lowercase field names with PascalCase to fit our type
						jsonResponse = jsonResponse.Replace("\"address\"", "\"Address\"")
												   .Replace("\"port\"", "\"Port\"");

						jsonResponse = "{\"Addresses\":" + jsonResponse.ToString() + "}";
						ServerAddresses result = JsonUtility.FromJson<ServerAddresses>(jsonResponse);

						// Do something with the server list
						foreach (ServerAddress server in result.Addresses)
						{
							Log.Debug("Client", $"New Login Server Address:{server.Address}, Port: {server.Port}");
						}

						// Assign our LoginServerAddresses
						LoginServerAddresses = result.Addresses;

						onFetchComplete?.Invoke(result.Addresses);
					}
				}
			}
			else
			{
				onFetchFail?.Invoke("Failed to configure IPFetchHost.");
			}
		}

		/// <summary>
		/// Handler for scene load start event. Unloads previous world scenes.
		/// </summary>
		/// <param name="args">Arguments describing the scene load start.</param>
		private void SceneManager_OnLoadStart(SceneLoadStartEventArgs args)
		{
			// Immediately unload all previous World scenes. We can only be in one World scene at a time.
			UnloadWorldScenes();
		}

		/// <summary>
		/// Handler for scene load percent change event.
		/// </summary>
		/// <param name="args">Arguments describing the scene load percent change.</param>
		private void SceneManager_OnLoadPercentChange(SceneLoadPercentEventArgs args)
		{
		}

		/// <summary>
		/// Handler for scene load end event. Adds loaded scenes to cache.
		/// </summary>
		/// <param name="args">Arguments describing the scene load end.</param>
		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			if (args.LoadedScenes == null)
			{
				return;
			}
			// Add Loaded World Scenes
			foreach (Scene scene in args.LoadedScenes)
			{
				loadedWorldScenes.Add(scene.handle, scene);
			}
		}

		/// <summary>
		/// Handler for scene unload start event.
		/// </summary>
		/// <param name="args">Arguments describing the scene unload start.</param>
		private void SceneManager_OnUnloadStart(SceneUnloadStartEventArgs args)
		{
		}

		/// <summary>
		/// Handler for scene unload end event. Removes unloaded scenes from cache and notifies server.
		/// </summary>
		/// <param name="args">Arguments describing the scene unload end.</param>
		private void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (args.UnloadedScenesV2 == null)
			{
				return;
			}

			// Remove Loaded World Scenes
			foreach (UnloadedScene unloadedScene in args.UnloadedScenesV2)
			{
				loadedWorldScenes.Remove(unloadedScene.Handle);
			}

			// Notify the server that we unloaded scenes.
			Client.Broadcast(new ClientScenesUnloadedBroadcast()
			{
				UnloadedScenes = args.UnloadedScenesV2,
			});
		}

		/// <summary>
		/// Unloads all cached world scenes loaded by the server. Called when exiting to login screen.
		/// </summary>
		private void UnloadWorldScenes()
		{
			SceneProcessorBase sceneProcessor = NetworkManager.SceneManager.GetSceneProcessor();
			if (sceneProcessor == null)
			{
				return;
			}
			if (loadedWorldScenes == null || loadedWorldScenes.Count < 1)
			{
				return;
			}
			foreach (Scene scene in loadedWorldScenes.Values)
			{
				sceneProcessor.BeginUnloadAsync(scene);
			}
			loadedWorldScenes.Clear();
		}

		/// <summary>
		/// Handler for world scene connect broadcast from the server. Connects to the scene server.
		/// </summary>
		/// <param name="msg">The world scene connect broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		private void OnClientWorldSceneConnectBroadcastReceived(WorldSceneConnectBroadcast msg, Channel channel)
		{
			if (IsConnectionReady())
			{
				// Connect to the scene server
				ConnectToServer(msg.Address, msg.Port);
			}
		}

		/// <summary>
		/// Handler for validated scene broadcast from the server. Loads world preload scenes.
		/// </summary>
		/// <param name="msg">The validated scene broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		public void OnClientValidatedSceneBroadcastReceived(ClientValidatedSceneBroadcast msg, Channel channel)
		{
			AddressableLoadProcessor.EnqueueLoad(WorldPreloadScenes);
			try
			{
				AddressableLoadProcessor.OnProgressUpdate += OnClientValidatedSceneProgressUpdate;

				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Log.Error("Client", $"Failed to load preload scenes...", ex);
			}
		}

		/// <summary>
		/// Handler for progress update during validated scene loading. Broadcasts completion to server.
		/// </summary>
		/// <param name="progress">Progress value (0-1).</param>
		private void OnClientValidatedSceneProgressUpdate(float progress)
		{
			if (progress < 1.0f)
			{
				return;
			}

			AddressableLoadProcessor.OnProgressUpdate -= OnClientValidatedSceneProgressUpdate;

			Client.Broadcast(new ClientValidatedSceneBroadcast(), Channel.Reliable);
		}

#if !UNITY_SERVER
		#region Character
		/// <summary>
		/// This function is called when the local Character reads a payload.
		/// </summary>
		public void Character_OnReadPayload(IPlayerCharacter character)
		{
			// load the characters name from disk or request it from the server
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, character.ID, (name) =>
			{
				character.GameObject.name = name;
				character.CharacterName = name;
				character.CharacterNameLower = name.ToLower();

				if (character.CharacterNameLabel != null)
					character.CharacterNameLabel.text = name;
			});
		}

		/// <summary>
		/// This function is called when the local Character connection is started. This generally happens when the character is successfully spawned in the scene.
		/// </summary>
		public void Character_OnStartLocalClient(IPlayerCharacter character)
		{
			// Assign UI Character
			UIManager.SetCharacter(character);

			LocalInputController localInputController = character.GameObject.GetComponent<LocalInputController>();
			if (localInputController == null)
			{
				localInputController = character.GameObject.AddComponent<LocalInputController>();
			}
			localInputController.Initialize(character);

			// Disable Mouse Mode by default, the character should be controllable as soon as we enter the scene.
			InputManager.MouseMode = false;
		}

		/// <summary>
		/// This function is called when the local Character connection is stopped. This generally happens when the character is despawned or disconnected.
		/// </summary>
		public void Character_OnStopLocalClient(IPlayerCharacter character)
		{
			// Enable the mouse
			InputManager.MouseMode = true;

			LocalInputController localInputController = character.GameObject.GetComponent<LocalInputController>();
			if (localInputController != null)
			{
				localInputController.Deinitialize();
			}

			// Clear the UI Character
			UIManager.UnsetCharacter();

			// Ensure the region name label is disabled.
			if (RegionNameLabel != null &&
				RegionNameLabel.gameObject != null)
			{
				RegionNameLabel.gameObject.SetActive(false);
			}

			// Ensure the local character is destroyed.
			if (character != null &&
				character.GameObject != null)
			{
				Destroy(character.GameObject);
			}

			// Clean up fog routines.
			if (fogLerpRoutine != null)
			{
				StopCoroutine(fogLerpRoutine);
				fogLerpRoutine = null;
			}
			fogInitialLerpSettings = null;
		}

		/// <summary>
		/// Handles guild ID assignment for a character, loads guild name from disk or requests from server.
		/// </summary>
		/// <param name="ID">Guild ID to resolve.</param>
		/// <param name="character">The character to assign the guild name to.</param>
		public static void GuildController_OnReadID(long ID, IPlayerCharacter character)
		{
			if (ID != 0)
			{
				// Load the character's guild name from disk or request from the server.
				ClientNamingSystem.SetName(NamingSystemType.GuildName, ID, (name) =>
				{
					character.SetGuildName(name);
				});
			}
			else
			{
				character.SetGuildName(null);
			}
		}

		/// <summary>
		/// Handles pet owner ID assignment, loads owner's name from disk or requests from server.
		/// </summary>
		/// <param name="ownerID">Owner's character ID.</param>
		/// <param name="pet">The pet to assign the owner's name to.</param>
		public static void Pet_OnReadID(long ownerID, Pet pet)
		{
			if (pet != null && ownerID != 0)
			{
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, ownerID, (name) =>
				{
					if (pet.CharacterGuildLabel)
					{
						pet.CharacterGuildLabel.text = $"<{name}'s pet>";
					}
				});
			}
		}

		/// <summary>
		/// Handles damage events for a character, displays damage label above the character.
		/// </summary>
		/// <param name="attacker">The character dealing damage.</param>
		/// <param name="hitCharacter">The character receiving damage.</param>
		/// <param name="amount">Amount of damage dealt.</param>
		/// <param name="damageAttribute">Damage attribute template for color and type.</param>
		public void CharacterDamageController_OnDamaged(ICharacter attacker, ICharacter hitCharacter, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (hitCharacter == null)
			{
				return;
			}
			// Only show damage if enabled in configuration.
			if (!Configuration.GlobalSettings.TryGetBool("ShowDamage", out bool result) || !result)
			{
				return;
			}

			Vector3 displayPos = hitCharacter.Transform.position;

			float colliderHeight = 1.0f;

			// Try to get the collider height for proper label placement.
			Collider collider = hitCharacter.GameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.TryGetDimensions(out colliderHeight, out float radius);
			}

			displayPos.y += colliderHeight;

			// Display damage label above the character.
			Cached3DLabel label = LabelMaker.Display3D(amount.ToString(), displayPos, damageAttribute.DisplayColor, 2.0f, 1.0f, false);

			// Start the move coroutine if provided
			if (label != null)
			{
				label.StartCoroutine(MoveLabelUpwardAndRandomly(label, 1.0f));
			}
		}

		/// <summary>
		/// Coroutine to move a damage label upward and randomly for a short duration, simulating a floating effect.
		/// </summary>
		/// <param name="label">The label to move.</param>
		/// <param name="duration">How long the label should move.</param>
		/// <param name="gravity">Gravity applied to the label's movement (default -4.0f).</param>
		/// <returns>Coroutine enumerator.</returns>
		public static IEnumerator MoveLabelUpwardAndRandomly(Cached3DLabel label, float duration, float gravity = -4.0f)
		{
			Vector3 initialPosition = label.transform.position;
			// Pick a random direction for the label to float, always moving up initially.
			Vector3 randomDirection = new Vector3(
				UnityEngine.Random.Range(-1f, 1f),  // Random X direction
				1f,                                 // Always moving up initially
				UnityEngine.Random.Range(-1f, 1f)   // Random Z direction
			).normalized;

			Vector3 velocity = randomDirection * 2f; // Initial velocity in the random direction
			float elapsedTime = 0f;

			while (elapsedTime < duration)
			{
				float t = elapsedTime / duration;

				// Apply gravity (simulating gravity by modifying the Y component of the velocity)
				velocity.y += gravity * Time.deltaTime;

				// Update position based on the velocity
				label.transform.position += velocity * Time.deltaTime;

				elapsedTime += Time.deltaTime;
				yield return null;
			}

			// Ensure it reaches the final position, just in case the gravity had too much effect
			label.transform.position = initialPosition + randomDirection * 2f;
		}

		/// <summary>
		/// Handles heal events for a character, displays heal label above the character.
		/// </summary>
		/// <param name="healer">The character performing the heal.</param>
		/// <param name="healed">The character being healed.</param>
		/// <param name="amount">Amount of healing.</param>
		public void CharacterDamageController_OnHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healed == null)
			{
				return;
			}
			// Only show heals if enabled in configuration.
			if (!Configuration.GlobalSettings.TryGetBool("ShowHeals", out bool result) || !result)
			{
				return;
			}
			Vector3 displayPos = healed.Transform.position;
			IPlayerCharacter playerCharacter = healed as IPlayerCharacter;
			if (playerCharacter != null)
			{
				displayPos.y += playerCharacter.CharacterController.FullCapsuleHeight;
			}
			LabelMaker.Display3D(amount.ToString(), displayPos, new TinyColor(64, 64, 255).ToUnityColor(), 4.0f, 1.0f, false);
		}

		/// <summary>
		/// Handles achievement completion events, displays achievement label above the character.
		/// </summary>
		/// <param name="character">The character completing the achievement.</param>
		/// <param name="template">Achievement template data.</param>
		/// <param name="tier">Achievement tier completed.</param>
		public void AchievementController_OnCompleteAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null ||
				template == null)
			{
				return;
			}
			// Only show achievement completion if enabled in configuration.
			if (!Configuration.GlobalSettings.TryGetBool("ShowAchievementCompletion", out bool result) || !result)
			{
				return;
			}
			Vector3 displayPos = character.Transform.position;
			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter != null)
			{
				displayPos.y += playerCharacter.CharacterController.FullCapsuleHeight;
			}
			LabelMaker.Display3D("Achievement: " + template.Name + "\r\n" + tier.TierCompleteMessage, displayPos, Color.yellow, 2.0f, 4.0f, false);
		}
		#endregion

		#region RegionNameDisplay
		/// <summary>
		/// Label used to display region names in the UI.
		/// </summary>
		private UIAdvancedLabel RegionNameLabel;

		/// <summary>
		/// Displays a 2D label for region names in the UI.
		/// </summary>
		/// <param name="text">Text to display.</param>
		/// <param name="style">Font style.</param>
		/// <param name="font">Font to use.</param>
		/// <param name="fontSize">Font size.</param>
		/// <param name="color">Text color.</param>
		/// <param name="lifeTime">How long the label should be visible.</param>
		/// <param name="fadeColor">Whether the label should fade out.</param>
		/// <param name="increaseY">Whether to increase Y position for stacking.</param>
		/// <param name="pixelOffset">Pixel offset for label placement.</param>
		public void RegionDisplayNameAction_OnDisplay2DLabel(string text, FontStyle style, Font font, int fontSize, Color color, float lifeTime, bool fadeColor, bool increaseY, Vector2 pixelOffset)
		{
			if (RegionNameLabel != null)
			{
				RegionNameLabel.gameObject.SetActive(true);
				RegionNameLabel.Initialize(text, style, font, fontSize, color, lifeTime, fadeColor, increaseY, pixelOffset);
			}
		}
		#endregion

		#region Fog
		/// <summary>
		/// Stores the initial fog settings for smooth transitions.
		/// </summary>
		private class FogInitialLerpSettings
		{
			/// <summary>
			/// Initial fog color before transition.
			/// </summary>
			public Color InitialColor = Color.white;
			/// <summary>
			/// Initial fog density before transition.
			/// </summary>
			public float InitialDensity = 0.0f;
			/// <summary>
			/// Initial fog start distance before transition.
			/// </summary>
			public float InitialStartDistance = 0.0f;
			/// <summary>
			/// Initial fog end distance before transition.
			/// </summary>
			public float InitialEndDistance = 0.0f;

			/// <summary>
			/// Initializes the initial fog settings for a transition.
			/// </summary>
			/// <param name="initialColor">Initial fog color.</param>
			/// <param name="initialDensity">Initial fog density.</param>
			/// <param name="initialStartDistance">Initial fog start distance.</param>
			/// <param name="initialEndDistance">Initial fog end distance.</param>
			public void Initialize(Color initialColor, float initialDensity, float initialStartDistance, float initialEndDistance)
			{
				InitialColor = initialColor;
				InitialDensity = initialDensity;
				InitialStartDistance = initialStartDistance;
				InitialEndDistance = initialEndDistance;
			}
		}

		/// <summary>
		/// Stores the initial fog settings for lerp transitions.
		/// </summary>
		private FogInitialLerpSettings fogInitialLerpSettings;
		/// <summary>
		/// Reference to the running fog lerp coroutine.
		/// </summary>
		private Coroutine fogLerpRoutine;

		/// <summary>
		/// Target fog change rate for transitions.
		/// </summary>
		private float fogChangeRate = 0.0f;
		/// <summary>
		/// Target fog color for transitions.
		/// </summary>
		private Color fogFinalColor = Color.white;
		/// <summary>
		/// Target fog density for transitions.
		/// </summary>
		private float fogFinalDensity = 0.0f;
		/// <summary>
		/// Target fog start distance for transitions.
		/// </summary>
		private float fogFinalStartDistance = 0.0f;
		/// <summary>
		/// Target fog end distance for transitions.
		/// </summary>
		private float fogFinalEndDistance = 0.0f;

		/// <summary>
		/// Handles fog change events, smoothly transitions fog settings using a coroutine.
		/// </summary>
		/// <param name="fogSettings">The new fog settings to apply.</param>
		public void RegionChangeFogAction_OnChangeFog(FogSettings fogSettings)
		{
			// If the coroutine exists, stop it and save current render settings for smooth transition.
			if (fogLerpRoutine != null)
			{
				StopCoroutine(fogLerpRoutine);

				// Save current render settings for lerp if available.
				if (fogInitialLerpSettings != null)
				{
					fogInitialLerpSettings.Initialize(RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance);
				}

				fogLerpRoutine = null;
			}

			RenderSettings.fog = fogSettings.Enabled;

			if (!fogSettings.Enabled)
			{
				return;
			}

			RenderSettings.fogMode = fogSettings.Mode;

			// If no fog lerp settings exist, instantiate and set immediately.
			if (fogInitialLerpSettings == null)
			{
				fogInitialLerpSettings = new FogInitialLerpSettings();
				fogInitialLerpSettings.Initialize(fogSettings.Color, fogSettings.Density, fogSettings.StartDistance, fogSettings.EndDistance);
				RenderSettings.fogColor = fogSettings.Color;
				RenderSettings.fogDensity = fogSettings.Density;
				RenderSettings.fogStartDistance = fogSettings.StartDistance;
				RenderSettings.fogEndDistance = fogSettings.EndDistance;
			}

			// Assign the final lerp values for these fog settings.
			this.fogChangeRate = fogSettings.ChangeRate;
			this.fogFinalColor = fogSettings.Color;
			this.fogFinalDensity = fogSettings.Density;
			this.fogFinalStartDistance = fogSettings.StartDistance;
			this.fogFinalEndDistance = fogSettings.EndDistance;

			fogLerpRoutine = StartCoroutine(FogLerp());
		}

		/// <summary>
		/// Coroutine to smoothly interpolate fog settings over time for visual transitions.
		/// </summary>
		/// <returns>Coroutine enumerator.</returns>
		IEnumerator FogLerp()
		{
			for (float t = 0.01f; t < fogChangeRate; t += 0.01f)
			{
				float lerpT = t / fogChangeRate;

				RenderSettings.fogColor = Color.Lerp(fogInitialLerpSettings.InitialColor, fogFinalColor, lerpT);
				RenderSettings.fogDensity = Mathf.Lerp(fogInitialLerpSettings.InitialDensity, fogFinalDensity, lerpT);
				RenderSettings.fogStartDistance = Mathf.Lerp(fogInitialLerpSettings.InitialStartDistance, fogFinalStartDistance, lerpT);
				RenderSettings.fogEndDistance = Mathf.Lerp(fogInitialLerpSettings.InitialEndDistance, fogFinalEndDistance, lerpT);

				yield return null;
			}
		}
		#endregion
#endif
	}
}