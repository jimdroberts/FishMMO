using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Core.Collections;
using FishMMO.Logging;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Engine-independent SRP-6a authenticator core for LoginServer use.
	/// Extends <see cref="BaseAuthenticatorCore{TConnection}"/> with bounded-channel SRP
	/// verify/proof workers, TOTP two-factor authentication, kick-request tracking, and
	/// per-IP/per-account rate limiting — with no dependency on Unity, FishNet, or any
	/// game-engine type.
	/// <para>
	/// Subclasses provide transport-specific callbacks for broadcasting auth results,
	/// disconnecting connections, resolving IP addresses, and performing database operations.
	/// </para>
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public abstract class SrpAuthenticatorCore<TConnection> : BaseAuthenticatorCore<TConnection>
	{
		#region Constants

		/// <summary>Number of concurrent SRP verify worker tasks.</summary>
		private const int VerifyWorkerCount = 2;
		/// <summary>Number of concurrent SRP proof worker tasks.</summary>
		private const int ProofWorkerCount = 2;
		/// <summary>Maximum pending SRP verify requests in the bounded channel before new writes are dropped.</summary>
		private const int VerifyChannelCapacity = 500;
		/// <summary>Maximum pending SRP proof requests in the bounded channel before new writes are dropped.</summary>
		private const int ProofChannelCapacity = 500;

		/// <summary>Maximum unauthenticated connection entries scanned per account manager sweep.</summary>
		private const int AccountManagerSweepMaxScan = 256;
		/// <summary>Maximum unauthenticated connection entries removed per account manager sweep.</summary>
		private const int AccountManagerSweepMaxRemovals = 64;

		/// <summary>Minimum seconds between kick-request submissions for the same account.</summary>
		private const float KickRequestDebounceSeconds = 10f;
		/// <summary>Minimum seconds between SRP auth attempts from the same IP address.</summary>
		private const float IpAuthAttemptDebounceSeconds = 1f;
		/// <summary>Minimum seconds between account-verify debounce entries for the same identifier.
		/// Override to 0 in test subclasses to disable per-username rate limiting between sessions.</summary>
		protected virtual float AccountVerifyDebounceSeconds => 2f;
		/// <summary>Maximum entries in the per-account verify debounce tracker before new entries are rejected.</summary>
		private const int MaxAccountVerifyDebounceEntries = 50_000;

		/// <summary>Maximum entries scanned per rate-limit map cleanup pass.</summary>
		private const int AuthRateLimitCleanupMaxScanPerMap = 256;
		/// <summary>Maximum entries removed per rate-limit map cleanup pass.</summary>
		private const int AuthRateLimitCleanupMaxRemovePerMap = 128;

		/// <summary>Seconds before a cached connection-IP mapping expires from the last-seen cache.</summary>
		private const float ConnectionIpCacheTtlSeconds = 300f;

		/// <summary>Maximum number of TOTP code verifications that may run concurrently.</summary>
		private const int MaxConcurrentTotpVerifications = 4;
		/// <summary>Maximum total TOTP attempts allowed per pending state before the connection is dropped.</summary>
		private const int MaxTotpAttempts = 5;
		/// <summary>Maximum TOTP failures tracked per username before that username is locked out.</summary>
		private const int MaxTotpFailuresPerUsername = 15;
		/// <summary>Duration of the per-username TOTP lockout after <see cref="MaxTotpFailuresPerUsername"/> failures.</summary>
		private static readonly TimeSpan TotpUsernameLockoutDuration = TimeSpan.FromMinutes(5);
		/// <summary>Maximum entries scanned per TOTP username-failure sweep pass.</summary>
		private const int TotpUsernameFailureSweepMaxScan = 64;
		/// <summary>Maximum entries retained in the TOTP username-failure tracker.</summary>
		private const int MaxTotpUsernameFailureEntries = 10_000;

		#endregion

		#region Fields

		/// <summary>Bounded channel for SRP verify requests; decouples network receipt from async processing.</summary>
		private Channel<SrpVerifyRequest<TConnection>>? verifyChannel;
		/// <summary>Bounded channel for SRP proof requests; decouples network receipt from async processing.</summary>
		private Channel<SrpProofRequest<TConnection>>? proofChannel;

		/// <summary>Debounce tracker preventing duplicate or rapid kick requests for the same account.</summary>
		private readonly ExpiringKeyTracker<string> kickRequestNextAllowedUtcByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Per-IP rate limiter applied at the SRP verify gate to cap authentication attempts.</summary>
		private readonly ExpiringKeyTracker<string> ipAuthNextAllowedUtc =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Per-identifier debounce tracker for account existence/verify lookups.</summary>
		private readonly ExpiringKeyTracker<string> accountVerifyNextAllowedUtc =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Short-lived cache mapping client ID to resolved IP string, avoiding repeated reverse-DNS/normalization calls.</summary>
		private readonly LastSeenCacheTracker<int, string> connectionIpCache = new LastSeenCacheTracker<int, string>();

		/// <summary>SRP-specific account manager for SRP session state storage and retrieval.</summary>
		private ISrpAccountManager<TConnection> srpAccountManager;

		/// <summary>HKDF-derived per-session key used to generate timing-consistent fake SRP salts for non-existent accounts.</summary>
		private byte[]? fakeSaltKey;

		/// <summary>HMAC-SHA256 signing key for auth token generation. Volatile for cross-thread visibility.</summary>
		private volatile byte[]? tokenSigningKey;
		/// <summary>AES-256 master key for decrypting TOTP secrets stored in the database. Volatile for cross-thread visibility.</summary>
		private volatile byte[]? totpMasterKey;

		/// <summary>Per-client-ID TOTP pending state for connections awaiting two-factor verification.</summary>
		private readonly ConcurrentDictionary<int, TotpPendingState> totpPendingStates = new ConcurrentDictionary<int, TotpPendingState>();
		/// <summary>Semaphore limiting the number of concurrent TOTP code verifications to <see cref="MaxConcurrentTotpVerifications"/>.</summary>
		private SemaphoreSlim? totpSemaphore;

		/// <summary>Tracks whether TOTP is enabled for each active connection (by client ID), populated during SRP verify.</summary>
		private readonly ConcurrentDictionary<int, bool> totpEnabledByClientId = new ConcurrentDictionary<int, bool>();
		private readonly ConcurrentDictionary<int, DateTime?> verifyCodeExpiryByClientId = new ConcurrentDictionary<int, DateTime?>();

		/// <summary>Tracks per-username TOTP failure counts and first-failure timestamps for lockout enforcement.</summary>
		private readonly ConcurrentDictionary<string, (int Count, DateTime FirstFailure)> totpUsernameFailures
			= new ConcurrentDictionary<string, (int Count, DateTime FirstFailure)>(StringComparer.OrdinalIgnoreCase);

		#endregion

		#region Properties

		/// <summary>
		/// HMAC signing key for token generation. Set by the server system on startup.
		/// If null, token issuance is disabled.
		/// </summary>
		public byte[]? TokenSigningKey
		{
			get => tokenSigningKey;
			set => tokenSigningKey = value;
		}

		/// <summary>
		/// AES-256 master key for decrypting TOTP secrets from the database during login.
		/// Must match the key used by AccountCreationSystem.
		/// </summary>
		public byte[]? TotpMasterKey
		{
			get => totpMasterKey;
			set => totpMasterKey = value;
		}

		/// <summary>
		/// LoginServer database ID, embedded in issued tokens.
		/// </summary>
		public long LoginServerId { get; set; }

		/// <summary>
		/// Database ID of the HMAC signing key used for token generation.
		/// </summary>
		public long TokenSigningKeyId { get; set; }

		/// <summary>
		/// Token validity duration in minutes.
		/// </summary>
		public float TokenExpirationMinutes { get; set; } = 10f;

		#endregion

		#region Constructor

		/// <summary>
		/// Initializes the SRP authenticator core.
		/// </summary>
		/// <param name="accountManager">SRP account manager instance.</param>
		protected SrpAuthenticatorCore(ISrpAccountManager<TConnection> accountManager)
			: base(accountManager)
		{
			srpAccountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
		}

		#endregion

		#region Worker Lifecycle

		/// <inheritdoc/>
		protected override void InitializeWorkersCore(CancellationToken cancellationToken)
		{
			fakeSaltKey = CryptoHelper.GenerateKey(CryptoHelper.HmacSha512KeyLength);

			// Pre-warm fake SRP state to prevent first-use timing side-channel.
			_ = SrpService.GetStaticFakeData();

			var fakeData = SrpService.GetStaticFakeData();

			// The fake salt MUST be exactly the same length and character set
			// as a real SRP verifier salt (128-char lowercase hex). A mismatch on either count creates
			// a ciphertext-size or charset oracle that lets an unauthenticated probe distinguish
			// existing from non-existing accounts. Probe a representative set of usernames covering
			// extremes (very short, very long, single-character, all-digit) so a regression that
			// depends on input length or content surfaces at startup rather than via a covert
			// timing side-channel in production.
			string[] probeUsernames = new[]
			{
				"__startup_length_check__",
				"a",
				"ab",
				"abc",
				"x",
				"0",
				"longuser_name_123",
				"verylongusernameforprobetesting_padding_padding_padding"
			};

			int expectedLength = fakeData.Salt.Length;
			foreach (string probe in probeUsernames)
			{
				string derived = SrpService.DerivePerUsernameFakeSalt(probe, fakeSaltKey);
				if (derived == null || derived.Length != expectedLength)
				{
					string msg = $"Fake salt length mismatch \u2014 DerivePerUsernameFakeSalt(\"{probe}\") produced {derived?.Length ?? -1} chars, expected {expectedLength}. " +
						"This would create a ciphertext-size oracle leaking account existence.";
					_ = Log.Error(LogPrefix, msg);
					throw new InvalidOperationException(msg);
				}
				// Charset: lowercase hex digits only. Any other character (uppercase, punctuation,
				// whitespace) likewise leaks via the encoded response.
				for (int i = 0; i < derived.Length; i++)
				{
					char c = derived[i];
					bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
					if (!isHex)
					{
						string msg = $"Fake salt charset violation \u2014 DerivePerUsernameFakeSalt(\"{probe}\") produced non-lowercase-hex char '{c}' at index {i}.";
						_ = Log.Error(LogPrefix, msg);
						throw new InvalidOperationException(msg);
					}
				}
			}

			totpSemaphore = new SemaphoreSlim(MaxConcurrentTotpVerifications, MaxConcurrentTotpVerifications);

			verifyChannel = Channel.CreateBounded<SrpVerifyRequest<TConnection>>(new BoundedChannelOptions(VerifyChannelCapacity)
			{
				FullMode = BoundedChannelFullMode.DropWrite,
				SingleReader = false,
				SingleWriter = false
			});

			proofChannel = Channel.CreateBounded<SrpProofRequest<TConnection>>(new BoundedChannelOptions(ProofChannelCapacity)
			{
				FullMode = BoundedChannelFullMode.DropWrite,
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

			_ = Log.Debug(LogPrefix, $"SRP workers initialized (Verify={VerifyWorkerCount}, Proof={ProofWorkerCount})");
		}

		/// <inheritdoc/>
		protected override void ShutdownWorkersCore()
		{
			verifyChannel?.Writer.TryComplete();
			proofChannel?.Writer.TryComplete();
			verifyChannel = null;
			proofChannel = null;

			if (fakeSaltKey != null)
			{
				CryptographicOperations.ZeroMemory(fakeSaltKey);
				fakeSaltKey = null;
			}

			if (tokenSigningKey != null)
			{
				CryptographicOperations.ZeroMemory(tokenSigningKey);
				tokenSigningKey = null;
			}

			if (totpMasterKey != null)
			{
				CryptographicOperations.ZeroMemory(totpMasterKey);
				totpMasterKey = null;
			}

			totpPendingStates.Clear();
			totpEnabledByClientId.Clear();
			verifyCodeExpiryByClientId.Clear();
			totpUsernameFailures.Clear();
			totpSemaphore?.Dispose();
			totpSemaphore = null;
			kickRequestNextAllowedUtcByAccount.Clear();
			ipAuthNextAllowedUtc.Clear();
			accountVerifyNextAllowedUtc.Clear();
			connectionIpCache.Clear();
		}

		#endregion

		#region Periodic Sweeps

		/// <inheritdoc/>
		protected override void OnAuthSweep()
		{
			SweepStaleUnauthenticatedAccountState();
			SweepExpiredTotpUsernameFailures();
		}

		/// <summary>
		/// Invokes the SRP account manager sweep to purge stale unauthenticated connections.
		/// </summary>
		private void SweepStaleUnauthenticatedAccountState()
		{
			srpAccountManager.SweepUnauthenticatedConnections(
				TimeSpan.FromSeconds(AuthStaleTtlSeconds),
				IsConnectionAuthenticated,
				AccountManagerSweepMaxScan,
				AccountManagerSweepMaxRemovals);
		}

		/// <summary>
		/// Sweeps expired per-username TOTP failure entries to prevent unbounded dictionary growth.
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
		/// Sweeps the kick-request debounce and auth rate-limit trackers.
		/// Call from the hosting environment's per-tick update alongside <see cref="BaseAuthenticatorCore{TConnection}.Tick"/>.
		/// </summary>
		public void TickRateLimits()
		{
			DateTime now = DateTime.UtcNow;
			kickRequestNextAllowedUtcByAccount.SweepExpired(now, AuthRateLimitCleanupMaxScanPerMap, AuthRateLimitCleanupMaxRemovePerMap);
			ipAuthNextAllowedUtc.SweepExpired(now, AuthRateLimitCleanupMaxScanPerMap, AuthRateLimitCleanupMaxRemovePerMap);
			accountVerifyNextAllowedUtc.SweepExpired(now, AuthRateLimitCleanupMaxScanPerMap, AuthRateLimitCleanupMaxRemovePerMap);
			connectionIpCache.SweepExpired(now, TimeSpan.FromSeconds(ConnectionIpCacheTtlSeconds), AuthRateLimitCleanupMaxScanPerMap, AuthRateLimitCleanupMaxRemovePerMap);
		}

		#endregion

		#region Connection State

		/// <inheritdoc/>
		protected override void OnPurgeConnectionState(TConnection conn)
		{
			int clientId = GetConnectionClientId(conn);
			totpPendingStates.TryRemove(clientId, out _);
			totpEnabledByClientId.TryRemove(clientId, out _);
			verifyCodeExpiryByClientId.TryRemove(clientId, out _);
			connectionIpCache.Remove(clientId);
		}

		#endregion

		#region UDP Receiver Gates

		/// <summary>
		/// Gate for an incoming SRP verify request. Validates connection state, applies rate limits,
		/// and enqueues for async processing. No decryption or database work occurs here.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="encryptedUsername">Encrypted username bytes.</param>
		/// <param name="encryptedPublicEphemeral">Encrypted public ephemeral bytes.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		public void OnSrpVerifyReceived(TConnection conn, byte[] encryptedUsername, byte[] encryptedPublicEphemeral, uint seq)
		{
			if (IsConnectionAuthenticated(conn))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			string ipAddress = ResolveAndCacheIp(conn);
			if (!TryBeginIpAuthAttempt(ipAddress))
			{
				BroadcastAuthResult(conn, ClientAuthenticationResult.ServerBusy, reliable: false);
				return;
			}

			if (!AccountManager.TryAdvanceAuthState(conn, AuthState.Handshake, AuthState.VerifyPending))
				return;

			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (encryptedUsername == null || encryptedUsername.Length == 0 || encryptedUsername.Length > CryptoHelper.MaxSrpPayloadBytes ||
				encryptedPublicEphemeral == null || encryptedPublicEphemeral.Length == 0 || encryptedPublicEphemeral.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new SrpVerifyRequest<TConnection>(conn, encryptedUsername, encryptedPublicEphemeral, encryptionData.CloneForAsyncWorker(), seq);

			if (verifyChannel == null || !verifyChannel.Writer.TryWrite(request))
			{
				AccountManager.TryAdvanceAuthState(conn, AuthState.VerifyPending, AuthState.Handshake);
				BroadcastAuthResult(conn, ClientAuthenticationResult.ServerBusy, reliable: false);
			}
		}

		/// <summary>
		/// Gate for an incoming SRP proof request. Validates connection state and enqueues for async processing.
		/// No decryption or SRP math occurs here.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="encryptedProof">Encrypted proof bytes.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		public void OnSrpProofReceived(TConnection conn, byte[] encryptedProof, uint seq)
		{
			if (IsConnectionAuthenticated(conn))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			AuthState priorState;
			if (AccountManager.TryAdvanceAuthState(conn, AuthState.WaitingForProof, AuthState.ProofPending))
				priorState = AuthState.WaitingForProof;
			else if (AccountManager.TryAdvanceAuthState(conn, AuthState.VerifyPending, AuthState.ProofPending))
				priorState = AuthState.VerifyPending;
			else
				return;

			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (encryptedProof == null || encryptedProof.Length == 0 || encryptedProof.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new SrpProofRequest<TConnection>(conn, encryptedProof, encryptionData.CloneForAsyncWorker(), seq);

			if (proofChannel == null || !proofChannel.Writer.TryWrite(request))
			{
				AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, priorState);
				BroadcastAuthResult(conn, ClientAuthenticationResult.ServerBusy, reliable: false);
			}
		}

		/// <summary>
		/// Gate for an incoming TOTP verification code. Validates state, applies rate limits,
		/// and dispatches async processing.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="encryptedCode">Encrypted TOTP code bytes.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		public void OnTwoFactorVerifyReceived(TConnection conn, byte[] encryptedCode, uint seq)
		{
			if (conn == null || !IsConnectionActive(conn))
				return;

			int clientId = GetConnectionClientId(conn);

			if (!totpPendingStates.TryGetValue(clientId, out TotpPendingState pendingState))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (!ReferenceEquals(pendingState.Connection, conn))
			{
				totpPendingStates.TryRemove(clientId, out _);
				DisconnectConnection(conn, graceful: true);
				return;
			}

			RefreshAuthTtl(conn);

			if (encryptedCode == null || encryptedCode.Length > CryptoHelper.MaxSrpPayloadBytes)
			{
				totpPendingStates.TryRemove(clientId, out _);
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			// Per-username lockout check
			string? userKey = pendingState.Username?.ToLowerInvariant();
			if (userKey != null && totpUsernameFailures.TryGetValue(userKey, out var failInfo))
			{
				if (DateTime.UtcNow - failInfo.FirstFailure > TotpUsernameLockoutDuration)
				{
					totpUsernameFailures.TryRemove(userKey, out _);
				}
				else if (failInfo.Count >= MaxTotpFailuresPerUsername)
				{
					totpPendingStates.TryRemove(clientId, out _);
					EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: false));
					PurgeConnectionAuthState(conn, disconnect: false);
					return;
				}
			}

			var sem = totpSemaphore;
			if (sem == null)
			{
				totpPendingStates.TryRemove(clientId, out _);
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}
			if (!sem.Wait(0))
			{
				BroadcastAuthResult(conn, ClientAuthenticationResult.TwoFactorInvalid, reliable: true);
				return;
			}

			int attempts = Interlocked.Increment(ref pendingState.Attempts);
			if (attempts > MaxTotpAttempts)
			{
				sem.Release();
				totpPendingStates.TryRemove(clientId, out _);
				EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: false));
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await ProcessTwoFactorVerifyAsync(conn, encryptedCode, seq, pendingState);
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"TOTP verify error: {ex}");
					totpPendingStates.TryRemove(GetConnectionClientId(conn), out _);
					EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: false));
					PurgeConnectionAuthState(conn, disconnect: false);
				}
				finally
				{
					sem.Release();
				}
			});
		}

		#endregion

		#region Async Workers

		/// <summary>
		/// Long-running worker that reads <see cref="SrpVerifyRequest{TConnection}"/> items from
		/// <see cref="verifyChannel"/> and dispatches them to <see cref="ProcessSrpVerifyAsync"/>.
		/// </summary>
		/// <param name="cancellationToken">Token used to request graceful worker exit.</param>
		/// <param name="workerId">Human-readable worker index for log messages.</param>
		private async Task ProcessSrpVerifyRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"SRP Verify worker {workerId} started");
			try
			{
				await foreach (var request in verifyChannel!.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested) break;
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
			await Log.Debug(LogPrefix, $"SRP Verify worker {workerId} stopped");
		}

		/// <summary>
		/// Long-running worker that reads <see cref="SrpProofRequest{TConnection}"/> items from
		/// <see cref="proofChannel"/> and dispatches them to <see cref="ProcessSrpProofAsync"/>.
		/// </summary>
		/// <param name="cancellationToken">Token used to request graceful worker exit.</param>
		/// <param name="workerId">Human-readable worker index for log messages.</param>
		private async Task ProcessSrpProofRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"SRP Proof worker {workerId} started");
			try
			{
				await foreach (var request in proofChannel!.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested) break;
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
			await Log.Debug(LogPrefix, $"SRP Proof worker {workerId} stopped");
		}

		#endregion

		#region SRP Processing

		/// <summary>
		/// Decrypts and validates the SRP verify fields, performs account lookup, generates
		/// the server ephemeral, and broadcasts the verify response to the client.
		/// Runs on an async worker thread — no main-thread access without <see cref="EnqueueMainThread"/>.
		/// </summary>
		/// <param name="request">The dequeued SRP verify request.</param>
		private async Task ProcessSrpVerifyAsync(SrpVerifyRequest<TConnection> request)
		{
			TConnection conn = request.Connection;
			ClientAuthenticationResult result;
			bool waitingForProof = false;

			string? username;
			string? publicEphemeral;
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

			bool isEmail = username!.Contains('@');

			if (isEmail)
			{
				if (!IsAllowedEmailUsername(username))
				{
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}
			}
			else
			{
				if (!IsAllowedUsername(username))
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

			try
			{
				SrpAccountLookupResult lookupResult = await FetchAccountForLoginAsync(username, isEmail);
				RefreshAuthTtl(conn);

				string salt;
				string verifier;
				AccessLevel accessLevel;
				// Carry the unverified flag through the SRP exchange instead of
				// short-circuiting here: an early AccountUnverified reject leaks
				// "this username exists" to anyone who can guess account names.
				// We instead let the client complete the M1 proof against the
				// real verifier; a wrong password still returns the same
				// InvalidUsernameOrPassword as for any non-existent account, and
				// only a correct password reveals AccountUnverified (which is
				// useful information to the legitimate owner anyway).
				bool isUnverified = false;

				if (!lookupResult.IsSuccess)
				{
					salt = SrpService.DerivePerUsernameFakeSalt(username!, fakeSaltKey!);
					verifier = SrpService.GetStaticFakeData().Verifier;
					accessLevel = AccessLevel.Player;
					await Log.Debug(LogPrefix, $"Using fake SRP state for non-existent account '{username}'.");
				}
				else
				{
					if (!lookupResult.IsVerified)
					{
						if (isEmail)
						{
							// Email-based login of an unverified account still uses
							// fake state: revealing "this *email* exists in the system"
							// is a stronger privacy leak than the same fact about a
							// username, since emails are reused across services.
							salt = SrpService.DerivePerUsernameFakeSalt(username!, fakeSaltKey!);
							verifier = SrpService.GetStaticFakeData().Verifier;
							accessLevel = AccessLevel.Player;
							await Log.Debug(LogPrefix, "Using fake SRP state for unverified email-based login.");
						}
						else
						{
							// Username-based unverified account: use the real salt+verifier
							// so the client can prove knowledge of the password; the
							// AccountUnverified result is only sent if the proof succeeds
							// (see ProcessSrpProofAsync).
							salt = lookupResult.Salt;
							verifier = lookupResult.Verifier;
							accessLevel = lookupResult.AccessLevel;
							isUnverified = true;
							await Log.Debug(LogPrefix, "Carrying real SRP state for unverified username-based login; AccountUnverified deferred until after M1.");
						}
					}
					else
					{
						salt = lookupResult.Salt;
						verifier = lookupResult.Verifier;
						accessLevel = lookupResult.AccessLevel;
						totpEnabledByClientId[GetConnectionClientId(conn)] = lookupResult.TotpEnabled;
						verifyCodeExpiryByClientId[GetConnectionClientId(conn)] = lookupResult.VerifyCodeExpiresUtc;
					}
				}

				result = ClientAuthenticationResult.SrpVerify;

				if (!srpAccountManager.AddConnectionAccount(conn, username!, publicEphemeral!, salt, verifier, accessLevel, isUnverified))
				{
					RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
					return;
				}

				string? srpSalt = null;
				string? srpPublicServerEphemeral = null;

				if (AccountManager.TryAdvanceAuthState(conn, AuthState.WaitingForProof, AuthState.WaitingForProof, (a) =>
				{
					srpSalt = a.SrpData?.Salt;
					srpPublicServerEphemeral = a.SrpData?.ServerEphemeral?.Public;
					return true;
				}))
				{
					byte[] encryptedSalt;
					byte[] encryptedPublicServerEphemeral;
					try
					{
						SrpService.EncryptVerifyResponse(srpSalt!, srpPublicServerEphemeral!, request.EncryptionData, out encryptedSalt, out encryptedPublicServerEphemeral);
					}
					catch (CryptographicException ex)
					{
						await Log.Error(LogPrefix, $"AES encryption failed for SRP response: {ex.Message}");
						RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
						return;
					}

					waitingForProof = true;
					EnqueueMainThread(conn, () => BroadcastSrpVerifyResponse(conn, encryptedSalt, encryptedPublicServerEphemeral));
					return;
				}

				result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Error during SRP verify: {ex}");
				result = ClientAuthenticationResult.ServerBusy;
			}

			EnqueueMainThread(conn, () =>
			{
				if (IsConnectionActive(conn))
				{
					BroadcastAuthResult(conn, result, reliable: true);
					DisconnectConnection(conn, graceful: false);
				}
			});

			if (!waitingForProof)
			{
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		/// <summary>
		/// Verifies the client SRP proof, performs online-check and kick-request logic, issues
		/// the auth token, and broadcasts the SRP success response.
		/// Runs on an async worker thread — no main-thread access without <see cref="EnqueueMainThread"/>.
		/// </summary>
		/// <param name="request">The dequeued SRP proof request.</param>
		private async Task ProcessSrpProofAsync(SrpProofRequest<TConnection> request)
		{
			TConnection conn = request.Connection;

			if (request.EncryptionData == null)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

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

			string? serverProof = null;
			string? username = null;
			AccessLevel accessLevel = AccessLevel.Player;
			bool isUnverified = false;

			// Phase 1: Read AccountData under the lock to get the SrpData reference.
			// We must NOT call GetProof() inside the lock — that method performs
			// 2048-bit modular exponentiation (CPU-bound, ~2-10ms). Holding the
			// global syncRoot during that time blocks ALL other connections from
			// registering encryption data, advancing auth state, or disconnecting.
			ServerSrpData? srpDataSnapshot = null;
			if (!AccountManager.GetConnectionAccountData(conn, out AccountData? accountData) ||
				accountData == null ||
				accountData.SrpData == null ||
				accountData.AuthState != AuthState.ProofPending)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}
			srpDataSnapshot = accountData.SrpData;
			username = accountData.SrpData.UserName;
			accessLevel = accountData.AccessLevel;
			isUnverified = accountData.IsUnverified;

			// Phase 2: Compute the SRP proof OUTSIDE the lock.
			// SrpData is per-connection and only one proof verification runs per
			// connection at a time (enforced by the ProofPending → SrpSuccess
			// state transition). The only race is with ClearSrpState, which would
			// require a concurrent state transition that TryAdvanceAuthState prevents.
			bool proofOk = srpDataSnapshot.GetProof(clientProof, out string computedProof);
			if (!proofOk || computedProof == null)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}
			serverProof = computedProof;

			// Phase 3: Atomically advance the auth state under the lock.
			// The callback is now a trivial validation — no CPU-bound work.
			bool proofValid = AccountManager.TryAdvanceAuthState(conn, AuthState.ProofPending, AuthState.SrpSuccess, (a) =>
			{
				// Cross-check: the SrpData reference must still match and the
				// proof result we pre-computed must be valid.
				if (a.SrpData != srpDataSnapshot || serverProof == null)
					return false;
				return true;
			});

			if (!proofValid || serverProof == null || username == null)
			{
				RejectAndPurge(conn, ClientAuthenticationResult.InvalidUsernameOrPassword);
				return;
			}

			// Defer the AccountUnverified result until *after* a valid M1 proof so
			// that wrong-password attempts on an unverified account are
			// indistinguishable from any other failed login (no username-existence
			// oracle). The legitimate owner who knows their password gets a
			// meaningful "verify your email" response.
			if (isUnverified)
			{
				// If the verification code has expired, auto-generate a fresh one.
				// Only triggers when the user actively attempts login after expiry.
				if (verifyCodeExpiryByClientId.TryGetValue(GetConnectionClientId(conn), out var expiry))
				{
					_ = TryResendVerificationEmailIfExpiredAsync(username!, expiry);
				}
				RejectAndPurge(conn, ClientAuthenticationResult.AccountUnverified);
				return;
			}

				// Reject banned accounts after SRP proof succeeds so that wrong-password
				// attempts on banned accounts still return InvalidUsernameOrPassword
				// (same as non-existent accounts), preventing username enumeration.
				if (accessLevel == AccessLevel.Banned)
				{
					RejectAndPurge(conn, ClientAuthenticationResult.Banned);
					return;
				}

			RefreshAuthTtl(conn);

			try
			{
				bool isOnline = await CheckIsOnlineAsync(username);
				RefreshAuthTtl(conn);

				bool hasPendingKick = false;
				if (!isOnline)
				{
					hasPendingKick = await CheckHasPendingKickAsync(username);
				}

				if (isOnline)
				{
					if (TryBeginKickRequest(username))
					{
						await PersistKickRequestAsync(username);
					}
				}

				ClientAuthenticationResult result = (isOnline || hasPendingKick)
					? ClientAuthenticationResult.AlreadyOnline
					: await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username);

				RefreshAuthTtl(conn);

				if (result == ClientAuthenticationResult.LoginSuccess)
				{
					bool totpRequired = false;
					byte[]? totpMasterKeySnapshot = totpMasterKey;
					if (totpEnabledByClientId.TryGetValue(GetConnectionClientId(conn), out bool cachedTotpEnabled) &&
						cachedTotpEnabled)
					{
						// Server-side 2FA enforcement: this account has TotpEnabled=true in the
						// database (set when TwoFactorSetup completed). The login MUST go through
						// the TOTP/recovery-code path. If the master key required to decrypt the
						// user's TOTP secret is not configured, we cannot verify codes — refuse
						// the login rather than silently regressing to non-2FA authentication.
						if (totpMasterKeySnapshot == null || totpMasterKeySnapshot.Length != 32)
						{
							await Log.Error(LogPrefix, $"Account '{username}' has TotpEnabled=true but TotpMasterKey is not configured (length {(totpMasterKeySnapshot?.Length ?? 0)}). Refusing login to prevent 2FA bypass.");
							RejectAndPurge(conn, ClientAuthenticationResult.ServerBusy);
							return;
						}
						totpRequired = true;
					}

					if (totpRequired)
					{
						totpPendingStates[GetConnectionClientId(conn)] = new TotpPendingState
						{
							Connection = conn,
							EncryptionData = request.EncryptionData,
							ServerProof = serverProof,
							Username = username,
							AccessLevel = accessLevel,
							IsEmail = username != null && username.Contains('@'),
							Attempts = 0,
						};

						EnqueueMainThread(conn, () => BroadcastAuthResult(conn, ClientAuthenticationResult.TwoFactorRequired, reliable: true));
						return;
					}
				}

				bool authenticated = result == ClientAuthenticationResult.LoginSuccess;

				byte[] encryptedServerProof = SrpService.EncryptServerProof(serverProof!, request.EncryptionData);

				byte[]? encryptedToken = (authenticated && IsConnectionActive(conn))
					? await GenerateEncryptedAuthTokenAsync(request.EncryptionData, username!, accessLevel)
					: null;

				EnqueueMainThread(conn, () =>
				{
					if (IsConnectionActive(conn))
					{
						BroadcastSrpSuccess(conn, encryptedServerProof, result, encryptedToken);
					}

					OnAuthenticationResult(conn, authenticated);

					if (authenticated)
					{
						if (AccountManager.TryAdvanceAuthState(conn, AuthState.SrpSuccess, AuthState.Authenticated))
						{
							srpAccountManager.ClearSrpState(conn);
						}
					}
					else
					{
						AccountManager.RemoveConnectionAccount(conn);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Error during SRP proof login: {ex}");
				EnqueueMainThread(conn, () =>
				{
					if (IsConnectionActive(conn)) DisconnectConnection(conn, graceful: false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		/// <summary>
		/// Decrypts and verifies the TOTP code for a connection awaiting two-factor confirmation.
		/// On success, completes login by issuing the auth token and broadcasting SRP success.
		/// Runs on an async Task — no main-thread access without <see cref="EnqueueMainThread"/>.
		/// </summary>
		/// <param name="conn">The connection undergoing TOTP verification.</param>
		/// <param name="encryptedCode">AES-GCM encrypted TOTP code bytes.</param>
		/// <param name="seq">Broadcast sequence number for replay protection.</param>
		/// <param name="pendingState">The saved TOTP pending state for this connection.</param>
		private async Task ProcessTwoFactorVerifyAsync(TConnection conn, byte[] encryptedCode, uint seq, TotpPendingState pendingState)
		{
			string totpCode;
			try
			{
				// NOTE: Sequence consumption is handled inside ServerDecryptTotpCode.
				// Do NOT call TryConsumeReceiveSequence here — that would double-consume
				// the sequence number, causing every TOTP verification to fail with
				// CryptographicException ("sequence out-of-order or duplicate").
				totpCode = SrpService.ServerDecryptTotpCode(encryptedCode, pendingState.EncryptionData, seq);
			}
			catch (CryptographicException)
			{
				await Log.Warning(LogPrefix, "AES decryption failed for TOTP code.");
				TrackTotpUsernameFailure(pendingState.Username);

				if (pendingState.Attempts > MaxTotpAttempts)
				{
					totpPendingStates.TryRemove(GetConnectionClientId(conn), out _);
					EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: false));
					PurgeConnectionAuthState(conn, disconnect: false);
				}
				else
				{
					BroadcastAuthResult(conn, ClientAuthenticationResult.TwoFactorInvalid, reliable: true);
				}
				return;
			}

			bool totpValid = await VerifyTotpCodeAsync(pendingState.Username!, totpCode, totpMasterKey!);
			RefreshAuthTtl(conn);

			if (!totpValid)
			{
				TrackTotpUsernameFailure(pendingState.Username);

				if (pendingState.Attempts > MaxTotpAttempts)
				{
					totpPendingStates.TryRemove(GetConnectionClientId(conn), out _);
					EnqueueMainThread(conn, () => DisconnectConnection(conn, graceful: false));
					PurgeConnectionAuthState(conn, disconnect: false);
				}
				else
				{
					BroadcastAuthResult(conn, ClientAuthenticationResult.TwoFactorInvalid, reliable: true);
				}
				return;
			}

			// TOTP success — complete login
			totpPendingStates.TryRemove(GetConnectionClientId(conn), out _);
			totpEnabledByClientId.TryRemove(GetConnectionClientId(conn), out _);

			byte[] encryptedServerProof = SrpService.EncryptServerProof(pendingState.ServerProof!, pendingState.EncryptionData);
			byte[]? encryptedToken = (IsConnectionActive(conn))
				? await GenerateEncryptedAuthTokenAsync(pendingState.EncryptionData, pendingState.Username!, pendingState.AccessLevel)
				: null;

			EnqueueMainThread(conn, () =>
			{
				if (IsConnectionActive(conn))
				{
					BroadcastSrpSuccess(conn, encryptedServerProof, ClientAuthenticationResult.LoginSuccess, encryptedToken);
				}

				OnAuthenticationResult(conn, authenticated: true);

				if (AccountManager.TryAdvanceAuthState(conn, AuthState.SrpSuccess, AuthState.Authenticated))
				{
					srpAccountManager.ClearSrpState(conn);
				}
			});
		}

		#endregion

		#region Rate Limiting Helpers

		/// <summary>
		/// Attempts to begin an IP-level auth attempt, enforcing the per-IP debounce.
		/// </summary>
		/// <param name="ip">Normalized remote IP address.</param>
		/// <returns><c>true</c> if the attempt is allowed; <c>false</c> if rate-limited.</returns>
		private bool TryBeginIpAuthAttempt(string ip)
		{
			if (string.IsNullOrEmpty(ip)) return true;
			return ipAuthNextAllowedUtc.TryBegin(ip, DateTime.UtcNow, TimeSpan.FromSeconds(IpAuthAttemptDebounceSeconds));
		}

		/// <summary>
		/// Attempts to begin an account-level verify debounce entry.
		/// Prevents excessive database lookups for the same account identifier.
		/// </summary>
		/// <param name="username">Account identifier (username or email).</param>
		/// <returns><c>true</c> if the attempt is allowed; <c>false</c> if debounced or the tracker is full.</returns>
		private bool TryBeginAccountVerifyAttempt(string username)
		{
			if (string.IsNullOrEmpty(username)) return true;
			if (accountVerifyNextAllowedUtc.Count >= MaxAccountVerifyDebounceEntries) return false;
			return accountVerifyNextAllowedUtc.TryBegin(username, DateTime.UtcNow, TimeSpan.FromSeconds(AccountVerifyDebounceSeconds));
		}

		/// <summary>
		/// Attempts to begin a kick-request debounce entry for the given account.
		/// Prevents duplicate kick requests from being persisted within the debounce window.
		/// </summary>
		/// <param name="username">Account name to kick.</param>
		/// <returns><c>true</c> if a kick request should be sent; <c>false</c> if already debounced.</returns>
		private bool TryBeginKickRequest(string username)
		{
			if (string.IsNullOrEmpty(username)) return false;
			return kickRequestNextAllowedUtcByAccount.TryBegin(username, DateTime.UtcNow, TimeSpan.FromSeconds(KickRequestDebounceSeconds));
		}

		/// <summary>
		/// Resolves the normalized IP address for a connection, using the short-lived
		/// <see cref="connectionIpCache"/> to avoid repeated normalization on the hot path.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <returns>Normalized IP string (IPv4 or IPv6).</returns>
		private string ResolveAndCacheIp(TConnection conn)
		{
			int clientId = GetConnectionClientId(conn);
			if (connectionIpCache.TryGetAndTouch(clientId, DateTime.UtcNow, out string cached))
				return cached;
			string ip = HandshakeService.NormalizeIp(GetConnectionAddress(conn));
			connectionIpCache.Upsert(clientId, ip, DateTime.UtcNow);
			return ip;
		}

		/// <summary>
		/// Records a TOTP failure for the given username toward the per-username lockout threshold.
		/// </summary>
		/// <param name="username">Account name that failed TOTP verification.</param>
		private void TrackTotpUsernameFailure(string? username)
		{
			if (string.IsNullOrEmpty(username)) return;
			string key = username.ToLowerInvariant();
			if (totpUsernameFailures.Count >= MaxTotpUsernameFailureEntries) return;
			totpUsernameFailures.AddOrUpdate(
				key,
				_ => (1, DateTime.UtcNow),
				(_, prev) => (prev.Count + 1, prev.FirstFailure));
		}

		/// <summary>
		/// Generates and AES-GCM encrypts an auth token for the authenticated connection,
		/// then persists its hash via <see cref="PersistTokenHashAsync"/>.
		/// </summary>
		/// <param name="encryptionData">Per-connection encryption context for the outgoing token.</param>
		/// <param name="username">Account name to embed in the token.</param>
		/// <param name="accessLevel">Access level to embed in the token.</param>
		/// <returns>The encrypted token bytes, or <c>null</c> if signing key is unavailable or encryption failed.</returns>
		private async Task<byte[]?> GenerateEncryptedAuthTokenAsync(ConnectionEncryptionData encryptionData, string username, AccessLevel accessLevel)
		{
			byte[]? signingKey = tokenSigningKey;
			if (signingKey == null) return null;

			byte[]? encryptedToken = TokenService.GenerateAndEncryptToken(
				encryptionData,
				username,
				LoginServerId,
				TokenSigningKeyId,
				(int)TokenExpirationMinutes,
				signingKey,
				accessLevel,
				out byte[]? rawTokenForHashing);

			if (rawTokenForHashing != null && encryptedToken != null)
			{
				string tokenHash = TokenService.HashToken(rawTokenForHashing);
				CryptographicOperations.ZeroMemory(rawTokenForHashing);
				await PersistTokenHashAsync(username, tokenHash, (int)TokenExpirationMinutes);
			}
			else if (rawTokenForHashing != null)
			{
				CryptographicOperations.ZeroMemory(rawTokenForHashing);
			}

			return encryptedToken;
		}

		/// <summary>
		/// Provides a way to reject and purge a connection from an async callback context.
		/// </summary>
		private void RejectAndPurge(TConnection conn, ClientAuthenticationResult result)
		{
			EnqueueMainThread(conn, () =>
			{
				if (IsConnectionActive(conn))
				{
					BroadcastAuthResult(conn, result, reliable: true);
					DisconnectConnection(conn, graceful: false);
				}
			});
			PurgeConnectionAuthState(conn, disconnect: false);
		}

		#endregion

		#region Abstract / Virtual Transport Callbacks

		/// <summary>
		/// Called when an authentication result (success or failure) must be reported for a connection.
		/// Implementations should call OnAuthenticationResult event, FishNet's PassAuthentication/FailAuthentication, etc.
		/// </summary>
		/// <param name="conn">The authenticated (or rejected) connection.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
		protected abstract void OnAuthenticationResult(TConnection conn, bool authenticated);

		/// <summary>
		/// Returns whether a connection is currently active (connected and not disposed).
		/// </summary>
		/// <param name="conn">The connection to check.</param>
		/// <returns><c>true</c> if the connection is still active; otherwise, <c>false</c>.</returns>
		protected abstract bool IsConnectionActive(TConnection conn);

		/// <summary>
		/// Broadcasts a generic auth result to the client.
		/// </summary>
		/// <param name="conn">Target connection.</param>
		/// <param name="result">Auth result code.</param>
		/// <param name="reliable">True for reliable delivery, false for unreliable.</param>
		protected abstract void BroadcastAuthResult(TConnection conn, ClientAuthenticationResult result, bool reliable);

		/// <summary>
		/// Broadcasts the SRP verify response (encrypted salt + server public ephemeral) to the client.
		/// </summary>
		/// <param name="conn">Target connection.</param>
		/// <param name="encryptedSalt">AES-GCM encrypted SRP salt.</param>
		/// <param name="encryptedPublicServerEphemeral">AES-GCM encrypted server public ephemeral.</param>
		protected abstract void BroadcastSrpVerifyResponse(TConnection conn, byte[] encryptedSalt, byte[] encryptedPublicServerEphemeral);

		/// <summary>
		/// Broadcasts the SRP success response (encrypted server proof + result + optional token) to the client.
		/// </summary>
		/// <param name="conn">Target connection.</param>
		/// <param name="encryptedServerProof">AES-GCM encrypted server proof bytes.</param>
		/// <param name="result">Auth result code accompanying the proof.</param>
		/// <param name="encryptedToken">AES-GCM encrypted auth token, or <c>null</c> if login was not successful.</param>
		protected abstract void BroadcastSrpSuccess(TConnection conn, byte[] encryptedServerProof, ClientAuthenticationResult result, byte[]? encryptedToken);

		/// <summary>
		/// Enqueues an action to be executed on the main/UI thread.
		/// Implementations using Unity must marshal all network API calls (Broadcast, Disconnect) via this method.
		/// Non-Unity implementations may execute immediately or use their own dispatcher.
		/// </summary>
		/// <param name="conn">The connection context (for lifetime checking).</param>
		/// <param name="action">The action to enqueue.</param>
		protected abstract void EnqueueMainThread(TConnection conn, Action action);

		/// <summary>
		/// Validates a username against the allowed username rules.
		/// Delegates to <c>Authentication.IsAllowedUsername</c> (FishMMO-SharedUtility).
		/// </summary>
		/// <param name="username">The username string to validate.</param>
		/// <returns><c>true</c> if the username is allowed; otherwise, <c>false</c>.</returns>
		protected abstract bool IsAllowedUsername(string username);

		/// <summary>
		/// Validates an email-format username against the allowed email username rules.
		/// Delegates to <c>Authentication.IsAllowedEmailUsername</c> (FishMMO-SharedUtility).
		/// </summary>
		/// <param name="username">The email address to validate.</param>
		/// <returns><c>true</c> if the email is a valid login identifier; otherwise, <c>false</c>.</returns>
		protected abstract bool IsAllowedEmailUsername(string username);

		/// <summary>
		/// Attempts to complete login and returns the final auth result.
		/// Override to apply server-type-specific login logic (e.g., WorldServer player limit checks).
		/// Default: returns <paramref name="defaultResult"/> unchanged.
		/// </summary>
		/// <param name="defaultResult">The result to return if no override logic modifies it.</param>
		/// <param name="username">Account name being authenticated.</param>
		/// <returns>The final <see cref="ClientAuthenticationResult"/> to send to the client.</returns>
		protected virtual Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username)
		{
			return Task.FromResult(defaultResult);
		}

		/// <summary>
		/// Fetches account data for SRP login (by username or email).
		/// </summary>
		/// <param name="identifier">Username or email address.</param>
		/// <param name="isEmail">True if <paramref name="identifier"/> is an email address.</param>
		/// <returns>An <see cref="SrpAccountLookupResult"/> with account data if found.</returns>
		protected abstract Task<SrpAccountLookupResult> FetchAccountForLoginAsync(string identifier, bool isEmail);

		/// <summary>
		/// Checks whether any character for the given account name is currently online.
		/// </summary>
		/// <param name="username">Account name to check.</param>
		/// <returns><c>true</c> if any session for this account is active; otherwise, <c>false</c>.</returns>
		protected abstract Task<bool> CheckIsOnlineAsync(string username);

		/// <summary>
		/// Checks whether a pending kick request exists for the given account name.
		/// </summary>
		/// <param name="username">Account name to check.</param>
		/// <returns><c>true</c> if a pending kick is recorded; otherwise, <c>false</c>.</returns>
		protected abstract Task<bool> CheckHasPendingKickAsync(string username);

		/// <summary>
		/// Persists a kick request for the given account name to the database.
		/// </summary>
		/// <param name="username">Account name to kick.</param>
		protected abstract Task PersistKickRequestAsync(string username);

		/// <summary>
		/// Persists the hashed token for the given account to the database (for revocation tracking).
		/// </summary>
		/// <param name="username">Account name.</param>
		/// <param name="tokenHash">SHA-256 hex hash of the raw token.</param>
		/// <param name="expirationMinutes">Token validity duration.</param>
		protected abstract Task PersistTokenHashAsync(string username, string tokenHash, int expirationMinutes);

		/// <summary>
		/// Verifies a TOTP code for the given username against the database-stored secret.
		/// </summary>
		/// <param name="username">Account name.</param>
		/// <param name="totpCode">6-digit TOTP code (plaintext).</param>
		/// <param name="totpMasterKey">AES-256 key for decrypting the stored TOTP secret.</param>
		/// <returns>True if the code is valid; false otherwise.</returns>
		protected abstract Task<bool> VerifyTotpCodeAsync(string username, string totpCode, byte[] totpMasterKey);

		/// <summary>
		/// Called when an unverified account attempts login. The implementation should
		/// check whether the verification code has expired and, if so, generate a new
		/// code and enqueue a fresh verification email. No-op when the code is still
		/// valid or when the email has not yet been sent (VerificationEmailSentAt is null).
		/// </summary>
		/// <param name="username">The account username.</param>
		/// <param name="verifyCodeExpiresUtc">UTC expiry of the current code, or null.</param>
		/// <returns>True if a new code was generated and the email was enqueued.</returns>
		protected abstract Task<bool> TryResendVerificationEmailIfExpiredAsync(string username, DateTime? verifyCodeExpiresUtc);

		#endregion

		#region Nested Types

		/// <summary>
		/// Result of an account fetch for SRP login.
		/// </summary>
		public struct SrpAccountLookupResult
		{
			/// <summary>Whether the account was found and fetchable.</summary>
			public bool IsSuccess;
			/// <summary>Whether the account has been email-verified.</summary>
			public bool IsVerified;
			/// <summary>SRP salt.</summary>
			public string Salt;
			/// <summary>SRP verifier.</summary>
			public string Verifier;
			/// <summary>Account access level.</summary>
			public AccessLevel AccessLevel;
			/// <summary>Whether TOTP is enabled for this account.</summary>
			public bool TotpEnabled;
			/// <summary>UTC expiry for the verification code (null if not applicable).</summary>
			public DateTime? VerifyCodeExpiresUtc;
		}

		/// <summary>Holds transient state for a connection that has passed SRP proof and is awaiting TOTP confirmation.</summary>
		private class TotpPendingState
		{
			/// <summary>The network connection awaiting TOTP verification.</summary>
			public TConnection Connection = default!;
			/// <summary>Per-connection encryption context used to decrypt the TOTP code.</summary>
			public ConnectionEncryptionData EncryptionData = default!;
			/// <summary>Encrypted server SRP proof, held until TOTP succeeds before being broadcast.</summary>
			public string? ServerProof;
			/// <summary>Account name associated with the pending TOTP verification.</summary>
			public string? Username;
			/// <summary>Access level resolved during SRP proof, forwarded to the token on TOTP success.</summary>
			public AccessLevel AccessLevel;
			/// <summary>True if <see cref="Username"/> is an email-format identifier.</summary>
			public bool IsEmail;
			/// <summary>Running count of TOTP verification attempts; used to enforce <see cref="MaxTotpAttempts"/>.</summary>
			public int Attempts;
		}

		#endregion
	}
}