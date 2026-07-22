using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core.Smtp;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Handles player account creation requests asynchronously with rate limiting and DoS protection.
	/// Stateless logic container - all mutable state stored in RuntimeDataContainers.
	/// Network thread acts as ultra-fast reactive UDP gate with zero blocking operations.
	/// </summary>
	[CreateAssetMenu(fileName = "AccountCreationSystem", menuName = "FishMMO/Server/LoginServer/Account Creation System", order = 1)]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	[RequiresDataContainer(typeof(AccountCreationSystemRuntimeData))]
	[RequiresDataContainer(typeof(AccountCreationSystemMappingData))]
	[RequiresDataContainer(typeof(AccountCreationSystemMainThreadQueueData))]
	public class AccountCreationSystem : ServerBehaviour, IAccountCreationSystem<NetworkConnection>
	{
		/// <summary>
		/// Result of attempting to enqueue an account creation request.
		/// </summary>
		private enum EnqueueResult : byte
		{
			/// <summary>Request was accepted and queued for processing.</summary>
			Accepted = 0,
			/// <summary>Request was rate-limited; the client should back off.</summary>
			RateLimited = 1,
			/// <summary>IP address is blocked due to excessive failed attempts.</summary>
			Blocked = 2,
			/// <summary>Async worker queue is full; the client should retry later.</summary>
			QueueFull = 3,
			/// <summary>Data containers or services are unavailable.</summary>
			Unavailable = 4,
		}

		/// <summary>
		/// Minimum seconds between account creation attempts from the same IP address.
		/// </summary>
		[Header("Rate Limiting")]
		[Tooltip("Minimum seconds between account creation attempts from the same IP")]
		[SerializeField] private float ipRateLimitSeconds = 5.0f;

		/// <summary>
		/// Maximum failed attempts allowed before an IP is temporarily blocked.
		/// </summary>
		[Tooltip("Maximum failed attempts before IP is temporarily blocked")]
		[SerializeField] private int maxFailedAttempts = 5;

		/// <summary>
		/// Duration in seconds that an IP remains blocked after exceeding failed-attempt threshold.
		/// </summary>
		[Tooltip("Duration in seconds to block an IP after max failed attempts")]
		[SerializeField] private float ipBlockDurationSeconds = 300.0f; // 5 minutes

		/// <summary>
		/// Maximum number of accounts that may be created
		/// globally within a rolling one-hour window. Per-IP and per-connection
		/// caps alone are bypassable by an attacker with a sufficiently large IP
		/// pool (botnet, residential-proxy abuse). A global ceiling caps the
		/// blast radius of automated registration floods regardless of IP
		/// diversity. Set to a value that comfortably exceeds expected organic
		/// growth; legitimate spikes (launch days, marketing pushes) should be
		/// handled by raising this value, not by disabling it.
		/// </summary>
		[Tooltip("Global hourly account creation cap. Excess requests are rejected with ServerBusy.")]
		[SerializeField] private int maxGlobalAccountCreationsPerHour = 1000;

		/// <summary>
		/// Lock-free sliding-window state for the global hourly cap. The hour
		/// is identified by UTC hours-since-epoch; on tick-over the counter is
		/// reset atomically. Uses Interlocked operations rather than a lock so
		/// the hot path stays alloc-free under contention.
		/// </summary>
		private long globalCreationsCurrentHourBucket = -1;
		private int globalCreationsCurrentHourCount = 0;
		private readonly object globalCreationsCounterLock = new object();

		/// <summary>
		/// Minimum seconds between email queue processing sweeps.
		/// </summary>
		[Header("Email Queue")]
		[Tooltip("Seconds between email queue processing sweeps. Set to 0 to disable.")]
		[SerializeField] private float emailSendIntervalSeconds = 10.0f;

		/// <summary>
		/// Accumulator for the email send interval timer.
		/// </summary>
		private float emailSendTimer;
		/// <summary>
		/// Lazily-constructed SMTP sender. Null until first email queue sweep.
		/// </summary>
		private ISmtpService smtpService;
	/// <summary>
	/// Lock for thread-safe lazy initialization of <see cref="smtpService"/>.
	/// </summary>
	private readonly object smtpServiceLock = new object();

		/// <summary>
		/// Injectable SMTP service. When set externally (e.g. by LoginServerSystem during
		/// initialization), this instance is used instead of lazy-constructing from config.
		/// Set to null to revert to lazy construction.
		/// </summary>
		public ISmtpService SmtpService
		{
			get => smtpService;
			set => smtpService = value;
		}
		/// <summary>
		/// Maximum number of queued main-thread response actions processed per frame.
		/// This time-slices response dispatch to avoid frame spikes during heavy login waves.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max account-creation responses drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadResponsesPerFrame = 100;

		/// <summary>
		/// Hard cap on the number of unique IPs tracked in the <see cref="IAccountCreationSystemMappingData.IpFailureTracker"/>.
		/// Prevents unbounded dictionary growth if an attacker floods from spoofed or rotating IPs.
		/// When the cap is reached, <see cref="TryTrackIpFailure"/> returns <c>false</c> so the caller can
		/// fail closed (disconnect the request) rather than silently skipping the increment, which would
		/// let the offender stay just under the per-IP block threshold indefinitely.
		/// </summary>
		private const int MaxIpFailureTrackerEntries = 50_000;

		/// <summary>
		/// Maximum entries scanned per map during one maintenance sweep.
		/// </summary>
		[Header("Cleanup Bounds")]
		[Tooltip("Max entries scanned per map each cleanup sweep")]
		[SerializeField] private int cleanupMaxScanPerMap = 256;

		/// <summary>
		/// Maximum entries removed per map during one maintenance sweep.
		/// </summary>
		[Tooltip("Max entries removed per map each cleanup sweep")]
		[SerializeField] private int cleanupMaxRemovalsPerMap = 128;

		/// <summary>
		/// When true, uses the transport-level connection ID (conn.ClientId) as the rate-limiting key
		/// instead of the resolved IP address. Enable this when the server is behind a proxy, NAT, or
		/// load balancer where all clients share the proxy's IP, causing false-positive rate limiting
		/// that blocks legitimate users.
		/// </summary>
		/// <summary>
		/// Maximum cumulative verification failures per username across all connections before
		/// a temporary lockout. Prevents botnets from distributing brute-force across IPs.
		/// Tightened from 10 → 5: with a 900K-value 6-digit code space, 10
		/// attempts gave attackers a non-trivial probability of success when combined with
		/// even modest IP rotation. Five attempts is still enough headroom for a legitimate
		/// user who fat-fingers the code a few times.
		/// </summary>
		private const int MaxVerifyFailuresPerUsername = 5;

		/// <summary>
		/// How long a per-username verification lockout lasts after exceeding <see cref="MaxVerifyFailuresPerUsername"/>.
		/// Extended from 30 → 60 minutes to outlast typical email-delivery windows and reduce
		/// the duty cycle available to a distributed brute-force attacker.
		/// </summary>
		private static readonly TimeSpan VerifyUsernameLockoutDuration = TimeSpan.FromMinutes(60);

		/// <summary>
		/// Maximum entries allowed in the per-username verification failure tracker before new
		/// entries are silently ignored. Guards against memory exhaustion from unique username floods.
		/// </summary>
		private const int MaxVerifyUsernameFailureEntries = 50_000;

		/// <summary>
		/// Maximum entries scanned per sweep when evicting expired per-username verification failure records.
		/// </summary>
		private const int VerifyUsernameFailureSweepMaxScan = 64;

		/// <summary>
		/// Per-username verification failure counter for cross-connection rate limiting.
		/// Key = lowercased username, Value = (failureCount, firstFailureUtc).
		/// Entries are lazily evicted when checked and proactively swept in <see cref="CleanUpMappingData"/>.
		/// </summary>
		private readonly ConcurrentDictionary<string, (int Count, DateTime FirstFailure)> verifyUsernameFailures = new ConcurrentDictionary<string, (int, DateTime)>();

		/// <summary>
		/// Maximum allowed size in bytes for any single encrypted field in CreateAccountBroadcast.
		/// Rejects oversized payloads on the network thread before any decryption or allocation.
		/// </summary>
		private const int MaxEncryptedFieldSize = 2048;

		/// <summary>
		/// AES-256 master key for encrypting TOTP secrets at rest in the database.
		/// Set by LoginServerSystem on startup. Must match ServerAuthenticator.TotpMasterKey.
		/// </summary>
		public byte[] TotpMasterKey { get; set; }

		/// <summary>
		/// Maximum allowed length for the decrypted SRP salt string.
		/// </summary>
		private const int MaxSaltLength = 256;

		/// <summary>
		/// Maximum allowed length for the decrypted SRP verifier string.
		/// </summary>
		private const int MaxVerifierLength = 1024;

		/// <summary>
		/// Gets the current number of pending account creation requests in the async queue.
		/// </summary>
		public int PendingRequestCount
		{
			get
			{
				if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
				{
					return asyncWorker.PendingCount;
				}
				return 0;
			}
		}

		/// <summary>
		/// Gets the total number of successfully processed account creation requests.
		/// </summary>
		public long TotalProcessed
		{
			get
			{
				if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) == true)
				{
					return runtimeData.TotalProcessed;
				}
				return 0;
			}
		}

		/// <summary>
		/// Gets the total number of rejected account creation requests.
		/// </summary>
		public long TotalRejected
		{
			get
			{
				if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) == true)
				{
					return runtimeData.TotalRejected;
				}
				return 0;
			}
		}

		/// <summary>
		/// Initializes the account creation system and registers network/connection handlers.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("AccountCreationSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Verify all required data containers are available
			if (!Server.DataContainerRegistry.TryGet<IAsyncWorkerData>(out _))
			{
				Log.Error("AccountCreationSystem", "Failed to initialize: IAsyncWorkerData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out _))
			{
				Log.Error("AccountCreationSystem", "Failed to initialize: IAccountCreationSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out _))
			{
				Log.Error("AccountCreationSystem", "Failed to initialize: IAccountCreationSystemMappingData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemMainThreadQueueData>(out _))
			{
				Log.Error("AccountCreationSystem", "Failed to initialize: IAccountCreationSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (ServerManager == null)
			{
				Log.Error("AccountCreationSystem", "InitializeOnce: ServerManager is null");
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}

			SubscribeToConnectionEvents();

			// Clamp tunables to safe values.
			ipRateLimitSeconds = Mathf.Max(0f, ipRateLimitSeconds);
			maxFailedAttempts = Mathf.Max(1, maxFailedAttempts);
			ipBlockDurationSeconds = Mathf.Max(1f, ipBlockDurationSeconds);
			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);
			cleanupMaxScanPerMap = Mathf.Max(1, cleanupMaxScanPerMap);
			cleanupMaxRemovalsPerMap = Mathf.Max(1, cleanupMaxRemovalsPerMap);

			// Register network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);
			Server.NetworkWrapper.RegisterBroadcast<AccountVerifyBroadcast>(OnServerAccountVerifyBroadcastReceived, false);

			Log.Debug("AccountCreationSystem", $"Initialized (RateLimit={ipRateLimitSeconds}s, MaxFailures={maxFailedAttempts}, BlockDuration={ipBlockDurationSeconds}s)");

			// Operational warning: ClientId-keyed rate limiting only makes sense behind a
			// trusted proxy that prevents arbitrary client reconnection. On a direct-Internet
			// listener it lets an attacker reset their rate-limit bucket simply by reconnecting,
			// so surface this loudly at startup so it cannot be enabled by accident.
			{
				Log.Warning("AccountCreationSystem",
					"This is ONLY safe behind a trusted reverse proxy / load balancer that authenticates " +
					"connection establishment. On a direct-Internet listener an attacker can bypass " +
					"per-IP throttling by simply reconnecting. Disable this unless you have a proxy in front.");
			}

			// Operational warning: the global hourly account-creation cap is a primary
			// DoS shield. Zero/negative values intentionally disable it (see
			// TryConsumeGlobalCreationBudget) so it can be hot-toggled, but a config
			// typo that leaves it disabled in production is a serious foot-gun. Surface
			// this loudly at startup so it cannot pass code review unnoticed.
			if (maxGlobalAccountCreationsPerHour <= 0)
			{
				Log.Warning("AccountCreationSystem",
					$"maxGlobalAccountCreationsPerHour={maxGlobalAccountCreationsPerHour}: " +
					"the global account-creation DoS cap is DISABLED. Account creation is now " +
					"limited only by per-IP throttles, which a distributed attacker can bypass. " +
					"Set a positive value (recommended: >=100) in production.");
			}
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the account creation system and unregisters handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("AccountCreationSystem", "OnDeinitialize: Server is null");
				return;
			}

			UnsubscribeFromConnectionEvents();

			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.ConnectionIpCache.Clear();
				runtimeData.ConnectionEncryptionCache.Clear();
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);

			// Unregister broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<AccountVerifyBroadcast>(OnServerAccountVerifyBroadcastReceived);

			Log.Debug("AccountCreationSystem", "Deinitialized");
		}

		/// <summary>
		/// Ultra-fast network broadcast handler - acts as reactive UDP gate with zero blocking.
		/// Only validates connection and enqueues ENCRYPTED data. All heavy work offloaded to workers.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">CreateAccountBroadcast message containing encrypted credentials.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnServerCreateAccountBroadcastReceived(NetworkConnection conn, CreateAccountBroadcast msg, Channel channel)
		{
			// Already-authenticated connections should not be creating accounts.
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// Fast validation - don't block network thread
			if (!ResolveEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Reject oversized encrypted fields before any allocation or decryption.
			if (msg.Username == null || msg.Username.Length > MaxEncryptedFieldSize ||
				msg.Email == null || msg.Email.Length > MaxEncryptedFieldSize ||
				msg.Age == null || msg.Age.Length > MaxEncryptedFieldSize ||
				msg.Salt == null || msg.Salt.Length > MaxEncryptedFieldSize ||
				msg.Verifier == null || msg.Verifier.Length > MaxEncryptedFieldSize)
			{
				conn.Disconnect(true);
				return;
			}

			// Get the real IP from the connection token cache.
			// Never fall back to proxy IP or ClientId — disconnect if unavailable.
			string? ipAddress = ResolveIpAddress(conn);
			if (string.IsNullOrEmpty(ipAddress))
			{
				_ = Log.Warning("AccountCreationSystem", $"Rejecting account creation: no real IP for connection {conn.ClientId}.");
				conn.Disconnect(true);
				return;
			}

			// Create request with ENCRYPTED data (no decryption on network thread!)
			var request = new AccountCreationRequest<NetworkConnection>(
				conn,
				msg.Username,              // Still encrypted!
				msg.Email,                 // Still encrypted!
				msg.Age,                   // Still encrypted!
				msg.Salt,                  // Still encrypted!
				msg.Verifier,              // Still encrypted!
				encryptionData,
				ipAddress,
				msg.Seq
			);

			// Try to enqueue request for async processing
			EnqueueResult enqueueResult = TryEnqueueAccountCreationInternal(request);
			switch (enqueueResult)
			{
				case EnqueueResult.Accepted:
					return;
				case EnqueueResult.Blocked:
					// Blocked IP is disconnected immediately; do not spend time sending a response.
					return;
				default:
					// Queue full/rate limited/unavailable - send immediate rejection.
					SendServerBusyResponse(conn);
					return;
			}
		}

		/// <summary>
		/// Public API expected by <see cref="IAccountCreationSystem{TConnection}"/>.
		/// Returns <c>true</c> only when the request is accepted.
		/// </summary>
		/// <param name="request">Account creation request containing encrypted credentials.</param>
		public bool TryEnqueueAccountCreation(AccountCreationRequest<NetworkConnection> request)
		{
			return TryEnqueueAccountCreationInternal(request) == EnqueueResult.Accepted;
		}

		/// <summary>
		/// Internal enqueue path returning a detailed result used by the network ingress handler.
		/// </summary>
		private EnqueueResult TryEnqueueAccountCreationInternal(AccountCreationRequest<NetworkConnection> request)
		{
			// Access data containers for this operation.
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData) ||
				!Server.DataContainerRegistry.TryGet<IAsyncWorkerData>(out _))
			{
				return EnqueueResult.Unavailable;
			}

			// Check IP block BEFORE updating rate-limit timestamp.
			// This ensures blocked IPs don't refresh their rate-limit entry,
			// so the cleanup sweep can expire and eventually lift the block.
			if (mappingData.IpFailureTracker.TryGetValue(request.IpAddress, out int failureCount))
			{
				if (failureCount >= maxFailedAttempts)
				{
					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
					{
						runtimeData.IncrementRejected();
					}

					request.Connection.Disconnect(true); // Disconnect immediately to mitigate DoS
					return EnqueueResult.Blocked;
				}
			}

			// Atomic IP rate-limit check using AddOrUpdate to prevent TOCTOU race.
			bool wasRateLimited = false;
			DateTime nowUtc = DateTime.UtcNow;
			mappingData.IpRateLimitTracker.AddOrUpdate(
				request.IpAddress,
				nowUtc,
				(_, lastAttempt) =>
				{
					if ((nowUtc - lastAttempt).TotalSeconds < ipRateLimitSeconds)
					{
						wasRateLimited = true;
						return lastAttempt; // Don't update timestamp if rate limited.
					}
					return nowUtc;
				});
			if (wasRateLimited)
			{
				return EnqueueResult.RateLimited;
			}

			// Global hourly account-creation cap. Applied
			// AFTER the per-IP rate-limit and BEFORE enqueue so an attacker
			// rotating IPs still pays the per-IP delay. A check-only gate
			// prevents accepting requests when the budget is already exhausted;
			// the actual budget slot is consumed on successful account creation
			// so that failed requests (duplicate username, validation, DB errors)
			// do not deplete the shared cap. Failures here are reported as QueueFull
			// (which maps to ServerBusy on the wire) to avoid disclosing the
			// existence/threshold of the global cap to a probing attacker.
			if (!TryCheckGlobalCreationBudget(nowUtc))
			{
				return EnqueueResult.QueueFull;
			}

			// Try to enqueue to centralized async worker.
			if (TryEnqueueAsyncWork(() => ProcessAccountCreationAsync(request), request.Connection.ClientId))
			{
				return EnqueueResult.Accepted;
			}

			return EnqueueResult.QueueFull;
		}

		/// <summary>
		/// Checks whether the rolling hourly global account-creation budget has been exhausted
		/// without consuming a slot. The actual consumption happens on successful creation
		/// (<see cref="IncrementGlobalCreationCount"/>) so that failed requests do not
		/// deplete the budget.
		/// </summary>
		private bool TryCheckGlobalCreationBudget(DateTime nowUtc)
		{
			int cap = maxGlobalAccountCreationsPerHour;
			if (cap <= 0)
			{
				return true; // Cap disabled by configuration.
			}

			long currentBucket = (long)(nowUtc - DateTime.UnixEpoch).TotalHours;
			lock (globalCreationsCounterLock)
			{
				if (globalCreationsCurrentHourBucket != currentBucket)
				{
					// New hour bucket — always allow at check time; the consumer will reset.
					return true;
				}
				return globalCreationsCurrentHourCount < cap;
			}
		}

		/// <summary>
		/// Atomically consumes one slot from the rolling hourly global account-creation budget.
		/// Must only be called after the account has been successfully persisted.
		/// </summary>
		private void IncrementGlobalCreationCount(DateTime nowUtc)
		{
			int cap = maxGlobalAccountCreationsPerHour;
			if (cap <= 0)
			{
				return; // Cap disabled by configuration.
			}

			long currentBucket = (long)(nowUtc - DateTime.UnixEpoch).TotalHours;
			lock (globalCreationsCounterLock)
			{
				if (globalCreationsCurrentHourBucket != currentBucket)
				{
					globalCreationsCurrentHourBucket = currentBucket;
					globalCreationsCurrentHourCount = 0;
				}
				globalCreationsCurrentHourCount++;
			}
		}

		/// <summary>
		/// Sends immediate ServerBusy response when rate limited or queue full.
		/// Ultra-fast reactive UDP response with no blocking operations.
		/// </summary>
		/// <param name="conn">Network connection to send response to.</param>
		private void SendServerBusyResponse(NetworkConnection conn)
		{
			if (conn == null)
				return;

			// Increment rejection counter
			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IncrementRejected();
			}

			// Send immediate response as unreliable as we don't want to risk blocking the network thread during a DoS attack
			// Note: Client should handle potential loss of this message gracefully since it's a reactive UDP response to a failed request
			Server.NetworkWrapper.Broadcast(conn, new ClientAuthResultBroadcast()
			{
				Result = ClientAuthenticationResult.ServerBusy
			}, false, Channel.Unreliable);

			// Optional: Log for monitoring
			Log.Warning("AccountCreationSystem", $"Rejected request from {ResolveIpAddress(conn)} - Rate limited or queue full");
		}

		/// <summary>
		/// Processes a single account creation request asynchronously.
		/// Performs decryption, database operations, and sends response to client.
		/// </summary>
		/// <param name="request">Account creation request containing encrypted credentials.</param>
		private async Task ProcessAccountCreationAsync(AccountCreationRequest<NetworkConnection> request)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				try
				{
					#region Decrypt
					// Decrypt credentials on worker thread using explicit sequence numbers provided by client.
					// Design note — String heap retention: After decrypting into byte arrays (which are
					// zeroed in finally/catch blocks), the byte data must be converted to .NET strings
					// (username, email, salt, verifier) for validation, DB operations, and SRP math.
					// These strings are immutable, GC-managed, and CANNOT be deterministically zeroed.
					// They will persist in heap memory until the GC collects them. This is an inherent
					// limitation of the .NET string type. No practical mitigation exists short of pinvoke
					// to pinned char arrays, which would not integrate with Entity Framework or SRP libraries.
					// The byte[] plaintext is zeroed as soon as the string conversion completes.
					byte[] decryptedUsername;
					byte[] decryptedEmail;
					byte[] decryptedAge;
					byte[] decryptedSalt;
					byte[] decryptedVerifier;
					try
					{
						uint seq = request.Seq;
						// Guard: ValidateSequenceRange ensures seq is large enough for the 5-field
						// protocol encoding (seq-4..seq) without uint underflow.
						if (!CryptoHelper.ValidateSequenceRange(seq, 5))
						{
							NetworkConnection failConn = request.Connection;
							TryEnqueueMainThread(() =>
							{
								if (failConn != null && failConn.IsActive)
									failConn.Disconnect(false);
							});
							return;
						}

						// Expected order: username (seq-4), email (seq-3), age (seq-2), salt (seq-1), verifier (seq)
						uint seqUsername = seq - 4;
						uint seqEmail = seq - 3;
						uint seqAge = seq - 2;
						uint seqSalt = seq - 1;
						uint seqVerifier = seq;

						// Atomic 5-sequence consume. Either all five
						// receive slots advance or none do; we never leave the counter
						// mid-burst on a partial decrypt failure.
						if (!request.EncryptionData.TryConsumeReceiveSequenceRange(seqUsername, 5))
							throw new CryptographicException("Account creation sequence range out-of-order or duplicate.");

						// Decrypt username
						byte[] nonceU = request.EncryptionData.BuildReceiveNonce(seqUsername);
						byte[] aadU = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadU, (byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqUsername);
						decryptedUsername = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceU, request.EncryptedUsername, aadU);

						// Decrypt email
						byte[] nonceE = request.EncryptionData.BuildReceiveNonce(seqEmail);
						byte[] aadE = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadE, (byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqEmail);
						decryptedEmail = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceE, request.EncryptedEmail, aadE);

						// Decrypt age
						byte[] nonceA = request.EncryptionData.BuildReceiveNonce(seqAge);
						byte[] aadA = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadA, (byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqAge);
						decryptedAge = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceA, request.EncryptedAge, aadA);

						// Decrypt salt
						byte[] nonceS = request.EncryptionData.BuildReceiveNonce(seqSalt);
						byte[] aadS = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadS, (byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqSalt);
						decryptedSalt = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceS, request.EncryptedSalt, aadS);

						// Decrypt verifier
						byte[] nonceV = request.EncryptionData.BuildReceiveNonce(seqVerifier);
						byte[] aadV = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadV, (byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqVerifier);
						decryptedVerifier = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceV, request.EncryptedVerifier, aadV);
					}
					catch (CryptographicException)
					{
						NetworkConnection failConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
				#endregion

				#region Validate
					string username;
					string email;
					int age;
					try
					{
						username = CryptoHelper.StrictUtf8.GetString(decryptedUsername);
						email = CryptoHelper.StrictUtf8.GetString(decryptedEmail);
						string ageStr = CryptoHelper.StrictUtf8.GetString(decryptedAge);
						if (!int.TryParse(ageStr, out age))
						{
							CryptographicOperations.ZeroMemory(decryptedUsername);
							CryptographicOperations.ZeroMemory(decryptedEmail);
							CryptographicOperations.ZeroMemory(decryptedAge);
							CryptographicOperations.ZeroMemory(decryptedSalt);
							CryptographicOperations.ZeroMemory(decryptedVerifier);
							NetworkConnection failConn = request.Connection;
							TryEnqueueMainThread(() =>
							{
								if (failConn != null && failConn.IsActive)
									failConn.Disconnect(false);
							});
							return;
						}
					}
					catch (DecoderFallbackException)
					{
						CryptographicOperations.ZeroMemory(decryptedUsername);
						CryptographicOperations.ZeroMemory(decryptedEmail);
						CryptographicOperations.ZeroMemory(decryptedAge);
						CryptographicOperations.ZeroMemory(decryptedSalt);
						CryptographicOperations.ZeroMemory(decryptedVerifier);
						NetworkConnection failConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperations.ZeroMemory(decryptedUsername);
					CryptographicOperations.ZeroMemory(decryptedEmail);
					CryptographicOperations.ZeroMemory(decryptedAge);

					// Validate decrypted username against centralized naming rules before any DB work.
					if (!Authentication.IsAllowedUsername(username))
					{
						result = ClientAuthenticationResult.InvalidUsernameOrPassword;

						// Marshal early rejection — skip DB call entirely
						NetworkConnection earlyConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (earlyConn != null && earlyConn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(earlyConn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.InvalidUsernameOrPassword },
									false, Channel.Reliable);
							}
						});
						return;
					}

					// Validate email against centralized rules.
					if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !Authentication.IsAllowedEmailUsername(email))
					{
						result = ClientAuthenticationResult.InvalidUsernameOrPassword;

						NetworkConnection earlyConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (earlyConn != null && earlyConn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(earlyConn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.InvalidUsernameOrPassword },
									false, Channel.Reliable);
							}
						});
						return;
					}

					// Validate age range.
					if (age < 0 || age > 200)
					{
						result = ClientAuthenticationResult.InvalidUsernameOrPassword;

						NetworkConnection earlyConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (earlyConn != null && earlyConn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(earlyConn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.InvalidUsernameOrPassword },
									false, Channel.Reliable);
							}
						});
						return;
					}

					string salt;
					string verifier;
					try
					{
						salt = CryptoHelper.StrictUtf8.GetString(decryptedSalt);
						verifier = CryptoHelper.StrictUtf8.GetString(decryptedVerifier);
					}
					catch (DecoderFallbackException)
					{
						CryptographicOperations.ZeroMemory(decryptedSalt);
						CryptographicOperations.ZeroMemory(decryptedVerifier);
						// email+age already zeroed above
						NetworkConnection failConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperations.ZeroMemory(decryptedSalt);
					CryptographicOperations.ZeroMemory(decryptedVerifier);

					// Validate decrypted salt/verifier lengths before any DB work.
					if (salt.Length > MaxSaltLength || verifier.Length > MaxVerifierLength)
					{
						NetworkConnection earlyConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (earlyConn != null && earlyConn.IsActive)
							{
								Server.NetworkWrapper.Broadcast(earlyConn,
									new ClientAuthResultBroadcast() { Result = ClientAuthenticationResult.InvalidUsernameOrPassword },
									false, Channel.Reliable);
							}
						});
						return;
					}

					#endregion

					#region Persist & PostCreate
					// Database operation via registry-resolved service (BaseService handles context lifecycle)
					DatabaseResult dbResult = await accountService.PersistAsync(username, salt, verifier, email, age);

					// Update statistics
					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) &&
						Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
					{
						if (dbResult.IsSuccess)
						{
							result = ClientAuthenticationResult.AccountCreated;
							runtimeData.IncrementProcessed();
							// Consume one slot from the global hourly creation budget.
							// This runs AFTER successful persistence so failed requests
							// (duplicate username, validation errors, DB faults) do not
							// deplete the shared budget — an attacker cannot exhaust the
							// cap by sending bad registrations.
							IncrementGlobalCreationCount(DateTime.UtcNow);
							// Clear failure tracker on success
							mappingData.IpFailureTracker.TryRemove(request.IpAddress, out _);

							// Determine whether to auto-verify (skip 2FA/email).
							// Controlled by a compile-time guard AND a runtime AutoVerifyAccounts config flag.
							// Account is created, immediately verified, and has no TOTP requirement when enabled.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
							// In development builds, check the runtime AutoVerifyAccounts config.
							// Default to true when the config key is absent (dev convention).
							bool shouldAutoVerify = false;
							if (Server.Configuration.TryGetString("AutoVerifyAccounts", out string autoVerifyStr))
							{
								bool.TryParse(autoVerifyStr, out shouldAutoVerify);
							}
							else
							{
								shouldAutoVerify = true;
							}
#else
							bool shouldAutoVerify = false;
#endif
							if (shouldAutoVerify)
							{
								result = ClientAuthenticationResult.AccountVerified;
							}
							else
							{
							// Release mode: full 2FA setup + verification code delivered in-band.
							// Generate and store a verification code.
							int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
							// 24 hour TTL: long enough for users to act, short enough that an
							// exposed code cannot be re-used indefinitely.
							DateTime verifyExpiresUtc = DateTime.UtcNow.AddHours(24);
							DatabaseResult verifyResult = await accountService.PersistVerifyCodeAsync(username, verifyCode, verifyExpiresUtc);
							if (!verifyResult.IsSuccess)
							{
								await Log.Warning("AccountCreationSystem", $"PersistVerifyCodeAsync DB error for user '{username}': {verifyResult.ErrorCode} - {verifyResult.ErrorMessage}");
							}


							// Enqueue verification email for SMTP delivery.
							// The background processor will pick this up and send via the configured SMTP server.
							if (Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueueService))
							{
								// Prevent duplicate emails: skip if a pending email already exists for this user.
								var dupCheck = await emailQueueService.HasPendingForUserAsync(username);
								if (dupCheck.IsSuccess && dupCheck.Data)
								{
									await Log.Debug("AccountCreationSystem", $"Skipping duplicate verification email for '{username}' — a pending email already exists.");
								}
								else
								{
									string emailSubject = "FishMMO - Verify Your Account";
									string emailBody = BuildVerificationEmailBody(username, verifyCode);
									DatabaseResult emailResult = await emailQueueService.EnqueueAsync(email, username, emailSubject, emailBody);
									if (!emailResult.IsSuccess)
									{
										await Log.Warning("AccountCreationSystem", $"Failed to enqueue verification email for '{username}': {emailResult.ErrorCode} - {emailResult.ErrorMessage}");
									}
								}
							}
							else
							{
								await Log.Warning("AccountCreationSystem", $"IEmailQueueService not registered — verification email for '{username}' not enqueued.");
							}
							// Generate and store mandatory 2FA setup.
							// Snapshot TotpMasterKey to prevent a TOCTOU race.
							byte[] totpMasterKeySnapshot = TotpMasterKey;
							if (totpMasterKeySnapshot != null && totpMasterKeySnapshot.Length == 32)
							{
								try
								{
									byte[] totpSecret = CryptoHelper.TwoFactor.GenerateTotpSecret();

									// Encrypt for DB at-rest storage.
									string encryptedTotpSecret = CryptoHelper.TwoFactor.EncryptTotpSecret(totpMasterKeySnapshot, username, totpSecret);
										DatabaseResult totpSecretResult = await accountService.PersistTotpSecretAsync(username, encryptedTotpSecret);
										if (!totpSecretResult.IsSuccess)
										{
											await Log.Warning("AccountCreationSystem", $"PersistTotpSecretAsync DB error for user '{username}': {totpSecretResult.ErrorCode} - {totpSecretResult.ErrorMessage}");
											// Do NOT enable TOTP when the secret failed to persist.
											// An account with totp_enabled=true but no valid secret is
											// permanently locked out — VerifyTotpCodeCoreAsync checks
											// IsNullOrEmpty(TotpSecret) and returns false.
										}
										else
										{
											DatabaseResult totpEnabledResult = await accountService.PersistTotpEnabledAsync(username, true);
											if (!totpEnabledResult.IsSuccess)
											{
												await Log.Warning("AccountCreationSystem", $"PersistTotpEnabledAsync DB error for user '{username}': {totpEnabledResult.ErrorCode} - {totpEnabledResult.ErrorMessage}");
											}
										}
									// Generate and hash recovery codes (best-effort).
									// TOTP setup proceeds even if recovery code persistence fails —
									// the user can still use their authenticator app without recovery.
									string[] recoveryCodes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
									var codeHashes = new List<string>(recoveryCodes.Length);
									foreach (string code in recoveryCodes)
									{
										codeHashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
									}
									if (Server.Database.ServiceRegistry.TryGet<ITwoFactorRecoveryCodeService>(out var recoveryCodeService))
									{
											DatabaseResult recoveryResult = await recoveryCodeService.PersistManyAsync(username, codeHashes);
											if (!recoveryResult.IsSuccess)
											{
												await Log.Warning("AccountCreationSystem", $"PersistManyAsync recovery codes DB error for user '{username}': {recoveryResult.ErrorCode} - {recoveryResult.ErrorMessage}");
											}
									}
									else
									{
										await Log.Warning("AccountCreationSystem", $"ITwoFactorRecoveryCodeService not registered — recovery codes for '{username}' not persisted.");
									}
									// Build otpauth URI for the client's authenticator app.
									// The TwoFactorSetupBroadcast is ALWAYS sent when TOTP is
									// configured, regardless of recovery code persistence.
									string otpauthUri = CryptoHelper.TwoFactor.BuildOtpauthUri(totpSecret, username);

									// Encrypt setup data with the session key for secure transport to client.
									byte[] otpauthUriBytes = Encoding.UTF8.GetBytes(otpauthUri);
									byte[] recoveryCodesBytes = Encoding.UTF8.GetBytes(string.Join("\n", recoveryCodes));

									uint seq1 = request.EncryptionData.NextSendSequence();
									byte[] nonce1 = request.EncryptionData.BuildSendNonce(seq1);
									byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, request.EncryptionData.AgreedVersion, seq1);
									byte[] encOtpauthUri = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, nonce1, otpauthUriBytes, aad1);

									uint seq2 = request.EncryptionData.NextSendSequence();
									byte[] nonce2 = request.EncryptionData.BuildSendNonce(seq2);
									byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, request.EncryptionData.AgreedVersion, seq2);
									byte[] encRecoveryCodes = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, nonce2, recoveryCodesBytes, aad2);

									// Zeroize plaintext secrets.
									CryptographicOperations.ZeroMemory(totpSecret);
									CryptographicOperations.ZeroMemory(otpauthUriBytes);
									CryptographicOperations.ZeroMemory(recoveryCodesBytes);

									// Capture for main-thread dispatch.
									byte[] capturedEncUri = encOtpauthUri;
									byte[] capturedEncCodes = encRecoveryCodes;
									uint capturedSetupSeq = seq2;
									string capturedUsername = username;
									NetworkConnection setupConn = request.Connection;

									TryEnqueueMainThread(() =>
									{
										if (setupConn != null && setupConn.IsActive)
										{
											Server.NetworkWrapper.Broadcast(setupConn,
												new TwoFactorSetupBroadcast()
												{
													OtpauthUri = capturedEncUri,
													RecoveryCodes = capturedEncCodes,
													Seq = capturedSetupSeq,
												}, false, Channel.Reliable);
										}
									});
								}
								catch (Exception tfaEx)
								{
									// Account was created but 2FA setup failed. The account exists
									// without an active TOTP secret, so login will not require 2FA
									// until the user reconfigures it. Log at Error level for ops visibility.
									await Log.Error("AccountCreationSystem", $"2FA setup failed for {username} (account created without 2FA): {tfaEx}");
								}
							}
							}
						}
						else
						{
							// Map database error codes to client-facing results.
							//
							// All foreseeable validation/uniqueness branches are collapsed to
							// InvalidUsernameOrPassword so the client (and any on-the-wire
							// observer) cannot distinguish "username taken" from "format invalid"
							// and enumerate accounts via the registration endpoint. Genuine server
							// faults still surface as ServerBusy because the client needs to know
							// to back off and retry.
							result = dbResult.ErrorCode switch
							{
								DatabaseErrorCodes.UniqueViolation => ClientAuthenticationResult.InvalidUsernameOrPassword,
								DatabaseErrorCodes.ValidationError => ClientAuthenticationResult.InvalidUsernameOrPassword,
								_ => ClientAuthenticationResult.ServerBusy,
							};

							// Fail-closed: when the IP failure tracker is at capacity we cannot
							// safely record another failure, so disconnect the offender immediately
							// rather than silently skipping the increment (which would let an
							// attacker stay just under the per-IP block threshold indefinitely).
							if (!TryTrackIpFailure(mappingData, request.IpAddress))
							{
								NetworkConnection capacityConn = request.Connection;
								TryEnqueueMainThread(() =>
								{
									if (capacityConn != null && capacityConn.IsActive)
										capacityConn.Disconnect(true);
								});
							}
							runtimeData.IncrementRejected();
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("AccountCreationSystem", $"Error during account creation processing: {ex}");
					result = ClientAuthenticationResult.InvalidUsernameOrPassword;

					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
					{
						runtimeData.IncrementFailed();
					}

					// Track failure against IP for blocking (e.g., garbage-payload decryption exceptions).
					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var failMappingData))
					{
						TryTrackIpFailure(failMappingData, request.IpAddress);
					}
				}
			}

			#endregion

			#region Response
			// Marshal response back to main thread - FishNet Broadcast is not thread-safe
			ClientAuthenticationResult capturedResult = result;
			NetworkConnection capturedConn = request.Connection;
			TryEnqueueMainThread(() =>
			{
				if (capturedConn != null && capturedConn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(capturedConn,
						new ClientAuthResultBroadcast() { Result = capturedResult },
						false, Channel.Reliable);
				}
			});
		}
		#endregion

		/// <summary>
		/// Drains the main-thread response queue each frame and performs periodic maintenance.
		/// All network operations from async workers are marshalled through this queue
		/// to ensure they execute on the main Unity thread.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			CleanUpMappingData(deltaTime);
		}

		/// <summary>
		/// Processes pending emails from the outbound queue via the configured SMTP service.
		/// Called every frame; gated by <see cref="emailSendIntervalSeconds"/>.
		/// </summary>
		private void ProcessEmailQueue(float deltaTime)
		{
			if (emailSendIntervalSeconds <= 0f) return;
			emailSendTimer += deltaTime;
			if (emailSendTimer < emailSendIntervalSeconds) return;
			emailSendTimer = 0f;

			if (Server?.Database?.ServiceRegistry == null) return;
			if (!Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueueService)) return;

			// Thread-safe lazy construction of the SMTP service from server configuration.
			// Uses double-checked locking to ensure only one instance is created.
			if (smtpService == null)
			{
				lock (smtpServiceLock)
				{
					if (smtpService == null && Server.Configuration != null)
					{
						smtpService = new FishMMO.Server.Implementation.Smtp.SmtpService(Server.Configuration);
					}
				}
			}
			if (smtpService == null) return;

			// Resolve server identity for claim tracking so multiple LoginServers
			// can safely share the email queue via FOR UPDATE SKIP LOCKED.
			string serverName = Server.Configuration?.GetString("ServerName", "unknown") ?? "unknown";

			// Fire-and-forget: process one email per sweep to avoid blocking the main thread.
			_ = ProcessNextEmailAsync(emailQueueService, serverName);
		}

		/// <summary>
		/// Dequeues and sends the next pending email from the queue.
		/// </summary>
		private async Task ProcessNextEmailAsync(IEmailQueueService emailQueueService, string claimedBy)
		{
			try
			{
				var result = await emailQueueService.DequeueNextAsync(claimedBy);
				if (!result.IsSuccess) return;

				var email = result.Data;
				bool sent = await smtpService.SendEmailAsync(email.RecipientEmail, email.Subject, email.Body);
				if (sent)
				{
					await emailQueueService.MarkSentAsync(email.ID);

					// Mark the account so login is blocked until the user verifies.
					// Before this point (VerificationEmailSentAt is null), unverified
					// accounts enjoy a grace period and can log in freely.
					if (Server?.Database?.ServiceRegistry != null &&
						Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
					{
						var persistResult = await accountService.PersistVerificationEmailSentAsync(email.RecipientUsername);
						if (!persistResult.IsSuccess)
						{
							await Log.Warning("AccountCreationSystem", $"Failed to mark verification email sent for '{email.RecipientUsername}': {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
						}
					}

					await Log.Debug("AccountCreationSystem", $"Verification email sent to {email.RecipientEmail} for '{email.RecipientUsername}'.");
				}
				else
				{
					await emailQueueService.MarkFailedAsync(email.ID, "SMTP send returned false.");
				}
			}
			catch (Exception ex)
			{
				await Log.Warning("AccountCreationSystem", $"Email queue processing error: {ex.Message}");
			}
		}

		/// <summary>
		/// Periodically cleans up stale IP rate-limit and failure-tracking entries
		/// to prevent unbounded memory growth from one-time visitors.
		/// Iterating a ConcurrentDictionary creates a point-in-time snapshot, so this is safe.
		/// </summary>
		private void CleanUpMappingData(float deltaTime)
		{
			ProcessEmailQueue(deltaTime);
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			// Float accumulation of deltaTime is acceptable here: a 60-second
			// interval resets to 0 each cycle, so precision loss is negligible.
			runtimeData.CleanupTimer += deltaTime;
			if (runtimeData.CleanupTimer < 60f)
			{
				return;
			}
			runtimeData.CleanupTimer = 0f;

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
			{
				return;
			}

			DateTime cutoff = DateTime.UtcNow.AddSeconds(-ipBlockDurationSeconds);

			// Evict rate-limit entries older than the block duration.
			CleanupExpiredEntries(mappingData.IpRateLimitTracker,
				entry => entry.Value < cutoff,
				cleanupMaxScanPerMap,
				cleanupMaxRemovalsPerMap);

			// Evict failure-tracking entries for IPs whose block period has expired.
			// Once the rate-limit entry is gone (expired above), the failure count serves no purpose.
			// Snapshot keys first to avoid mutating the dictionary during enumeration.
			// Uses key-only TryRemove: the rate-limit entry being expired is sufficient
			// justification for removal. A concurrent request that created a fresh failure
			// entry after the rate-limit expired will simply re-create the entry.
			int scannedFailures = 0;
			int removedFailures = 0;
			var failureKeysToRemove = new System.Collections.Generic.List<string>();
			foreach (var entry in mappingData.IpFailureTracker)
			{
				if (scannedFailures >= cleanupMaxScanPerMap)
					break;
				scannedFailures++;
				if (!mappingData.IpRateLimitTracker.ContainsKey(entry.Key))
				{
					failureKeysToRemove.Add(entry.Key);
				}
			}

			foreach (var key in failureKeysToRemove)
			{
				if (removedFailures >= cleanupMaxRemovalsPerMap)
					break;
				if (mappingData.IpFailureTracker.TryRemove(key, out _))
				{
					removedFailures++;
				}
			}

			// Evict stale per-connection caches as a backstop against delayed disconnect events.
			TimeSpan cacheTtl = TimeSpan.FromSeconds(Math.Max(1f, ipBlockDurationSeconds));
			runtimeData.ConnectionIpCache.SweepExpired(DateTime.UtcNow, cacheTtl, cleanupMaxScanPerMap, cleanupMaxRemovalsPerMap);
			runtimeData.ConnectionEncryptionCache.SweepExpired(DateTime.UtcNow, cacheTtl, cleanupMaxScanPerMap, cleanupMaxRemovalsPerMap);

			// Evict expired per-username verification failure entries.
			SweepExpiredVerifyUsernameFailures();
		}

		/// <summary>
		/// Performs bounded, lock-free cleanup over a concurrent dictionary using <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/>.
		/// </summary>
		private static void CleanupExpiredEntries<TKey, TValue>(
			ConcurrentDictionary<TKey, TValue> map,
			Func<KeyValuePair<TKey, TValue>, bool> isExpired,
			int maxScan,
			int maxRemove)
		{
			if (map == null || map.Count == 0 || maxScan <= 0 || maxRemove <= 0)
			{
				return;
			}

			int scanned = 0;
			int removed = 0;
			foreach (KeyValuePair<TKey, TValue> entry in map)
			{
				scanned++;
				if (isExpired(entry) && map.TryRemove(entry.Key, out _))
				{
					removed++;
				}

				if (scanned >= maxScan || removed >= maxRemove)
				{
					break;
				}
			}
		}

		/// <summary>
		/// Drains the main-thread queue via the base class generic helper.
		/// Uses time-slicing during normal updates and full drain during shutdown.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IAccountCreationSystemMainThreadQueueData>(maxMainThreadResponsesPerFrame, drainAll);
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the base class generic helper.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IAccountCreationSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Removes cached connection-IP mapping when a connection closes.
		/// </summary>
		protected override void OnRemoteConnectionStopped(NetworkConnection conn)
		{
			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.ConnectionIpCache.Remove(conn.ClientId);
				runtimeData.ConnectionEncryptionCache.Remove(conn.ClientId);
			}
		}

		/// <summary>
		/// Resolves a stable key for rate-limiting while avoiding repeated address string allocations.
		///
		/// <para><b>Proxy / NAT / Load Balancer Limitation:</b></para>
		/// <para>
		/// The default mode uses <c>conn.GetAddress()</c> which returns the transport-level (TCP/UDP)
		/// source IP. When the server sits behind a reverse proxy, NAT gateway, or cloud load balancer,
		/// <b>all</b> clients share the proxy's single IP address. This causes false-positive rate
		/// limiting: one client's request can block every other client behind the same proxy.
		/// </para>
		///
		/// <para><b>Mitigation options (ordered by robustness):</b></para>
		/// <list type="number">
		///   <item>
		///     <description>
		///       <b>PROXY protocol support at the transport layer</b> – configure the proxy to prepend
		///       the real client IP via PROXY protocol v1/v2. The transport must parse and expose the
		///       original IP so <c>conn.GetAddress()</c> returns it natively.
		///     </description>
		///   </item>
		///   <item>
		///     <description>
		///       <b>Application-level client fingerprinting</b> – use a combination of connection ID,
		///       handshake data, or encrypted client tokens to produce a per-client key that does not
		///       depend on source IP.
		///     </description>
		///   </item>
		///   <item>
		///     <description>
		///       <b>Configurable trusted-proxy list</b> – maintain a whitelist of known proxy IPs.
		///       When the source IP matches a trusted proxy, switch to an alternative key
		///       (e.g., connection ID or forwarded header).
		///     </description>
		///   </item>
		/// </list>
		///
		/// <para>
		/// For direct connections (no proxy), the current implementation is correct.
		/// When connection-ID keying was enabled, this method returns
		/// <c>conn.ClientId.ToString()</c> as a proxy-compatible fallback key.
		/// Operators must be aware that connection-ID keying trades IP-level aggregation
		/// for per-socket granularity, which may be less effective against distributed attacks
		/// but avoids false-positive blocking behind proxies.
		/// </para>
		/// </summary>
		/// <param name="conn">The network connection to resolve a rate-limit key for.</param>
		/// <returns>A stable string key for IP-based or connection-based rate limiting.</returns>
		/// <summary>
		/// Resolves the real client IP for rate limiting. Requires the IP to have
		/// been recovered from a verified connection token. Returns null if the IP
		/// is not available — callers MUST reject the request.
		/// Never falls back to proxy IP or ClientId.
		/// </summary>
		private string? ResolveIpAddress(NetworkConnection conn)
		{
			if (conn == null) return null;
			if (Server?.DataContainerRegistry != null &&
				Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var rt) &&
				rt.ConnectionIpCache != null &&
				rt.ConnectionIpCache.TryGetAndTouch(conn.ClientId, DateTime.UtcNow, out string? realIp))
			{
				return HandshakeService.NormalizeIp(realIp);
			}
			return null;
		}
		private bool ResolveEncryptionData(NetworkConnection conn, out ConnectionEncryptionData encryptionData)
		{
			encryptionData = null;
			if (conn == null)
			{
				return false;
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				return Server.AccountManager.GetConnectionEncryptionData(conn, out encryptionData) && encryptionData != null;
			}

			DateTime now = DateTime.UtcNow;
			if (runtimeData.ConnectionEncryptionCache.TryGetAndTouch(conn.ClientId, now, out ConnectionEncryptionData cached) &&
				cached != null)
			{
				// Validate the cache hit against the authoritative AccountManager data.
				// After a disconnect+reconnect, the same ClientId may map to a fresh
				// ConnectionEncryptionData. Serving a stale cache entry would cause
				// decryption failures (wrong keys) and nonce desync.
				if (Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData authoritative) &&
					ReferenceEquals(cached, authoritative))
				{
					encryptionData = cached;
					return true;
				}

				// Stale cache hit — evict and fall through to re-fetch.
				runtimeData.ConnectionEncryptionCache.Remove(conn.ClientId);
			}

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out encryptionData) || encryptionData == null)
			{
				return false;
			}

			runtimeData.ConnectionEncryptionCache.Upsert(conn.ClientId, encryptionData, now);
			return true;
		}

		/// <summary>
		/// UDP gate: Receives AccountVerify broadcast, validates connection, and enqueues
		/// encrypted data for async processing. Zero blocking — no decryption on network thread.
		/// </summary>
		private void OnServerAccountVerifyBroadcastReceived(NetworkConnection conn, AccountVerifyBroadcast msg, Channel channel)
		{
			// Already authenticated — verification is meaningless. Disconnect to prevent abuse.
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			if (!ResolveEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Reject oversized payloads before any allocation or decryption.
			if (msg.Username == null || msg.Username.Length > MaxEncryptedFieldSize ||
				msg.VerifyCode == null || msg.VerifyCode.Length > MaxEncryptedFieldSize)
			{
				conn.Disconnect(true);
				return;
			}

			string ipAddress = ResolveIpAddress(conn);

			// Reuse account creation rate limiting for verification attempts.
			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
			{
				if (mappingData.IpFailureTracker.TryGetValue(ipAddress, out int failureCount) &&
					failureCount >= maxFailedAttempts)
				{
					conn.Disconnect(true);
					return;
				}
			}

			// Per-username brute-force protection: prevents distributed attacks from
			// bypassing per-IP limits by rotating source IPs.
			// Username is still encrypted here, so we defer the actual check to
			// ProcessAccountVerifyAsync after decryption. The gate here only checks
			// the IP-based limit; the username-based check runs asynchronously.

			if (TryEnqueueAsyncWork(() => ProcessAccountVerifyAsync(conn, msg.Username, msg.VerifyCode, encryptionData, ipAddress, msg.Seq), conn.ClientId))
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ClientAuthResultBroadcast()
			{
				Result = ClientAuthenticationResult.ServerBusy
			}, false, Channel.Unreliable);
		}

		/// <summary>
		/// Processes a single account verification request asynchronously.
		/// Decrypts username and verify code, then validates via PersistVerifiedAsync.
		/// </summary>
		/// <remarks>
		/// <b>CancellationToken:</b> This method does not currently accept a CancellationToken.
		/// The underlying <see cref="TryEnqueueAsyncWork"/> infrastructure dispatches bare
		/// <c>Func&lt;Task&gt;</c> delegates. If the base infrastructure is extended to pass
		/// per-operation tokens (e.g., linked to server shutdown), this method should propagate
		/// that token into its DB calls (<c>PersistVerifiedAsync</c>) to enable cooperative
		/// cancellation during graceful shutdown.
		/// </remarks>
		private async Task ProcessAccountVerifyAsync(
			NetworkConnection conn,
			byte[] encryptedUsername,
			byte[] encryptedVerifyCode,
			ConnectionEncryptionData encryptionData,
			string ipAddress,
			uint seq)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			if (Server.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				try
				{
					byte[] decryptedUsername;
					byte[] decryptedVerifyCode;
					try
					{
						// Guard: ValidateSequenceRange ensures seq is large enough for the 2-field
						// protocol encoding (seq-1: username, seq: verify code) without uint underflow.
						if (!CryptoHelper.ValidateSequenceRange(seq, 2))
						{
							NetworkConnection failConn = conn;
							TryEnqueueMainThread(() =>
							{
								if (failConn != null && failConn.IsActive)
									failConn.Disconnect(false);
							});
							return;
						}

						// Expected order: username (seq-1), verifyCode (seq)
						uint seqUsername = seq - 1;
						uint seqCode = seq;

						// Atomic 2-sequence consume.
						if (!encryptionData.TryConsumeReceiveSequenceRange(seqUsername, 2))
							throw new CryptographicException("Account verify sequence range out-of-order or duplicate.");

						byte[] nonceU = encryptionData.BuildReceiveNonce(seqUsername);
						byte[] aadU = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadU, (byte)CryptoHelper.AuthMessageType.AccountVerify, encryptionData.AgreedVersion, seqUsername);
						decryptedUsername = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey, nonceU, encryptedUsername, aadU);

						byte[] nonceC = encryptionData.BuildReceiveNonce(seqCode);
						byte[] aadC = new byte[CryptoHelper.AadLength];
						CryptoHelper.WriteAad(aadC, (byte)CryptoHelper.AuthMessageType.AccountVerify, encryptionData.AgreedVersion, seqCode);
						decryptedVerifyCode = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey, nonceC, encryptedVerifyCode, aadC);
					}
					catch (CryptographicException)
					{
						NetworkConnection failConn = conn;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}

					string username;
					int verifyCode;
					try
					{
						username = CryptoHelper.StrictUtf8.GetString(decryptedUsername);
						string codeStr = CryptoHelper.StrictUtf8.GetString(decryptedVerifyCode);
						if (!int.TryParse(codeStr, out verifyCode))
						{
							CryptographicOperations.ZeroMemory(decryptedUsername);
							CryptographicOperations.ZeroMemory(decryptedVerifyCode);
							NetworkConnection failConn = conn;
							TryEnqueueMainThread(() =>
							{
								if (failConn != null && failConn.IsActive)
									failConn.Disconnect(false);
							});
							return;
						}
					}
					catch (DecoderFallbackException)
					{
						CryptographicOperations.ZeroMemory(decryptedUsername);
						CryptographicOperations.ZeroMemory(decryptedVerifyCode);
						NetworkConnection failConn = conn;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperations.ZeroMemory(decryptedUsername);
					CryptographicOperations.ZeroMemory(decryptedVerifyCode);

					if (!Authentication.IsAllowedUsername(username))
					{
						result = ClientAuthenticationResult.InvalidUsernameOrPassword;
					}
					else
					{
						// Per-username brute-force check. Uses the CAS-style
						// TryRemove(KeyValuePair) overload so an expired entry is only
						// evicted if no concurrent thread has updated it in the meantime
						// — prevents losing a fresh failure counter to a stale eviction.
						// Use NFKC + invariant-case normalisation so confusable Unicode
						// usernames cannot bypass the per-username lockout.
						string userKey = Authentication.NormalizeAccountLookup(username);
						if (verifyUsernameFailures.TryGetValue(userKey, out var failInfo))
						{
							if (DateTime.UtcNow - failInfo.FirstFailure > VerifyUsernameLockoutDuration)
							{
								// Window expired — try to evict, but only if the entry we
								// observed is the one still in the map. If a concurrent
								// TrackVerifyUsernameFailure has already reset it, the CAS
								// fails and we leave their fresh entry intact.
								// NOTE: ConcurrentDictionary.TryRemove(KeyValuePair) is not available
								// in Unity's runtime. The ICollection<KVP>.Remove fallback is fragile --
								// verify after Unity runtime upgrades.
								((ICollection<KeyValuePair<string, (int Count, DateTime FirstFailure)>>)verifyUsernameFailures)
									.Remove(new KeyValuePair<string, (int Count, DateTime FirstFailure)>(userKey, failInfo));
							}
							else if (failInfo.Count >= MaxVerifyFailuresPerUsername)
							{
								// Locked out — reject immediately.
								result = ClientAuthenticationResult.InvalidUsernameOrPassword;
								goto trackFailure;
							}
						}

						DatabaseResult dbResult = await accountService.PersistVerifiedAsync(username, verifyCode);
						result = dbResult.IsSuccess
							? ClientAuthenticationResult.AccountVerified
							: ClientAuthenticationResult.InvalidUsernameOrPassword;
					}

				trackFailure:
					// Track failures for rate limiting.
					if (result != ClientAuthenticationResult.AccountVerified)
					{
						// Per-IP failure tracking. Fail-closed: when the tracker is at
						// capacity, disconnect the offender immediately so they cannot stay
						// just under the per-IP block threshold.
						if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData)
							&& !TryTrackIpFailure(mappingData, ipAddress))
						{
							NetworkConnection capacityConn = conn;
							TryEnqueueMainThread(() =>
							{
								if (capacityConn != null && capacityConn.IsActive)
									capacityConn.Disconnect(true);
							});
							return;
						}

						// Per-username failure tracking.
						TrackVerifyUsernameFailure(username);
					}
				}
				catch (Exception ex)
				{
					await Log.Error("AccountCreationSystem", $"Error during account verification: {ex}");
					result = ClientAuthenticationResult.InvalidUsernameOrPassword;
				}
			}

			ClientAuthenticationResult capturedResult = result;
			NetworkConnection capturedConn = conn;
			TryEnqueueMainThread(() =>
			{
				if (capturedConn != null && capturedConn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(capturedConn,
						new ClientAuthResultBroadcast() { Result = capturedResult },
						false, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// Tracks a failure against <paramref name="ipAddress"/> in the per-IP failure
		/// counter. Returns <c>false</c> when the tracker is at capacity AND the IP
		/// is not already present — the caller should treat that as a fail-closed
		/// signal (disconnect) rather than silently ignoring the failure, otherwise
		/// an attacker can exhaust the tracker to escape the per-IP block.
		/// Existing IPs are always incremented regardless of capacity.
		/// </summary>
		private static bool TryTrackIpFailure(IAccountCreationSystemMappingData mappingData, string ipAddress)
		{
			if (mappingData == null || string.IsNullOrEmpty(ipAddress))
				return true;

			// NOTE: Capacity check is 'racy by design' — under flood, the dictionary may
			// temporarily exceed MaxIpFailureTrackerEntries before the race resolves.
			// This is an acceptable probabilistic memory guard.
			if (mappingData.IpFailureTracker.Count >= MaxIpFailureTrackerEntries &&
				!mappingData.IpFailureTracker.ContainsKey(ipAddress))
			{
				return false;
			}
			mappingData.IpFailureTracker.AddOrUpdate(ipAddress, 1, (_, existing) => existing + 1);
			return true;
		}

		/// <summary>
		/// Tracks a verification failure for the given username in the per-username rate limiter.
		/// Atomically resets the (count, firstFailure) pair when an existing entry's lockout
		/// window has already elapsed — i.e., this single AddOrUpdate handles both the
		/// "first failure", "continuing failure within window", and "window expired, start a
		/// fresh window" cases without TOCTOU races against concurrent updates or sweeps.
		/// </summary>
		private void TrackVerifyUsernameFailure(string username)
		{
			string failKey = Authentication.NormalizeAccountLookup(username);
			if (string.IsNullOrEmpty(failKey))
				return;

			// Hard cap: reject new entries when the tracker is full to prevent
			// unbounded memory growth from unique-username flood attacks. Existing
			// entries are still incremented so a real lockout still applies even
			// when the tracker is at capacity.
			if (verifyUsernameFailures.Count >= MaxVerifyUsernameFailureEntries &&
				!verifyUsernameFailures.ContainsKey(failKey))
				return;

			DateTime now = DateTime.UtcNow;
			verifyUsernameFailures.AddOrUpdate(
				failKey,
				_ => (1, now),
				(_, existing) =>
				{
					// Window expired between observation and increment — start fresh
					// atomically so a concurrent sweep can't double-decrement us back
					// to zero or lose the increment entirely.
					if (now - existing.FirstFailure > VerifyUsernameLockoutDuration)
						return (1, now);
					return (existing.Count + 1, existing.FirstFailure);
				});
		}

		/// <summary>
		/// Evicts expired entries from <see cref="verifyUsernameFailures"/> whose lockout
		/// window has elapsed. Bounded scan to avoid stalling the main thread.
		/// </summary>
		private void SweepExpiredVerifyUsernameFailures()
		{
			DateTime now = DateTime.UtcNow;
			int scanned = 0;
			foreach (var kvp in verifyUsernameFailures)
			{
				if (++scanned > VerifyUsernameFailureSweepMaxScan)
					break;
				if (now - kvp.Value.FirstFailure > VerifyUsernameLockoutDuration)
				{
					verifyUsernameFailures.TryRemove(kvp.Key, out _);

				}
			}
		}

		/// <summary>
		/// Builds the HTML body for the account verification email.
		/// </summary>
		private static string BuildVerificationEmailBody(string username, int verifyCode)
		{
			return $@"<html><body style='font-family: Arial, sans-serif; color: #333;'>
				<h2>Welcome to FishMMO, {System.Net.WebUtility.HtmlEncode(username)}!</h2>
				<p>Thank you for creating an account. To complete your registration,
				please use the following verification code:</p>
				<h1 style='font-size: 32px; letter-spacing: 4px; color: #2563eb;'>{verifyCode:D6}</h1>
				<p>This code is valid for 24 hours. If you did not create this account,
				you can safely ignore this email.</p>
				<hr/>
				<p style='font-size: 12px; color: #999;'>— The FishMMO Team</p>
			</body></html>";
		}
	}
}