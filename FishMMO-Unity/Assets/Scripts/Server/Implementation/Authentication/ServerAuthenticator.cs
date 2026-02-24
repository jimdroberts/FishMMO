using FishNet.Authenticating;
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
using FishMMO.Server.Core.Account.SRP;
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
	public class ServerAuthenticator : Authenticator
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per Update tick.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		private const int MaxMainThreadActionsPerUpdate = 100;

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
		/// Authentication TTL in seconds. Connections that do not complete SRP within this window are purged.
		/// </summary>
		private const float AuthStaleTtlSeconds = 15f;

		/// <summary>
		/// Sweep interval in seconds for stale authentication cleanup.
		/// </summary>
		private const float AuthSweepIntervalSeconds = 1f;

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
		/// Maximum allowed size in bytes for any single encrypted SRP payload field.
		/// Prevents oversized payloads from consuming AES decryption CPU on workers.
		/// </summary>
		private const int MaxSrpPayloadBytes = 1024;

		/// <summary>
		/// Maximum allowed size in bytes for a client RSA public key during handshake.
		/// A 2048-bit RSA key is 256-byte modulus + 3-byte exponent = 259 bytes.
		/// 512 provides generous headroom while blocking multi-MB abuse payloads.
		/// </summary>
		private const int MaxRsaPublicKeyBytes = 512;

		/// <summary>
		/// Bounded channel for queuing SRP verify requests for async worker processing.
		/// </summary>
		private System.Threading.Channels.Channel<SrpVerifyRequest<NetworkConnection>> verifyChannel;

		/// <summary>
		/// Bounded channel for queuing SRP proof requests for async worker processing.
		/// </summary>
		private System.Threading.Channels.Channel<SrpProofRequest<NetworkConnection>> proofChannel;

		/// <summary>
		/// Cancellation token source for signalling graceful shutdown of all async workers.
		/// </summary>
		private CancellationTokenSource workerCts;

		/// <summary>
		/// Thread-safe queue for marshalling network operations from async worker threads
		/// back to the main Unity thread. Workers enqueue Actions, Update() drains them.
		/// ConcurrentQueue avoids lock contention between network thread and worker threads.
		/// </summary>
		private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

		/// <summary>
		/// Verify-stage in-flight gate keyed by ClientId.
		/// Prevents duplicate SRP verify processing for the same connection.
		/// </summary>
		private readonly ConcurrentDictionary<int, byte> verifyInFlightByClientId = new ConcurrentDictionary<int, byte>();

		/// <summary>
		/// Proof-stage in-flight gate keyed by ClientId.
		/// Prevents duplicate SRP proof processing for the same connection.
		/// </summary>
		private readonly ConcurrentDictionary<int, byte> proofInFlightByClientId = new ConcurrentDictionary<int, byte>();

		/// <summary>
		/// Tracks authentication start times for stale-auth TTL enforcement.
		/// Key: ClientId, Value: UTC start timestamp.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> authStartTimeByClientId = new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Reverse map from ClientId to active connection for stale-auth cleanup.
		/// </summary>
		private readonly ConcurrentDictionary<int, NetworkConnection> authConnectionByClientId = new ConcurrentDictionary<int, NetworkConnection>();

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
		/// Countdown timer (seconds) until the next stale-authentication sweep.
		/// </summary>
		private float nextAuthSweepSeconds = AuthSweepIntervalSeconds;

		/// <summary>
		/// Countdown timer (seconds) until the next kick-request debounce cleanup sweep.
		/// </summary>
		private float nextKickDebounceSweepSeconds = KickDebounceSweepIntervalSeconds;

		/// <summary>
		/// Countdown timer (seconds) until the next auth rate-limit cleanup sweep.
		/// </summary>
		private float nextAuthRateLimitSweepSeconds = AuthRateLimitSweepIntervalSeconds;

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
		/// Used for custom client-side authentication result handling.
		/// </summary>
		public event Action<NetworkConnection, bool> OnClientAuthenticationResult;

		/// <summary>
		/// Initializes the authenticator and registers broadcast handlers for client authentication steps.
		/// Broadcast handlers are registered as unauthenticated so they can process login packets.
		/// </summary>
		/// <param name="networkManager">The network manager instance.</param>
		public override void InitializeOnce(NetworkManager networkManager)
		{
			base.InitializeOnce(networkManager);

			// Subscribe to remote connection state changes to clean up accounts on disconnect.
			networkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;

			// Register handlers for client authentication broadcasts.
			networkManager.ServerManager.RegisterBroadcast<ClientHandshake>(OnServerClientHandshakeReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
		}

		/// <summary>
		/// Initializes bounded channels and starts async workers for processing SRP requests.
		/// Called after the Server reference is assigned and infrastructure is ready.
		/// </summary>
		public void InitializeWorkers()
		{
			ShutdownWorkers();

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

			workerCts = new CancellationTokenSource();

			for (int i = 0; i < VerifyWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessSrpVerifyRequestsAsync(workerCts.Token, workerId);
			}

			for (int i = 0; i < ProofWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessSrpProofRequestsAsync(workerCts.Token, workerId);
			}

			Log.Debug("ServerAuthenticator", $"Workers initialized (Verify={VerifyWorkerCount}, Proof={ProofWorkerCount})");
		}

		/// <summary>
		/// Gracefully shuts down all async workers and disposes channel resources.
		/// Drains any remaining queued main-thread actions before clearing channels.
		/// </summary>
		public void ShutdownWorkers()
		{
			workerCts?.Cancel();
			workerCts?.Dispose();
			workerCts = null;
			verifyChannel = null;
			proofChannel = null;

			verifyInFlightByClientId.Clear();
			proofInFlightByClientId.Clear();
			authStartTimeByClientId.Clear();
			authConnectionByClientId.Clear();
			kickRequestNextAllowedUtcByAccount.Clear();
			ipAuthNextAllowedUtc.Clear();
			accountVerifyNextAllowedUtc.Clear();
			connectionIpCache.Clear();

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue(drainAll: true);
		}

		/// <summary>
		/// Drains the main-thread response queue each frame. All network operations
		/// (Broadcast, Disconnect, OnAuthentication) from async workers are marshalled
		/// through this queue to ensure they execute on the main Unity thread.
		/// </summary>
		private void Update()
		{
			DrainMainThreadQueue(drainAll: false);

			nextAuthSweepSeconds -= Time.deltaTime;
			if (nextAuthSweepSeconds <= 0f)
			{
				nextAuthSweepSeconds = AuthSweepIntervalSeconds;
				SweepStaleAuthentication();
				SweepStaleUnauthenticatedAccountState();
			}

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

		/// <summary>
		/// Drains queued actions without locks using ConcurrentQueue.
		/// This reduces contention when many workers and the network thread enqueue simultaneously.
		/// </summary>
		private void DrainMainThreadQueue(bool drainAll)
		{
			int maxActions = drainAll ? int.MaxValue : MaxMainThreadActionsPerUpdate;
			for (int i = 0; i < maxActions; i++)
			{
				if (!mainThreadQueue.TryDequeue(out Action action))
				{
					return;
				}

				action.Invoke();
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private void EnqueueMainThread(Action action)
		{
			mainThreadQueue.Enqueue(action);
		}

		#region UDP Receiver Gates

		/// <summary>
		/// UDP gate: Handles the initial handshake broadcast from a client.
		/// Sets up AES encryption for the connection. No channel needed — this is pure in-memory crypto
		/// with no database or heavy SRP work, so it runs inline on the network thread.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="msg">The handshake message containing the client's RSA public key.</param>
		/// <param name="channel">The network channel used.</param>
		internal void OnServerClientHandshakeReceived(NetworkConnection conn, ClientHandshake msg, Channel channel)
		{
			/* If client is already authenticated this could be an attack. Connections
			 * are removed when a client disconnects so there is no reason they should
			 * already be considered authenticated. */
			if (conn.IsAuthenticated ||
				msg.PublicKey == null ||
				msg.PublicKey.Length > MaxRsaPublicKeyBytes)
			{
				conn.Disconnect(true);
				return;
			}

			// Ignore repeated handshake attempts while verify/proof is already in progress.
			if (verifyInFlightByClientId.ContainsKey(conn.ClientId) || proofInFlightByClientId.ContainsKey(conn.ClientId))
			{
				return;
			}

			TrackAuthStart(conn);

			// Generate encryption keys for the connection and store them in AccountManager.
			Server.AccountManager.AddConnectionEncryptionData(conn, msg.PublicKey);

			// Retrieve the generated encryption data for this connection.
			if (Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				// Reconstruct the client's public key from the wire bytes.
				var clientPublicKey = CryptoHelper.ImportPublicKey(msg.PublicKey);

				// Encrypt the symmetric key and IV using the client's public key (OAEP-SHA256 via BouncyCastle).
				byte[] encryptedSymmetricKey = CryptoHelper.EncryptRsaOaepSha256(clientPublicKey, encryptionData.SymmetricKey);
				byte[] encryptedIV = CryptoHelper.EncryptRsaOaepSha256(clientPublicKey, encryptionData.IV);

				// Send the encrypted symmetric key and IV to the client for secure communication.
				ServerHandshake handshake = new ServerHandshake()
				{
					Key = encryptedSymmetricKey,
					IV = encryptedIV,
				};
				NetworkManager.ServerManager.Broadcast(conn, handshake, false, Channel.Reliable);
			}
			else
			{
				Log.Warning("ServerAuthenticator", "Failed to generate encryption keys for connection.");
				conn.Disconnect(true);
			}
		}

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
				}, false, Channel.Unreliable);
				return;
			}

			// Connection-level in-flight lock: only one verify request at a time per connection.
			if (!verifyInFlightByClientId.TryAdd(conn.ClientId, 0))
			{
				return;
			}

			TrackAuthStart(conn);

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no decryption on network thread
			if (msg.S == null || msg.S.Length > MaxSrpPayloadBytes ||
				msg.PublicEphemeral == null || msg.PublicEphemeral.Length > MaxSrpPayloadBytes)
			{
				verifyInFlightByClientId.TryRemove(conn.ClientId, out _);
				conn.Disconnect(true);
				return;
			}

			var request = new SrpVerifyRequest<NetworkConnection>(
				conn,
				msg.S,
				msg.PublicEphemeral,
				encryptionData.SymmetricKey,
				encryptionData.IV
			);

			if (verifyChannel == null || !verifyChannel.Writer.TryWrite(request))
			{
				verifyInFlightByClientId.TryRemove(conn.ClientId, out _);
				// Channel full or not initialized — reject immediately
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

			// Proof is only valid after verify has started.
			if (!verifyInFlightByClientId.ContainsKey(conn.ClientId))
			{
				return;
			}

			// Only one proof in-flight per connection.
			if (!proofInFlightByClientId.TryAdd(conn.ClientId, 0))
			{
				return;
			}

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				proofInFlightByClientId.TryRemove(conn.ClientId, out _);
				PurgeConnectionAuthState(conn, disconnect: false);
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no SRP math on network thread
			if (msg.Proof == null || msg.Proof.Length > MaxSrpPayloadBytes)
			{
				proofInFlightByClientId.TryRemove(conn.ClientId, out _);
				conn.Disconnect(true);
				return;
			}

			var request = new SrpProofRequest<NetworkConnection>(
				conn,
				msg.Proof,
				encryptionData.SymmetricKey,
				encryptionData.IV
			);

			if (proofChannel == null || !proofChannel.Writer.TryWrite(request))
			{
				proofInFlightByClientId.TryRemove(conn.ClientId, out _);
				// Channel full or not initialized — reject immediately
				NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
				{
					Result = ClientAuthenticationResult.ServerBusy,
				}, false, Channel.Reliable);
				conn.Disconnect(false);
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
				await foreach (var request in verifyChannel.Reader.ReadAllAsync(cancellationToken))
				{
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
			catch (OperationCanceledException)
			{
				await Log.Debug("ServerAuthenticator", $"Verify worker {workerId} cancelled");
			}
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
				await foreach (var request in proofChannel.Reader.ReadAllAsync(cancellationToken))
				{
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
			catch (OperationCanceledException)
			{
				await Log.Debug("ServerAuthenticator", $"Proof worker {workerId} cancelled");
			}
		}

		#endregion

		#region Request Processing

		/// <summary>
		/// Processes a single SRP verify request asynchronously.
		/// Decrypts credentials, checks online status, fetches account data, and initializes SRP state.
		/// All network operations are marshalled to the main thread via the response queue.
		/// </summary>
		/// <param name="request">The SRP verify request with encrypted credentials.</param>
		private async Task ProcessSrpVerifyAsync(SrpVerifyRequest<NetworkConnection> request)
		{
			NetworkConnection conn = request.Connection;
			ClientAuthenticationResult result;
			bool waitingForProof = false;

			// Decrypt the username on worker thread (not network thread).
			string username;
			try
			{
				byte[] decryptedRawUsername = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedUsername);
				username = Encoding.UTF8.GetString(decryptedRawUsername);
				// zero decrypted buffer
				CryptographicOperations.ZeroMemory(decryptedRawUsername);
			}
			catch (CryptographicException ex)
			{
				await Log.Warning("ServerAuthenticator", $"AES decryption/authentication failed for SRP verify: {ex.Message}");
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			if (!TryBeginAccountVerifyAttempt(username))
			{
				result = ClientAuthenticationResult.ServerBusy;
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = result,
						}, false, Channel.Unreliable);
					}
				});

				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService) ||
				!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var kickRequestService) ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				result = ClientAuthenticationResult.ServerBusy;
			}
			else
			{
				try
				{
					// Check if any characters are already online for this account.
					DatabaseResult<IReadOnlyList<CharacterData>> charactersResult = await characterService.FetchManyAsync(username);

					bool isOnline = false;
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

					if (isOnline)
					{
						// Add a kick request for the online character (rate-limited per account).
						if (TryBeginKickRequest(username))
						{
							await kickRequestService.PersistAsync(username);
						}
						result = ClientAuthenticationResult.AlreadyOnline;
					}
					else
					{
						// Decrypt the public ephemeral value on worker thread.
						string publicEphemeral;
						try
						{
							byte[] decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedPublicEphemeral);
							publicEphemeral = Encoding.UTF8.GetString(decryptedRawPublicEphemeral);
							CryptographicOperations.ZeroMemory(decryptedRawPublicEphemeral);
						}
						catch (CryptographicException ex)
						{
							await Log.Warning("ServerAuthenticator", $"AES decryption/authentication failed for public ephemeral: {ex.Message}");
							PurgeConnectionAuthState(conn, disconnect: true);
							return;
						}

						// Fetch account for login from database.
						DatabaseResult<Database.Data.AccountData> loginResult = await accountService.FetchForLoginAsync(username);

						if (!loginResult.IsSuccess)
						{
							result = loginResult.ErrorCode == DatabaseErrorCodes.Forbidden
								? ClientAuthenticationResult.Banned
								: ClientAuthenticationResult.InvalidUsernameOrPassword;
						}
						else
						{
							Database.Data.AccountData accountData = loginResult.Data;
							string salt = accountData.Salt;
							string verifier = accountData.Verifier;
							AccessLevel accessLevel = (AccessLevel)accountData.AccessLevel;

							result = ClientAuthenticationResult.SrpVerify;

							// Prepare account data for SRP verification (thread-safe AccountManager).
							Server.AccountManager.AddConnectionAccount(conn, username, publicEphemeral, salt, verifier, accessLevel);

							// Atomically transition SRP state. Encryption runs inside the lock
							// (pure computation), but the Broadcast is enqueued for the main thread.
							string srpSalt = null;
							string srpPublicServerEphemeral = null;

							if (Server.AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpVerify, (a) =>
								{
									// Extract SRP data inside the lock; encrypt outside to reduce lock hold time.
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
									encryptedSalt = CryptoHelper.EncryptAES(request.SymmetricKey, request.IV, Encoding.UTF8.GetBytes(srpSalt));
									encryptedPublicServerEphemeral = CryptoHelper.EncryptAES(request.SymmetricKey, request.IV, Encoding.UTF8.GetBytes(srpPublicServerEphemeral));
								}
								catch (CryptographicException ex)
								{
									await Log.Error("ServerAuthenticator", $"AES encryption failed for SRP response: {ex.Message}");
									PurgeConnectionAuthState(conn, disconnect: true);
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
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("ServerAuthenticator", $"Error during SRP verify: {ex}");
					result = ClientAuthenticationResult.ServerBusy;
				}
			}

			// Marshal authentication result to main thread.
			EnqueueMainThread(() =>
			{
				if (conn.IsActive)
				{
					NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
					{
						Result = result,
					}, false, Channel.Reliable);
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
			bool authenticationSucceeded = false;

			// Decrypt client proof on worker thread.
			string clientProof;
			try
			{
				byte[] decryptedClientProof = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedClientProof);
				clientProof = Encoding.UTF8.GetString(decryptedClientProof);
				CryptographicOperations.ZeroMemory(decryptedClientProof);
			}
			catch (CryptographicException ex)
			{
				await Log.Warning("ServerAuthenticator", $"AES decryption/authentication failed for client proof: {ex.Message}");
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = ClientAuthenticationResult.InvalidUsernameOrPassword,
						}, false, Channel.Reliable);
						conn.Disconnect(false);
					}
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			string serverProof = null;
			string username = null;

			// Atomically validate proof and advance SRP state.
			bool proofValid = Server.AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpProof, (a) =>
			{
				if (a.SrpData.GetProof(clientProof, out string proof))
				{
					serverProof = proof;
					username = a.SrpData.UserName;
					return true;
				}
				return false;
			});

			if (!proofValid || serverProof == null || username == null)
			{
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = ClientAuthenticationResult.InvalidUsernameOrPassword,
						}, false, Channel.Reliable);
						conn.Disconnect(false);
					}
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			// Advance to SrpSuccess state.
			if (!Server.AccountManager.TryUpdateSrpState(conn, SrpState.SrpProof, SrpState.SrpSuccess))
			{
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = ClientAuthenticationResult.InvalidUsernameOrPassword,
						}, false, Channel.Reliable);
						conn.Disconnect(false);
					}
				});
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}

			try
			{
				// Attempt to complete login authentication (virtual — overridden by WorldServer/SceneServer).
				ClientAuthenticationResult result = await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username);

				bool authenticated = result != ClientAuthenticationResult.InvalidUsernameOrPassword &&
								 result != ClientAuthenticationResult.ServerBusy;
				authenticationSucceeded = authenticated;

				// Encrypt server proof on worker thread.
				byte[] encryptedServerProof = CryptoHelper.EncryptAES(request.SymmetricKey, request.IV, Encoding.UTF8.GetBytes(serverProof));

				// Marshal final broadcast + authentication events to main thread.
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						SrpSuccessBroadcast resultMsg = new SrpSuccessBroadcast()
						{
							Proof = encryptedServerProof,
							Result = result,
						};
						NetworkManager.ServerManager.Broadcast(conn, resultMsg, false, Channel.Reliable);
					}

					/* Invoke result. This is handled internally to complete the connection authentication or kick client.
					 * It's important to call this after sending the broadcast so that the broadcast
					 * makes it out to the client before the kick. */
					OnAuthentication(conn, authenticated);
					OnClientAuthenticationResult?.Invoke(conn, authenticated);

					if (!authenticated)
					{
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
			finally
			{
				proofInFlightByClientId.TryRemove(clientId, out _);

				if (!authenticationSucceeded)
				{
					verifyInFlightByClientId.TryRemove(clientId, out _);
				}
			}
		}

		#endregion

		/// <summary>
		/// Invokes the authentication result event for a connection.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="authenticated">True if authentication succeeded, false otherwise.</param>
		public virtual void OnAuthentication(NetworkConnection conn, bool authenticated)
		{
			OnAuthenticationResult?.Invoke(conn, authenticated);
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
			return Task.FromResult(ClientAuthenticationResult.LoginSuccess);
		}

		/// <summary>
		/// Handles remote connection state changes to clean up account data when a connection stops.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="args">Arguments describing the connection state change.</param>
		private void ServerManager_OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
		{
			if (args.ConnectionState == RemoteConnectionState.Stopped)
			{
				if (conn != null)
				{
					connectionIpCache.Remove(conn.ClientId);
				}

				PurgeConnectionAuthState(conn, disconnect: false);
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

			string ip = conn.GetAddress();
			if (string.IsNullOrWhiteSpace(ip))
			{
				return string.Empty;
			}

			connectionIpCache.Upsert(conn.ClientId, ip, DateTime.UtcNow);
			return ip;
		}

		/// <summary>
		/// Starts auth TTL tracking for a connection if not already tracked.
		/// </summary>
		/// <param name="conn">Connection entering the authentication flow.</param>
		private void TrackAuthStart(NetworkConnection conn)
		{
			if (conn == null)
			{
				return;
			}

			authStartTimeByClientId.TryAdd(conn.ClientId, DateTime.UtcNow);
			authConnectionByClientId[conn.ClientId] = conn;
		}

		/// <summary>
		/// Clears transient per-connection authenticator gate and TTL tracking state.
		/// </summary>
		/// <param name="clientId">FishNet client ID.</param>
		private void ClearTransientAuthState(int clientId)
		{
			verifyInFlightByClientId.TryRemove(clientId, out _);
			proofInFlightByClientId.TryRemove(clientId, out _);
			authStartTimeByClientId.TryRemove(clientId, out _);
			authConnectionByClientId.TryRemove(clientId, out _);
		}

		/// <summary>
		/// Purges all authenticator state for a connection and optionally disconnects it.
		/// </summary>
		/// <param name="conn">Connection to purge.</param>
		/// <param name="disconnect">If true and active, disconnect the client after purge.</param>
		private void PurgeConnectionAuthState(NetworkConnection conn, bool disconnect)
		{
			if (conn == null)
			{
				return;
			}

			ClearTransientAuthState(conn.ClientId);
			connectionIpCache.Remove(conn.ClientId);
			Server.AccountManager.RemoveConnectionAccount(conn);

			if (disconnect && conn.IsActive)
			{
				conn.Disconnect(false);
			}
		}

		/// <summary>
		/// Disconnects and purges connections that exceeded the authentication TTL window
		/// without completing SRP success.
		/// </summary>
		private void SweepStaleAuthentication()
		{
			if (authStartTimeByClientId.Count == 0)
			{
				return;
			}

			DateTime now = DateTime.UtcNow;
			foreach (KeyValuePair<int, DateTime> kvp in authStartTimeByClientId)
			{
				if ((now - kvp.Value).TotalSeconds < AuthStaleTtlSeconds)
				{
					continue;
				}

				if (authConnectionByClientId.TryGetValue(kvp.Key, out NetworkConnection conn) && conn != null)
				{
					PurgeConnectionAuthState(conn, disconnect: true);
				}
				else
				{
					ClearTransientAuthState(kvp.Key);
				}
			}
		}

		/// <summary>
		/// Sweeps stale unauthenticated account/encryption state held by AccountManager.
		/// This is a backstop for SRP memory cleanup in case network disconnect events are delayed.
		/// </summary>
		private void SweepStaleUnauthenticatedAccountState()
		{
			if (Server?.AccountManager == null)
			{
				return;
			}

			Server.AccountManager.SweepUnauthenticatedConnections(
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