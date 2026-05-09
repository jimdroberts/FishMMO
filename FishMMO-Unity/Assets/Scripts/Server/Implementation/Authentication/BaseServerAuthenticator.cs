using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Abstract base MonoBehaviour authenticator that routes FishNet transport callbacks
	/// to an engine-independent <see cref="BaseAuthenticatorCore{TConnection}"/> instance.
	/// All handshake, TTL, rate-limit, and worker logic lives in the core; this class
	/// bridges FishNet lifecycle events (broadcasts, connection state) to the core.
	/// </summary>
	public abstract class BaseServerAuthenticator : Authenticator, IServerAuthenticator
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per Update tick.
		/// </summary>
		[UnityEngine.SerializeField]
		private int maxMainThreadActionsPerUpdate = 100;

		/// <summary>
		/// When true, uses the connection ID as the rate-limiting key instead of the remote IP.
		/// Enable when the server is behind a reverse proxy where all clients share one IP.
		/// </summary>
		[Header("Proxy Compatibility")]
		[Tooltip("Use connection ID instead of IP for rate limiting. Enable when behind a proxy/NAT/load balancer.")]
		[SerializeField]
		private bool useConnectionIdForRateLimit = false;

		/// <summary>Thread-safe queue for marshalling network operations to the main Unity thread.</summary>
		private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

		/// <summary>CancellationTokenSource for async workers. Created in InitializeWorkers, cancelled in ShutdownWorkers.</summary>
		protected CancellationTokenSource workerCts;

		/// <summary>The engine-independent authenticator core. Created by <see cref="InitializeCoreInstance"/>.</summary>
		protected abstract BaseAuthenticatorCore<NetworkConnection> Core { get; }

		/// <summary>The server instance providing access to AccountManager and other infrastructure.</summary>
		public IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> Server { get; set; }

		/// <summary>Event triggered when server authentication completes for a connection.</summary>
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;

		/// <summary>Event triggered for custom post-authentication logic in server behaviours.</summary>
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		/// <summary>Display name used in log messages. Defaults to the concrete class name.</summary>
		protected virtual string LogPrefix => GetType().Name;

		#region Lifecycle

		/// <inheritdoc/>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);
			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(OnServerClientHandshakeReceived, false);
			RegisterProtocolHandlers(networkManager);
		}

		/// <summary>
		/// Registers protocol-specific broadcast handlers (e.g., SRP or token auth).
		/// Called once during <see cref="InitializeOnce"/>.
		/// </summary>
		protected abstract void RegisterProtocolHandlers(NetworkManager networkManager);

		/// <summary>
		/// Creates the protocol-specific core instance. Called at the start of
		/// <see cref="InitializeWorkers"/> before workers are started.
		/// Subclasses must create and store their typed core reference here.
		/// </summary>
		protected abstract void InitializeCoreInstance();

		/// <summary>
		/// Creates the core instance and starts async workers.
		/// Called after <see cref="Server"/> is assigned.
		/// </summary>
		public void InitializeWorkers()
		{
			ShutdownWorkers();
			InitializeCoreInstance();
			workerCts = new CancellationTokenSource();
			Core.InitializeWorkers(workerCts.Token);
		}

		/// <summary>
		/// Shuts down async workers, disposes resources, and drains the main-thread queue.
		/// </summary>
		public void ShutdownWorkers()
		{
			Core?.ShutdownWorkers();
			workerCts?.Cancel();
			workerCts?.Dispose();
			workerCts = null;
			DrainMainThreadQueue(drainAll: true);
		}

		/// <summary>
		/// Unity OnDestroy callback. Ensures async workers are stopped even if the owning
		/// Server does not call <see cref="ShutdownWorkers"/> (e.g. abnormal teardown).
		/// </summary>
		private void OnDestroy()
		{
			ShutdownWorkers();
		}

		/// <summary>Drains the main-thread queue and calls <see cref="Core"/>.Tick() each frame.</summary>
		private void Update()
		{
			DrainMainThreadQueue(drainAll: false);
			Core?.Tick();
			OnUpdate();
			OnAuthSweep();
		}

		/// <summary>Override for subclass-specific per-frame logic (e.g., additional rate-limit sweeps).</summary>
		protected virtual void OnUpdate() { }

		/// <summary>
		/// Override for subclass-specific periodic auth state cleanup.
		/// Called every frame; implementations should use bounded scan/remove to stay cheap.
		/// </summary>
		protected virtual void OnAuthSweep() { }

		#endregion

		#region Main Thread Queue

		/// <summary>
		/// Dequeues and invokes pending main-thread actions. When <paramref name="drainAll"/> is <c>true</c>
		/// all entries are processed (used on shutdown); otherwise at most
		/// <c>maxMainThreadActionsPerUpdate</c> entries are processed per frame.
		/// Logs a back-pressure warning when the queue is not fully drained.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			int maxActions = drainAll ? int.MaxValue : maxMainThreadActionsPerUpdate;
			for (int i = 0; i < maxActions; i++)
			{
				if (!mainThreadQueue.TryDequeue(out Action action))
					return;
				try
				{
					action.Invoke();
				}
				catch (Exception ex)
				{
					_ = Log.Error(LogPrefix, $"Exception in main-thread auth action: {ex}");
				}
			}
			if (!drainAll && mainThreadQueue.Count > 0)
				_ = Log.Warning(LogPrefix, $"Main-thread queue back-pressure: {mainThreadQueue.Count} actions remain after draining {maxMainThreadActionsPerUpdate}.");
		}

		/// <summary>Thread-safe enqueue of an action to be executed on the main Unity thread.</summary>
		protected void EnqueueMainThreadAction(Action action) => mainThreadQueue.Enqueue(action);

		#endregion

		#region Rate Limit Key Resolution

		/// <summary>
		/// Resolves a rate-limit key for a connection.
		/// Returns the connection ID string when <c>useConnectionIdForRateLimit</c> is enabled
		/// (proxy/NAT deployments); otherwise returns the normalized remote IP.
		/// </summary>
		protected string ResolveRateLimitKey(NetworkConnection conn)
		{
			if (conn == null) return string.Empty;
			if (useConnectionIdForRateLimit) return conn.ClientId.ToString();
			return HandshakeService.NormalizeIp(conn.GetAddress());
		}

		#endregion

		#region FishNet Broadcast / Event Routing

		/// <summary>Routes incoming ClientHandshake broadcast to the core handshake handler.</summary>
		internal void OnServerClientHandshakeReceived(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}
			Core?.OnHandshakeReceived(conn, msg.PublicKey, msg.Cookie, msg.MinVersion, msg.MaxVersion);
		}

		/// <summary>Notifies the core when a remote connection stops so transient auth state can be purged.</summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
				Core?.HandleConnectionStopped(conn);
		}

		#endregion

		#region Authentication Events

		/// <summary>
		/// Invokes the authentication result event for a connection.
		/// On success, the stale-auth sweep no longer purges this connection.
		/// </summary>
		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			OnAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>Invokes the client authentication result event for post-authentication logic.</summary>
		protected void InvokeClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			OnClientAuthenticationResult?.Invoke(conn, authenticated);
		}

		#endregion

		#region Virtual Login

		/// <summary>
		/// Attempts to complete login authentication. Override in subclasses for
		/// server-type-specific logic (e.g., WorldServer checks player limit).
		/// </summary>
		internal virtual Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			return Task.FromResult(result);
		}

		#endregion
	}
}