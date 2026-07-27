using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.WebTransport;
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

		/// <summary>Number of reconnect attempts made since the last successful connection.</summary>
		public int ReconnectsAttempted { get; private set; }
		private float nextReconnect;
		/// <summary>In-flight connection guard. Prevents concurrent ConnectToServer calls from starting duplicate coroutines.</summary>
		/// <remarks>All access to connectingGuard happens on the Unity main thread (Update, coroutines, event callbacks),
		/// so a plain int would be technically sufficient. The Interlocked.CompareExchange/Exchange usage is
		/// a defensive measure — it costs nothing on x86/ARM and documents the intent against future refactoring
		/// that might introduce background-thread access.</remarks>
		private int connectingGuard = 0;
		/// <summary>Thread-safe disconnect flag. Volatile for visibility across transport-worker callbacks.</summary>
		private volatile bool forceDisconnect;
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
		/// <summary>
		/// Timeout in seconds waiting for a connection to fully stop before force-reset.
		/// Default 3s (was 10s) — World→Scene must not stall on hung Editor native QUIC stop.
		/// </summary>
		public float ConnectionStopTimeoutSeconds { get; set; } = 3f;

		/// <summary>
		/// Max seconds to wait for <see cref="LocalConnectionState.Started"/> after
		/// <c>StartConnection</c>. Prevents register/login UI from hanging forever when
		/// WebTransport/QUIC never completes (UDP blocked, bad host, silent handshake fail).
		/// </summary>
		public float ConnectTimeoutSeconds { get; set; } = 20f;

		/// <summary>Fired when a connection to the server is successfully established.</summary>
		public event Action OnConnectionSuccessful;
		/// <summary>Fired on each reconnect attempt with current and max attempt counts.</summary>
		public event Action<int, int> OnReconnectAttempt;
		/// <summary>Fired when all reconnect attempts are exhausted without success.</summary>
		public event Action OnReconnectFailed;
		/// <summary>
		/// Fired when a connect attempt times out without reaching Started.
		/// Args: address, port, message for UI.
		/// </summary>
		public event Action<string, ushort, string> OnConnectTimedOut;

		/// <summary>
		/// Optional override for reconnect connects. When set, <see cref="TryReconnect"/>
		/// invokes this instead of <see cref="ConnectToServer"/> so the client can
		/// re-fetch an IPFetch HMAC connection token before the World/Scene handshake.
		/// Args: address, port.
		/// </summary>
		public Action<string, ushort> ReconnectConnectOverride;

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
			if (isWorldServer) CurrentConnectionType = ServerConnectionType.ConnectingToWorld;
			// Stop any prior session first — but only if not already Stopped.
			// Unnecessary Stop+Start churn was producing double WTConnect (index=0 then index=1).
			// Auth core must NOT wipe login/register credentials on Stop events.
			Log.Info("ClientConnection",
				$"ConnectToServer begin host={address} port={port} priorState={ClientState} " +
				"(single StartConnection after clean stop — one WT session)");
			if (ClientState != LocalConnectionState.Stopped)
			{
				Log.Info("ClientConnection",
					$"Stopping prior connection state={ClientState} before new connect");
				NetworkManager.ClientManager.StopConnection();
			}
			CoroutineRunner.Start(OnAwaitingConnectionReady(address, port, isWorldServer));
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
					// Prefer override so Client can stage a fresh IPFetch connection token
					// (World/Scene reject first ClientHandshake without one behind L4 proxy).
					if (ReconnectConnectOverride != null)
						ReconnectConnectOverride(lastWorldAddress, lastWorldPort);
					else
						ConnectToServer(lastWorldAddress, lastWorldPort);
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
			ReconnectsAttempted = 0; nextReconnect = -1;
			CurrentConnectionType = ServerConnectionType.None;
			lastWorldAddress = ""; lastWorldPort = 0;
			// Reset the in-flight connection guard. If a coroutine was
			// interrupted (e.g., StopAllCoroutines in QuitToLogin), the
			// guard would otherwise be permanently stuck at 1, blocking
			// all future ConnectToServer calls.
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
			Log.Info("ClientConnection",
				$"State → {ClientState} forceDisconnect={forceDisconnect} type={CurrentConnectionType}");
			switch (ClientState)
			{
				case LocalConnectionState.Stopped:
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
					CurrentConnectionType = ServerConnectionType.None;
					break;
				case LocalConnectionState.Started:
					// FishNet is usable for broadcasts only after Started — not merely
					// "WebTransport session established" on the wire.
					Log.Info("ClientConnection",
						"Started — FishNet ready for ClientHandshake / CreateAccountBroadcast");
					OnConnectionSuccessful?.Invoke();
					ReconnectsAttempted = 0; nextReconnect = -1; forceDisconnect = false;
					break;
			}
		}

		/// <summary>
		/// Hard-stop WebTransport/FishNet client so a hop (Login→World, World→Scene) can
		/// open a new session. Editor native QUIC has been observed stuck Started while
		/// StopConnection never delivered Stopped — WebGL usually stops cleanly.
		/// </summary>
		private void ForceStopTransportForHop(string reason)
		{
			Log.Warning("ClientConnection",
				$"ForceStopTransportForHop: {reason} (localState={ClientState})");

			// Suppress auto-reconnect while we tear down for an intentional hop.
			bool savedForce = forceDisconnect;
			forceDisconnect = true;
			nextReconnect = -1;

			try
			{
				try { NetworkManager.ClientManager.StopConnection(); }
				catch (Exception ex) { Log.Warning("ClientConnection", $"StopConnection: {ex.Message}"); }

				Transport t = NetworkManager.TransportManager != null
					? NetworkManager.TransportManager.Transport
					: null;

				if (t is Multipass multipass)
				{
					try { multipass.StopConnection(server: false); }
					catch (Exception ex) { Log.Warning("ClientConnection", $"Multipass.Stop: {ex.Message}"); }

					foreach (Transport nested in multipass.Transports)
					{
						if (nested is WebTransport nestedWt)
						{
							try { nestedWt.ForceStopClient(); }
							catch (Exception ex) { Log.Warning("ClientConnection", $"ForceStop nested WT: {ex.Message}"); }
						}
					}
				}
				else if (t is WebTransport wt)
				{
					try { wt.ForceStopClient(); }
					catch (Exception ex) { Log.Warning("ClientConnection", $"ForceStop WT: {ex.Message}"); }
				}
			}
			finally
			{
				// Local state must read Stopped so StartConnection is allowed, even if
				// FishNet never raised OnClientConnectionState(Stopped).
				ClientState = LocalConnectionState.Stopped;
				forceDisconnect = savedForce;
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
						break;
					yield return null;
				}
				if (ClientState != LocalConnectionState.Stopped)
				{
					// Do NOT abort the hop — force the transport down and continue.
					// Aborting here was the Editor-only World→Scene failure: never
					// StartConnection to sceneserver:7790, no scene handshake.
					ForceStopTransportForHop(
						$"stop wait timed out after {ConnectionStopTimeoutSeconds:0.#}s " +
						$"before connect {address}:{port}");
					// One frame so any queued connection-state callbacks settle.
					yield return null;
					if (ClientState != LocalConnectionState.Stopped)
						ClientState = LocalConnectionState.Stopped;
				}
			}
			// Intentional quit/force path — do not start a new session.
			if (forceDisconnect)
			{
				forceDisconnect = false;
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
				yield break;
			}
			if (isWorldServer) { lastWorldAddress = address; lastWorldPort = port; }

			// If still Starting/Started after force-stop, force again then proceed.
			if (ClientState == LocalConnectionState.Starting || ClientState == LocalConnectionState.Started)
			{
				Log.Warning("ClientConnection",
					$"Still {ClientState} before StartConnection to {address}:{port} — force-stop and continue.");
				ForceStopTransportForHop($"pre-Start still {ClientState}");
				yield return null;
				ClientState = LocalConnectionState.Stopped;
			}

			Log.Info("ClientConnection",
				$"StartConnection host={address} port={port} isWorld={isWorldServer} " +
				$"priorState={ClientState} timeout={ConnectTimeoutSeconds:0.#}s " +
				"(expect exactly one [FishWT] WTConnect BEGIN)");

			try
			{
				bool started = NetworkManager.ClientManager.StartConnection(address, port);
				if (!started)
				{
					// One more force-stop + retry — socket may still have been Starting.
					Log.Warning("ClientConnection",
						$"StartConnection returned false for {address}:{port}; force-stop and retry once.");
					ForceStopTransportForHop("StartConnection returned false");
					yield return null;
					ClientState = LocalConnectionState.Stopped;
					started = NetworkManager.ClientManager.StartConnection(address, port);
					if (!started)
					{
						Log.Error("ClientConnection",
							$"StartConnection still false for {address}:{port} after force-stop retry");
						System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
						OnConnectTimedOut?.Invoke(address, port,
							$"Could not start WebTransport to {address}:{port} (StartConnection false)");
						yield break;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error("ClientConnection", $"StartConnection threw: {ex.Message}", ex);
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
				string failMsg = $"Could not start connection to {address}:{port} — {ex.Message}";
				OnConnectTimedOut?.Invoke(address, port, failMsg);
				yield break;
			}
			finally
			{
				// Guard released so ForceDisconnect / concurrent cleanup can proceed while
				// we wait for Started (or timeout). A second ConnectToServer during wait is
				// still possible; UI locks should prevent that for login/register.
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
			}

			// Wait until Started, Stopped, or hard timeout — never leave UI waiting forever.
			float timeout = Mathf.Max(1f, ConnectTimeoutSeconds);
			float connectDeadline = Time.realtimeSinceStartup + timeout;
			while (ClientState != LocalConnectionState.Started
				&& ClientState != LocalConnectionState.Stopped
				&& Time.realtimeSinceStartup < connectDeadline)
			{
				if (forceDisconnect)
					yield break;
				yield return null;
			}

			if (ClientState == LocalConnectionState.Started)
			{
				Log.Info("ClientConnection", $"Connected to {address}:{port}");
				yield break;
			}

			if (ClientState == LocalConnectionState.Stopped)
			{
				// Transport failed fast (WebTransport onError / create failed / handshake refused).
				// Surface a UI message — previously only unlocked the form with no dialog.
				string failMsg =
					$"WebTransport failed to open https://{address}:{port} " +
					"(server down, TLS/cert, origin, or browser WebTransport). " +
					"See [FishWT] lines in the browser console.";
				Log.Warning("ClientConnection",
					$"Connection to {address}:{port} stopped before Started — {failMsg}");
				OnConnectTimedOut?.Invoke(address, port, failMsg);
				yield break;
			}

			// Still Starting/Stopping past deadline — hard fail for UI.
			// Typical when LoginServer is dead/SEGV, or QUIC_NETWORK_IDLE_TIMEOUT
			// (handshake never completed). close() while connecting is fallout of this.
			string msg =
				$"Could not reach login server {address}:{port} within {timeout:0}s " +
				$"(WebTransport URL https://{address}:{port}). " +
				"LoginServer may have crashed (SEGV) or is not completing the QUIC handshake — " +
				"check journalctl -u fishmmo-loginserver, UDP 7770, DNS (direct A, not Cloudflare), " +
				"and browser console for [FishWT] Ready failed / QUIC_NETWORK_IDLE_TIMEOUT.";
			Log.Error("ClientConnection", msg);
			forceDisconnect = true;
			try { NetworkManager.ClientManager.StopConnection(); }
			catch (Exception ex) { Log.Warning("ClientConnection", $"Stop after timeout failed: {ex.Message}"); }
			OnConnectTimedOut?.Invoke(address, port, msg);
		}
	}
}