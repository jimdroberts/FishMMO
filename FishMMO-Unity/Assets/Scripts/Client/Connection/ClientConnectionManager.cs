using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages client connection lifecycle: connect, disconnect, reconnect with
	/// exponential backoff, and connection state tracking. Extracted from Client.cs.
	/// </summary>
	public class ClientConnectionManager
	{
		/// <summary>The FishNet NetworkManager managing this client connection.</summary>
		public NetworkManager NetworkManager { get; private set; }
		/// <summary>The current local connection state (Stopped, Starting, Started, Stopping).</summary>
		public LocalConnectionState ClientState { get; private set; } = LocalConnectionState.Stopped;
		/// <summary>The type of server currently connecting to (None, Login, World, Scene).</summary>
		public ServerConnectionType CurrentConnectionType { get; set; } = ServerConnectionType.None;

		/// <summary>
		/// Optional coroutine run immediately before every <c>StartConnection</c>, used to
		/// obtain the one-time connection token the game server needs to recover this
		/// client's real IP.
		/// <para>
		/// Every game server — Login, World and Scene — sits behind the same L4 UDP proxy
		/// and binds to loopback, so all of them see 127.0.0.1 as the source address and
		/// all of them reject a handshake that arrives without a token. Hooking the fetch
		/// here rather than at the individual call sites means world/scene routing and
		/// automatic reconnects get a token too, not just the login screen.
		/// </para>
		/// </summary>
		public Func<IEnumerator> EnsureConnectionToken;

		/// <summary>Number of reconnect attempts made since the last successful connection.</summary>
		public int ReconnectsAttempted { get; private set; }
		private float nextReconnect;
		/// <summary>In-flight connection guard. Prevents concurrent ConnectToServer calls from starting duplicate coroutines.</summary>
		/// <remarks>All access to connectingGuard happens on the Unity main thread (Update, coroutines, event callbacks),
		/// so a plain int would be technically sufficient. The Interlocked.CompareExchange/Exchange usage is
		/// a defensive measure — it costs nothing on x86/ARM and documents the intent against future refactoring
		/// that might introduce background-thread access.</remarks>
		private int connectingGuard = 0;
		/// <summary>
		/// Timestamp (double precision, seconds since startup) of the most recent
		/// successful connectingGuard acquisition. Used to detect and recover from a
		/// leaked guard. Double precision prevents float loss after ~24h of client
		/// uptime, which would cause guardAge to compute as zero.
		/// </summary>
		private double connectingGuardAcquiredAt = 0.0;
		/// <summary>
		/// ── FIX #10: Reference to the in-flight connection coroutine ──
		/// Stored so ResetReconnectState can stop the coroutine before
		/// releasing the connectingGuard.  Uses IEnumerator because
		/// CoroutineRunner.Start/Stop accept IEnumerator, not UnityEngine.Coroutine.
		/// </summary>
		private System.Collections.IEnumerator inFlightConnectionCoroutine;
		/// <summary>Thread-safe disconnect flag. Volatile for visibility across transport-worker callbacks.</summary>
		private volatile bool forceDisconnect;
		/// <summary>
		/// Set while <see cref="ConnectToServer"/> is tearing down the previous connection on
		/// purpose so it can start a new one. The resulting Stopped transition is a deliberate
		/// hop, not a dropped connection, so it must arm neither the reconnect timer nor
		/// <see cref="OnConnectionAttemptFailed"/>.
		/// </summary>
		/// <remarks>
		/// Without this, a World→Scene hop looked exactly like a world-server drop: the hop
		/// passes isWorldServer=false, so CurrentConnectionType stays World, CanReconnect is
		/// true, and the teardown armed a reconnect back to the world server. connectingGuard
		/// stopped the duplicate connect from landing, but only after TryReconnect had already
		/// incremented ReconnectsAttempted and fired OnReconnectAttempt, surfacing a spurious
		/// "reconnecting" state during a perfectly healthy scene transition.
		///
		/// Cleared immediately before StartConnection, which is what keeps a genuine connect
		/// failure reportable: every Stopped that means "the connection attempt failed" arrives
		/// after StartConnection, so it always sees this flag false. Volatile to match
		/// forceDisconnect — the transport reports state from worker callbacks.
		/// </remarks>
		private volatile bool stoppingForConnect;
		private string lastWorldAddress = "";
		private ushort lastWorldPort;

		private int maxReconnectAttempts = 10;
		/// <summary>Maximum reconnect attempts before giving up. Range [1, 255]. Default 10.</summary>
		public int MaxReconnectAttempts
		{
			get => maxReconnectAttempts;
			set => maxReconnectAttempts = Math.Max(1, Math.Min(value, 255));
		}
		/// <summary>Base wait time in seconds between reconnect attempts. Default 5.</summary>
		public float ReconnectAttemptWaitTime { get; set; } = 5f;
		/// <summary>Maximum delay in seconds for exponential backoff. Default 60.</summary>
		public float MaxReconnectDelay { get; set; } = 60f;
		/// <summary>Timeout in seconds waiting for a connection to fully stop. Default 10.</summary>
		public float ConnectionStopTimeoutSeconds { get; set; } = 10f;
		/// <summary>Timeout in seconds waiting for a new connection to reach Started state. Default 20.</summary>
		public float ConnectionEstablishTimeoutSeconds { get; set; } = 20f;

		/// <summary>
		/// Upper bound on frames rendered per second, used only to size the runaway-loop
		/// guards in <see cref="OnAwaitingConnectionReady"/>.
		/// </summary>
		/// <remarks>
		/// Those loops are bounded by a wall-clock deadline; the iteration counters exist so a
		/// clock that stops advancing cannot spin forever. Sizing them from a fixed frame count
		/// made them the *tighter* bound on any client rendering faster than that count divided
		/// by the timeout — which is routine, because FishNet's ClientManager.FrameRate defaults
		/// to 500 and the login screen renders essentially nothing. A 20s establish timeout was
		/// therefore aborted after roughly five seconds, reported as an internal
		/// "iteration limit exceeded" error rather than as the connection failure it was, so a
		/// player on a slow link saw the login button silently stop working.
		/// <para>Deliberately far above any real refresh rate: overshooting only weakens a
		/// backstop that the deadline already covers, while undershooting reintroduces the bug.</para>
		/// </remarks>
		private const float MaxExpectedFramesPerSecond = 2000f;

		/// <summary>
		/// Converts a timeout in seconds into a frame-iteration cap that cannot expire before
		/// the timeout does. See <see cref="MaxExpectedFramesPerSecond"/>.
		/// </summary>
		private static int IterationCapForSeconds(float seconds)
		{
			double frames = (double)Mathf.Max(0.1f, seconds) * MaxExpectedFramesPerSecond;
			return frames >= int.MaxValue ? int.MaxValue : (int)frames + 1;
		}

		/// <summary>Fired when a connection to the server is successfully established.</summary>
		public event Action OnConnectionSuccessful;
		/// <summary>Fired on each reconnect attempt with current and max attempt counts.</summary>
		public event Action<int, int> OnReconnectAttempt;
		/// <summary>Fired when all reconnect attempts are exhausted without success.</summary>
		public event Action OnReconnectFailed;
		/// <summary>Fired when a non-reconnectable connection attempt fails (e.g. login server unreachable).</summary>
		public event Action OnConnectionAttemptFailed;
		/// <summary>
		/// Fired the moment a reconnect is armed, before the backoff delay begins.
		/// </summary>
		/// <remarks>
		/// <see cref="OnReconnectAttempt"/> fires when the retry actually starts, which leaves
		/// the whole backoff window unreported. That window is not idle time during a scene
		/// transfer: the scene server has already unloaded the client's scene, so the loading
		/// overlay has been dismissed by the unload-end event and the player is looking at an
		/// empty world until the retry fires. Shortening the delay for a deliberate hop reduces
		/// that to a flicker; this event closes it, by letting the overlay treat "a reconnect is
		/// coming" as a transition in progress rather than waiting for it to begin.
		/// </remarks>
		public event Action OnReconnectPending;

		/// <summary>Returns true if the current connection type supports reconnection
		/// (World, Scene, or an in-flight world connect).</summary>
		/// <remarks>
		/// ConnectingToWorld counts. <see cref="ConnectToServer"/> sets it for every world
		/// connect — including the reconnects <see cref="TryReconnect"/> issues — and
		/// <see cref="OnClientConnectionState"/> clears CurrentConnectionType to None on the
		/// drop that arms the retry, so by the time an attempt fails ConnectingToWorld is the
		/// only type left describing it. Without it here, a failed reconnect read as a
		/// non-reconnectable failure: no further retry was armed, so MaxReconnectAttempts
		/// never counted past one, OnReconnectFailed (and its quit-to-login forward) never
		/// fired, and the client sat disconnected with no path forward. lastWorldAddress is
		/// assigned before StartConnection on that same path, so a retry always has an
		/// address to dial — and TryReconnect no-ops when it does not.
		/// </remarks>
		public bool CanReconnect =>
				CurrentConnectionType == ServerConnectionType.World ||
				CurrentConnectionType == ServerConnectionType.Scene ||
				CurrentConnectionType == ServerConnectionType.ConnectingToWorld;

		/// <summary>Creates a ClientConnectionManager and subscribes to NetworkManager connection events.</summary>
		/// <param name="networkManager">The FishNet NetworkManager to manage.</param>
		public ClientConnectionManager(NetworkManager networkManager)
		{
			NetworkManager = networkManager;
			NetworkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
		}

		/// <summary>Unsubscribes from NetworkManager events. Call during client teardown.</summary>
		public void Shutdown()
		{
			NetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;

			/* Drop every subscriber this manager is holding.
			 *
			 * Client.Awake attaches handlers here — including an anonymous lambda on
			 * OnConnectionAttemptFailed that captures the Client, which by construction can
			 * never be removed by its owner. Clearing the events here is the only place
			 * that reference can be released, and it stops a stale handler from firing
			 * against a half-destroyed Client if anything reaches this manager after
			 * teardown has begun.
			 *
			 * EnsureConnectionToken is a Func the Client also assigns, capturing it too. */
			OnConnectionSuccessful = null;
			OnReconnectAttempt = null;
			OnReconnectFailed = null;
			OnConnectionAttemptFailed = null;
			OnReconnectPending = null;
			EnsureConnectionToken = null;
		}

		/// <summary>Drives reconnect timer. Called every frame from the owning client.</summary>
		public void Update()
		{
			if (forceDisconnect || ReconnectsAttempted > MaxReconnectAttempts || ClientState != LocalConnectionState.Stopped)
				return;

			if (nextReconnect > 0)
			{
				nextReconnect -= Time.deltaTime;
				if (nextReconnect <= 0) TryReconnect();
			}
		}

		/// <summary>Initiates a connection. Guards against concurrent calls via CAS.</summary>
		/// <param name="address">Server IP or hostname.</param>
		/// <param name="port">Server port.</param>
		/// <param name="isWorldServer">If true, stores address for reconnection logic.</param>
		public void ConnectToServer(string address, ushort port, bool isWorldServer = false)
		{
			if (string.IsNullOrWhiteSpace(address))
			{
				Log.Error("ClientConnection", "ConnectToServer: address is null or empty.");
				return;
			}
			// In-flight guard: prevent double-invocation from starting two
			// concurrent coroutines that would both call StartConnection().
			if (System.Threading.Interlocked.CompareExchange(ref connectingGuard, 1, 0) != 0)
			{
				/* Leaked-guard recovery. Every exit path in OnAwaitingConnectionReady releases
				 * the guard, but the coroutine has to actually run for that to happen — and
				 * CoroutineRunner hosts it on a GameObject this class does not own. If the
				 * coroutine never starts or is stopped from outside, the guard stays acquired
				 * and every subsequent connect attempt is refused for the life of the process,
				 * which presents as a login button that silently stops working.
				 *
				 * The bound is generous: it must exceed the worst legitimate acquisition,
				 * which is the stop wait plus the token fetch plus the establish wait. */
				double guardAge = (double)Time.realtimeSinceStartup - connectingGuardAcquiredAt;
				double guardMaxAge = ConnectionStopTimeoutSeconds * 2.0 + ConnectionEstablishTimeoutSeconds + 30.0;
				if (connectingGuardAcquiredAt <= 0.0 || guardAge < guardMaxAge)
				{
					Log.Warning("ClientConnection", "ConnectToServer already in progress; ignoring duplicate call.");
					return;
				}

				Log.Error("ClientConnection",
					$"Connection guard held for {guardAge:F1}s with no in-flight connect (limit {guardMaxAge:F0}s); " +
					"reclaiming it. The previous connection coroutine was stopped without releasing.");
				if (inFlightConnectionCoroutine != null)
				{
					CoroutineRunner.Stop(inFlightConnectionCoroutine);
					inFlightConnectionCoroutine = null;
				}
				// The guard is already 1; leave it acquired and continue as the new owner.
			}
			connectingGuardAcquiredAt = (double)Time.realtimeSinceStartup;
			if (isWorldServer) CurrentConnectionType = ServerConnectionType.ConnectingToWorld;
			// Mark the teardown below as deliberate so OnClientConnectionState does not treat
			// it as a drop. Covers every hop — Login→World, World→Scene and reconnects alike.
			stoppingForConnect = true;
			// A deliberate connect supersedes any reconnect still counting down from an earlier
			// drop; leaving it armed lets Update fire TryReconnect at the old world address
			// mid-hop, which only connectingGuard would stop — and not before it had already
			// incremented ReconnectsAttempted and raised OnReconnectAttempt. A failed attempt
			// re-arms it from OnClientConnectionState, so the retry loop is unaffected.
			nextReconnect = -1;
			NetworkManager.ClientManager.StopConnection();
			var routine = OnAwaitingConnectionReady(address, port, isWorldServer);
			inFlightConnectionCoroutine = routine;
			CoroutineRunner.Start(routine);
		}

		/// <summary>Attempts a reconnect with exponential backoff. The backoff delay is set in
		/// <see cref="OnClientConnectionState"/> when the connection stops and counted
		/// down in <see cref="Update"/>. This method is called by Update() when the
		/// timer expires — it must NOT reset the delay or the reconnect will never fire.
		/// </summary>
		public void TryReconnect()
		{
			if (ReconnectsAttempted < MaxReconnectAttempts)
			{
				if (!string.IsNullOrEmpty(lastWorldAddress) && lastWorldPort != 0)
				{
					ReconnectsAttempted++;
					OnReconnectAttempt?.Invoke(ReconnectsAttempted, MaxReconnectAttempts);
					/* isWorldServer: true — lastWorldAddress/lastWorldPort are by definition the
					 * world server, so the reconnect must be typed as one. Omitting it left
					 * CurrentConnectionType at None (OnClientConnectionState clears it on the
					 * drop that armed this retry), which made a failed attempt look like a
					 * non-reconnectable failure: CanReconnect was false, so no further retry was
					 * armed and OnConnectionAttemptFailed fired instead — wrongly invalidating
					 * the cached login server list for what was a world-server outage. It also
					 * made the establish-timeout message name the login server while actually
					 * dialing the world server. See CanReconnect, which must accept
					 * ConnectingToWorld for the retry loop to survive past the first attempt. */
					ConnectToServer(lastWorldAddress, lastWorldPort, isWorldServer: true);
				}
			}
			else
			{
				ReconnectsAttempted = 0; nextReconnect = -1;
				OnReconnectFailed?.Invoke();
			}
		}

		/// <summary>Cancels pending reconnect and fires OnReconnectFailed immediately.</summary>
		public void CancelReconnect() { ReconnectsAttempted = 0; nextReconnect = -1; OnReconnectFailed?.Invoke(); }

		/// <summary>Forces the connection closed and prevents auto-reconnect until reset.</summary>
		/// <remarks>
		/// The suppression flag is only latched when there is a connection left to tear down.
		/// <see cref="OnClientConnectionState"/> is what consumes it, so latching it against a
		/// connection that is already Stopped strands it: no further Stopped transition is
		/// raised to clear it, and the next <see cref="ConnectToServer"/> aborts inside
		/// <see cref="OnAwaitingConnectionReady"/> — silently, with the guard released and no
		/// connection started. The login screen reaches that state routinely, because every
		/// auth-error dialog calls this whether or not the transport is still up.
		/// </remarks>
		public void ForceDisconnect()
		{
			if (ClientState != LocalConnectionState.Stopped)
			{
				forceDisconnect = true;
			}
			// A deliberate disconnect supersedes any reconnect still counting down.
			nextReconnect = -1;
			NetworkManager.ClientManager.StopConnection();
		}

		/// <summary>Resets all reconnect state: attempt count, connection type, stored address.</summary>
		public void ResetReconnectState()
		{
			/* Only clear the suppression flag when the teardown it was latched for has already
			 * landed. QuitToLogin calls ForceDisconnect and then this method on the same
			 * synchronous path, but the transport reports Stopped a frame or more later — so
			 * clearing unconditionally meant that Stopped arrived with nothing left to mark it
			 * deliberate, and OnClientConnectionState reported a perfectly ordinary quit-to-
			 * login as a failed connection attempt (invalidating the cached login server list
			 * and logging an error for it). The Stopped handler clears the flag itself, so
			 * preserving it here cannot strand it. */
			if (ClientState == LocalConnectionState.Stopped)
			{
				forceDisconnect = false;
			}
			stoppingForConnect = false;
			ReconnectsAttempted = 0; nextReconnect = -1;
			CurrentConnectionType = ServerConnectionType.None;
			lastWorldAddress = ""; lastWorldPort = 0;
			// ── FIX #10: Stop in-flight coroutine BEFORE releasing guard ──
			// If a connection coroutine is suspended at a yield point,
			// releasing the guard would allow a second ConnectToServer to
			// start a concurrent coroutine on the same transport.  Stop the
			// coroutine first, then release the guard.
			if (inFlightConnectionCoroutine != null)
			{
				CoroutineRunner.Stop(inFlightConnectionCoroutine);
				inFlightConnectionCoroutine = null;
			}
			// Reset the in-flight connection guard. If a coroutine was
			// interrupted (e.g., StopAllCoroutines in QuitToLogin), the
			// guard would otherwise be permanently stuck at 1, blocking
			// all future ConnectToServer calls.
			connectingGuardAcquiredAt = 0.0;
			System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
		}

		/// <summary>Returns true if connected and optionally authenticated.</summary>
		/// <param name="requireAuthentication">If true, also checks connection is authenticated.</param>
		/// <returns>True if the connection is ready for use.</returns>
		public bool IsConnectionReady(bool requireAuthentication = true)
		{
			if (NetworkManager == null || ClientState != LocalConnectionState.Started) return false;
			if (requireAuthentication && (NetworkManager.ClientManager.Connection == null ||
				!NetworkManager.ClientManager.Connection.IsValid ||
				!NetworkManager.ClientManager.Connection.IsAuthenticated)) return false;
			return true;
		}

		/// <summary>
		/// Delay used for the first retry after a scene server hands the client back, instead of
		/// the full backoff. Jittered so a scene-wide event does not send every client at once.
		/// </summary>
		/// <remarks>
		/// A scene-to-scene transfer is implemented as a deliberate drop: the scene server
		/// releases the character and disconnects, and the client is expected to return to the
		/// world server to be re-routed. That return went through the ordinary reconnect
		/// backoff, so every teleport and channel switch cost a full
		/// <see cref="ReconnectAttemptWaitTime"/> (5s) of dead time — during which the scene has
		/// already unloaded and the loading overlay has already been dismissed by
		/// <c>OnSceneEndUnload</c>, leaving the player looking at an empty world.
		/// <para>
		/// Only the first attempt is fast-pathed, and only from a Scene connection. If that
		/// attempt fails the normal exponential backoff resumes from attempt 1, so a genuinely
		/// unreachable world server is still not hammered.
		/// </para>
		/// </remarks>
		public float SceneHandoffReconnectDelay { get; set; } = 0.25f;

		/// <summary>Computes the reconnect delay with exponential backoff and jitter for the given attempt number.</summary>
		/// <param name="attempt">Number of reconnect attempts already made.</param>
		/// <param name="fromSceneHandoff">
		/// True when the drop that armed this retry came from a Scene connection, which is how a
		/// deliberate scene transfer presents. See <see cref="SceneHandoffReconnectDelay"/>.
		/// </param>
		private float ComputeReconnectDelay(int attempt, bool fromSceneHandoff = false)
		{
			if (fromSceneHandoff && attempt <= 0)
			{
				return Mathf.Max(0f, SceneHandoffReconnectDelay) * UnityEngine.Random.Range(0.75f, 1.25f);
			}

			float d = ReconnectAttemptWaitTime <= 0 ? 1f : ReconnectAttemptWaitTime;
			int shift = attempt < 0 ? 0 : Math.Min(attempt, 6);
			float backoff = d * (1 << shift);
			if (backoff > MaxReconnectDelay) backoff = MaxReconnectDelay;
			return backoff * UnityEngine.Random.Range(0.75f, 1.25f);
		}

		private void OnClientConnectionState(ClientConnectionStateArgs args)
		{
			ClientState = args.ConnectionState;
			switch (ClientState)
			{
				case LocalConnectionState.Stopped:
					// A teardown that ConnectToServer started on purpose is a hop, not a loss.
					// Consume the flag and leave every other piece of state alone: no reconnect
					// timer, no OnConnectionAttemptFailed, and CurrentConnectionType is kept so
					// ConnectingToWorld survives the stop it was set for (the authenticator
					// promotes it to World/Scene once the new connection authenticates).
					if (stoppingForConnect)
					{
						stoppingForConnect = false;
						break;
					}
					// Snapshot before consuming: this transition is the one ForceDisconnect
					// latched the flag for, so it must be cleared here. Leaving it set for a
					// later reader means the next ConnectToServer sees a stale suppression and
					// aborts its own connect attempt (see ForceDisconnect).
					bool wasForced = forceDisconnect;
					forceDisconnect = false;
					// Check CanReconnect BEFORE clearing CurrentConnectionType —
					// CanReconnect reads CurrentConnectionType to decide whether
					// World/Scene reconnection is applicable.
					if (!wasForced && CanReconnect)
					{
						// A drop from a Scene connection is how a deliberate transfer presents:
						// the scene server released the character and sent us back to the world
						// server on purpose. Do not make the player wait out a failure backoff
						// for it. Read before CurrentConnectionType is cleared below.
						bool fromSceneHandoff = CurrentConnectionType == ServerConnectionType.Scene;
						nextReconnect = ComputeReconnectDelay(ReconnectsAttempted, fromSceneHandoff);
						OnReconnectPending?.Invoke();
						// OnReconnectAttempt is intentionally NOT fired here — it fires
						// once in TryReconnect() when the actual reconnect begins, so
						// consumers don't receive duplicate events per cycle.
					}
					else if (!wasForced)
					{
						// Non-reconnectable connection failed (e.g. login server).
						// Fire so listeners can invalidate cached discovery data.
						OnConnectionAttemptFailed?.Invoke();
					}
					CurrentConnectionType = ServerConnectionType.None;
					break;
				case LocalConnectionState.Started:
					OnConnectionSuccessful?.Invoke();
					ReconnectsAttempted = 0; nextReconnect = -1; forceDisconnect = false;
					break;
			}
		}

		private IEnumerator OnAwaitingConnectionReady(string address, ushort port, bool isWorldServer)
		{
			if (ClientState != LocalConnectionState.Stopped)
			{
				float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, ConnectionStopTimeoutSeconds);
				int maxWaitIters = IterationCapForSeconds(ConnectionStopTimeoutSeconds);
				while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < deadline)
				{
					if (--maxWaitIters <= 0)
					{
						Log.Error("ClientConnection", "Connection stop wait iteration limit exceeded.");
						AbortConnectAttempt();
						yield break;
					}
					yield return null;
				}
				if (ClientState != LocalConnectionState.Stopped)
				{
					Log.Warning("ClientConnection", $"Timed out waiting for connection stop; forcing teardown.");
					NetworkManager.ClientManager.StopConnection();
					// Wait for the forced stop to complete before starting a new connection.
					// Without this wait, StartConnection may fail because the transport
					// is still in Stopping state.
					float forcedDeadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, ConnectionStopTimeoutSeconds);
					int maxForcedIters = IterationCapForSeconds(ConnectionStopTimeoutSeconds);
					while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < forcedDeadline)
					{
						if (--maxForcedIters <= 0)
						{
							Log.Error("ClientConnection", "Forced stop wait iteration limit exceeded.");
							AbortConnectAttempt();
							yield break;
						}
						yield return null;
					}
					if (ClientState != LocalConnectionState.Stopped)
					{
						Log.Error("ClientConnection", "Forced connection stop timed out; aborting connect.");
						forceDisconnect = false;
						AbortConnectAttempt();
						yield break;
					}
				}
			}
			// Release the in-flight guard only after StartConnection has been called
			// (force-disconnect path above releases early and exits because we don't
			// want to start).  Releasing before StartConnection would allow a concurrent
			// ConnectToServer call to acquire the guard and start a second connection
			// while the first is still being set up.
			if (forceDisconnect)
			{
				forceDisconnect = false;
				AbortConnectAttempt();
				yield break;
			}
			if (isWorldServer) { lastWorldAddress = address; lastWorldPort = port; }

			// Obtain a connection token before the handshake. Runs for every connection
			// type and for reconnects — see EnsureConnectionToken. A failure here is not
			// fatal on its own: the connect proceeds and the server reports the rejection,
			// which keeps the failure visible in one place instead of two.
			if (EnsureConnectionToken != null)
			{
				IEnumerator tokenRoutine = null;
				try
				{
					tokenRoutine = EnsureConnectionToken();
				}
				catch (System.Exception ex)
				{
					Log.Error("ClientConnection", $"EnsureConnectionToken threw: {ex}");
				}
				if (tokenRoutine != null)
				{
					yield return tokenRoutine;
				}
			}

			// The deliberate teardown is over. Every Stopped from here on means the connection
			// attempt itself failed, which must still arm reconnect / fire
			// OnConnectionAttemptFailed — so clear the flag before StartConnection, not after.
			stoppingForConnect = false;

			try
			{
				Log.Info("ClientConnection", $"StartConnection host={address} port={port} isWorld={isWorldServer} timeout={ConnectionEstablishTimeoutSeconds}s");
				NetworkManager.ClientManager.StartConnection(address, port);
			}
			catch (System.Exception ex)
			{
				Log.Error("ClientConnection", $"StartConnection threw: {ex}");
				AbortConnectAttempt();
				yield break;
			}

			// Wait for the connection to reach Started or Stopped, with a timeout.
			// The connectingGuard stays acquired during this wait to prevent
			// concurrent ConnectToServer calls from starting a second connection
			// while this one is still being established.
			float connectDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, ConnectionEstablishTimeoutSeconds);
			int connectMaxIters = IterationCapForSeconds(ConnectionEstablishTimeoutSeconds);
			while (ClientState == LocalConnectionState.Starting && Time.realtimeSinceStartup < connectDeadline)
			{
				if (--connectMaxIters <= 0)
				{
					Log.Error("ClientConnection", "Connection establish wait iteration limit exceeded.");
					NetworkManager.ClientManager.StopConnection();
					AbortConnectAttempt();
					yield break;
				}
				yield return null;
			}

			if (ClientState == LocalConnectionState.Stopped)
			{
				// The transport already reported an error (WebGlOnError / HandleNativeDisconnect).
				// Log context and release the guard — the UI layer will surface the error.
				Log.Error("ClientConnection", $"Connection to {address}:{port} failed before reaching Started state. " +
					"The transport reported an error — check preceding [WebTransport Client] log lines for the specific reason.");
				AbortConnectAttempt();
				yield break;
			}

			if (ClientState != LocalConnectionState.Started)
			{
				// Still in Starting state — timed out waiting for the transport to connect.
				string serverType = isWorldServer ? "world" : "login";
				Log.Error("ClientConnection", $"Could not reach {serverType} server {address}:{port} " +
					$"within {ConnectionEstablishTimeoutSeconds}s. " +
					"Check network/UDP (QUIC), DNS resolution, and that the server is running and accepting connections on this port.");
				NetworkManager.ClientManager.StopConnection();
				AbortConnectAttempt();
				yield break;
			}

			inFlightConnectionCoroutine = null;
			System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
		}

		/// <summary>
		/// Releases the in-flight connection guard for an attempt that is giving up before
		/// <c>StartConnection</c> could report anything.
		/// </summary>
		/// <remarks>
		/// Clearing <see cref="stoppingForConnect"/> is the part that matters. The flag is
		/// normally consumed by the Stopped transition that <see cref="ConnectToServer"/>
		/// asked for, but every abort path here is reached precisely when that transition did
		/// not arrive (the teardown timed out) or was never raised at all (the connection was
		/// already stopped). Leaving it latched hands it to the *next* Stopped transition,
		/// which is a genuine drop — and one marked deliberate arms no reconnect and raises no
		/// <see cref="OnConnectionAttemptFailed"/>, so the client sits disconnected with
		/// nothing driving it back to the login screen.
		/// </remarks>
		private void AbortConnectAttempt()
		{
			stoppingForConnect = false;
			inFlightConnectionCoroutine = null;
			System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
		}
	}
}