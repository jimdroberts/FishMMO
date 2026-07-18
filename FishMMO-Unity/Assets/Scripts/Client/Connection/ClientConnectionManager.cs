using System;
using System.Collections;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Logging;
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
		private bool forceDisconnect;
		private string lastWorldAddress = "";
		private ushort lastWorldPort;

		/// <summary>Maximum reconnect attempts before giving up. Default 10.</summary>
	public byte MaxReconnectAttempts = 10;
		/// <summary>Base wait time in seconds between reconnect attempts. Default 5.</summary>
	public float ReconnectAttemptWaitTime = 5f;
		/// <summary>Maximum delay in seconds for exponential backoff. Default 60.</summary>
	public float MaxReconnectDelay = 60f;
		/// <summary>Timeout in seconds waiting for a connection to fully stop. Default 10.</summary>
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

		/// <summary>Initiates a connection. Waits for existing connection to stop if needed.</summary>
	/// <param name="address">Server IP or hostname.</param>
	/// <param name="port">Server port.</param>
	/// <param name="isWorldServer">If true, stores address for reconnection logic.</param>
	public void ConnectToServer(string address, ushort port, bool isWorldServer = false)
		{
			if (isWorldServer) CurrentConnectionType = ServerConnectionType.ConnectingToWorld;
			NetworkManager.ClientManager.StopConnection();
			CoroutineRunner.Start(OnAwaitingConnectionReady(address, port, isWorldServer));
		}

		/// <summary>Attempts an immediate reconnect with exponential backoff + jitter.</summary>
	public void TryReconnect()
		{
			if (nextReconnect < 0) nextReconnect = ComputeReconnectDelay(ReconnectsAttempted);
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
	public void CancelReconnect() { OnReconnectFailed?.Invoke(); }

		/// <summary>Forces the connection closed and prevents auto-reconnect until reset.</summary>
	public void ForceDisconnect()
		{
			forceDisconnect = true;
			NetworkManager.ClientManager.StopConnection();
		}

		/// <summary>Resets all reconnect state: attempt count, connection type, stored address.</summary>
	public void ResetReconnectState()
		{
			ReconnectsAttempted = 0; nextReconnect = -1;
			CurrentConnectionType = ServerConnectionType.None;
			lastWorldAddress = ""; lastWorldPort = 0;
		}

		/// <summary>Returns true if connected and optionally authenticated.</summary>
	/// <param name="requireAuthentication">If true, also checks connection is authenticated.</param>
	/// <returns>True if the connection is ready for use.</returns>
	public bool IsConnectionReady(bool requireAuthentication = true)
		{
			if (NetworkManager == null || ClientState != LocalConnectionState.Started) return false;
			if (requireAuthentication && (!NetworkManager.ClientManager.Connection.IsValid ||
				!NetworkManager.ClientManager.Connection.IsAuthenticated)) return false;
			return true;
		}

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
					if (!forceDisconnect && CanReconnect)
					{
						nextReconnect = ComputeReconnectDelay(ReconnectsAttempted);
						OnReconnectAttempt?.Invoke(ReconnectsAttempted, MaxReconnectAttempts);
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
						yield break;
					}
				}
			}
			if (forceDisconnect) { forceDisconnect = false; yield return null; }
			if (isWorldServer) { lastWorldAddress = address; lastWorldPort = port; }
			NetworkManager.ClientManager.StartConnection(address, port);
			yield return null;
		}
	}

	/// <summary>Minimal MonoBehaviour for running coroutines from non-MonoBehaviour classes.</summary>
	internal class CoroutineRunner : MonoBehaviour
	{
		private static CoroutineRunner instance;
		/// <summary>Starts a coroutine on a persistent hidden GameObject (created on first call).</summary>
		/// <param name="routine">The coroutine to start.</param>
		public static void Start(IEnumerator routine)
		{
			if (instance == null)
			{
				var go = new GameObject("CoroutineRunner") { hideFlags = HideFlags.HideAndDontSave };
				DontDestroyOnLoad(go);
				instance = go.AddComponent<CoroutineRunner>();
			}
			instance.StartCoroutine(routine);
		}
	}
}
