using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Bayou;
using FishNet.Managing.Scened;
using FishMMO.Shared;
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
		private enum ServerConnectionType : byte
		{
			None,
			Login,
			ConnectingToWorld,
			World,
			Scene,
		}

#if UNITY_WEBGL
		[DllImport("__Internal")]
		private static extern void ClientWebGLQuit();

		[DllImport("__Internal")]
		private static extern void AddHijackAltKeyListener();
#endif

		private LocalConnectionState clientState = LocalConnectionState.Stopped;
		private Dictionary<string, Scene> serverLoadedScenes = new Dictionary<string, Scene>();

		private byte reconnectsAttempted = 0;
		private float nextReconnect = 0;
		private bool forceDisconnect = false;

		private string lastWorldAddress = "";
		private ushort lastWorldPort = 0;

		private ServerConnectionType currentConnectionType = ServerConnectionType.None;
		public byte ReconnectAttempts = 10;
		public float ReconnectAttemptWaitTime = 5f;

		public List<ServerAddress> LoginServerAddresses;

		public event Action OnConnectionSuccessful;
		public event Action<byte, byte> OnReconnectAttempt;
		public event Action OnReconnectFailed;
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

		void Awake()
		{
#if UNITY_WEBGL
			AddHijackAltKeyListener();
#endif

			if (NetworkManager == null)
			{
				NetworkManager = FindFirstObjectByType<NetworkManager>();
				if (NetworkManager == null)
				{
					Debug.LogError("Client: NetworkManager not found.");
					Quit();
					return;
				}
			}

			// set our static NM reference... this is used for easier client broadcasts
			_networkManager = NetworkManager;

			if (LoginAuthenticator == null)
			{
				LoginAuthenticator = FindFirstObjectByType<ClientLoginAuthenticator>();
				if (LoginAuthenticator == null)
				{
					Debug.LogError("Client: LoginAuthenticator not found.");
					Quit();
					return;
				}
			}

			TransportManager transportManager = _networkManager.TransportManager;
			if (transportManager == null)
			{
				Debug.LogError("Client: TransportManager not found.");
				Quit();
				return;
			}
			Multipass multipass = transportManager.GetTransport<Multipass>();
			if (multipass == null)
			{
				Debug.LogError("Client: Multipass not found.");
				Quit();
				return;
			}
#if UNITY_WEBGL && !UNITY_EDITOR
			multipass.SetClientTransport<Bayou>();
#else
			multipass.SetClientTransport<Tugboat>();
#endif

			Application.logMessageReceived += this.Application_logMessageReceived;

			// set the UIManager Client
			UIManager.SetClient(this);

			// initialize naming service
			ClientNamingSystem.InitializeOnce(this);

			// assign the client to the Login Authenticator
			LoginAuthenticator.SetClient(this);

			// load configuration
			if (Constants.Configuration.Settings == null)
			{
				Constants.Configuration.Settings = new Configuration(Client.GetWorkingDirectory());
				if (!Constants.Configuration.Settings.Load(Configuration.DEFAULT_FILENAME + Configuration.EXTENSION))
				{
					// if we failed to load the file.. save a new one
					Constants.Configuration.Settings.Set("Version", Constants.Configuration.Version);
					Constants.Configuration.Settings.Set("Resolution Width", 1280);
					Constants.Configuration.Settings.Set("Resolution Height", 800);
					Constants.Configuration.Settings.Set("Refresh Rate", (uint)60);
					Constants.Configuration.Settings.Set("Fullscreen", false);
					Constants.Configuration.Settings.Set("ShowDamage", true);
					Constants.Configuration.Settings.Set("ShowHeals", true);
					Constants.Configuration.Settings.Set("ShowAchievementCompletion", true);
					Constants.Configuration.Settings.Set("IPFetchHost", Constants.Configuration.IPFetchHost);
#if !UNITY_EDITOR
				Constants.Configuration.Settings.Save();
#endif
				}
			}

#if !UNITY_WEBGL
			if (Constants.Configuration.Settings.TryGetInt("Resolution Width", out int width) &&
				Constants.Configuration.Settings.TryGetInt("Resolution Height", out int height) &&
				Constants.Configuration.Settings.TryGetUInt("Refresh Rate", out uint refreshRate) &&
				Constants.Configuration.Settings.TryGetBool("Fullscreen", out bool fullscreen))
			{

				Screen.SetResolution(width, height, fullscreen ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed, new RefreshRate()
				{
					numerator = refreshRate,
					denominator = 1,
				});
		}
#endif

			// Ensure the KCC System is created.
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			UnityEngine.SceneManagement.SceneManager.sceneLoaded += UnitySceneManager_OnSceneLoaded;
			UnityEngine.SceneManagement.SceneManager.sceneUnloaded += UnitySceneManager_OnSceneUnloaded;
			NetworkManager.ClientManager.OnClientConnectionState += ClientManager_OnClientConnectionState;
			NetworkManager.SceneManager.OnLoadStart += SceneManager_OnLoadStart;
			NetworkManager.SceneManager.OnLoadPercentChange += SceneManager_OnLoadPercentChange;
			NetworkManager.SceneManager.OnLoadEnd += SceneManager_OnLoadEnd;
			NetworkManager.SceneManager.OnUnloadStart += SceneManager_OnUnloadStart;
			NetworkManager.SceneManager.OnUnloadEnd += SceneManager_OnUnloadEnd;
			LoginAuthenticator.OnClientAuthenticationResult += Authenticator_OnClientAuthenticationResult;

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload += Character_OnReadPayload;
			IPlayerCharacter.OnStartLocalClient += Character_OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient += Character_OnStopLocalClient;
			IGuildController.OnReadPayload += GuildController_OnReadPayload;
			ICharacterDamageController.OnDamaged += OnDisplayDamage;
			ICharacterDamageController.OnHealed += OnDisplayHeal;
			IAchievementController.OnCompleteAchievement += OnCompleteAchievement;

			RegionNameLabel = UIAdvancedLabel.Create("", FontStyle.Normal, null, 0, Color.magenta, 0, false, false, Vector2.zero) as UIAdvancedLabel;
			RegionDisplayNameAction.OnDisplay2DLabel += RegionDisplayNameAction_OnDisplay2DLabel;
			RegionChangeFogAction.OnChangeFog += RegionChangeFogAction_OnChangeFog;
#endif
		}

		private void Update()
		{
			if (forceDisconnect ||
				reconnectsAttempted > ReconnectAttempts ||
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
				Debug.LogError($"{stackTrace}");

				ForceDisconnect();
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
#if UNITY_EDITOR
			InputManager.MouseMode = true;
#endif

#if !UNITY_SERVER
			IPlayerCharacter.OnReadPayload -= Character_OnReadPayload;
			IPlayerCharacter.OnStartLocalClient -= Character_OnStartLocalClient;
			IPlayerCharacter.OnStopLocalClient -= Character_OnStopLocalClient;
			IGuildController.OnReadPayload -= GuildController_OnReadPayload;
			ICharacterDamageController.OnDamaged -= OnDisplayDamage;
			ICharacterDamageController.OnHealed -= OnDisplayHeal;
			IAchievementController.OnCompleteAchievement -= OnCompleteAchievement;

			RegionDisplayNameAction.OnDisplay2DLabel -= RegionDisplayNameAction_OnDisplay2DLabel;
			RegionChangeFogAction.OnChangeFog -= RegionChangeFogAction_OnChangeFog;
#endif

			ClientNamingSystem.Destroy();

			Application.logMessageReceived -= this.Application_logMessageReceived;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#elif UNITY_WEBGL
			ClientWebGLQuit();
#else
			Application.Quit();
#endif
		}

		public void QuitToLogin(bool forceDisconnect = true)
		{
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

			UnloadServerLoadedScenes();
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
					// unload previous scenes
					UnloadServerLoadedScenes();

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
							OnReconnectAttempt?.Invoke(reconnectsAttempted, ReconnectAttempts);
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
			if (reconnectsAttempted < ReconnectAttempts)
			{
				if (IsAddressValid(lastWorldAddress) && lastWorldPort != 0)
				{
					++reconnectsAttempted;
					OnReconnectAttempt?.Invoke(reconnectsAttempted, ReconnectAttempts);
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
			ForceDisconnect();
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
			Debug.Log($"[Broadcast] Sending: " + typeof(T));
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

		/// <summary>
		/// IPv4 Regex, can we get IPv6 support???
		/// </summary>
		public bool IsAddressValid(string address)
		{
			const string ValidIpAddressRegex = "^(([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])\\.){3}([0-9]|[1-9][0-9]|1[0-9]{2}|2[0-4][0-9]|25[0-5])$";
			Match match = Regex.Match(address, ValidIpAddressRegex);
			return match.Success;
		}

		public IEnumerator GetLoginServerList(Action<string> onFetchFail, Action<List<ServerAddress>> onFetchComplete)
		{
			if (LoginServerAddresses != null &&
				LoginServerAddresses.Count > 0)
			{
				onFetchComplete?.Invoke(LoginServerAddresses);
			}
			else if (Constants.Configuration.Settings.TryGetString("IPFetchHost", out string ipFetchHost))
			{
				using (UnityWebRequest request = UnityWebRequest.Get(ipFetchHost + "loginserver"))
				{
					// Pick a random IPFetch Host address if available.
					string[] ipFetchServers = ipFetchHost.Split(",");
					if (ipFetchServers != null && ipFetchServers.Length > 1)
					{
						ipFetchHost = ipFetchServers.GetRandom();
					}

					request.certificateHandler = new ClientSSLCertificateHandler();

					yield return request.SendWebRequest();

					if (request.result == UnityWebRequest.Result.ConnectionError ||
						request.result == UnityWebRequest.Result.ProtocolError)
					{
						onFetchFail?.Invoke("Error: " + request.error);
					}
					else
					{
						// Parse JSON response
						string jsonResponse = request.downloadHandler.text;
						jsonResponse = "{\"addresses\":" + jsonResponse.ToString() + "}";
						ServerAddresses result = JsonUtility.FromJson<ServerAddresses>(jsonResponse);

						// Do something with the server list
						foreach (ServerAddress server in result.addresses)
						{
							Debug.Log("Client: New Login Server Address:" + server.address + ", Port: " + server.port);
						}

						// Assign our LoginServerAddresses
						LoginServerAddresses = result.addresses;

						onFetchComplete?.Invoke(result.addresses);
					}
				}
			}
			else
			{
				onFetchFail?.Invoke("Failed to configure IPFetchHost.");
			}
		}

		private void UnitySceneManager_OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			// ClientBootstrap overrides active scene if it is ever loaded.
			if (scene.name.Contains("ClientBootstrap"))
			{
				UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
			}
		}

		private void UnitySceneManager_OnSceneUnloaded(Scene scene)
		{
		}

		private void SceneManager_OnLoadStart(SceneLoadStartEventArgs args)
		{
		}

		private void UnloadServerLoadedScenes()
		{
			foreach (Scene scene in serverLoadedScenes.Values)
			{
				UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
			}
			serverLoadedScenes.Clear();

			// Test
			//Resources.UnloadUnusedAssets();
		}

		private void SceneManager_OnLoadPercentChange(SceneLoadPercentEventArgs args)
		{
		}

		private void SceneManager_OnLoadEnd(SceneLoadEndEventArgs args)
		{
			// add loaded scenes to list
			if (args.LoadedScenes != null)
			{
				foreach (Scene scene in args.LoadedScenes)
				{
					serverLoadedScenes[scene.name] = scene;
				}
			}
		}

		private void SceneManager_OnUnloadStart(SceneUnloadStartEventArgs args)
		{
		}

		private void SceneManager_OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
		}

#if !UNITY_SERVER
		#region Character
		/// <summary>
		/// This function is called when the local Character reads a payload.
		/// </summary>
		public void Character_OnReadPayload(IPlayerCharacter character)
		{
			// load the characters name from disk or request it from the server
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, character.ID, (n) =>
			{
				character.GameObject.name = n;
				character.CharacterName = n;
				character.CharacterNameLower = n.ToLower();

				if (character.CharacterNameLabel != null)
					character.CharacterNameLabel.text = n;
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

		public static void GuildController_OnReadPayload(long ID, IPlayerCharacter character)
		{
			if (ID != 0)
			{
				// Load the characters guild from disk or request it from the server.
				ClientNamingSystem.SetName(NamingSystemType.GuildName, ID, (s) =>
				{
					character.SetGuildName(s);
				});
			}
		}

		public void OnDisplayDamage(ICharacter attacker, ICharacter hitCharacter, int amount, DamageAttributeTemplate damageAttribute)
		{
			if (hitCharacter == null)
			{
				return;
			}
			if (!Constants.Configuration.Settings.TryGetBool("ShowDamage", out bool result) || !result)
			{
				return;
			}
			Vector3 displayPos = hitCharacter.Transform.position;
			IPlayerCharacter playerCharacter = hitCharacter as IPlayerCharacter;
			if (playerCharacter != null)
			{
				displayPos.y += playerCharacter.CharacterController.FullCapsuleHeight;
			}
			LabelMaker.Display3D(amount.ToString(), displayPos, damageAttribute.DisplayColor, 4.0f, 1.0f, false);
		}

		public void OnDisplayHeal(ICharacter healer, ICharacter healed, int amount)
		{
			if (healed == null)
			{
				return;
			}
			if (!Constants.Configuration.Settings.TryGetBool("ShowHeals", out bool result) || !result)
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

		public void OnCompleteAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null ||
				template == null)
			{
				return;
			}
			if (!Constants.Configuration.Settings.TryGetBool("ShowAchievementCompletion", out bool result) || !result)
			{
				return;
			}
			Vector3 displayPos = character.Transform.position;
			IPlayerCharacter playerCharacter = character as IPlayerCharacter;
			if (playerCharacter != null)
			{
				displayPos.y += playerCharacter.CharacterController.FullCapsuleHeight;
			}
			LabelMaker.Display3D("Achievement: " + template.Name + "\r\n" + tier.TierCompleteMessage, displayPos, Color.yellow, 12.0f, 4.0f, false);
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

		public void RegionChangeFogAction_OnChangeFog(bool fogEnabled, float fogChangeRate, FogMode fogMode, Color fogColor, float fogDensity, float fogStartDistance, float fogEndDistance)
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

			RenderSettings.fog = fogEnabled;

			if (!fogEnabled)
			{
				return;
			}

			RenderSettings.fogMode = fogMode;

			// If no fog lerp settings exist we should instantiate one and immediately set fog render settings.
			if (fogInitialLerpSettings == null)
			{
				fogInitialLerpSettings = new FogInitialLerpSettings();
				fogInitialLerpSettings.Initialize(fogColor, fogDensity, fogStartDistance, fogEndDistance);
				RenderSettings.fogColor = fogColor;
				RenderSettings.fogDensity = fogDensity;
				RenderSettings.fogStartDistance = fogStartDistance;
				RenderSettings.fogEndDistance = fogEndDistance;
			}

			// Assign the final lerp values for these fog settings.
			this.fogChangeRate = fogChangeRate;
			this.fogFinalColor = fogColor;
			this.fogFinalDensity = fogDensity;
			this.fogFinalStartDistance = fogStartDistance;
			this.fogFinalEndDistance = fogEndDistance;

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