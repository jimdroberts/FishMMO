using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Server.Core.Account;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Token-based authenticator for World and Scene servers.
	/// Extends <see cref="BaseServerAuthenticator"/> for shared X25519 ECDH handshake, main-thread
	/// marshalling, and stale-auth TTL sweeps.
	/// <para>
	/// Flow: ClientHandshake → ServerHandshake → TokenAuthBroadcast → ClientAuthResultBroadcast.
	/// </para>
	/// </summary>
	public class TokenServerAuthenticator : BaseServerAuthenticator
	{
		#region Constants

		/// <summary>
		/// Number of concurrent workers processing token auth requests.
		/// </summary>
		private const int TokenWorkerCount = 2;

		/// <summary>
		/// Bounded channel capacity for token auth requests.
		/// </summary>
		private const int TokenChannelCapacity = 500;

		/// <summary>
		/// Maximum allowed size in bytes for an encrypted token payload.
		/// </summary>
		private const int MaxTokenPayloadBytes = 2048;

		#endregion

		#region Nested Types

		/// <summary>
		/// Token authentication request for async worker processing.
		/// </summary>
		private readonly struct TokenAuthRequest
		{
			public readonly NetworkConnection Connection;
			public readonly byte[] EncryptedToken;
			public readonly ConnectionEncryptionData EncryptionData;
			public readonly uint Seq;

			public TokenAuthRequest(NetworkConnection connection, byte[] encryptedToken, ConnectionEncryptionData encryptionData, uint seq)
			{
				Connection = connection;
				EncryptedToken = encryptedToken;
				EncryptionData = encryptionData;
				Seq = seq;
			}
		}

		#endregion

		#region Fields

		/// <summary>
		/// Bounded channel for queuing token auth requests for async worker processing.
		/// </summary>
		private System.Threading.Channels.Channel<TokenAuthRequest> tokenChannel;

		/// <summary>
		/// Typed reference to the token account manager. Cached on worker initialization
		/// to avoid repeated casts from <see cref="IAccountManager{TConnection}"/>.
		/// </summary>
		private ITokenAccountManager<NetworkConnection> tokenAccountManager;

		#endregion

		#region Lifecycle Overrides

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<TokenAuthBroadcast>(OnServerTokenAuthBroadcastReceived, false);
		}

		/// <inheritdoc/>
		protected override void InitializeWorkersCore(CancellationToken cancellationToken)
		{
			if (!(Server.AccountManager is ITokenAccountManager<NetworkConnection> tam))
				throw new InvalidOperationException($"{LogPrefix}: Server.AccountManager must implement ITokenAccountManager<NetworkConnection>. Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			tokenAccountManager = tam;

			tokenChannel = System.Threading.Channels.Channel.CreateBounded<TokenAuthRequest>(
				new System.Threading.Channels.BoundedChannelOptions(TokenChannelCapacity)
				{
					FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
					SingleReader = false,
					SingleWriter = false
				});

			for (int i = 0; i < TokenWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessTokenAuthRequestsAsync(cancellationToken, workerId);
			}

			Log.Debug(LogPrefix, $"Workers initialized (Token={TokenWorkerCount})");
		}

		/// <inheritdoc/>
		protected override void ShutdownWorkersCore()
		{
			tokenChannel?.Writer.TryComplete();
			tokenChannel = null;
		}

		#endregion

		#region UDP Receiver Gates

		/// <summary>
		/// UDP gate: Receives token auth broadcast, validates connection state, and enqueues
		/// encrypted data for async processing. Zero blocking — no decryption or database work.
		/// </summary>
		internal void OnServerTokenAuthBroadcastReceived(NetworkConnection conn, TokenAuthBroadcast msg, Channel channel)
		{
			if (conn.IsAuthenticated)
			{
				conn.Disconnect(true);
				return;
			}

			// Atomically advance Handshake → TokenPending to prevent duplicate token processing.
			if (!Server.AccountManager.TryAdvanceAuthState(conn, AuthState.Handshake, AuthState.TokenPending))
				return;

			if (!Server.AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				conn.Disconnect(true);
				return;
			}

			if (msg.Token == null || msg.Token.Length == 0 || msg.Token.Length > MaxTokenPayloadBytes)
			{
				conn.Disconnect(true);
				return;
			}

			var request = new TokenAuthRequest(conn, msg.Token, encryptionData, msg.Seq);

			if (tokenChannel == null || !tokenChannel.Writer.TryWrite(request))
			{
				RejectAndPurge(conn, ClientAuthenticationResult.ServerBusy);
			}
		}

		#endregion

		#region Async Workers

		/// <summary>
		/// Async worker that processes token auth requests from the bounded channel.
		/// <para><b>Rate limiting:</b> Per-connection token-attempt rate limiting is enforced
		/// by the TryAdvanceAuthState gate (Handshake → TokenPending) in the UDP receiver.
		/// A connection can only submit one token attempt; further attempts fail the state advance.</para>
		/// <para><b>Token reuse:</b> The same token may be presented to multiple World/Scene servers
		/// within its validity window (e.g., during server transfers). Single-use enforcement is
		/// intentionally not applied. Replay is bounded by the expiration window and can be
		/// explicitly terminated via token revocation in the database.</para>
		/// <para><b>Logging:</b> Warning-level logs for invalid tokens are bounded by the
		/// TryAdvanceAuthState gate (one attempt per connection). Under a connection flood,
		/// consider reducing log level or adding log-rate sampling.</para>
		/// </summary>
		private async Task ProcessTokenAuthRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"Token worker {workerId} started");
			try
			{
				// Capture a local reference to the channel to prevent a NullReferenceException
				// if ShutdownWorkersCore nulls the field between TryComplete and worker exit.
				var channel = tokenChannel;
				if (channel == null)
				{
					await Log.Warning(LogPrefix, $"Token worker {workerId}: channel is null at start.");
					return;
				}

				// Rely on channel completion (TryComplete in ShutdownWorkers) for graceful exit.
				// CancellationToken.None avoids a redundant cancellation race with completion.
				await foreach (var request in channel.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					try
					{
						await ProcessTokenAuthAsync(request);
					}
					catch (Exception ex)
					{
						await Log.Error(LogPrefix, $"Token worker {workerId} error: {ex}");
					}
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				await Log.Error(LogPrefix, $"Token worker {workerId} unexpected error: {ex}");
			}

			await Log.Debug(LogPrefix, $"Token worker {workerId} stopped");
		}

		/// <summary>
		/// Processes a single token auth request asynchronously.
		/// Decrypts token, verifies HMAC, checks expiration/revocation, and finalizes authentication.
		/// </summary>
		private async Task ProcessTokenAuthAsync(TokenAuthRequest request)
		{
			NetworkConnection conn = request.Connection;
			int clientId = conn.ClientId;
			byte[] rawToken = null;

			try
			{
				// Validate and consume explicit client→server sequence
				if (request.Seq == 0 || !request.EncryptionData.TryConsumeReceiveSequence(request.Seq))
				{
					await Log.Warning(LogPrefix, "Token auth sequence invalid or duplicate.");
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Decrypt the token on the worker thread
				try
				{
					byte[] nonce = request.EncryptionData.BuildReceiveNonce(request.Seq);
					byte[] aad = CryptoHelper.BuildAad((byte)CryptoHelper.AuthMessageType.TokenAuth, request.EncryptionData.AgreedVersion, request.Seq);
					rawToken = CryptoHelper.DecryptAES(request.EncryptionData.ClientToServerKey, nonce, request.EncryptedToken, aad);
				}
				catch (CryptographicException)
				{
					await Log.Warning(LogPrefix, "AES decryption failed for token auth.");
					rawToken = null;
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Validate minimum token structure length.
				// MinSignedTokenLength (69 bytes) guarantees at least 1 byte of account name
				// plus all fixed fields. The subsequent nameLen extraction (rawToken[2..3])
				// relies on rawToken having at least 4 bytes, which is always true since
				// MinSignedTokenLength > 4.
				if (rawToken.Length < CryptoHelper.MinSignedTokenLength)
				{
					await Log.Warning(LogPrefix, "Token too short.");
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Fetch DB services
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var signingKeyService) ||
					!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var authTokenService))
				{
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.ServerBusy);
					return;
				}

				// Extract loginServerId from token payload (partial parse to look up signing key).
				// Token format: [1B version][1B tokenType][2B nameLen BE][name][8B serverId]...
				int nameLen = (rawToken[2] << 8) | rawToken[3];
				if (nameLen <= 0 || 4 + nameLen + 8 + 8 + 16 + CryptoHelper.HmacTagLength > rawToken.Length)
				{
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				int serverIdOffset = 4 + nameLen;
				long loginServerId = 0;
				for (int i = 0; i < 8; i++)
					loginServerId = (loginServerId << 8) | rawToken[serverIdOffset + i];

				// Fetch the HMAC signing key for the issuing LoginServer
				DatabaseResult<LoginServerSigningKeyData> keyResult = await signingKeyService.FetchByLoginServerIdAsync(loginServerId);

				// Refresh TTL after potentially slow database fetch.
				RefreshAuthTtl(conn);

				// Equalize timing between "key not found" and "HMAC invalid" paths
				// to prevent loginServerId enumeration via response-time oracle.
				byte[] hmacKey;
				bool keyFound;
				if (!keyResult.IsSuccess || keyResult.Data.HmacKey == null || keyResult.Data.HmacKey.Length < CryptoHelper.HmacKeyLength)
				{
					// Use a throw-away random key so TryParseAndVerifyAuthToken still runs,
					// equalizing CPU cost with the success path.
					hmacKey = new byte[CryptoHelper.HmacKeyLength];
					using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
						rng.GetBytes(hmacKey);
					keyFound = false;

					if (!keyResult.IsSuccess || keyResult.Data.HmacKey == null)
						await Log.Warning(LogPrefix, $"Signing key not found for LoginServer {loginServerId}.");
					else
						await Log.Warning(LogPrefix, $"Signing key too short for LoginServer {loginServerId}.");
				}
				else
				{
					hmacKey = keyResult.Data.HmacKey;
					keyFound = true;
				}

				// Verify HMAC and parse token fields
				bool hmacValid = CryptoHelper.TryParseAndVerifyAuthToken(rawToken, hmacKey, out string accountName, out long parsedServerId, out DateTime expiresUtc);
				CryptographicOperations.ZeroMemory(hmacKey);

				if (!keyFound || !hmacValid)
				{
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Cross-check: parsedServerId inside HMAC envelope must match the
				// pre-HMAC partial parse to detect token tampering.
				if (parsedServerId != loginServerId)
				{
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Check expiration
				if (DateTime.UtcNow >= expiresUtc)
				{
					CryptographicOperations.ZeroMemory(rawToken);
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenExpired);
					return;
				}

				// Check revocation via token hash
				string tokenHash = CryptoHelper.HashTokenHex(rawToken);
				CryptographicOperations.ZeroMemory(rawToken);

				DatabaseResult<AuthTokenData> tokenResult = await authTokenService.FetchByHashAsync(tokenHash);

				// Refresh TTL after potentially slow revocation lookup.
				RefreshAuthTtl(conn);

				if (!tokenResult.IsSuccess)
				{
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				if (tokenResult.Data.Revoked)
				{
					SendResultAndDisconnect(conn, ClientAuthenticationResult.TokenRevoked);
					return;
				}

				// accountName is extracted from the HMAC-verified token payload.
				// Canonicalization (e.g., case normalization) must match the LoginServer's
				// token issuance to ensure consistent identity across server types.
				// TODO: Store AccessLevel in the auth token and extract it here
				// instead of hardcoding Player. GM-level tokens currently downgrade.
				tokenAccountManager.AddConnectionAccount(conn, accountName, AccessLevel.Player);

				// Attempt login (virtual — overridden by WorldServer/SceneServer)
				ClientAuthenticationResult result = await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, accountName);

				// Inclusion list: LoginSuccess, WorldLoginSuccess, and SceneLoginSuccess
				// are authenticated. All other result codes (including future additions)
				// default to unauthenticated.
				bool authenticated = result == ClientAuthenticationResult.LoginSuccess ||
									 result == ClientAuthenticationResult.WorldLoginSuccess ||
									 result == ClientAuthenticationResult.SceneLoginSuccess;
				// Marshal final broadcast + authentication events to main thread
				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = result,
						}, false, Channel.Reliable);
					}

					/* Invoke result. This is handled internally to complete the connection authentication or kick client.
					 * It's important to call this after sending the broadcast so that the broadcast
					 * makes it out to the client before the kick. */
					OnAuthentication(conn, authenticated);
					InvokeClientAuthenticationResult(conn, authenticated);

					if (authenticated)
					{
						// Advance to terminal Authenticated state.
						Server.AccountManager.TryAdvanceAuthState(conn, AuthState.TokenPending, AuthState.Authenticated);
					}
					else
					{
						Server.AccountManager.RemoveConnectionAccount(conn);
					}

					ClearTransientAuthState(clientId);
				});
			}
			catch (Exception ex)
			{
				if (rawToken != null) CryptographicOperations.ZeroMemory(rawToken);
				await Log.Error(LogPrefix, $"Error during token auth: {ex}");
				EnqueueMainThread(() =>
				{
					if (conn.IsActive) conn.Disconnect(false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		#endregion
	}
}