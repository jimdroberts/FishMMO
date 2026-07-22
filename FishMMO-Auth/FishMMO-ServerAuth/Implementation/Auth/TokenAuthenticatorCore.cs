using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Logging;

namespace FishMMO.Auth.Implementation
{
	/// <summary>
	/// Engine-independent token-based authenticator core for World/Scene servers.
	/// Extends <see cref="BaseAuthenticatorCore{TConnection}"/> with a bounded-channel
	/// token auth worker that decrypts, verifies, and revocation-checks client-supplied
	/// auth tokens issued by the LoginServer.
	/// <para>
	/// Subclasses supply transport-specific callbacks (broadcast, database validation).
	/// </para>
	/// </summary>
	/// <typeparam name="TConnection">The type representing a network connection.</typeparam>
	public abstract class TokenAuthenticatorCore<TConnection> : BaseAuthenticatorCore<TConnection>
	{
		#region Configuration

		/// <summary>Default number of concurrent token auth worker tasks. Configurable via .cfg AuthTokenWorkerCount.</summary>
		public int TokenWorkerCount { get; set; } = 2;
		/// <summary>Default maximum pending token auth requests in the bounded channel. Configurable via .cfg AuthTokenChannelCapacity.</summary>
		public int TokenChannelCapacity { get; set; } = 500;
		/// <summary>Maximum accepted byte length for an encrypted token payload.</summary>
		private const int MaxTokenPayloadBytes = 2048;

		/// <summary>
		/// Process-lifetime random HMAC key used to equalize timing when a token's claimed
		/// signing key cannot be resolved. Initialized once at type load — never zeroed and
		/// never used to sign real tokens.
		/// </summary>
		private static readonly byte[] StaticDummyHmacKey = GenerateStaticDummyHmacKey();

		private static byte[] GenerateStaticDummyHmacKey()
		{
			byte[] buf = new byte[CryptoHelper.HmacKeyLength];
			using (var rng = RandomNumberGenerator.Create())
				rng.GetBytes(buf);
			return buf;
		}

		#endregion

		#region Fields

		/// <summary>Bounded channel used to decouple token auth receipt from async processing workers.</summary>
		private Channel<TokenAuthRequest>? tokenChannel;
		/// <summary>Token-specific account manager for registering authenticated connections.</summary>
		private ITokenAccountManager<TConnection> tokenAccountManager;

		#endregion

		#region Constructor

		/// <summary>
		/// Initializes the token authenticator core.
		/// </summary>
		/// <param name="accountManager">Token account manager instance.</param>
		protected TokenAuthenticatorCore(ITokenAccountManager<TConnection> accountManager)
			: base(accountManager)
		{
			tokenAccountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
		}

		#endregion

		#region Worker Lifecycle

		/// <inheritdoc/>
		protected override void InitializeWorkersCore(CancellationToken cancellationToken)
		{
			// Clamp user-configurable values to safe ranges.
			TokenWorkerCount = Math.Max(1, Math.Min(TokenWorkerCount, 64));
			TokenChannelCapacity = Math.Max(1, Math.Min(TokenChannelCapacity, 10000));

			tokenChannel = Channel.CreateBounded<TokenAuthRequest>(new BoundedChannelOptions(TokenChannelCapacity)
			{
				FullMode = BoundedChannelFullMode.DropWrite,
				SingleReader = false,
				SingleWriter = false
			});

			for (int i = 0; i < TokenWorkerCount; i++)
			{
				int workerId = i + 1;
				_ = ProcessTokenAuthRequestsAsync(cancellationToken, workerId);
			}

			_ = Log.Debug(LogPrefix, $"Token workers initialized (count={TokenWorkerCount})");
		}

		/// <inheritdoc/>
		protected override void ShutdownWorkersCore()
		{
			tokenChannel?.Writer.TryComplete();
			tokenChannel = null;
		}

		/// <inheritdoc/>
		public override bool IsWorkerIdle =>
			(tokenChannel?.Reader.Completion.IsCompleted ?? true);

		#endregion

		#region UDP Receiver Gate

		/// <summary>
		/// Gate for an incoming token auth broadcast. Validates connection state and enqueues for async processing.
		/// No decryption or database work occurs here.
		/// </summary>
		/// <param name="conn">The network connection.</param>
		/// <param name="encryptedToken">AES-GCM encrypted auth token from client.</param>
		/// <param name="seq">Broadcast sequence number.</param>
		public void OnTokenAuthReceived(TConnection conn, byte[] encryptedToken, uint seq)
		{
			if (IsConnectionAuthenticated(conn))
			{
				DisconnectConnection(conn, graceful: true);
				return;
			}

			// Atomically advance Handshake → TokenPending to prevent duplicate token processing.
			if (!AccountManager.TryAdvanceAuthState(conn, AuthState.Handshake, AuthState.TokenPending))
				return;

			if (!AccountManager.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				DisconnectConnection(conn, graceful: true);
				return;
			}

			if (encryptedToken == null || encryptedToken.Length == 0 || encryptedToken.Length > MaxTokenPayloadBytes)
			{
				PurgeConnectionAuthState(conn, disconnect: true);
				return;
			}

			var request = new TokenAuthRequest(conn, encryptedToken, encryptionData, seq);

			if (tokenChannel == null || !tokenChannel.Writer.TryWrite(request))
			{
				// TOKEN NO-RETRY: Token auth is one-shot by design. Client must reconnect.
				RejectAndPurge(conn, ClientAuthenticationResult.ServerBusy);
			}
		}

		#endregion

		#region Async Worker

		private async Task ProcessTokenAuthRequestsAsync(CancellationToken cancellationToken, int workerId)
		{
			await Log.Debug(LogPrefix, $"Token worker {workerId} started");
			try
			{
				var channel = tokenChannel;
				if (channel == null)
				{
					await Log.Warning(LogPrefix, $"Token worker {workerId}: channel is null at start.");
					return;
				}

				await foreach (var request in channel.Reader.ReadAllAsync(CancellationToken.None))
				{
					if (cancellationToken.IsCancellationRequested) break;
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

		private async Task ProcessTokenAuthAsync(TokenAuthRequest request)
		{
			TConnection conn = request.Connection;
			// Ghost-connection guard: bail early if the client disconnected
			// while this request was queued in the bounded channel.
			if (!IsConnectionActive(conn))
			{
				PurgeConnectionAuthState(conn, disconnect: false);
				return;
			}
			byte[]? rawToken = null;
			byte[]? hmacKey = null;

			try
			{
				if (!TokenService.TryDecryptAndPartialParse(
					request.EncryptedToken, request.EncryptionData, request.Seq,
					out rawToken, out long loginServerId, out long signingKeyId))
				{
					await Log.Warning(LogPrefix, "Token decryption or parse failed.");
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				hmacKey = await FetchSigningKeyAsync(loginServerId, signingKeyId);
				bool keyFound = hmacKey != null;
				RefreshAuthTtl(conn);

				// Use a process-lifetime static dummy HMAC key for the
				// "key not found" timing-equalization path rather than allocating a fresh RNG
				// buffer per request. The value of the dummy key is irrelevant — only its
				// presence (so HMAC verify runs to equalize timing) and length matter, and a
				// reused buffer eliminates per-request RNG and GC pressure on a hot failure path.
				if (!keyFound)
				{
					hmacKey = StaticDummyHmacKey;
				}

				TokenService.TokenVerifyResult verifyResult;
				try
				{
					verifyResult = TokenService.VerifyToken(rawToken!, hmacKey!, keyFound, loginServerId, signingKeyId);
				}
				finally
				{
					// Only zero a real signing key. The static dummy is shared across requests
					// and must not be zero-filled — that would corrupt subsequent verifications
					// and defeat the timing-equalization guarantee.
					if (keyFound && hmacKey != null)
					{
						CryptographicOperations.ZeroMemory(hmacKey);
					}
					hmacKey = null;
				}
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

				// Revocation check
				bool revoked = await CheckTokenRevocationAsync(verifyResult.TokenHash!);
				RefreshAuthTtl(conn);

				if (revoked)
				{
					RejectAndPurge(conn, ClientAuthenticationResult.TokenRevoked);
					return;
				}

				try
				{
					tokenAccountManager.AddConnectionAccount(conn, verifyResult.AccountName!, verifyResult.AccessLevel);

				// Require a verified real IP from the auth token (v4+).
				// World/Scene servers behind an L4 proxy see 127.0.0.1 from
				// conn.GetAddress(). If the IP is missing or unverified,
				// disconnect immediately — no fallback to proxy IP or ClientId.
				if (verifyResult.RealIp == null)
				{
					await Log.Warning(LogPrefix, $"Token for '{verifyResult.AccountName}' missing real IP — rejecting.");
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}
				EnqueueMainThread(conn, () => StoreClientRealIp(conn, verifyResult.RealIp!));
				}
				catch (InvalidOperationException addEx)
				{
					await Log.Error(LogPrefix, $"AddConnectionAccount failed: {addEx.Message}");
					RejectAndPurge(conn, ClientAuthenticationResult.TokenInvalid);
					return;
				}

				ClientAuthenticationResult result = await TryLoginAsync(ClientAuthenticationResult.LoginSuccess, verifyResult.AccountName!);

				bool authenticated = result == ClientAuthenticationResult.LoginSuccess ||
									 result == ClientAuthenticationResult.WorldLoginSuccess ||
									 result == ClientAuthenticationResult.SceneLoginSuccess;

				EnqueueMainThread(conn, () =>
				{
					if (IsConnectionActive(conn))
					{
						BroadcastAuthResult(conn, result, reliable: true);
					}

					OnAuthenticationResult(conn, authenticated);

					if (authenticated)
					{
						AccountManager.TryAdvanceAuthState(conn, AuthState.TokenPending, AuthState.Authenticated);
					}
					else
					{
						AccountManager.RemoveConnectionAccount(conn);
					}
				});

				// Reconnect-only token refresh: after a successful token auth, fire the
				// post-auth hook so the host can issue a freshly-minted auth token with a
				// refreshed expiration window over the existing AES-GCM session channel.
				// Failures here are non-fatal — the client retains its current token.
				if (authenticated)
				{
					try
					{
						await OnTokenAuthSuccessAsync(conn, verifyResult.AccountName!, verifyResult.AccessLevel, loginServerId);
					}
					catch (Exception renewEx)
					{
						await Log.Warning(LogPrefix, $"OnTokenAuthSuccessAsync hook threw (non-fatal): {renewEx.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				if (rawToken != null) CryptographicOperations.ZeroMemory(rawToken);
				if (hmacKey != null) CryptographicOperations.ZeroMemory(hmacKey);
				await Log.Error(LogPrefix, $"Error during token auth: {ex}");
				EnqueueMainThread(conn, () =>
				{
					if (IsConnectionActive(conn)) DisconnectConnection(conn, graceful: false);
				});
				PurgeConnectionAuthState(conn, disconnect: false);
			}
		}

		#endregion

		#region Helpers

		/// <summary>
		/// Rejects and purges a connection from an async worker thread context.
		/// <para>
		/// SAFETY (async-thread / main-thread callback race):
		/// <see cref="PurgeConnectionAuthState"/> zeroes per-connection encryption keys
		/// immediately on the async thread. The <see cref="EnqueueMainThread"/> callback
		/// must NOT reference those keys after they are zeroed. This is safe because:
		/// (a) <see cref="BroadcastAuthResult"/> sends only a pre-computed result code,
		/// not any encrypted payload; and (b) any encrypted payload the caller needs
		/// (e.g., server proof, auth token) MUST be pre-computed <em>before</em>
		/// this method is called. Callers on the async thread must pre-compute all
		/// encrypted payloads before calling <see cref="RejectAndPurge"/>; the
		/// main-thread callback only uses pre-computed data or simple result codes.
		/// </para>
		/// </summary>
		/// <param name="conn">The connection to reject and purge.</param>
		/// <param name="result">The authentication result to broadcast before disconnecting.</param>
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

		/// <summary>
		/// Attempts to complete login for a newly authenticated token connection.
		/// Override to apply server-type-specific login logic.
		/// </summary>
		/// <param name="defaultResult">The result to return if no override logic modifies it.</param>
		/// <param name="username">Account name being authenticated.</param>
		/// <returns>The final <see cref="ClientAuthenticationResult"/> to send to the client.</returns>
		protected virtual Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username)
		{
			return Task.FromResult(defaultResult);
		}

		/// <summary>
		/// Invoked once after a successful token authentication (and the resulting auth-result
		/// broadcast). Override to mint and push a fresh auth token to the client over the
		/// existing AES-GCM session channel, extending the effective session lifetime
		/// past the original LoginServer-issued token's expiration.
		/// <para>
		/// Exceptions thrown here are caught by the caller and logged as warnings; they
		/// do not invalidate the just-completed authentication.
		/// </para>
		/// </summary>
		/// <param name="conn">The newly authenticated connection.</param>
		/// <param name="accountName">Account name extracted from the verified token.</param>
		/// <param name="accessLevel">Access level extracted from the verified token.</param>
		/// <param name="loginServerId">Login server ID extracted from the verified token (used to look up the HMAC signing key for the renewal).</param>
		protected virtual Task OnTokenAuthSuccessAsync(TConnection conn, string accountName, AccessLevel accessLevel, long loginServerId)
		{
			return Task.CompletedTask;
		}

		#endregion

		#region Abstract Transport / DB Callbacks

		/// <summary>
		/// Returns whether the connection is currently active (connected and not disposed).
		/// </summary>
		/// <param name="conn">The connection to check.</param>
		/// <returns><c>true</c> if the connection is still active; otherwise, <c>false</c>.</returns>
		protected abstract bool IsConnectionActive(TConnection conn);

		/// <summary>
		/// Called when authentication completes (success or failure).
		/// Implementations should call their engine's authenticate/reject methods here.
		/// </summary>
		/// <param name="conn">The authenticated (or rejected) connection.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
		protected abstract void OnAuthenticationResult(TConnection conn, bool authenticated);

		/// <summary>
		/// Broadcasts a generic auth result to the client.
		/// </summary>
		/// <summary>
		/// Fetches the HMAC signing key for the given login server ID from the database.
		/// Returns null if the key is not found, too short, or a database error occurred;
		/// the caller will substitute a random dummy key for timing equalization when null is returned.
		/// </summary>
		/// <param name="loginServerId">Login server database ID.</param>
		/// <returns>A fresh copy of the HMAC key, or null.</returns>
		protected abstract Task<byte[]> FetchSigningKeyAsync(long loginServerId, long signingKeyId);

		/// <summary>
		/// Checks whether a token has been revoked, using its SHA-256 hex hash.
		/// </summary>
		/// <param name="tokenHash">SHA-256 hex hash of the raw token.</param>
		/// <returns>True if the token is revoked.</returns>
		protected abstract Task<bool> CheckTokenRevocationAsync(string tokenHash);

		#endregion

		#region Nested Type

		/// <summary>Immutable request ticket passed from the receive-thread gate to a token auth worker.</summary>
		private readonly struct TokenAuthRequest
		{
			/// <summary>The network connection that submitted the token.</summary>
			public readonly TConnection Connection;
			/// <summary>AES-GCM encrypted token bytes received from the client.</summary>
			public readonly byte[] EncryptedToken;
			/// <summary>Per-connection encryption context holding session keys and nonce state.</summary>
			public readonly ConnectionEncryptionData EncryptionData;
			/// <summary>Broadcast sequence number used for replay protection.</summary>
			public readonly uint Seq;

			/// <summary>Initializes a new <see cref="TokenAuthRequest"/>.</summary>
			/// <param name="connection">The network connection.</param>
			/// <param name="encryptedToken">AES-GCM encrypted token bytes.</param>
			/// <param name="encryptionData">Per-connection encryption context.</param>
			/// <param name="seq">Broadcast sequence number.</param>
			public TokenAuthRequest(TConnection connection, byte[] encryptedToken, ConnectionEncryptionData encryptionData, uint seq)
			{
				Connection = connection;
				EncryptedToken = encryptedToken;
				EncryptionData = encryptionData;
				Seq = seq;
			}
		}

		/// <summary>
		/// Stores the real client IP recovered from a verified auth token (v4+).
		/// Override in server-specific subclasses to write to ConnectionIpCache.
		/// </summary>
		protected virtual void StoreClientRealIp(TConnection conn, string realIp) { }

		#endregion
	}
}