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
		private Dictionary<int, Scene> loadedWorldScenes = new Dictionary<int, Scene>();

		private LocalConnectionState clientState = LocalConnectionState.Stopped;
		private ServerConnectionType currentConnectionType = ServerConnectionType.None;

		private byte reconnectsAttempted = 0;
		private float nextReconnect = 0;
		private bool forceDisconnect = false;
		private string lastWorldAddress = "";
		private ushort lastWorldPort = 0;

		public string UILoadingScreenKey = "UILoadingScreen";
		public List<ServerAddress> LoginServerAddresses;
		public List<AddressableSceneLoadData> WorldPreloadScenes = new List<AddressableSceneLoadData>();
		public byte MaxReconnectAttempts = 10;
		public float ReconnectAttemptWaitTime = 5f;
		public ClientPostbootSystem ClientPostbootSystem;

		public event Action OnConnectionSuccessful;
		public event Action<byte, byte> OnReconnectAttempt;
		public event Action OnReconnectFailed;
		public event Action OnEnterGameWorld;
		public event Action OnQuitToLogin;

		public bool CanReconnect
		{
			get
			{
				return currentConnectionType == ServerConnectionType.World ||
					   currentConnectionType == ServerConnectionType.Scene;
			}
		}

		private static NetworkManager _networkManager;
		public NetworkManager NetworkManager;
		public ClientLoginAuthenticator LoginAuthenticator;

		public AudioListener AudioListener;

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
			_networkManager = NetworkManager;

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

		private void DeinitializeLoginAuthenticator()
		{
			if (LoginAuthenticator == null)
			{
				return;
			}
			LoginAuthenticator.SetClient(null);
			LoginAuthenticator.OnClientAuthenticationResult -= Authenticator_OnClientAuthenticationResult;
		}

		private bool TryInitializeTransport()
		{
			TransportManager transportManager = _networkManager.TransportManager;
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

		private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
		{
			if (type == LogType.Exception)
			{
				Log.Error("Client", $"{stackTrace}");

				ForceDisconnect();
			}
		}

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

		void OnApplicationPause(bool isPaused)
		{
			// Handle pause state here. (This is useful for VR Headsets/Android devices that suspend the application instead of exiting)
		}

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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReconnectCancel()
		{
			OnReconnectFailed?.Invoke();
			QuitToLogin();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ForceDisconnect()
		{
			forceDisconnect = true;

			// stop current connection if any
			NetworkManager.ClientManager.StopConnection();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(T broadcast, Channel channel = Channel.Reliable) where T : struct, IBroadcast
		{
			Log.Debug("Broadcast", "Sending: " + typeof(T));
			_networkManager.ClientManager.Broadcast(broadcast, channel);
		}

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
		
		private void SceneManager_OnLoadStart(SceneLoadStartEventArgs args)
		{
			// Immediately unload all previous World scenes. We can only be in one World scene at a time.
			UnloadWorldScenes();
		}

		private void SceneManager_OnLoadPercentChange(SceneLoadPercentEventArgs args)
		{
		}

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

		private void SceneManager_OnUnloadStart(SceneUnloadStartEventArgs args)
		{
		}

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
		/// Unloads all cached World Scenes loaded by the Server. This is generally only called when the player exits back to the login screen.
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

		private void OnClientWorldSceneConnectBroadcastReceived(WorldSceneConnectBroadcast msg, Channel channel)
		{
			if (IsConnectionReady())
			{
				// Connect to the scene server
				ConnectToServer(msg.Address, msg.Port);
			}
		}

		/// <summary>
		/// The server validated the client scene is valid and fully loaded. This is invoked before the clients character is spawned in the World scene.
		/// </summary>
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

		public static void GuildController_OnReadID(long ID, IPlayerCharacter character)
		{
			if (ID != 0)
			{
				// Load the characters guild from disk or request it from the server.
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

		public static void Pet_OnReadID(long ownerID, Pet pet)
		{
			if (pet != null && ownerID != 0)
			{
				// Load the characters guild from disk or request it from the server.
				ClientNamingSystem.SetName(NamingSystemType.CharacterName, ownerID, (name) =>
				{
					if (pet.CharacterGuildLabel)
					{
						pet.CharacterGuildLabel.text = $"<{name}'s pet>";
					}
				});
			}
		}

		public void CharacterDamageController_OnDamaged(ICharacter attacker, ICharacter hitCharacter, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (hitCharacter == null)
			{
				return;
			}
			if (!Configuration.GlobalSettings.TryGetBool("ShowDamage", out bool result) || !result)
			{
				return;
			}

			Vector3 displayPos = hitCharacter.Transform.position;

			float colliderHeight = 1.0f;

			Collider collider = hitCharacter.GameObject.GetComponent<Collider>();
			if (collider != null)
			{
				collider.TryGetDimensions(out colliderHeight, out float radius);
			}

			displayPos.y += colliderHeight;

			Cached3DLabel label = LabelMaker.Display3D(amount.ToString(), displayPos, damageAttribute.DisplayColor, 2.0f, 1.0f, false);

			// Start the move coroutine if provided
			if (label != null)
			{
				label.StartCoroutine(MoveLabelUpwardAndRandomly(label, 1.0f));
			}
		}

		public static IEnumerator MoveLabelUpwardAndRandomly(Cached3DLabel label, float duration, float gravity = -4.0f)
		{
			Vector3 initialPosition = label.transform.position;
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

		public void CharacterDamageController_OnHealed(ICharacter healer, ICharacter healed, int amount)
		{
			if (healed == null)
			{
				return;
			}
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

		public void AchievementController_OnCompleteAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null ||
				template == null)
			{
				return;
			}
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
		private UIAdvancedLabel RegionNameLabel;

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
		private class FogInitialLerpSettings
		{
			public Color InitialColor = Color.white;
			public float InitialDensity = 0.0f;
			public float InitialStartDistance = 0.0f;
			public float InitialEndDistance = 0.0f;

			public void Initialize(Color initialColor, float initialDensity, float initialStartDistance, float initialEndDistance)
			{
				InitialColor = initialColor;
				InitialDensity = initialDensity;
				InitialStartDistance = initialStartDistance;
				InitialEndDistance = initialEndDistance;
			}
		}

		private FogInitialLerpSettings fogInitialLerpSettings;
		private Coroutine fogLerpRoutine;

		private float fogChangeRate = 0.0f;
		private Color fogFinalColor = Color.white;
		private float fogFinalDensity = 0.0f;
		private float fogFinalStartDistance = 0.0f;
		private float fogFinalEndDistance = 0.0f;

		public void RegionChangeFogAction_OnChangeFog(FogSettings fogSettings)
		{
			// If the coroutine exists.
			if (fogLerpRoutine != null)
			{
				StopCoroutine(fogLerpRoutine);

				// If there is an existing lerp setting we will save the current render settings.
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

			// If no fog lerp settings exist we should instantiate one and immediately set fog render settings.
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