using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Account;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Abstract base authenticator providing shared X25519 ECDH handshake, main-thread marshalling,
	/// stale-auth TTL sweeps, and connection lifecycle management common to all server
	/// authentication methods (SRP-6a and token-based).
	/// <para>Subclasses implement protocol-specific authentication logic by overriding
	/// <see cref="RegisterProtocolHandlers"/>, <see cref="InitializeWorkersCore"/>,
	/// and <see cref="ShutdownWorkersCore"/>.</para>
	/// </summary>
	/// <remarks>
	/// <para><b>Thread model:</b> The handshake handler runs on the network thread (zero blocking).
	/// Workers run on thread pool threads. All network operations (Broadcast, Disconnect,
	/// OnAuthenticationResult) are marshalled to the main Unity thread via a ConcurrentQueue.</para>
	/// <para><b>Stale-auth sweep:</b> Connections that do not complete authentication within
	/// <see cref="AuthStaleTtlSeconds"/> are unconditionally purged, preventing half-open
	/// connection accumulation.</para>
	/// <para><b>Wall-clock invariant:</b> TTL enforcement, hard deadlines, and cookie expiration
	/// use <see cref="DateTime.UtcNow"/>, which is sensitive to system clock adjustments.
	/// Hosts MUST run NTP (or equivalent) to keep the clock monotonically accurate.
	/// Large forward jumps may prematurely expire legitimate auth attempts; large backward
	/// jumps may extend attacker windows. This is an accepted deployment invariant.</para>
	/// <para><b>Fail-closed cookie rotation:</b> The HMAC key used for stateless handshake
	/// cookies is regenerated on every <see cref="InitializeWorkers"/> call and zeroed on
	/// <see cref="ShutdownWorkers"/>. Any in-flight phase-2 handshakes holding cookies
	/// signed with the previous key will fail verification and be disconnected. This is
	/// intentionally fail-closed — a brief disruption during authenticator restart is
	/// preferred over accepting stale cookies signed by a potentially compromised key.</para>
	/// <para><b>Error indistinguishability:</b> Most handshake and authentication failure
	/// paths disconnect without protocol-level error detail. This prevents oracle attacks
	/// at the cost of reduced client-side diagnostics. Server-side logs provide the detail
	/// needed for operational troubleshooting.</para>
	/// </remarks>
	public abstract class BaseServerAuthenticator : Authenticator, IServerAuthenticator
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per Update tick.
		/// This time-slices queue draining to avoid frame spikes. Increase on high-throughput
		/// servers with fast frames; decrease on servers sharing the main thread with simulation.
		/// </summary>
		[UnityEngine.SerializeField]
		private int maxMainThreadActionsPerUpdate = 100;

		/// <summary>
		/// Authentication TTL in seconds. Connections that do not complete auth within this window are purged.
		/// </summary>
		protected const float AuthStaleTtlSeconds = 15f;

		/// <summary>
		/// Hard deadline in seconds for any single authentication attempt.
		/// <see cref="RefreshAuthTtl"/> will not extend a connection's TTL beyond this
		/// absolute limit from its original start time, preventing unbounded TTL extension
		/// by adversaries who trigger repeated slow operations.
		/// <para>
		/// <b>Connection-slot exhaustion vector:</b> An attacker can hold up to
		/// <see cref="MaxPendingAuthConnections"/> slots for <c>AuthHardDeadlineSeconds</c>
		/// each by sending a valid cookie but never completing SRP. The product
		/// <c>MaxPendingAuthConnections × AuthHardDeadlineSeconds</c> bounds steady-state
		/// slot-seconds of exposure; operators should size <c>MaxPendingAuthConnections</c>
		/// accordingly and monitor the pending-auth-cap warning log for sustained saturation.
		/// </para>
		/// </summary>
		private const float AuthHardDeadlineSeconds = 60f;

		/// <summary>
		/// Sweep interval in seconds for stale authentication cleanup.
		/// </summary>
		private const float AuthSweepIntervalSeconds = 1f;

		/// <summary>
		/// Maximum stale auth entries scanned per sweep. Bounds main-thread cost
		/// when thousands of unauthenticated connections are queued.
		/// </summary>
		private const int AuthSweepMaxScan = 256;

		/// <summary>
		/// Maximum stale auth entries purged per sweep.
		/// </summary>
		private const int AuthSweepMaxRemovals = 64;

		/// <summary>
		/// Maximum number of concurrent pending authentication connections.
		/// Caps the size of <see cref="authStartTimeByClientId"/> to prevent memory exhaustion
		/// from half-open connection floods.
		/// </summary>
		/// <remarks>
		/// When this cap is reached, new handshakes are dropped without disconnect and a warning
		/// is logged. Operators should monitor for these warnings — sustained occurrences indicate
		/// either a connection flood or that the cap needs tuning for the deployment's expected
		/// concurrent authentication volume.
		/// </remarks>
		private const int MaxPendingAuthConnections = 10000;

		/// <summary>
		/// Minimum seconds between handshake attempts from the same remote IP.
		/// Mitigates X25519 CPU abuse from rapid handshake replay.
		/// </summary>
		/// <remarks>
		/// Keyed by normalised IP string. NAT gateways, IPv6 privacy extensions, and shared
		/// proxies may cause multiple distinct clients to share a single rate-limit identity,
		/// resulting in false-positive throttling under those network topologies. This is an
		/// accepted operational trade-off — the alternative (no per-IP throttle) permits
		/// trivial single-source CPU exhaustion.
		/// </remarks>
		private const float HandshakeIpDebounceSeconds = 0.25f;

		/// <summary>
		/// Sweep interval in seconds for expired handshake rate-limit entries.
		/// Tuned to keep pace with the global handshake cap (<see cref="MaxGlobalHandshakesPerSecond"/>).
		/// </summary>
		private const float HandshakeRateLimitSweepIntervalSeconds = 5f;

		/// <summary>
		/// Maximum entries scanned per handshake rate-limit sweep.
		/// Higher than <see cref="AuthSweepMaxScan"/> because the global handshake cap
		/// can generate up to <c>MaxGlobalHandshakesPerSecond × HandshakeRateLimitSweepIntervalSeconds</c>
		/// entries (2,500) between sweeps.
		/// </summary>
		private const int HandshakeRateLimitSweepMaxScan = 4096;

		/// <summary>
		/// Maximum entries removed per handshake rate-limit sweep.
		/// </summary>
		private const int HandshakeRateLimitSweepMaxRemovals = 2048;

		/// <summary>
		/// Time bucket width in seconds for stateless handshake cookies.
		/// Cookies are valid for the current bucket and the immediately preceding one,
		/// giving a maximum validity window of 2× this value.
		/// </summary>
		private const int CookieTimeBucketSeconds = 30;

		/// <summary>
		/// Maximum X25519 handshakes completed globally per second.
		/// Bounds total ECDH CPU regardless of IP diversity (botnet defence).
		/// The cookie challenge filters spoofed IPs before this counter is checked.
		/// </summary>
		/// <remarks>
		/// The counter reset and increment are not fully atomic across the window boundary,
		/// so up to ~2× this value may be admitted in a brief burst when the window rolls over.
		/// This is acceptable for a soft DoS defence — the cookie challenge is the primary gate.
		/// </remarks>
		private const int MaxGlobalHandshakesPerSecond = 500;

		/// <summary>
		/// Domain separator prepended to cookie HMAC input to prevent cross-purpose
		/// key reuse if the same HMAC key were accidentally shared with another subsystem.
		/// </summary>
		private static readonly byte[] CookieDomainSeparator = Encoding.ASCII.GetBytes("fishmmo-cookie-v1:");

		/// <summary>
		/// Cancellation token source for signalling graceful shutdown of all async workers.
		/// </summary>
		protected CancellationTokenSource workerCts;

		/// <summary>
		/// Thread-safe queue for marshalling network operations from async worker threads
		/// back to the main Unity thread. Workers enqueue Actions, Update() drains them.
		/// ConcurrentQueue avoids lock contention between network thread and worker threads.
		/// </summary>
		private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

		/// <summary>
		/// Tracks authentication start times for stale-auth TTL enforcement.
		/// Key: ClientId, Value: UTC start timestamp.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> authStartTimeByClientId = new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Tracks original authentication start times for hard deadline enforcement.
		/// Key: ClientId, Value: UTC start timestamp (never refreshed).
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> authOriginalStartByClientId = new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Reverse map from ClientId to active connection for stale-auth cleanup.
		/// </summary>
		private readonly ConcurrentDictionary<int, NetworkConnection> authConnectionByClientId = new ConcurrentDictionary<int, NetworkConnection>();

		/// <summary>
		/// Per-IP handshake rate limiter. Prevents X25519 CPU abuse from rapid handshake replay.
		/// </summary>
		/// <para><b>Bounding:</b> Under sustained attack the dictionary can accumulate up to
		/// <c>MaxGlobalHandshakesPerSecond × HandshakeRateLimitSweepIntervalSeconds</c> unique IP
		/// entries between sweeps. <see cref="SweepExpiredHandshakeRateLimits"/> evicts expired
		/// entries each tick, keeping steady-state size proportional to active unique IPs.</para>
		private readonly ConcurrentDictionary<string, DateTime> handshakeIpNextAllowedUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Countdown timer (seconds) until the next handshake rate-limit cleanup sweep.
		/// </summary>
		private float nextHandshakeRateLimitSweepSeconds = HandshakeRateLimitSweepIntervalSeconds;

		/// <summary>
		/// HMAC-SHA256 key for stateless handshake cookies.
		/// Generated when workers start; zeroed on shutdown.
		/// <para><b>Thread safety:</b> Marked volatile because the field is written on the
		/// main thread (InitializeWorkers/ShutdownWorkers) and read on the network thread
		/// (OnServerClientHandshakeReceived). Volatile ensures cross-thread visibility.</para>
		/// <para><b>Key rotation:</b> Zeroing this key on shutdown invalidates all outstanding
		/// cookies immediately. In-flight phase-2 handshakes that present cookies signed with
		/// the previous key will fail verification and be disconnected. Callers snapshot the
		/// reference before use to prevent a race with concurrent zeroing.</para>
		/// </summary>
		private volatile byte[] cookieHmacKey;

		/// <summary>
		/// Rolling count of completed X25519 handshakes in the current 1-second window.
		/// Accessed from the network thread via <see cref="Interlocked"/>.
		/// <para>
		/// <b>Transient negative values:</b> This counter can briefly read negative because
		/// the increment-then-reject pattern in <c>OnServerClientHandshakeReceived</c> is
		/// not wrapped in a single atomic CAS. Under benign concurrency the window is a few
		/// instructions wide and self-corrects when the counter is reset to zero at the start
		/// of each 1-second window via <see cref="Interlocked.Exchange"/>. The downstream
		/// comparison <c>count > MaxGlobalHandshakesPerSecond</c> is unaffected because a
		/// negative <c>int</c> is always <c>&lt; Max</c>.
		/// </para>
		/// </summary>
		private int globalHandshakeCount;

		/// <summary>
		/// UTC instant when the current global handshake window expires and the counter resets.
		/// Uses wall-clock time instead of frame-based <c>Time.deltaTime</c> so a single long
		/// frame cannot silently extend the window beyond 1 second.
		/// </summary>
		private DateTime nextGlobalHandshakeResetUtc = DateTime.UtcNow.AddSeconds(1);

		/// <summary>
		/// Rate limiter for the pending auth cap warning log.
		/// Prevents log flooding when the cap is sustained under attack.
		/// Network-thread only — no volatile or Interlocked needed.
		/// </summary>
		private DateTime nextPendingAuthCapWarningUtc;

		/// <summary>
		/// Countdown timer (seconds) until the next stale-authentication sweep.
		/// <para>
		/// <b>Time.deltaTime usage:</b> Unlike the global handshake rate-limit window (which
		/// uses wall-clock <c>DateTime.UtcNow</c>), this timer uses <c>Time.deltaTime</c>.
		/// A long frame will delay the sweep proportionally, which is acceptable because the
		/// sweep is a housekeeping operation — stale connections simply live slightly longer
		/// than <c>AuthStaleTtlSeconds</c>. The security-critical hard deadline is enforced
		/// by wall-clock comparison inside <c>SweepStaleAuthentication</c> itself.
		/// </para>
		/// </summary>
		private float nextAuthSweepSeconds = AuthSweepIntervalSeconds;

		/// <summary>
		/// The server instance providing access to AccountManager and other infrastructure.
		/// Setting this property initializes the bounded channels and starts async workers.
		/// </summary>
		public IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> Server { get; set; }

		/// <summary>
		/// Event triggered when server authentication completes for a client connection.
		/// Subscribe to this to handle post-authentication logic.
		/// </summary>
		public override event Action<NetworkConnection, bool> OnAuthenticationResult;

		/// <summary>
		/// Event triggered when client authentication completes.
		/// Used for custom post-authentication logic in server behaviours.
		/// </summary>
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		/// <summary>
		/// Display name used in log messages. Defaults to the concrete class name.
		/// </summary>
		protected virtual string LogPrefix => GetType().Name;

		#region Lifecycle

		/// <summary>
		/// Initializes the authenticator, registers the shared handshake broadcast handler
		/// and connection state handler, then delegates to <see cref="RegisterProtocolHandlers"/>
		/// for protocol-specific handler registration.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);
			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(OnServerClientHandshakeReceived, false);
			RegisterProtocolHandlers(networkManager);
		}

		/// <summary>
		/// Registers protocol-specific broadcast handlers (e.g., SRP verify/proof or token auth).
		/// Called once during <see cref="InitializeOnce"/>.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		protected abstract void RegisterProtocolHandlers(NetworkManager networkManager);

		/// <summary>
		/// Initializes bounded channels and starts async workers for processing auth requests.
		/// Called after the Server reference is assigned and infrastructure is ready.
		/// </summary>
		public void InitializeWorkers()
		{
			ShutdownWorkers();
			cookieHmacKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength);
			workerCts = new CancellationTokenSource();
			InitializeWorkersCore(workerCts.Token);
		}

		/// <summary>
		/// Subclass-specific worker initialization: create channels and start worker tasks.
		/// Called after the base creates the <see cref="workerCts"/>.
		/// </summary>
		/// <param name="cancellationToken">Token for signalling worker shutdown.</param>
		protected abstract void InitializeWorkersCore(CancellationToken cancellationToken);

		/// <summary>
		/// Gracefully shuts down all async workers and disposes channel resources.
		/// Calls <see cref="ShutdownWorkersCore"/> first to allow subclasses to complete
		/// channel writers for graceful worker exit before the cancellation token fires.
		/// </summary>
		/// <remarks>
		/// Zeroing <see cref="cookieHmacKey"/> is intentionally fail-closed: any in-flight
		/// handshakes holding cookies signed with the outgoing key will fail verification
		/// on their next attempt and be disconnected. This prevents stale cookie acceptance
		/// across authenticator restarts at the cost of a brief connection disruption for
		/// clients whose handshake spans the restart window.
		/// </remarks>
		public void ShutdownWorkers()
		{
			ShutdownWorkersCore();
			workerCts?.Cancel();
			workerCts?.Dispose();
			workerCts = null;
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
			DrainMainThreadQueue(drainAll: true);
		}

		/// <summary>
		/// Subclass-specific worker shutdown: complete channel writers, null channel references,
		/// and clear subclass-specific state. Called BEFORE the base cancels the CTS and clears
		/// shared state.
		/// </summary>
		protected abstract void ShutdownWorkersCore();

		/// <summary>
		/// Drains the main-thread response queue each frame and runs periodic sweeps.
		/// </summary>
		private void Update()
		{
			DrainMainThreadQueue(drainAll: false);

			float dt = Time.deltaTime;

			nextAuthSweepSeconds -= dt;
			if (nextAuthSweepSeconds <= 0f)
			{
				nextAuthSweepSeconds = AuthSweepIntervalSeconds;
				SweepStaleAuthentication();
				OnAuthSweep();
			}

			nextHandshakeRateLimitSweepSeconds -= dt;
			if (nextHandshakeRateLimitSweepSeconds <= 0f)
			{
				nextHandshakeRateLimitSweepSeconds = HandshakeRateLimitSweepIntervalSeconds;
				SweepExpiredHandshakeRateLimits();
			}

			// Global handshake rate-limit window reset — wall-clock based
			// so a single long frame cannot silently extend the window.
			// NOTE: A 2× burst is theoretically possible at the boundary if the network
			// thread increments the counter between the Exchange(0) and the time update.
			// This is acceptable for a rate limiter — the cap is a soft defence, not a
			// hard guarantee, and the cookie challenge already filters most abuse.
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow >= nextGlobalHandshakeResetUtc)
			{
				nextGlobalHandshakeResetUtc = utcNow.AddSeconds(1);
				Interlocked.Exchange(ref globalHandshakeCount, 0);
			}

			OnUpdate();
		}

		/// <summary>
		/// Override for subclass-specific per-frame logic (e.g., additional periodic sweeps).
		/// </summary>
		protected virtual void OnUpdate() { }

		/// <summary>
		/// Override for subclass-specific logic that runs at the auth sweep interval
		/// (e.g., sweeping stale unauthenticated account state).
		/// </summary>
		protected virtual void OnAuthSweep() { }

		#endregion

		#region Main Thread Queue

		/// <summary>
		/// Drains queued actions without locks using ConcurrentQueue.
		/// This reduces contention when many workers and the network thread enqueue simultaneously.
		/// </summary>
		/// <param name="drainAll">If true, drains all queued actions; otherwise caps at <see cref="maxMainThreadActionsPerUpdate"/>.</param>
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
					// Isolate exceptions per action so one failing callback does not
					// silently abort remaining queued auth actions in this drain pass.
					Log.Error(LogPrefix, $"Exception in main-thread auth action: {ex}");
				}
			}

			// If we hit the per-frame cap without fully draining, log a warning so
			// operators can tune maxMainThreadActionsPerUpdate or investigate load.
			if (!drainAll && mainThreadQueue.Count > 0)
			{
				Log.Warning(LogPrefix, $"Main-thread queue back-pressure: {mainThreadQueue.Count} actions remain after draining {maxMainThreadActionsPerUpdate}.");
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		protected void EnqueueMainThread(Action action)
		{
			mainThreadQueue.Enqueue(action);
		}

		#endregion

		#region Handshake

		/// <summary>
		/// UDP gate: Handles the initial handshake broadcast from a client.
		/// Uses a two-phase stateless cookie challenge followed by X25519 ECDH key agreement.
		/// Runs inline on the network thread (no database or heavy work).
		/// </summary>
		/// <remarks>
		/// <para><b>Cookie challenge (phase 1):</b> On the first handshake from a client
		/// (Cookie is null), the server replies with a stateless HMAC cookie without performing
		/// any X25519 computation. The client must echo this cookie in a subsequent
		/// <see cref="ClientHandshake"/> to prove it can receive replies from the server.
		/// This prevents spoofed-source-IP attacks from burning ECDH CPU.</para>
		/// <para><b>ECDH key agreement (phase 2):</b> After cookie verification the server
		/// performs X25519 ECDH and derives directional AES-256 session keys.
		/// Both ephemeral keypairs are discarded immediately for forward secrecy.</para>
		/// <para><b>Idempotency:</b> If a connection already has encryption data established,
		/// duplicate handshake packets are silently dropped.</para>
		/// <para><b>Latency cost:</b> The cookie adds one extra round-trip before key agreement
		/// completes. This is acceptable because it runs once per connection and prevents
		/// the far more expensive X25519 computation from being triggered by spoofed traffic.</para>
		/// </remarks>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The handshake message containing the client's X25519 public key.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerClientHandshakeReceived(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			if (conn.IsAuthenticated ||
				msg.PublicKey == null ||
				msg.PublicKey.Length != CryptoHelper.X25519PublicKeyLength)
			{
				conn.Disconnect(true);
				return;
			}

			// Idempotency guard: if encryption data already exists for this connection,
			// the handshake was already processed. Drop duplicate packets to prevent
			// session state confusion and prefix desync.
			if (Server.AccountManager.GetConnectionEncryptionData(conn, out _))
				return;

			if (Server.AccountManager.IsAuthInProgress(conn))
				return;

			// Snapshot the HMAC key reference to prevent a race where ShutdownWorkers
			// zeroes the key array while an in-flight HMAC computation is using it.
			byte[] hmacKeySnapshot = cookieHmacKey;
			if (hmacKeySnapshot == null)
			{
				conn.Disconnect(true);
				return;
			}

			// ── Phase 1: Cookie challenge ───────────────────────────────────
			// First handshake (no cookie): reply with a stateless HMAC cookie.
			// Only one HMAC computation runs — no X25519, no state allocation.
			if (msg.Cookie == null)
			{
				string challengeIp = NormalizeIp(conn.GetAddress());
				byte[] cookie = ComputeHandshakeCookie(challengeIp, msg.PublicKey, GetTimeBucket(), hmacKeySnapshot);
				NetworkManager.ServerManager.Broadcast(conn, new ServerHandshake()
				{
					PublicKey = null,
					Cookie = cookie,
				}, false, Channel.Reliable);
				return;
			}

			// ── Phase 2: Cookie verification ────────────────────────────────
			// Client echoed a cookie. Verify against the current and immediately
			// preceding time bucket to tolerate bucket-boundary crossings.
			string remoteIp = NormalizeIp(conn.GetAddress());
			{
				uint currentBucket = GetTimeBucket();
				if (!VerifyHandshakeCookie(msg.Cookie, remoteIp, msg.PublicKey, currentBucket, hmacKeySnapshot) &&
					!VerifyHandshakeCookie(msg.Cookie, remoteIp, msg.PublicKey, currentBucket - 1, hmacKeySnapshot))
				{
					conn.Disconnect(true);
					return;
				}
			}

			// ── Per-IP rate limit ───────────────────────────────────────────
			// Checked before the global cap so that per-IP debounce is always
			// updated, even when the global cap would silently drop the request.
			if (!string.IsNullOrEmpty(remoteIp))
			{
				DateTime nowUtc = DateTime.UtcNow;
				if (handshakeIpNextAllowedUtc.TryGetValue(remoteIp, out DateTime nextAllowed) && nowUtc < nextAllowed)
				{
					conn.Disconnect(true);
					return;
				}
				handshakeIpNextAllowedUtc[remoteIp] = nowUtc.AddSeconds(HandshakeIpDebounceSeconds);
			}

			// ── Global rate limit ───────────────────────────────────────────
			// Bounds total X25519 CPU regardless of IP diversity (botnet defence).
			// Silent drop instead of hard disconnect: avoids weaponising the cap
			// against legitimate players. The connection stays alive; the client's
			// cookie remains valid and they can retry on a subsequent tick.
			// Decrement on rejection to prevent over-counting under sustained load.
			//
			// ASYMMETRY NOTE: Successful handshakes intentionally consume a count
			// slot for the full timer window (reset in OnUpdate). Only rejections
			// decrement immediately. This means the effective per-window budget is
			// MaxGlobalHandshakesPerSecond, and successful completions consume from
			// that budget until the next window reset.
			if (Interlocked.Increment(ref globalHandshakeCount) > MaxGlobalHandshakesPerSecond)
			{
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			// Begin TTL tracking only after all rate-limit gates have passed.
			// This avoids dictionary pressure from connections that were rate-limited.
			if (!TrackAuthStart(conn))
			{
				// Pending auth cap reached — drop without disconnect (like global rate limit).
				// Rate-limit this warning to prevent log flooding under sustained attack.
				DateTime capNow = DateTime.UtcNow;
				if (capNow >= nextPendingAuthCapWarningUtc)
				{
					nextPendingAuthCapWarningUtc = capNow.AddSeconds(5);
					Log.Warning(LogPrefix, $"Pending auth cap ({MaxPendingAuthConnections}) reached — handshake(s) dropped.");
				}
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			// ── X25519 ECDH key agreement ───────────────────────────────────
			// Negotiate protocol version from client's advertised range.
			ushort agreedVersion;
			try
			{
				agreedVersion = CryptoHelper.NegotiateProtocolVersion(msg.MinVersion, msg.MaxVersion);
			}
			catch (CryptographicException)
			{
				Interlocked.Decrement(ref globalHandshakeCount);
				conn.Disconnect(true);
				return;
			}

			// Atomically register encryption data. If another concurrent handshake
			// packet already registered for this connection, TryAdd returns false and
			// we silently drop — the first packet's ECDH result wins.
			if (!Server.AccountManager.TryAddConnectionEncryptionData(conn, msg.PublicKey))
			{
				// Clean up TTL tracking entries added by TrackAuthStart; otherwise the
				// stale entry consumes a MaxPendingAuthConnections slot until the sweep.
				ClearTransientAuthState(conn.ClientId);
				Interlocked.Decrement(ref globalHandshakeCount);
				return;
			}

			if (Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				encryptionData.AgreedVersion = agreedVersion;

				try
				{
					// Generate ephemeral server X25519 keypair — private key is never exposed.
					using var serverKeyPair = new CryptoHelper.X25519EphemeralKeyPair();

					// Compute transcript hash with domain separation and version binding
					// to prevent cross-protocol replay and version downgrade attacks.
					// Layout: SHA256(domain || clientPub || serverPub || clientMin(2B) || clientMax(2B) || agreed(2B))
					byte[] transcriptHash;
					using (var sha = SHA256.Create())
					{
						sha.TransformBlock(CryptoHelper.HandshakeDomainSeparator, 0, CryptoHelper.HandshakeDomainSeparator.Length, null, 0);
						sha.TransformBlock(msg.PublicKey, 0, msg.PublicKey.Length, null, 0);
						sha.TransformBlock(serverKeyPair.PublicKey, 0, serverKeyPair.PublicKey.Length, null, 0);
						// Bind the client's advertised version range and the negotiated version
						// so that any MITM modification causes a transcript mismatch → key mismatch.
						byte[] versionBytes = new byte[6];
						versionBytes[0] = (byte)(msg.MinVersion >> 8);
						versionBytes[1] = (byte)msg.MinVersion;
						versionBytes[2] = (byte)(msg.MaxVersion >> 8);
						versionBytes[3] = (byte)msg.MaxVersion;
						versionBytes[4] = (byte)(agreedVersion >> 8);
						versionBytes[5] = (byte)agreedVersion;
						sha.TransformFinalBlock(versionBytes, 0, versionBytes.Length);
						transcriptHash = sha.Hash;
					}

					// Derive shared secret via X25519 ECDH + HKDF.
					// DeriveSharedSecret auto-zeros the private key after use (single-use).
					byte[] sharedSecret = serverKeyPair.DeriveSharedSecret(msg.PublicKey, transcriptHash);

					// Derive directional session keys from shared secret + transcript hash.
					// DeriveSessionKeys zeroes masterSecret (sharedSecret) internally.
					var sessionKeys = CryptoHelper.DeriveSessionKeys(sharedSecret, transcriptHash);
					// sharedSecret already zeroed by DeriveSessionKeys.

					// Zero transcript hash — no longer needed for key derivation.
					CryptographicOperations.ZeroMemory(transcriptHash);

					encryptionData.PromoteToDirectional(sessionKeys);

					// serverKeyPair.PublicKey is broadcast to the client — it is public, not secret.
					NetworkManager.ServerManager.Broadcast(conn, new ServerHandshake()
					{
						PublicKey = serverKeyPair.PublicKey,
						AgreedVersion = agreedVersion,
					}, false, Channel.Reliable);
				}
				catch (Exception ex)
				{
					Log.Warning(LogPrefix, $"X25519 handshake failed: {ex.Message}");
					Server.AccountManager.RemoveConnectionAccount(conn);
					conn.Disconnect(true);
				}
			}
			else
			{
				Log.Warning(LogPrefix, "Failed to create encryption data for connection.");
				conn.Disconnect(true);
			}
		}

		/// <summary>
		/// Returns the current UTC time bucket index used for cookie expiration.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static uint GetTimeBucket()
		{
			return (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / CookieTimeBucketSeconds);
		}

		/// <summary>
		/// Computes a stateless HMAC-SHA256 handshake cookie binding the client's IP,
		/// public key, and time bucket. The server stores no state — validity is
		/// re-derived on echo.
		/// </summary>
		/// <remarks>
		/// <para><b>Input structure:</b> The HMAC input is [domain‖timeBucket(4B)‖ipBytes‖publicKey].
		/// No explicit length delimiter separates IP from public key; this is safe because
		/// the public key is fixed at <see cref="CryptoHelper.X25519PublicKeyLength"/> bytes
		/// (validated before reaching this method) and the domain separator + time bucket
		/// are fixed-width, so the boundary between IP and key is unambiguous.</para>
		/// <para><b>Replay:</b> Cookies are not single-use — a valid cookie may be replayed
		/// within its validity window (up to 2× <see cref="CookieTimeBucketSeconds"/>).
		/// Replay only permits re-attempting the ECDH handshake, which is still gated by
		/// per-IP and global rate limits. This is the standard SYN-cookie trade-off:
		/// statelessness in exchange for bounded replay.</para>
		/// </remarks>
		private byte[] ComputeHandshakeCookie(string remoteIp, byte[] clientPublicKey, uint timeBucket, byte[] hmacKey)
		{
			byte[] ipBytes = string.IsNullOrEmpty(remoteIp) ? Array.Empty<byte>() : Encoding.ASCII.GetBytes(remoteIp);
			// +2 for IP length prefix to eliminate concatenation ambiguity between
			// variable-length IP strings (7–45 chars) and the fixed-width public key.
			int dataLen = CookieDomainSeparator.Length + 4 + 2 + ipBytes.Length + clientPublicKey.Length;
			byte[] data = new byte[dataLen];
			int offset = 0;

			// Domain separator prevents cross-purpose key reuse.
			Buffer.BlockCopy(CookieDomainSeparator, 0, data, offset, CookieDomainSeparator.Length);
			offset += CookieDomainSeparator.Length;

			// Time bucket (4 bytes big-endian)
			data[offset++] = (byte)(timeBucket >> 24);
			data[offset++] = (byte)(timeBucket >> 16);
			data[offset++] = (byte)(timeBucket >> 8);
			data[offset++] = (byte)timeBucket;

			// IP length prefix (2 bytes big-endian) disambiguates the boundary
			// between the variable-length IP and the fixed-width public key.
			data[offset++] = (byte)(ipBytes.Length >> 8);
			data[offset++] = (byte)ipBytes.Length;

			Buffer.BlockCopy(ipBytes, 0, data, offset, ipBytes.Length);
			offset += ipBytes.Length;
			Buffer.BlockCopy(clientPublicKey, 0, data, offset, clientPublicKey.Length);

			byte[] cookie;
			using (var hmac = new HMACSHA256(hmacKey))
			{
				cookie = hmac.ComputeHash(data);
			}
			CryptographicOperations.ZeroMemory(data);
			return cookie;
		}

		/// <summary>
		/// Verifies a handshake cookie against a specific time bucket in constant time.
		/// </summary>
		/// <remarks>
		/// <para><b>Two-bucket check:</b> The caller checks current and previous buckets via
		/// short-circuit <c>&amp;&amp;</c>. If the current-bucket check succeeds, the previous-bucket
		/// check is skipped, leaking approximately which 30-second bucket issued the cookie.
		/// This is acceptable because the time bucket is not secret — it is derivable from
		/// public wall-clock time.</para>
		/// </remarks>
		private bool VerifyHandshakeCookie(byte[] cookie, string remoteIp, byte[] clientPublicKey, uint timeBucket, byte[] hmacKey)
		{
			if (cookie == null || cookie.Length != CryptoHelper.HmacTagLength)
				return false;
			byte[] expected = ComputeHandshakeCookie(remoteIp, clientPublicKey, timeBucket, hmacKey);
			bool valid = CryptoHelper.FixedTimeEquals(cookie, expected);
			CryptographicOperations.ZeroMemory(expected);
			return valid;
		}

		/// <summary>
		/// Normalises a raw IP address string to its canonical form via <see cref="IPAddress"/>.
		/// IPv4-mapped IPv6 addresses (e.g., <c>::ffff:192.168.1.1</c>) are collapsed to plain IPv4
		/// so that both representations share a single rate-limit and cookie identity.
		/// Returns an empty string for null/unparseable input to prevent malformed IP strings
		/// from bypassing rate-limit and cookie identity checks.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		protected static string NormalizeIp(string rawIp)
		{
			if (string.IsNullOrEmpty(rawIp))
				return string.Empty;
			if (!IPAddress.TryParse(rawIp, out IPAddress parsed))
				return string.Empty;
			if (parsed.IsIPv4MappedToIPv6)
				parsed = parsed.MapToIPv4();
			return parsed.ToString();
		}

		#endregion

		#region Auth Tracking

		/// <summary>
		/// Starts auth TTL tracking for a connection if not already tracked.
		/// Returns <c>false</c> if the pending authentication cap (<see cref="MaxPendingAuthConnections"/>)
		/// has been reached, preventing memory exhaustion from half-open connection floods.
		/// </summary>
		/// <remarks>
		/// The <c>Count</c> check and <c>TryAdd</c> are not atomic — under extreme
		/// concurrency the count may overshoot the cap by up to (thread-count ×
		/// handshake-rate-per-tick) entries before the next check observes the
		/// excess. This is intentional: a hard atomic cap would require a global lock
		/// on every handshake, and the stale-auth sweep drains excess entries within
		/// one TTL cycle.
		/// </remarks>
		/// <param name="conn">Connection entering the authentication flow.</param>
		/// <returns><c>true</c> if tracking was started; <c>false</c> if the cap was reached.</returns>
		protected bool TrackAuthStart(NetworkConnection conn)
		{
			if (conn == null) return false;
			if (authStartTimeByClientId.Count >= MaxPendingAuthConnections)
				return false;
			DateTime now = DateTime.UtcNow;
			authStartTimeByClientId.TryAdd(conn.ClientId, now);
			authOriginalStartByClientId.TryAdd(conn.ClientId, now);
			authConnectionByClientId[conn.ClientId] = conn;
			return true;
		}

		/// <summary>
		/// Resets the TTL timestamp for a tracked connection to <see cref="DateTime.UtcNow"/>.
		/// Call from async workers at meaningful progress points (e.g., after SRP verify
		/// or database lookups) to prevent the stale-auth sweep from purging connections
		/// whose authentication is legitimately slow (GC stalls, database latency).
		/// Thread-safe — may be called from any thread.
		/// </summary>
		/// <remarks>
		/// <para><b>Hard deadline:</b> Refresh is refused once the connection has exceeded
		/// <see cref="AuthHardDeadlineSeconds"/> from its original start time. This prevents
		/// an adversary from extending a connection's auth window indefinitely by triggering
		/// repeated slow operations (e.g., database queries that refresh the TTL).</para>
		/// </remarks>
		/// <param name="conn">Connection whose TTL to refresh.</param>
		protected void RefreshAuthTtl(NetworkConnection conn)
		{
			if (conn == null) return;
			// Enforce hard deadline: never extend TTL beyond AuthHardDeadlineSeconds
			// from the original authentication start time.
			if (authOriginalStartByClientId.TryGetValue(conn.ClientId, out DateTime originalStart))
			{
				if ((DateTime.UtcNow - originalStart).TotalSeconds >= AuthHardDeadlineSeconds)
					return;
			}
			authStartTimeByClientId[conn.ClientId] = DateTime.UtcNow;
		}

		/// <summary>
		/// Clears transient per-connection authenticator TTL tracking state.
		/// Auth state is managed on <see cref="AccountData"/> and cleared by
		/// <see cref="PurgeConnectionAuthState"/> or <c>RemoveConnectionAccount</c>.
		/// </summary>
		/// <param name="clientId">FishNet client ID.</param>
		protected void ClearTransientAuthState(int clientId)
		{
			authStartTimeByClientId.TryRemove(clientId, out _);
			authOriginalStartByClientId.TryRemove(clientId, out _);
			authConnectionByClientId.TryRemove(clientId, out _);
		}

		/// <summary>
		/// Purges all authenticator state for a connection and optionally disconnects it.
		/// TTL tracking is cleared <b>before</b> disconnect to prevent
		/// races with late-arriving packets on the network thread re-setting the gate.
		/// Calls <see cref="OnPurgeConnectionState"/> for subclass-specific cleanup before
		/// removing account data.
		/// </summary>
		/// <param name="conn">Connection to purge.</param>
		/// <param name="disconnect">If true and active, disconnect the client after purge.</param>
		protected void PurgeConnectionAuthState(NetworkConnection conn, bool disconnect)
		{
			if (conn == null) return;
			ClearTransientAuthState(conn.ClientId);
			OnPurgeConnectionState(conn);
			Server?.AccountManager?.RemoveConnectionAccount(conn);
			if (disconnect && conn.IsActive)
				conn.Disconnect(false);
		}

		/// <summary>
		/// Override for subclass-specific cleanup during connection purge (e.g., clearing IP cache).
		/// Called before <c>AccountManager.RemoveConnectionAccount</c> but after
		/// <see cref="ClearTransientAuthState"/> has removed TTL tracking.
		/// </summary>
		/// <remarks>
		/// <para><b>Ordering:</b> The purge sequence is:
		/// (1) <see cref="ClearTransientAuthState"/> — remove TTL entries,
		/// (2) <see cref="OnPurgeConnectionState"/> — subclass cleanup,
		/// (3) <c>AccountManager.RemoveConnectionAccount</c> — remove encryption + account data,
		/// (4) optionally disconnect.</para>
		/// </remarks>
		/// <param name="conn">The connection being purged.</param>
		protected virtual void OnPurgeConnectionState(NetworkConnection conn) { }

		#endregion

		#region Sweeps

		/// <summary>
		/// Disconnects and purges connections that exceeded the authentication TTL window
		/// without completing authentication. Scans are bounded by <see cref="AuthSweepMaxScan"/>
		/// and <see cref="AuthSweepMaxRemovals"/> to keep main-thread cost predictable.
		/// </summary>
		/// <remarks>
		/// <para><b>Scan coverage:</b> <see cref="ConcurrentDictionary{TKey,TValue}"/> enumeration
		/// does not guarantee a fixed traversal order. Entries beyond the scan cap may not be
		/// evaluated in a single pass, but repeated sweeps (every <see cref="AuthSweepIntervalSeconds"/>
		/// seconds) will eventually reach all entries. This is acceptable because the hard deadline
		/// (<see cref="AuthHardDeadlineSeconds"/>) ensures no entry can persist indefinitely.</para>
		/// <para><b>Max removals vs max scans:</b> <see cref="AuthSweepMaxRemovals"/> is intentionally
		/// lower than <see cref="AuthSweepMaxScan"/> to limit the number of disconnects per sweep
		/// while still scanning enough entries to make progress. Non-stale entries are skipped
		/// cheaply with a DateTime comparison.</para>
		/// </remarks>
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

				// Atomically claim ownership via TryRemove to prevent double-purge
				// if a concurrent path (connection state change, subclass sweep) also
				// triggers purge for this connection.
				// SAFE DOUBLE-PURGE: Even if both this sweep and a concurrent TOTP/token
				// handler purge race on the same ClientId, the worst outcome is redundant
				// TryRemove calls on ConcurrentDictionary (returns false) and an extra
				// OnPurgeConnectionState call — which is idempotent for ConcurrentDictionary
				// operations. No state corruption occurs.
				if (!authStartTimeByClientId.TryRemove(kvp.Key, out _))
					continue;

				removed++;

				authOriginalStartByClientId.TryRemove(kvp.Key, out _);
				authConnectionByClientId.TryRemove(kvp.Key, out NetworkConnection conn);

				if (conn != null)
				{
					OnPurgeConnectionState(conn);
					Server?.AccountManager?.RemoveConnectionAccount(conn);
					if (conn.IsActive)
						conn.Disconnect(false);
				}
				else
				{
					// Connection reference was lost (e.g., FishNet recycled the object before
					// the sweep ran). Dictionary entries have been cleaned up above, but no
					// AccountManager cleanup can run without a valid connection reference.
					// This is harmless — the account-level entries will be purged by the
					// AccountManager's own unauthenticated-connection sweep.
					//
					// RECYCLED-ID RISK: If FishNet has already reassigned this ClientId to a
					// new connection, the dictionary entries we removed above belonged to the
					// OLD connection. The new connection's entries (if any) are safe because
					// they would have been added AFTER the old reference was lost, so
					// authConnectionByClientId would hold the new object (not null). If a
					// subclass sweep (e.g., SweepUnauthenticatedConnections) iterates by
					// ClientId, it should validate that the connection object it retrieves is
					// the same one that created the entry (ReferenceEquals) before acting.
					Log.Warning(LogPrefix, $"SweepStaleAuthentication: conn was null for ClientId {kvp.Key} — dictionary entries cleaned.");
				}
			}
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

		#region Connection State

		/// <summary>
		/// Handles remote connection state changes to clean up account data when a connection stops.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="args">Arguments describing the connection state change.</param>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		#endregion

		#region Virtual Methods

		/// <summary>
		/// Invokes the authentication result event for a connection.
		/// On success, clears TTL tracking so the stale-auth sweep does not
		/// purge the now-authenticated connection.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="authenticated">True if authentication succeeded, false otherwise.</param>
		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			// Always clear TTL tracking regardless of outcome. On failure, the
			// connection will be disconnected and orphaned entries would linger
			// until the stale-auth sweep.
			if (conn != null)
				ClearTransientAuthState(conn.ClientId);

			OnAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>
		/// Invokes the client authentication result event for post-authentication logic.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="authenticated">True if authentication succeeded, false otherwise.</param>
		protected void InvokeClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			OnClientAuthenticationResult?.Invoke(conn, authenticated);
		}

		/// <summary>
		/// Sends a result broadcast and disconnects the client on the main thread.
		/// Common pattern used by all authenticator types for terminal failure responses.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="result">The authentication result to send.</param>
		protected void SendResultAndDisconnect(NetworkConnection conn, ClientAuthenticationResult result)
		{
			EnqueueMainThread(() =>
			{
				if (conn.IsActive)
				{
					NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
					{
						Result = result,
					}, false, Channel.Reliable);
					conn.Disconnect(false);
				}
			});
		}

		/// <summary>
		/// Sends a terminal failure result, disconnects the client, and immediately purges
		/// all transient and AccountManager auth state for the connection.
		/// Combines <see cref="SendResultAndDisconnect"/> and
		/// <see cref="PurgeConnectionAuthState"/> for the common worker-thread
		/// error-exit pattern.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="result">The authentication result to send.</param>
		protected void RejectAndPurge(NetworkConnection conn, ClientAuthenticationResult result)
		{
			SendResultAndDisconnect(conn, result);
			PurgeConnectionAuthState(conn, disconnect: false);
		}

		/// <summary>
		/// Attempts to complete login authentication for a user. Override in subclasses for
		/// server-type-specific logic (e.g., WorldServer checks player limit and selected character).
		/// </summary>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username to authenticate.</param>
		/// <returns>Final authentication result.</returns>
		internal virtual Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			return Task.FromResult(result);
		}

		#endregion
	}
}