using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.WebTransport;
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
using KinematicCharacterController;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FishMMO.Client
{
	/// <summary>
	/// Thin orchestrator for the client. Delegates connection lifecycle to
	/// <see cref="ClientConnectionManager"/>, combat display to <see cref="ClientCombatDisplay"/>,
	/// and fog to <see cref="ClientFogManager"/>. Handles login server discovery and character
	/// lifecycle events that must live on the root GameObject.
	/// </summary>
	public class Client : MonoBehaviour
	{
		// ── Orchestrated services ───────────────────────────────────────

		/// <summary>
		/// Manages the client's connection lifecycle (connect, disconnect, reconnect).
		/// </summary>
		public ClientConnectionManager Connection { get; private set; }
		/// <summary>
		/// Displays combat-related UI elements.
		/// </summary>
		private ClientCombatDisplay combatDisplay;
		/// <summary>
		/// Manages client-side fog-of-war visibility.
		/// </summary>
		private ClientFogManager fogManager;

		// ── Login server discovery ──────────────────────────────────────

		/// <summary>
		/// Cached list of login server addresses discovered via API host probing.
		/// </summary>
		[SerializeField]
		private List<ushort> loginServerPorts;
		/// <summary>
		/// Cached list of login server addresses discovered via API host probing.
		/// </summary>
		public List<ushort> LoginServerPorts => loginServerPorts;
		/// <summary>
		/// UTC timestamp of the last successful login server address fetch.
		/// Uses DateTime.UtcNow instead of Time.realtimeSinceStartup to survive scene reloads.
		/// </summary>
		private DateTime loginServerPortsFetchedAt = DateTime.MinValue;
		/// <summary>
		/// Cached connection token from the last successful IPFetch response.
		/// </summary>
		private string cachedConnectionToken;
		/// <summary>
		/// Time-to-live in seconds for the cached login server address list.
		/// This value MUST be less than the server's connection token TTL (typically 60s)
		/// to ensure we never serve a stale list with an expired token from a previous
		/// <see cref="IPFetch"/> response. If the cache outlives the token the server
		/// will reject the handshake.
		/// </summary>
		[SerializeField]
		private float loginServerCacheTtlSeconds = 55f;
		/// <summary>
		/// Time-to-live in seconds for the cached login server address list.
		/// </summary>
		public float LoginServerCacheTtlSeconds => loginServerCacheTtlSeconds;
		/// <summary>
		/// Timeout in seconds for each login server probe request.
		/// </summary>
		[SerializeField]
		private int loginServerRequestTimeoutSeconds = 10;
		/// <summary>
		/// Timeout in seconds for each login server probe request.
		/// </summary>
		public int LoginServerRequestTimeoutSeconds => loginServerRequestTimeoutSeconds;

		/// <summary>
		/// Delay in seconds between staggering login server probe requests.
		/// Spreading probes out gives slower hosts time to respond before
		/// the next candidate is tried.
		/// </summary>
		[SerializeField]
		private float probeStaggerInterval = 0.25f;

		// ── Scene preloading ────────────────────────────────────────────

		/// <summary>
		/// List of addressable scenes to preload when entering the game world.
		/// </summary>
		[SerializeField]
		private List<AddressableSceneLoadData> worldPreloadScenes = new List<AddressableSceneLoadData>();
		/// <summary>
		/// List of addressable scenes to preload when entering the game world.
		/// </summary>
		public List<AddressableSceneLoadData> WorldPreloadScenes => worldPreloadScenes;
		/// <summary>
		/// Dictionary of world scenes currently loaded, keyed by scene handle.
		/// </summary>
		private Dictionary<int, Scene> loadedWorldScenes = new Dictionary<int, Scene>();

		// ── Events ──────────────────────────────────────────────────────

		/// <summary>
		/// Invoked when the client has successfully entered the game world after scene login.
		/// </summary>
		public event Action OnEnterGameWorld;
		/// <summary>
		/// Invoked when the client transitions from the game world back to the login screen.
		/// </summary>
		public event Action OnQuitToLogin;

		/// <summary>
		/// Backing field for <see cref="OnReconnectAttempt"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action<byte, byte> onReconnectAttempt;
		/// <summary>
		/// Backing field for <see cref="OnReconnectFailed"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action onReconnectFailed;
		/// <summary>
		/// Backing field for <see cref="OnConnectionSuccessful"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action onConnectionSuccessful;

		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectAttempt"/>.</summary>
		public event Action<byte, byte> OnReconnectAttempt
		{
			add { this.onReconnectAttempt += value; if (Connection != null) Connection.OnReconnectAttempt += value; }
			remove { this.onReconnectAttempt -= value; if (Connection != null) Connection.OnReconnectAttempt -= value; }
		}
		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectFailed"/>.</summary>
		public event Action OnReconnectFailed
		{
			add { this.onReconnectFailed += value; if (Connection != null) Connection.OnReconnectFailed += value; }
			remove { this.onReconnectFailed -= value; if (Connection != null) Connection.OnReconnectFailed -= value; }
		}
		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnConnectionSuccessful"/>.</summary>
		public event Action OnConnectionSuccessful
		{
			add { this.onConnectionSuccessful += value; if (Connection != null) Connection.OnConnectionSuccessful += value; }
			remove { this.onConnectionSuccessful -= value; if (Connection != null) Connection.OnConnectionSuccessful -= value; }
		}

		// ── References ──────────────────────────────────────────────────

		/// <summary>
		/// Reference to the FishNet NetworkManager singleton used by all client networking.
		/// </summary>
		public static NetworkManager NetworkManager;
		/// <summary>
		/// Authenticator used for login and world server authentication.
		/// </summary>
		[SerializeField]
		private ClientLoginAuthenticator loginAuthenticator;
		/// <summary>
		/// Authenticator used for login and world server authentication.
		/// </summary>
		public ClientLoginAuthenticator LoginAuthenticator => loginAuthenticator;
		/// <summary>
		/// AudioListener component used by the client for 3D audio positioning.
		/// </summary>
		[SerializeField]
		private AudioListener audioListener;
		/// <summary>
		/// AudioListener component used by the client for 3D audio positioning.
		/// </summary>
		public AudioListener AudioListener => audioListener;
		/// <summary>
		/// Optional post-boot system for client-side initialization after scene load.
		/// </summary>
		[SerializeField]
		private ClientPostbootSystem clientPostbootSystem;
		/// <summary>
		/// Optional post-boot system for client-side initialization after scene load.
		/// </summary>
		public ClientPostbootSystem ClientPostbootSystem => clientPostbootSystem;
		/// <summary>
		/// The current server connection type (None, Login, World, or Scene).
		/// </summary>
		public ServerConnectionType CurrentConnectionType => Connection?.CurrentConnectionType ?? ServerConnectionType.None;

		// ── Region display ──────────────────────────────────────────────

		/// <summary>
		/// Label used to display the current region name on the client UI.
		/// </summary>
		private UIAdvancedLabel regionNameLabel;

		// ── Lifecycle ───────────────────────────────────────────────────

		/// <summary>
		/// Unity Awake. Initializes NetworkManager, authenticator, transport, audio, UI, and event handlers.
		/// </summary>
		private void Awake()
		{
			if (!TryInitializeNetworkManager() || !TryInitializeAuthenticator() || !TryInitializeTransport())
			{ Quit(); return; }

			Application.logMessageReceived += OnLogMessage;

			if (this.audioListener == null && Camera.main != null)
				this.audioListener = Camera.main.gameObject.GetComponent<AudioListener>();

			this.clientPostbootSystem?.SetClient(this);
			UIManager.SetClient(this);
			ClientNamingSystem.Initialize(this);

			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			Connection = new ClientConnectionManager(NetworkManager);

			// Forward any event subscribers that were queued before Connection was created.
			if (this.onReconnectAttempt != null)
				foreach (var d in this.onReconnectAttempt.GetInvocationList())
					Connection.OnReconnectAttempt += (Action<byte, byte>)d;
			if (this.onReconnectFailed != null)
				foreach (var d in this.onReconnectFailed.GetInvocationList())
					Connection.OnReconnectFailed += (Action)d;
			if (this.onConnectionSuccessful != null)
				foreach (var d in this.onConnectionSuccessful.GetInvocationList())
					Connection.OnConnectionSuccessful += (Action)d;

			Connection.OnReconnectFailed += () => OnQuitToLogin?.Invoke();

			this.combatDisplay = new ClientCombatDisplay();
			this.combatDisplay.Initialize();

			this.fogManager = new ClientFogManager(this);
			this.fogManager.Initialize();

			IPlayerCharacter.OnReadPayload += OnCharacterReadPayload;
			IPlayerCharacter.OnStartLocalClient += OnCharacterStartLocal;
			IPlayerCharacter.OnStopLocalClient += OnCharacterStopLocal;
			IGuildController.OnReadID += OnGuildReadId;
			Pet.OnReadID += OnPetReadId;
			this.regionNameLabel = UIAdvancedLabel.Create("", FontStyle.Normal, null, 0, Color.magenta, 0, false, false, Vector2.zero) as UIAdvancedLabel;
			DisplayRegionNameAction.OnDisplay2DLabel += OnRegionNameDisplay;
		}

		/// <summary>
		/// Unity Update. Delegates tick processing to the Connection manager.
		/// </summary>
		private void Update() => Connection?.Update();

		/// <summary>
		/// Unity OnDestroy. Cleans up event subscriptions, managers, authenticator, connection, and settings.
		/// </summary>
		private void OnDestroy()
		{
#if UNITY_EDITOR
			PlayerInputController.MouseMode = true;
#endif
#if !UNITY_EDITOR
			try { Configuration.GlobalSettings.Save(); } catch (Exception ex) { Log.Warning("Client", $"Settings save failed: {ex.Message}"); }
#endif
			IPlayerCharacter.OnReadPayload -= OnCharacterReadPayload;
			IPlayerCharacter.OnStartLocalClient -= OnCharacterStartLocal;
			IPlayerCharacter.OnStopLocalClient -= OnCharacterStopLocal;
			IGuildController.OnReadID -= OnGuildReadId;
			Pet.OnReadID -= OnPetReadId;
			if (this.regionNameLabel != null) { Destroy(this.regionNameLabel.gameObject); this.regionNameLabel = null; }
			DisplayRegionNameAction.OnDisplay2DLabel -= OnRegionNameDisplay;
			this.audioListener = null;
			this.combatDisplay?.Shutdown();
			this.fogManager?.Shutdown();
			DeinitializeAuthenticator();
			Connection?.Shutdown();
			ClientNamingSystem.Destroy();
			UIManager.SetClient(null);
			this.clientPostbootSystem?.UnsetClient(this);
			Application.logMessageReceived -= OnLogMessage;
		}

		// ── Init helpers ────────────────────────────────────────────────

		/// <summary>
		/// Finds and initializes the NetworkManager, registering required client broadcasts.
		/// </summary>
		/// <returns>True if initialization succeeded; otherwise, false.</returns>
		private bool TryInitializeNetworkManager()
		{
			if (NetworkManager == null) NetworkManager = FindFirstObjectByType<NetworkManager>();
			if (NetworkManager == null) { Log.Error("Client", "NetworkManager not found."); return false; }
			NetworkManager.ClientManager.RegisterBroadcast<WorldSceneConnectBroadcast>(OnWorldSceneConnect);
			NetworkManager.ClientManager.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnValidatedScene);
			NetworkManager.ClientManager.RegisterBroadcast<ServerBusyBroadcast>(OnServerBusy);
			NetworkManager.ClientManager.RegisterBroadcast<DeathBroadcast>(OnDeathBroadcast);
			NetworkManager.SceneManager.OnLoadStart += OnSceneLoadStart;
			NetworkManager.SceneManager.OnLoadEnd += OnSceneLoadEnd;
			NetworkManager.SceneManager.OnUnloadEnd += OnSceneUnloadEnd;
			return true;
		}

		/// <summary>
		/// Finds and initializes the ClientLoginAuthenticator, subscribing to authentication results.
		/// </summary>
		/// <returns>True if initialization succeeded; otherwise, false.</returns>
		private bool TryInitializeAuthenticator()
		{
			if (this.loginAuthenticator == null) this.loginAuthenticator = FindFirstObjectByType<ClientLoginAuthenticator>();
			if (this.loginAuthenticator == null) { Log.Error("Client", "LoginAuthenticator not found."); return false; }
			this.loginAuthenticator.SetClient(this);
			this.loginAuthenticator.OnClientAuthenticationResult += OnAuthResult;
			return true;
		}

		/// <summary>
		/// Unsubscribes from authentication events and clears the authenticator reference.
		/// </summary>
		private void DeinitializeAuthenticator()
		{
			if (this.loginAuthenticator == null) return;
			this.loginAuthenticator.SetClient(null);
			this.loginAuthenticator.OnClientAuthenticationResult -= OnAuthResult;
		}

		/// <summary>
		/// Configures the client transport. All platforms use WebTransport (QUIC/HTTP3)
		/// via NGINX UDP stream proxy. Native: DllImport → libfishmmo_webtransport;
		/// WebGL: jslib → browser WebTransport API.
		/// </summary>
		/// <returns>True if initialization succeeded; otherwise, false.</returns>
		private bool TryInitializeTransport()
		{
			var tm = NetworkManager.TransportManager;
			if (tm == null) { Log.Error("Client", "TransportManager not found."); return false; }
			var mp = tm.GetTransport<Multipass>();
			if (mp == null) { Log.Error("Client", "Multipass not found."); return false; }
			// WebTransport (QUIC/HTTP3) for all platforms.
			mp.SetClientTransport<WebTransport>();
			return true;
		}

		// ── Public API ──────────────────────────────────────────────────

		/// <summary>
		/// Exits the application (play mode in editor, Application.Quit in builds, or WebGL key-hijack path).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#elif UNITY_WEBGL
			GetComponent<WebGLKeyHijack>()?.ClientQuit();
#else
			Application.Quit();
#endif
		}

		/// <summary>
		/// Disconnects from the game world and returns to the login screen.
		/// </summary>
		/// <param name="forceDisconnect">If true, forces an immediate disconnection.</param>
		public void QuitToLogin(bool forceDisconnect = true)
		{
			this.fogManager?.Stop();
			AddressableLoadProcessor.UnloadSceneByLabelAsync(this.worldPreloadScenes);
			StopAllCoroutines(); // after unload has been initiated so the async operation isn't cancelled
			UnloadWorldScenes();
			if (forceDisconnect) Connection?.ForceDisconnect();
			this.loginAuthenticator?.RevokeAndClearAuthToken();
			Connection?.ResetReconnectState();
			this.cachedConnectionToken = null;
			OnQuitToLogin?.Invoke();
#if UNITY_EDITOR
			PlayerInputController.MouseMode = true;
#endif
		}

		/// <summary>
		/// Connects the client to a game server at the specified port.
		/// Hostname is always <see cref="Constants.Configuration.GameHost"/>.
		/// With WebTransport, the port is a QUIC connection parameter, not a URL path.
		/// </summary>
		/// <param name="port">The server port.</param>
		/// <param name="isWorldServer">If true, marks this as a world server connection.</param>
		public void ConnectToServer(ushort port, bool isWorldServer = false)
		{
			Connection?.ConnectToServer(Constants.Configuration.GameHost, port, isWorldServer);
		}
		/// <summary>
		/// Checks whether the client connection is ready (authenticated by default).
		/// </summary>
		/// <param name="requireAuth">If true, the connection must be authenticated to be considered ready.</param>
		/// <returns>True if the connection is ready; otherwise, false.</returns>
		public bool IsConnectionReady(bool requireAuth = true) => Connection?.IsConnectionReady(requireAuth) ?? false;

		/// <summary>
		/// Checks whether the client connection is in the specified state and ready.
		/// Backward-compatible overload accepting LocalConnectionState.
		/// </summary>
		/// <param name="state">The connection state to check for.</param>
		/// <param name="requireAuth">If true, the connection must be authenticated to be considered ready.</param>
		/// <returns>True if the connection is in the specified state and ready; otherwise, false.</returns>
		public bool IsConnectionReady(LocalConnectionState state, bool requireAuth = false)
		{
			if (Connection == null || Connection.ClientState != state)
				return false;

			// State match alone is sufficient when auth is not required (e.g. checking
			// for Stopped before calling ConnectToServer).  When the caller also needs
			// authentication we delegate to the manager, which itself enforces Started.
			return !requireAuth || Connection.IsConnectionReady(requireAuthentication: true);
		}

		/// <summary>
		/// Forces an immediate disconnection from the current server.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ForceDisconnect() => Connection?.ForceDisconnect();

		/// <summary>
		/// Cancels any active reconnection attempt.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void ReconnectCancel() => Connection?.CancelReconnect();

		/// <summary>
		/// Sends a network broadcast to the server on the specified channel.
		/// </summary>
		/// <typeparam name="T">The broadcast message type.</typeparam>
		/// <param name="broadcast">The broadcast message to send.</param>
		/// <param name="channel">The network channel to use (default Reliable).</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Broadcast<T>(T broadcast, Channel channel = Channel.Reliable) where T : struct, IBroadcast
			=> NetworkManager.ClientManager.Broadcast(broadcast, channel);

		// ── Login server discovery ──────────────────────────────────────

		/// <summary>
		/// Attempts to retrieve a random login server address from the cached list.
		/// </summary>
		/// <param name="addr">The selected server address if found.</param>
		/// <returns>True if an address was available; otherwise, false.</returns>
		public bool TryGetRandomLoginServerPort(out ushort port)
		{
			if (this.loginServerPorts != null && this.loginServerPorts.Count > 0) { port = this.loginServerPorts.GetRandom(); return true; }
			port = default; return false;
		}

		/// <summary>
		/// Probes API host candidates for a login server address list. Returns cached results if within TTL.
		/// </summary>
		/// <param name="onFail">Callback invoked with an error message if all probes fail.</param>
		/// <param name="onDone">Callback invoked with the list of discovered server addresses.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLoginServerList(Action<string> onFail, Action<List<ushort>, string> onDone)
		{
			if (this.loginServerPorts != null && this.loginServerPorts.Count > 0)
			{
				double age = Math.Max(0.0, (DateTime.UtcNow - this.loginServerPortsFetchedAt).TotalSeconds);
				if (LoginServerCacheTtlSeconds <= 0 || age < LoginServerCacheTtlSeconds) { onDone?.Invoke(this.loginServerPorts, this.cachedConnectionToken); yield break; }
			}
			var candidates = ApiHostResolver.GetCandidates();
			if (candidates.Count == 0) { onFail?.Invoke("Failed to configure APIHost."); yield break; }
			float stagger = this.probeStaggerInterval;
			var pending = new List<PendingProbe>(candidates.Count);
			float lastStart = float.NegativeInfinity;
			int next = 0; string lastErr = null; List<ushort> winner = null;
			try
			{
				while (winner == null)
				{
					if (next < candidates.Count && (pending.Count == 0 || Time.realtimeSinceStartup - lastStart >= stagger))
					{
						var url = candidates[next++] + "loginserver";
						var req = UnityWebRequest.Get(url);
						req.certificateHandler = new ClientSSLCertificateHandler();
						req.redirectLimit = -1; // -1 disables redirect following (0 = unlimited in Unity)
						req.SetRequestHeader(ClientApiSigner.HeaderKey, ClientApiSigner.BuildHeaderValue(UnityWebRequest.kHttpVerbGET, url));
						if (LoginServerRequestTimeoutSeconds > 0) req.timeout = LoginServerRequestTimeoutSeconds;
						pending.Add(new PendingProbe { Request = req, Op = req.SendWebRequest() });
						lastStart = Time.realtimeSinceStartup;
					}
					bool any = false;
					for (int i = 0; i < pending.Count; i++)
					{
						if (pending[i].Request == null) continue;
						if (!pending[i].Op.isDone) { any = true; continue; }
						var done = pending[i]; pending[i] = default;
						try
						{
							if (done.Request.result != UnityWebRequest.Result.Success) { lastErr = done.Request.error; continue; }
							var parsed = JsonUtility.FromJson<ServerAddresses>(done.Request.downloadHandler.text);
							if (parsed?.Ports == null) 
							{ 
								string rawText = done.Request.downloadHandler.text;
								lastErr = "Parse failed.";
								Log.Debug("Client", $"GetLoginServerList: JSON parse failed. Raw response (truncated): {(rawText != null && rawText.Length > 200 ? rawText.Substring(0, 200) + "..." : rawText)}");
								continue; 
							}
							this.cachedConnectionToken = parsed.ConnectionToken;
							winner = parsed.Ports;
							break;
						}
						finally { done.Request.Dispose(); }
					}
					if (winner != null) break;
					if (!any && next >= candidates.Count) break;
					yield return null;
				}
				if (winner != null) { this.loginServerPorts = winner; this.loginServerPortsFetchedAt = DateTime.UtcNow; onDone?.Invoke(winner, this.cachedConnectionToken); }
				else onFail?.Invoke(lastErr ?? "Failed to reach any APIHost.");
			}
			finally
			{
				foreach (var p in pending) { try { p.Request?.Abort(); } catch { } try { p.Request?.Dispose(); } catch { } }
			}
		}

		/// <summary>
		/// Tracks an in-flight UnityWebRequest probe to a candidate API host.
		/// </summary>
		private struct PendingProbe { public UnityWebRequest Request; public UnityWebRequestAsyncOperation Op; }

		// ── Scene management ────────────────────────────────────────────

		/// <summary>
		/// Called when a scene load begins. Unloads previously loaded world scenes.
		/// </summary>
		/// <param name="args">Scene load start event arguments.</param>
		private void OnSceneLoadStart(SceneLoadStartEventArgs args) => UnloadWorldScenes();
		/// <summary>
		/// Called when a scene load completes. Tracks newly loaded scenes by handle.
		/// </summary>
		/// <param name="args">Scene load end event arguments.</param>
		private void OnSceneLoadEnd(SceneLoadEndEventArgs args) { if (args.LoadedScenes != null) foreach (var s in args.LoadedScenes) this.loadedWorldScenes[s.handle] = s; }
		/// <summary>
		/// Called when a scene unload completes. Removes unloaded scenes from the tracking dictionary.
		/// </summary>
		/// <param name="args">Scene unload end event arguments.</param>
		private void OnSceneUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (args.UnloadedScenesV2 != null)
			{
				foreach (var us in args.UnloadedScenesV2) this.loadedWorldScenes.Remove(us.Handle);
				Client.Broadcast(new ClientScenesUnloadedBroadcast { UnloadedScenes = args.UnloadedScenesV2.ToArray() });
			}
		}
		/// <summary>
		/// Unloads all currently loaded world scenes via the scene processor.
		/// </summary>
		private void UnloadWorldScenes()
		{
			var sp = NetworkManager.SceneManager.GetSceneProcessor();
			if (sp == null || this.loadedWorldScenes.Count < 1) return;
			foreach (var s in this.loadedWorldScenes.Values) sp.BeginUnloadAsync(s);
			this.loadedWorldScenes.Clear();
		}

		// ── Broadcast handlers ──────────────────────────────────────────

		/// <summary>
		/// Handles a world scene connection broadcast by connecting to the specified address and port.
		/// </summary>
		/// <param name="msg">The world scene connect message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnWorldSceneConnect(WorldSceneConnectBroadcast msg, Channel ch) { try { if (IsConnectionReady()) ConnectToServer(msg.Port); } catch (Exception ex) { Log.Error("Client", $"OnWorldSceneConnect: {ex}"); } }
		/// <summary>
		/// Handles a validated scene broadcast by beginning the world scene preload queue.
		/// </summary>
		/// <param name="msg">The validated scene message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnValidatedScene(ClientValidatedSceneBroadcast msg, Channel ch)
		{
			// Guard against duplicate broadcasts: unsubscribe first to prevent
			// multiple subscriptions that would send duplicate responses.
			AddressableLoadProcessor.OnProgressUpdate -= OnValidatedSceneProgress;
			AddressableLoadProcessor.EnqueueLoad(this.worldPreloadScenes);
			try { AddressableLoadProcessor.OnProgressUpdate += OnValidatedSceneProgress; AddressableLoadProcessor.BeginProcessQueue(); }
			catch (UnityException ex) { Log.Error("Client", "Preload failed", ex); }
		}
		/// <summary>
		/// Called during world scene preload progress. Sends a validated scene broadcast when loading completes.
		/// </summary>
		/// <param name="p">Progress value from 0 to 1.</param>
		private void OnValidatedSceneProgress(float p) 
		{ 
			// Unsubscribe on completion (p >= 1) or error/failure (p < 0).
			if (p >= 1f || p < 0f) 
			{ 
				AddressableLoadProcessor.OnProgressUpdate -= OnValidatedSceneProgress; 
				if (p >= 1f) 
					Client.Broadcast(new ClientValidatedSceneBroadcast(), Channel.Reliable); 
			} 
		}
		/// <summary>
		/// Handles a server busy broadcast by showing a dialog to the player.
		/// </summary>
		/// <param name="msg">The server busy message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnServerBusy(ServerBusyBroadcast msg, Channel ch) { if (UIManager.TryGet("UIDialogBox", out UIDialogBox d)) d.Open("Server is busy. Please try again."); }
		/// <summary>
		/// Handles a death broadcast by showing the death dialog UI.
		/// </summary>
		/// <param name="msg">The death broadcast message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnDeathBroadcast(DeathBroadcast msg, Channel ch) { try { if (UIManager.TryGetTK("UITKDeathDialog", out UITKDeathDialog d)) d.ShowDeathDialog(); } catch (Exception ex) { Log.Error("Client", $"OnDeathBroadcast: {ex}"); } }

		// ── Auth ────────────────────────────────────────────────────────

		/// <summary>
		/// Handles authentication results, updating the connection type and invoking world entry events.
		/// </summary>
		/// <param name="r">The authentication result.</param>
		private void OnAuthResult(ClientAuthenticationResult r)
		{
			switch (r)
			{
				case ClientAuthenticationResult.LoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.Login; break;
				case ClientAuthenticationResult.WorldLoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.World; break;
				case ClientAuthenticationResult.SceneLoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.Scene; DismissLoadingScreen(true); OnEnterGameWorld?.Invoke(); break;
			}
		}

		// ── Log guard ───────────────────────────────────────────────────

		/// <summary>
		/// Handles Unity log messages. Forces a disconnect on unhandled exceptions from the networking stack.
		/// </summary>
		/// <param name="condition">The log message.</param>
		/// <param name="stackTrace">The stack trace.</param>
		/// <param name="type">The log type.</param>
		private void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception) return;
			Log.Error("Client", stackTrace);
			if (Connection?.ClientState == LocalConnectionState.Stopped) return;
			if (string.IsNullOrEmpty(stackTrace) || !IsNetworkStack(stackTrace)) return;
			try { loginAuthenticator?.RevokeAndClearAuthToken(); } catch { }
			Connection?.ForceDisconnect();
		}
		/// <summary>
		/// Determines whether a stack trace originates from the networking layer.
		/// </summary>
		/// <param name="st">The stack trace to inspect.</param>
		/// <returns>True if the stack trace matches known networking namespaces; otherwise, false.</returns>
		private static bool IsNetworkStack(string st) => st.IndexOf("FishNet.Managing.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishNet.Transporting.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishNet.Serializing.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishMMO.Shared.Network.", StringComparison.Ordinal) >= 0 || st.IndexOf("FishMMO.Client.Authentication", StringComparison.Ordinal) >= 0 || st.IndexOf("LoginAuthenticator", StringComparison.Ordinal) >= 0 || st.IndexOf("SrpAuthenticator", StringComparison.Ordinal) >= 0 || st.IndexOf("ClientAuthenticator", StringComparison.Ordinal) >= 0;

		// ── App lifecycle ───────────────────────────────────────────────

		/// <summary>
		/// Unity OnApplicationPause. Does NOT revoke the auth token — revoking on pause
		/// kills WebGL tab blur / mobile sessions. Only revoke on explicit quit or logout.
		/// </summary>
		/// <param name="paused">True if the application is being paused.</param>
		private void OnApplicationPause(bool paused) { /* Auth token preserved across pause/unpause cycle. */ }
		/// <summary>
		/// Unity OnApplicationQuit. Revokes the auth token when the application exits.
		/// </summary>
		private void OnApplicationQuit() { try { this.loginAuthenticator?.RevokeAndClearAuthToken(); } catch { } }

		// ── Character / guild / pet / region handlers ──────────────────

		/// <summary>
		/// Called when a character payload is received. Sets the character's name and updates the name label.
		/// </summary>
		/// <param name="c">The player character that was read.</param>
		private void OnCharacterReadPayload(IPlayerCharacter c)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, c.ID, name => { c.GameObject.name = name; c.CharacterName = name; c.CharacterNameLower = name.ToLowerInvariant(); if (c.CharacterNameLabel != null) c.CharacterNameLabel.text = name; });
		}
		/// <summary>
		/// Called when the local character starts. Sets up input controller and UI.
		/// </summary>
		/// <param name="c">The local player character.</param>
		/// <summary>True after world entry — suppresses loading overlay re-shows.</summary>
		public static bool LoadingSuppressed { get; private set; }

		/// <summary>
		/// Hides the loading overlay and optionally suppresses future Show calls.
		/// Called on successful scene login and local character start so the player
		/// is never stuck behind a loading screen after world entry.
		/// </summary>
		public static void DismissLoadingScreen(bool suppress)
		{
			if (suppress) LoadingSuppressed = true;
			if (UIManager.TryGetTK<UITKLoadingScreen>("UITKLoadingScreen", out _)) UIManager.Hide("UITKLoadingScreen");
			if (UIManager.TryGet<UILoadingScreen>("UILoadingScreen", out _)) UIManager.Hide("UILoadingScreen");
		}

		private void OnCharacterStartLocal(IPlayerCharacter c)
		{
			UIManager.SetCharacter(c);
			var input = c.GameObject.GetComponent<PlayerInputController>() ?? c.GameObject.AddComponent<PlayerInputController>();
			input.Initialize(c);
			PlayerInputController.MouseMode = false;
			DismissLoadingScreen(true);
		}
		/// <summary>
		/// Called when the local character stops. Cleans up input, UI, fog, and destroys the character object.
		/// </summary>
		/// <param name="c">The local player character.</param>
		private void OnCharacterStopLocal(IPlayerCharacter c)
		{
			PlayerInputController.MouseMode = true;
			c.GameObject.GetComponent<PlayerInputController>()?.Deinitialize();
			UIManager.UnsetCharacter();
			if (this.regionNameLabel != null && this.regionNameLabel.gameObject != null) this.regionNameLabel.gameObject.SetActive(false);
			this.fogManager?.Stop();
			if (c?.GameObject != null) Destroy(c.GameObject);
		}
		/// <summary>
		/// Called when a guild ID is read. Sets the guild name label on the character.
		/// </summary>
		/// <param name="id">The guild ID.</param>
		/// <param name="c">The player character.</param>
		private static void OnGuildReadId(long id, IPlayerCharacter c)
		{
			if (id != 0) ClientNamingSystem.SetName(NamingSystemType.GuildName, id, name => c.SetGuildName(name));
			else c.SetGuildName(null);
		}
		/// <summary>
		/// Called when a pet ID is read. Sets the pet's label with the owner's name.
		/// </summary>
		/// <param name="ownerId">The owner's character ID.</param>
		/// <param name="pet">The pet instance.</param>
		private static void OnPetReadId(long ownerId, Pet pet)
		{
			if (pet != null && ownerId != 0) ClientNamingSystem.SetName(NamingSystemType.CharacterName, ownerId, name => { if (pet.CharacterGuildLabel) pet.CharacterGuildLabel.text = $"<{name}'s pet>"; });
		}
		/// <summary>
		/// Displays the region name label with the specified styling and lifetime parameters.
		/// </summary>
		/// <param name="text">The region name text.</param>
		/// <param name="style">The font style.</param>
		/// <param name="font">The font to use.</param>
		/// <param name="size">The font size.</param>
		/// <param name="color">The text color.</param>
		/// <param name="life">The display lifetime in seconds.</param>
		/// <param name="fade">If true, the label fades out over its lifetime.</param>
		/// <param name="up">If true, the label drifts upward.</param>
		/// <param name="offset">The screen offset position.</param>
		private void OnRegionNameDisplay(string text, FontStyle style, Font font, int size, Color color, float life, bool fade, bool up, Vector2 offset)
		{
			if (this.regionNameLabel != null) { this.regionNameLabel.gameObject.SetActive(true); this.regionNameLabel.Initialize(text, style, font, size, color, life, fade, up, offset); }
		}
	}
}