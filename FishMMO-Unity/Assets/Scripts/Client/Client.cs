using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.Bayou;
using FishNet.Managing.Scened;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
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
		/// UTC time (<see cref="Time.realtimeSinceStartup"/> reference frame) at
		/// which <see cref="LoginServerAddresses"/> was last populated. Used by
		/// <see cref="GetLoginServerList"/> to invalidate stale caches after
		/// <see cref="LoginServerCacheTtlSeconds"/>. Initialized to a sentinel
		/// (<see cref="float.NegativeInfinity"/>) so the first call always fetches.
		/// </summary>
		private float loginServerAddressesFetchedAt = float.NegativeInfinity;
		/// <summary>
		/// Maximum age (seconds) of <see cref="LoginServerAddresses"/> before the
		/// next login attempt forces a fresh APIHost lookup. Without a TTL a
		/// long-running client process keeps using a stale set of login mirrors
		/// across operator migrations, masking outages and preventing rollouts.
		/// </summary>
		public float LoginServerCacheTtlSeconds = 300f;
		/// <summary>
		/// List of scenes to preload when entering the world.
		/// </summary>
		public List<AddressableSceneLoadData> WorldPreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// Maximum number of reconnect attempts allowed.
		/// </summary>
		public byte MaxReconnectAttempts = 10;
		/// <summary>
		/// Time to wait between reconnect attempts (in seconds). Used as the
		/// base for exponential backoff (see <see cref="ComputeReconnectDelay"/>).
		/// </summary>
		public float ReconnectAttemptWaitTime = 5f;
		/// <summary>
		/// Hard ceiling on the reconnect delay applied by exponential backoff.
		/// </summary>
		public float MaxReconnectDelay = 60f;
		/// <summary>
		/// Per-request timeout (seconds) applied to the <c>loginserver</c>
		/// discovery <see cref="UnityWebRequest"/>. Prevents a half-open TLS
		/// connection on a misbehaving APIHost mirror from stalling login
		/// indefinitely; on timeout we fall through to the next candidate host.
		/// </summary>
		public int LoginServerRequestTimeoutSeconds = 10;
		/// <summary>
		/// Hard upper bound (seconds) on how long
		/// <see cref="OnAwaitingConnectionReady"/> waits for the previous client
		/// connection to fully tear down before issuing a new
		/// <c>StartConnection</c>. Guards against a stuck FishNet transport state
		/// machine that never raises <see cref="LocalConnectionState.Stopped"/>.
		/// </summary>
		public float ConnectionStopTimeoutSeconds = 10f;
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
			DisplayRegionNameAction.OnDisplay2DLabel += RegionDisplayNameAction_OnDisplay2DLabel;
			ChangeFogAction.OnChangeFog += RegionChangeFogAction_OnChangeFog;
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
			NetworkManager.ClientManager.RegisterBroadcast<ServerBusyBroadcast>(OnServerBusyBroadcastReceived);

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
			NetworkManager.ClientManager.UnregisterBroadcast<ServerBusyBroadcast>(OnServerBusyBroadcastReceived);

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
		/// Handles log messages received by the application. Force-disconnects ONLY
		/// when the exception clearly originates from the networking/auth pipeline
		/// AND a connection is currently active. Unrelated managed exceptions
		/// (UI, addressables, third-party plugins, etc.) are logged but no longer
		/// tear down the network connection.
		/// </summary>
		private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception)
			{
				return;
			}

			Log.Error("Client", $"{stackTrace}");

			// If we are not currently connected (or actively connecting), a stray
			// exception cannot represent a corrupted networking state — leave the
			// rest of the app alone.
			if (clientState == LocalConnectionState.Stopped)
			{
				return;
			}

			// Only escalate to ForceDisconnect for exceptions that look like they
			// came from the network/auth/transport stack. This keeps unrelated
			// gameplay or UI errors from tearing down the session.
			if (string.IsNullOrEmpty(stackTrace) || !IsNetworkRelatedStackTrace(stackTrace))
			{
				return;
			}

			ForceDisconnect();
		}

		/// <summary>
		/// Heuristic check: does the supplied stack trace mention any of the
		/// networking, authentication, or transport subsystems whose failure
		/// should terminate the current session? Markers are deliberately
		/// scoped to the network stack itself so that exceptions thrown from
		/// gameplay broadcast handlers (which run on the network thread but
		/// represent user code) do NOT force a disconnect.
		/// </summary>
		private static bool IsNetworkRelatedStackTrace(string stackTrace)
		{
			// Specific namespaces / type names that indicate the transport,
			// FishNet client manager, or our authenticator pipeline is in a
			// corrupted state. Substrings are anchored to namespace boundaries
			// (trailing '.') wherever possible to avoid accidental matches in
			// unrelated type names.
			return stackTrace.IndexOf("FishNet.Managing.", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("FishNet.Transporting.", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("FishNet.Serializing.", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("FishMMO.Shared.Network.", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("FishMMO.Client.Authentication", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("LoginAuthenticator", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("SrpAuthenticator", StringComparison.Ordinal) >= 0
				|| stackTrace.IndexOf("ClientAuthenticator", StringComparison.Ordinal) >= 0;
		}

		/// <summary>
		/// Cleans up client resources and unregisters event handlers on destroy.
		/// </summary>
		void OnDestroy()
		{
#if UNITY_EDITOR
			PlayerInputHandler.MouseMode = true;
#endif

#if !UNITY_EDITOR
			try
			{
				// Best-effort: never let a settings-save IO error block teardown.
				Configuration.GlobalSettings.Save();
			}
			catch (Exception ex)
			{
				Log.Warning("Client", $"Failed to save global settings on shutdown: {ex.Message}");
			}
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
			DisplayRegionNameAction.OnDisplay2DLabel -= RegionDisplayNameAction_OnDisplay2DLabel;
			ChangeFogAction.OnChangeFog -= RegionChangeFogAction_OnChangeFog;
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
			// On mobile / VR the OS may freeze or kill the process at any moment after a
			// background transition without ever invoking OnApplicationQuit. Make a
			// best-effort token revocation so the cached auth token cannot outlive the
			// session if the process is silently terminated. Failure is non-fatal: the
			// underlying call handles disconnected/no-token states.
			if (isPaused)
			{
				try
				{
					LoginAuthenticator?.RevokeAndClearAuthToken();
				}
				catch { /* best effort during suspend */ }
			}
		}

		/// <summary>
		/// Final shutdown hook. Mirrors <see cref="QuitToLogin"/>'s token-revocation
		/// behaviour so that closing the game (Alt-F4, dock-quit, OS shutdown) cannot
		/// leave a valid auth token cached on disk past the process lifetime. The
		/// underlying revoke call is safe when the connection is already torn down.
		/// </summary>
		void OnApplicationQuit()
		{
			try
			{
				LoginAuthenticator?.RevokeAndClearAuthToken();
			}
			catch { /* best effort during shutdown */ }
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

			// Wipe the cached auth token on explicit logout. The token's lifetime
			// is still bounded by the server's tokenExpirationMinutes window, but
			// dropping it locally makes credential rotation effective immediately
			// and removes the token from process memory. We also send a best-effort
			// RevokeTokenBroadcast so the server marks the token revoked in the DB
			// before its TTL elapses; RevokeAndClearAuthToken zeroes the local copy
			// whether or not the broadcast can be delivered.
			if (LoginAuthenticator != null)
			{
				LoginAuthenticator.RevokeAndClearAuthToken();
			}

			reconnectsAttempted = 0;
			nextReconnect = -1;
			currentConnectionType = ServerConnectionType.None;
			lastWorldAddress = "";
			lastWorldPort = 0;

			OnQuitToLogin?.Invoke();

#if UNITY_EDITOR
			PlayerInputHandler.MouseMode = true;
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
							// wait until we can reconnect again (exponential backoff w/ jitter)
							nextReconnect = ComputeReconnectDelay(reconnectsAttempted);

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
				// Auth results handled by UI layers (UILogin, UIRegister, UICharacterSelect, UIServerSelect).
				case ClientAuthenticationResult.AccountCreated:
				case ClientAuthenticationResult.SrpVerify:
				case ClientAuthenticationResult.SrpProof:
				case ClientAuthenticationResult.InvalidUsernameOrPassword:
				case ClientAuthenticationResult.AlreadyOnline:
				case ClientAuthenticationResult.Banned:
				case ClientAuthenticationResult.ServerFull:
				case ClientAuthenticationResult.ServerBusy:
				case ClientAuthenticationResult.NoCharacterSelected:
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenRevoked:
				case ClientAuthenticationResult.AccountUnverified:
				case ClientAuthenticationResult.AccountVerified:
				case ClientAuthenticationResult.TwoFactorRequired:
				case ClientAuthenticationResult.TwoFactorInvalid:
				case ClientAuthenticationResult.TokenDecryptFailed:
					break;
			}
		}

		/// <summary>
		/// Connects to a server at the specified address and port. Optionally marks as world server.
		/// On WebGL (Bayou), rewrites the address for NGINX routing: game.fishmmo.com/ws/{port}:443.
		/// </summary>
		/// <param name="address">Server address.</param>
		/// <param name="port">Server port.</param>
		/// <param name="isWorldServer">True if connecting to a world server.</param>
		public void ConnectToServer(string address, ushort port, bool isWorldServer = false)
		{
#if UNITY_WEBGL && !UNITY_EDITOR
			// WebGL clients connect through NGINX, which terminates SSL and
			// routes wss://game.fishmmo.com/ws/{port} to the correct backend.
			// Rewrite the raw IP:port from the server into the NGINX path format.
			//
			// Security note: on the WebGL/Bayou transport TLS is handled by the
			// browser using the system CA bundle; ClientCertificatePinning does
			// NOT apply to this connection (UnityWebSocket cannot expose the leaf
			// certificate). Defence here relies on HSTS at the edge and correctly
			// configured CAA records for game.fishmmo.com.
			address = Constants.Configuration.GameHost + "/ws/" + port;
			port = 443;
#endif

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
				nextReconnect = ComputeReconnectDelay(reconnectsAttempted);
			}
			if (reconnectsAttempted < MaxReconnectAttempts)
			{
				if (Authentication.IsAddressValid(lastWorldAddress) && lastWorldPort != 0)
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
				// Drop the cached login-server list so the next login flow re-fetches
				// it via APIHost. If the world server has been permanently moved,
				// re-discovery is the only way the client can find the new host
				// without a restart.
				LoginServerAddresses = null;
				loginServerAddressesFetchedAt = float.NegativeInfinity;
				OnReconnectFailed?.Invoke();
			}
		}

		/// <summary>
		/// Computes the next reconnect delay using exponential backoff with
		/// ±25% jitter, capped at <see cref="MaxReconnectDelay"/>. Backoff
		/// avoids thundering herds against the world server when a transient
		/// outage simultaneously drops many clients.
		/// </summary>
		/// <param name="attempt">
		///   Zero-based attempt index. The first retry waits
		///   <see cref="ReconnectAttemptWaitTime"/>, the second waits roughly
		///   2× that, and so on.
		/// </param>
		private float ComputeReconnectDelay(int attempt)
		{
			float baseDelay = ReconnectAttemptWaitTime <= 0 ? 1f : ReconnectAttemptWaitTime;
			int shift = attempt < 0 ? 0 : Math.Min(attempt, 6); // clamp 2^n to avoid overflow
			float backoff = baseDelay * (1 << shift);
			if (backoff > MaxReconnectDelay)
			{
				backoff = MaxReconnectDelay;
			}
			// ±25% jitter
			float jitter = UnityEngine.Random.Range(0.75f, 1.25f);
			return backoff * jitter;
		}

		/// <summary>
		/// Coroutine that waits for connection to stop before connecting to a new server.
		/// </summary>
		/// <param name="address">Server address.</param>
		/// <param name="port">Server port.</param>
		/// <param name="isWorldServer">True if connecting to a world server.</param>
		IEnumerator OnAwaitingConnectionReady(string address, ushort port, bool isWorldServer)
		{
			// Wait until the existing connection has fully torn down, but bound the
			// wait so a transport stuck in Stopping never freezes the connect flow.
			// clientState is kept current by ClientManager_OnClientConnectionState.
			if (clientState != LocalConnectionState.Stopped)
			{
				float waitDeadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, ConnectionStopTimeoutSeconds);
				while (clientState != LocalConnectionState.Stopped &&
					   Time.realtimeSinceStartup < waitDeadline)
				{
					yield return null;
				}
				if (clientState != LocalConnectionState.Stopped)
				{
					Log.Warning("Client",
						$"Timed out after {ConnectionStopTimeoutSeconds:0.0}s waiting for previous " +
						"connection to stop; forcing teardown before reconnect.");
					NetworkManager.ClientManager.StopConnection();
					yield return null;
				}
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
			// Bounded-age cache: honour an existing list only while it is fresher
			// than LoginServerCacheTtlSeconds. Without this a long-lived client
			// retains stale mirrors across operator migrations indefinitely.
			if (LoginServerAddresses != null && LoginServerAddresses.Count > 0)
			{
				float age = Time.realtimeSinceStartup - loginServerAddressesFetchedAt;
				if (LoginServerCacheTtlSeconds <= 0 || age < LoginServerCacheTtlSeconds)
				{
					onFetchComplete?.Invoke(LoginServerAddresses);
					yield break;
				}
			}

			// Resolve the configured APIHost candidates with randomized failover order.
			List<string> candidates = ApiHostResolver.GetCandidates();
			if (candidates.Count == 0)
			{
				onFetchFail?.Invoke("Failed to configure APIHost.");
				yield break;
			}

			// Happy-Eyeballs-style staggered parallel probing: fire the first
			// candidate immediately, then a new candidate every staggerSeconds
			// until one succeeds. The first valid response wins; the rest are
			// aborted+disposed. This bounds the failover delay (no longer
			// timeout-per-host serial waits) without flooding all mirrors on
			// every healthy login.
			const float staggerSeconds = 0.25f;
			List<PendingApiCandidate> pending = new List<PendingApiCandidate>(candidates.Count);
			float lastStartedAt = float.NegativeInfinity;
			int nextToStart = 0;
			string lastError = null;
			List<ServerAddress> winner = null;

			try
			{
				while (winner == null)
				{
					// Stagger-start the next candidate if appropriate.
					if (nextToStart < candidates.Count &&
						(pending.Count == 0 || Time.realtimeSinceStartup - lastStartedAt >= staggerSeconds))
					{
						string apiHost = candidates[nextToStart++];
						string loginServerUrl = apiHost + "loginserver";
						UnityWebRequest request = UnityWebRequest.Get(loginServerUrl);
						request.certificateHandler = new ClientSSLCertificateHandler();
						// Hardening: refuse HTTP redirects. The loginserver endpoint URL is
						// authoritative; a 3xx response is either misconfiguration or MITM.
						request.redirectLimit = 0;
						// Sign the request so the gateway will accept it. See ClientApiSigner.
						request.SetRequestHeader(ClientApiSigner.HeaderKey, ClientApiSigner.BuildHeaderValue(UnityWebRequest.kHttpVerbGET, loginServerUrl));
						if (LoginServerRequestTimeoutSeconds > 0)
						{
							request.timeout = LoginServerRequestTimeoutSeconds;
						}
						UnityWebRequestAsyncOperation op = request.SendWebRequest();
						pending.Add(new PendingApiCandidate { Request = request, Operation = op, ApiHost = apiHost });
						lastStartedAt = Time.realtimeSinceStartup;
					}

					// Bail out if everything has been started and all are finished without success.
					bool anyInFlight = false;
					for (int i = 0; i < pending.Count; i++)
					{
						if (pending[i].Request == null) continue;
						if (!pending[i].Operation.isDone) { anyInFlight = true; continue; }

						// Inspect the completed request.
						PendingApiCandidate done = pending[i];
						pending[i] = new PendingApiCandidate { Request = null, Operation = null, ApiHost = done.ApiHost };
						try
						{
							string apiHostForLog = ApiHostResolver.SanitizeForLog(done.ApiHost);
							if (done.Request.result == UnityWebRequest.Result.ConnectionError)
							{
								lastError = "Connection Error: " + done.Request.error;
								Log.Debug("Client", $"APIHost {apiHostForLog} unreachable ({done.Request.error}); trying next.");
								continue;
							}
							if (done.Request.result == UnityWebRequest.Result.ProtocolError)
							{
								lastError = "Protocol Error: " + done.Request.error;
								Log.Debug("Client", $"APIHost {apiHostForLog} returned protocol error ({done.Request.error}); trying next.");
								continue;
							}
							if (done.Request.result == UnityWebRequest.Result.DataProcessingError)
							{
								lastError = "Data Processing Error: " + done.Request.error;
								Log.Debug("Client", $"APIHost {apiHostForLog} data processing error ({done.Request.error}); trying next.");
								continue;
							}

							// The server returns { "Addresses": [ { "Address": ..., "Port": ... }, ... ] }
							// in PascalCase to match the Unity ServerAddresses type directly.
							string jsonResponse = done.Request.downloadHandler.text;
							ServerAddresses parsed = JsonUtility.FromJson<ServerAddresses>(jsonResponse);
							if (parsed == null || parsed.Addresses == null)
							{
								lastError = "Failed to parse login server list.";
								Log.Debug("Client", $"APIHost {apiHostForLog} returned unparseable response; trying next.");
								continue;
							}

							foreach (ServerAddress server in parsed.Addresses)
							{
								Log.Debug("Client", $"New Login Server Address:{server.Address}, Port: {server.Port}");
							}

							winner = parsed.Addresses;
							break;
						}
						finally
						{
							done.Request.Dispose();
						}
					}

					if (winner != null) break;
					if (!anyInFlight && nextToStart >= candidates.Count) break;
					yield return null;
				}

				if (winner != null)
				{
					LoginServerAddresses = winner;
					loginServerAddressesFetchedAt = Time.realtimeSinceStartup;
					onFetchComplete?.Invoke(winner);
				}
				else
				{
					onFetchFail?.Invoke(lastError ?? "Failed to reach any configured APIHost.");
				}
			}
			finally
			{
				// Abort and dispose any still-in-flight or unread candidates.
				for (int i = 0; i < pending.Count; i++)
				{
					if (pending[i].Request != null)
					{
						try { pending[i].Request.Abort(); } catch { }
						try { pending[i].Request.Dispose(); } catch { }
					}
				}
			}
		}

		/// <summary>
		/// Bookkeeping for a single in-flight APIHost candidate inside the
		/// Happy-Eyeballs-style login-server probe. Held in a list and walked
		/// per yield to detect the first successful result.
		/// </summary>
		private struct PendingApiCandidate
		{
			public UnityWebRequest Request;
			public UnityWebRequestAsyncOperation Operation;
			public string ApiHost;
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

		/// <summary>
		/// Handler for server busy broadcast. Displays a dialog box notifying the player.
		/// </summary>
		private void OnServerBusyBroadcastReceived(ServerBusyBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiDialogBox))
			{
				uiDialogBox.Open("Server is busy. Please try again.");
			}
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
				character.CharacterNameLower = name.ToLowerInvariant();

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

			PlayerInputController playerInputController = character.GameObject.GetComponent<PlayerInputController>();
			if (playerInputController == null)
			{
				playerInputController = character.GameObject.AddComponent<PlayerInputController>();
			}
			playerInputController.Initialize(character);

			// Disable Mouse Mode by default, the character should be controllable as soon as we enter the scene.
			PlayerInputHandler.MouseMode = false;
		}

		/// <summary>
		/// This function is called when the local Character connection is stopped. This generally happens when the character is despawned or disconnected.
		/// </summary>
		public void Character_OnStopLocalClient(IPlayerCharacter character)
		{
			// Enable the mouse
			PlayerInputHandler.MouseMode = true;

			PlayerInputController playerInputController = character.GameObject.GetComponent<PlayerInputController>();
			if (playerInputController != null)
			{
				playerInputController.Deinitialize();
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

			// Display damage label above the character with float-random and fade-out effects.
			int damageEffects = 0;
			damageEffects.EnableBit(LabelEffect.FloatRandom);
			damageEffects.EnableBit(LabelEffect.FadeOut);
			LabelMaker.Display3D(amount.ToString(), displayPos, damageAttribute.DisplayColor, 2.0f, 1.0f, false, damageEffects);
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
			int healEffects = 0;
			healEffects.EnableBit(LabelEffect.FloatUp);
			healEffects.EnableBit(LabelEffect.FadeOut);
			LabelMaker.Display3D(amount.ToString(), displayPos, new TinyColor(64, 64, 255).ToUnityColor(), 4.0f, 1.0f, false, healEffects);
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
			int achieveEffects = 0;
			achieveEffects.EnableBit(LabelEffect.FadeIn);
			achieveEffects.EnableBit(LabelEffect.FadeOut);
			achieveEffects.EnableBit(LabelEffect.Bounce);
			LabelMaker.Display3D("Achievement: " + template.Name + "\r\n" + tier.TierCompleteMessage, displayPos, Color.yellow, 2.0f, 4.0f, false, achieveEffects);
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