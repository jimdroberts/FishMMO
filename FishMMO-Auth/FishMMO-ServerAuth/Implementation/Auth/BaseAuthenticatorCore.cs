using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Abstract engine-independent base for all server authenticators.
	/// Provides X25519 ECDH handshake logic, stale-auth TTL sweeps, per-IP and global
	/// handshake rate limiting, and connection auth-state tracking — with no dependency
	/// on Unity, FishNet, or any game-engine type.
	/// <para>
	/// Concrete implementations supply transport-specific callbacks (broadcast, disconnect,
	/// IP resolution) by implementing the abstract members, then call
	/// <see cref="OnHandshakeReceived"/> from their transport layer.
	/// </para>
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public abstract class BaseAuthenticatorCore<TConnection>
	{
		/// <summary>Authentication TTL in seconds. Connections that do not complete auth within this window are purged.</summary>
		protected const float AuthStaleTtlSeconds = 15f;

		/// <summary>
		/// Hard deadline in seconds for any single authentication attempt.
		/// <see cref="RefreshAuthTtl"/> will not extend a connection's TTL beyond this
		/// absolute limit from its original start time, preventing unbounded TTL extension.
		/// </summary>
		protected const float AuthHardDeadlineSeconds = 60f;

		/// <summary>Maximum number of concurrent pending authentication connections.</summary>
		protected const int MaxPendingAuthConnections = 10000;

		/// <summary>Minimum seconds between handshake attempts from the same remote IP (or connection ID).</summary>
		protected const float HandshakeIpDebounceSeconds = 0.25f;

		/// <summary>Maximum X25519 handshakes accepted in a single 1-second window.</summary>
		protected const int MaxGlobalHandshakesPerSecond = 500;

		/// <summary>Maximum stale auth entries scanned per sweep.</summary>
		protected const int AuthSweepMaxScan = 256;

		/// <summary>Maximum stale auth entries purged per sweep.</summary>
		protected const int AuthSweepMaxRemovals = 64;

		/// <summary>Maximum entries scanned per handshake rate-limit sweep.</summary>
		protected const int HandshakeRateLimitSweepMaxScan = 4096;

		/// <summary>Maximum entries removed per handshake rate-limit sweep.</summary>
		protected const int HandshakeRateLimitSweepMaxRemovals = 2048;

		/// <summary>
		/// Tracks authentication start times for stale-auth TTL enforcement.
		/// Key: connection identifier, Value: UTC start timestamp.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> authStartTimeByClientId = new ConcurrentDictionary<int, DateTime>();

		/// <summary>Tracks original authentication start times for hard deadline enforcement.</summary>
		private readonly ConcurrentDictionary<int, DateTime> authOriginalStartByClientId = new ConcurrentDictionary<int, DateTime>();

		/// <summary>Reverse map from client ID to active connection for stale-auth cleanup.</summary>
		private readonly ConcurrentDictionary<int, TConnection> authConnectionByClientId = new ConcurrentDictionary<int, TConnection>();

		/// <summary>Per-IP handshake rate limiter. Prevents X25519 CPU abuse from rapid handshake replay.</summary>
		private readonly ConcurrentDictionary<string, DateTime> handshakeIpNextAllowedUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// HMAC-SHA256 key for stateless handshake cookies.
		/// Generated when workers start; zeroed on shutdown.
		/// Volatile for cross-thread visibility between main thread and network thread.
		/// </summary>
		private volatile byte[]? cookieHmacKey;

		/// <summary>Rolling count of completed X25519 handshakes in the current 1-second window.</summary>
		private int globalHandshakeCount;

		/// <summary>UTC instant when the current global handshake window expires and the counter resets.</summary>
		private DateTime nextGlobalHandshakeResetUtc = DateTime.UtcNow.AddSeconds(1);

		/// <summary>Rate limiter for the pending auth cap warning log.</summary>
		private DateTime nextPendingAuthCapWarningUtc;

		/// <summary>The account manager for this authenticator.</summary>
		protected IAccountManager<TConnection> AccountManager { get; private set; }

		/// <summary>Log source tag used in all log messages emitted by this core.</summary>
		protected virtual string LogPrefix => GetType().Name;

		/// <summary>
		/// Initializes the core with the required account manager.
		/// </summary>
		/// <param name="accountManager">The account manager instance.</param>
		protected BaseAuthenticatorCore(IAccountManager<TConnection> accountManager)
		{
			AccountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
		}

		#region Worker Lifecycle

		/// <summary>
		/// Generates a fresh cookie HMAC key and starts protocol-specific workers.
		/// Must be called before accepting connections.
		/// </summary>
		/// <param name="cancellationToken">Token for signalling graceful shutdown.</param>
		public void InitializeWorkers(CancellationToken cancellationToken)
		{
			cookieHmacKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength);
			InitializeWorkersCore(cancellationToken);
		}

		/// <summary>
		/// Subclass-specific worker initialization: create channels and start worker tasks.
		/// Called after the base generates the cookie key.
		/// </summary>
		/// <param name="cancellationToken">Token for signalling worker shutdown.</param>
		protected abstract void InitializeWorkersCore(CancellationToken cancellationToken);

		/// <summary>
		/// Gracefully shuts down all async workers and zeroes sensitive key material.
		/// Calls <see cref="ShutdownWorkersCore"/> first to allow subclasses to complete
		/// channel writers for graceful worker exit.
		/// </summary>
		public void ShutdownWorkers()
		{
			ShutdownWorkersCore();

			if (cookieHmacKey != null)
			{
				CryptographicOperations.ZeroMemory(cookieHmacKey);
				cookieHmacKey = null;
			}

			Interlocked.Exchange(ref globalHandshakeCount, 0);
			nextGlobalHandshakeResetUtc = DateTime.UtcNow.AddSeconds(1);
			authStartTimeByClientId.Clear();
			authOriginalStartByClientId.Clear();
			authConnectionByClientId.Clear();
			handshakeIpNextAllowedUtc.Clear();
		}

		/// <summary>
		/// Subclass-specific worker shutdown: complete channel writers, null channel references,
		/// and clear subclass-specific state. Called BEFORE the base zeroes the cookie key.
		/// </summary>
		protected abstract void ShutdownWorkersCore();

		#endregion

		#region Periodic Sweeps

		/// <summary>
		/// Runs the stale-auth TTL sweep and the handshake rate-limit sweep.
		/// Must be called periodically by the hosting environment (e.g., every server tick or Update frame).
		/// </summary>
		public void Tick()
		{
			SweepStaleAuthentication();
			SweepExpiredHandshakeRateLimits();
			ResetGlobalHandshakeWindowIfExpired();
			OnTick();
		}

		/// <summary>
		/// Override for subclass-specific per-tick logic (e.g., additional periodic sweeps).
		/// </summary>
		protected virtual void OnTick() { }

		/// <summary>
		/// Override for subclass-specific logic that runs alongside the stale-auth sweep.
		/// </summary>
		protected virtual void OnAuthSweep() { }

		/// <summary>
		/// Resets the global handshake counter when the 1-second window expires.
		/// Wall-clock based so a single long tick cannot silently extend the window.
		/// </summary>
		private void ResetGlobalHandshakeWindowIfExpired()
		{
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow >= nextGlobalHandshakeResetUtc)
			{
				nextGlobalHandshakeResetUtc = utcNow.AddSeconds(1);
				Interlocked.Exchange(ref globalHandshakeCount, 0);
			}
		}

		/// <summary>
		/// Disconnects and purges connections that exceeded the authentication TTL window
		/// without completing authentication. Scans are bounded by <see cref="AuthSweepMaxScan"/>
		/// and <see cref="AuthSweepMaxRemovals"/> to keep call-site cost predictable.
		/// </summary>
		private void SweepStaleAuthentication()
		{
			if (authStartTimeByClientId.Count == 0) return;

			DateTime now = DateTime.UtcNow;
			int scanned = 0;
			int removed = 0;

			foreach (var kvp in authStartTimeByClientId)
			{
				if (scanned >= AuthSweepMaxScan || removed >= AuthSweepMaxRemovals)
					break;

				scanned++;

				if ((now - kvp.Value).TotalSeconds < AuthStaleTtlSeconds)
					continue;

				if (!authStartTimeByClientId.TryRemove(kvp.Key, out _))
					continue;

				removed++;

				authOriginalStartByClientId.TryRemove(kvp.Key, out _);
				authConnectionByClientId.TryRemove(kvp.Key, out TConnection conn);

				if (conn != null)
				{
					if (IsConnectionAuthenticated(conn))
					{
						_ = Log.Warning(LogPrefix, $"SweepStaleAuthentication: id {kvp.Key} is authenticated (likely recycled) — skipping purge.");
						continue;
					}

					OnPurgeConnectionState(conn);
					AccountManager.RemoveConnectionAccount(conn);
					DisconnectConnection(conn, graceful: false);
				}
				else
				{
					_ = Log.Warning(LogPrefix, $"SweepStaleAuthentication: conn was null for id {kvp.Key} — dictionary entries cleaned.");
				}
			}

			OnAuthSweep();
		}

		/// <summary>
		/// Removes expired per-IP handshake rate-limit entries to prevent unbounded dictionary growth.
		/// </summary>
		private void SweepExpiredHandshakeRateLimits()
		{
			if (handshakeIpNextAllowedUtc.Count == 0) return;

			DateTime now = DateTime.UtcNow;
			int scanned = 0;
			int removed = 0;

			foreach (var kvp in handshakeIpNextAllowedUtc)
			{
				if (scanned >= HandshakeRateLimitSweepMaxScan || removed >= HandshakeRateLimitSweepMaxRemovals)
					break;

				scanned++;

				if (now >= kvp.Value)
				{
					handshakeIpNextAllowedUtc.TryRemove(kvp.Key, out _);
					removed++;
				}
			}
		}

		#endregion

		#region Handshake

		/// <summary>
		/// Processes an incoming client handshake. Must be called from the transport layer
		/// (e.g., on receipt of a <c>ClientHandshakeBroadcast</c>).
		/// Implements a two-phase stateless cookie challenge followed by X25519 ECDH key agreement.
		/// Runs with no blocking I/O — safe to call on a network-receive thread.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="publicKey">Client's X25519 ephemeral public key (32 bytes). Must not be null.</param>
		/// <param name="cookie">Cookie echoed from a prior challenge, or null on first attempt.</param>
		/// <param name="minVersion">Minimum protocol version supported by the client.</param>
		/// <param name="maxVersion">Maximum protocol version supported by the client.</param>
		public void OnHandshakeReceived(TConnection conn, byte[] publicKey, byte[] cookie, ushort minVersion, ushort maxVersion)
		{
			if (IsConnectionAuthenticated(conn) ||
				publicKey == null ||
				publicKey.Length != CryptoHelper.X25519PublicKeyLength)
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (AccountManager.GetConnectionEncryptionData(conn, out _))
				return;

			if (AccountManager.IsAuthInProgress(conn))
				return;

			byte[]? hmacKeySnapshot = cookieHmacKey;
			if (hmacKeySnapshot == null)
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// Reject the X25519 all-zero (identity/low-order) point — an all-zero public key
			// always yields an all-zero shared secret regardless of the server's private key
			// (RFC 7748 §6.1), breaking ECDH forward secrecy entirely.
			if (IsAllZeroX25519Key(publicKey))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// ── Phase 1: Cookie challenge ────────────────────────────────
			if (cookie == null)
			{
				// Enforce protocol version intersection before issuing a cookie.
				try
				{
					CryptoHelper.NegotiateProtocolVersion(minVersion, maxVersion);
				}
				catch (Exception)
				{
					DisconnectConnection(conn, graceful: true);
					return;
				}
				string challengeIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
				byte[] challengeCookie = HandshakeService.ComputeHandshakeCookie(challengeIp, publicKey, HandshakeService.GetTimeBucket(), hmacKeySnapshot);
				BroadcastCookieChallenge(conn, challengeCookie);
				return;
			}

			// ── Phase 2: Cookie verification ────────────────────────────
			string remoteIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
			if (!HandshakeService.VerifyHandshakeCookieWithRollover(cookie, remoteIp, publicKey, hmacKeySnapshot))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// ── Per-IP rate limit ─────────────────────────────────────────
			string rateLimitKey = ResolveRateLimitKey(conn);
			if (!string.IsNullOrEmpty(rateLimitKey))
			{
				DateTime nowUtc = DateTime.UtcNow;
				if (handshakeIpNextAllowedUtc.TryGetValue(rateLimitKey, out DateTime nextAllowed) && nowUtc < nextAllowed)
				{
					DisconnectConnection(conn, graceful: true);
					return;
				}
				handshakeIpNextAllowedUtc[rateLimitKey] = nowUtc.AddSeconds(HandshakeIpDebounceSeconds);
			}

			// ── Global rate limit ─────────────────────────────────────────
			if (Interlocked.Increment(ref globalHandshakeCount) > MaxGlobalHandshakesPerSecond)
			{
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			// Begin TTL tracking after all rate-limit gates have passed.
			if (!TrackAuthStart(conn))
			{
				DateTime capNow = DateTime.UtcNow;
				if (capNow >= nextPendingAuthCapWarningUtc)
				{
					nextPendingAuthCapWarningUtc = capNow.AddSeconds(5);
					_ = Log.Warning(LogPrefix, $"Pending auth cap ({MaxPendingAuthConnections}) reached — handshake(s) dropped.");
				}
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			// ── X25519 ECDH key agreement ─────────────────────────────────
			var kaResult = HandshakeService.ServerPerformKeyAgreement(publicKey, minVersion, maxVersion);
			if (!kaResult.Success)
			{
				Interlocked.Decrement(ref globalHandshakeCount);
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (!AccountManager.TryAddConnectionEncryptionData(conn, publicKey))
			{
				ClearTransientAuthState(GetConnectionClientId(conn));
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			if (AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				encryptionData.AgreedVersion = kaResult.AgreedVersion;
				encryptionData.PromoteToDirectional(kaResult.SessionKeys);
				BroadcastServerHandshake(conn, kaResult.ServerPublicKey, kaResult.AgreedVersion);
			}
			else
			{
				_ = Log.Warning(LogPrefix, "Failed to retrieve encryption data after handshake registration.");
				DisconnectConnection(conn, graceful: true);
			}
		}

		#endregion

		#region Auth Tracking

		/// <summary>
		/// Starts auth TTL tracking for a connection if not already tracked.
		/// Returns <c>false</c> if the pending authentication cap has been reached.
		/// </summary>
		/// <param name="conn">Connection entering the authentication flow.</param>
		/// <returns><c>true</c> if tracking was started; <c>false</c> if the cap was reached.</returns>
		protected bool TrackAuthStart(TConnection conn)
		{
			if (conn == null) return false;
			if (authStartTimeByClientId.Count >= MaxPendingAuthConnections)
				return false;
			DateTime now = DateTime.UtcNow;
			int clientId = GetConnectionClientId(conn);
			authStartTimeByClientId.TryAdd(clientId, now);
			authOriginalStartByClientId.TryAdd(clientId, now);
			authConnectionByClientId[clientId] = conn;
			return true;
		}

		/// <summary>
		/// Resets the TTL timestamp for a tracked connection to <see cref="DateTime.UtcNow"/>.
		/// Call from async workers at meaningful progress points to prevent premature sweeping.
		/// Refuses to refresh beyond <see cref="AuthHardDeadlineSeconds"/> from original start.
		/// </summary>
		/// <param name="conn">Connection whose TTL to refresh.</param>
		protected void RefreshAuthTtl(TConnection conn)
		{
			if (conn == null) return;
			int clientId = GetConnectionClientId(conn);
			if (authOriginalStartByClientId.TryGetValue(clientId, out DateTime originalStart))
			{
				if ((DateTime.UtcNow - originalStart).TotalSeconds >= AuthHardDeadlineSeconds)
					return;
			}
			authStartTimeByClientId[clientId] = DateTime.UtcNow;
		}

		/// <summary>
		/// Clears transient per-connection authenticator TTL tracking state.
		/// </summary>
		/// <param name="clientId">Connection client ID.</param>
		protected void ClearTransientAuthState(int clientId)
		{
			authStartTimeByClientId.TryRemove(clientId, out _);
			authOriginalStartByClientId.TryRemove(clientId, out _);
			authConnectionByClientId.TryRemove(clientId, out _);
		}

		/// <summary>
		/// Purges all authenticator state for a connection and optionally disconnects it.
		/// TTL tracking is cleared before disconnect to prevent races.
		/// </summary>
		/// <param name="conn">Connection to purge.</param>
		/// <param name="disconnect">If true, disconnect the client after purge.</param>
		protected void PurgeConnectionAuthState(TConnection conn, bool disconnect)
		{
			if (conn == null) return;
			ClearTransientAuthState(GetConnectionClientId(conn));
			OnPurgeConnectionState(conn);
			AccountManager.RemoveConnectionAccount(conn);
			if (disconnect)
				DisconnectConnection(conn, graceful: false);
		}

		/// <summary>
		/// Called by the hosting transport layer when a connection has been disconnected.
		/// Purges all authenticator state for the connection without disconnecting (already stopped).
		/// </summary>
		/// <param name="conn">The stopped connection.</param>
		public void HandleConnectionStopped(TConnection conn)
		{
			PurgeConnectionAuthState(conn, disconnect: false);
		}

		/// <summary>
		/// Override for subclass-specific cleanup during connection purge.
		/// Called before <c>AccountManager.RemoveConnectionAccount</c> but after
		/// <see cref="ClearTransientAuthState"/> has removed TTL tracking.
		/// </summary>
		/// <param name="conn">The connection being purged.</param>
		protected virtual void OnPurgeConnectionState(TConnection conn) { }

		#endregion

		#region Rate Limit Key Resolution

		/// <summary>
		/// Resolves a rate-limit key for a connection.
		/// Override to return a connection-ID string in proxy/NAT deployments where all
		/// clients share the same transport-level IP.
		/// Default: returns the normalized remote IP address.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <returns>A string key suitable for per-identity rate limiting.</returns>
		protected virtual string ResolveRateLimitKey(TConnection conn)
		{
			return HandshakeService.NormalizeIp(GetConnectionAddress(conn));
		}

		#endregion

		#region Static Helpers

		/// <summary>
		/// Returns <c>true</c> if every byte in <paramref name="key"/> is zero.
		/// Used to detect the X25519 identity (all-zero / low-order) point, which
		/// always produces an all-zero shared secret regardless of the other party's
		/// private key (RFC 7748 §6.1).
		/// </summary>
		private static bool IsAllZeroX25519Key(byte[] key)
		{
			for (int i = 0; i < key.Length; i++)
				if (key[i] != 0) return false;
			return true;
		}

		#endregion

		#region Abstract Transport Callbacks

		/// <summary>
		/// Returns whether this connection has already completed authentication.
		/// Called on the network-receive thread — must be thread-safe and non-blocking.
		/// </summary>
		protected abstract bool IsConnectionAuthenticated(TConnection conn);

		/// <summary>
		/// Returns the remote IP address (or equivalent string identifier) for the connection.
		/// Used for cookie challenge IP binding and rate limiting.
		/// </summary>
		protected abstract string GetConnectionAddress(TConnection conn);

		/// <summary>
		/// Returns the numeric client ID for the connection (e.g., FishNet <c>ClientId</c>).
		/// Used as the key for TTL tracking dictionaries.
		/// </summary>
		protected abstract int GetConnectionClientId(TConnection conn);

		/// <summary>
		/// Sends a cookie-challenge <c>ServerHandshake</c> response to the client.
		/// Called on the network-receive thread — must be non-blocking.
		/// </summary>
		/// <param name="conn">The target connection.</param>
		/// <param name="cookie">The HMAC cookie to send.</param>
		protected abstract void BroadcastCookieChallenge(TConnection conn, byte[] cookie);

		/// <summary>
		/// Sends the final <c>ServerHandshake</c> response (with the server's X25519 public key)
		/// to complete ECDH key agreement.
		/// Called on the network-receive thread — must be non-blocking.
		/// </summary>
		/// <param name="conn">The target connection.</param>
		/// <param name="serverPublicKey">Server's ephemeral X25519 public key.</param>
		/// <param name="agreedVersion">Negotiated protocol version.</param>
		protected abstract void BroadcastServerHandshake(TConnection conn, byte[] serverPublicKey, ushort agreedVersion);

		/// <summary>
		/// Disconnects the specified connection.
		/// </summary>
		/// <param name="conn">The connection to disconnect.</param>
		/// <param name="graceful">If true, attempt a graceful close; otherwise force-close immediately.</param>
		protected abstract void DisconnectConnection(TConnection conn, bool graceful);

		#endregion
	}
}