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
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Logging;
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
		private enum EnqueueResult : byte
		{
			Accepted = 0,
			RateLimited = 1,
			Blocked = 2,
			QueueFull = 3,
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
		/// Maximum number of queued main-thread response actions processed per frame.
		/// This time-slices response dispatch to avoid frame spikes during heavy login waves.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max account-creation responses drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadResponsesPerFrame = 100;

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
		[Header("Proxy Compatibility")]
		[Tooltip("Use connection ID instead of IP for rate limiting. Enable when behind a proxy/NAT/load balancer where all clients share one IP.")]
		[SerializeField] private bool useConnectionIdForRateLimit = false;

		/// <summary>
		/// Maximum allowed size in bytes for any single encrypted field in CreateAccountBroadcast.
		/// Rejects oversized payloads on the network thread before any decryption or allocation.
		/// </summary>
		private const int MaxEncryptedFieldSize = 2048;

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

			// Get a cached IP key for rate limiting.
			string ipAddress = ResolveIpAddress(conn);

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

			// Try to enqueue to centralized async worker.
			if (TryEnqueueAsyncWork(() => ProcessAccountCreationAsync(request), request.Connection.ClientId))
			{
				return EnqueueResult.Accepted;
			}

			return EnqueueResult.QueueFull;
		}

		/// <summary>
		/// Sends immediate ServerBusy response when rate limited or queue full.
		/// Ultra-fast reactive UDP response with no blocking operations.
		/// </summary>
		/// <param name="conn">Network connection to send response to.</param>
		private void SendServerBusyResponse(NetworkConnection conn)
		{
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
			if (conn != null)
			{
				Log.Warning("AccountCreationSystem", $"Rejected request from {ResolveIpAddress(conn)} - Rate limited or queue full");
			}
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
					// Decrypt credentials on worker thread using explicit sequence numbers provided by client.
					byte[] decryptedUsername;
					byte[] decryptedEmail;
					byte[] decryptedAge;
					byte[] decryptedSalt;
					byte[] decryptedVerifier;
					try
					{
						uint seq = request.Seq;
						if (seq < 4)
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

						// Consume and decrypt username
						if (!request.EncryptionData.TryConsumeReceiveSequence(seqUsername))
							throw new CryptographicException("Account creation username sequence out-of-order or duplicate.");
						byte[] nonceU = request.EncryptionData.BuildReceiveNonce(seqUsername);
						byte[] aadU = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqUsername);
						decryptedUsername = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceU, request.EncryptedUsername, aadU);

						// Consume and decrypt email
						if (!request.EncryptionData.TryConsumeReceiveSequence(seqEmail))
							throw new CryptographicException("Account creation email sequence out-of-order or duplicate.");
						byte[] nonceE = request.EncryptionData.BuildReceiveNonce(seqEmail);
						byte[] aadE = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqEmail);
						decryptedEmail = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceE, request.EncryptedEmail, aadE);

						// Consume and decrypt age
						if (!request.EncryptionData.TryConsumeReceiveSequence(seqAge))
							throw new CryptographicException("Account creation age sequence out-of-order or duplicate.");
						byte[] nonceA = request.EncryptionData.BuildReceiveNonce(seqAge);
						byte[] aadA = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqAge);
						decryptedAge = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceA, request.EncryptedAge, aadA);

						// Consume and decrypt salt
						if (!request.EncryptionData.TryConsumeReceiveSequence(seqSalt))
							throw new CryptographicException("Account creation salt sequence out-of-order or duplicate.");
						byte[] nonceS = request.EncryptionData.BuildReceiveNonce(seqSalt);
						byte[] aadS = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqSalt);
						decryptedSalt = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonceS, request.EncryptedSalt, aadS);

						// Consume and decrypt verifier
						if (!request.EncryptionData.TryConsumeReceiveSequence(seqVerifier))
							throw new CryptographicException("Account creation verifier sequence out-of-order or duplicate.");
						byte[] nonceV = request.EncryptionData.BuildReceiveNonce(seqVerifier);
						byte[] aadV = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.CreateAccount, request.EncryptionData.AgreedVersion, seqVerifier);
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
							// Clear failure tracker on success
							mappingData.IpFailureTracker.TryRemove(request.IpAddress, out _);

							// Generate and store a verification code for email verification.
							int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
							await accountService.PersistVerifyCodeAsync(username, verifyCode);
						}
						else
						{
							// Map database error codes to client-facing results
							result = dbResult.ErrorCode switch
							{
								DatabaseErrorCodes.UniqueViolation => ClientAuthenticationResult.InvalidUsernameOrPassword,
								DatabaseErrorCodes.ValidationError => ClientAuthenticationResult.InvalidUsernameOrPassword,
								_ => ClientAuthenticationResult.ServerBusy,
							};

							// Track failure atomically
							mappingData.IpFailureTracker.AddOrUpdate(request.IpAddress, 1, (_, existing) => existing + 1);
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
						failMappingData.IpFailureTracker.AddOrUpdate(request.IpAddress, 1, (_, existing) => existing + 1);
					}
				}
			}

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
		/// Periodically cleans up stale IP rate-limit and failure-tracking entries
		/// to prevent unbounded memory growth from one-time visitors.
		/// Iterating a ConcurrentDictionary creates a point-in-time snapshot, so this is safe.
		/// </summary>
		private void CleanUpMappingData(float deltaTime)
		{
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

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
			int scannedFailures = 0;
			int removedFailures = 0;
			foreach (var entry in mappingData.IpFailureTracker)
			{
				scannedFailures++;
				if (!mappingData.IpRateLimitTracker.ContainsKey(entry.Key) &&
					mappingData.IpFailureTracker.TryRemove(entry.Key, out _))
				{
					removedFailures++;
				}

				if (scannedFailures >= cleanupMaxScanPerMap || removedFailures >= cleanupMaxRemovalsPerMap)
				{
					break;
				}
			}

			// Evict stale per-connection caches as a backstop against delayed disconnect events.
			TimeSpan cacheTtl = TimeSpan.FromSeconds(Math.Max(1f, ipBlockDurationSeconds));
			runtimeData.ConnectionIpCache.SweepExpired(DateTime.UtcNow, cacheTtl, cleanupMaxScanPerMap, cleanupMaxRemovalsPerMap);
			runtimeData.ConnectionEncryptionCache.SweepExpired(DateTime.UtcNow, cacheTtl, cleanupMaxScanPerMap, cleanupMaxRemovalsPerMap);
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
		/// When <see cref="useConnectionIdForRateLimit"/> is enabled, this method returns
		/// <c>conn.ClientId.ToString()</c> as a proxy-compatible fallback key.
		/// Operators must be aware that connection-ID keying trades IP-level aggregation
		/// for per-socket granularity, which may be less effective against distributed attacks
		/// but avoids false-positive blocking behind proxies.
		/// </para>
		/// </summary>
		/// <param name="conn">The network connection to resolve a rate-limit key for.</param>
		/// <returns>A stable string key for IP-based or connection-based rate limiting.</returns>
		private string ResolveIpAddress(NetworkConnection conn)
		{
			if (conn == null)
			{
				return string.Empty;
			}

			// Proxy-compatible fallback: use connection ID instead of IP when configured.
			if (useConnectionIdForRateLimit)
			{
				return conn.ClientId.ToString();
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				return conn.GetAddress() ?? "unknown";
			}

			DateTime now = DateTime.UtcNow;
			if (runtimeData.ConnectionIpCache.TryGetAndTouch(conn.ClientId, now, out string cachedIp) &&
				!string.IsNullOrEmpty(cachedIp))
			{
				return cachedIp;
			}

			string ipAddress = conn.GetAddress();
			if (string.IsNullOrEmpty(ipAddress))
			{
				ipAddress = "unknown";
			}

			runtimeData.ConnectionIpCache.Upsert(conn.ClientId, ipAddress, now);
			return ipAddress;
		}

		/// <summary>
		/// Resolves encryption data with a per-connection cache to reduce lock pressure on AccountManager.
		/// </summary>
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
				encryptionData = cached;
				return true;
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
						if (seq < 1)
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

						if (!encryptionData.TryConsumeReceiveSequence(seqUsername))
							throw new CryptographicException("Account verify username sequence out-of-order or duplicate.");
						byte[] nonceU = encryptionData.BuildReceiveNonce(seqUsername);
						byte[] aadU = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, encryptionData.AgreedVersion, seqUsername);
						decryptedUsername = CryptoHelper.DecryptAES(encryptionData.ClientToServerKey, nonceU, encryptedUsername, aadU);

						if (!encryptionData.TryConsumeReceiveSequence(seqCode))
							throw new CryptographicException("Account verify code sequence out-of-order or duplicate.");
						byte[] nonceC = encryptionData.BuildReceiveNonce(seqCode);
						byte[] aadC = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.AccountVerify, encryptionData.AgreedVersion, seqCode);
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
						DatabaseResult dbResult = await accountService.PersistVerifiedAsync(username, verifyCode);
						result = dbResult.IsSuccess
							? ClientAuthenticationResult.AccountVerified
							: ClientAuthenticationResult.InvalidUsernameOrPassword;
					}

					// Track failures for rate limiting.
					if (result != ClientAuthenticationResult.AccountVerified &&
						Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
					{
						mappingData.IpFailureTracker.AddOrUpdate(ipAddress, 1, (_, existing) => existing + 1);
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

	}
}