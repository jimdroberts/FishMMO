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
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
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
		/// <remarks>
		/// Ordering: TryComplete signals the channel's Reader to stop, then the field is
		/// nulled. Workers capture a local reference at startup (<c>var channel = tokenChannel</c>),
		/// so the null assignment cannot race with an in-progress ReadAllAsync enumeration.
		/// </remarks>
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
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new TokenAuthRequest(conn, msg.Token, encryptionData, msg.Seq);

			if (tokenChannel == null || !tokenChannel.Writer.TryWrite(request))
			{
				// TOKEN NO-RETRY: Unlike the SRP path (which rolls back state to allow
				// retry), token auth is one-shot by design. RejectAndPurge disconnects
				// the client and removes all connection state. The client must reconnect
				// and present the token again on a fresh connection. This is intentional
				// because token auth has no intermediate state worth preserving.
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
				// Defensive backstop: capture a local reference to prevent a NullReferenceException
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
		/// Delegates crypto to <see cref="TokenService"/>, handles DB lookups, revocation, and finalization.
		/// </summary>
		private async Task ProcessTokenAuthAsync(TokenAuthRequest request)
		{
			NetworkConnection conn = request.Connection;
			byte[] rawToken = null;

			try
			{
				// Decrypt and partially parse token to extract loginServerId for signing key lookup.
				if (!TokenService.TryDecryptAndPartialParse(request.EncryptedToken, request.EncryptionData, request.Seq, out rawToken, out long loginServerId))
				{
					await Log.Warning(LogPrefix, "Token decryption or parse failed.");
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Fetch DB services
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var signingKeyService) ||
					!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var authTokenService))
				{
					CryptographicOperations.ZeroMemory(rawToken);
					RejectAndPurge(conn, ClientAuthenticationResult.ServerBusy);
					return;
				}

				// Fetch the HMAC signing key for the issuing LoginServer
				DatabaseResult<LoginServerSigningKeyData> keyResult = await signingKeyService.FetchByLoginServerIdAsync(loginServerId);
				RefreshAuthTtl(conn);

				// Equalize timing: use a random dummy key if signing key not found
				// to prevent loginServerId enumeration via response-time oracle.
				byte[] hmacKey;
				bool keyFound;
				if (!keyResult.IsSuccess || keyResult.Data.HmacKey == null || keyResult.Data.HmacKey.Length < CryptoHelper.HmacKeyLength)
				{
					hmacKey = new byte[CryptoHelper.HmacKeyLength];
					using (var rng = RandomNumberGenerator.Create())
						rng.GetBytes(hmacKey);
					keyFound = false;

					if (!keyResult.IsSuccess || keyResult.Data.HmacKey == null)
						await Log.Warning(LogPrefix, $"Signing key not found for LoginServer {loginServerId}.");
					else
						await Log.Warning(LogPrefix, $"Signing key too short for LoginServer {loginServerId}.");
				}
				else
				{
					hmacKey = new byte[keyResult.Data.HmacKey.Length];
					Buffer.BlockCopy(keyResult.Data.HmacKey, 0, hmacKey, 0, keyResult.Data.HmacKey.Length);
					keyFound = true;
				}

				// Verify HMAC, cross-check loginServerId, and check expiration via TokenService.
				var verifyResult = TokenService.VerifyToken(rawToken, hmacKey, keyFound, loginServerId);
				CryptographicOperations.ZeroMemory(hmacKey);
				CryptographicOperations.ZeroMemory(rawToken);
				rawToken = null;

				if (!verifyResult.IsValid)
				{
					if (verifyResult.SigningKeyFound && verifyResult.ExpiresUtc != default && DateTime.UtcNow >= verifyResult.ExpiresUtc)
						RejectAndPurge(conn, ClientAuthenticationResult.TokenExpired);
					else
						RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Check revocation via token hash (computed by VerifyToken on success).
				DatabaseResult<AuthTokenData> tokenResult = await authTokenService.FetchByHashAsync(verifyResult.TokenHash);
				RefreshAuthTtl(conn);

				if (!tokenResult.IsSuccess)
				{
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				if (tokenResult.Data.Revoked)
				{
					RejectAndPurge(conn, ClientAuthenticationResult.TokenRevoked);
					return;
				}

				// Register account from HMAC-verified token payload.
				try
				{
					tokenAccountManager.AddConnectionAccount(conn, verifyResult.AccountName, verifyResult.AccessLevel);
				}
				catch (InvalidOperationException addEx)
				{
					await Log.Error(LogPrefix, $"AddConnectionAccount failed: {addEx.Message}");
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				// Attempt login (virtual — overridden by WorldServer/SceneServer)
				ClientAuthenticationResult result = await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, verifyResult.AccountName);

				bool authenticated = result == ClientAuthenticationResult.LoginSuccess ||
									 result == ClientAuthenticationResult.WorldLoginSuccess ||
									 result == ClientAuthenticationResult.SceneLoginSuccess;

				EnqueueMainThread(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn, new ClientAuthResultBroadcast()
						{
							Result = result,
						}, false, Channel.Reliable);
					}

					OnAuthentication(conn, authenticated);
					InvokeClientAuthenticationResult(conn, authenticated);

					if (authenticated)
					{
						Server.AccountManager.TryAdvanceAuthState(conn, AuthState.TokenPending, AuthState.Authenticated);
					}
					else
					{
						Server.AccountManager.RemoveConnectionAccount(conn);
					}
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