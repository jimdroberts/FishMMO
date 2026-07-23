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

		/// <summary>Number of reconnect attempts made since the last successful connection.</summary>
		public byte ReconnectsAttempted { get; private set; }
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

		/// <summary>Maximum reconnect attempts before giving up. Default 10.</summary>
		[SerializeField]
		public byte MaxReconnectAttempts = 10;
		/// <summary>Base wait time in seconds between reconnect attempts. Default 5.</summary>
		[SerializeField]
		public float ReconnectAttemptWaitTime = 5f;
		/// <summary>Maximum delay in seconds for exponential backoff. Default 60.</summary>
		[SerializeField]
		public float MaxReconnectDelay = 60f;
		/// <summary>Timeout in seconds waiting for a connection to fully stop. Default 10.</summary>
		[SerializeField]
		public float ConnectionStopTimeoutSeconds = 10f;

		/// <summary>Fired when a connection to the server is successfully established.</summary>
		public event Action OnConnectionSuccessful;
		/// <summary>Fired on each reconnect attempt with current and max attempt counts.</summary>
		public event Action<byte, byte> OnReconnectAttempt;
		/// <summary>Fired when all reconnect attempts are exhausted without success.</summary>
		public event Action OnReconnectFailed;

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
			NetworkManager.ClientManager.StopConnection();
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
			switch (ClientState)
			{
				case LocalConnectionState.Stopped:
					CurrentConnectionType = ServerConnectionType.None;
					if (!forceDisconnect && CanReconnect)
					{
						nextReconnect = ComputeReconnectDelay(ReconnectsAttempted);
						// OnReconnectAttempt is intentionally NOT fired here — it fires
						// once in TryReconnect() when the actual reconnect begins, so
						// consumers don't receive duplicate events per cycle.
					}
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
				while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < deadline)
					yield return null;
				if (ClientState != LocalConnectionState.Stopped)
				{
					Log.Warning("ClientConnection", $"Timed out waiting for connection stop; forcing teardown.");
					NetworkManager.ClientManager.StopConnection();
					// Wait for the forced stop to complete before starting a new connection.
					// Without this wait, StartConnection may fail because the transport
					// is still in Stopping state.
					float forcedDeadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, ConnectionStopTimeoutSeconds);
					while (ClientState != LocalConnectionState.Stopped && Time.realtimeSinceStartup < forcedDeadline)
						yield return null;
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
			try
			{
				NetworkManager.ClientManager.StartConnection(address, port);
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref connectingGuard, 0);
			}
			yield return null;
		}
	}
}