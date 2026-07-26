using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Collections;
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

		/// <summary>
		/// Per-key rate limiter for incoming <see cref="ClientHandshake"/> broadcasts.
		/// Limits to 10 handshakes per second per key (real IP when available, otherwise
		/// transport address or ClientId). Prevents CPU exhaustion from HMAC-SHA256
		/// verification of forged connection tokens before the handshake enters the
		/// core's capacity-limited SRP channels.
		/// </summary>
		private readonly ExpiringKeyTracker<string> handshakeRateLimiter = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Minimum interval between accepted handshakes per rate-limit key (100ms = 10/sec).</summary>
		private static readonly TimeSpan HandshakeRateLimitInterval = TimeSpan.FromMilliseconds(100);

		/// <summary>
		/// Maximum time a newly-connected client has to send a valid <see cref="ClientHandshake"/>
		/// before being disconnected. Prevents slot exhaustion from silent connections
		/// (clients that open a QUIC connection but never authenticate). Default 15 seconds.
		/// Configurable via the Unity Inspector.
		/// </summary>
		[UnityEngine.SerializeField]
		private float authHandshakeTimeoutSeconds = 15f;

		/// <summary>
		/// Thread-safe map of ClientId → UTC timestamp when the remote connection was established.
		/// Used by the authentication timeout sweep in <see cref="OnAuthSweep"/> to disconnect
		/// clients that connect but never send a <see cref="ClientHandshake"/>.
		/// Entries are added on <see cref="RemoteConnectionState.Started"/> and removed on
		/// <see cref="RemoteConnectionState.Stopped"/> or successful authentication.
		/// </summary>
		private protected readonly ConcurrentDictionary<int, DateTime> connectionStartTimes = new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Per-connection random nonce, assigned when a remote connection starts and removed
		/// when it stops. Used by <see cref="OnServerClientHandshakeReceivedAsync"/> to
		/// detect connection recycling: if FishNet reuses a ClientId between two await
		/// points, the nonce will not match, and the handshake is safely aborted rather
		/// than routed to the wrong connection. See BUG 6 in the security audit.
		/// </summary>
		private protected readonly ConcurrentDictionary<int, string> connectionNonces = new ConcurrentDictionary<int, string>();


		/// <summary>Thread-safe queue for marshalling network operations to the main Unity thread.</summary>
		private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

		/// <summary>CancellationTokenSource for async workers. Created in InitializeWorkers, cancelled in ShutdownWorkers.</summary>
		protected CancellationTokenSource workerCts;

		/// <summary>
		/// Token derived from <see cref="workerCts"/> for cooperative cancellation checks
		/// in fire-and-forget async operations. Set during <see cref="InitializeWorkers"/>.
		/// </summary>
		private CancellationToken shutdownToken;

		/// <summary>
		/// Exposed to subclasses so fire-and-forget async operations (e.g. renewal token
		/// issuance) can cooperatively cancel during server shutdown rather than accessing
		/// disposed database handles after <see cref="Server.PerformShutdown"/> completes.
		/// </summary>
		protected CancellationToken ShutdownToken => shutdownToken;

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
			this.clientHandshakeHandler = async (conn, msg, channel) =>
			{
				/* Fire-and-forget the async handshake via async void so the
				 * continuation runs on Unity's main thread (SynchronizationContext).
				 * ContinueWith would run on the ThreadPool, making conn.Disconnect()
				 * and Log calls unsafe.  The try/catch prevents unobserved Task
				 * exceptions from terminating the process (.NET Framework) or
				 * silently hanging the connection (.NET Core).
				 *
				 * IMPORTANT: Capture ClientId BEFORE any await.  The
				 * NetworkConnection object may be disconnected and recycled
				 * by FishNet while the async operation is in flight.  After
				 * each await, re-resolve the connection from the server's
				 * client dictionary via TryResolveConnection.  Never carry
				 * a NetworkConnection reference across an await boundary. */
				int clientId = conn.ClientId;
				try
				{
					await OnServerClientHandshakeReceivedAsync(clientId, msg, channel);
				}
				catch (Exception ex)
				{
					_ = Log.Error(LogPrefix,
						$"Unhandled exception in handshake for connection " +
						$"{clientId}: {ex.Message}");
					try
					{
						if (TryResolveConnection(clientId, out var staleConn))
							staleConn.Disconnect(true);
					}
					catch { /* best effort */ }
				}
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
			// Validate that the IConnectionTokenKeyService is available in the
			// database service registry. Connection token HMAC keys come SOLELY
			// from the database — no environment variable or .cfg file fallbacks.
			// If the service is not registered, the server will be unable to verify
			// connection tokens from proxy-authenticated clients.
			{
				bool serviceAvailable = Server?.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<IConnectionTokenKeyService>(out _);
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
				if (!serviceAvailable)
				{
					_ = Log.Error(LogPrefix,
						"FATAL: IConnectionTokenKeyService is not registered in the database service registry. " +
						"Clients will be unable to authenticate through the proxy. " +
						"Connection token HMAC keys must come from the database — no environment variable or " +
						".cfg file fallbacks are supported. Ensure the database service assembly is loaded.");
					throw new InvalidOperationException(
						"IConnectionTokenKeyService is not registered. " +
						"The server cannot start without a valid database-backed connection token key service. " +
						"Connection token HMAC keys must come from the database — no environment variable or " +
						".cfg file fallbacks are supported.");
				}
#else
				if (!serviceAvailable)
				{
					_ = Log.Warning(LogPrefix,
						"IConnectionTokenKeyService is not registered in the database service registry. " +
						"Connection token verification will fail — clients connecting through the proxy " +
						"will be rejected. For local testing, ensure clients connect directly " +
						"(not through the HTTP IPFetch flow).");
				}
#endif
			}
			workerCts = new CancellationTokenSource();
			shutdownToken = workerCts.Token;
			Core.InitializeWorkers(workerCts.Token);

			// Start DB-backed connection token key loading and periodic refresh.
			// The initial load populates the key map; the refresh loop polls every 60s.
			// The database is the ONLY source — no env var or .cfg file fallbacks exist.
			_ = LoadConnectionTokenKeysFromDbAsync();
			_ = KeyRefreshLoop(shutdownToken);

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
			SweepAuthTimeouts();
			// Sweep expired entries from the handshake rate limiter to prevent
			// unbounded memory growth under sustained handshake traffic.
			handshakeRateLimiter.SweepExpired(DateTime.UtcNow, maxScan: 64, maxRemove: 16);
		}

		/// <summary>
		/// Disconnects clients that connected but never sent a valid
		/// <see cref="ClientHandshake"/> within <see cref="authHandshakeTimeoutSeconds"/>.
		/// Bounded scan (max 32 entries/frame) to keep per-frame cost low.
		/// Authenticated connections are removed from the tracker without disconnecting.
		/// </summary>
		private void SweepAuthTimeouts()
		{
			if (authHandshakeTimeoutSeconds <= 0f || connectionStartTimes.IsEmpty)
				return;

			DateTime now = DateTime.UtcNow;
			TimeSpan timeout = TimeSpan.FromSeconds(authHandshakeTimeoutSeconds);
			const int maxScanPerFrame = 32;

			// Refresh the snapshot when the cursor is at the start (new cycle)
			// or when the snapshot is null/stale (first call or dictionary changed).
			if (authTimeoutSweepCursor == 0 || authTimeoutSnapshot == null)
			{
				authTimeoutSnapshot = connectionStartTimes.ToArray();
			}

			var snapshot = authTimeoutSnapshot;
			int scanned = 0;

			for (int i = authTimeoutSweepCursor; i < snapshot.Length && scanned < maxScanPerFrame; i++, scanned++)
			{
				var kvp = snapshot[i];
				int clientId = kvp.Key;
				DateTime started = kvp.Value;

				// Skip entries that were already removed between snapshot and now.
				if (!connectionStartTimes.ContainsKey(clientId))
					continue;

				NetworkConnection conn = null;
				var clients = NetworkManager?.ServerManager?.Clients;
				if (clients != null)
					clients.TryGetValue(clientId, out conn);
				if (conn != null && conn.IsAuthenticated)
				{
					connectionStartTimes.TryRemove(clientId, out _);
					continue;
				}

				if (conn == null)
				{
					connectionStartTimes.TryRemove(clientId, out _);
					continue;
				}

				if (now - started > timeout)
				{
					_ = Log.Warning(LogPrefix,
						$"Connection {clientId} timed out waiting for ClientHandshake " +
						$"({(now - started).TotalSeconds:F1}s > {authHandshakeTimeoutSeconds}s). Disconnecting.");
					connectionStartTimes.TryRemove(clientId, out _);
					try { conn.Disconnect(true); } catch { /* best effort */ }
				}
			}

			// Advance cursor; wrap to 0 when we've covered the entire snapshot.
			authTimeoutSweepCursor += scanned;
			if (authTimeoutSweepCursor >= snapshot.Length)
				authTimeoutSweepCursor = 0;

			// Periodic full sweep to prevent unbounded dictionary growth under
			// sustained connection churn.  The per-frame bounded scan (32 entries)
			// with cursor provides fair coverage; this periodic full pass ensures
			// stale entries are eventually cleaned up regardless of cursor position.
			if (Time.realtimeSinceStartup - lastFullAuthTimeoutSweepTime >= FullAuthTimeoutSweepIntervalSeconds)
			{
				lastFullAuthTimeoutSweepTime = Time.realtimeSinceStartup;
				PerformFullAuthTimeoutSweep(timeout, now);
			}
		}

		/// <summary>Timestamp of the last full auth-timeout sweep (seconds since startup).</summary>
		private float lastFullAuthTimeoutSweepTime = 0f;

		/// <summary>Interval in seconds between full sweeps of <see cref="connectionStartTimes"/>.</summary>
		private const float FullAuthTimeoutSweepIntervalSeconds = 30f;

		/// <summary>
		/// Cursor into the most recent snapshot of <see cref="connectionStartTimes"/>.
		/// Advances each frame by <c>maxScanPerFrame</c> entries to guarantee every entry
		/// is eventually visited, unlike <c>foreach</c> on a <see cref="ConcurrentDictionary{TKey,TValue}"/>
		/// which returns a snapshot-at-enumeration that may skip entries indefinitely
		/// under high connection churn.
		/// </summary>
		private int authTimeoutSweepCursor = 0;

		/// <summary>
		/// Most recent snapshot of <see cref="connectionStartTimes"/> for the per-frame
		/// bounded scan.  Refreshed when the cursor wraps around or on first use each cycle.
		/// </summary>
		private KeyValuePair<int, DateTime>[] authTimeoutSnapshot = null;

		/// <summary>
		/// Processes ALL entries in <see cref="connectionStartTimes"/> to remove stale
		/// entries that the per-frame bounded scan may have repeatedly skipped.
		/// Uses <see cref="ConcurrentDictionary{TKey,TValue}.ToArray"/> to obtain a
		/// stable snapshot; the allocation is acceptable at 30-second intervals.
		/// </summary>
		private void PerformFullAuthTimeoutSweep(TimeSpan timeout, DateTime now)
		{
			if (connectionStartTimes.IsEmpty) return;

			int removed = 0;
			foreach (var kvp in connectionStartTimes.ToArray())
			{
				int clientId = kvp.Key;
				DateTime started = kvp.Value;

				NetworkConnection conn = null;
				var clients = NetworkManager?.ServerManager?.Clients;
				if (clients != null)
					clients.TryGetValue(clientId, out conn);

				// Remove stale entries: connection gone or already authenticated.
				if (conn == null || conn.IsAuthenticated)
				{
					connectionStartTimes.TryRemove(clientId, out _);
					removed++;
				}
				// Timed-out connections: disconnect AND remove.
				else if (now - started > timeout)
				{
					_ = Log.Warning(LogPrefix,
						$"Connection {clientId} timed out waiting for ClientHandshake " +
						$"({(now - started).TotalSeconds:F1}s > {timeout.TotalSeconds:F1}s) — " +
						"disconnecting (full sweep).");
					connectionStartTimes.TryRemove(clientId, out _);
					try { conn.Disconnect(true); } catch { /* best effort */ }
					removed++;
				}
			}

			if (removed > 0)
			{
				_ = Log.Debug(LogPrefix,
					$"Full auth-timeout sweep removed {removed} stale entries " +
					$"({connectionStartTimes.Count} remaining).");
			}
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
		/// and processes the connection token for real-IP recovery.
		/// Accepts <paramref name="clientId"/> rather than a <see cref="NetworkConnection"/>
		/// reference because the connection may be disconnected and recycled by FishNet
		/// while this async method is suspended at an await point.</summary>
		internal async Task OnServerClientHandshakeReceivedAsync(int clientId, ClientHandshake msg, Channel channel)
		{
			// Resolve the connection at entry.  If it's already gone, bail.
			if (!TryResolveConnection(clientId, out var conn))
				return;

			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// Capture the connection-instance nonce NOW, before any await.
			// If the ClientId is recycled while we're suspended, the nonce
			// in the dictionary will not match, and we abort the handshake
			// rather than routing it to the wrong connection.
			connectionNonces.TryGetValue(clientId, out string? capturedNonce);

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

				// ── FIX #3: Validate protocol version range semantics ──
				// MinVersion/MaxVersion are ushort — wire-type safe but never
				// semantically validated before expensive crypto operations
				// (HMAC-SHA256 token verification, ECDH key agreement).
				// Reject invalid ranges here to avoid wasting CPU on
				// connections that will inevitably fail version negotiation.
				// Version 0 is reserved (indicates uninitialized client).
				if (msg.MinVersion == 0 || msg.MaxVersion == 0 || msg.MinVersion > msg.MaxVersion)
				{
					conn.Disconnect(true);
					return;
				}

			// Rate-limit handshake processing BEFORE the connection token HMAC
			// verification (which is CPU-bound). Without this gate, an attacker
			// can open a single QUIC connection and flood ClientHandshake messages
			// with forged tokens, consuming HMAC-SHA256 cycles without ever reaching
			// the capacity-limited SRP channels deeper in the core.
			// Uses the resolved real IP when available; falls back to transport
			// address or ClientId for initial connections where IP isn't yet known.
			//
			// IMPORTANT: Never use conn.GetAddress() as the fallback key when it
			// returns a loopback address (127.0.0.1, ::1). Behind an L4 UDP proxy
			// all connections appear to come from 127.0.0.1, which would make every
			// pre-authentication client share one rate-limit bucket — a single
			// attacker could saturate it and deny service to all legitimate clients.
			// Fall back to ClientId (unique per QUIC connection) instead.
			{
				string? rateLimitKey = ResolveRateLimitKey(conn);
				if (string.IsNullOrEmpty(rateLimitKey))
				{
					string addr = conn.GetAddress();
					if (!string.IsNullOrEmpty(addr) && !NetHelper.IsLoopbackAddress(addr))
						rateLimitKey = addr;
					else
						rateLimitKey = $"conn:{clientId}";
				}
				if (!handshakeRateLimiter.TryBegin(rateLimitKey, DateTime.UtcNow, HandshakeRateLimitInterval))
				{
					conn.Disconnect(true);
					return;
				}
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
					if (!await ProcessConnectionTokenAsync(clientId, msg.ConnectionToken))
					{
						if (TryResolveConnection(clientId, out conn))
							conn.Disconnect(true);
						return;
					}
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"ProcessConnectionTokenAsync threw for connection {clientId}: {ex.Message}");
					if (TryResolveConnection(clientId, out conn))
						conn.Disconnect(true);
					return;
				}
			}
			else
			{
				// No connection token provided — client is not coming through the
				// IPFetch proxy path. Disconnect immediately.
				await Log.Warning(LogPrefix, $"Connection {clientId} rejected: no connection token.");
				if (TryResolveConnection(clientId, out conn))
					conn.Disconnect(true);
				return;
			}

			// ── Re-resolve connection after await ──────────────────
			// ProcessConnectionTokenAsync yielded at an await.  The
			// connection may have been disconnected (e.g. by the auth
			// timeout sweep) or recycled while we were suspended.
			// Re-resolve from the server's client dictionary before
			// touching any connection state.
			if (!TryResolveConnection(clientId, out conn))
				return;

			// Nonce guard: ensure the connection wasn't recycled during
			// ProcessConnectionTokenAsync's await.
			if (!IsConnectionNonceValid(clientId, capturedNonce))
				return;

			// ── FIX #4: Re-check IsAuthenticated after await ──────
			// ProcessConnectionTokenAsync (above) and the rate-limit
			// resolution are both async operations that yield.  A
			// second ClientHandshake broadcast on the same connection
			// could have completed authentication between our initial
			// IsAuthenticated guard and this point.  Without this
			// re-check, duplicate handshakes enter the auth pipeline
			// and consume worker-channel capacity.
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			if (Core == null)
			{
				await Log.Warning(LogPrefix, $"Core is null during OnHandshakeReceived for connection {clientId} — handshake discarded. Ensure InitializeWorkers() was called before accepting connections.");
			}
			else
			{
				Core.OnHandshakeReceived(conn, msg.PublicKey, msg.Cookie, msg.ConnectionToken, msg.MinVersion, msg.MaxVersion, msg.GameVersion ?? "");
			}
		}

		/// <summary>
		/// Validates a connection token against the database and stores the real IP
		/// for rate-limiting and logging. Fire-and-forget — does not block the handshake.
		/// Accepts <paramref name="clientId"/> rather than a <see cref="NetworkConnection"/>
		/// because the caller may have yielded at an await and the original connection
		/// reference may be stale.
		/// </summary>
		/// <returns>true if the real IP was successfully recovered; false if the client should be disconnected.</returns>
		private async Task<bool> ProcessConnectionTokenAsync(int clientId, string rawToken)
		{
			CancellationToken ct = shutdownToken;
			if (ct.IsCancellationRequested) return false;

			var server = Server;
			if (server?.DataContainerRegistry == null ||
				!server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out _))
				return false;

			// Try stateless HMAC verification (format: payloadB64.sigB64).
			// Uses the database-backed key map — no environment variable or .cfg file fallbacks.
			string? realIp = TryVerifyStatelessConnectionToken(rawToken);
			if (realIp != null)
			{
				StoreRealIpForConnection(clientId, realIp);
				await Log.Debug(LogPrefix, $"Real IP {realIp} recovered via HMAC token for connection {clientId}.");
				return true;
			}

			// Legacy DB-backed token path removed (IConnectionTokenService deleted
			// when connection tokens migrated to stateless HMAC).
			await Log.Warning(LogPrefix, $"Legacy connection token not supported for {clientId}.");
			return false;
		}

		/// <summary>
		/// Resolves a <see cref="NetworkConnection"/> from its ClientId using the
		/// server's active client dictionary.  Returns <c>false</c> if the connection
		/// has been disconnected or the server is not running.
		/// </summary>
		private bool TryResolveConnection(int clientId, out NetworkConnection conn)
		{
			conn = null;
			var clients = NetworkManager?.ServerManager?.Clients;
			return clients != null && clients.TryGetValue(clientId, out conn) && conn != null;
		}

		/// <summary>
		/// Validates that the connection identified by <paramref name="clientId"/> still
		/// has the same instance nonce that was captured before the first await in
		/// <see cref="OnServerClientHandshakeReceivedAsync"/>.  If the nonce does not
		/// match (or is missing), the ClientId was recycled while we were suspended,
		/// and the handshake must be aborted to avoid routing auth data to the wrong
		/// connection.
		/// </summary>
		/// <returns>True if the nonce matches or no nonce was captured (pre-nonce server); false if the connection was recycled.</returns>
		private bool IsConnectionNonceValid(int clientId, string? capturedNonce)
		{
			if (capturedNonce == null) return true; // Pre-nonce server — no validation possible
			return connectionNonces.TryGetValue(clientId, out string? currentNonce) &&
			       currentNonce == capturedNonce;
		}

		/// <summary>
		/// Thread-safe store for connection token keys loaded from the database.
		/// Populated by <see cref="LoadConnectionTokenKeysFromDbAsync"/> and refreshed
		/// every 60 seconds by <see cref="KeyRefreshLoop"/>.
		/// The database is the ONLY source — no environment variable or .cfg file fallbacks.
		/// </summary>
		private static volatile Dictionary<string, byte[]>? s_dbConnectionTokenKeyMap;
		private static readonly object s_dbConnectionTokenKeyMapLock = new();


		/// <summary>
		/// Loads all active connection token keys from the database into the
		/// <see cref="s_dbConnectionTokenKeyMap"/> static field. Uses the
		/// <see cref="IConnectionTokenKeyService"/> resolved from the server's
		/// database service registry.
		/// This is called once on startup and then periodically by <see cref="KeyRefreshLoop"/>.
		/// The database is the ONLY source — no environment variable or .cfg file fallbacks exist.
		/// If the database is unavailable, connection token verification will fail.
		/// </summary>
		private async Task LoadConnectionTokenKeysFromDbAsync()
		{
			try
			{
				var server = Server;
				if (server?.Database?.ServiceRegistry == null)
				{
					await Log.Debug(LogPrefix, "Database not available yet — skipping connection token key load.");
					return;
				}

				if (!server.Database.ServiceRegistry.TryGet<IConnectionTokenKeyService>(out var service))
				{
					await Log.Error(LogPrefix,
						"CRITICAL: IConnectionTokenKeyService not registered in database service registry. " +
						"Connection token keys must come from the database — no environment variable or " +
						".cfg file fallbacks exist. Ensure the service assembly is loaded.");
					return;
				}

				// Check cancellation before the DB call.
				var ct = shutdownToken;
				if (ct.IsCancellationRequested) return;

				var result = await service.FetchAllActiveAsync(ct);
				if (result.IsSuccess && result.Data != null && result.Data.Length > 0)
				{
					var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
					foreach (var keyData in result.Data)
					{
						if (keyData.HmacKey != null && keyData.HmacKey.Length >= 32)
						{
							map[keyData.KeyId] = keyData.HmacKey;
						}
						else
						{
							await Log.Warning(LogPrefix,
								$"Skipping connection token key '{keyData.KeyId}' from database: " +
								$"HMAC key is {(keyData.HmacKey == null ? "null" : $"too short ({keyData.HmacKey.Length} bytes)")}.");
						}
					}

					lock (s_dbConnectionTokenKeyMapLock)
					{
						s_dbConnectionTokenKeyMap = map;
					}
					await Log.Debug(LogPrefix, $"Loaded {map.Count} active connection token key(s) from database.");
				}
				else if (result.IsSuccess)
				{
					// No active keys in DB — clear any stale map so we don't use rotated-out keys.
					lock (s_dbConnectionTokenKeyMapLock)
					{
						s_dbConnectionTokenKeyMap = null;
					}
					await Log.Warning(LogPrefix,
						"Database returned zero active connection token keys. " +
						"No connection token keys are available — token verification will fail.");
				}
				else
				{
					await Log.Warning(LogPrefix,
						$"Failed to fetch connection token keys from database: " +
						$"[{result.ErrorCode}] {result.ErrorMessage}. " +
						"No connection token keys are available — token verification will fail.");
				}
			}
			catch (OperationCanceledException)
			{
				// Shutdown in progress — not actionable.
			}
			catch (Exception ex)
			{
				await Log.Warning(LogPrefix,
					$"Exception loading connection token keys from database: {ex.Message}. " +
					"No connection token keys are available — token verification will fail.");
			}
		}

		/// <summary>
		/// Periodic background loop that refreshes the DB-loaded connection token key map
		/// every 60 seconds. Runs for the lifetime of the server, cooperatively cancelling
		/// via <paramref name="ct"/>.
		/// </summary>
		private async Task KeyRefreshLoop(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(60), ct);
					await LoadConnectionTokenKeysFromDbAsync();
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix,
						$"Unhandled exception in connection token key refresh loop: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Attempts to verify a stateless HMAC-signed connection token.
		/// Supports format: base64url(keyId:realIp|expiryUnix).base64url(HMAC-SHA256(key, payload))
		/// The keyId is used to look up the verification key from the database-backed key map.
		/// Returns the real IP on success, null if the token is invalid/expired
		/// or the HMAC key is not available.
		/// </summary>
		private static string? TryVerifyStatelessConnectionToken(string rawToken)
		{
			if (string.IsNullOrEmpty(rawToken)) return null;
			int dotIdx = rawToken.LastIndexOf('.');
			if (dotIdx <= 0 || dotIdx >= rawToken.Length - 1) return null;

			try
			{
				var payloadB64 = rawToken.Substring(0, dotIdx).Replace('-', '+').Replace('_', '/');
				var sigB64 = rawToken.Substring(dotIdx + 1).Replace('-', '+').Replace('_', '/');
				// Restore Base64 padding
				while (payloadB64.Length % 4 != 0) payloadB64 += "=";
				while (sigB64.Length % 4 != 0) sigB64 += "=";

				var payload = Convert.FromBase64String(payloadB64);
				var expectedSig = Convert.FromBase64String(sigB64);

				var payloadStr = Encoding.UTF8.GetString(payload);
				int pipeIdx = payloadStr.LastIndexOf('|');
				if (pipeIdx <= 0) return null;

				// Determine token format by checking for a keyId prefix.
				// Format: "keyId:realIp|expiryUnix" - first colon separates
				// keyId from IP (keyId is looked up in the database-backed key map).
				// Safe for IPv6: we only treat the prefix as a keyId if it is
				// actually found in the key map.  This avoids rejecting tokens where an
				// IPv6 address segment happens to be separated by a colon.
				int colonIdx = payloadStr.IndexOf(':');
				bool hasKeyId = false;

				byte[]? hmacKey = null;
				if (colonIdx > 0 && colonIdx < pipeIdx)
				{
					string potentialKeyId = payloadStr.Substring(0, colonIdx);
					// Keys come SOLELY from the database — no environment variable or
					// .cfg file fallbacks.
					var keyMap = s_dbConnectionTokenKeyMap;
					hasKeyId = keyMap != null && keyMap.TryGetValue(potentialKeyId, out hmacKey);
				}
				else
				{
					// No keyId prefix in the payload — use the default shared key.
					// This is the path taken when IpFetchServer.GetConnectionTokenKeyId()
					// returns null/empty and the payload is simply "realIp|expiryUnix".
					//
					// NOTE: IPv6 addresses in the realIp position contain colons that
					// would be caught by the keyId branch above.  The first colon is
					// interpreted as a keyId separator, and when TryGetValue fails
					// (because "2001" is not a registered keyId), hmacKey stays null.
					// For single-region deployments, setting GetConnectionTokenKeyId()
					// to "shared" (non-null) produces "shared:2001:db8::1|..." —
					// unambiguous regardless of IP version.
					var keyMap = s_dbConnectionTokenKeyMap;
					hmacKey = keyMap != null && keyMap.TryGetValue("shared", out var defaultKey)
						? defaultKey : null;
				}

				if (hmacKey == null) return null;

				using var hmac = new HMACSHA256(hmacKey);
				var computedSig = hmac.ComputeHash(payload);
				if (!CryptographicOperations.FixedTimeEquals(computedSig, expectedSig))
					return null;

				// Extract the real IP from the payload.
				string realIp;
				if (hasKeyId)
				{
					realIp = payloadStr.Substring(colonIdx + 1, pipeIdx - colonIdx - 1);
				}
				else
				{
					realIp = payloadStr.Substring(0, pipeIdx);
				}

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
			if (args.ConnectionState == RemoteConnectionState.Started)
			{
				// Record the connection start time for the authentication timeout sweep.
				// TryAdd is a no-op if the key already exists (defense against duplicate Started events).
				connectionStartTimes.TryAdd(conn.ClientId, DateTime.UtcNow);
				// Generate a random nonce to bind this handshake to this specific
				// connection instance.  If the ClientId is recycled during an await
				// in OnServerClientHandshakeReceivedAsync, the nonce check detects
				// the mismatch and aborts safely.  TryAdd is a no-op on duplicate
				// Started events (same defense as connectionStartTimes).
				connectionNonces.TryAdd(conn.ClientId, Guid.NewGuid().ToString("N"));
			}
			else if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				// Clean up the auth timeout tracker and nonce — the connection is
				// gone regardless of whether it authenticated successfully.
				connectionStartTimes.TryRemove(conn.ClientId, out _);
				connectionNonces.TryRemove(conn.ClientId, out _);

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