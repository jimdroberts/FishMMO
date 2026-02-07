using FishNet.Authenticating;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core;
using FishMMO.Server.Core.Account;
using FishMMO.Server.Core.Account.SRP;
using FishMMO.Server.Core.Authentication;
using FishMMO.Shared;
using FishMMO.Logging;

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

		private System.Threading.Channels.Channel<SrpVerifyRequest<NetworkConnection>> verifyChannel;
		private System.Threading.Channels.Channel<SrpProofRequest<NetworkConnection>> proofChannel;
		private CancellationTokenSource workerCts;

		/// <summary>
		/// Thread-safe queue for marshalling network operations from async worker threads
		/// back to the main Unity thread. Workers enqueue Actions, Update() drains them.
		/// Protected by _queueLock for thread safety.
		/// </summary>
		private readonly Queue<Action> mainThreadQueue = new Queue<Action>();
		private readonly object _queueLock = new object();

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

			// Drain remaining responses so clients get their final messages.
			DrainMainThreadQueue();
		}

		/// <summary>
		/// Drains the main-thread response queue each frame. All network operations
		/// (Broadcast, Disconnect, OnAuthentication) from async workers are marshalled
		/// through this queue to ensure they execute on the main Unity thread.
		/// </summary>
		private void Update()
		{
			DrainMainThreadQueue();
		}

		/// <summary>
		/// Copies all queued actions under lock, then invokes them outside the lock.
		/// This minimizes lock hold time and avoids potential re-entrancy issues.
		/// </summary>
		private void DrainMainThreadQueue()
		{
			Action[] actions;
			lock (_queueLock)
			{
				if (mainThreadQueue.Count == 0)
				{
					return;
				}
				actions = mainThreadQueue.ToArray();
				mainThreadQueue.Clear();
			}

			for (int i = 0; i < actions.Length; i++)
			{
				actions[i].Invoke();
			}
		}

		/// <summary>
		/// Thread-safe enqueue of an action to be executed on the main Unity thread.
		/// </summary>
		/// <param name="action">The action to execute on the main thread.</param>
		private void EnqueueMainThread(Action action)
		{
			lock (_queueLock)
			{
				mainThreadQueue.Enqueue(action);
			}
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
				msg.PublicKey == null)
			{
				conn.Disconnect(true);
				return;
			}

			// Generate encryption keys for the connection and store them in AccountManager.
			Server.AccountManager.AddConnectionEncryptionData(conn, msg.PublicKey);

			// Retrieve the generated encryption data for this connection.
			if (Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				using (var rsa = RSA.Create(2048))
				{
					// Import the client's public key into the RSA instance.
					CryptoHelper.ImportPublicKey(rsa, msg.PublicKey);

					// Encrypt the symmetric key and IV using the client's public key.
					byte[] encryptedSymmetricKey = rsa.Encrypt(encryptionData.SymmetricKey, RSAEncryptionPadding.Pkcs1);
					byte[] encryptedIV = rsa.Encrypt(encryptionData.IV, RSAEncryptionPadding.Pkcs1);

					// Send the encrypted symmetric key and IV to the client for secure communication.
					ServerHandshake handshake = new ServerHandshake()
					{
						Key = encryptedSymmetricKey,
						IV = encryptedIV,
					};
					NetworkManager.ServerManager.Broadcast(conn, handshake, false, Channel.Reliable);
				}
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

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no decryption on network thread
			var request = new SrpVerifyRequest<NetworkConnection>(
				conn,
				msg.S,
				msg.PublicEphemeral,
				encryptionData.SymmetricKey,
				encryptionData.IV
			);

			if (verifyChannel == null || !verifyChannel.Writer.TryWrite(request))
			{
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

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				conn.Disconnect(true);
				return;
			}

			// Enqueue encrypted data for async processing — no SRP math on network thread
			var request = new SrpProofRequest<NetworkConnection>(
				conn,
				msg.Proof,
				encryptionData.SymmetricKey,
				encryptionData.IV
			);

			if (proofChannel == null || !proofChannel.Writer.TryWrite(request))
			{
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

			// Decrypt the username on worker thread (not network thread).
			byte[] decryptedRawUsername = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedUsername);
			string username = Encoding.UTF8.GetString(decryptedRawUsername);

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
						// Add a kick request for the online character.
						await kickRequestService.PersistAsync(username);
						result = ClientAuthenticationResult.AlreadyOnline;
					}
					else
					{
						// Decrypt the public ephemeral value on worker thread.
						byte[] decryptedRawPublicEphemeral = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedPublicEphemeral);
						string publicEphemeral = Encoding.UTF8.GetString(decryptedRawPublicEphemeral);

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
							byte[] encryptedSalt = null;
							byte[] encryptedPublicServerEphemeral = null;

							if (Server.AccountManager.TryUpdateSrpState(conn, SrpState.SrpVerify, SrpState.SrpVerify, (a) =>
								{
									encryptedSalt = CryptoHelper.EncryptAES(request.SymmetricKey, request.IV, Encoding.UTF8.GetBytes(a.SrpData.Salt));
									encryptedPublicServerEphemeral = CryptoHelper.EncryptAES(request.SymmetricKey, request.IV, Encoding.UTF8.GetBytes(a.SrpData.ServerEphemeral.Public));
									return true;
								}))
							{
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

			// Decrypt client proof on worker thread.
			byte[] decryptedClientProof = CryptoHelper.DecryptAES(request.SymmetricKey, request.IV, request.EncryptedClientProof);
			string clientProof = Encoding.UTF8.GetString(decryptedClientProof);

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
				return;
			}

			try
			{
				// Attempt to complete login authentication (virtual — overridden by WorldServer/SceneServer).
				ClientAuthenticationResult result = await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, username);

				bool authenticated = result != ClientAuthenticationResult.InvalidUsernameOrPassword &&
								 result != ClientAuthenticationResult.ServerBusy;

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
				});
			}
			catch (Exception ex)
			{
				await Log.Error("ServerAuthenticator", $"Error during SRP proof login: {ex}");
				EnqueueMainThread(() =>
				{
					conn.Disconnect(false);
				});
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
				Server.AccountManager.RemoveConnectionAccount(conn);
			}
		}
	}
}