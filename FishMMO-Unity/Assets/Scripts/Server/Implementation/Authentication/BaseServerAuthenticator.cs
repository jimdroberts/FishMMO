using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Database.Npgsql.Services.Interfaces;
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


		/// <summary>Thread-safe queue for marshalling network operations to the main Unity thread.</summary>
		private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

		/// <summary>CancellationTokenSource for async workers. Created in InitializeWorkers, cancelled in ShutdownWorkers.</summary>
		protected CancellationTokenSource workerCts;

		/// <summary>
		/// Token derived from <see cref="workerCts"/> for cooperative cancellation checks
		/// in fire-and-forget async operations. Set during <see cref="InitializeWorkers"/>.
		/// </summary>
		private CancellationToken shutdownToken;

		/// <summary>Cached handler delegate for ClientHandshake broadcast registration/unregistration.</summary>
		private Action<NetworkConnection, ClientHandshake, Channel> clientHandshakeHandler;

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
			this.clientHandshakeHandler = (conn, msg, channel) =>
			{
				_ = OnServerClientHandshakeReceivedAsync(conn, msg, channel);
			};
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(this.clientHandshakeHandler, false);
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
			Core.ExpectedGameVersion = MainBootstrapSystem.GameVersion;
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
			if (string.IsNullOrEmpty(Core.ExpectedGameVersion))
			{
				_ = Log.Warning(LogPrefix, $"ExpectedGameVersion is null or empty. Client version validation will be disabled in production. " +
					"Set GameVersion in the MainBootstrap prefab to enable version enforcement.");
			}
#endif
			// Validate connection token HMAC key is configured.
			// Without this key, connection token verification will fail and all
			// proxy-authenticated clients will be rejected.
			{
				string? hmacKeyB64 = System.Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64");
				if (string.IsNullOrWhiteSpace(hmacKeyB64))
				{
					// Also check .cfg file fallback
					if (Server?.Configuration != null)
						Server.Configuration.TryGetString("ConnectionTokenHmacKeyBase64", out hmacKeyB64);
				}
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
				if (string.IsNullOrWhiteSpace(hmacKeyB64))
				{
					_ = Log.Error(LogPrefix,
						"FATAL: ConnectionTokenHmacKeyBase64 is not configured. " +
						"Clients will be unable to authenticate. " +
						"Set ConnectionTokenHmacKeyBase64 in the server .cfg file " +
						"or FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64 environment variable.");
				}
#else
				if (string.IsNullOrWhiteSpace(hmacKeyB64))
				{
					_ = Log.Warning(LogPrefix,
						"ConnectionTokenHmacKeyBase64 is not configured. " +
						"Connection token verification will fail -- clients connecting " +
						"through the proxy will be rejected. For local testing, ensure " +
						"clients connect directly (not through the HTTP IPFetch flow).");
				}
#endif
			}
			workerCts = new CancellationTokenSource();
			shutdownToken = workerCts.Token;
			Core.InitializeWorkers(workerCts.Token);

		}

		/// <summary>
		/// Shuts down async workers, disposes resources, and drains the main-thread queue.
		/// </summary>
		public virtual void ShutdownWorkers()
		{
			Core?.ShutdownWorkers();
			workerCts?.Cancel();
			workerCts?.Dispose();
			workerCts = null;
			DrainMainThreadQueue(drainAll: true);
		}

		/// <inheritdoc/>
		public abstract IAccountManager<NetworkConnection> CreateAccountManager();

		/// <inheritdoc/>
		public virtual bool AreWorkersDrained()
		{
			// Default implementation: workers are drained when the main-thread
			// queue is empty and the core reports no active worker operations.
			return mainThreadQueue.IsEmpty && (Core?.IsWorkerIdle ?? true);
		}

		/// <summary>
		/// Unity OnDestroy callback. Ensures async workers are stopped and network
		/// event handlers are unregistered even if the owning Server does not call
		/// <see cref="ShutdownWorkers"/> (e.g. abnormal teardown).
		/// </summary>
		private void OnDestroy()
		{
			ShutdownWorkers();
			CleanupNetworkHandlers();
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

		/// <summary>
		/// Unregisters broadcast handlers and event subscriptions that were registered
		/// in <see cref="InitializeOnce"/>. Subclasses overriding <see cref="RegisterProtocolHandlers"/>
		/// should override this method to unregister their protocol-specific handlers as well.
		/// Called during <see cref="OnDestroy"/> to prevent handler accumulation if the
		/// authenticator is destroyed and re-initialized.
		/// </summary>
		protected virtual void UnregisterProtocolHandlers(NetworkManager networkManager) { }

		/// <summary>
		/// Cleans up all network event handlers and broadcast registrations created
		/// during <see cref="InitializeOnce"/>.
		/// </summary>
		private void CleanupNetworkHandlers()
		{
			if (NetworkManager == null)
				return;

			NetworkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			NetworkManager.ServerManager.UnregisterBroadcast<ClientHandshake>(this.clientHandshakeHandler);
			UnregisterProtocolHandlers(NetworkManager);
		}

		#endregion

		#region Rate Limit Key Resolution

		/// <summary>
		/// Resolves the real client IP for rate limiting. Requires the IP to have been
		/// recovered from a verified connection token or auth token. Returns null if
		/// the real IP is not yet available — callers MUST disconnect the client.
		/// Never falls back to proxy IP or ClientId.
		/// </summary>
		protected string? ResolveRateLimitKey(NetworkConnection conn)
		{
			if (conn == null) return null;
			// Look up the real IP recovered from the connection/auth token.
			// Never fall back to conn.GetAddress() (which returns 127.0.0.1
			// behind an L4 proxy) or conn.ClientId (which resets on reconnect).
			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var rt) &&
				rt.ConnectionIpCache != null &&
				rt.ConnectionIpCache.TryGetAndTouch(conn.ClientId, DateTime.UtcNow, out string? realIp))
			{
				return HandshakeService.NormalizeIp(realIp);
			}
			return null;
		}

		#endregion

		#region FishNet Broadcast / Event Routing

		/// <summary>Routes incoming ClientHandshake broadcast to the core handshake handler
		/// and processes the connection token for real-IP recovery.</summary>
		internal async Task OnServerClientHandshakeReceivedAsync(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// Validate field sizes before forwarding to the core handler.
			// Reject oversized payloads on the network thread before any allocation or crypto work.
			if (msg.PublicKey == null || msg.PublicKey.Length > AuthSizeLimits.MaxPublicKeySize ||
				(msg.Cookie != null && msg.Cookie.Length > AuthSizeLimits.MaxCookieSize) ||
				(msg.ConnectionToken != null && msg.ConnectionToken.Length > AuthSizeLimits.MaxConnectionTokenLength) ||
				(msg.GameVersion != null && msg.GameVersion.Length > AuthSizeLimits.MaxGameVersionLength))
			{
				conn.Disconnect(true);
				return;
			}

			// Process connection token for real-IP recovery synchronously.
			// When behind an L4 UDP proxy, conn.GetAddress() returns 127.0.0.1.
			// The token bridges the real IP from the HTTP layer into the QUIC layer.
			// We await resolution because rate limiting requires a verified real IP —
			// falling back to proxy IP or ClientId is not acceptable for DoS protection.
			if (!string.IsNullOrEmpty(msg.ConnectionToken))
			{
				try
				{
					if (!await ProcessConnectionTokenAsync(conn, msg.ConnectionToken))
					{
						conn.Disconnect(true);
						return;
					}
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"ProcessConnectionTokenAsync threw for connection {conn.ClientId}: {ex.Message}");
					conn.Disconnect(true);
					return;
				}
			}
			else
			{
				// No connection token provided — client is not coming through the
				// IPFetch proxy path. Disconnect immediately.
				await Log.Warning(LogPrefix, $"Connection {conn.ClientId} rejected: no connection token.");
				conn.Disconnect(true);
				return;
			}

			if (Core == null)
			{
				await Log.Warning(LogPrefix, $"Core is null during OnHandshakeReceived for connection {conn.ClientId} — handshake discarded. Ensure InitializeWorkers() was called before accepting connections.");
			}
			else
			{
				Core.OnHandshakeReceived(conn, msg.PublicKey, msg.Cookie, msg.ConnectionToken, msg.MinVersion, msg.MaxVersion, msg.GameVersion ?? "");
			}
		}

		/// <summary>
		/// Validates a connection token against the database and stores the real IP
		/// for rate-limiting and logging. Fire-and-forget — does not block the handshake.
		/// </summary>
		/// <returns>true if the real IP was successfully recovered; false if the client should be disconnected.</returns>
		private async Task<bool> ProcessConnectionTokenAsync(NetworkConnection conn, string rawToken)
		{
			CancellationToken ct = shutdownToken;
			if (ct.IsCancellationRequested) return false;

			var server = Server;
			if (server?.DataContainerRegistry == null ||
				!server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out _))
				return false;

			// Try stateless HMAC verification first (new format: payloadB64.sigB64).
			// Falls back to DB lookup for legacy tokens or if HMAC key not configured.
			string? realIp = TryVerifyStatelessConnectionToken(rawToken, server.Configuration);
			if (realIp != null)
			{
				StoreRealIpForConnection(conn.ClientId, realIp);
				await Log.Debug(LogPrefix, $"Real IP {realIp} recovered via HMAC token for connection {conn.ClientId}.");
				return true;
			}

			// Legacy DB-backed token path removed (IConnectionTokenService deleted
			// when connection tokens migrated to stateless HMAC).
			await Log.Warning(LogPrefix, $"Legacy connection token not supported for {conn.ClientId}.");
			return false;
		}

		/// <summary>
		/// Attempts to verify a stateless HMAC-signed connection token.
		/// Token format: base64url(realIp|expiryUnix).base64url(HMAC-SHA256(key, payload))
		/// Returns the real IP on success, null if the token is invalid/expired
		/// or the HMAC key is not configured.
		/// </summary>
		private static string? TryVerifyStatelessConnectionToken(string rawToken, IServerConfiguration? config)
		{
			if (string.IsNullOrEmpty(rawToken)) return null;
			int dotIdx = rawToken.LastIndexOf('.');
			if (dotIdx <= 0 || dotIdx >= rawToken.Length - 1) return null;

			var hmacKey = ResolveConnectionTokenHmacKey(config);
			if (hmacKey == null) return null;

			try
			{
				var payloadB64 = rawToken.Substring(0, dotIdx).Replace('-', '+').Replace('_', '/');
				var sigB64 = rawToken.Substring(dotIdx + 1).Replace('-', '+').Replace('_', '/');
				// Restore Base64 padding
				while (payloadB64.Length % 4 != 0) payloadB64 += "=";
				while (sigB64.Length % 4 != 0) sigB64 += "=";

				var payload = Convert.FromBase64String(payloadB64);
				var expectedSig = Convert.FromBase64String(sigB64);

				using var hmac = new HMACSHA256(hmacKey);
				var computedSig = hmac.ComputeHash(payload);
				if (!CryptographicOperations.FixedTimeEquals(computedSig, expectedSig))
					return null;

				var payloadStr = Encoding.UTF8.GetString(payload);
				int pipeIdx = payloadStr.LastIndexOf('|');
				if (pipeIdx <= 0) return null;

				var realIp = payloadStr.Substring(0, pipeIdx);
				if (!long.TryParse(payloadStr.Substring(pipeIdx + 1), out long expiryUnix))
					return null;

				if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiryUnix)
					return null; // expired

				return realIp;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Resolves the shared HMAC key for connection token verification.
		/// Order: ConnectionTokenHmacKeyBase64 in server .cfg, then
		/// FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64 env var.
		/// </summary>
		private static byte[]? ResolveConnectionTokenHmacKey(IServerConfiguration? config)
		{
			string? b64 = null;
			if (config != null && config.TryGetString("ConnectionTokenHmacKeyBase64", out var cfgValue) &&
				!string.IsNullOrWhiteSpace(cfgValue))
				b64 = cfgValue.Trim();
			else
			{
				var envValue = System.Environment.GetEnvironmentVariable("FISHMMO_CONNECTION_TOKEN_HMAC_KEY_BASE64");
				if (!string.IsNullOrWhiteSpace(envValue))
					b64 = envValue.Trim();
			}
			if (string.IsNullOrEmpty(b64)) return null;
			try { return Convert.FromBase64String(b64); }
			catch { return null; }
		}

		/// <summary>
		/// Stores the real client IP for a connection so that rate-limiting and
		/// logging use the actual address instead of the proxy's loopback IP.
		/// </summary>
		private void StoreRealIpForConnection(int clientId, string realIp)
		{
			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.ConnectionIpCache?.Upsert(clientId, realIp, DateTime.UtcNow);
			}
		}

		/// <summary>Notifies the core when a remote connection stops so transient auth state can be purged.</summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				Core?.HandleConnectionStopped(conn);
				// Immediately notify the login queue so dead connections
				// don't consume admission ticks for up to 10 seconds.
				if (Server?.BehaviourRegistry != null &&
					Server.BehaviourRegistry.TryGet<LoginServer.LoginQueueSystem>(out var queueSystem))
				{
					queueSystem.OnClientDisconnected(conn.ClientId);
				}
			}
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