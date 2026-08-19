using FishNet.Transporting;
using FishNet.Broadcast;
using FishNet.Managing;

using FishNet.Transporting.Multipass;
using FishNet.Transporting.WebTransport;
using FishNet.Managing.Scened;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
		/// Cached connection token from the last successful IPFetch response.
		/// </summary>
		/// <remarks>
		/// Handed out at most once by <see cref="TakeConnectionToken"/>, then cleared, so the
		/// next connect attempt re-probes for a fresh one. The token itself is stateless and
		/// stays valid for its 60-second expiry rather than being consumed server-side, but it
		/// cannot simply be cached and reused: <see cref="ClientLoginAuthenticator.ConnectionToken"/>
		/// is nulled the moment it is sent, so a retry would otherwise be served a token that
		/// has already outlived part of its short window. The server rejects an expired token
		/// before checking credentials, which presents as a silent login failure that only a
		/// client restart clears. This single-use rule is why
		/// <see cref="GetLoginServerList"/> cannot cache.
		/// </remarks>
		private string cachedConnectionToken;
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
		private Action<int, int> onReconnectAttempt;
		/// <summary>
		/// Backing field for <see cref="OnReconnectFailed"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action onReconnectFailed;
		/// <summary>
		/// Backing field for <see cref="OnReconnectPending"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action onReconnectPending;
		/// <summary>
		/// Stored delegate for the OnReconnectFailed -> OnQuitToLogin forwarding subscription.
		/// Must be held as a field so it can be unsubscribed in <see cref="OnDestroy"/>.
		/// </summary>
		/// <summary>
		/// Delegate that forwards OnReconnectFailed to OnQuitToLogin.
		/// Initialized in <see cref="Awake"/> because field initializers cannot reference instance members.
		/// </summary>
		private Action onReconnectFailedQuitToLogin;
		/// <summary>
		/// Backing field for <see cref="OnConnectionSuccessful"/>.
		/// Accumulates subscribers even before <see cref="Connection"/> is created.
		/// </summary>
		private Action onConnectionSuccessful;
		/// <summary>
		/// True while a world-scene preload batch is in flight, so a repeated
		/// <c>ClientValidatedSceneBroadcast</c> from the server does not start a second
		/// preload and send a duplicate acknowledgement.
		/// </summary>
		private bool isPreloadingWorldScenes;
		/// <summary>
		/// The world-scene preload batch this client is currently waiting on, or null.
		/// </summary>
		/// <remarks>
		/// The batch outlives the session that started it: <see cref="AddressableLoadProcessor"/>
		/// keeps draining after a quit-to-login, and a batch always completes rather than being
		/// cancelled. Without an identity check, that late completion acknowledged scene
		/// validation on whatever connection happened to be current — most damagingly the next
		/// login's scene server, where it burns the validated-scene rate-limit window and makes
		/// the real acknowledgement get dropped, leaving the client on a loading screen until
		/// the server's 90s scene handshake timeout disconnects it.
		/// </remarks>
		private AddressableLoadBatch worldPreloadBatch;

		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectAttempt"/>.</summary>
		public event Action<int, int> OnReconnectAttempt
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
		/// <summary>Forwarded to <see cref="ClientConnectionManager.OnReconnectPending"/>.</summary>
		public event Action OnReconnectPending
		{
			add { this.onReconnectPending += value; if (Connection != null) Connection.OnReconnectPending += value; }
			remove { this.onReconnectPending -= value; if (Connection != null) Connection.OnReconnectPending -= value; }
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
		/// Initializes NetworkManager, authenticator, transport, audio, UI, and event handlers.
		/// This starts the primary client behaviour.
		/// </summary>
		public void Initialize()
		{
			if (!TryInitializeNetworkManager() || !TryInitializeAuthenticator() || !TryInitializeTransport())
			{ Quit(); return; }

			Application.logMessageReceived += OnLogMessage;

			// Validate configuration: loginServerRequestTimeoutSeconds must be at least 1.
			if (loginServerRequestTimeoutSeconds < 1)
			{
				Log.Warning("Client",
					$"loginServerRequestTimeoutSeconds ({loginServerRequestTimeoutSeconds}) is too low. Defaulting to 10.");
				loginServerRequestTimeoutSeconds = 10;
			}

			if (this.audioListener == null && Camera.main != null)
				this.audioListener = Camera.main.gameObject.GetComponent<AudioListener>();

			this.clientPostbootSystem?.SetClient(this);
			UIManager.SetClient(this);
			ClientNamingSystem.Initialize(this);

			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			Connection = new ClientConnectionManager(NetworkManager);
			Connection.EnsureConnectionToken = EnsureConnectionTokenRoutine;

			/* Exhausting the reconnect attempts must run the full quit-to-login teardown, not
			 * just raise the event that repaints the UI.
			 *
			 * Raising OnQuitToLogin alone put the login panels back on screen while the client
			 * was still in its world state: world scenes loaded, fog manager running, the auth
			 * token unrevoked, the reconnect manager still holding the dead world address, and
			 * — most visibly — LoadingSuppressed still latched, so the next login ran its whole
			 * world entry with no loading screen at all. QuitToLogin raises the same event at
			 * the end, so subscribers still see exactly one notification.
			 *
			 * No recursion: QuitToLogin reaches the connection manager through ForceDisconnect
			 * and ResetReconnectState, neither of which raises OnReconnectFailed. */
			this.onReconnectFailedQuitToLogin = () => QuitToLogin();

			// Forward any event subscribers that were queued before Connection was created.
			if (this.onReconnectAttempt != null)
				foreach (var d in this.onReconnectAttempt.GetInvocationList())
					Connection.OnReconnectAttempt += (Action<int, int>)d;
			if (this.onReconnectFailed != null)
				foreach (var d in this.onReconnectFailed.GetInvocationList())
					Connection.OnReconnectFailed += (Action)d;
			if (this.onReconnectPending != null)
				foreach (var d in this.onReconnectPending.GetInvocationList())
					Connection.OnReconnectPending += (Action)d;
			if (this.onConnectionSuccessful != null)
				foreach (var d in this.onConnectionSuccessful.GetInvocationList())
					Connection.OnConnectionSuccessful += (Action)d;

			Connection.OnReconnectFailed += this.onReconnectFailedQuitToLogin;

			/* A disconnect notice describes one session. Reaching Started means whatever it
			 * warned about has been recovered from — the reconnect worked, or the hop to the
			 * next server succeeded — so holding it would surface a stale, contradictory
			 * explanation the next time the player quits to login for an unrelated reason. */
			Connection.OnConnectionSuccessful += () => this.pendingDisconnectNotice = null;

			// When a non-reconnectable connection fails (e.g. login server unreachable),
			// invalidate the cached login server list so the next attempt re-fetches from
			// IPFetch instead of retrying the same potentially dead server/port.
			//
			// This used to fire on a healthy Login→World hop too, because ConnectToServer's
			// deliberate teardown was indistinguishable from a drop — which is what made the
			// message read as a failure on the success path. ClientConnectionManager now marks
			// that teardown with stoppingForConnect and skips this event for it, so reaching
			// here again means a real failed attempt and Info is the honest level.
			Connection.OnConnectionAttemptFailed += () =>
			{
				if (this.loginServerPorts != null)
				{
					Log.Info("Client", "Login server connection attempt failed — invalidating cached server list.");
					this.loginServerPorts = null;
					this.cachedConnectionToken = null;
				}

				/* Return to the login screen. This event fires only for a connection that
				 * stopped without being reconnectable and without being torn down on purpose —
				 * in practice, losing the login server. Nothing used to act on it, and no login
				 * panel watches for it either: UICharacterSelect hides itself on any Stopped,
				 * UILogin only clears its handshake text, and neither shows anything again. A
				 * client kicked (or timed out, or caught by a login-server restart) while on
				 * character select was therefore left staring at an empty scene with no panel
				 * and no button, recoverable only by restarting the client.
				 *
				 * QuitToLogin is the established path back and every panel already implements
				 * OnQuitToLogin correctly, so routing here reuses it rather than teaching each
				 * panel a second recovery route. Deliberate teardowns (server hops,
				 * ForceDisconnect from an auth-error dialog) never raise this event, so it
				 * cannot interrupt a healthy Login -> World transition. */
				QuitToLogin(forceDisconnect: false);
			};

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
		private void Update()
		{
			Connection?.Update();

			TickDeathDialogFallback();
			
			if (pendingErrorStackTrace != null)
			{
				string stackTrace = pendingErrorStackTrace;
				pendingErrorStackTrace = null;
				HandleNetworkStackException(stackTrace);
			}
		}

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
			if (Connection != null)
			{
				Connection.OnReconnectFailed -= this.onReconnectFailedQuitToLogin;
			}
			Connection?.Shutdown();
			// Null guard: NetworkManager may already be destroyed during teardown
			// (OnDestroy can fire after scene unload).  The null-conditional ?.
			// prevents NRE on NetworkManager, and the != null check on SceneManager
			// ensures we only unsubscribe if the SceneManager is still live.
			if (NetworkManager?.SceneManager != null)
			{
				NetworkManager.SceneManager.OnLoadStart -= OnSceneLoadStart;
				NetworkManager.SceneManager.OnLoadEnd -= OnSceneLoadEnd;
				NetworkManager.SceneManager.OnUnloadEnd -= OnSceneUnloadEnd;
			}
			ClientNamingSystem.Destroy();
			UIManager.SetClient(null);
			this.clientPostbootSystem?.UnsetClient(this);
			Application.logMessageReceived -= OnLogMessage;

#if UNITY_WEBGL || UNITY_IOS || UNITY_ANDROID
			// ── FIX #5: Best-effort token revocation for WebGL/mobile ──
			// OnApplicationQuit is not reliably called on these platforms.
			// OnApplicationPause(true) sets wasApplicationPaused before
			// OnDestroy fires during tab-close / app-kill.  This is a
			// best-effort revocation; if the runtime is killed abruptly
			// (force-quit, browser crash), the token remains valid until
			// its natural TTL (default 10 min on server).  The auth token
			// expiry window is the effective revocation delay.
			if (wasApplicationPaused)
			{
				try { this.loginAuthenticator?.RevokeAndClearAuthToken(); }
				catch { /* best effort — runtime may already be tearing down */ }
			}
#endif
		}

		// ── Init helpers ────────────────────────────────────────────────

		/// <summary>
		/// Finds and initializes the NetworkManager, registering required client broadcasts.
		/// </summary>
		/// <returns>True if initialization succeeded; otherwise, false.</returns>
		private bool TryInitializeNetworkManager()
		{
			// TODO: For production, assign NetworkManager via the Inspector (SerializeField)
			// to avoid the FindFirstObjectByType scan in Awake.
			if (NetworkManager == null) NetworkManager = FindFirstObjectByType<NetworkManager>();
			if (NetworkManager == null) { Log.Error("Client", "NetworkManager not found."); return false; }
			NetworkManager.ClientManager.RegisterBroadcast<WorldSceneConnectBroadcast>(OnWorldSceneConnect);
			NetworkManager.ClientManager.RegisterBroadcast<ConnectionTokenBroadcast>(OnConnectionTokenReceived);
			NetworkManager.ClientManager.RegisterBroadcast<ClientValidatedSceneBroadcast>(OnValidatedScene);
			NetworkManager.ClientManager.RegisterBroadcast<ServerBusyBroadcast>(OnServerBusy);
			NetworkManager.ClientManager.RegisterBroadcast<LoginQueuePositionBroadcast>(OnLoginQueuePosition);
			NetworkManager.ClientManager.RegisterBroadcast<WorldSceneQueuePositionBroadcast>(OnWorldSceneQueuePosition);
			NetworkManager.ClientManager.RegisterBroadcast<DeathBroadcast>(OnDeathBroadcast);
			NetworkManager.ClientManager.RegisterBroadcast<DisconnectNoticeBroadcast>(OnDisconnectNotice);
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
			// TODO: For production, assign loginAuthenticator via the Inspector to avoid
			// the FindFirstObjectByType scan in Awake.
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
			// Leaving the world: re-arm the incidental loading overlay that world entry
			// latched off, so the next login/world-entry cycle shows loading progress again.
			RestoreLoadingScreen();

			/* Take the overlay down as well, not just re-arm it.
			 *
			 * Both loading screens keep a set of independent "drivers", and one of them —
			 * reconnectPendingActive — is cleared only by Hide(). Every route into this method
			 * that comes from a lost connection has that driver latched: the drop armed a
			 * reconnect, the retry reached the server, and the server rejected the stale token,
			 * landing here. Nothing else clears it, so the login scene reloaded *behind* an
			 * overlay that could never come down, and the player was left staring at a loading
			 * bar at 100% with no way forward. The same applies to sceneTransitionActive when
			 * the connection dies part-way through a scene load and OnLoadEnd never arrives.
			 *
			 * Dismissing without suppression is safe here because the world is being torn down:
			 * the login scenes ClientPostbootSystem reloads raise the overlay again through the
			 * ordinary Addressable progress path and drop it when they finish. */
			DismissLoadingScreen(suppress: false);

			this.fogManager?.Stop();

			/* Abandon any world-scene preload this session started. The flag is what stops a
			 * second preload from being kicked off, and it is only ever cleared by the batch's
			 * own completion — so leaving it set here meant a quit-to-login during the preload
			 * latched it for the rest of the process: every later OnValidatedScene returned
			 * early, the client never acknowledged, and the scene server disconnected it after
			 * the handshake timeout. Every login after the first would fail to enter the world. */
			this.isPreloadingWorldScenes = false;
			this.worldPreloadBatch = null;

			// Nothing about a dead character in the world survives a return to the login screen.
			this.localCharacter = null;
			CancelDeathDialogFallback();

			AddressableLoadProcessor.UnloadSceneByLabelAsync(this.worldPreloadScenes);
			StopAllCoroutines(); // after unload has been initiated so the async operation isn't cancelled
			UnloadWorldScenes();

			/* Revoke first, disconnect second.
			 *
			 * The revocation is a broadcast, and FishNet does not put a broadcast on the wire
			 * when it is written — it goes into the outgoing bundle and leaves on the next tick.
			 * Tearing the transport down on the line above therefore discarded it every single
			 * time, so an explicit logout never revoked anything and the session token stayed
			 * valid for the rest of its lifetime. The local copy was still zeroed, which is why
			 * this went unnoticed: nothing on this client could use the token afterwards, but
			 * anyone who had captured it still could.
			 *
			 * The stop is deferred just long enough for that tick to happen. Everything else in
			 * this teardown runs immediately, so the player sees the login screen at the same
			 * moment they did before. */
			this.loginAuthenticator?.RevokeAndClearAuthToken();
			if (forceDisconnect) DisconnectAfterRevocationFlush();
			Connection?.ResetReconnectState();
			this.cachedConnectionToken = null;
			OnQuitToLogin?.Invoke();

			// After the panels are back, never before — see ShowPendingDisconnectNotice.
			ShowPendingDisconnectNotice();
#if UNITY_EDITOR
			PlayerInputController.MouseMode = true;
#endif
		}

		/// <summary>
		/// Ticks to let elapse before the transport is stopped, so a revocation written on the
		/// way out is actually sent.
		/// </summary>
		/// <remarks>
		/// One tick would be enough in principle; two costs a few tens of milliseconds the
		/// player spends looking at the login screen either way, and covers the case where the
		/// broadcast is written immediately after a tick boundary.
		/// </remarks>
		private const uint RevocationFlushTicks = 2;

		/// <summary>
		/// Upper bound, in seconds, on how long the deferred disconnect will wait for those
		/// ticks. A stalled or already-dead TimeManager must not leave the connection open.
		/// </summary>
		private const float RevocationFlushTimeoutSeconds = 0.5f;

		/// <summary>
		/// Stops the connection once the outgoing bundle carrying the token revocation has been
		/// flushed. See the call site in <see cref="QuitToLogin"/>.
		/// </summary>
		/// <remarks>
		/// Hosted on <see cref="CoroutineRunner"/> rather than on this component, because
		/// <see cref="QuitToLogin"/> calls <c>StopAllCoroutines</c> on itself a few lines
		/// earlier and would kill this before it ever ran.
		/// </remarks>
		private void DisconnectAfterRevocationFlush()
		{
			ClientConnectionManager connection = Connection;
			if (connection == null)
			{
				return;
			}

			// Nothing to flush, and nothing to wait for.
			if (connection.ClientState != LocalConnectionState.Started ||
				NetworkManager?.TimeManager == null)
			{
				connection.ForceDisconnect();
				return;
			}

			CoroutineRunner.Start(FlushThenDisconnectRoutine(connection));
		}

		private static IEnumerator FlushThenDisconnectRoutine(ClientConnectionManager connection)
		{
			var timeManager = NetworkManager != null ? NetworkManager.TimeManager : null;
			if (timeManager != null)
			{
				uint target = timeManager.LocalTick + RevocationFlushTicks;
				float deadline = Time.realtimeSinceStartup + RevocationFlushTimeoutSeconds;
				while (timeManager != null &&
					timeManager.LocalTick < target &&
					Time.realtimeSinceStartup < deadline &&
					connection.ClientState == LocalConnectionState.Started)
				{
					yield return null;
				}
			}

			// Unconditional: the disconnect is the point of this routine, and every exit above
			// (timeout, a TimeManager that went away, a connection that dropped on its own)
			// still has to reach it. ForceDisconnect is safe on an already-stopped connection.
			connection.ForceDisconnect();
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
		/// Returns the cached connection token and clears it, so it is only ever handed out
		/// once. See <see cref="cachedConnectionToken"/>.
		/// </summary>
		private string TakeConnectionToken()
		{
			string token = this.cachedConnectionToken;
			this.cachedConnectionToken = null;
			return token;
		}

		/// <summary>Set when a <see cref="ConnectionTokenBroadcast"/> reply arrives.</summary>
		private bool hopTokenReplyReceived;

		/// <summary>
		/// Receives a hop token from the server this client is currently connected to and
		/// stages it for the next handshake. See <see cref="RequestHopTokenThenConnect"/>.
		/// </summary>
		private void OnConnectionTokenReceived(ConnectionTokenBroadcast msg, Channel channel)
		{
			hopTokenReplyReceived = true;
			if (LoginAuthenticator != null && !string.IsNullOrEmpty(msg.ConnectionToken))
			{
				LoginAuthenticator.ConnectionToken = msg.ConnectionToken;
			}
			else
			{
				Log.Warning("Client", "Server returned no connection token for the next hop; " +
					"that server will reject the handshake.");
			}
		}

		/// <summary>
		/// Asks the currently connected server for a connection token, then connects to
		/// <paramref name="port"/> once it arrives (or the wait times out).
		/// </summary>
		/// <remarks>
		/// Used for both server hops — Login → World and World → Scene. The token must be
		/// obtained before the current connection is torn down, because it is the only party
		/// that knows this client's real IP; that is why it cannot be fetched from inside the
		/// connect coroutine, which runs after StopConnection.
		/// </remarks>
		public void RequestHopTokenThenConnect(ushort port, bool isWorldServer)
		{
			StartCoroutine(RequestHopTokenThenConnectRoutine(port, isWorldServer));
		}

		private IEnumerator RequestHopTokenThenConnectRoutine(ushort port, bool isWorldServer)
		{
			hopTokenReplyReceived = false;
			if (LoginAuthenticator != null)
			{
				// Drop any stale token so a failed request cannot silently reuse one that
				// was already spent on the current connection.
				LoginAuthenticator.ConnectionToken = null;
			}

			if (IsConnectionReady())
			{
				Broadcast(new RequestConnectionTokenBroadcast(), Channel.Reliable);

				float deadline = Time.realtimeSinceStartup + HopTokenTimeoutSeconds;
				while (!hopTokenReplyReceived && Time.realtimeSinceStartup < deadline)
				{
					yield return null;
				}
				if (!hopTokenReplyReceived)
				{
					Log.Warning("Client", "Timed out waiting for a connection token from the current server; " +
						"connecting anyway so the failure surfaces from the handshake.");
				}
			}

			ConnectToServer(port, isWorldServer);
		}

		/// <summary>Seconds to wait for a hop token before connecting regardless.</summary>
		private const float HopTokenTimeoutSeconds = 5f;

		/// <summary>
		/// Ensures <see cref="ClientLoginAuthenticator.ConnectionToken"/> holds a token for
		/// the connection that is about to start. Invoked by
		/// <see cref="ClientConnectionManager.EnsureConnectionToken"/> before every
		/// StartConnection.
		/// </summary>
		/// <remarks>
		/// The token is what lets a game server learn the client's real IP: NGINX forwards
		/// game traffic as raw UDP to loopback-bound servers, so every server — Login,
		/// World and Scene — sees 127.0.0.1 and rejects a handshake without one. Only
		/// IPFetch mints tokens (it is the sole component that sees the real IP, via
		/// X-Forwarded-For), and it does so per request, so each connection needs its own
		/// fetch. Skips the fetch when a token is already staged, which is the case on the
		/// login screen where the UI fetched it alongside the server list.
		/// </remarks>
		private IEnumerator EnsureConnectionTokenRoutine()
		{
			if (LoginAuthenticator == null)
			{
				yield break;
			}
			if (!string.IsNullOrEmpty(LoginAuthenticator.ConnectionToken))
			{
				yield break;
			}
			yield return GetLoginServerList(
				(error) => Log.Warning("Client", $"Could not obtain a connection token: {error}. " +
					"The server will reject the handshake without one."),
				(servers, token) =>
				{
					if (!string.IsNullOrEmpty(token))
					{
						LoginAuthenticator.ConnectionToken = token;
					}
				});
		}

		/// <summary>
		/// Probes API host candidates for a login server address list and the connection token
		/// that must accompany the next handshake. Always performs a fresh probe.
		/// </summary>
		/// <remarks>
		/// This deliberately does not cache. IPFetch returns the port list and the connection
		/// token in one response, the token is single-use, and every connect needs one — so a
		/// cache hit could never skip the round trip it would have to make anyway to mint a
		/// fresh token. A TTL-based port cache used to sit here; it was unreachable, because
		/// its guard required an unspent <see cref="cachedConnectionToken"/> and
		/// <see cref="TakeConnectionToken"/> clears that on the same synchronous path that
		/// sets it. Serving a stale list with a spent or expired token would present as a
		/// silent login failure that only a client restart clears, so re-probing is also the
		/// safe behaviour, not merely the simpler one.
		/// </remarks>
		/// <param name="onFail">Callback invoked with an error message if all probes fail.</param>
		/// <param name="onDone">Callback invoked with the list of discovered server addresses.</param>
		/// <returns>Coroutine enumerator.</returns>
		public IEnumerator GetLoginServerList(Action<string> onFail, Action<List<ushort>, string> onDone)
		{
			var candidates = ApiHostResolver.GetCandidates() ?? new List<string>();
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
						req.redirectLimit = -1; // -1 disables redirect following entirely
						req.SetRequestHeader(ClientApiSigner.HeaderKey, ClientApiSigner.BuildHeaderValue(UnityWebRequest.kHttpVerbGET, url));
						if (LoginServerRequestTimeoutSeconds > 0) req.timeout = LoginServerRequestTimeoutSeconds;
						pending.Add(new PendingProbe { Request = req, Op = req.SendWebRequest() });
						lastStart = Time.realtimeSinceStartup;
					}
					// NOTE: PendingProbe structs are value types, so writing default to the list element and using the local copy is safe. Do not change to foreach.
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
				if (winner != null) { this.loginServerPorts = winner; onDone?.Invoke(winner, TakeConnectionToken()); }
				else onFail?.Invoke(lastErr ?? "Failed to reach any APIHost.");
			}
			finally
			{
				foreach (var p in pending) { try { p.Request?.Abort(); } catch { /* Dispose/Abort exceptions are intentionally swallowed — the UnityWebRequest may already be disposed or the operation already complete. */ } try { p.Request?.Dispose(); } catch { /* Dispose/Abort exceptions are intentionally swallowed — the UnityWebRequest may already be disposed or the operation already complete. */ } }
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
		/// <summary>
		/// World Server → "connect to this Scene Server". Requests a connection token from
		/// the World Server first: the Scene Server is behind the same proxy and needs the
		/// real IP, and the World Server is the only party still holding it.
		/// </summary>
		private void OnWorldSceneConnect(WorldSceneConnectBroadcast msg, Channel ch)
		{
			try
			{
				/* Close the wait dialog here as well as on the queue's own position 0.
				 * That message is the tidy signal, but it is one message: this is the event
				 * that actually ends the wait, and leaving the dialog up over the scene
				 * transition would block the world behind a stale "queue position" box. */
				HideQueueDialog();

				if (IsConnectionReady())
				{
					RequestHopTokenThenConnect(msg.Port, false);
				}
			}
			catch (Exception ex)
			{
				Log.Error("Client", $"OnWorldSceneConnect: {ex}");
			}
		}
		/// <summary>
		/// Handles a validated scene broadcast by beginning the world scene preload queue.
		/// </summary>
		/// <param name="msg">The validated scene message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnValidatedScene(ClientValidatedSceneBroadcast msg, Channel ch)
		{
			/* Readiness is keyed off this call's own load batch.
			 *
			 * It used to subscribe to AddressableLoadProcessor.OnProgressUpdate, a global
			 * event that fires 1 whenever ANY queue drains. A bootstrap system or a loading
			 * screen finishing unrelated work would satisfy the >= 1 test and make the
			 * client tell the server it was in the scene before its world scenes existed.
			 * A batch only reports on the scenes enqueued right here. */
			if (this.isPreloadingWorldScenes)
			{
				// Duplicate broadcast from the server; the in-flight batch already covers it.
				return;
			}

			try
			{
				AddressableLoadProcessor.EnqueueLoad(this.worldPreloadScenes);

				this.isPreloadingWorldScenes = true;
				AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
				this.worldPreloadBatch = batch;
				batch.Completed += OnWorldPreloadComplete;
			}
			catch (UnityException ex)
			{
				this.isPreloadingWorldScenes = false;
				this.worldPreloadBatch = null;
				Log.Error("Client", "Preload failed", ex);
			}
		}
		/// <summary>
		/// Sends the validated-scene acknowledgement once this client's world preload
		/// scenes have finished loading.
		/// </summary>
		/// <param name="batch">The completed world preload batch.</param>
		private void OnWorldPreloadComplete(AddressableLoadBatch batch)
		{
			// A batch this client is no longer waiting on (quit to login, or a newer scene
			// transition superseded it) must not acknowledge anything. See worldPreloadBatch.
			if (batch == null || !ReferenceEquals(batch, this.worldPreloadBatch))
			{
				return;
			}

			this.isPreloadingWorldScenes = false;
			this.worldPreloadBatch = null;

			if (batch.HasFailures)
			{
				// Do not claim readiness for a scene set that did not load — the server
				// would spawn the character into a world this client cannot render.
				Log.Error("Client", $"World scene preload failed for: {string.Join(", ", batch.FailedItems)}. Not acknowledging scene validation.");
				return;
			}

			Client.Broadcast(new ClientValidatedSceneBroadcast(), Channel.Reliable);
		}
		/// <summary>
		/// Handles a server busy broadcast by showing a dialog to the player.
		/// </summary>
		/// <param name="msg">The server busy message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnServerBusy(ServerBusyBroadcast msg, Channel ch) { if (UIManager.TryGet("UIDialogBox", out UIDialogBox d)) d.Open("Server is busy. Please try again."); }

		// ── Queue feedback ──────────────────────────────────────────────

		/// <summary>
		/// Shows, or live-updates, the shared queue-wait dialog.
		/// </summary>
		/// <remarks>
		/// Both queues in the connection pipeline — the LoginServer's admission queue and the
		/// WorldServer's scene-routing queue — present through this one control so they cannot
		/// drift apart, and so a client cannot end up showing two competing wait dialogs.
		/// <see cref="UIDialogBox.Open"/> is a no-op while the box is already visible, which is
		/// why an update has to go through <see cref="UIDialogBox.SetText"/> instead.
		/// <para>
		/// Opened with no accept handler, which makes the remaining button read "Close" — the
		/// only action a waiting player has is to give up, and that is what
		/// <paramref name="onLeave"/> does.
		/// </para>
		/// </remarks>
		/// <param name="text">Body text to display.</param>
		/// <param name="onLeave">Invoked if the player abandons the wait.</param>
		private static void ShowQueueDialog(string text, Action onLeave)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox dialogBox))
			{
				if (dialogBox.Visible)
				{
					dialogBox.SetText(text);
				}
				else
				{
					dialogBox.Open(text, onAccept: null, onCancel: onLeave);
				}
				return;
			}

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tkDialogBox))
			{
				if (tkDialogBox.Visible)
				{
					tkDialogBox.SetText(text);
				}
				else
				{
					tkDialogBox.Open(text, onAccept: null, onCancel: onLeave);
				}
			}
		}

		/// <summary>
		/// Dismisses the shared queue-wait dialog if it is showing.
		/// </summary>
		private static void HideQueueDialog()
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox dialogBox) && dialogBox.Visible)
			{
				dialogBox.Hide();
				return;
			}

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tkDialogBox) && tkDialogBox.Visible)
			{
				tkDialogBox.Hide();
			}
		}

		/// <summary>
		/// Opens a one-off informational dialog through whichever dialog control this build
		/// registers.
		/// </summary>
		/// <remarks>
		/// The UGUI and UI Toolkit control sets live in separate registries, and a build ships
		/// one or the other. Resolving both here is what keeps the queue messages from silently
		/// doing nothing on a UI Toolkit build — which is how the login queue's own dialogs
		/// behaved, because they only ever asked for the UGUI control.
		/// </remarks>
		/// <param name="text">Message to display.</param>
		private static void ShowInfoDialog(string text)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox dialogBox))
			{
				dialogBox.Open(text);
				return;
			}

			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tkDialogBox))
			{
				tkDialogBox.Open(text);
			}
		}

		/// <summary>
		/// Builds the body text for a queue-wait dialog.
		/// </summary>
		/// <remarks>
		/// An estimate of zero means the server could not derive one — most often because the
		/// queue is not draining at all — so it is rendered as "Please wait...". Printing
		/// "~0s" over a queue that is going nowhere would be worse than saying nothing.
		/// </remarks>
		/// <param name="heading">One line explaining what is being waited for.</param>
		/// <param name="position">1-based position in the queue.</param>
		/// <param name="total">Total number of clients waiting.</param>
		/// <param name="estimatedWaitSeconds">Server estimate in seconds, or 0 when unknown.</param>
		private static string FormatQueueText(string heading, int position, int total, int estimatedWaitSeconds)
		{
			string tail = estimatedWaitSeconds > 0
				? $"Estimated wait: ~{estimatedWaitSeconds}s"
				: "Please wait...";

			return $"{heading}\nQueue position: {position} of {total}\n{tail}";
		}

		/// <summary>
		/// Handles a <see cref="LoginQueuePositionBroadcast"/> from the LoginServer,
		/// displaying the current queue position to the user.  Position &gt; 0 shows
		/// a waiting dialog; position 0 means the client has been admitted and should
		/// retry the handshake; position -1 means the queue entry was cancelled.
		/// </summary>
		private void OnLoginQueuePosition(LoginQueuePositionBroadcast msg, Channel ch)
		{
			if (msg.QueuePosition > 0)
			{
				ShowQueueDialog(
					FormatQueueText("Waiting to log in.", msg.QueuePosition, msg.TotalQueued, msg.EstimatedWaitSeconds),
					// The player chose to leave the queue.
					onLeave: () => QuitToLogin());
				return;
			}

			HideQueueDialog();

			if (msg.QueuePosition == 0)
			{
				// Admitted from queue — retry the handshake on the existing connection.

				/* async void local function captures Unity's SynchronizationContext
				 * so the continuation after await runs on the main thread.
				 * ContinueWith would run on the ThreadPool, making Log.Error
				 * and any future Unity API calls unsafe. */
				async void RetryWithFaultHandling()
				{
					try
					{
						if (loginAuthenticator != null)
							await loginAuthenticator.RetryHandshakeAsync();
					}
					catch (Exception ex)
					{
						Log.Error("Client", $"RetryHandshakeAsync failed: {ex.Message}");
					}
				}
				RetryWithFaultHandling();
				return;
			}

			// position -1 = cancelled (timeout / shutdown)
			QuitToLogin();

			/* Explain it, and only after the teardown. QuitToLogin drives every panel through
			 * its quit-to-login handler, which closes the dialog box along with everything else
			 * — so a message opened before it is swallowed and the player is returned to the
			 * login screen with no idea why their wait ended. */
			ShowInfoDialog("The login queue timed out. Please try again.");
		}

		/// <summary>
		/// Handles a <see cref="WorldSceneQueuePositionBroadcast"/> from the WorldServer while it
		/// is waiting for somewhere to put this client.
		/// </summary>
		/// <remarks>
		/// The World → Scene hop is the only leg of the connection pipeline that could stall for
		/// a long time with nothing on screen but a loading overlay: no capacity in any instance
		/// of the target scene, an instance still loading on a scene server, or a combat-logout
		/// body that only one specific instance can hand back. All three are legitimate waits
		/// and all three were completely silent, which is indistinguishable from a hang. This is
		/// the same feedback the login queue gives, one hop later.
		/// <para>
		/// Position 0 arrives immediately before <c>WorldSceneConnectBroadcast</c> and just
		/// closes the dialog; -1 means the server gave up, and is terminal — the connection is
		/// already closing, so retrying in place would only re-enter the same queue.
		/// </para>
		/// </remarks>
		private void OnWorldSceneQueuePosition(WorldSceneQueuePositionBroadcast msg, Channel ch)
		{
			if (msg.QueuePosition > 0)
			{
				ShowQueueDialog(
					FormatQueueText(DescribeWorldSceneQueueReason(msg.Reason), msg.QueuePosition, msg.TotalQueued, msg.EstimatedWaitSeconds),
					// The player chose not to keep waiting for a world slot.
					onLeave: () => QuitToLogin());
				return;
			}

			HideQueueDialog();

			if (msg.QueuePosition == 0)
			{
				// Routed. The scene connect broadcast is right behind this one.
				return;
			}

			// position -1 = the world server abandoned the wait and is closing the connection.
			QuitToLogin();

			ShowInfoDialog("The world server could not find room for your character. Please try again.");
		}

		/// <summary>
		/// Turns a <see cref="WorldSceneQueueReason"/> into a line the player can act on.
		/// </summary>
		/// <remarks>
		/// The three waits look identical from the outside but mean very different things — one
		/// is the world being full, one is a zone starting up, and one is the player's own body
		/// still standing in a fight they disconnected from. Only the last of those is
		/// self-inflicted, and a player who is not told about it has no way to understand why
		/// they are waiting when the world is visibly not busy.
		/// </remarks>
		private static string DescribeWorldSceneQueueReason(WorldSceneQueueReason reason)
		{
			switch (reason)
			{
				case WorldSceneQueueReason.SceneLoading:
					return "Preparing your zone.";
				case WorldSceneQueueReason.CombatLogoutBody:
					return "Your character is still in combat where you left it.\nWaiting for it to become available.";
				case WorldSceneQueueReason.Capacity:
				default:
					return "Waiting for space in the world.";
			}
		}

		// ── Disconnect feedback ─────────────────────────────────────────

		/// <summary>
		/// The most recent reason a server gave for closing the connection, or null.
		/// </summary>
		/// <remarks>
		/// Held rather than shown immediately, because the notice arrives while the world is
		/// still up: showing a dialog there would put it behind the teardown that follows, and
		/// for a non-terminal reason the reconnect may well succeed and make the message a lie.
		/// It is surfaced at the end of <see cref="QuitToLogin"/> — the one place every route
		/// back to the login screen passes through — and dropped the moment a connection
		/// succeeds, so a notice can never outlive the session it describes.
		/// </remarks>
		private DisconnectNoticeReason? pendingDisconnectNotice;

		/// <summary>
		/// A server is closing this connection on purpose and has said why.
		/// </summary>
		/// <remarks>
		/// Terminal reasons short-circuit the reconnect loop. Without that, a character that
		/// cannot be claimed at all still cost the player ten attempts with exponential backoff
		/// — several minutes of a "reconnecting" overlay — before landing on the login screen
		/// with the same outcome the server already knew about. Non-terminal reasons leave the
		/// loop alone, because retrying is the designed recovery for them; the message is kept
		/// in case the retries run out too.
		/// </remarks>
		private void OnDisconnectNotice(DisconnectNoticeBroadcast msg, Channel ch)
		{
			try
			{
				this.pendingDisconnectNotice = msg.Reason;

				Log.Info("Client", $"The server is closing this connection: {msg.Reason} (terminal={msg.Terminal}).");

				if (msg.Terminal)
				{
					QuitToLogin();
				}
			}
			catch (Exception ex)
			{
				Log.Error("Client", $"OnDisconnectNotice: {ex}");
			}
		}

		/// <summary>
		/// Shows the last disconnect reason, if one is outstanding, and clears it.
		/// </summary>
		/// <remarks>
		/// Called at the very end of <see cref="QuitToLogin"/>, after the panels have been
		/// restored — a dialog opened before that is closed again by the panels'
		/// quit-to-login handlers, which is how the login queue's timeout message used to
		/// disappear.
		/// </remarks>
		private void ShowPendingDisconnectNotice()
		{
			if (this.pendingDisconnectNotice == null)
			{
				return;
			}

			DisconnectNoticeReason reason = this.pendingDisconnectNotice.Value;
			this.pendingDisconnectNotice = null;

			ShowInfoDialog(DescribeDisconnectReason(reason));
		}

		/// <summary>
		/// Turns a <see cref="DisconnectNoticeReason"/> into something a player can act on.
		/// </summary>
		/// <remarks>
		/// The server deliberately sends a code rather than its own log text: its wording is
		/// written for an operator, and putting internal state on the wire would narrate the
		/// server's behaviour to anyone watching. Each line here says what happened and what to
		/// do about it, which is the part the player actually needs.
		/// </remarks>
		private static string DescribeDisconnectReason(DisconnectNoticeReason reason)
		{
			switch (reason)
			{
				case DisconnectNoticeReason.CharacterUnavailable:
					return "Your character could not be loaded.\nPlease try again, or pick a different character.";
				case DisconnectNoticeReason.SceneUnavailable:
					return "The zone your character is in could not be prepared.\nPlease try again in a moment.";
				case DisconnectNoticeReason.RoutingFailed:
					return "The world server could not find a place for your character.\nPlease try again in a moment.";
				case DisconnectNoticeReason.RoutingTimedOut:
					return "The world server took too long to place your character.\nPlease try again.";
				case DisconnectNoticeReason.SceneHandshakeTimedOut:
					return "Your client did not finish loading the zone in time.\nPlease try again.";
				case DisconnectNoticeReason.SessionSuperseded:
					return "Your character was taken over by another session.\nPlease log in again.";
				case DisconnectNoticeReason.RateLimited:
					return "You are doing that too quickly.\nPlease wait a moment and try again.";
				case DisconnectNoticeReason.ProtocolViolation:
					return "The server rejected this connection.\nIf this keeps happening, please contact support.";
				case DisconnectNoticeReason.ServerError:
					return "The server ran into a problem handling your login.\nPlease try again in a moment.";
				case DisconnectNoticeReason.Unspecified:
				default:
					return "You were disconnected by the server.\nPlease try again.";
			}
		}

		/// <summary>
		/// Handles a death broadcast by showing the death dialog UI.
		/// </summary>
		/// <param name="msg">The death broadcast message.</param>
		/// <param name="ch">The network channel.</param>
		private void OnDeathBroadcast(DeathBroadcast msg, Channel ch)
		{
			try
			{
				if (TryShowDeathDialog())
				{
					return;
				}

				/* Do NOT respawn from here. Sending the player to their bind point the instant
				 * one lookup misses would take the decision away from them — including a
				 * resurrect that another player may already be casting — and it is not
				 * reversible once the character has left its corpse.
				 *
				 * A miss here is far more likely to be a timing artefact than a missing build:
				 * the world GUI scene that carries the dialog is loaded per world entry, so a
				 * death notification can in principle be handled a moment before that scene's
				 * controls have registered. Arm a grace period that keeps retrying instead, and
				 * let Update decide. */
				ArmDeathDialogFallback();
			}
			catch (Exception ex)
			{
				Log.Error("Client", $"OnDeathBroadcast: {ex}");
			}
		}

		/// <summary>
		/// Shows the death dialog if it is currently registered.
		/// </summary>
		/// <returns><c>true</c> when the dialog was found and shown.</returns>
		private bool TryShowDeathDialog()
		{
			if (!UIManager.TryGetTK("UITKDeathDialog", out UITKDeathDialog deathDialog))
			{
				return false;
			}

			// Idempotent: the control also opens itself when handed a dead character, and
			// showing an already-visible dialog only refreshes its message.
			deathDialog.ShowDeathDialog();
			return true;
		}

		/// <summary>
		/// The local player character while one is spawned, or null.
		/// </summary>
		/// <remarks>
		/// Held so the death-dialog fallback can re-verify the situation immediately before it
		/// acts. Firing a respawn against a character that has since been resurrected, or that
		/// has already left this scene server, would move a player who never asked to be moved.
		/// </remarks>
		private IPlayerCharacter localCharacter;

		/// <summary>True while waiting for the death dialog to appear before giving up on it.</summary>
		private bool deathDialogFallbackPending;
		/// <summary>Unscaled time after which the dialog is treated as genuinely absent.</summary>
		private float deathDialogFallbackDeadline;
		/// <summary>Unscaled time of the next attempt to find the dialog.</summary>
		private float nextDeathDialogRetryTime;

		/// <summary>
		/// How long to keep looking for the death dialog before falling back to an automatic
		/// respawn.
		/// </summary>
		/// <remarks>
		/// Long enough to cover the dialog's scene registering after the death notification is
		/// handled, short enough that a genuinely missing dialog does not leave the player
		/// staring at a corpse wondering whether the game has stopped responding.
		/// </remarks>
		private const float DeathDialogGraceSeconds = 5.0f;

		/// <summary>Seconds between attempts to find the dialog during the grace period.</summary>
		private const float DeathDialogRetryIntervalSeconds = 0.25f;

		/// <summary>
		/// Starts the grace period during which the death dialog may still appear.
		/// </summary>
		private void ArmDeathDialogFallback()
		{
			if (this.deathDialogFallbackPending)
			{
				return;
			}

			this.deathDialogFallbackPending = true;
			this.deathDialogFallbackDeadline = Time.unscaledTime + DeathDialogGraceSeconds;
			this.nextDeathDialogRetryTime = Time.unscaledTime + DeathDialogRetryIntervalSeconds;

			Log.Warning("Client",
				"Character is dead but no death dialog is registered under 'UITKDeathDialog' yet; " +
				$"waiting up to {DeathDialogGraceSeconds:F0}s for it before falling back to an automatic respawn.");
		}

		/// <summary>
		/// Cancels a pending fallback. Called whenever the dead state it was armed for no longer
		/// applies.
		/// </summary>
		private void CancelDeathDialogFallback()
		{
			this.deathDialogFallbackPending = false;
			this.deathDialogFallbackDeadline = 0f;
			this.nextDeathDialogRetryTime = 0f;
		}

		/// <summary>
		/// Retries the death dialog during its grace period and, only once that expires,
		/// respawns so the character is not stranded.
		/// </summary>
		private void TickDeathDialogFallback()
		{
			if (!this.deathDialogFallbackPending)
			{
				return;
			}

			float now = Time.unscaledTime;
			if (now >= this.nextDeathDialogRetryTime)
			{
				this.nextDeathDialogRetryTime = now + DeathDialogRetryIntervalSeconds;

				if (TryShowDeathDialog())
				{
					// The dialog turned up. The player chooses from here.
					CancelDeathDialogFallback();
					return;
				}
			}

			if (now < this.deathDialogFallbackDeadline)
			{
				return;
			}

			CancelDeathDialogFallback();

			/* Re-verify before acting. The grace period is long enough for the situation to have
			 * changed underneath us — another player's resurrect can land, or the character can
			 * be despawned by a scene transfer — and respawning then would relocate a player who
			 * is fine, or who is already somewhere else. By this point the client has certainly
			 * reconciled health, so IsAlive is trustworthy here in a way it would not be in the
			 * first instants after death. */
			if (this.localCharacter == null)
			{
				Log.Debug("Client", "Death dialog fallback abandoned: the character is no longer spawned locally.");
				return;
			}

			if (this.localCharacter.TryGet(out ICharacterDamageController damageController) &&
				damageController.IsAlive)
			{
				Log.Debug("Client", "Death dialog fallback abandoned: the character is alive again.");
				return;
			}

			Log.Error("Client",
				$"No death dialog registered under 'UITKDeathDialog' after {DeathDialogGraceSeconds:F0}s. " +
				"The respawn and resurrect controls are unreachable, so this character would be stranded dead. " +
				"Check that the UITKDeathDialog object exists in the world GUI scene, is active, and that its " +
				"GameObject is named 'UITKDeathDialog'.");

			RequestFallbackRespawn("the death dialog never appeared");
		}

		/// <summary>
		/// Earliest unscaled time at which another fallback respawn may be sent.
		/// </summary>
		private float nextFallbackRespawnTime;

		/// <summary>
		/// Minimum seconds between fallback respawn requests.
		/// </summary>
		/// <remarks>
		/// Longer than the server's two-second respawn ingress debounce, so a repeated death
		/// notification cannot produce a burst the server would only throw away.
		/// </remarks>
		private const float FallbackRespawnCooldownSeconds = 3.0f;

		/// <summary>
		/// Asks the server to respawn this character at its bind point without going through
		/// the death dialog.
		/// </summary>
		/// <remarks>
		/// A last resort for the case where the dialog cannot be shown at all. Being dead is
		/// only an intermediate state if something can end it, and every route out of it runs
		/// through a dialog that is missing here — so the choice is between taking the decision
		/// away from the player and leaving the character permanently unplayable. Respawning at
		/// the bind point is the option the player would have had anyway; the resurrect they
		/// forfeit is speculative, and only offered while they are still lying there.
		/// <para>
		/// This is a degraded path that should never run in a correct build, which is why the
		/// caller logs an error explaining the wiring fault before reaching it.
		/// </para>
		/// </remarks>
		/// <param name="reason">Why the fallback was needed, for diagnostics.</param>
		private void RequestFallbackRespawn(string reason)
		{
			if (Time.unscaledTime < this.nextFallbackRespawnTime)
			{
				return;
			}

			if (!IsConnectionReady())
			{
				Log.Warning("Client",
					$"Cannot send a fallback respawn ({reason}): the connection is not ready. " +
					"The character stays dead until the connection recovers.");
				return;
			}

			this.nextFallbackRespawnTime = Time.unscaledTime + FallbackRespawnCooldownSeconds;

			Log.Warning("Client",
				$"Automatically respawning at the bind point because {reason}. " +
				"The player was not offered the choice; fix the death dialog wiring.");

			Broadcast(new RespawnAtBindPointBroadcast(), Channel.Reliable);
		}

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
				/* Do NOT dismiss the loading screen here. SceneLoginSuccess only means the
				 * network handshake with the scene server finished — Unity has not begun
				 * loading the actual scene yet, and the character does not exist. Dismissing
				 * at this point hid the overlay for the entire real load, which is the
				 * "world server hangs with no loading screen" report. The screen is dismissed
				 * in OnCharacterStartLocal, when the player actually exists. */
				case ClientAuthenticationResult.SceneLoginSuccess: Connection.CurrentConnectionType = ServerConnectionType.Scene; OnEnterGameWorld?.Invoke(); break;

				/* A rejected token is terminal, but nothing used to stop the reconnect loop
				 * from running its full course anyway: ten attempts with exponential backoff
				 * (5,10,20,40,60,60...) that cannot succeed, because the core has discarded the
				 * token and the credentials were nulled after login. That left the player
				 * watching a "reconnecting" spinner for minutes before landing on the login
				 * screen regardless. Go there immediately instead. */
				case ClientAuthenticationResult.TokenExpired:
				case ClientAuthenticationResult.TokenInvalid:
				case ClientAuthenticationResult.TokenRevoked:
					Log.Warning("Client", $"Authentication token rejected ({r}) — returning to login.");
					HandleUnrecoverableAuthFailure();
					break;

				default:
					Log.Warning("Client", $"Unhandled auth result: {r}");
					break;
			}
		}

		/// <summary>
		/// Abandons the current session after the server rejected our credentials in a way that
		/// retrying cannot fix, and returns the player to the login screen.
		/// </summary>
		/// <remarks>
		/// <see cref="ClientAuthenticatorCore.OnAuthResultReceived"/> already discards the
		/// rejected token before this runs, so the reconnect loop was not re-presenting it —
		/// but nothing stopped the loop either, and a client with no token and no credentials
		/// (they are nulled after login) cannot complete any of its ten attempts. Going
		/// straight to the login screen replaces several minutes of futile backoff with the
		/// outcome those attempts were always going to reach.
		/// <para>
		/// The redundant clear is kept as a safety net so this stays correct if the result is
		/// ever raised on a path that did not go through the core.
		/// </para>
		/// </remarks>
		private void HandleUnrecoverableAuthFailure()
		{
			try { this.loginAuthenticator?.ClearAuthToken(); }
			catch (Exception ex) { Log.Warning("Client", $"ClearAuthToken failed: {ex.Message}"); }
			QuitToLogin();
		}

		// ── Log guard ───────────────────────────────────────────────────
		
		/// <summary>
		/// Set by <see cref="OnLogMessage"/> on any thread when a networking-related
		/// exception is logged.  Checked and cleared on the main thread in
		/// <see cref="Update"/> so that Unity API calls (ForceDisconnect, auth
		/// token revocation) always execute on the main thread.
		/// </summary>
		private volatile string pendingErrorStackTrace;
		
		/// <summary>
		/// Handles Unity log messages. Thread-safe: stores the stack trace in a
		/// volatile field for main-thread processing in <see cref="Update"/>.
		/// Forces a disconnect on unhandled exceptions from the networking stack.
		/// </summary>
		/// <param name="condition">The log message.</param>
		/// <param name="stackTrace">The stack trace.</param>
		/// <param name="type">The log type.</param>
		private void OnLogMessage(string condition, string stackTrace, LogType type)
		{
			if (type != LogType.Exception) return;
			Log.Error("Client", string.IsNullOrEmpty(condition) ? stackTrace : $"{condition}\n{stackTrace}");
			if (string.IsNullOrEmpty(stackTrace) || !IsNetworkStack(stackTrace)) return;
			pendingErrorStackTrace = stackTrace;
		}
		/// <summary>
		/// Tears the session down after an unhandled exception escaped the networking stack.
		/// </summary>
		/// <remarks>
		/// This used to revoke the token and call <see cref="ClientConnectionManager.ForceDisconnect"/>
		/// and stop there. ForceDisconnect deliberately suppresses both the reconnect timer and
		/// <c>OnConnectionAttemptFailed</c> for the stop it causes, so nothing downstream ran:
		/// the world scenes stayed loaded, the HUD stayed on screen, no login panel was shown,
		/// and — because the token had just been revoked — no amount of waiting could recover
		/// it. The player was left standing in a frozen world with no way back short of
		/// restarting the client.
		/// <para>
		/// <see cref="QuitToLogin"/> does the same disconnect and revocation as part of a
		/// teardown that actually lands somewhere: world scenes unloaded, login scenes
		/// reloaded, panels restored. It is the only honest destination once the token is gone.
		/// </para>
		/// </remarks>
		/// <param name="stackTrace">Stack trace of the exception that triggered this, for diagnostics.</param>
		private void HandleNetworkStackException(string stackTrace)
		{
			// Nothing to tear down. Returning to login from here would put the login screen up
			// over whatever the player is already looking at — which, with no connection, is
			// the login screen.
			if (Connection == null || Connection.ClientState == LocalConnectionState.Stopped)
			{
				return;
			}

			Log.Error("Client",
				"An unhandled exception escaped the networking stack; the connection can no longer be trusted. " +
				$"Returning to the login screen.\n{stackTrace}");

			// QuitToLogin revokes the token itself; doing it here as well would try to send the
			// revocation twice and clear the local copy before the teardown could use it.
			QuitToLogin();
		}

		/// <summary>
		/// Determines whether an exception was thrown <i>by</i> the networking layer, as opposed
		/// to merely having passed through it.
		/// </summary>
		/// <remarks>
		/// Only the throwing frame counts, and getting this wrong is expensive. Testing the
		/// whole stack — which this used to do — matched <c>FishNet.Managing.</c> for every
		/// exception raised inside <i>any</i> broadcast or RPC handler, because that is the code
		/// that dispatches them. A null reference in a HUD panel reacting to a chat message was
		/// therefore indistinguishable from a corrupted transport stream, and the response to
		/// both is to revoke the session token and return to the login screen. Losing a session
		/// over a cosmetic UI bug is far worse than the bug.
		/// <para>
		/// The first line of a Unity stack trace is the throw site; its callers follow beneath
		/// it. Matching only that line keeps the teardown for faults raised inside the reader,
		/// the transport or the authenticator — those genuinely mean the connection can no
		/// longer be trusted — while a handler's own bug is logged and otherwise left alone.
		/// </para>
		/// </remarks>
		/// <param name="st">The stack trace to inspect.</param>
		/// <returns>True when the throwing frame belongs to the networking layer.</returns>
		private static bool IsNetworkStack(string st)
		{
			// First line = the frame that threw. Callers, including the broadcast dispatcher
			// that would match every handler in the game, are deliberately not considered.
			int lineEnd = st.IndexOf('\n');
			string throwSite = lineEnd >= 0 ? st.Substring(0, lineEnd) : st;

			return throwSite.IndexOf("FishNet.Managing.", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("FishNet.Transporting.", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("FishNet.Serializing.", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("FishMMO.Shared.Network.", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("FishMMO.Client.Authentication", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("LoginAuthenticator", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("SrpAuthenticator", StringComparison.Ordinal) >= 0 ||
				throwSite.IndexOf("ClientAuthenticator", StringComparison.Ordinal) >= 0;
		}

		// ── App lifecycle ───────────────────────────────────────────────

		/// <summary>
		/// Unity OnApplicationPause. Does NOT revoke the auth token — revoking on pause
		/// kills WebGL tab blur / mobile sessions. Only revoke on explicit quit or logout.
		/// </summary>
		/// <param name="paused">True if the application is being paused.</param>
		/// <summary>
		/// Unity OnApplicationPause. Does NOT revoke the auth token — revoking on pause
		/// kills WebGL tab blur / mobile sessions. On WebGL/mobile, flags the application
		/// as terminating so <see cref="OnDestroy"/> can attempt best-effort revocation.
		/// </summary>
		/// <param name="paused">True if the application is being paused.</param>
		private void OnApplicationPause(bool paused)
		{
			/* Auth token preserved across pause/unpause cycle.
			 * ── FIX #5: Flag termination for OnDestroy fallback ──
			 * On WebGL, iOS, and Android, Unity does not reliably call
			 * OnApplicationQuit when the app is terminated (tab close,
			 * app kill).  OnApplicationPause(true) is the closest signal
			 * we get.  Flag wasApplicationPaused so OnDestroy can
			 * attempt a best-effort token revocation. */
#if UNITY_WEBGL || UNITY_IOS || UNITY_ANDROID
			wasApplicationPaused = paused;
#endif
		}
#if UNITY_WEBGL || UNITY_IOS || UNITY_ANDROID
		/// <summary>
		/// Set to true by OnApplicationPause when the app is backgrounded
		/// (WebGL tab blur / mobile app suspend).  OnDestroy uses this flag
		/// to decide whether to attempt best-effort token revocation.
		/// </summary>
		private bool wasApplicationPaused = false;
#endif
		/// <summary>
		/// Unity OnApplicationQuit. Revokes the auth token when the application exits.
		/// NOTE: Not reliably called on WebGL or mobile platforms.
		/// On those platforms, OnApplicationPause + OnDestroy provide a fallback.
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
		/// <summary>
		/// True after world entry. Suppresses only the <i>incidental</i> loading overlay —
		/// the one driven by background Addressable asset loads.
		/// </summary>
		/// <remarks>
		/// This deliberately does not suppress genuine scene transitions. The overlay has
		/// two independent drivers: <c>AddressableLoadProcessor.OnProgressUpdate</c>, which
		/// fires for any background asset work and would otherwise flash the overlay over
		/// live gameplay, and FishNet's <c>SceneManager</c> load events, which are real zone
		/// changes the player must see a loading screen for.
		/// <para>Gating both on this flag meant that once it latched — which happened at the
		/// network handshake, before the first scene had even loaded — no loading screen
		/// could ever appear again for the rest of the session, including zone-to-zone
		/// teleports. The flag is now checked only on the Addressable path.</para>
		/// </remarks>
		public static bool LoadingSuppressed { get; private set; }

		/// <summary>
		/// Hides the loading overlay and optionally suppresses future incidental re-shows.
		/// Called on local character start, when the player actually exists in the world.
		/// </summary>
		/// <param name="suppress">
		/// True to stop background asset loads from re-showing the overlay. Genuine scene
		/// transitions are unaffected — see <see cref="LoadingSuppressed"/>.
		/// </param>
		public static void DismissLoadingScreen(bool suppress)
		{
			if (suppress) LoadingSuppressed = true;
			if (UIManager.TryGetTK<UITKLoadingScreen>("UITKLoadingScreen", out _)) UIManager.Hide("UITKLoadingScreen");
			if (UIManager.TryGet<UILoadingScreen>("UILoadingScreen", out _)) UIManager.Hide("UILoadingScreen");
		}

		/// <summary>
		/// Re-arms the incidental loading overlay after leaving the world.
		/// </summary>
		/// <remarks>
		/// <see cref="LoadingSuppressed"/> latches on world entry and had no counterpart, so
		/// it stayed set for the rest of the process: quit to login and come back and the
		/// Addressable-driven overlay never appeared again, because both loading screens
		/// return early from their progress handler while it is set. Clearing it on the way
		/// out of the world restores the overlay for the next login/world-entry cycle.
		/// </remarks>
		public static void RestoreLoadingScreen()
		{
			LoadingSuppressed = false;
		}

		/// <summary>
		/// Caps how fast the client renders, and keeps FishNet from overriding the cap.
		/// </summary>
		/// <param name="framesPerSecond">
		/// Target frames per second, normally the user's saved "Refresh Rate" preference.
		/// Values outside 1..500 are ignored.
		/// </param>
		/// <remarks>
		/// Setting <see cref="Application.targetFrameRate"/> alone is not enough to make a
		/// preference stick. FishNet's <c>NetworkManager.UpdateFramerate</c> writes
		/// <c>targetFrameRate</c> from <c>ClientManager.FrameRate</c> — 500 by default —
		/// every time the connection state changes, so a value set here would be discarded
		/// at the next connect or server hop. Pushing the same number into
		/// <c>ClientManager.SetFrameRate</c> makes FishNet's own value agree, so the cap
		/// survives every later <c>UpdateFramerate</c> call.
		/// <para>Applying the screen refresh rate via <c>Screen.SetResolution</c> changes the
		/// display mode only; with vSync off it does not limit the render loop.</para>
		/// </remarks>
		public static void ApplyTargetFrameRate(int framesPerSecond)
		{
			if (framesPerSecond <= 0 || framesPerSecond > 500) return;

			Application.targetFrameRate = framesPerSecond;

			// Keep FishNet's value in step so UpdateFramerate does not overwrite ours.
			NetworkManager?.ClientManager?.SetFrameRate((ushort)framesPerSecond);
		}

		private void OnCharacterStartLocal(IPlayerCharacter c)
		{
			/* World entry must always complete. Each step is isolated and the loading-screen
			 * dismissal is in a finally wrapping both: a throw while binding UI or input
			 * propagated straight out of this handler, so DismissLoadingScreen never ran and
			 * the player was left on a black screen with the overlay up and no input
			 * controller — unrecoverable without restarting the client. Losing a HUD panel is
			 * recoverable; never reaching the world is not.
			 *
			 * The finally covers the whole body rather than only the second step, so the
			 * dismissal is reached even if a catch block itself throws. */
			this.localCharacter = c;

			try
			{
				try
				{
					UIManager.SetCharacter(c);
				}
				catch (Exception ex)
				{
					Log.Error("Client", "OnCharacterStartLocal: UIManager.SetCharacter failed.", ex);
				}

				try
				{
					var input = c.GameObject.GetComponent<PlayerInputController>() ?? c.GameObject.AddComponent<PlayerInputController>();
					input.Initialize(c);
					PlayerInputController.MouseMode = false;
				}
				catch (Exception ex)
				{
					Log.Error("Client", "OnCharacterStartLocal: input controller setup failed.", ex);
				}

				try
				{
					EnsureDeadCharacterHasAWayOut(c);
				}
				catch (Exception ex)
				{
					Log.Error("Client", "OnCharacterStartLocal: dead-character check failed.", ex);
				}
			}
			finally
			{
				DismissLoadingScreen(true);
			}
		}

		/// <summary>
		/// Guarantees a character that entered the world already dead can act on it.
		/// </summary>
		/// <remarks>
		/// A character can arrive dead in two ordinary ways: logging in on a corpse, and being
		/// transferred between scene servers while dead. In both, <c>Flags</c> carries
		/// <see cref="CharacterFlags.IsDead"/> in the spawn payload, so the state is known here
		/// without waiting on any message.
		/// <para>
		/// <see cref="UITKDeathDialog"/> opens itself from the same state when
		/// <c>UIManager.SetCharacter</c> hands it the character, which is the normal path and
		/// runs a moment before this. This exists for the case where that control is not
		/// present at all: the server's own re-sent <c>DeathBroadcast</c> would reach
		/// <see cref="OnDeathBroadcast"/> and trigger the same fallback, but only if it arrives
		/// — and it is sent before the character spawns, so it depends on the world GUI scene
		/// already being loaded. Checking the spawned character's state does not.
		/// </para>
		/// </remarks>
		/// <param name="c">The local player character that just started.</param>
		private void EnsureDeadCharacterHasAWayOut(IPlayerCharacter c)
		{
			if (c == null || !c.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			if (TryShowDeathDialog())
			{
				return;
			}

			// Same rule as OnDeathBroadcast: never respawn on a single missed lookup. Give the
			// dialog its grace period first — this path in particular runs during world entry,
			// which is exactly when its scene may still be settling.
			ArmDeathDialogFallback();
		}
		/// <summary>
		/// Called when the local character stops. Cleans up input, UI, fog, and destroys the character object.
		/// </summary>
		/// <param name="c">The local player character.</param>
		private void OnCharacterStopLocal(IPlayerCharacter c)
		{
			this.localCharacter = null;

			// The character this was armed for is gone — despawn, scene transfer, or logout.
			// Letting it survive would respawn whatever character comes next at its bind point.
			CancelDeathDialogFallback();

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