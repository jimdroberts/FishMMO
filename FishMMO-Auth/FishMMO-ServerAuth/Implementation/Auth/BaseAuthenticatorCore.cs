using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using FishMMO.Auth.Core;
using FishMMO.Auth.Core.Collections;
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
		/// <summary>
		/// Seconds an authenticating connection may go without progress before it is purged. See
		/// <see cref="PendingAuthRules.ProgressTtlSeconds"/>.
		/// </summary>
		/// <remarks>
		/// Machine work only. A connection waiting on the player's two-factor code is bounded by
		/// <see cref="TwoFactorWindowSeconds"/> instead.
		/// </remarks>
		protected const float AuthStaleTtlSeconds = (float)PendingAuthRules.ProgressTtlSeconds;

		/// <summary>
		/// Seconds into one authenticating phase after which <see cref="RefreshAuthTtl"/> no longer
		/// extends it, preventing unbounded TTL extension. See
		/// <see cref="PendingAuthRules.AuthenticatingCapSeconds"/>.
		/// </summary>
		protected const float AuthHardDeadlineSeconds = (float)PendingAuthRules.AuthenticatingCapSeconds;

		/// <summary>Default for <see cref="TwoFactorWindowSeconds"/>; see <see cref="PendingAuthRules.DefaultTwoFactorWindowSeconds"/>.</summary>
		public const float DefaultTwoFactorWindowSeconds = (float)PendingAuthRules.DefaultTwoFactorWindowSeconds;

		/// <summary>
		/// Seconds a player has to answer one two-factor prompt before the connection is dropped.
		/// </summary>
		/// <remarks>
		/// Every prompt gets the whole window: the first, and each re-prompt after a counted wrong
		/// code. Set by the host from configuration; values are brought into
		/// [<see cref="PendingAuthRules.MinTwoFactorWindowSeconds"/>, <see cref="PendingAuthRules.MaxTwoFactorWindowSeconds"/>]
		/// (see <see cref="PendingAuthRules.ClampTwoFactorWindow"/>). A change applies to prompts
		/// already on screen too.
		/// </remarks>
		public float TwoFactorWindowSeconds
		{
			get => (float)pendingAuth.TwoFactorWindowSeconds;
			set => pendingAuth.TwoFactorWindowSeconds = PendingAuthRules.ClampTwoFactorWindow(value);
		}

		/// <summary>Default for <see cref="MaxPendingAuthConnections"/>.</summary>
		public const int DefaultMaxPendingAuthConnections = 10000;

		/// <summary>
		/// Maximum number of connections allowed to be mid-authentication at once. A handshake
		/// that would exceed it is offered to <see cref="OnHandshakeDeferred"/> (the login queue)
		/// and dropped when that declines.
		/// </summary>
		/// <remarks>
		/// Only connections still authenticating count; a player at the two-factor prompt does not,
		/// and the whole pending set, prompts included, is instead held to
		/// <see cref="PendingAuthRules.PendingCeilingMultiplier"/> times this (see
		/// <see cref="PendingAuthRules.AdmitsNewPending"/>). Tracking ends when a connection
		/// authenticates (<see cref="EndAuthTracking"/>), is purged, or runs out of time. Set by the
		/// host from configuration before connections are accepted; values below 1 are raised to 1.
		/// </remarks>
		public int MaxPendingAuthConnections
		{
			get => Volatile.Read(ref maxPendingAuthConnections);
			set => Volatile.Write(ref maxPendingAuthConnections, Math.Max(1, value));
		}

		private int maxPendingAuthConnections = DefaultMaxPendingAuthConnections;

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

		/// <summary>
		/// Maximum stale auth entries purged per sweep. The sweep runs every tick and only ever
		/// looks at the oldest entries, so this bounds one tick's disconnect work, not coverage:
		/// whatever is left is at the head for the next tick.
		/// </summary>
		protected const int AuthSweepMaxRemovals = 64;

		/// <summary>Maximum closed windows removed per handshake rate-limit sweep.</summary>
		protected const int HandshakeRateLimitSweepMaxRemovals = 2048;

		/// <summary>
		/// Synchronization gate for <see cref="globalHandshakeCount"/> and
		/// <see cref="nextGlobalHandshakeResetSeconds"/>. All read/write access to
		/// these two fields must acquire this lock first.
		/// </summary>
		private readonly object handshakeCountGate = new object();

		/// <summary>
		/// Connections that have completed their handshake and not yet authenticated, each either
		/// authenticating or awaiting its second factor.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Its lock is a leaf: it is never taken while holding <see cref="handshakeCountGate"/> or
		/// <c>AccountManager.SyncRoot</c>, and it calls nothing outside itself, so the disconnect
		/// and cleanup the sweep leads to happen after it is released. It used to be a dictionary
		/// and one list ordered by last refresh under a lock here, which held every pending
		/// connection to the same fifteen-second TTL — including a player reading a code off their
		/// phone. See <see cref="PendingAuthTracker{TConnection}"/>.
		/// </para>
		/// <para>
		/// Times are <see cref="MonotonicClock"/> seconds. The TTL and the windows are local
		/// durations; on the wall clock, a step backwards stopped every pending entry from going
		/// stale until the clock caught up, and the pending cap filled with connections nobody
		/// would ever purge.
		/// </para>
		/// </remarks>
		private readonly PendingAuthTracker<TConnection> pendingAuth = new PendingAuthTracker<TConnection>(
			PendingAuthRules.ProgressTtlSeconds,
			PendingAuthRules.AuthenticatingCapSeconds,
			PendingAuthRules.DefaultTwoFactorWindowSeconds);

		/// <summary>
		/// Connections the stale sweep has taken out of tracking and has still to purge, each with
		/// the phase it ran out of time in. Filled under the tracker's lock, drained outside it. Only
		/// <see cref="Tick"/> uses it.
		/// </summary>
		private readonly List<(TConnection Connection, PendingAuthPhase Phase)> staleConnectionBuffer =
			new List<(TConnection Connection, PendingAuthPhase Phase)>();

		/// <summary>
		/// Per-IP Phase-2 handshake limiter with a burst allowance. Tracks completed
		/// X25519 handshakes per remote IP inside a fixed measurement window instead of
		/// a single debounce deadline, so a burst of legitimate near-simultaneous
		/// handshakes from one IP (players behind one NAT, a fast re-login on a
		/// sub-10 ms link) is accepted while a sustained flood is still throttled.
		/// See <see cref="HandshakeIpWindowSeconds"/> and <see cref="HandshakeIpBurstLimit"/>.
		/// </summary>
		/// <remarks>
		/// A window runs from its first completion for <see cref="HandshakeIpWindowSeconds"/> and
		/// does not move on acceptance, so a boundary straddle can admit up to
		/// 2 × <see cref="HandshakeIpBurstLimit"/> completions in ~2.01 s (the last of one window
		/// plus the first of the next). That doubling is bounded upstream: every completion still
		/// requires a fresh one-time connection token, a rate-limited Phase-1 handshake, and a
		/// valid cookie. A rejected attempt never touches the window (no extension), mirroring
		/// the anti-hammer property of the previous single-deadline debounce.
		/// </remarks>
		private readonly FixedWindowCounter<string> handshakeIpWindows =
			new FixedWindowCounter<string>(TimeSpan.FromSeconds(HandshakeIpWindowSeconds), StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// HMAC-SHA256 key for stateless handshake cookies.
		/// Generated when workers start; zeroed on shutdown.
		/// Volatile for cross-thread visibility between main thread and network thread.
		/// </summary>
		private volatile byte[]? cookieHmacKey;

		/// <summary>Rolling count of completed X25519 handshakes in the current 1-second window.
		/// Guarded by <see cref="handshakeCountGate"/>.</summary>
		private int globalHandshakeCount;

		/// <summary>Monotonic instant (seconds) when the current global handshake window expires and the counter resets.
		/// Guarded by <see cref="handshakeCountGate"/>.</summary>
		private double nextGlobalHandshakeResetSeconds = MonotonicClock.NowSeconds + 1.0;

		/// <summary>Rate limiter (monotonic seconds) for the pending auth cap warning log.</summary>
		private double nextPendingAuthCapWarningSeconds;

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
		/// The core's clock: seconds on this process's monotonic clock. Every duration the core
		/// times — the pending-authentication limits, the two-factor window, the rate limits and
		/// debounces — reads it here.
		/// </summary>
		/// <remarks>
		/// Only differences between readings mean anything. Virtual so a test can move time forward
		/// past a two-factor window or a debounce without waiting for it; nothing else overrides it,
		/// and an override must never run backwards.
		/// </remarks>
		protected virtual double NowSeconds => MonotonicClock.NowSeconds;

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
				nextGlobalHandshakeResetSeconds = NowSeconds + 1.0;
			}
			pendingAuth.Clear();
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
		/// Clock based rather than tick based so a single long tick cannot silently extend the
		/// window; monotonic so a wall-clock step cannot either.
		/// </summary>
		private void ResetGlobalHandshakeWindowIfExpired()
		{
			lock (handshakeCountGate)
			{
				double now = NowSeconds;
				if (now >= nextGlobalHandshakeResetSeconds)
				{
					nextGlobalHandshakeResetSeconds = now + 1.0;
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
				double now = NowSeconds;
				if (now >= nextGlobalHandshakeResetSeconds)
				{
					nextGlobalHandshakeResetSeconds = now + 1.0;
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
		/// Disconnects and purges pending connections that ran out of time — an authenticating one
		/// that stopped making progress, or a two-factor prompt left unanswered for its window —
		/// oldest first.
		/// </summary>
		/// <remarks>
		/// Reads only the head of each phase's list and stops at the first entry still in time, so
		/// a tick with nothing overdue costs one lock and two comparisons whatever the number of
		/// pending connections. At most <see cref="AuthSweepMaxRemovals"/> are purged per tick; any
		/// left over are still at the heads on the next one.
		/// </remarks>
		private void SweepStaleAuthentication()
		{
			double now = NowSeconds;
			List<(TConnection Connection, PendingAuthPhase Phase)> stale = staleConnectionBuffer;
			stale.Clear();

			// Phase 1: observe that an entry is overdue and remove it in one lock hold — no TOCTOU
			// window between the two (fixes HIGH-1).
			pendingAuth.SweepOverdue(now, AuthSweepMaxRemovals, stale);

			// Phase 2: answer, disconnect and purge OUTSIDE the lock so that
			// TrackAuthStart / RefreshAuthTtl are never blocked by disconnect I/O.
			int twoFactorExpired = 0;
			for (int i = 0; i < stale.Count; i++)
			{
				(TConnection conn, PendingAuthPhase phase) = stale[i];

				/* Tracking ends when a connection authenticates (EndAuthTracking), so an
				 * authenticated connection here is one whose client ID was recycled onto a new
				 * connection that authenticated, or a host that never reported the success. Either
				 * way it must not be disconnected. */
				if (IsConnectionAuthenticated(conn))
				{
					_ = Log.Warning(LogPrefix, $"SweepStaleAuthentication: conn is authenticated (likely recycled) — skipping purge.");
					continue;
				}

				/* A stalled exchange is dropped without a word, like every other stall. A two-factor
				 * prompt is different: a person was asked for a code, and the connection used to
				 * close under them with nothing said, so the client could only guess whether the
				 * time had run out, the attempts had, or the network had failed. The prompt's
				 * window runs out only while no code from it is being checked (a code arriving moves
				 * the connection back to Authenticating), so this is unambiguously the window.
				 * Reliable, and ahead of the disconnect, which below is the kind that sends what is
				 * already queued first. The answer carries nothing encrypted, so it does not depend
				 * on the state purged after it. */
				if (phase == PendingAuthPhase.AwaitingTwoFactor)
				{
					twoFactorExpired++;
					BroadcastAuthResult(conn, ClientAuthenticationResult.TwoFactorExpired, reliable: true);
				}

				OnPurgeConnectionState(conn);
				AccountManager.RemoveConnectionAccount(conn);
				DisconnectConnection(conn, graceful: false);
			}
			stale.Clear();

			if (twoFactorExpired > 0)
			{
				_ = Log.Debug(LogPrefix, $"{twoFactorExpired} two-factor prompt(s) went unanswered for {TwoFactorWindowSeconds:0}s; told TwoFactorExpired and disconnecting.");
			}

			OnAuthSweep();
		}

		/// <summary>
		/// Removes closed per-IP handshake rate-limit windows to prevent unbounded growth.
		/// </summary>
		/// <remarks>
		/// Head-first over windows in the order they opened, so it always reaches the oldest and
		/// costs nothing when none has closed. It used to read the dictionary's Count every tick
		/// (which takes every one of a ConcurrentDictionary's locks) and then scan from the
		/// dictionary's head, which never reached entries behind a run of live ones.
		/// </remarks>
		private void SweepExpiredHandshakeRateLimits()
		{
			handshakeIpWindows.SweepExpired(NowSeconds, HandshakeRateLimitSweepMaxRemovals);
		}

		#endregion

		#region Handshake

		/// <summary>
		/// Processes an incoming client handshake. Must be called from the transport layer
		/// (e.g., on receipt of a <c>ClientHandshakeBroadcast</c>).
		/// Implements a two-phase stateless cookie challenge followed by X25519 ECDH key agreement.
		/// Runs with no blocking I/O — safe to call on a network-receive thread.
		/// </summary>
		/// <remarks>
		/// The return value is the hand-off between the host's handshake timeout and this core. A
		/// host bounds a new connection until its handshake completes; once this returns
		/// <c>true</c> the connection is tracked here as pending authentication and the limits in
		/// <see cref="PendingAuthRules"/> bound it, so the host must stop timing the handshake.
		/// Timing it on until authentication made the handshake timeout a limit on the whole
		/// sign-in, two-factor prompt included.
		/// </remarks>
		/// <param name="conn">The network connection.</param>
		/// <param name="publicKey">Client's X25519 ephemeral public key (32 bytes). Must not be null.</param>
		/// <param name="cookie">Cookie echoed from a prior challenge, or null on first attempt.</param>
		/// <param name="minVersion">Minimum protocol version supported by the client.</param>
		/// <param name="maxVersion">Maximum protocol version supported by the client.</param>
		/// <returns>
		/// <c>true</c> when this call completed the key agreement and the connection is now tracked
		/// as pending authentication; <c>false</c> for a cookie challenge, a refusal, a deferral to
		/// the login queue, or a duplicate.
		/// </returns>
		public bool OnHandshakeReceived(TConnection conn, byte[] publicKey, byte[] cookie, string connectionToken, ushort minVersion, ushort maxVersion, string gameVersion = "")
		{
			if (IsConnectionAuthenticated(conn) ||
				publicKey == null ||
				publicKey.Length != CryptoHelper.X25519PublicKeyLength)
			{
				DisconnectConnection(conn, graceful: true);
				return false;
			}

			if (AccountManager.GetConnectionEncryptionData(conn, out _))
			{
				return false;
			}

			if (AccountManager.IsAuthInProgress(conn))
			{
				return false;
			}

			byte[]? hmacKeySnapshot = cookieHmacKey;
			if (hmacKeySnapshot == null)
			{
				DisconnectConnection(conn, graceful: true);
				return false;
			}

			// Reject the entire RFC 7748 §6.1 small-order point
			// blacklist (was previously only the all-zero point). Any of these
			// always yield an all-zero shared secret regardless of the server's
			// private key, breaking ECDH forward secrecy entirely.
			if (!CryptoHelper.IsValidX25519PublicKey(publicKey))
			{
				DisconnectConnection(conn, graceful: true);
				return false;
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
					return false;
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
						return false;
					}
				}

				string challengeIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
				// Bind the connection identity into the cookie so a
				// captured cookie cannot be replayed by another connection from the
				// same IP (e.g. shared NAT / proxy).
				byte[] challengeCookie = HandshakeService.ComputeHandshakeCookie(challengeIp, publicKey, HandshakeService.GetTimeBucket(), hmacKeySnapshot, GetConnectionClientId(conn));
				BroadcastCookieChallenge(conn, challengeCookie);
				return false;
			}

			// ── Phase 2: Cookie verification ──────────────────────────────
			string remoteIp = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
			if (!HandshakeService.VerifyHandshakeCookieWithRollover(cookie, remoteIp, publicKey, hmacKeySnapshot, GetConnectionClientId(conn)))
			{
				DisconnectConnection(conn, graceful: true);
				return false;
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
				return false;
			}
			// Atomic per-IP check-and-count over a burst window.
			// The previous single-deadline debounce rejected every completion within
			// HandshakeIpDebounceSeconds of another from the same IP — including the
			// cookie echo of a client whose Phase-1 challenge was issued inside the
			// window (every sub-10 ms / loopback client) and a second player behind the
			// same NAT logging in alongside the first. The windowed counter accepts a
			// burst of HandshakeIpBurstLimit completions per IP per window and only
			// throttles the sustained flood the limiter exists to stop. Rejected
			// attempts never touch the window (no sliding extension). The check and the
			// count happen under one lock, so no race can admit an extra completion.
			if (!handshakeIpWindows.TryIncrement(rateLimitKey, NowSeconds, HandshakeIpBurstLimit))
			{
				DisconnectConnection(conn, graceful: true);
				return false;
			}

			// ── Global rate limit ─────────────────────────────────────────
			if (!TryIncrementGlobalHandshakeCount())
			{
				return false;
			}

			// Begin TTL tracking after all rate-limit gates have passed.
			if (!TrackAuthStart(conn))
			{
				// Give the hosting environment a chance to defer (queue) the handshake
				// rather than dropping it outright.  LoginQueueSystem overrides this.
				bool deferred = OnHandshakeDeferred(conn);
				if (!deferred)
				{
					double capNow = NowSeconds;
					if (capNow >= nextPendingAuthCapWarningSeconds)
					{
						nextPendingAuthCapWarningSeconds = capNow + 5.0;
						_ = Log.Warning(LogPrefix, $"Pending auth cap ({MaxPendingAuthConnections}) reached — handshake(s) dropped.");
					}
				}
				DecrementGlobalHandshakeCount();
				return false;
			}

			// ── X25519 ECDH key agreement ─────────────────────────────────
			var kaResult = HandshakeService.ServerPerformKeyAgreement(publicKey, minVersion, maxVersion);
			if (!kaResult.Success)
			{
				DecrementGlobalHandshakeCount();
				DisconnectConnection(conn, graceful: true);
				return false;
			}

			if (!AccountManager.TryAddConnectionEncryptionData(conn, publicKey))
			{
				// Do NOT call ClearTransientAuthState here — a concurrent handshake
				// packet may have succeeded at TryAddConnectionEncryptionData and
				// relies on the TTL tracking that TrackAuthStart established.
				// Orphaned TTL entries created by the losing packet are naturally
				// swept after AuthStaleTtlSeconds (15 s) by SweepStaleAuthentication.
				DecrementGlobalHandshakeCount();
				return false;
			}

			if (AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				encryptionData.AgreedVersion = kaResult.AgreedVersion;
				encryptionData.PromoteToDirectional(kaResult.SessionKeys);
				BroadcastServerHandshake(conn, kaResult.ServerPublicKey, kaResult.AgreedVersion);
				return true;
			}

			_ = Log.Warning(LogPrefix, "Failed to retrieve encryption data after handshake registration.");
			DisconnectConnection(conn, graceful: true);
			return false;
		}

		#endregion

		#region Auth Tracking

		/// <summary>
		/// Starts pending-authentication tracking for a connection whose handshake is completing, in
		/// <see cref="PendingAuthPhase.Authenticating"/>. A client ID already tracked is restarted.
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
			return pendingAuth.TryStart(clientId, conn, NowSeconds, MaxPendingAuthConnections);
		}

		/// <summary>
		/// Records progress on an authenticating connection, restarting its
		/// <see cref="AuthStaleTtlSeconds"/> TTL. Call from async workers at meaningful progress
		/// points to prevent premature sweeping.
		/// </summary>
		/// <remarks>
		/// Refused once the current authenticating phase has run for
		/// <see cref="AuthHardDeadlineSeconds"/>, and ignored while the connection is awaiting its
		/// second factor: that phase is bounded by <see cref="TwoFactorWindowSeconds"/> alone, and a
		/// verification of an earlier code finishing late must not take it off its window. Refreshes
		/// only a connection that is still tracked; it used to write the timestamp unconditionally,
		/// so a refresh landing after tracking had ended re-created an entry with no connection
		/// behind it.
		/// </remarks>
		/// <param name="conn">Connection whose TTL to refresh.</param>
		protected void RefreshAuthTtl(TConnection conn)
		{
			if (conn == null) return;
			int clientId = GetConnectionClientId(conn);
			if (clientId < 0) return;
			pendingAuth.ReportProgress(clientId, NowSeconds);
		}

		/// <summary>
		/// Puts a connection on the two-factor prompt: from now it has
		/// <see cref="TwoFactorWindowSeconds"/> to send a code, and nothing but a code starts
		/// the clock again.
		/// </summary>
		/// <remarks>
		/// Call immediately before sending the prompt — <c>TwoFactorRequired</c>, or a re-prompt
		/// after a counted attempt — so every prompt the player sees carries a full window. Does
		/// nothing for a connection no longer tracked; it is already being dropped.
		/// </remarks>
		/// <param name="conn">The connection being prompted.</param>
		protected void BeginAwaitingTwoFactor(TConnection conn)
		{
			if (conn == null) return;
			int clientId = GetConnectionClientId(conn);
			if (clientId < 0) return;
			pendingAuth.BeginAwaitingTwoFactor(clientId, conn, NowSeconds);
		}

		/// <summary>
		/// Returns a connection to <see cref="PendingAuthPhase.Authenticating"/> with a fresh
		/// phase: the player's code has arrived and the servers are checking it, which is machine
		/// work again and bounded like it.
		/// </summary>
		/// <param name="conn">The connection whose code is being checked.</param>
		protected void ResumeAuthentication(TConnection conn)
		{
			if (conn == null) return;
			int clientId = GetConnectionClientId(conn);
			if (clientId < 0) return;
			pendingAuth.ResumeAuthenticating(clientId, conn, NowSeconds);
		}

		/// <summary>
		/// Clears transient per-connection authenticator TTL tracking state.
		/// </summary>
		/// <param name="clientId">Connection client ID.</param>
		protected void ClearTransientAuthState(int clientId)
		{
			pendingAuth.Remove(clientId);
		}

		/// <summary>
		/// Ends pending-authentication tracking for a connection that has just authenticated.
		/// </summary>
		/// <remarks>
		/// Called by the host when it applies a successful authentication result. Until this
		/// existed nothing ended tracking on success: every authenticated connection went on
		/// holding one of the <see cref="MaxPendingAuthConnections"/> slots until its TTL ran
		/// out, and the stale sweep then found it authenticated and logged it as a recycled
		/// connection — a warning for every successful sign-in that stayed connected for longer
		/// than <see cref="AuthStaleTtlSeconds"/>.
		/// <para>
		/// Removes only the entry that belongs to <paramref name="conn"/>, so a late call for a
		/// connection whose client ID has since been recycled cannot end the new connection's
		/// tracking.
		/// </para>
		/// </remarks>
		/// <param name="conn">The connection that authenticated.</param>
		public void EndAuthTracking(TConnection conn)
		{
			if (conn == null) return;
			int clientId = GetConnectionClientId(conn);
			if (clientId < 0) return;
			pendingAuth.Remove(clientId, conn);
		}

		/// <summary>
		/// The phase a connection's pending authentication is in, when it has completed its
		/// handshake and not yet authenticated.
		/// </summary>
		/// <param name="conn">The connection.</param>
		/// <param name="phase">Its phase, when pending.</param>
		/// <returns><c>true</c> when <paramref name="conn"/> is pending here.</returns>
		public bool TryGetPendingAuthPhase(TConnection conn, out PendingAuthPhase phase)
		{
			phase = default;
			if (conn == null) return false;
			int clientId = GetConnectionClientId(conn);
			if (clientId < 0) return false;
			return pendingAuth.TryGetPhase(clientId, conn, out phase);
		}

		/// <summary>
		/// Whether <paramref name="conn"/> has completed its handshake and is still authenticating
		/// or awaiting its second factor — in the hands of this core's time limits.
		/// </summary>
		public bool IsAuthPending(TConnection conn) => TryGetPendingAuthPhase(conn, out _);

		/// <summary>Connections currently mid-authentication, awaiting a second factor included.</summary>
		public int PendingAuthCount => pendingAuth.Count;

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