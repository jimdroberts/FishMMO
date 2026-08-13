using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

		/// <summary>
		/// Duration of the per-IP Phase-2 handshake measurement window.
		/// Combined with <see cref="HandshakeIpBurstLimit"/> this sustains 4 completed
		/// handshakes/second/IP (unchanged from the previous single-deadline debounce)
		/// while allowing a burst of near-simultaneous completions from one IP.
		/// </summary>
		protected const float HandshakeIpWindowSeconds = 2f;

		/// <summary>
		/// Maximum Phase-2 handshake completions accepted from one IP inside
		/// <see cref="HandshakeIpWindowSeconds"/>. The old fixed 0.25 s debounce keyed
		/// the whole handshake round trip on one interval: any second completion inside
		/// the window — a player behind the same NAT as another, or a re-login whose
		/// connect+token+challenge cycle finishes faster than the window on a sub-10 ms
		/// link — was silently disconnected. A burst of 8 covers the legitimate worst
		/// case (a household logging in together, a fast reconnect loop) without
		/// meaningfully weakening the sustained per-IP throttle the limiter exists for.
		/// </summary>
		protected const int HandshakeIpBurstLimit = 8;

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
		/// Synchronization gate for <see cref="authStartTimeByClientId"/>,
		/// <see cref="authOriginalStartByClientId"/>, and <see cref="authConnectionByClientId"/>.
		/// All access to these three dictionaries must acquire this lock first.
		/// Never acquired while holding <see cref="handshakeCountGate"/> or
		/// <c>AccountManager.SyncRoot</c> — disconnect/cleanup calls that need those
		/// locks are performed outside the critical section.
		/// </summary>
		private readonly object ttlGate = new object();

		/// <summary>
		/// Synchronization gate for <see cref="globalHandshakeCount"/> and
		/// <see cref="nextGlobalHandshakeResetUtc"/>. All read/write access to
		/// these two fields must acquire this lock first.
		/// </summary>
		private readonly object handshakeCountGate = new object();

		/// <summary>
		/// Tracks authentication start times for stale-auth TTL enforcement.
		/// Key: connection identifier, Value: UTC start timestamp.
		/// Guarded by <see cref="ttlGate"/>.
		/// </summary>
		private readonly Dictionary<int, DateTime> authStartTimeByClientId = new Dictionary<int, DateTime>();

		/// <summary>Tracks original authentication start times for hard deadline enforcement.
		/// Guarded by <see cref="ttlGate"/>.</summary>
		private readonly Dictionary<int, DateTime> authOriginalStartByClientId = new Dictionary<int, DateTime>();

		/// <summary>Reverse map from client ID to active connection for stale-auth cleanup.
		/// Guarded by <see cref="ttlGate"/>.</summary>
		private readonly Dictionary<int, TConnection> authConnectionByClientId = new Dictionary<int, TConnection>();

		/// <summary>
		/// Per-IP Phase-2 handshake limiter with a burst allowance. Tracks completed
		/// X25519 handshakes per remote IP inside a fixed measurement window instead of
		/// a single debounce deadline, so a burst of legitimate near-simultaneous
		/// handshakes from one IP (players behind one NAT, a fast re-login on a
		/// sub-10 ms link) is accepted while a sustained flood is still throttled.
		/// See <see cref="HandshakeIpWindowSeconds"/> and <see cref="HandshakeIpBurstLimit"/>.
		/// </summary>
		private readonly ConcurrentDictionary<string, HandshakeIpWindow> handshakeIpWindows = new ConcurrentDictionary<string, HandshakeIpWindow>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Sliding-window burst state for one rate-limit key in <see cref="handshakeIpWindows"/>.
		/// A rejected attempt never touches the window (no sliding extension), mirroring the
		/// anti-hammer property of the previous single-deadline debounce.
		/// </summary>
		private struct HandshakeIpWindow
		{
			public int Count;
			public DateTime WindowStartUtc;
		}

		/// <summary>
		/// HMAC-SHA256 key for stateless handshake cookies.
		/// Generated when workers start; zeroed on shutdown.
		/// Volatile for cross-thread visibility between main thread and network thread.
		/// </summary>
		private volatile byte[]? cookieHmacKey;

		/// <summary>Rolling count of completed X25519 handshakes in the current 1-second window.
		/// Guarded by <see cref="handshakeCountGate"/>.</summary>
		private int globalHandshakeCount;

		/// <summary>UTC instant when the current global handshake window expires and the counter resets.
		/// Guarded by <see cref="handshakeCountGate"/>.</summary>
		private DateTime nextGlobalHandshakeResetUtc = DateTime.UtcNow.AddSeconds(1);

		/// <summary>Rate limiter for the pending auth cap warning log.</summary>
		private DateTime nextPendingAuthCapWarningUtc;

		/// <summary>The account manager for this authenticator.</summary>
		protected IAccountManager<TConnection> AccountManager { get; private set; }

		/// <summary>
		/// Expected game version string (e.g. "0.1.0"). Set by the server host before
		/// connections are accepted. If null or empty, game version validation is skipped
		/// (development safety). When set, clients with a mismatched <c>ClientHandshake.GameVersion</c>
		/// are rejected with <see cref="ClientAuthenticationResult.VersionMismatch"/>.
		/// </summary>
		public string ExpectedGameVersion { get; set; } = "";

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

			lock (handshakeCountGate)
			{
				globalHandshakeCount = 0;
				nextGlobalHandshakeResetUtc = DateTime.UtcNow.AddSeconds(1);
			}
			lock (ttlGate)
			{
				authStartTimeByClientId.Clear();
				authOriginalStartByClientId.Clear();
				authConnectionByClientId.Clear();
			}
			handshakeIpWindows.Clear();
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
		/// Returns true when no async worker operations are in-flight.
		/// Subclasses with bounded-channel workers should override to check
		/// channel emptiness. Default returns true.
		/// </summary>
		public virtual bool IsWorkerIdle => true;

		/// <summary>
		/// Override for subclass-specific per-tick logic (e.g., additional periodic sweeps).
		/// </summary>
		protected virtual void OnTick() { }

		/// <summary>
		/// Override for subclass-specific logic that runs alongside the stale-auth sweep.
		/// </summary>
		protected virtual void OnAuthSweep() { }

		/// <summary>
		/// Invoked when a handshake cannot be admitted because the pending authentication
		/// cap (<see cref="MaxPendingAuthConnections"/>) has been reached.  Override to
		/// implement a login queue — return <c>true</c> if the connection was queued and
		/// should NOT be disconnected; return <c>false</c> (default) to drop the handshake.
		/// </summary>
		/// <param name="conn">The connection that is being deferred.</param>
		/// <returns><c>true</c> if the connection was queued; <c>false</c> to reject.</returns>
		protected virtual bool OnHandshakeDeferred(TConnection conn) => false;

		/// <summary>
		/// Resets the global handshake counter when the 1-second window expires.
		/// Wall-clock based so a single long tick cannot silently extend the window.
		/// </summary>
		private void ResetGlobalHandshakeWindowIfExpired()
		{
			lock (handshakeCountGate)
			{
				DateTime utcNow = DateTime.UtcNow;
				if (utcNow >= nextGlobalHandshakeResetUtc)
				{
					nextGlobalHandshakeResetUtc = utcNow.AddSeconds(1);
					globalHandshakeCount = 0;
				}
			}
		}

		/// <summary>
		/// Atomically increments the global handshake count if the rate limit has not been
		/// reached this window.  Resets the window lazily when it has expired.
		/// Returns <c>true</c> if the handshake may proceed; <c>false</c> if the global cap
		/// has been exhausted and the handshake must be rejected.
		/// </summary>
		private bool TryIncrementGlobalHandshakeCount()
		{
			lock (handshakeCountGate)
			{
				DateTime now = DateTime.UtcNow;
				if (now >= nextGlobalHandshakeResetUtc)
				{
					nextGlobalHandshakeResetUtc = now.AddSeconds(1);
					globalHandshakeCount = 0;
				}
				if (globalHandshakeCount >= MaxGlobalHandshakesPerSecond)
					return false;
				globalHandshakeCount++;
				return true;
			}
		}

		/// <summary>
		/// Decrements the global handshake count on a failure path so a rejected handshake
		/// does not consume a rate-limit slot.  Safe to call even if the count is already
		/// zero (floor at 0).
		/// </summary>
		private void DecrementGlobalHandshakeCount()
		{
			lock (handshakeCountGate)
			{
				if (globalHandshakeCount > 0)
					globalHandshakeCount--;
			}
		}

		/// <summary>
		/// Disconnects and purges connections that exceeded the authentication TTL window
		/// without completing authentication. Scans are bounded by <see cref="AuthSweepMaxScan"/>
		/// and <see cref="AuthSweepMaxRemovals"/> to keep call-site cost predictable.
		/// </summary>
		private void SweepStaleAuthentication()
		{
			DateTime now = DateTime.UtcNow;

			// Phase 1: under ttlGate, capture the set of stale client IDs and their
			// associated connections.  The snapshot, staleness check, and removal all
			// happen under a single lock hold — no TOCTOU window between observing a
			// stale timestamp and removing the entry (fixes HIGH-1).
			List<TConnection> staleConns;

			lock (ttlGate)
			{
				if (authStartTimeByClientId.Count == 0)
				{
					OnAuthSweep();
					return;
				}

				var staleIds = new List<int>(Math.Min(AuthSweepMaxRemovals, authStartTimeByClientId.Count));
				staleConns = new List<TConnection>(staleIds.Capacity);

				foreach (var kvp in authStartTimeByClientId)
				{
					if (staleIds.Count >= AuthSweepMaxRemovals)
						break;

					if ((now - kvp.Value).TotalSeconds < AuthStaleTtlSeconds)
						continue;

					staleIds.Add(kvp.Key);
				}

				foreach (int clientId in staleIds)
				{
					authStartTimeByClientId.Remove(clientId);
					authOriginalStartByClientId.Remove(clientId);

					if (authConnectionByClientId.TryGetValue(clientId, out TConnection conn))
					{
						authConnectionByClientId.Remove(clientId);
						staleConns.Add(conn);
					}
				}
			}

			// Phase 2: disconnect and purge OUTSIDE the lock so that
			// TrackAuthStart / RefreshAuthTtl are never blocked by disconnect I/O.
			foreach (TConnection conn in staleConns)
			{
				if (IsConnectionAuthenticated(conn))
				{
					_ = Log.Warning(LogPrefix, $"SweepStaleAuthentication: conn is authenticated (likely recycled) — skipping purge.");
					continue;
				}

				OnPurgeConnectionState(conn);
				AccountManager.RemoveConnectionAccount(conn);
				DisconnectConnection(conn, graceful: false);
			}

			OnAuthSweep();
		}

		/// <summary>
		/// Removes expired per-IP handshake rate-limit windows to prevent unbounded dictionary growth.
		/// </summary>
		private void SweepExpiredHandshakeRateLimits()
		{
			if (handshakeIpWindows.Count == 0) return;

			DateTime now = DateTime.UtcNow;
			TimeSpan window = TimeSpan.FromSeconds(HandshakeIpWindowSeconds);
			int scanned = 0;
			int removed = 0;

			foreach (var kvp in handshakeIpWindows)
			{
				if (scanned >= HandshakeRateLimitSweepMaxScan || removed >= HandshakeRateLimitSweepMaxRemovals)
					break;

				scanned++;

				if (now >= kvp.Value.WindowStartUtc + window)
				{
					handshakeIpWindows.TryRemove(kvp.Key, out _);
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
		public void OnHandshakeReceived(TConnection conn, byte[] publicKey, byte[] cookie, string connectionToken, ushort minVersion, ushort maxVersion, string gameVersion = "")
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

			// Reject the entire RFC 7748 §6.1 small-order point
			// blacklist (was previously only the all-zero point). Any of these
			// always yield an all-zero shared secret regardless of the server's
			// private key, breaking ECDH forward secrecy entirely.
			if (!CryptoHelper.IsValidX25519PublicKey(publicKey))
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
				catch (CryptographicException)
				{
					DisconnectConnection(conn, graceful: true);
					return;
				}

				// ── Game version validation ────────────────────────────────
				// Reject clients whose game version does not match the server.
				// Skipped when ExpectedGameVersion is empty (development safety).
				if (!string.IsNullOrEmpty(ExpectedGameVersion))
				{
					if (string.IsNullOrEmpty(gameVersion) || gameVersion != ExpectedGameVersion)
					{
						_ = Log.Warning(LogPrefix, string.Format("Game version mismatch: client=\"{0}\", server=\"{1}\"", gameVersion, ExpectedGameVersion));
						BroadcastAuthResult(conn, ClientAuthenticationResult.VersionMismatch, reliable: true);
						// Defer disconnect to the main thread so the reliable broadcast is
						// sent before the connection is torn down. The transport's reliable
						// channel guarantees delivery ordering, so no blocking sleep is needed.
						EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: true));
						return;
					}
				}

				string challengeIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
				// Bind the connection identity into the cookie so a
				// captured cookie cannot be replayed by another connection from the
				// same IP (e.g. shared NAT / proxy).
				byte[] challengeCookie = HandshakeService.ComputeHandshakeCookie(challengeIp, publicKey, HandshakeService.GetTimeBucket(), hmacKeySnapshot, GetConnectionClientId(conn));
				BroadcastCookieChallenge(conn, challengeCookie);
				return;
			}

			// ── Phase 2: Cookie verification ──────────────────────────────
			string remoteIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
			if (!HandshakeService.VerifyHandshakeCookieWithRollover(cookie, remoteIp, publicKey, hmacKeySnapshot, GetConnectionClientId(conn)))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// ── Per-IP rate limit (burst window) ────────────────────────
			// Fail closed: if we cannot resolve a usable rate-limit key (no remote IP, parse
			// failure, etc.), drop the connection rather than allowing it to bypass the
			// per-IP throttle. Otherwise an attacker that strips remote-IP info from their
			// transport could flood handshakes without ever hitting the rate limiter.
			string rateLimitKey = ResolveRateLimitKey(conn);
			if (string.IsNullOrEmpty(rateLimitKey))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}
			DateTime nowUtc = DateTime.UtcNow;
			TimeSpan window = TimeSpan.FromSeconds(HandshakeIpWindowSeconds);

			// Atomic per-IP check-and-set via AddOrUpdate over a burst window.
			// The previous single-deadline debounce rejected every completion within
			// HandshakeIpDebounceSeconds of another from the same IP — including the
			// cookie echo of a client whose Phase-1 challenge was issued inside the
			// window (every sub-10 ms / loopback client) and a second player behind the
			// same NAT logging in alongside the first. The windowed counter accepts a
			// burst of HandshakeIpBurstLimit completions per IP per window and only
			// throttles the sustained flood the limiter exists to stop. Rejected
			// attempts never touch the window (no sliding extension).
			//
			// The delegates capture 'rateLimited' to distinguish whether AddOrUpdate
			// honoured the check (rate-limited) or actually recorded the attempt.
			// Under extreme contention the add factory's side effect may survive into
			// a retry, but the consequence is a single extra handshake — not a
			// systematic bypass — and is negligible compared to the original TOCTOU race.
			bool rateLimited = true;
			handshakeIpWindows.AddOrUpdate(
				rateLimitKey,
				_ =>
				{
					rateLimited = false;
					return new HandshakeIpWindow { Count = 1, WindowStartUtc = nowUtc };
				},
				(_, existing) =>
				{
					if (nowUtc >= existing.WindowStartUtc + window)
					{
						// Window expired — start a fresh one.
						rateLimited = false;
						return new HandshakeIpWindow { Count = 1, WindowStartUtc = nowUtc };
					}
					if (existing.Count < HandshakeIpBurstLimit)
					{
						rateLimited = false;
						return new HandshakeIpWindow { Count = existing.Count + 1, WindowStartUtc = existing.WindowStartUtc };
					}
					// Burst exhausted — reject without touching the window.
					return existing;
				});
			if (rateLimited)
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// ── Global rate limit ─────────────────────────────────────────
			if (!TryIncrementGlobalHandshakeCount())
				return;

			// Begin TTL tracking after all rate-limit gates have passed.
			if (!TrackAuthStart(conn))
			{
				// Give the hosting environment a chance to defer (queue) the handshake
				// rather than dropping it outright.  LoginQueueSystem overrides this.
				if (!OnHandshakeDeferred(conn))
				{
					DateTime capNow = DateTime.UtcNow;
					if (capNow >= nextPendingAuthCapWarningUtc)
					{
						nextPendingAuthCapWarningUtc = capNow.AddSeconds(5);
						_ = Log.Warning(LogPrefix, $"Pending auth cap ({MaxPendingAuthConnections}) reached — handshake(s) dropped.");
					}
				}
				DecrementGlobalHandshakeCount();
				return;
			}

			// ── X25519 ECDH key agreement ─────────────────────────────────
			var kaResult = HandshakeService.ServerPerformKeyAgreement(publicKey, minVersion, maxVersion);
			if (!kaResult.Success)
			{
				DecrementGlobalHandshakeCount();
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (!AccountManager.TryAddConnectionEncryptionData(conn, publicKey))
			{
				// Do NOT call ClearTransientAuthState here — a concurrent handshake
				// packet may have succeeded at TryAddConnectionEncryptionData and
				// relies on the TTL tracking that TrackAuthStart established.
				// Orphaned TTL entries created by the losing packet are naturally
				// swept after AuthStaleTtlSeconds (15 s) by SweepStaleAuthentication.
				DecrementGlobalHandshakeCount();
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
			int clientId = GetConnectionClientId(conn);
			// FishNet assigns ClientId 0 to the first remote connection; -1 is unset.
			// Reject only genuinely invalid IDs (negative), not ClientId 0.
			if (clientId < 0)
			{
				_ = Log.Warning(LogPrefix, $"TrackAuthStart: refusing to track invalid clientId {clientId}.");
				return false;
			}
			lock (ttlGate)
			{
				if (authStartTimeByClientId.Count >= MaxPendingAuthConnections)
					return false;
				DateTime now = DateTime.UtcNow;
				authStartTimeByClientId[clientId] = now;
				authOriginalStartByClientId[clientId] = now;
				authConnectionByClientId[clientId] = conn;
				return true;
			}
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
			if (clientId < 0) return;
			lock (ttlGate)
			{
				if (authOriginalStartByClientId.TryGetValue(clientId, out DateTime originalStart))
				{
					if ((DateTime.UtcNow - originalStart).TotalSeconds >= AuthHardDeadlineSeconds)
						return;
				}
				authStartTimeByClientId[clientId] = DateTime.UtcNow;
			}
		}

		/// <summary>
		/// Clears transient per-connection authenticator TTL tracking state.
		/// </summary>
		/// <param name="clientId">Connection client ID.</param>
		protected void ClearTransientAuthState(int clientId)
		{
			lock (ttlGate)
			{
				authStartTimeByClientId.Remove(clientId);
				authOriginalStartByClientId.Remove(clientId);
				authConnectionByClientId.Remove(clientId);
			}
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

		/// <summary>
		/// Enqueues an action to be executed on the main/UI thread.
		/// Implementations using Unity must marshal all network API calls (Broadcast, Disconnect) via this method.
		/// Non-Unity implementations may execute immediately or use their own dispatcher.
		/// </summary>
		/// <param name="conn">The connection context (for lifetime checking).</param>
		/// <param name="action">The action to enqueue.</param>
		protected abstract void EnqueueMainThread(TConnection conn, Action action);

		/// <summary>
		/// Broadcasts an authentication result to a single connection.
		/// </summary>
		/// <param name="conn">Target connection.</param>
		/// <param name="result">Auth result code.</param>
		/// <param name="reliable">True for reliable delivery, false for unreliable.</param>
		protected abstract void BroadcastAuthResult(TConnection conn, ClientAuthenticationResult result, bool reliable);

		#endregion
	}
}