using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.Collections;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Server Authenticator using SRP-6a protocol with bounded channel architecture.
	/// Broadcast handlers act as ultra-fast UDP receiver gates with zero blocking — all heavy
	/// crypto, database, and SRP work is offloaded to async workers via bounded channels.
	/// Thread-safe: broadcast handlers run on the network thread, workers run on thread pool threads.
	/// </summary>
	/// <remarks>
	/// <para><b>INVARIANT — AES-GCM failure is connection-fatal:</b> Any GCM tag mismatch
	/// permanently invalidates the session. No retries, no partial continuation, no oracle.
	/// The handler logs, sends a generic failure, disconnects, and purges all state.</para>
	/// <para><b>DropWrite self-healing:</b> Bounded channels use
	/// <c>BoundedChannelFullMode.DropWrite</c>. If a TryWrite fails after an
	/// <see cref="AuthState"/> advance, the handler immediately rolls the state back
	/// to its prior value so the client can retry without re-handshaking.
	/// <see cref="BaseServerAuthenticator.SweepStaleAuthentication"/> remains the ultimate safety net,
	/// unconditionally purging all auth state for connections exceeding
	/// the auth TTL without completing authentication.</para>
	/// </remarks>
	public class ServerAuthenticator : BaseServerAuthenticator
	{
		/// <summary>
		/// Number of concurrent workers processing SRP verify requests.
		/// </summary>
		private const int VerifyWorkerCount = 2;

		/// <summary>
		/// Number of concurrent workers processing SRP proof requests.
		/// </summary>
		private const int ProofWorkerCount = 2;

		/// <summary>
		/// Bounded channel capacity for SRP verify requests.
		/// </summary>
		private const int VerifyChannelCapacity = 500;

		/// <summary>
		/// Bounded channel capacity for SRP proof requests.
		/// </summary>
		private const int ProofChannelCapacity = 500;

		/// <summary>
		/// Maximum AccountManager unauthenticated entries evaluated per sweep.
		/// </summary>
		private const int AccountManagerSweepMaxScan = 256;

		/// <summary>
		/// Maximum stale AccountManager unauthenticated entries purged per sweep.
		/// </summary>
		private const int AccountManagerSweepMaxRemovals = 64;

		/// <summary>
		/// Minimum seconds between persisted kick requests for the same account.
		/// </summary>
		private const float KickRequestDebounceSeconds = 10f;

		/// <summary>
		/// Sweep interval in seconds for stale kick-request debounce entries.
		/// </summary>
		private const float KickDebounceSweepIntervalSeconds = 60f;

		/// <summary>
		/// Minimum seconds between SRP verify attempts from the same IP.
		/// </summary>
		/// <remarks>
		/// <para><b>NAT consideration:</b> Multiple legitimate users behind a shared NAT/CGNAT
		/// gateway share a single IP. This debounce may cause brief delays for subsequent users.
		/// The 1-second window balances abuse mitigation against NAT user impact.
		/// If NAT issues arise in production, consider per-IP-plus-port tracking or raising
		/// the debounce only under detected attack conditions.</para>
		/// </remarks>
		private const float IpAuthAttemptDebounceSeconds = 1f;

		/// <summary>
		/// Minimum seconds between SRP verify attempts for the same account name.
		/// </summary>
		private const float AccountVerifyDebounceSeconds = 2f;

		/// <summary>
		/// Maximum entries allowed in the account-verify debounce tracker before new
		/// entries are silently rejected. Guards against memory exhaustion if an attacker
		/// floods unique account names to grow the dictionary unboundedly.
		/// </summary>
		private const int MaxAccountVerifyDebounceEntries = 50_000;

		/// <summary>
		/// Sweep interval in seconds for expired auth rate-limit entries.
		/// </summary>
		private const float AuthRateLimitSweepIntervalSeconds = 60f;

		/// <summary>
		/// Maximum entries scanned per dictionary during a single auth cleanup sweep.
		/// Bounds main-thread cleanup cost during large attacks.
		/// </summary>
		private const int AuthRateLimitCleanupMaxScanPerMap = 256;

		/// <summary>
		/// Maximum removals per dictionary during a single auth cleanup sweep.
		/// </summary>
		private const int AuthRateLimitCleanupMaxRemovePerMap = 128;

		/// <summary>
		/// TTL in seconds for per-connection IP cache entries.
		/// <para>
		/// <b>Coupling note:</b> This value MUST be ≥ the longest IP-based block duration
		/// across all server systems (currently <c>AccountCreationSystem.ipBlockDurationSeconds = 300s</c>).
		/// If the cache TTL is shorter than the block duration, a blocked IP's cache entry can
		/// expire and be re-resolved, but the block is still active — causing a logic error where
		/// the IP is looked up again (minor perf cost, no security breach). If this value is
		/// ever made configurable, add a startup assertion:
		/// <c>Log.Error(..., "ConnectionIpCacheTtlSeconds < AccountCreationSystem.ipBlockDurationSeconds")</c>.
		/// </para>
		/// </summary>
		private const float ConnectionIpCacheTtlSeconds = 300f;

		/// <summary>
		/// Bounded channel for queuing SRP verify requests for async worker processing.
		/// </summary>
		private System.Threading.Channels.Channel<SrpVerifyRequest<NetworkConnection>> verifyChannel;

		/// <summary>
		/// Bounded channel for queuing SRP proof requests for async worker processing.
		/// </summary>
		private System.Threading.Channels.Channel<SrpProofRequest<NetworkConnection>> proofChannel;

		/// <summary>
		/// Per-account kick-request debounce map. Value is next UTC time at which a kick request may be persisted.
		/// </summary>
		private readonly ExpiringKeyTracker<string> kickRequestNextAllowedUtcByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Per-IP auth attempt debounce map. Value is next UTC time a verify attempt is allowed.
		/// </summary>
		private readonly ExpiringKeyTracker<string> ipAuthNextAllowedUtc =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Per-account verify debounce map. Value is next UTC time a verify attempt is allowed.
		/// </summary>
		private readonly ExpiringKeyTracker<string> accountVerifyNextAllowedUtc =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Per-connection IP cache to avoid repeated address allocation/parsing on hot paths.
		/// </summary>
		private readonly LastSeenCacheTracker<int, string> connectionIpCache = new LastSeenCacheTracker<int, string>();

		/// <summary>
		/// Typed reference to the SRP account manager. Cached on worker initialization
		/// to avoid repeated casts from <see cref="IAccountManager{TConnection}"/>.
		/// </summary>
		private ISrpAccountManager<NetworkConnection> srpAccountManager;

		/// <summary>
		/// HMAC key for deriving deterministic per-username fake SRP salts.
		/// Each fake account receives a unique but repeatable salt derived via
		/// HMAC-SHA512(fakeSaltKey, username), preventing attackers from detecting
		/// salt reuse across different non-existent accounts.
		/// </summary>
		private byte[] fakeSaltKey;

		/// <summary>
		/// Volatile backing field for <see cref="TokenSigningKey"/>.
		/// </summary>
		private volatile byte[] _tokenSigningKey;

		/// <summary>
		/// Countdown timer (seconds) until the next kick-request debounce cleanup sweep.
		/// </summary>
		private float nextKickDebounceSweepSeconds = KickDebounceSweepIntervalSeconds;

		/// <summary>
		/// Countdown timer (seconds) until the next auth rate-limit cleanup sweep.
		/// </summary>
		private float nextAuthRateLimitSweepSeconds = AuthRateLimitSweepIntervalSeconds;

		/// <summary>
		/// HMAC signing key for token generation. Set by LoginServerSystem on startup.
		/// If null, token issuance is disabled.
		/// <para><b>Thread safety:</b> Backed by a volatile field to ensure visibility
		/// across the main thread (writer) and worker threads (readers).</para>
		/// </summary>
		public byte[] TokenSigningKey
		{
			get => _tokenSigningKey;
			set => _tokenSigningKey = value;
		}

		/// <summary>
		/// Volatile backing field for <see cref="TotpMasterKey"/>.
		/// </summary>
		private volatile byte[] _totpMasterKey;

		/// <summary>
		/// AES-256 master key for decrypting TOTP secrets from the database during login.
		/// Set by LoginServerSystem on startup. Must match AccountCreationSystem.TotpMasterKey.
		/// </summary>
		public byte[] TotpMasterKey
		{
			get => _totpMasterKey;
			set => _totpMasterKey = value;
		}

		/// <summary>
		/// Maximum concurrent TOTP verification tasks.
		/// Bounds thread-pool usage for TOTP processing, unlike SRP which uses bounded channels.
		/// </summary>
		private const int MaxConcurrentTotpVerifications = 4;

		/// <summary>
		/// Maximum failed TOTP attempts before disconnecting the client.
		/// </summary>
		private const int MaxTotpAttempts = 5;

		/// <summary>
		/// Maximum cumulative TOTP failures per username across all connections before
		/// a temporary lockout. Prevents attackers from reconnecting to reset the per-
		/// connection attempt counter.
		/// </summary>
		private const int MaxTotpFailuresPerUsername = 15;

		/// <summary>
		/// How long a per-username TOTP lockout lasts after exceeding <see cref="MaxTotpFailuresPerUsername"/>.
		/// </summary>
		private static readonly TimeSpan TotpUsernameLockoutDuration = TimeSpan.FromMinutes(5);

		/// <summary>
		/// Maximum entries to scan per sweep when evicting expired TOTP per-username
		/// failure records. Bounds main-thread work per tick.
		/// </summary>
		private const int TotpUsernameFailureSweepMaxScan = 64;

		/// <summary>
		/// Maximum entries allowed in the per-username TOTP failure tracker before new
		/// entries are silently ignored. Guards against memory exhaustion if an attacker
		/// floods unique usernames to grow the dictionary unboundedly.
		/// </summary>
		private const int MaxTotpUsernameFailureEntries = 10_000;

		/// <summary>
		/// Pending TOTP verification state per connection. Stores data needed to complete
		/// login after the client provides a valid TOTP code.
		/// </summary>
		private readonly ConcurrentDictionary<int, TotpPendingState> totpPendingStates = new ConcurrentDictionary<int, TotpPendingState>();

		/// <summary>
		/// Semaphore controlling concurrent TOTP verification tasks to prevent thread-pool exhaustion.
		/// </summary>
		private SemaphoreSlim totpSemaphore;

		/// <summary>
		/// Per-connection cache of TotpEnabled flag, populated during SRP verify (when account data
		/// is already fetched) and consumed during SRP proof to avoid a redundant DB fetch.
		/// <para><b>Bounding:</b> Entry count is bounded by <c>MaxPendingAuthConnections</c> — each
		/// connection can cache at most one flag, and the total number of in-flight auth connections
		/// is capped. Entries are removed by <see cref="OnPurgeConnectionState"/> and sweep cleanup.</para>
		/// </summary>
		private readonly ConcurrentDictionary<int, bool> totpEnabledByClientId = new ConcurrentDictionary<int, bool>();

		/// <summary>
		/// Per-username TOTP failure counter for cross-connection rate limiting.
		/// Key = lowercased username, Value = (failureCount, firstFailureUtc).
		/// Entries are lazily evicted when checked after <see cref="TotpUsernameLockoutDuration"/> expires,
		/// and proactively swept by <see cref="SweepExpiredTotpUsernameFailures"/> during <see cref="OnAuthSweep"/>.
		/// </summary>
		private readonly ConcurrentDictionary<string, (int Count, DateTime FirstFailure)> totpUsernameFailures = new ConcurrentDictionary<string, (int, DateTime)>();

		/// <summary>
		/// Holds intermediate login state while waiting for TOTP code from the client.
		/// </summary>
		private class TotpPendingState
		{
			/// <summary>
			/// The connection that created this pending state. Used to detect stale state
			/// from a recycled ClientId after TTL sweep or disconnect.
			/// </summary>
			public NetworkConnection Connection;
			public ConnectionEncryptionData EncryptionData;
			public string ServerProof;
			public string Username;
			public AccessLevel AccessLevel;

			/// <summary>
			/// Whether <see cref="Username"/> is an email address.
			/// Preserved from SRP verify so that TOTP re-fetch uses the correct
			/// <c>FetchForLoginAsync</c> overload (email vs username column lookup).
			/// </summary>
			public bool IsEmail;

			/// <summary>
			/// Failed TOTP attempt counter. Must be accessed exclusively via
			/// <see cref="System.Threading.Interlocked"/> methods since the network thread
			/// and async workers may read/write concurrently.
			/// Interlocked operations provide full memory barriers, so volatile is
			/// not strictly required, but it documents the cross-thread intent.
			/// </summary>
			public int Attempts;
		}

		/// <summary>
		/// LoginServer database ID for token generation. Set by LoginServerSystem on startup.
		/// </summary>
		public long LoginServerId { get; set; }

		/// <summary>
		/// Token expiration duration in minutes. Defaults to 10 minutes.
		/// </summary>
		[SerializeField] private float tokenExpirationMinutes = 10f;

		/// <summary>
		/// Registers SRP-specific broadcast handlers for client authentication steps.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<TwoFactorVerifyBroadcast>(OnServerTwoFactorVerifyBroadcastReceived, false);
		}

		/// <summary>
		/// Creates SRP bounded channels and starts verify/proof async workers.
		/// </summary>
		/// <param name="cancellationToken">Token for signalling worker shutdown.</param>
		protected override void InitializeWorkersCore(CancellationToken cancellationToken)
		{
			if (!(Server.AccountManager is ISrpAccountManager<NetworkConnection> sam))
				throw new InvalidOperationException($"{LogPrefix}: Server.AccountManager must implement ISrpAccountManager<NetworkConnection>. Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			srpAccountManager = sam;

			// Generate per-username fake salt derivation key (HMAC-SHA512 optimal key length).
			fakeSaltKey = CryptoHelper.GenerateKey(CryptoHelper.HmacSha512KeyLength);

			// Force FakeSrpTuple initialization now to prevent first-use timing
			// side-channel on the first non-existent account lookup.
			_ = SrpService.GetStaticFakeData();

			// Validate that per-username derived fake salts match the length of the
			// static fake salt. A mismatch would create a ciphertext-size oracle that
			// leaks whether an account exists (length of encrypted salt differs).
			var fakeData = SrpService.GetStaticFakeData();
			string testDerivedSalt = SrpService.DerivePerUsernameFakeSalt("__startup_length_check__", fakeSaltKey);
			if (testDerivedSalt.Length != fakeData.Salt.Length)
				Log.Error(LogPrefix, $"Fake salt length mismatch — DerivePerUsernameFakeSalt produced {testDerivedSalt.Length} chars, FakeSrpTuple.Salt is {fakeData.Salt.Length} chars. " +
					"This would create a ciphertext-size oracle leaking account existence.");

			// Initialize TOTP concurrency limiter.
			totpSemaphore = new SemaphoreSlim(MaxConcurrentTotpVerifications, MaxConcurrentTotpVerifications);

			// DropWrite: under load, excess requests are silently discarded rather than
			// back-pressuring the network thread. Handlers roll back AuthState on write
			// failure so clients can retry immediately. SweepStaleAuthentication acts as
			// the ultimate safety net for any stranded connections.
			verifyChannel = System.Threading.Channels.Channel.CreateBounded<SrpVerifyRequest<NetworkConnection>>(new System.Threading.Channels.BoundedChannelOptions(VerifyChannelCapacity)
			{
				FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
				SingleReader = false,
				SingleWriter = false
			});

			proofChannel = System.Threading.Channels.Channel.CreateBounded<SrpProofRequest<NetworkConnection>>(new System.Threading.Channels.BoundedChannelOptions(ProofChannelCapacity)
			{
				FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
				SingleReader = false,
				SingleWriter = false
			});

			for (int i = 0; i < VerifyWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessSrpVerifyRequestsAsync(cancellationToken, workerId);
			}

			for (int i = 0; i < ProofWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessSrpProofRequestsAsync(cancellationToken, workerId);
			}

			Log.Debug(LogPrefix, $"Workers initialized (Verify={VerifyWorkerCount}, Proof={ProofWorkerCount})");
		}

		/// <summary>
		/// Completes SRP channel writers and clears SRP-specific state.
		/// Called before the base cancels the CTS and clears shared state.
		/// </summary>
		protected override void ShutdownWorkersCore()
		{
			// Complete channel writers first to allow workers to drain gracefully
			// before the cancellation token fires.
			verifyChannel?.Writer.TryComplete();
			proofChannel?.Writer.TryComplete();

			verifyChannel = null;
			proofChannel = null;

			if (fakeSaltKey != null)
			{
				CryptographicOperations.ZeroMemory(fakeSaltKey);
				fakeSaltKey = null;
			}

			// Defense-in-depth: zero the token signing key in case the external owner
			// (LoginServerSystem) fails to clean up before authenticator shutdown.
			if (_tokenSigningKey != null)
			{
				CryptographicOperations.ZeroMemory(_tokenSigningKey);
				_tokenSigningKey = null;
			}

			if (_totpMasterKey != null)
			{
				CryptographicOperations.ZeroMemory(_totpMasterKey);
				_totpMasterKey = null;
			}

			totpPendingStates.Clear();
			totpEnabledByClientId.Clear();
			totpUsernameFailures.Clear();
			totpSemaphore?.Dispose();
			totpSemaphore = null;
			kickRequestNextAllowedUtcByAccount.Clear();
			ipAuthNextAllowedUtc.Clear();
			accountVerifyNextAllowedUtc.Clear();
			connectionIpCache.Clear();
		}

		/// <summary>
		/// Sweeps stale unauthenticated SRP/encryption state and expired TOTP
		/// per-username failure entries at the auth sweep interval.
		/// </summary>
		protected override void OnAuthSweep()
		{
			SweepStaleUnauthenticatedAccountState();
			SweepExpiredTotpUsernameFailures();
		}

		/// <summary>
		/// Evicts expired entries from <see cref="totpUsernameFailures"/> whose lockout
		/// window has elapsed. Bounded scan to avoid stalling the main thread.
		/// </summary>
		private void SweepExpiredTotpUsernameFailures()
		{
			DateTime now = DateTime.UtcNow;
			int scanned = 0;
			foreach (var kvp in totpUsernameFailures)
			{
				if (++scanned > TotpUsernameFailureSweepMaxScan)
					break;
				if (now - kvp.Value.FirstFailure > TotpUsernameLockoutDuration)
				{
					totpUsernameFailures.TryRemove(kvp.Key, out _);
				}
			}
		}

		/// <summary>
		/// Runs additional periodic sweeps for kick debounce and auth rate limiting.
		/// </summary>
		protected override void OnUpdate()
		{
			nextKickDebounceSweepSeconds -= Time.deltaTime;
			if (nextKickDebounceSweepSeconds <= 0f)
			{
				nextKickDebounceSweepSeconds = KickDebounceSweepIntervalSeconds;
				CleanupKickRequestDebounceEntries();
			}

			nextAuthRateLimitSweepSeconds -= Time.deltaTime;
			if (nextAuthRateLimitSweepSeconds <= 0f)
			{
				nextAuthRateLimitSweepSeconds = AuthRateLimitSweepIntervalSeconds;
				CleanupAuthRateLimitEntries();
			}
		}

		#region UDP Receiver Gates

		// ──────────────────────────────────────────────────────────────────
		// UDP amplification mitigations (evaluated in order):
		//   1. IsAuthenticated check — reject already-authenticated connections
		//   2. Handshake completion gate — verify/proof require prior encryption setup
		//   3. Per-IP debounce (IpAuthAttemptDebounceSeconds)
		//   4. Per-account debounce (AccountVerifyDebounceSeconds)
		//   5. AuthState gate (TryAdvanceAuthState / HasAuthState)
		//   6. Bounded channel capacity (VerifyChannelCapacity / ProofChannelCapacity)
		//   7. Max payload size (CryptoHelper.MaxSrpPayloadBytes / X25519PublicKeyLength)
		//
		// INVARIANT: Any AES-GCM authentication failure permanently invalidates
		// the session — no retries, no partial continuation, no decryption oracle.
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// UDP gate: Receives SRP verify broadcast, validates connection state, and enqueues
		/// encrypted data for async processing. Zero blocking — no decryption or database work.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The SrpVerify broadcast message containing encrypted credentials.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerSrpVerifyBroadcastReceived(NetworkConnection conn, SrpVerifyBroadcast msg, Channel channel)
		{
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			string ipAddress = ResolveIpAddress(conn);
			if (!TryBeginIpAuthAttempt(ipAddress))
			{
				// Use Unreliable to avoid amplifying server-side send-queue work under flood.
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Unreliable);
				return;
			}

			// Connection-level auth state gate: atomically advance Handshake → VerifyPending.
			// Prevents duplicate SRP verify processing for the same connection.
			if (!Server.AccountManager.TryAdvanceAuthState(conn, AuthState.Handshake, AuthState.VerifyPending))
			{
				return;
			}

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no decryption on network thread
			if (msg.S == null || msg.S.Length == 0 || msg.S.Length > CryptoHelper.MaxSrpPayloadBytes ||
				msg.PublicEphemeral == null || msg.PublicEphemeral.Length == 0 || msg.PublicEphemeral.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new SrpVerifyRequest<NetworkConnection>(
				conn,
				msg.S,
				msg.PublicEphemeral,
				encryptionData,
				msg.Seq
			);

			if (verifyChannel == null || !verifyChannel.Writer.TryWrite(request))
			{
				// DropWrite self-healing: roll back to Handshake so the client can
				// retry verify without re-handshaking. TTL sweep is the ultimate safety net.
				Server.AccountManager.TryAdvanceAuthState(conn, AuthState.VerifyPending, AuthState.Handshake);
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Unreliable);
			}
		}

		/// <summary>
		/// UDP gate: Receives SRP proof broadcast, validates connection state, and enqueues
		/// encrypted data for async processing. Zero blocking — no decryption or SRP math.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The SrpProof broadcast message containing encrypted proof.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerSrpProofBroadcastReceived(NetworkConnection conn, SrpProofBroadcast msg, Channel channel)
		{
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// Proof is only valid after verify has established SRP data (WaitingForProof).
			// Atomically advance WaitingForProof → ProofPending to prevent duplicate proof processing.
			//
			// Defense-in-depth: also accept VerifyPending for a narrow scheduling edge case
			// where proof arrives before the verify worker completes AddConnectionAccount.
			// If this rare path fires, the proof worker will find SrpData == null and
			// disconnect gracefully — strictly better than a silent timeout.
			// Note: a theoretical TOCTOU exists between the two calls if the verify worker
			// advances state in between, but the TTL sweep provides the ultimate safety net.
			// Track which state the connection came from so the rollback on channel-
			// write failure restores the correct prior state (see end of method).
			AuthState priorState;
			if (Server.AccountManager.TryAdvanceAuthState(conn, AuthState.WaitingForProof, AuthState.ProofPending))
			{
				priorState = AuthState.WaitingForProof;
			}
			else if (Server.AccountManager.TryAdvanceAuthState(conn, AuthState.VerifyPending, AuthState.ProofPending))
			{
				priorState = AuthState.VerifyPending;
			}
			else
			{
				return;
			}

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no SRP math on network thread
			if (msg.Proof == null || msg.Proof.Length == 0 || msg.Proof.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new SrpProofRequest<NetworkConnection>(
				conn,
				msg.Proof,
				encryptionData,
				msg.Seq
			);

			if (proofChannel == null || !proofChannel.Writer.TryWrite(request))
			{
				// DropWrite self-healing: roll back to the state we came from so the
				// client can re-submit proof. TTL sweep is the ultimate safety net.
				Server.AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, priorState);
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Unreliable);
			}
		}

		#endregion

		#region Async Workers

		/// <summary>
		/// Async worker that processes SRP verify requests from the bounded channel.
		/// Performs AES decryption, database lookups (online check, account fetch), and SRP setup.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
		/// <param name="workerId">Worker ID for logging.</param>
		private async Task ProcessSrpVerifyRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"Verify worker {workerId} started");
			try
			{
				// Rely on channel completion (TryComplete in ShutdownWorkers) for graceful exit.
				// CancellationToken.None avoids a redundant cancellation race with completion.
				await foreach (var request in verifyChannel.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					try
					{
						await ProcessSrpVerifyAsync(request);
					}
					catch (Exception ex)
					{
						await Log.Error(LogPrefix, $"Verify worker {workerId} error: {ex}");
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				await Log.Error(LogPrefix, $"Verify worker {workerId} unexpected error: {ex}");
			}

			await Log.Debug(LogPrefix, $"Verify worker {workerId} stopped");
		}

		/// <summary>
		/// Async worker that processes SRP proof requests from the bounded channel.
		/// Performs AES decryption, SRP proof validation, and login finalization.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
		/// <param name="workerId">Worker ID for logging.</param>
		private async Task ProcessSrpProofRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"Proof worker {workerId} started");
			try
			{
				// Rely on channel completion (TryComplete in ShutdownWorkers) for graceful exit.
				// CancellationToken.None avoids a redundant cancellation race with completion.
				await foreach (var request in proofChannel.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					try
					{
						await ProcessSrpProofAsync(request);
					}
					catch (Exception ex)
					{
						await Log.Error(LogPrefix, $"Proof worker {workerId} error: {ex}");
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				await Log.Error(LogPrefix, $"Proof worker {workerId} unexpected error: {ex}");
			}

			await Log.Debug(LogPrefix, $"Proof worker {workerId} stopped");
		}

		#endregion

		#region Request Processing

		// INVARIANT: Every CryptographicException catch in this region treats
		// AES-GCM authentication failure as connection-fatal:
		//   1. Log warning (no sensitive data)
		//   2. Enqueue generic ClientAuthResultBroadcast failure
		//   3. Disconnect
		//   4. PurgeConnectionAuthState
		// No retries, no fallback, no decryption oracle. Do not weaken.

		// ── Design note — String zeroization ─────────────────────────────
		//
		// SRP values (username, verifier, salt, proof) are .NET immutable strings
		// that cannot be deterministically zeroed. The SecureRemotePassword library
		// and database boundary both require string parameters, so byte[] conversion
		// is impractical. ServerSrpData.Clear() nulls all references so the GC can
		// collect them. Intermediate decrypted byte[] buffers ARE zeroed below via
		// CryptographicOperations.ZeroMemory.
		//
		// Proof decrypt ordering: ProcessSrpProofAsync decrypts the client proof
		// BEFORE checking auth state. This is intentional — decrypting first keeps
		// timing uniform regardless of state validity, preventing oracles.
		// ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// Processes a single SRP verify request asynchronously.
		/// Decrypts credentials, checks online status, fetches account data, and initializes SRP state.
		/// All network operations are marshalled to the main thread via the response queue.
		/// </summary>
		/// <remarks>
		/// <para><b>Purge paths:</b> Every early-return error path calls
		/// <see cref="BaseServerAuthenticator.PurgeConnectionAuthState"/> to clean up
		/// TTL tracking, encryption data, and AccountManager state. The only exception
		/// is the success path (<c>waitingForProof = true</c>), which returns without
		/// purging so the proof worker can continue the flow.</para>
		/// </remarks>
		/// <param name="request">The SRP verify request with encrypted credentials.</param>
		private async Task ProcessSrpVerifyAsync(SrpVerifyRequest<NetworkConnection> request)
		{
			NetworkConnection conn = request.Connection;
			ClientAuthenticationResult result;
			bool waitingForProof = false;

			// Decrypt the username (or email) and public ephemeral on worker thread.
			string username;
			string publicEphemeral;
			uint seq = request.Seq;

			try
			{
				if (!SrpService.TryDecryptVerifyFields(
					request.EncryptedUsername, request.EncryptedPublicEphemeral,
					request.EncryptionData, seq, out username, out publicEphemeral))
				{
					await Log.Warning(LogPrefix, "SRP verify field decryption failed.");
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}
			}
			catch (CryptographicException)
			{
				await Log.Warning(LogPrefix, "AES decryption/authentication failed for SRP verify.");
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			// Determine if the identifier is an email address or a username.
			bool isEmail = username.Contains('@');

			// Validate the identifier against the appropriate rules.
			if (isEmail)
			{
				if (!Authentication.IsAllowedEmailUsername(username))
				{
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}
			}
			else
			{
				if (!Authentication.IsAllowedUsername(username))
				{
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}
			}

			if (!TryBeginAccountVerifyAttempt(username))
			{
				RejectAndPurge(conn, ClientAuthenticationResult.ServerBusy);
				return;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				result = ClientAuthenticationResult.ServerBusy;
			}
			else
			{
				try
				{
					// Online check is deferred to proof processing (ProcessSrpProofAsync)
					// to prevent account-existence enumeration. Before proof, both real and
					// non-existent accounts proceed through indistinguishable SRP exchanges.

					// Fetch account for login from database (identifier is username or email).
					DatabaseResult<Database.Data.AccountData> loginResult = await accountService.FetchForLoginAsync(username, isEmail);

					// Refresh TTL after potentially slow database fetch to prevent
					// the stale-auth sweep from purging this connection mid-flow.
					RefreshAuthTtl(conn);

					// Prepare salt/verifier and access level. For non-existent or errored lookups
					// generate a fake SRP verifier so clients cannot enumerate usernames by timing.
					string salt;
					string verifier;
					AccessLevel accessLevel;

					if (!loginResult.IsSuccess)
					{
						// Derive a deterministic per-username fake salt so that different
						// non-existent usernames receive distinct salts, matching the
						// pattern of real accounts. The salt is AES-GCM encrypted before
						// transmission so the value is unobservable on the wire. The SRP
						// server session (modular exponentiation) still dominates timing,
						// preserving indistinguishability from real accounts.
						salt = SrpService.DerivePerUsernameFakeSalt(username, fakeSaltKey);
						verifier = SrpService.GetStaticFakeData().Verifier;
						accessLevel = AccessLevel.Player;
						await Log.Debug(LogPrefix, $"Using pre-computed fake SRP state for non-existent account '{username}' to avoid enumeration.");
					}
					else
					{
						Database.Data.AccountData accountData = loginResult.Data;

						// Reject unverified accounts.
						if (!accountData.Verified)
						{
							if (isEmail)
							{
								// For email-based login, treat unverified accounts as non-existent
								// to prevent email enumeration. The SRP exchange runs with a fake
								// verifier so the timing and response are indistinguishable from
								// a non-existent account. Legitimate users should verify their
								// account first, or log in by username (which still shows
								// AccountUnverified for clear UX feedback).
								salt = SrpService.DerivePerUsernameFakeSalt(username, fakeSaltKey);
								verifier = SrpService.GetStaticFakeData().Verifier;
								accessLevel = AccessLevel.Player;
								await Log.Debug(LogPrefix, $"Using fake SRP state for unverified email-based login to prevent enumeration.");
							}
							else
							{
								RejectAndPurge(conn, ClientAuthenticationResult.AccountUnverified);
								return;
							}
						}
						else
						{
							salt = accountData.Salt;
							verifier = accountData.Verifier;
							accessLevel = (AccessLevel)accountData.AccessLevel;

							// Cache TotpEnabled so ProcessSrpProofAsync can skip a redundant DB fetch.
							totpEnabledByClientId[conn.ClientId] = accountData.TotpEnabled;
						}
					}

					result = ClientAuthenticationResult.SrpVerify;

					// Populate SRP data on the existing AccountData (advances VerifyPending → WaitingForProof).
					// Note: the SRP library validates A % N != 0 at proof time (DeriveSession),
					// which prevents trivial shared secrets. Early validation here would require
					// parsing big integers and is unnecessary given the library's guarantees.
					if (!srpAccountManager.AddConnectionAccount(conn, username, publicEphemeral, salt, verifier, accessLevel))
					{
						// Connection was purged or state was not VerifyPending — discard.
						RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
						return;
					}

					// Extract SRP response data inside the lock; encrypt outside to reduce lock hold time.
					string srpSalt = null;
					string srpPublicServerEphemeral = null;

					// NOTE: TryAdvanceAuthState is called with expected == new == WaitingForProof.
					// This is intentional: it performs a synchronized read of the auth data under
					// the AccountManager lock without transitioning to a different state.
					if (Server.AccountManager.TryAdvanceAuthState(conn, AuthState.WaitingForProof, AuthState.WaitingForProof, (a) =>
						{
							srpSalt = a.SrpData.Salt;
							srpPublicServerEphemeral = a.SrpData.ServerEphemeral.Public;
							return true;
						}))
					{
						// AES encryption runs outside the AccountManager lock.
						byte[] encryptedSalt;
						byte[] encryptedPublicServerEphemeral;
						try
						{
							SrpService.EncryptVerifyResponse(srpSalt, srpPublicServerEphemeral, request.EncryptionData, out encryptedSalt, out encryptedPublicServerEphemeral);
						}
						catch (CryptographicException ex)
						{
							await Log.Error(LogPrefix, $"AES encryption failed for SRP response: {ex.Message}");
							RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
							return;
						}

						waitingForProof = true;
						// Marshal SRP verify response to main thread.
						EnqueueMainThread(() =>
						{
							if (conn.IsActive)
							{
								NetworkManager.ServerManager.Broadcast(conn, new SrpVerifyBroadcast()
								{
									S = encryptedSalt,
									PublicEphemeral = encryptedPublicServerEphemeral,
								}, false, Channel.Reliable);
							}
						});
						return;
					}

					// TryAdvanceAuthState failed — state was changed by a concurrent path.
					result = ClientAuthenticationResult.InvalidUsernameOrPassword;
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"Error during SRP verify: {ex}");
					result = ClientAuthenticationResult.ServerBusy;
				}
			}

			// Marshal authentication result + disconnect to main thread.
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

			if (!waitingForProof)
			{
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		/// <summary>
		/// Processes a single SRP proof request asynchronously.
		/// Decrypts proof, validates it against SRP state, and finalizes authentication via TryLoginAsync.
		/// All network operations are marshalled to the main thread via the response queue.
		/// </summary>
		/// <param name="request">The SRP proof request with encrypted proof data.</param>
		private async Task ProcessSrpProofAsync(SrpProofRequest<NetworkConnection> request)
		{
			NetworkConnection conn = request.Connection;
			int clientId = conn.ClientId;

			// Guard: EncryptionData can be null if the connection was purged between
			// the channel write and this worker consuming the request.
			if (request.EncryptionData == null)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			// Decrypt client proof on worker thread.
			string clientProof;
			try
			{
				clientProof = SrpService.DecryptProof(request.EncryptedClientProof, request.EncryptionData, request.Seq);
			}
			catch (CryptographicException)
			{
				await Log.Warning(LogPrefix, "AES decryption/authentication failed for client proof.");
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			string serverProof = null;
			string username = null;
			AccessLevel accessLevel = AccessLevel.Player;

			// Atomically validate proof and advance auth state: ProofPending → SrpSuccess.
			bool proofValid = Server.AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, AuthState.SrpSuccess, (a) =>
			{
				if (a.SrpData != null && a.SrpData.GetProof(clientProof, out string proof))
				{
					serverProof = proof;
					username = a.SrpData.UserName;
					accessLevel = a.AccessLevel;
					return true;
				}
				return false;
			});

			if (!proofValid || serverProof == null || username == null)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			// Refresh TTL after SRP proof validation which involves modular exponentiation.
			RefreshAuthTtl(conn);

			try
			{
				// ── Deferred online check ──────────────────────────────────────
				// Performed after SRP proof to prevent account-existence enumeration.
				// Before this point, real and non-existent accounts proceed through
				// indistinguishable SRP exchanges. Only after the client proves
				// knowledge of the password do we reveal online status.
				//
				// DUAL-PROOF GUARD: The TryAdvanceAuthState(ProofPending → SrpSuccess)
				// above is the primary serialization point — only one proof worker per
				// connection wins the CAS. For the same *account* from different
				// connections, this online check is the guard: the first proof completes
				// login, and any concurrent proof for the same account sees isOnline=true
				// and receives AlreadyOnline.
				//
				// CAVEAT: The online flag lives on CharacterData. If the server crashes
				// without cleanly logging characters out, stale Online=true flags may
				// persist. Crash recovery should reset all Online flags on startup.
				//
				// AnyOnlineAsync short-circuits at the database level, avoiding
				// the previous FetchManyAsync per-auth DoS vector.
				bool isOnline = false;
				bool hasPendingKick = false;
				if (Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					DatabaseResult<bool> onlineResult = await characterService.AnyOnlineAsync(username);
					if (onlineResult.IsSuccess && onlineResult.Data)
					{
						isOnline = true;
					}
				}

				// Check for a pending kick request that hasn't been processed yet.
				// Without this, a login between kick persist and kick processing could
				// succeed, and the stale kick would terminate the new session.
				if (!isOnline &&
					Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var pendingKickService))
				{
					var pendingResult = await pendingKickService.HasPendingAsync(username);
					if (pendingResult.IsSuccess && pendingResult.Data)
					{
						hasPendingKick = true;
					}
				}

				if (isOnline || hasPendingKick)
				{
					// Persist kick request for the online character (rate-limited per account).
					if (isOnline && TryBeginKickRequest(username) &&
						Server.Database?.ServiceRegistry != null &&
						Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var kickRequestService))
					{
						await kickRequestService.PersistAsync(username);
					}
				}

				// Attempt to complete login authentication (virtual — overridden by WorldServer/SceneServer).
				// Skip login for already-online accounts or those with pending kick requests.
				ClientAuthenticationResult result = (isOnline || hasPendingKick)
					? ClientAuthenticationResult.AlreadyOnline
					: await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username);

				// Refresh TTL after potentially slow database/login checks.
				RefreshAuthTtl(conn);

				// ── TOTP two-factor check ─────────────────────────────────────
				// After SRP proof succeeds and login would succeed, check if the
				// account requires TOTP verification before completing login.
				// TotpEnabled was cached during ProcessSrpVerifyAsync to avoid a
				// redundant database fetch here.
				if (result == ClientAuthenticationResult.LoginSuccess)
				{
					bool totpRequired = false;
					byte[] totpMasterKeySnapshot = TotpMasterKey;
					if (totpMasterKeySnapshot != null && totpMasterKeySnapshot.Length == 32 &&
						totpEnabledByClientId.TryGetValue(conn.ClientId, out bool cachedTotpEnabled) &&
						cachedTotpEnabled)
					{
						totpRequired = true;
					}

					if (totpRequired)
					{
						// Store pending state for TOTP verification completion.
						// AUTH-STATE NOTE: The connection remains in SrpSuccess while awaiting TOTP.
						// SrpSuccess is intentionally untracked from the unauthenticated timer —
						// TryAdvanceAuthState removes the unauthenticated-connection tracking when
						// advancing to SrpSuccess, so SweepUnauthenticatedAccountState will not
						// evict this connection. The stale-auth TTL sweep (BaseServerAuthenticator)
						// is the safety net if TOTP is never completed.
						// SRP material (salt, verifier, ephemeral) remains on AccountData until
						// ProcessTwoFactorVerifyAsync completes and calls ClearSrpState, or until
						// SweepStaleAuthentication purges the connection.
						totpPendingStates[conn.ClientId] = new TotpPendingState
						{
							Connection = conn,
							EncryptionData = request.EncryptionData,
							ServerProof = serverProof,
							Username = username,
							AccessLevel = accessLevel,
							IsEmail = username != null && username.Contains('@'),
							Attempts = 0,
						};

						// Send TwoFactorRequired — client will prompt for TOTP code.
						EnqueueMainThread(() =>
						{
							if (conn.IsActive)
							{
								NetworkManager.ServerManager.Broadcast(conn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorRequired },
									false, Channel.Reliable);
							}
						});
						return;
					}
				}

				// Inclusion list: only LoginSuccess is authenticated. All other results
				// (including future new codes) default to unauthenticated.
				bool authenticated = result == ClientAuthenticationResult.LoginSuccess;

				// Encrypt server proof on worker thread.
				byte[] encryptedServerProof = SrpService.EncryptServerProof(serverProof, request.EncryptionData);

				// Generate and encrypt auth token if signing key is available.
				// Guard: skip token generation if the connection dropped during the
				// proof/login round-trip. Without this, GenerateEncryptedAuthTokenAsync
				// persists a token hash via IssueAsync that will never be redeemed,
				// creating orphaned DB records under disconnect floods.
				// Note: encryptedToken is ciphertext — not sensitive even if the lambda
				// is held alive during main-thread queue backlog.
				byte[] encryptedToken = (authenticated && conn.IsActive)
					? await GenerateEncryptedAuthTokenAsync(request.EncryptionData, username, accessLevel)
					: null;

				// Marshal final broadcast + authentication events to main thread.
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						SrpSuccessBroadcast resultMsg = new SrpSuccessBroadcast()
						{
							Proof = encryptedServerProof,
							Result = result,
							Token = encryptedToken,
						};
						NetworkManager.ServerManager.Broadcast(conn, resultMsg, false, Channel.Reliable);
					}

					/* Invoke result. This is handled internally to complete the connection authentication or kick client.
					 * It's important to call this after sending the broadcast so that the broadcast
					 * makes it out to the client before the kick. */
					OnAuthentication(conn, authenticated);
					InvokeClientAuthenticationResult(conn, authenticated);

					if (authenticated)
					{
						// Advance to terminal Authenticated state and remove sensitive SRP material.
						// Only clear SRP state if the advance succeeded — if it fails, another
						// path already moved past SrpSuccess and cleanup is their responsibility.
						if (Server.AccountManager.TryAdvanceAuthState(conn, AuthState.SrpSuccess, AuthState.Authenticated))
						{
							srpAccountManager.ClearSrpState(conn);
						}
					}
					else
					{
						// On failed authentication, remove all connection/account mappings and encryption state.
						Server.AccountManager.RemoveConnectionAccount(conn);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Error during SRP proof login: {ex}");
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		#endregion

		#region Two-Factor TOTP

		/// <summary>
		/// UDP gate: Handles TwoFactorVerifyBroadcast from clients during TOTP login.
		/// Validates connection state and processes asynchronously.
		/// </summary>
		private void OnServerTwoFactorVerifyBroadcastReceived(NetworkConnection conn, TwoFactorVerifyBroadcast msg, Channel channel)
		{
			if (conn == null || !conn.IsActive)
				return;

			// Only connections with pending TOTP state should send this.
			if (!totpPendingStates.TryGetValue(conn.ClientId, out var pendingState))
			{
				conn.Disconnect(true);
				return;
			}

			// Guard against stale pending state after ClientId reuse: the connection
			// that created the pending state must be the same object as the current one.
			if (!object.ReferenceEquals(pendingState.Connection, conn))
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				conn.Disconnect(true);
				return;
			}

			// Give the user a fresh TTL window while they are actively submitting TOTP codes.
			RefreshAuthTtl(conn);

			// Reject oversized payloads.
			// Purge state before disconnecting so cleanup doesn't race with a
			// reconnect on a recycled ClientId if Disconnect ever becomes async.
			if (msg.Code == null || msg.Code.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			// Per-username rate limit: prevents reconnect-and-retry from resetting
			// the per-connection attempt cap. Entries expire after the lockout window.
			//
			// THREAD NOTE: This lazy eviction runs on the network thread, while
			// SweepExpiredTotpUsernameFailures runs on the main thread. Both call
			// TryRemove on the same ConcurrentDictionary, which is thread-safe.
			// However, an entry that expired microseconds ago may still reject
			// a legitimate TOTP attempt if the sweep hasn't run yet. This is a
			// low-impact correctness edge case at bucket boundaries.
			string userKey = pendingState.Username?.ToLowerInvariant();
			if (userKey != null && totpUsernameFailures.TryGetValue(userKey, out var failInfo))
			{
				if (DateTime.UtcNow - failInfo.FirstFailure > TotpUsernameLockoutDuration)
				{
					// Window expired — evict stale entry.
					totpUsernameFailures.TryRemove(userKey, out _);
				}
				else if (failInfo.Count >= MaxTotpFailuresPerUsername)
				{
					// Locked out — reject immediately.
					totpPendingStates.TryRemove(conn.ClientId, out _);
					EnqueueMainThread(() =>
					{
						if (conn.IsActive) conn.Disconnect(false);
					});
					PurgeConnectionAuthState(conn, disconnect: false);
					return;
				}
			}

			// Gate TOTP verification through the concurrency semaphore to
			// prevent thread-pool exhaustion under burst traffic.
			// Checked BEFORE incrementing attempts so a busy server doesn't
			// silently burn one of the user's allowed retries.
			var sem = totpSemaphore;
			if (sem == null)
			{
				// Shutdown in progress — semaphore was disposed.
				totpPendingStates.TryRemove(conn.ClientId, out _);
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}
			if (!sem.Wait(0))
			{
				// All verification slots are occupied — ask client to retry.
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn,
							new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorInvalid },
							false, Channel.Reliable);
					}
				});
				return;
			}

			// Increment attempt counter AFTER the semaphore gate so that
			// back-pressure rejections don't consume one of the allowed retries.
			int attempts = System.Threading.Interlocked.Increment(ref pendingState.Attempts);
			if (attempts > MaxTotpAttempts)
			{
				sem.Release();
				totpPendingStates.TryRemove(conn.ClientId, out _);
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await ProcessTwoFactorVerifyAsync(conn, msg.Code, msg.Seq, pendingState);
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"TOTP verify error: {ex}");
					totpPendingStates.TryRemove(conn.ClientId, out _);
					EnqueueMainThread(() =>
					{
						if (conn.IsActive) conn.Disconnect(false);
					});
					PurgeConnectionAuthState(conn, disconnect: false);
				}
				finally
				{
					sem.Release();
				}
			});
		}

		/// <summary>
		/// Processes a TOTP verification request asynchronously.
		/// Decrypts the code, verifies against the stored TOTP secret, and completes login on success.
		/// </summary>
		private async Task ProcessTwoFactorVerifyAsync(
			NetworkConnection conn,
			byte[] encryptedCode,
			uint seq,
			TotpPendingState pendingState)
		{
			// Decrypt TOTP code.
			string totpCode;
			try
			{
				if (!pendingState.EncryptionData.TryConsumeReceiveSequence(seq))
				{
					// Sequence replay/out-of-order: track as a per-username failure to prevent
					// reconnect-and-retry abuse with stale sequence numbers.
					TrackTotpUsernameFailure(pendingState.Username);

					// Check if max attempts exceeded (already incremented in outer handler).
					// Attempts is volatile — plain read has acquire semantics.
					if (pendingState.Attempts > MaxTotpAttempts)
					{
						totpPendingStates.TryRemove(conn.ClientId, out _);
						EnqueueMainThread(() =>
						{
							if (conn.IsActive) conn.Disconnect(false);
						});
						PurgeConnectionAuthState(conn, disconnect: false);
						return;
					}

					EnqueueMainThread(() =>
					{
						if (conn.IsActive)
						{
							NetworkManager.ServerManager.Broadcast(conn,
								new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorInvalid },
								false, Channel.Reliable);
						}
					});
					return;
				}

				byte[] nonce = pendingState.EncryptionData.BuildReceiveNonce(seq);
				byte[] aad = new byte[CryptoHelper.AadLength];
				CryptoHelper.WriteAad(aad, (byte)CryptoHelper.AuthMessageType.TwoFactorVerify, pendingState.EncryptionData.AgreedVersion, seq);
				byte[] decryptedCode = CryptoHelper.DecryptAES(pendingState.EncryptionData.ClientToServerKey, nonce, encryptedCode, aad);
				try
				{
					totpCode = CryptoHelper.StrictUtf8.GetString(decryptedCode);
				}
				catch (DecoderFallbackException)
				{
					CryptographicOperations.ZeroMemory(decryptedCode);
					throw new CryptographicException("Malformed UTF-8 in TOTP code.");
				}
				CryptographicOperations.ZeroMemory(decryptedCode);
			}
			catch (CryptographicException)
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			// Fetch account data and verify TOTP code.
			byte[] totpMasterKeySnapshot = TotpMasterKey;
			if (totpMasterKeySnapshot == null || totpMasterKeySnapshot.Length != 32 ||
				Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			var accountResult = await accountService.FetchForLoginAsync(pendingState.Username, pendingState.IsEmail);
			if (!accountResult.IsSuccess || string.IsNullOrEmpty(accountResult.Data.TotpSecret))
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			// Detect code type: TOTP codes are 6-digit numeric; recovery
			// codes use the XXXXX-XXXXX uppercase hex format (11 chars).
			bool isRecoveryCode = false;
			if (totpCode.Length == 11 && totpCode[5] == '-')
			{
				isRecoveryCode = true;
				for (int i = 0; i < 11; i++)
				{
					if (i == 5) continue;
					char c = char.ToUpperInvariant(totpCode[i]);
					if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
					{
						isRecoveryCode = false;
						break;
					}
				}
			}

			if (isRecoveryCode)
			{
				// Recovery code path: fetch stored hashes and verify against each.
				if (!Server.Database.ServiceRegistry.TryGet<ITwoFactorRecoveryCodeService>(out var recoveryCodeService))
				{
					totpPendingStates.TryRemove(conn.ClientId, out _);
					EnqueueMainThread(() =>
					{
						if (conn.IsActive) conn.Disconnect(false);
					});
					PurgeConnectionAuthState(conn, disconnect: false);
					return;
				}

				var storedCodesResult = await recoveryCodeService.FetchUnusedByAccountAsync(pendingState.Username);
				if (!storedCodesResult.IsSuccess || storedCodesResult.Data == null || storedCodesResult.Data.Count == 0)
				{
					TrackTotpUsernameFailure(pendingState.Username);
					EnqueueMainThread(() =>
					{
						if (conn.IsActive)
						{
							NetworkManager.ServerManager.Broadcast(conn,
								new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorInvalid },
								false, Channel.Reliable);
						}
					});
					return;
				}

				string matchedHash = null;
				foreach (var codeData in storedCodesResult.Data)
				{
					if (CryptoHelper.TwoFactor.VerifyRecoveryCode(totpCode, codeData.CodeHash))
					{
						matchedHash = codeData.CodeHash;
						break;
					}
				}

				if (matchedHash == null)
				{
					TrackTotpUsernameFailure(pendingState.Username);
					EnqueueMainThread(() =>
					{
						if (conn.IsActive)
						{
							NetworkManager.ServerManager.Broadcast(conn,
								new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorInvalid },
								false, Channel.Reliable);
						}
					});
					return;
				}

				// Consume the matched recovery code so it cannot be reused.
				await recoveryCodeService.ConsumeCodeAsync(pendingState.Username, matchedHash);
			}
			else
			{
				// Standard TOTP path.
				byte[] plaintextSecret = null;
				try
				{
					plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(totpMasterKeySnapshot, accountResult.Data.TotpSecret);
					var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(plaintextSecret, totpCode, accountResult.Data.LastTotpWindow);

					if (!valid)
					{
						// Track per-username TOTP failures across connections.
						TrackTotpUsernameFailure(pendingState.Username);

						// Invalid TOTP — allow retry.
						EnqueueMainThread(() =>
						{
							if (conn.IsActive)
							{
								NetworkManager.ServerManager.Broadcast(conn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.TwoFactorInvalid },
									false, Channel.Reliable);
							}
						});
						return;
					}

					// TOTP valid — persist anti-replay window.
					// ANTI-REPLAY NOTE: There is an inherent race between VerifyTotpCode (which
					// checks the last-used window) and PersistLastTotpWindowAsync (which writes
					// the new window). Two concurrent requests using the same TOTP code could both
					// pass the in-memory check before either persists. This is mitigated by:
					//   1. The totpSemaphore limits concurrent TOTP verifications (MaxConcurrentTotpVerifications).
					//   2. The per-ClientId TotpPendingState means only one TOTP path is active per connection.
					//   3. PersistLastTotpWindowAsync SHOULD use a conditional update in the DB:
					//      UPDATE accounts SET last_totp_window = @new WHERE last_totp_window < @new
					//      This ensures that if two writes race, only the later window value wins
					//      and replays of the same window are rejected on the next attempt.
					if (accountResult.Data.TotpVerifiedAt == null)
					{
						// First TOTP verification ever — marks 2FA as fully activated.
						await accountService.PersistTotpVerifiedAtAsync(pendingState.Username, windowUsed);
					}
					else
					{
						await accountService.PersistLastTotpWindowAsync(pendingState.Username, windowUsed);
					}
				}
				finally
				{
					if (plaintextSecret != null)
						CryptographicOperations.ZeroMemory(plaintextSecret);
				}
			}

			// TOTP verified — advance auth state on the worker thread BEFORE
			// encrypting or enqueueing. This prevents two concurrent TOTP verify
			// messages from both reaching token issuance before either advances state.
			totpPendingStates.TryRemove(conn.ClientId, out _);
			totpEnabledByClientId.TryRemove(conn.ClientId, out _);

			if (!Server.AccountManager.TryAdvanceAuthState(conn, AuthState.SrpSuccess, AuthState.Authenticated))
			{
				// Another path (parallel verify, sweep, or disconnect) already
				// transitioned or purged this connection — nothing to do.
				// NOTE: This warning is expected under packet reordering: if two
				// TOTP verify messages arrive close together, the first succeeds
				// and the second hits this path. Not a security issue.
				await Log.Warning(LogPrefix, $"TOTP advance to Authenticated failed for client {conn.ClientId} — duplicate or purged.");
				return;
			}

			string serverProof = pendingState.ServerProof;
			string username = pendingState.Username;
			AccessLevel accessLevel = pendingState.AccessLevel;
			ClientAuthenticationResult loginResult = ClientAuthenticationResult.LoginSuccess;

			// Refresh TTL before final work.
			RefreshAuthTtl(conn);

			// Encrypt server proof.
			byte[] encryptedServerProof = SrpService.EncryptServerProof(serverProof, pendingState.EncryptionData);

			// Generate and encrypt auth token.
			// Guard: skip if the connection dropped during TOTP verification to avoid
			// orphaned DB token records (same rationale as the SRP proof path).
			byte[] encryptedToken = conn.IsActive
				? await GenerateEncryptedAuthTokenAsync(pendingState.EncryptionData, username, accessLevel)
				: null;

			// Marshal login success to main thread.
			EnqueueMainThread(() =>
			{
				if (conn.IsActive)
				{
					SrpSuccessBroadcast resultMsg = new SrpSuccessBroadcast()
					{
						Proof = encryptedServerProof,
						Result = loginResult,
						Token = encryptedToken,
					};
					NetworkManager.ServerManager.Broadcast(conn, resultMsg, false, Channel.Reliable);
				}

				OnAuthentication(conn, true);
				InvokeClientAuthenticationResult(conn, true);

				// State already advanced to Authenticated on the worker thread.
				// Clear SRP material now that login is complete.
				srpAccountManager.ClearSrpState(conn);
			});
		}

		/// <summary>
		/// Tracks a TOTP failure for the given username in the per-username rate limiter.
		/// </summary>
		private void TrackTotpUsernameFailure(string username)
		{
			string failKey = username?.ToLowerInvariant();
			if (failKey != null)
			{
				// Hard cap: reject new entries when the tracker is full to prevent
				// unbounded memory growth from unique-username flooding.
				if (!totpUsernameFailures.ContainsKey(failKey) &&
					totpUsernameFailures.Count >= MaxTotpUsernameFailureEntries)
					return;

				DateTime now = DateTime.UtcNow;
				totpUsernameFailures.AddOrUpdate(
					failKey,
					_ => (1, now),
					(_, existing) => (Math.Min(existing.Count + 1, MaxTotpFailuresPerUsername + 1), existing.FirstFailure));
			}
		}

		/// <summary>
		/// Cleans up TOTP pending state when a connection is purged.
		/// </summary>
		private void CleanupTotpPendingState(NetworkConnection conn)
		{
			if (conn != null)
			{
				totpPendingStates.TryRemove(conn.ClientId, out _);
				totpEnabledByClientId.TryRemove(conn.ClientId, out _);
			}
		}

		#endregion

		// NOTE: TryLoginAsync is intentionally NOT overridden here.
		// The base implementation (passthrough) is correct for the LoginServer SRP flow,
		// which always passes LoginSuccess. Subclasses (WorldServerAuthenticator,
		// SceneServerAuthenticator) override TryLoginAsync for server-type-specific checks.

		/// <summary>
		/// Clears the connection IP cache during connection purge.
		/// </summary>
		/// <param name="conn">The connection being purged.</param>
		protected override void OnPurgeConnectionState(NetworkConnection conn)
		{
			if (conn != null)
			{
				connectionIpCache.Remove(conn.ClientId);
				totpEnabledByClientId.TryRemove(conn.ClientId, out _);
				CleanupTotpPendingState(conn);
			}
		}

		/// <summary>
		/// Resolves and caches a connection IP for auth rate limiting.
		/// </summary>
		/// <param name="conn">Connection to resolve.</param>
		/// <returns>Canonical IP key or empty string when unavailable.</returns>
		private string ResolveIpAddress(NetworkConnection conn)
		{
			if (conn == null)
			{
				return string.Empty;
			}

			if (connectionIpCache.TryGetAndTouch(conn.ClientId, DateTime.UtcNow, out string cachedIp) && !string.IsNullOrWhiteSpace(cachedIp))
			{
				return cachedIp;
			}

			// Normalize to canonical form (collapses IPv4-mapped IPv6) to ensure
			// consistent rate-limit and debounce identity with the base handshake layer.
			string ip = HandshakeService.NormalizeIp(conn.GetAddress());
			if (string.IsNullOrWhiteSpace(ip))
			{
				return string.Empty;
			}

			connectionIpCache.Upsert(conn.ClientId, ip, DateTime.UtcNow);
			return ip;
		}

		/// <summary>
		/// Sweeps stale unauthenticated account/encryption state held by AccountManager.
		/// This is a backstop for SRP memory cleanup in case network disconnect events are delayed.
		/// </summary>
		private void SweepStaleUnauthenticatedAccountState()
		{
			if (srpAccountManager == null)
			{
				return;
			}

			srpAccountManager.SweepUnauthenticatedConnections(
				TimeSpan.FromSeconds(AuthStaleTtlSeconds),
				connection => connection != null && connection.IsAuthenticated,
				AccountManagerSweepMaxScan,
				AccountManagerSweepMaxRemovals);
		}

		/// <summary>
		/// Attempts to begin a per-account kick request within debounce limits.
		/// </summary>
		/// <param name="accountName">Account name to debounce.</param>
		/// <returns><c>true</c> if a kick request may be persisted; otherwise <c>false</c>.</returns>
		private bool TryBeginKickRequest(string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return false;
			}

			return kickRequestNextAllowedUtcByAccount.TryBegin(
				accountName,
				DateTime.UtcNow,
				TimeSpan.FromSeconds(KickRequestDebounceSeconds));
		}

		/// <summary>
		/// Attempts to begin an IP-scoped authentication attempt within debounce limits.
		/// </summary>
		/// <param name="ipAddress">IP address key.</param>
		/// <returns><c>true</c> if allowed now; otherwise <c>false</c>.</returns>
		private bool TryBeginIpAuthAttempt(string ipAddress)
		{
			if (string.IsNullOrWhiteSpace(ipAddress))
			{
				return true;
			}

			return ipAuthNextAllowedUtc.TryBegin(
				ipAddress,
				DateTime.UtcNow,
				TimeSpan.FromSeconds(IpAuthAttemptDebounceSeconds));
		}

		/// <summary>
		/// Attempts to begin an account-scoped SRP verify attempt within debounce limits.
		/// </summary>
		/// <param name="accountName">Account name key.</param>
		/// <returns><c>true</c> if allowed now; otherwise <c>false</c>.</returns>
		private bool TryBeginAccountVerifyAttempt(string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return false;
			}

			// Hard cap: reject new entries when the debounce tracker is full to prevent
			// unbounded memory growth from unique-account-name flooding.
			if (accountVerifyNextAllowedUtc.Count >= MaxAccountVerifyDebounceEntries)
			{
				return false;
			}

			// Normalize to prevent case-variant bypass of the debounce window.
			return accountVerifyNextAllowedUtc.TryBegin(
				accountName.ToLowerInvariant(),
				DateTime.UtcNow,
				TimeSpan.FromSeconds(AccountVerifyDebounceSeconds));
		}

		/// <summary>
		/// Builds an auth token, persists its hash for revocation, and encrypts it with the session key.
		/// Returns <c>null</c> if the signing key is unavailable or if token generation fails (non-fatal).
		/// </summary>
		private async Task<byte[]> GenerateEncryptedAuthTokenAsync(ConnectionEncryptionData encryptionData, string username, AccessLevel accessLevel)
		{
			byte[] signingKeySnapshot = TokenSigningKey;
			if (signingKeySnapshot == null || signingKeySnapshot.Length < CryptoHelper.HmacKeyLength)
				return null;

			try
			{
				byte[] encryptedToken = TokenService.GenerateAndEncryptToken(
					encryptionData, username, LoginServerId, (int)tokenExpirationMinutes,
					signingKeySnapshot, accessLevel, out byte[] rawToken);

				if (encryptedToken == null)
					return null;

				try
				{
					string tokenHash = TokenService.HashToken(rawToken);
					if (Server.Database?.ServiceRegistry != null &&
						Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var authTokenService))
					{
						await authTokenService.IssueAsync(tokenHash, username, LoginServerId, DateTime.UtcNow.AddMinutes(tokenExpirationMinutes));
					}
				}
				finally
				{
					CryptographicOperations.ZeroMemory(rawToken);
				}

				return encryptedToken;
			}
			catch (Exception tokenEx)
			{
				await Log.Warning(LogPrefix, $"Token generation failed (non-fatal): {tokenEx.Message}");
				return null;
			}
		}

		/// <summary>
		/// Removes expired auth rate-limit entries for IP and account debounce maps.
		/// </summary>
		private void CleanupAuthRateLimitEntries()
		{
			DateTime now = DateTime.UtcNow;

			ipAuthNextAllowedUtc.SweepExpired(
				now,
				AuthRateLimitCleanupMaxScanPerMap,
				AuthRateLimitCleanupMaxRemovePerMap);

			accountVerifyNextAllowedUtc.SweepExpired(
				now,
				AuthRateLimitCleanupMaxScanPerMap,
				AuthRateLimitCleanupMaxRemovePerMap);

			connectionIpCache.SweepExpired(
				now,
				TimeSpan.FromSeconds(ConnectionIpCacheTtlSeconds),
				AuthRateLimitCleanupMaxScanPerMap,
				AuthRateLimitCleanupMaxRemovePerMap);
		}

		/// <summary>
		/// Removes expired per-account kick-request debounce entries.
		/// </summary>
		private void CleanupKickRequestDebounceEntries()
		{
			kickRequestNextAllowedUtcByAccount.SweepExpired(
				DateTime.UtcNow,
				AuthRateLimitCleanupMaxScanPerMap,
				AuthRateLimitCleanupMaxRemovePerMap);
		}
	}
}