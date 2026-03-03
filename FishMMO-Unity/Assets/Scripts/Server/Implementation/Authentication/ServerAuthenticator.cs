using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Authentication;
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
		/// </summary>
		private const float ConnectionIpCacheTtlSeconds = 120f;

		/// <summary>
		/// Pre-computed fake SRP salt/verifier for non-existent accounts.
		/// Avoids per-request SRP math during credential-stuffing attacks.
		/// The salt is AES-GCM encrypted before transmission, so reuse is unobservable on the wire.
		/// The SRP server session (modular exponentiation) still dominates timing,
		/// preserving indistinguishability from real accounts.
		/// </summary>
		/// <remarks>
		/// <para><b>Verifier correlation:</b> The verifier is static across all fake accounts.
		/// Because SRP server ephemeral <c>B = kv + g^b</c> includes a random <c>b</c>, different
		/// sessions produce different <c>B</c> values even with the same verifier. The static <c>v</c>
		/// component is masked by the random term, making correlation impractical without solving DLP.</para>
		/// <para><b>Salt uniqueness:</b> Per-username salts are derived via <see cref="DerivePerUsernameFakeSalt"/>
		/// using HMAC-SHA256, so each non-existent username receives a distinct salt.</para>
		/// </remarks>
		private static readonly Lazy<(string Salt, string Verifier)> FakeSrpTuple =
			new Lazy<(string, string)>(() =>
			{
				var client = new SecureRemotePassword.SrpClient(
					SecureRemotePassword.SrpParameters.Create2048<System.Security.Cryptography.SHA512>());
				string salt = client.GenerateSalt();
				string priv = client.DerivePrivateKey(salt, "fake_user", "fake_password");
				string verifier = client.DeriveVerifier(priv);
				return (salt, verifier);
			});

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
			_ = FakeSrpTuple.Value;

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

			Log.Debug("ServerAuthenticator", $"Workers initialized (Verify={VerifyWorkerCount}, Proof={ProofWorkerCount})");
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

			kickRequestNextAllowedUtcByAccount.Clear();
			ipAuthNextAllowedUtc.Clear();
			accountVerifyNextAllowedUtc.Clear();
			connectionIpCache.Clear();
		}

		/// <summary>
		/// Sweeps stale unauthenticated SRP/encryption state at the auth sweep interval.
		/// </summary>
		protected override void OnAuthSweep()
		{
			SweepStaleUnauthenticatedAccountState();
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
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Reliable);
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
				conn.Disconnect(true);
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
				}, false, Channel.Reliable);
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
			if (!Server.AccountManager.TryAdvanceAuthState(conn, AuthState.WaitingForProof, AuthState.ProofPending) &&
				!Server.AccountManager.TryAdvanceAuthState(conn, AuthState.VerifyPending, AuthState.ProofPending))
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
				conn.Disconnect(true);
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
				// DropWrite self-healing: roll back to WaitingForProof so the client can
				// re-submit proof. TTL sweep is the ultimate safety net.
				Server.AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, AuthState.WaitingForProof);
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Reliable);
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
			await Log.Debug("ServerAuthenticator", $"Verify worker {workerId} started");
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
						await Log.Error("ServerAuthenticator", $"Verify worker {workerId} error: {ex}");
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				await Log.Error("ServerAuthenticator", $"Verify worker {workerId} unexpected error: {ex}");
			}

			await Log.Debug("ServerAuthenticator", $"Verify worker {workerId} stopped");
		}

		/// <summary>
		/// Async worker that processes SRP proof requests from the bounded channel.
		/// Performs AES decryption, SRP proof validation, and login finalization.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
		/// <param name="workerId">Worker ID for logging.</param>
		private async Task ProcessSrpProofRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug("ServerAuthenticator", $"Proof worker {workerId} started");
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
						await Log.Error("ServerAuthenticator", $"Proof worker {workerId} error: {ex}");
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				await Log.Error("ServerAuthenticator", $"Proof worker {workerId} unexpected error: {ex}");
			}

			await Log.Debug("ServerAuthenticator", $"Proof worker {workerId} stopped");
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

			// Decrypt the username and public ephemeral on worker thread using explicit sequence numbers.
			string username;
			uint seq = request.Seq;

			// Guard: seq == 0 prevents uint underflow in the seq - 1 computation below
			// (uint 0 - 1 wraps to uint.MaxValue, producing an invalid sequence).
			if (seq == 0)
			{
				await Log.Warning("ServerAuthenticator", "Invalid SRP verify sequence 0 received.");
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			try
			{
				// ┌──────────────────────────────────────────────────────────────┐
				// │ PROTOCOL RULE — SRP Verify two-sequence encoding:            │
				// │   seq-1 : AES-GCM encrypted username                         │
				// │   seq   : AES-GCM encrypted public ephemeral                 │
				// │ Both sequences are consumed and nonce-bound independently.    │
				// │ DO NOT reorder, collapse, or reuse — replay safety depends    │
				// │ on each field having a unique (nonce, AAD) pair.              │
				// └──────────────────────────────────────────────────────────────┘
				uint seqUsername = seq - 1;
				if (!request.EncryptionData.TryConsumeReceiveSequence(seqUsername))
				{
					await Log.Warning("ServerAuthenticator", "SRP verify username sequence out-of-order or duplicate.");
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}

				byte[] nonce1 = request.EncryptionData.BuildReceiveNonce(seqUsername);
				byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerify, request.EncryptionData.AgreedVersion, seqUsername);
				byte[] decryptedRawUsername = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonce1, request.EncryptedUsername, aad1);
				try
				{
					username = CryptoHelper.StrictUtf8.GetString(decryptedRawUsername);
				}
				catch (DecoderFallbackException)
				{
					CryptographicOperations.ZeroMemory(decryptedRawUsername);
					throw new CryptographicException("Malformed UTF-8 in decrypted username.");
				}
				CryptographicOperations.ZeroMemory(decryptedRawUsername);

				// Consume sequence for the public ephemeral and decrypt it below when needed.
				if (!request.EncryptionData.TryConsumeReceiveSequence(seq))
				{
					await Log.Warning("ServerAuthenticator", "SRP verify public ephemeral sequence out-of-order or duplicate.");
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}
			}
			catch (CryptographicException)
			{
				await Log.Warning("ServerAuthenticator", "AES decryption/authentication failed for SRP verify.");
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			// Reject oversized or empty usernames to prevent heavy DB lookups and dictionary churn.
			// The encrypted payload is already bounded by MaxSrpPayloadBytes, but post-decrypt
			// validation is a cheap safety net against encoding edge cases.
			// Username is used as-is from client decryption. Database collation determines
			// whether lookups are case-sensitive. If case-insensitive authentication is desired,
			// normalize here (e.g., username = username.ToLowerInvariant()) and ensure the
			// database stores usernames in the same canonical form.
			if (!Authentication.IsAllowedUsername(username))
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
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

					// Decrypt the public ephemeral value (consumer sequence already advanced above).
					string publicEphemeral;
					try
					{
						byte[] nonce2 = request.EncryptionData.BuildReceiveNonce(request.Seq);
						byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerify, request.EncryptionData.AgreedVersion, request.Seq);
						byte[] decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonce2, request.EncryptedPublicEphemeral, aad2);
						try
						{
							publicEphemeral = CryptoHelper.StrictUtf8.GetString(decryptedRawPublicEphemeral);
						}
						catch (DecoderFallbackException)
						{
							CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
							throw new CryptographicException("Malformed UTF-8 in decrypted public ephemeral.");
						}
						CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
					}
					catch (CryptographicException)
					{
						await Log.Warning("ServerAuthenticator", "AES decryption/authentication failed for public ephemeral.");
						RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
						return;
					}

					// Fetch account for login from database.
					DatabaseResult<Database.Data.AccountData> loginResult = await accountService.FetchForLoginAsync(username);

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
						salt = DerivePerUsernameFakeSalt(username);
						verifier = FakeSrpTuple.Value.Verifier;
						accessLevel = AccessLevel.Player;
						await Log.Debug("ServerAuthenticator", $"Using pre-computed fake SRP state for non-existent account '{username}' to avoid enumeration.");
					}
					else
					{
						Database.Data.AccountData accountData = loginResult.Data;
						salt = accountData.Salt;
						verifier = accountData.Verifier;
						accessLevel = (AccessLevel)accountData.AccessLevel;
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
							// Acquire explicit send sequence and build nonce + AAD for each field
							uint sendSeq1 = request.EncryptionData.NextSendSequence();
							byte[] sendNonce1 = request.EncryptionData.BuildSendNonce(sendSeq1);
							byte[] aadSend1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, request.EncryptionData.AgreedVersion, sendSeq1);
							encryptedSalt = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, sendNonce1, Encoding.UTF8.GetBytes(srpSalt), aadSend1);

							uint sendSeq2 = request.EncryptionData.NextSendSequence();
							byte[] sendNonce2 = request.EncryptionData.BuildSendNonce(sendSeq2);
							byte[] aadSend2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpVerifyResponse, request.EncryptionData.AgreedVersion, sendSeq2);
							encryptedPublicServerEphemeral = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, sendNonce2, Encoding.UTF8.GetBytes(srpPublicServerEphemeral), aadSend2);
						}
						catch (CryptographicException ex)
						{
							await Log.Error("ServerAuthenticator", $"AES encryption failed for SRP response: {ex.Message}");
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
					await Log.Error("ServerAuthenticator", $"Error during SRP verify: {ex}");
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

			// Decrypt client proof on worker thread.
			// Note: clientProof is a managed string and cannot be deterministically zeroed.
			// See "Design note — String zeroization" above.
			string clientProof;
			try
			{
				// Validate and consume explicit client->server sequence before decrypting.
				if (!request.EncryptionData.TryConsumeReceiveSequence(request.Seq))
				{
					await Log.Warning("ServerAuthenticator", "SRP proof sequence out-of-order or duplicate.");
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}

				byte[] nonce = request.EncryptionData.BuildReceiveNonce(request.Seq);
				byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpProof, request.EncryptionData.AgreedVersion, request.Seq);
				byte[] decryptedClientProof = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonce, request.EncryptedClientProof, aad);
				try
				{
					clientProof = CryptoHelper.StrictUtf8.GetString(decryptedClientProof);
				}
				catch (DecoderFallbackException)
				{
					CryptographicOperations.ZeroMemory(decryptedClientProof);
					throw new CryptographicException("Malformed UTF-8 in decrypted client proof.");
				}
				CryptographicOperations.ZeroMemory(decryptedClientProof);
			}
			catch (CryptographicException)
			{
				await Log.Warning("ServerAuthenticator", "AES decryption/authentication failed for client proof.");
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			string serverProof = null;
			string username = null;

			// Atomically validate proof and advance auth state: ProofPending → SrpSuccess.
			bool proofValid = Server.AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, AuthState.SrpSuccess, (a) =>
			{
				if (a.SrpData != null && a.SrpData.GetProof(clientProof, out string proof))
				{
					serverProof = proof;
					username = a.SrpData.UserName;
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
				// CAVEAT: The online flag lives on CharacterData. If the server crashes
				// without cleanly logging characters out, stale Online=true flags may
				// persist. Crash recovery should reset all Online flags on startup.
				//
				// PERFORMANCE NOTE: FetchManyAsync loads all characters for the account.
				// For accounts with many characters this is a per-auth DoS vector.
				// Consider adding a database-level "any online" flag on the account row
				// or a stored procedure that short-circuits on the first Online=true.
				bool isOnline = false;
				if (Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					DatabaseResult<IReadOnlyList<CharacterData>> charactersResult = await characterService.FetchManyAsync(username);
					if (charactersResult.IsSuccess && charactersResult.Data != null)
					{
						foreach (CharacterData c in charactersResult.Data)
						{
							if (c.Online)
							{
								isOnline = true;
								break;
							}
						}
					}
				}

				if (isOnline)
				{
					// Persist kick request for the online character (rate-limited per account).
					if (TryBeginKickRequest(username) &&
						Server.Database?.ServiceRegistry != null &&
						Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var kickRequestService))
					{
						await kickRequestService.PersistAsync(username);
					}
				}

				// Attempt to complete login authentication (virtual — overridden by WorldServer/SceneServer).
				// Skip login for already-online accounts — they receive AlreadyOnline + kick request instead.
				ClientAuthenticationResult result = isOnline
					? ClientAuthenticationResult.AlreadyOnline
					: await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username);

				// Refresh TTL after potentially slow database/login checks.
				RefreshAuthTtl(conn);

				// Inclusion list: only LoginSuccess is authenticated. All other results
				// (including future new codes) default to unauthenticated.
				bool authenticated = result == ClientAuthenticationResult.LoginSuccess;

				// NOTE: Both server proof and token use SrpSuccess AAD type discriminator.
				// This is safe because each has a unique sequence number → unique (nonce, AAD) pair.
				// Encrypt server proof on worker thread.
				uint sendSeq = request.EncryptionData.NextSendSequence();
				byte[] sendNonce = request.EncryptionData.BuildSendNonce(sendSeq);
				byte[] aadSend = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, request.EncryptionData.AgreedVersion, sendSeq);
				byte[] encryptedServerProof = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, sendNonce, Encoding.UTF8.GetBytes(serverProof), aadSend);

				// Generate and encrypt auth token if signing key is available.
				// Note: encryptedToken is ciphertext — not sensitive even if the lambda
				// is held alive during main-thread queue backlog.
				byte[] encryptedToken = null;
				// Snapshot TokenSigningKey to prevent a race where ShutdownWorkers or key
				// rotation zeroes the array between the null check and BuildAuthToken.
				byte[] signingKeySnapshot = TokenSigningKey;
				if (authenticated && signingKeySnapshot != null && signingKeySnapshot.Length >= CryptoHelper.HmacKeyLength)
				{
					try
					{
						DateTime expiresUtc = DateTime.UtcNow.AddMinutes(tokenExpirationMinutes);
						byte[] rawToken = CryptoHelper.BuildAuthToken(username, LoginServerId, expiresUtc, signingKeySnapshot);

						// Persist token hash to DB for revocation support.
						string tokenHash = CryptoHelper.HashTokenHex(rawToken);
						if (Server.Database?.ServiceRegistry != null &&
							Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var authTokenService))
						{
							await authTokenService.IssueAsync(tokenHash, username, LoginServerId, expiresUtc);
						}

						// Encrypt token with session key.
						uint tokenSeq = request.EncryptionData.NextSendSequence();
						byte[] tokenNonce = request.EncryptionData.BuildSendNonce(tokenSeq);
						byte[] tokenAad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.SrpSuccess, request.EncryptionData.AgreedVersion, tokenSeq);
						encryptedToken = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, tokenNonce, rawToken, tokenAad);

						CryptographicOperations.ZeroMemory(rawToken);
					}
					catch (Exception tokenEx)
					{
						await Log.Warning("ServerAuthenticator", $"Token generation failed (non-fatal): {tokenEx.Message}");
						// Token issuance failure is non-fatal — auth still succeeds.
						encryptedToken = null;
					}
				}

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
						Server.AccountManager.TryAdvanceAuthState(conn, AuthState.SrpSuccess, AuthState.Authenticated);
						srpAccountManager.ClearSrpState(conn);
					}
					else
					{
						// On failed authentication, remove all connection/account mappings and encryption state.
						Server.AccountManager.RemoveConnectionAccount(conn);
					}

					ClearTransientAuthState(clientId);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("ServerAuthenticator", $"Error during SRP proof login: {ex}");
				EnqueueMainThread(() =>
				{
					conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		#endregion

		/// <summary>
		/// Returns <see cref="ClientAuthenticationResult.LoginSuccess"/> for the SRP login flow.
		/// Subclasses (WorldServer/SceneServer) override for additional server-type-specific checks.
		/// </summary>
		internal override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			return Task.FromResult(ClientAuthenticationResult.LoginSuccess);
		}

		/// <summary>
		/// Derives a deterministic per-username fake SRP salt via HMAC-SHA512
		/// so that each non-existent username receives a unique but repeatable salt.
		/// This prevents attackers from detecting salt reuse across different fake accounts
		/// if AES-GCM encryption were somehow compromised.
		/// </summary>
		/// <remarks>
		/// <para><b>Key snapshot:</b> A local reference to <see cref="fakeSaltKey"/> is captured
		/// to prevent a race with <see cref="ShutdownWorkersCore"/> zeroing the array mid-HMAC.</para>
		/// <para><b>Format note:</b> The output is a 128-character lowercase hex string derived from
		/// HMAC-SHA512, matching the length of real SRP salts produced by the SRP library with
		/// SHA-512 parameters. This prevents ciphertext-size oracles from leaking account existence.</para>
		/// </remarks>
		/// <param name="username">The username to derive a fake salt for.</param>
		/// <returns>Hex-encoded fake salt string, or the static fake salt if the key was already zeroed.</returns>
		private string DerivePerUsernameFakeSalt(string username)
		{
			byte[] keySnapshot = fakeSaltKey;
			if (keySnapshot == null || keySnapshot.Length < CryptoHelper.HmacSha512KeyLength)
			{
				// Key already zeroed during shutdown — fall back to static fake salt.
				return FakeSrpTuple.Value.Salt;
			}
			using (var hmac = new HMACSHA512(keySnapshot))
			{
				byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(username));
				return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
			}
		}

		/// <summary>
		/// Clears the connection IP cache during connection purge.
		/// </summary>
		/// <param name="conn">The connection being purged.</param>
		protected override void OnPurgeConnectionState(NetworkConnection conn)
		{
			if (conn != null)
			{
				connectionIpCache.Remove(conn.ClientId);
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
			string ip = NormalizeIp(conn.GetAddress());
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

			return accountVerifyNextAllowedUtc.TryBegin(
				accountName,
				DateTime.UtcNow,
				TimeSpan.FromSeconds(AccountVerifyDebounceSeconds));
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