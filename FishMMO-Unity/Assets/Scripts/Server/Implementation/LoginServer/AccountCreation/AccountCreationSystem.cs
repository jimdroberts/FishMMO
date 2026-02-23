using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
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

			ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Clamp tunables to safe values.
			ipRateLimitSeconds = Mathf.Max(0f, ipRateLimitSeconds);
			maxFailedAttempts = Mathf.Max(1, maxFailedAttempts);
			ipBlockDurationSeconds = Mathf.Max(1f, ipBlockDurationSeconds);
			maxMainThreadResponsesPerFrame = Mathf.Max(1, maxMainThreadResponsesPerFrame);
			cleanupMaxScanPerMap = Mathf.Max(1, cleanupMaxScanPerMap);
			cleanupMaxRemovalsPerMap = Mathf.Max(1, cleanupMaxRemovalsPerMap);

			// Register network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);

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

			if (ServerManager != null)
			{
				ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
			}

			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.ConnectionIpCache.Clear();
				runtimeData.ConnectionEncryptionCache.Clear();
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);

			// Unregister broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived);

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
				msg.Salt,                  // Still encrypted!
				msg.Verifier,              // Still encrypted!
				encryptionData.SymmetricKey,
				encryptionData.IV,
				ipAddress
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
					// Decrypt credentials on worker thread (doesn't block network)
					byte[] decryptedUsername = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedUsername);
					byte[] decryptedSalt = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedSalt);
					byte[] decryptedVerifier = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedVerifier);

					string username = Encoding.UTF8.GetString(decryptedUsername);

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

					string salt = Encoding.UTF8.GetString(decryptedSalt);
					string verifier = Encoding.UTF8.GetString(decryptedVerifier);

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
					DatabaseResult dbResult = await accountService.PersistAsync(username, salt, verifier);

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
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// Uses time-slicing during normal updates and full drain during shutdown.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemMainThreadQueueData>(out var queueData) == true)
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadResponsesPerFrame);
				}
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private bool TryEnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemMainThreadQueueData>(out var queueData) == true)
			{
				return queueData.TryEnqueue(action);
			}
			return false;
		}

		/// <summary>
		/// Removes cached connection-IP mapping when a connection closes.
		/// </summary>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (conn != null &&
				args.ConnectionState == RemoteConnectionState.Stopped &&
				Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.ConnectionIpCache.Remove(conn.ClientId);
				runtimeData.ConnectionEncryptionCache.Remove(conn.ClientId);
			}
		}

		/// <summary>
		/// Resolves a stable IP key for rate-limiting while avoiding repeated address string allocations.
		/// </summary>
		private string ResolveIpAddress(NetworkConnection conn)
		{
			if (conn == null)
			{
				return string.Empty;
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

	}
}