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

		/// <summary>Fired when a connection to the server is successfully established.</summary>
		public event Action OnConnectionSuccessful;
		/// <summary>Fired on each reconnect attempt with current and max attempt counts.</summary>
		public event Action<int, int> OnReconnectAttempt;
		/// <summary>Fired when all reconnect attempts are exhausted without success.</summary>
		public event Action OnReconnectFailed;
		/// <summary>Fired when a non-reconnectable connection attempt fails (e.g. login server unreachable).</summary>
		public event Action OnConnectionAttemptFailed;

		/// <summary>Returns true if the current connection type supports reconnection (World or Scene).</summary>
		public bool CanReconnect =>
				CurrentConnectionType == ServerConnectionType.World ||
				CurrentConnectionType == ServerConnectionType.Scene;

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
				Log.Warning("ClientConnection", "ConnectToServer already in progress; ignoring duplicate call.");
				return;
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
					 * CurrentConnectionType at whatever the dropped connection was (Scene, after
					 * a world->scene hop), so a reconnect to the world server was recorded as a
					 * scene connection and the cached world address no longer matched the state
					 * describing it. */
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
		public void ForceDisconnect()
		{
			forceDisconnect = true;
			NetworkManager.ClientManager.StopConnection();
		}

		/// <summary>Resets all reconnect state: attempt count, connection type, stored address.</summary>
		public void ResetReconnectState()
		{
			forceDisconnect = false;
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

		/// <summary>Computes the reconnect delay with exponential backoff and jitter for the given attempt number.</summary>
		private float ComputeReconnectDelay(int attempt)
		{
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
					// Check CanReconnect BEFORE clearing CurrentConnectionType —
					// CanReconnect reads CurrentConnectionType to decide whether
					// World/Scene reconnection is applicable.
					if (!forceDisconnect && CanReconnect)
					{
						nextReconnect = ComputeReconnectDelay(ReconnectsAttempted);
						// OnReconnectAttempt is intentionally NOT fired here — it fires
						// once in TryReconnect() when the actual reconnect begins, so
						// consumers don't receive duplicate events per cycle.
					}
					else if (!forceDisconnect)
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
				int maxWaitIters = 1000;
				while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < deadline)
				{
					if (--maxWaitIters <= 0)
					{
						Log.Error("ClientConnection", "Connection stop wait iteration limit exceeded.");
						System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
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
					int maxForcedIters = 1000;
					while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < forcedDeadline)
					{
						if (--maxForcedIters <= 0)
						{
							Log.Error("ClientConnection", "Forced stop wait iteration limit exceeded.");
							System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
							yield break;
						}
						yield return null;
					}
					if (ClientState != LocalConnectionState.Stopped)
					{
						Log.Error("ClientConnection", "Forced connection stop timed out; aborting connect.");
						forceDisconnect = false;
						System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
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
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
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
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
				yield break;
			}

			// Wait for the connection to reach Started or Stopped, with a timeout.
			// The connectingGuard stays acquired during this wait to prevent
			// concurrent ConnectToServer calls from starting a second connection
			// while this one is still being established.
			float connectDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, ConnectionEstablishTimeoutSeconds);
			int connectMaxIters = Mathf.CeilToInt(ConnectionEstablishTimeoutSeconds * 120f); // ~120 checks/s
			while (ClientState == LocalConnectionState.Starting && Time.realtimeSinceStartup < connectDeadline)
			{
				if (--connectMaxIters <= 0)
				{
					Log.Error("ClientConnection", "Connection establish wait iteration limit exceeded.");
					NetworkManager.ClientManager.StopConnection();
					System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
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
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
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
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
				yield break;
			}

			System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
		}
	}
}