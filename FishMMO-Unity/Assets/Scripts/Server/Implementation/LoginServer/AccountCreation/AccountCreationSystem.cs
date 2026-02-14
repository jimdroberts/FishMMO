using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Text;
using System.Threading;
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
	[RequiresDataContainer(typeof(AccountCreationSystemQueueData))]
	[RequiresDataContainer(typeof(AccountCreationSystemRuntimeData))]
	[RequiresDataContainer(typeof(AccountCreationSystemMappingData))]
	[RequiresDataContainer(typeof(AccountCreationSystemMainThreadQueueData))]
	public class AccountCreationSystem : ServerBehaviour, IAccountCreationSystem<NetworkConnection>
	{
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
		/// Number of concurrent background workers processing queued account creation requests.
		/// </summary>
		[Header("Queue Configuration")]
		[Tooltip("Number of concurrent workers processing account creations")]
		[SerializeField] private int workerCount = 2;

		/// <summary>
		/// Gets the current number of pending account creation requests in the async queue.
		/// </summary>
		public int PendingRequestCount
		{
			get
			{
				if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemQueueData<NetworkConnection>>(out var queueData) == true)
				{
					return queueData.PendingCount;
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
		/// Initializes the account creation system, starts async workers, and registers broadcast handlers.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("AccountCreationSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Verify all required data containers are available
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemQueueData<NetworkConnection>>(out var queueData))
			{
				Log.Error("AccountCreationSystem", "Failed to initialize: IAccountCreationSystemQueueData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
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

			// Start async workers and track their tasks in the runtime data container
			runtimeData.WorkerCancellationToken = queueData.CancellationTokenSource.Token;
			runtimeData.WorkerTasks = new Task[workerCount];
			for (int i = 0; i < workerCount; i++)
			{
				int workerId = i + 1;
				runtimeData.WorkerTasks[i] = ProcessAccountCreationRequestsAsync(runtimeData.WorkerCancellationToken, workerId);
			}

			// Register network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<CreateAccountBroadcast>(OnServerCreateAccountBroadcastReceived, false);

			Log.Debug("AccountCreationSystem", $"Initialized (Workers={workerCount}, RateLimit={ipRateLimitSeconds}s, MaxFailures={maxFailedAttempts}, BlockDuration={ipBlockDurationSeconds}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the account creation system, cancels async workers, and unregisters broadcast handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("AccountCreationSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Cancel async processing via data container
			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemQueueData<NetworkConnection>>(out var queueData))
			{
				queueData.CancellationTokenSource?.Cancel();
			}

			// Wait for workers to finish gracefully
			if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) &&
				runtimeData.WorkerTasks != null)
			{
				try
				{
					Task.WaitAll(runtimeData.WorkerTasks, TimeSpan.FromSeconds(5));
				}
				catch (AggregateException) { /* workers may have faulted or been cancelled */ }
				runtimeData.WorkerTasks = null;
			}

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue();

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
			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Get IP address for rate limiting
			string ipAddress = conn.GetAddress();

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
			if (!TryEnqueueAccountCreation(request))
			{
				// Queue full or rate limited - send immediate rejection
				SendServerBusyResponse(conn);
			}
		}

		/// <summary>
		/// Public API: Attempts to enqueue an account creation request with rate limiting checks.
		/// Returns false immediately if rate limited or queue full.
		/// </summary>
		/// <param name="request">Account creation request containing encrypted credentials.</param>
		/// <returns>True if queued successfully; false if rejected.</returns>
		public bool TryEnqueueAccountCreation(AccountCreationRequest<NetworkConnection> request)
		{
			// Access data containers for this operation
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemQueueData<NetworkConnection>>(out var queueData) ||
				!Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
			{
				return false;
			}

			// Check IP rate limit
			if (mappingData.IpRateLimitTracker.TryGetValue(request.IpAddress, out DateTime lastAttempt))
			{
				double secondsSinceLastAttempt = (DateTime.UtcNow - lastAttempt).TotalSeconds;
				if (secondsSinceLastAttempt < ipRateLimitSeconds)
				{
					return false; // Rate limited
				}
			}

			// Check IP block (too many failures)
			if (mappingData.IpFailureTracker.TryGetValue(request.IpAddress, out int failureCount))
			{
				if (failureCount >= maxFailedAttempts)
				{
					return false; // IP blocked
				}
			}

			// Update rate limit tracker
			mappingData.IpRateLimitTracker[request.IpAddress] = DateTime.UtcNow;

			// Try to enqueue
			return queueData.RequestChannel.Writer.TryWrite(request);
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
				runtimeData.TotalRejected++;
			}

			// Send immediate response
			Server.NetworkWrapper.Broadcast(conn, new ClientAuthResultBroadcast()
			{
				Result = ClientAuthenticationResult.ServerBusy
			}, false, Channel.Reliable);

			// Optional: Log for monitoring
			if (conn != null)
			{
				Log.Warning("AccountCreationSystem", $"Rejected request from {conn.GetAddress()} - Rate limited or queue full");
			}
		}

		/// <summary>
		/// Async worker that processes account creation requests from the channel.
		/// Runs on background thread, performing decryption and database operations.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
		/// <param name="workerId">Worker ID for logging/debugging.</param>
		private async Task ProcessAccountCreationRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			// Get queue data once at start of worker
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemQueueData<NetworkConnection>>(out var queueData))
			{
				await Log.Error("AccountCreationSystem", $"Worker {workerId} failed to get queue data");
				return;
			}

			await Log.Debug("AccountCreationSystem", $"Worker {workerId} started");
			try
			{
				await foreach (var request in queueData.RequestChannel.Reader.ReadAllAsync(cancellationToken))
				{
					try
					{
						await ProcessAccountCreationAsync(request);
					}
					catch (Exception ex)
					{
						await Log.Error("AccountCreationSystem", $"Worker {workerId} error processing account creation: {ex}");

						// Increment failure counter
						if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
						{
							runtimeData.TotalFailed++;
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Expected during shutdown
				await Log.Debug("AccountCreationSystem", $"Worker {workerId} cancelled");
			}

			await Log.Debug("AccountCreationSystem", $"Worker {workerId} stopped");
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
					string salt = Encoding.UTF8.GetString(decryptedSalt);
					string verifier = Encoding.UTF8.GetString(decryptedVerifier);

					// Database operation via registry-resolved service (BaseService handles context lifecycle)
					DatabaseResult dbResult = await accountService.PersistAsync(username, salt, verifier);

					// Update statistics
					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) &&
						Server.DataContainerRegistry.TryGet<IAccountCreationSystemMappingData>(out var mappingData))
					{
						if (dbResult.IsSuccess)
						{
							result = ClientAuthenticationResult.AccountCreated;
							runtimeData.TotalProcessed++;
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
							runtimeData.TotalRejected++;
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("AccountCreationSystem", $"Error during account creation processing: {ex}");
					result = ClientAuthenticationResult.InvalidUsernameOrPassword;

					if (Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData))
					{
						runtimeData.TotalFailed++;
					}
				}
			}

			// Marshal response back to main thread - FishNet Broadcast is not thread-safe
			ClientAuthenticationResult capturedResult = result;
			NetworkConnection capturedConn = request.Connection;
			EnqueueMainThread(() =>
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
		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue();
			MonitorWorkerHealth();
			CleanUpMappingData(deltaTime);
		}

		/// <summary>
		/// Checks worker tasks for unexpected completion and respawns any that have died.
		/// Handles Faulted, Canceled, and silent RanToCompletion exits.
		/// Runs on the main thread each frame via OnLateUpdate.
		/// </summary>
		private void MonitorWorkerHealth()
		{
			if (!Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var runtimeData) ||
				runtimeData.WorkerTasks == null ||
				runtimeData.WorkerCancellationToken.IsCancellationRequested)
			{
				return;
			}

			for (int i = 0; i < runtimeData.WorkerTasks.Length; i++)
			{
				Task task = runtimeData.WorkerTasks[i];
				if (task == null || !task.IsCompleted)
				{
					continue;
				}

				int workerId = i + 1;
				string reason = task.Status.ToString();
				string detail = task.Exception?.Flatten().Message;
				_ = Log.Error("AccountCreationSystem",
					$"Worker {workerId} died unexpectedly (Status={reason}{(detail != null ? $", Error={detail}" : "")}). Respawning...");
				runtimeData.WorkerTasks[i] = ProcessAccountCreationRequestsAsync(runtimeData.WorkerCancellationToken, workerId);
			}
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

			// Evict rate-limit entries older than the block duration
			foreach (var entry in mappingData.IpRateLimitTracker)
			{
				if (entry.Value < cutoff)
				{
					mappingData.IpRateLimitTracker.TryRemove(entry.Key, out _);
				}
			}

			// Evict failure-tracking entries for IPs whose block period has expired.
			// Once the rate-limit entry is gone (expired above), the failure count serves no purpose.
			foreach (var entry in mappingData.IpFailureTracker)
			{
				if (!mappingData.IpRateLimitTracker.ContainsKey(entry.Key))
				{
					mappingData.IpFailureTracker.TryRemove(entry.Key, out _);
				}
			}
		}

		/// <summary>
		/// Drains the main-thread queue via the RuntimeDataContainer.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Drain();
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread
		/// via the RuntimeDataContainer.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private void EnqueueMainThread(Action action)
		{
			if (Server?.DataContainerRegistry.TryGet<IAccountCreationSystemMainThreadQueueData>(out var queueData) == true)
			{
				queueData.Enqueue(action);
			}
		}
	}
}