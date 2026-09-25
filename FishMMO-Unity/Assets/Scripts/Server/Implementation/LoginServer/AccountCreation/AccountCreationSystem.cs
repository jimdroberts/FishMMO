// Nullable annotations without null-state analysis. The authentication path annotates the
// references that are legitimately absent — an unestablished session, a challenge that never
// arrived — and Unity compiles this assembly with the nullable context off, so every one of those
// annotations raised CS8632. `annotations` alone enables them without switching on flow analysis,
// which would bury real warnings under hundreds more for code never written against it.
#nullable enable annotations

using FishNet.Connection;
using FishNet.Transporting;
using System;
using FishMMO.Server.Core.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
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

		/// <summary>Per-IP debounce for <see cref="AccountVerifyBroadcast"/>. See the handler.</summary>
		private readonly ExpiringKeyTracker<string> verifyRateLimiter = new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		private static readonly TimeSpan VerifyRateLimitDuration = TimeSpan.FromSeconds(1);

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
		/// Whether this LoginServer drains the outbound email queue. Resolved once, lazily.
		/// </summary>
		/// <remarks>
		/// <b>Off by default.</b> The Control Panel drains the queue now: it is an ASP.NET Core
		/// host with a real background service, so sending is not paced by a game server's frame
		/// time and blocking network I/O stays off this tick entirely. Accounts are still created
		/// here and mail is still ENQUEUED here — only the sending moved, and the queue row is
		/// the seam between the two.
		/// <para>
		/// Set <c>Smtp:DrainQueue=true</c> to put it back, for a deployment that runs no panel.
		/// Both draining at once is safe as far as the database goes — the claim is a
		/// <c>FOR UPDATE SKIP LOCKED</c> so no row is sent twice — but it puts the I/O back on
		/// the tick, which is the thing this change exists to remove.
		/// </para>
		/// </remarks>
		private bool? drainEmailQueue;
		/// <summary>
		/// Lazily-constructed SMTP sender. Null until first email queue sweep.
		/// </summary>
		private ISmtpService smtpService;
		/// <summary>
		/// Guard flag preventing concurrent in-flight email sends.
		/// ProcessNextEmailAsync is fire-and-forget without this guard;
		/// if the SendEmailAsync call takes longer than emailSendIntervalSeconds,
		/// a second sweep could overlap and send duplicate emails.
		/// </summary>
		private volatile int emailSendInFlight;
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
		[Tooltip("When true, uses conn.ClientId instead of remote IP for rate limiting. Safe only behind a trusted proxy.")]
		[SerializeField] private bool useConnectionIdForRateLimiting = false;

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
			if (useConnectionIdForRateLimiting)
			{
				Log.Warning("AccountCreationSystem",
					"Rate limiting is using ConnectionId instead of real client IP. " +
					"This is ONLY safe behind a trusted reverse proxy that sets X-Forwarded-For correctly.");
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
			verifyRateLimiter.SweepExpired(DateTime.UtcNow, maxScan: 64, maxRemove: 16);
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
				msg.Verifier == null || msg.Verifier.Length > MaxEncryptedFieldSize ||
				msg.Profile == null || msg.Profile.Length > AuthSizeLimits.MaxRegistrationProfileSize)
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
				msg.Profile,               // Still encrypted!
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
			if (request.IpAddress == null)
			{
				// Defense-in-depth: IpAddress should never be null here (the caller in
				// OnServerAccountVerifyBroadcastReceived guards against it), but if the
				// method is ever called from a different path, fail closed.
				NetworkConnection failConn = request.Connection;
				TryEnqueueMainThread(() =>
				{
					if (failConn != null && failConn.IsActive)
						failConn.Disconnect(false);
				});
				return;
			}

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
					RegistrationProfile submittedProfile;
					try
					{
						/* Six fields on six consecutive sequences — username, email, age, salt, verifier,
						 * profile — consumed atomically. The arithmetic lives in SrpService, beside the
						 * client-side encryption it has to mirror, rather than being counted out here. */
						SrpService.ServerDecryptRegistrationFields(
							request.EncryptionData,
							request.Seq,
							request.EncryptedUsername,
							request.EncryptedEmail,
							request.EncryptedAge,
							request.EncryptedSalt,
							request.EncryptedVerifier,
							request.EncryptedProfile,
							out decryptedUsername,
							out decryptedEmail,
							out decryptedAge,
							out decryptedSalt,
							out decryptedVerifier,
							out byte[] decryptedProfile);

						// Parsed at once so the personal details spend as little time as possible in a buffer.
						bool profileParsed = RegistrationProfile.TryDeserialize(decryptedProfile, out submittedProfile);
						CryptographicOperationsCompat.ZeroMemory(decryptedProfile);
						if (!profileParsed)
						{
							CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
							CryptographicOperationsCompat.ZeroMemory(decryptedEmail);
							CryptographicOperationsCompat.ZeroMemory(decryptedAge);
							CryptographicOperationsCompat.ZeroMemory(decryptedSalt);
							CryptographicOperationsCompat.ZeroMemory(decryptedVerifier);
							throw new CryptographicException("Malformed registration profile.");
						}
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
							CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
							CryptographicOperationsCompat.ZeroMemory(decryptedEmail);
							CryptographicOperationsCompat.ZeroMemory(decryptedAge);
							CryptographicOperationsCompat.ZeroMemory(decryptedSalt);
							CryptographicOperationsCompat.ZeroMemory(decryptedVerifier);
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
						CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
						CryptographicOperationsCompat.ZeroMemory(decryptedEmail);
						CryptographicOperationsCompat.ZeroMemory(decryptedAge);
						CryptographicOperationsCompat.ZeroMemory(decryptedSalt);
						CryptographicOperationsCompat.ZeroMemory(decryptedVerifier);
						NetworkConnection failConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
					CryptographicOperationsCompat.ZeroMemory(decryptedEmail);
					CryptographicOperationsCompat.ZeroMemory(decryptedAge);

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

					// Validate age range. Must be at least 13 (matching the client dropdown floor).
					if (age < 13 || age > 200)
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
						CryptographicOperationsCompat.ZeroMemory(decryptedSalt);
						CryptographicOperationsCompat.ZeroMemory(decryptedVerifier);
						// email+age already zeroed above
						NetworkConnection failConn = request.Connection;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperationsCompat.ZeroMemory(decryptedSalt);
					CryptographicOperationsCompat.ZeroMemory(decryptedVerifier);

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

					#region Profile & Beta Gate
					/* The optional details are validated by the database's own rules BEFORE the account row
					 * exists: refusing afterwards would leave an account behind that the player was told
					 * was not created. The client checks the same rules first, so this is a backstop. */
					AccountVerificationChannels chosenChannels = (AccountVerificationChannels)(byte)submittedProfile.VerificationChannels & AccountVerificationRules.All;
					if (chosenChannels == AccountVerificationChannels.None)
					{
						// Email is mandatory at registration anyway; "neither" means the default.
						chosenChannels = AccountVerificationChannels.Email;
					}
					var submittedDetails = new AccountProfileData
					{
						Phone = submittedProfile.Phone,
						RealName = submittedProfile.RealName,
						Country = submittedProfile.Country,
						Address = submittedProfile.Address,
						ReferralAccount = submittedProfile.ReferralAccount,
						DiscordUsername = submittedProfile.DiscordUsername,
						VerificationChannels = chosenChannels,
					};
					if (!AccountProfileRules.TryValidate(submittedDetails, out AccountProfileData cleanProfile, out string _))
					{
						// The reason is not logged: it quotes nothing, but it is about personal data.
						await Log.Debug("AccountCreationSystem", "Refused account creation: an optional detail failed the profile rules.");
						BroadcastEarlyResult(request.Connection, ClientAuthenticationResult.AccountDetailsInvalid);
						return;
					}

					/* A server that verifies at least one channel must be able to send the new account a code on one
					 * of them. Email is always given, so this refuses only when email verification is off and the player
					 * gave nothing an enabled channel can use (a Discord-only server and no Discord username). Refused
					 * here, before the row exists: an existing account in that position is let in at sign-in instead
					 * (AccountVerificationRules.IsWaived), because it cannot be asked, but a new one need not be made so. */
					if (!AccountVerificationPolicy.IsAutoVerifyEnabled(Server.Configuration))
					{
						AccountVerificationChannels enabledChannels = AccountVerificationPolicy.EnabledChannels(Server.Configuration);
						if (enabledChannels != AccountVerificationChannels.None &&
							AccountVerificationRules.Effective(cleanProfile.VerificationChannels, enabledChannels,
								AccountVerificationRules.Receivable(email, cleanProfile.Phone, cleanProfile.DiscordUsername)) == AccountVerificationChannels.None)
						{
							await Log.Debug("AccountCreationSystem", "Refused account creation: no verification channel this server uses can reach the account.");
							BroadcastEarlyResult(request.Connection, ClientAuthenticationResult.AccountDetailsInvalid);
							return;
						}
					}

					string betaCode = string.IsNullOrWhiteSpace(submittedProfile.BetaCode) ? null : submittedProfile.BetaCode.Trim();
					bool betaMode = LoginSecurityPolicy.IsBetaModeEnabled(Server.Configuration);
					if (betaMode)
					{
						ClientAuthenticationResult? betaRefusal = await CheckRegistrationBetaCodeAsync(betaCode);
						if (betaRefusal.HasValue)
						{
							if (betaRefusal.Value == ClientAuthenticationResult.BetaCodeInvalid &&
								Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var betaMappingData))
							{
								// A wrong code is a guess; the per-IP failure counter is what blocks guessing.
								TryTrackIpFailure(betaMappingData, request.IpAddress);
							}
							BroadcastEarlyResult(request.Connection, betaRefusal.Value);
							return;
						}
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

							/* The optional details, then the beta code. Both after the row exists and neither
							 * able to undo it: the account is real now. A profile that failed to write leaves
							 * the database's default channel (email), so verification falls back to email
							 * rather than asking for an SMS code to a number that was never stored. */
							DatabaseResult profileResult = await accountService.PersistProfileAsync(username, cleanProfile);
							if (!profileResult.IsSuccess)
							{
								await Log.Warning("AccountCreationSystem", $"PersistProfileAsync DB error for user '{username}': {profileResult.ErrorCode} - {profileResult.ErrorMessage}");
								chosenChannels = AccountVerificationChannels.Email;
							}
							if (betaCode != null)
							{
								await RedeemRegistrationBetaCodeAsync(username, betaCode, betaMode);
							}

							// Determine whether to auto-verify (skip 2FA/email).
							// Controlled by a compile-time guard AND a runtime AutoVerifyAccounts config flag;
							// see AccountVerificationPolicy, which the login path consults with the same rules.
							// Account is created, immediately verified, and has no TOTP requirement when enabled.
							bool shouldAutoVerify = AccountVerificationPolicy.IsAutoVerifyEnabled(Server.Configuration);
							if (shouldAutoVerify)
							{
								// Persist verified=true so the database agrees with the AccountVerified
								// response. Reporting verification to the client without writing the column
								// left the row unverified, and login then succeeded only for as long as the
								// account stayed inside the VerificationEmailSentAt grace period.
								DatabaseResult autoVerifyResult = await accountService.PersistAutoVerifiedAsync(username);
								if (!autoVerifyResult.IsSuccess)
								{
									await Log.Warning("AccountCreationSystem", $"PersistAutoVerifiedAsync DB error for user '{username}': {autoVerifyResult.ErrorCode} - {autoVerifyResult.ErrorMessage}");
								}
								result = ClientAuthenticationResult.AccountVerified;
							}
							else
							{
							// Release mode: a code on every chosen channel that is switched on, then the mandatory
							// 2FA setup below. AccountVerificationPolicy documents the precedence.
							result = await DeliverVerificationCodesAsync(accountService, username, email,
							profileResult.IsSuccess ? cleanProfile.Phone : null,
							profileResult.IsSuccess ? cleanProfile.DiscordUsername : null,
							chosenChannels);
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
									bool totpEnabled = false;
									DatabaseResult totpSecretResult = await accountService.PersistTotpSecretAsync(username, encryptedTotpSecret);
									if (!totpSecretResult.IsSuccess)
									{
										await Log.Error("AccountCreationSystem", $"PersistTotpSecretAsync DB error for user '{username}': [{totpSecretResult.ErrorCode}] {totpSecretResult.ErrorMessage}. Account created without 2FA.");
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
											await Log.Error("AccountCreationSystem", $"PersistTotpEnabledAsync DB error for user '{username}': [{totpEnabledResult.ErrorCode}] {totpEnabledResult.ErrorMessage}. Account created without 2FA.");
										}
										else
										{
											totpEnabled = true;
										}
									}

									/* Show the player only what the database will honour.
									 *
									 * The setup broadcast used to go out whatever the writes above did, so a
									 * failed secret or enable write still walked the player through scanning
									 * a QR code: they left believing the mandatory second factor was on, and
									 * sign-in — which asks for a code only when totp_enabled is set — never
									 * asked. With nothing enabled there is nothing to set up, so nothing is
									 * sent: the account is in the same state as the catch below describes,
									 * and the client's reply timeout already treats a missing setup message
									 * as "skip this step" and carries on to verification. */
									if (!totpEnabled)
									{
										CryptographicOperationsCompat.ZeroMemory(totpSecret);
									}
									else
									{
										// Generate and hash recovery codes (best-effort).
										// TOTP setup proceeds even if recovery code persistence fails —
										// the user can still use their authenticator app without recovery.
										// What it must not do is hand over codes that were never stored:
										// codes the player writes down as their fallback, and that then
										// fail on the day the authenticator is lost, are worse than none.
										// So an unstored set is withheld and the setup carries an empty one.
										string[] recoveryCodes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
										var codeHashes = new List<string>(recoveryCodes.Length);
										foreach (string code in recoveryCodes)
										{
											codeHashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
										}
										bool recoveryCodesStored = false;
										if (Server.Database.ServiceRegistry.TryGet<ITwoFactorRecoveryCodeService>(out var recoveryCodeService))
										{
											DatabaseResult recoveryResult = await recoveryCodeService.PersistManyAsync(username, codeHashes);
											if (!recoveryResult.IsSuccess)
											{
												await Log.Error("AccountCreationSystem", $"PersistManyAsync recovery codes DB error for user '{username}': [{recoveryResult.ErrorCode}] {recoveryResult.ErrorMessage}. 2FA is enabled without recovery codes; none were sent.");
											}
											else
											{
												recoveryCodesStored = true;
											}
										}
										else
										{
											await Log.Error("AccountCreationSystem", $"ITwoFactorRecoveryCodeService not registered — recovery codes for '{username}' not persisted; none were sent.");
										}
										// Build otpauth URI for the client's authenticator app.
										// Sent whenever TOTP was enabled, with or without recovery codes.
										string otpauthUri = CryptoHelper.TwoFactor.BuildOtpauthUri(totpSecret, username);

										// Encrypt setup data with the session key for secure transport to client.
										byte[] otpauthUriBytes = Encoding.UTF8.GetBytes(otpauthUri);
										byte[] recoveryCodesBytes = Encoding.UTF8.GetBytes(recoveryCodesStored ? string.Join("\n", recoveryCodes) : string.Empty);

										uint seq1 = request.EncryptionData.NextSendSequence();
										byte[] nonce1 = request.EncryptionData.BuildSendNonce(seq1);
										byte[] aad1 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, request.EncryptionData.AgreedVersion, seq1);
										byte[] encOtpauthUri = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, nonce1, otpauthUriBytes, aad1);

										uint seq2 = request.EncryptionData.NextSendSequence();
										byte[] nonce2 = request.EncryptionData.BuildSendNonce(seq2);
										byte[] aad2 = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TwoFactorSetup, request.EncryptionData.AgreedVersion, seq2);
										byte[] encRecoveryCodes = CryptoHelper.EncryptAES(request.EncryptionData.ServerToClientKey, nonce2, recoveryCodesBytes, aad2);

										// Zeroize plaintext secrets.
										CryptographicOperationsCompat.ZeroMemory(totpSecret);
										CryptographicOperationsCompat.ZeroMemory(otpauthUriBytes);
										CryptographicOperationsCompat.ZeroMemory(recoveryCodesBytes);

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
								}
								catch (Exception tfaEx)
								{
									// Account was created but 2FA setup failed. The account exists
									// without an active TOTP secret, so login will not require 2FA
									// until the user reconfigures it. Log at Error level for ops visibility.
									await Log.Error("AccountCreationSystem", $"2FA setup failed for {username} (account created without 2FA): {tfaEx}");
								}
							}
							else
							{
								await Log.Error("AccountCreationSystem", $"TotpMasterKey is {(totpMasterKeySnapshot == null ? "null" : $"length {totpMasterKeySnapshot.Length}, expected 32")}. TOTP two-factor authentication setup is DISABLED for new accounts. Set TotpMasterKey to a valid 32-byte AES-256 key.");
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
							if (result == ClientAuthenticationResult.ServerBusy)
							{
								// The collapsed answers are the client's doing and stay quiet; a fault
								// is the operator's, and without this line it left no trace at all.
								await Log.Warning("AccountCreationSystem", $"PersistAsync DB error for user '{username}': [{dbResult.ErrorCode}] {dbResult.ErrorMessage}");
							}

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

			/* Resolved on the first sweep rather than at startup, because Server.Configuration
			 * is not guaranteed to be present before then — the same reason smtpService is
			 * built lazily below. */
			if (drainEmailQueue == null)
			{
				// IServerConfiguration exposes no bool accessor, so this parses a string the way
				// SmtpService parses Smtp:UseSsl. Anything but "true" leaves the drain off.
				string configured = Server.Configuration?.GetString("Smtp:DrainQueue", "false");
				drainEmailQueue = string.Equals(configured, "true", System.StringComparison.OrdinalIgnoreCase);
			}
			if (drainEmailQueue == false) return;

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

			// Guard: prevent concurrent in-flight email sends. ProcessNextEmailAsync
			// is fire-and-forget; if the SMTP call takes longer than the sweep interval,
			// a second call would overlap and could send duplicate emails.
			if (Interlocked.CompareExchange(ref emailSendInFlight, 1, 0) != 0)
				return;

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
				if (!result.IsSuccess)
				{
					// NotFound is an empty queue, the ordinary answer on most sweeps.
					if (result.ErrorCode != DatabaseErrorCodes.NotFound)
					{
						await Log.Warning("AccountCreationSystem", $"Email queue dequeue failed: [{result.ErrorCode}] {result.ErrorMessage}");
					}
					return;
				}

				var email = result.Data;
				bool sent = await smtpService.SendEmailAsync(email.RecipientEmail, email.Subject, email.Body);
				if (sent)
				{
					/* The message is out either way; this only stops it looking pending. A failure
					 * leaves the row claimed and unsent, which no sweep picks up again, so it is
					 * not re-sent — but it is worth a line, because it still reads as undelivered. */
					DatabaseResult sentResult = await emailQueueService.MarkSentAsync(email.ID);
					if (!sentResult.IsSuccess)
					{
						await Log.Warning("AccountCreationSystem", $"Failed to mark email {email.ID} sent for '{email.RecipientUsername}': [{sentResult.ErrorCode}] {sentResult.ErrorMessage}");
					}

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
					/* MarkFailedAsync is what releases the claim while attempts remain, and
					 * DequeueNextAsync only ever selects unclaimed rows — so if this write fails, the
					 * message is never retried by any login server. Nothing can fix that from here,
					 * but it must not be silent: it is a player's mail — often a verification
					 * code — gone for good. */
					DatabaseResult failedResult = await emailQueueService.MarkFailedAsync(email.ID, "SMTP send returned false.");
					if (!failedResult.IsSuccess)
					{
						await Log.Error("AccountCreationSystem", $"Failed to release email {email.ID} for '{email.RecipientUsername}' after an SMTP failure: [{failedResult.ErrorCode}] {failedResult.ErrorMessage}. It stays claimed and will not be retried.");
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Warning("AccountCreationSystem", $"Email queue processing error: {ex.Message}");
			}
			finally
			{
				// Release the in-flight guard so the next sweep can send.
				Interlocked.Exchange(ref emailSendInFlight, 0);
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

			// When behind a trusted proxy, all clients share the proxy's IP which
			// breaks IP-based rate limiting. Use the transport-level connection ID
			// as the rate-limiting key instead.
			if (useConnectionIdForRateLimiting)
			{
				return conn.ClientId.ToString();
			}

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

			// Reject oversized payloads, and a channel no client sends, before any allocation or decryption.
			if (msg.Username == null || msg.Username.Length > MaxEncryptedFieldSize ||
				msg.VerifyCode == null || msg.VerifyCode.Length > MaxEncryptedFieldSize ||
				(msg.Channel != VerificationCodeChannel.Email && msg.Channel != VerificationCodeChannel.Sms))
			{
				conn.Disconnect(true);
				return;
			}

			string? ipAddress = ResolveIpAddress(conn);
			if (string.IsNullOrEmpty(ipAddress))
			{
				_ = Log.Warning("AccountCreationSystem", $"Rejecting account verify: no real IP for connection {conn.ClientId}.");
				conn.Disconnect(true);
				return;
			}

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

			/* A per-IP debounce, like account creation has, and not only the failure counter:
			 * until maxFailedAttempts accumulate, every message here enqueued a decrypt and a
			 * database lookup from an unauthenticated connection. One verification per IP per
			 * second is far more than any honest client sends. */
			if (!verifyRateLimiter.TryBegin(ipAddress, DateTime.UtcNow, VerifyRateLimitDuration))
			{
				return;
			}

			// Per-username brute-force protection: prevents distributed attacks from
			// bypassing per-IP limits by rotating source IPs.
			// Username is still encrypted here, so we defer the actual check to
			// ProcessAccountVerifyAsync after decryption. The gate here only checks
			// the IP-based limit; the username-based check runs asynchronously.

			VerificationCodeChannel verifyChannel = msg.Channel;
			if (TryEnqueueAsyncWork(() => ProcessAccountVerifyAsync(conn, msg.Username, msg.VerifyCode, encryptionData, ipAddress, msg.Seq, verifyChannel), conn.ClientId))
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
		/// Decrypts username and verify code, then redeems it via PersistVerifiedByCodeAsync.
		/// </summary>
		/// <remarks>
		/// <b>CancellationToken:</b> This method does not currently accept a CancellationToken.
		/// The underlying <see cref="TryEnqueueAsyncWork"/> infrastructure dispatches bare
		/// <c>Func&lt;Task&gt;</c> delegates. If the base infrastructure is extended to pass
		/// per-operation tokens (e.g., linked to server shutdown), this method should propagate
		/// that token into its DB calls (<c>PersistVerifiedByCodeAsync</c>) to enable cooperative
		/// cancellation during graceful shutdown.
		/// </remarks>
		private async Task ProcessAccountVerifyAsync(
			NetworkConnection conn,
			byte[] encryptedUsername,
			byte[] encryptedVerifyCode,
			ConnectionEncryptionData encryptionData,
			string ipAddress,
			uint seq,
			VerificationCodeChannel channel)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;
			if (ipAddress == null)
			{
				// Defense-in-depth: ipAddress should never be null here (the caller in
				// OnServerAccountVerifyBroadcastReceived guards against it), but if the
				// method is ever called from a different path, fail closed.
				NetworkConnection failConn = conn;
				TryEnqueueMainThread(() =>
				{
					if (failConn != null && failConn.IsActive)
						failConn.Disconnect(false);
				});
				return;
			}

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
							CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
							CryptographicOperationsCompat.ZeroMemory(decryptedVerifyCode);
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
						CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
						CryptographicOperationsCompat.ZeroMemory(decryptedVerifyCode);
						NetworkConnection failConn = conn;
						TryEnqueueMainThread(() =>
						{
							if (failConn != null && failConn.IsActive)
								failConn.Disconnect(false);
						});
						return;
					}
					CryptographicOperationsCompat.ZeroMemory(decryptedUsername);
					CryptographicOperationsCompat.ZeroMemory(decryptedVerifyCode);

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
								// Use TryGetValue + TryRemove instead of the ICollection<KVP>.Remove
								// pattern which has known issues under IL2CPP.
								if (verifyUsernameFailures.TryGetValue(userKey, out var existing)
									&& existing.Count == failInfo.Count
									&& existing.FirstFailure == failInfo.FirstFailure)
								{
									verifyUsernameFailures.TryRemove(userKey, out _);
								}
							}
							else if (failInfo.Count >= MaxVerifyFailuresPerUsername)
							{
								// Locked out — reject immediately.
								result = ClientAuthenticationResult.InvalidUsernameOrPassword;
								goto trackFailure;
							}
						}

						/* One conditional update tries the code against every code the account holds — email,
						 * SMS and Discord — and any match verifies it, so the broadcast's channel is not consulted.
						 * A wrong code counts towards the support ticket opened after three in a row. The answer is
						 * the same whether or not that ticket was opened: this message is accepted before any
						 * password, so it must not say which accounts exist and are waiting for a code. */
						DatabaseResult<AccountVerificationChannels> dbResult = await accountService.PersistVerifiedByCodeAsync(username, verifyCode);
						if (dbResult.IsSuccess)
						{
							result = ClientAuthenticationResult.AccountVerified;
						}
						else if (dbResult.ErrorCode != DatabaseErrorCodes.ValidationError)
						{
							/* Every way of the code being wrong — wrong, expired, unknown account, already
							 * verified — is the one ValidationError, so anything else is the database failing
							 * to answer. That used to be counted as a wrong code: the player was told their
							 * correct code was wrong, and it went towards the support ticket opened after
							 * three, the per-IP block and the per-username lockout. ServerBusy says only that
							 * the server could not check, which is the same for every account, so it tells
							 * an observer nothing the wrong-code answer would not; and it is not the
							 * InvalidUsernameOrPassword the failure tracking below counts. */
							await Log.Warning("AccountCreationSystem", $"PersistVerifiedByCodeAsync DB error for user '{username}': [{dbResult.ErrorCode}] {dbResult.ErrorMessage}");
							result = ClientAuthenticationResult.ServerBusy;
						}
						else
						{
							result = ClientAuthenticationResult.InvalidUsernameOrPassword;
							Server.Database.ServiceRegistry.TryGet<ISupportTicketService>(out var supportTickets);
							FishMMO.Database.Npgsql.Services.VerificationFailureTicket.Outcome outcome =
								await FishMMO.Database.Npgsql.Services.VerificationFailureTicket.RecordAsync(accountService, supportTickets, username);
							if (outcome.TicketOpened)
							{
								await Log.Warning("AccountCreationSystem", $"Opened support ticket {outcome.TicketId} for '{username}' after {outcome.Failures} incorrect verification codes.");
							}
							else if (outcome.TicketError != null)
							{
								await Log.Warning("AccountCreationSystem", $"A support ticket was due for '{username}' after {outcome.Failures} incorrect verification codes but could not be opened: {outcome.TicketError}");
							}
						}
					}

				trackFailure:
					// Track failures for rate limiting. Every accepted code answers with something other
					// than InvalidUsernameOrPassword — including the result asking for the next code.
					if (result == ClientAuthenticationResult.InvalidUsernameOrPassword)
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

		/// <summary>Broadcasts a terminal result for a request refused before any account was written.</summary>
		private void BroadcastEarlyResult(NetworkConnection conn, ClientAuthenticationResult earlyResult)
		{
			TryEnqueueMainThread(() =>
			{
				if (conn != null && conn.IsActive)
				{
					Server.NetworkWrapper.Broadcast(conn,
						new ClientAuthResultBroadcast() { Result = earlyResult },
						false, Channel.Reliable);
				}
			});
		}

		/// <summary>
		/// The closed-test gate at registration: is this code redeemable for an active program?
		/// </summary>
		/// <remarks>
		/// Checked BEFORE the account exists and redeemed after, because redeeming first would attach a
		/// use to a name the insert may then find taken. Every bad-code case — missing, malformed,
		/// unknown, revoked, expired, used up — is the one <see cref="ClientAuthenticationResult.BetaCodeInvalid"/>,
		/// so the answer cannot tell a real code from a guess. Only a gate that cannot be evaluated says
		/// something different (ServerBusy), and it refuses rather than admits.
		/// </remarks>
		/// <returns>Null when the code may be redeemed; otherwise the refusal.</returns>
		private async Task<ClientAuthenticationResult?> CheckRegistrationBetaCodeAsync(string betaCode)
		{
			if (string.IsNullOrEmpty(betaCode))
			{
				return ClientAuthenticationResult.BetaCodeInvalid;
			}

			IReadOnlyList<string> programs = LoginSecurityPolicy.GetBetaPrograms(Server.Configuration, out int rejected);
			if (programs.Count == 0)
			{
				// The service reads an empty list as "any program", which would admit registrations the
				// sign-in gate then refuses forever. Fail closed, loudly.
				await Log.Error("AccountCreationSystem", $"BetaMode is on but BetaPrograms lists no valid program ({rejected} invalid name(s)); refusing every beta code.");
				return ClientAuthenticationResult.BetaCodeInvalid;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IBetaCodeService>(out var betaService))
			{
				await Log.Error("AccountCreationSystem", "BetaMode is on but IBetaCodeService is not registered; refusing account creation.");
				return ClientAuthenticationResult.ServerBusy;
			}

			DatabaseResult<bool> check = await betaService.CheckRedeemableAsync(betaCode, programs);
			if (!check.IsSuccess)
			{
				await Log.Warning("AccountCreationSystem", $"CheckRedeemableAsync failed: {check.ErrorCode} - {check.ErrorMessage}");
				return ClientAuthenticationResult.ServerBusy;
			}
			return check.Data ? (ClientAuthenticationResult?)null : ClientAuthenticationResult.BetaCodeInvalid;
		}

		/// <summary>
		/// Redeems the registration beta code for the new account: always attempted when a code was
		/// given, so a code entered while no test is running is still on the account when one starts.
		/// </summary>
		private async Task RedeemRegistrationBetaCodeAsync(string username, string betaCode, bool betaMode)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IBetaCodeService>(out var betaService))
			{
				return;
			}

			var redeemed = await betaService.RedeemAsync(username, betaCode);
			if (!redeemed.IsSuccess)
			{
				/* Checked a moment ago, so in beta mode this is a lost race for the code's last use: the
				 * account exists without access and can redeem another code in the Control Panel. */
				string message = $"Beta code redemption for new account '{username}' failed: {redeemed.ErrorCode} - {redeemed.ErrorMessage}";
				if (betaMode)
				{
					await Log.Warning("AccountCreationSystem", message);
				}
				else
				{
					await Log.Debug("AccountCreationSystem", message);
				}
			}
		}

		/// <summary>
		/// Sends a verification code on every channel this account is asked for, and says what the client
		/// should do next.
		/// </summary>
		/// <remarks>
		/// Which channels is <see cref="AccountVerificationRules.Effective"/>, the rule sign-in and the Control
		/// Panel apply too: the player's choices that this server has switched on and the account can receive,
		/// falling back to email. Any one of the codes verifies the account. This runs only when the
		/// development <c>AutoVerifyAccounts</c> bypass is off. A server that verifies no channel, and an account
		/// no enabled channel can reach, are verified at creation — but the caller still enrols them in
		/// two-factor authentication, because unlike the bypass these are legal in production.
		/// </remarks>
		/// <returns>
		/// <see cref="ClientAuthenticationResult.AccountCreated"/> when a code is outstanding, and
		/// <see cref="ClientAuthenticationResult.AccountVerified"/> when none is.
		/// </returns>
		private async Task<ClientAuthenticationResult> DeliverVerificationCodesAsync(
			IAccountService accountService,
			string username,
			string email,
			string phone,
			string discordUsername,
			AccountVerificationChannels chosen)
		{
			AccountVerificationChannels enabled = AccountVerificationPolicy.EnabledChannels(Server.Configuration);
			AccountVerificationChannels effective = AccountVerificationRules.Effective(chosen, enabled,
				AccountVerificationRules.Receivable(email, phone, discordUsername));

			if (effective == AccountVerificationChannels.None)
			{
				/* Nothing to ask for: the server verifies no channel (the same write the development bypass
				 * uses), or verification is required but nothing it sends can reach this account. */
				DatabaseResult verified = enabled == AccountVerificationChannels.None
					? await accountService.PersistAutoVerifiedAsync(username)
					: await accountService.PersistChannelsVerifiedAsync(username, AccountVerificationChannels.None);
				if (!verified.IsSuccess)
				{
					await Log.Warning("AccountCreationSystem", $"Verifying '{username}' without a code failed: {verified.ErrorCode} - {verified.ErrorMessage}");
				}
				return ClientAuthenticationResult.AccountVerified;
			}

			if ((effective & AccountVerificationChannels.Email) != 0)
			{
				await SendEmailVerificationCodeAsync(accountService, username, email);
			}
			if ((effective & AccountVerificationChannels.Sms) != 0)
			{
				await SendSmsVerificationCodeAsync(accountService, username, phone);
			}
			if ((effective & AccountVerificationChannels.Discord) != 0)
			{
				await IssueDiscordVerificationCodeAsync(accountService, username);
			}
			return ClientAuthenticationResult.AccountCreated;
		}

		/// <summary>Generates, stores and queues the email verification code.</summary>
		private async Task SendEmailVerificationCodeAsync(IAccountService accountService, string username, string email)
		{
			/* No queue, no code: storing one that nothing will deliver only starts its 24 hours, and
			 * sign-in will not issue another until they are up. Left missing instead, the first
			 * correct sign-in once a queue exists sends one. */
			if (!Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueueService))
			{
				await Log.Warning("AccountCreationSystem", $"IEmailQueueService not registered — verification email for '{username}' not enqueued.");
				return;
			}

			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			// 24 hour TTL: long enough for users to act, short enough that an
			// exposed code cannot be re-used indefinitely.
			DateTime verifyExpiresUtc = DateTime.UtcNow.AddHours(24);
			DatabaseResult verifyResult = await accountService.PersistVerifyCodeAsync(username, verifyCode, verifyExpiresUtc);
			if (!verifyResult.IsSuccess)
			{
				/* Stop here, as the SMS twin does. Mailing on regardless sent the player a code the
				 * database never held: every attempt to enter it was a wrong code, counted towards
				 * the support ticket opened after three. With no code stored, sign-in treats one as
				 * due and sends a real one. */
				await Log.Warning("AccountCreationSystem", $"PersistVerifyCodeAsync DB error for user '{username}': [{verifyResult.ErrorCode}] {verifyResult.ErrorMessage}. No verification email sent.");
				return;
			}

			// Enqueue verification email for SMTP delivery.
			// The background processor will pick this up and send via the configured SMTP server.
			// Prevent duplicate emails: skip if a pending email already exists for this user.
			var dupCheck = await emailQueueService.HasPendingForUserAsync(username, EmailKind.Verification);
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
					await Log.Warning("AccountCreationSystem", $"Failed to enqueue verification email for '{username}': [{emailResult.ErrorCode}] {emailResult.ErrorMessage}");
					await AccountVerificationPolicy.ExpireUndeliveredCodeAsync(accountService, username, verifyCode, AccountVerificationChannels.Email, "AccountCreationSystem");
				}
			}
		}

		/// <summary>
		/// Generates, stores and queues the SMS verification code — the same six-digit, 24-hour shape as
		/// the email code. The login server only enqueues; the Control Panel drains the SMS queue.
		/// </summary>
		private async Task SendSmsVerificationCodeAsync(IAccountService accountService, string username, string phone)
		{
			if (string.IsNullOrEmpty(phone))
			{
				await Log.Error("AccountCreationSystem", $"SMS verification was chosen for '{username}' but no phone number is on record; no SMS code was sent.");
				return;
			}

			// No queue, no code — for the reason given in SendEmailVerificationCodeAsync.
			if (!Server.Database.ServiceRegistry.TryGet<ISmsQueueService>(out var smsQueueService))
			{
				await Log.Warning("AccountCreationSystem", $"ISmsQueueService not registered — verification SMS for '{username}' not enqueued.");
				return;
			}

			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			DateTime verifyExpiresUtc = DateTime.UtcNow.AddHours(24);
			DatabaseResult codeResult = await accountService.PersistPhoneVerifyCodeAsync(username, verifyCode, verifyExpiresUtc);
			if (!codeResult.IsSuccess)
			{
				await Log.Error("AccountCreationSystem", $"PersistPhoneVerifyCodeAsync DB error for user '{username}': [{codeResult.ErrorCode}] {codeResult.ErrorMessage}");
				return;
			}

			var dupCheck = await smsQueueService.HasPendingForUserAsync(username, SmsKind.Verification);
			if (dupCheck.IsSuccess && dupCheck.Data)
			{
				await Log.Debug("AccountCreationSystem", $"Skipping duplicate verification SMS for '{username}' — a pending message already exists.");
				return;
			}

			DatabaseResult smsResult = await smsQueueService.EnqueueAsync(phone, username, BuildVerificationSmsBody(verifyCode), SmsKind.Verification);
			if (!smsResult.IsSuccess)
			{
				await Log.Warning("AccountCreationSystem", $"Failed to enqueue verification SMS for '{username}': [{smsResult.ErrorCode}] {smsResult.ErrorMessage}");
				await AccountVerificationPolicy.ExpireUndeliveredCodeAsync(accountService, username, verifyCode, AccountVerificationChannels.Sms, "AccountCreationSystem");
			}
		}

		/// <summary>
		/// Issues the Discord verification code. The login server only issues it: the database wakes the
		/// Discord bot, which sends it in the account's one DM, once the player is in the Discord server.
		/// </summary>
		private async Task IssueDiscordVerificationCodeAsync(IAccountService accountService, string username)
		{
			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			DatabaseResult<bool> issued = await accountService.PersistDiscordVerifyCodeAsync(username, verifyCode);
			if (!issued.IsSuccess)
			{
				await Log.Error("AccountCreationSystem", $"PersistDiscordVerifyCodeAsync DB error for user '{username}': {issued.ErrorCode} - {issued.ErrorMessage}");
			}
		}

		/// <summary>Builds the verification SMS. Plain text, well under the queue's 480-character limit.</summary>
		private static string BuildVerificationSmsBody(int verifyCode)
		{
			return $"FishMMO verification code: {verifyCode:D6}. It expires in 24 hours. If you did not create a FishMMO account, ignore this message.";
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