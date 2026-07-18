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
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Token-based server authenticator for World and Scene servers.
	/// Delegates all handshake, channel, worker, and protocol logic to
	/// <see cref="TokenAuthenticatorCore{TConnection}"/> in FishMMO-Auth.
	/// This class bridges FishNet broadcast events to the core and provides Unity/DB callbacks.
	/// </summary>
	public class TokenServerAuthenticator : BaseServerAuthenticator
	{
		/// <summary>
		/// Lifetime of renewal-issued auth tokens, in minutes. Used when this World/Scene
		/// server mints a fresh token immediately after a successful <see cref="TokenAuthBroadcast"/>
		/// (the reconnect-only refresh flow). Inspector-configurable.
		/// </summary>
		[SerializeField] private float renewalTokenExpirationMinutes = 10f;

		/// <summary>The token-specific core instance. Null until <see cref="InitializeCoreInstance"/> is called.</summary>
		private TokenCore core;

		/// <summary>
		/// Lazily loaded 32-byte AES-256 KEK used to unwrap signing-key blobs returned by the DB.
		/// Cached for the lifetime of the authenticator. <c>null</c> until first fetch attempt.
		/// </summary>
		private volatile byte[] signingKeyKek;

		/// <summary>
		/// Loads (and caches) the deployment KEK. Returns <c>null</c> on failure and emits a
		/// warning log; callers must fail closed.
		/// </summary>
		private byte[] TryGetSigningKeyKek()
		{
			if (this.signingKeyKek != null) return this.signingKeyKek;
			if (!SigningKeyKekProvider.TryLoad(Server.Configuration, out byte[] kek, out string error))
			{
				_ = Log.Warning(LogPrefix, $"Signing-key KEK unavailable: {error}");
				return null;
			}
			this.signingKeyKek = kek;
			return kek;
		}

		/// <inheritdoc/>
		protected override BaseAuthenticatorCore<NetworkConnection> Core => core;

		#region Lifecycle

		/// <inheritdoc/>
		protected override void InitializeCoreInstance()
		{
			var tam = Server.AccountManager as ITokenAccountManager<NetworkConnection>
				?? throw new InvalidOperationException(
					$"{LogPrefix}: Server.AccountManager must implement ITokenAccountManager<NetworkConnection>. " +
					$"Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			core = new TokenCore(this, tam);
		}

		/// <inheritdoc/>
		public override IAccountManager<NetworkConnection> CreateAccountManager() =>
			new TokenAccountManager();

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<TokenAuthBroadcast>(OnServerTokenAuthBroadcastReceived, false);
		}

		#endregion

		#region UDP Receiver Gate (routes to core)

		/// <summary>Routes an incoming <see cref="TokenAuthBroadcast"/> to the core token authentication channel.</summary>
		internal void OnServerTokenAuthBroadcastReceived(NetworkConnection conn, TokenAuthBroadcast msg, Channel channel)
			=> core?.OnTokenAuthReceived(conn, msg.Token, msg.Seq);

		#endregion

		#region DB Implementations (called by TokenCore)

		/// <summary>
		/// Fetches the token-embedded HMAC signing key from the database.
		/// Returns <c>null</c> if the service is unavailable, the key is not found, or the key is too short.
		/// </summary>
		private async Task<byte[]> FetchSigningKeyCoreAsync(long loginServerId, long signingKeyId)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"Signing key service unavailable for LoginServer {loginServerId}, key {signingKeyId}.");
				return null;
			}

			var result = await svc.FetchByIdAsync(signingKeyId);

			if (!result.IsSuccess || result.Data.HmacKey == null)
			{
				await Log.Warning(LogPrefix, $"Signing key {signingKeyId} not found for LoginServer {loginServerId}.");
				return null;
			}

			if (result.Data.LoginServerId != loginServerId)
			{
				await Log.Warning(LogPrefix, $"Signing key {signingKeyId} belongs to LoginServer {result.Data.LoginServerId}, not {loginServerId}.");
				return null;
			}

			// HmacKey is an AES-256-GCM envelope keyed on the deployment KEK with AAD bound
			// to the owning LoginServer id. Unwrap and fail closed on any tag/AAD/structural error.
			byte[] kek = TryGetSigningKeyKek();
			byte[] unwrapped;
			if (kek == null) { if (KeyEnvelope.LooksWrapped(result.Data.HmacKey)) { await Log.Warning(LogPrefix, $"Signing key is wrapped but no KEK configured."); return null; } unwrapped = result.Data.HmacKey; } else { unwrapped = KeyEnvelope.Unwrap(kek, result.Data.HmacKey, SigningKeyKekProvider.BuildAad(loginServerId)); if (unwrapped == null) { await Log.Warning(LogPrefix, $"Signing key failed AEAD unwrap."); return null; } }

			if (unwrapped.Length < CryptoHelper.HmacKeyLength)
			{
				CryptographicOperations.ZeroMemory(unwrapped);
				await Log.Warning(LogPrefix, $"Signing key too short for LoginServer {loginServerId}.");
				return null;
			}

			return unwrapped;
		}

		/// <summary>
		/// Fetches the latest signing key for renewal token issuance.
		/// </summary>
		private async Task<(byte[] Key, long KeyId)> FetchCurrentSigningKeyCoreAsync(long loginServerId)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"Signing key service unavailable for LoginServer {loginServerId}.");
				return (null, 0);
			}

			var result = await svc.FetchByLoginServerIdAsync(loginServerId);
			if (!result.IsSuccess || result.Data.HmacKey == null)
			{
				await Log.Warning(LogPrefix, $"Current signing key not found for LoginServer {loginServerId}.");
				return (null, 0);
			}

			byte[] kek = TryGetSigningKeyKek();
			byte[] unwrapped;
			if (kek == null) { if (KeyEnvelope.LooksWrapped(result.Data.HmacKey)) { await Log.Warning(LogPrefix, $"Current signing key is wrapped but no KEK configured."); return (null, 0); } unwrapped = result.Data.HmacKey; } else { unwrapped = KeyEnvelope.Unwrap(kek, result.Data.HmacKey, SigningKeyKekProvider.BuildAad(loginServerId)); if (unwrapped == null) { await Log.Warning(LogPrefix, $"Current signing key failed AEAD unwrap."); return (null, 0); } }

			if (unwrapped.Length < CryptoHelper.HmacKeyLength)
			{
				CryptographicOperations.ZeroMemory(unwrapped);
				await Log.Warning(LogPrefix, $"Current signing key too short for LoginServer {loginServerId}.");
				return (null, 0);
			}

			return (unwrapped, result.Data.ID);
		}

		/// <summary>
		/// Checks whether the token hash has been revoked in the database.
		/// Fails closed: returns <c>true</c> (revoked) if the service is unavailable or the DB query fails.
		/// </summary>
		private async Task<bool> CheckTokenRevocationCoreAsync(string tokenHash)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var svc))
				return true; // Treat service-unavailable as revoked (fail-closed).

			var result = await svc.FetchByHashAsync(tokenHash);
			if (!result.IsSuccess) return true; // DB error → fail-closed.
			return result.Data.Revoked;
		}

		/// <summary>
		/// Mints a fresh AES-GCM-encrypted auth token for <paramref name="conn"/> using
		/// the existing session encryption channel, persists its hash, and pushes it to
		/// the client via <see cref="RenewTokenResponseBroadcast"/>. Called once
		/// immediately after a successful <see cref="TokenAuthBroadcast"/> (reconnect-only
		/// refresh). Failures are logged and swallowed — the client retains its current
		/// token in that case.
		/// </summary>
		/// <param name="conn">The newly authenticated connection.</param>
		/// <param name="accountName">Account name extracted from the verified token.</param>
		/// <param name="accessLevel">Access level extracted from the verified token.</param>
		/// <param name="loginServerId">Originating LoginServer ID (used to look up the HMAC signing key).</param>
		private async Task IssueRenewalTokenCoreAsync(NetworkConnection conn, string accountName, AccessLevel accessLevel, long loginServerId)
		{
			if (conn == null || !conn.IsActive)
				return;

			if (Server.AccountManager is not IAccountManager<NetworkConnection> am)
				return;

			if (!am.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData) || encryptionData == null)
				return;

			// Renewal is a best-effort path but a transient DB blip here forces the
			// client back through full SRP at the LoginServer, which compounds load
			// during exactly the conditions that caused the blip. Make one short
			// retry with linear backoff before giving up.
			var currentSigningKey = await FetchCurrentSigningKeyCoreAsync(loginServerId);
			if (currentSigningKey.Key == null)
			{
				await Task.Delay(150);
				if (!conn.IsActive) return;
				currentSigningKey = await FetchCurrentSigningKeyCoreAsync(loginServerId);
			}
			byte[] signingKey = currentSigningKey.Key;
			if (signingKey == null)
			{
				await Log.Warning(LogPrefix, $"Renewal token skipped for '{accountName}': signing key unavailable for LoginServer {loginServerId}.");
				return;
			}

			byte[] rawTokenForHashing = null;
			try
			{
				int expirationMinutes = Math.Max(1, (int)renewalTokenExpirationMinutes);

				byte[] encryptedToken = TokenService.GenerateAndEncryptToken(
					encryptionData,
					accountName,
					loginServerId,
					currentSigningKey.KeyId,
					expirationMinutes,
					signingKey,
					accessLevel,
					out rawTokenForHashing);

				if (encryptedToken == null || rawTokenForHashing == null)
				{
					await Log.Warning(LogPrefix, $"Renewal token generation failed for '{accountName}'.");
					return;
				}

				string tokenHash = TokenService.HashToken(rawTokenForHashing);

				if (Server.Database?.ServiceRegistry != null &&
					Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var tokenSvc))
				{
					var r = await tokenSvc.IssueAsync(tokenHash, accountName, loginServerId, DateTime.UtcNow.AddMinutes(expirationMinutes));
					if (!r.IsSuccess)
					{
						await Log.Warning(LogPrefix, $"Renewal IssueAsync DB error for '{accountName}': {r.ErrorCode} - {r.ErrorMessage}");
						return;
					}
				}
				else
				{
					await Log.Warning(LogPrefix, $"Renewal token skipped for '{accountName}': IAuthTokenService unavailable.");
					return;
				}

				// Capture seq from the encrypted-token framing if available, otherwise leave 0
				// (the wire format embeds its own sequence; this field is informational here).
				EnqueueMainThreadAction(() =>
				{
					if (conn.IsActive)
					{
						NetworkManager.ServerManager.Broadcast(conn,
							new RenewTokenResponseBroadcast { Token = encryptedToken, Seq = 0 },
							false, Channel.Reliable);
					}
				});
			}
			finally
			{
				if (rawTokenForHashing != null)
				{
					CryptographicOperations.ZeroMemory(rawTokenForHashing);
				}
				CryptographicOperations.ZeroMemory(signingKey);
			}
		}

		#endregion

		#region Inner Core (bridges FishNet callbacks to TokenAuthenticatorCore)

		/// <summary>
		/// Inner sealed implementation of <see cref="TokenAuthenticatorCore{TConnection}"/> bound to
		/// <see cref="NetworkConnection"/>. All abstract callbacks route to <see cref="outer"/>
		/// (the enclosing <see cref="TokenServerAuthenticator"/>), which provides FishNet broadcasts,
		/// DB access, and event invocation.
		/// </summary>
		private sealed class TokenCore : TokenAuthenticatorCore<NetworkConnection>
		{
			/// <summary>The enclosing <see cref="TokenServerAuthenticator"/> instance that hosts this core.</summary>
			private readonly TokenServerAuthenticator outer;

			/// <summary>
			/// Initializes the core with the enclosing authenticator and the token account manager.
			/// </summary>
			public TokenCore(TokenServerAuthenticator outer, ITokenAccountManager<NetworkConnection> accountManager)
				: base(accountManager) => this.outer = outer;

			// ── BaseAuthenticatorCore abstracts ──────────────────────────────
			/// <inheritdoc/>
			protected override bool IsConnectionAuthenticated(NetworkConnection conn) => conn.IsAuthenticated;
			/// <inheritdoc/>
			protected override string GetConnectionAddress(NetworkConnection conn) => conn.GetAddress();
			/// <inheritdoc/>
			protected override int GetConnectionClientId(NetworkConnection conn) => conn.ClientId;
			/// <inheritdoc/>
			protected override string ResolveRateLimitKey(NetworkConnection conn) => outer.ResolveRateLimitKey(conn);

			/// <inheritdoc/>
			protected override void BroadcastCookieChallenge(NetworkConnection conn, byte[] cookie) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ServerHandshake { Cookie = cookie }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void BroadcastServerHandshake(NetworkConnection conn, byte[] key, ushort version) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ServerHandshake { PublicKey = key, AgreedVersion = version }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void DisconnectConnection(NetworkConnection conn, bool graceful) =>
				conn.Disconnect(graceful);

			// ── TokenAuthenticatorCore abstracts ─────────────────────────────
			/// <inheritdoc/>
			protected override bool IsConnectionActive(NetworkConnection conn) => conn.IsActive;

			/// <inheritdoc/>
			protected override void OnAuthenticationResult(NetworkConnection conn, bool authenticated)
			{
				outer.OnAuthentication(conn, authenticated);
				outer.InvokeClientAuthenticationResult(conn, authenticated);
			}

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void EnqueueMainThread(NetworkConnection conn, Action action) =>
				outer.EnqueueMainThreadAction(action);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username) =>
				outer.TryLoginAsync(defaultResult, username);

			// ── DB callbacks ─────────────────────────────────────────────────
			/// <inheritdoc/>
			protected override Task<byte[]> FetchSigningKeyAsync(long loginServerId, long signingKeyId) =>
				outer.FetchSigningKeyCoreAsync(loginServerId, signingKeyId);

			/// <inheritdoc/>
			protected override Task<bool> CheckTokenRevocationAsync(string tokenHash) =>
				outer.CheckTokenRevocationCoreAsync(tokenHash);

			/// <inheritdoc/>
			protected override Task OnTokenAuthSuccessAsync(NetworkConnection conn, string accountName, AccessLevel accessLevel, long loginServerId) =>
				outer.IssueRenewalTokenCoreAsync(conn, accountName, accessLevel, loginServerId);
		}

		#endregion
	}
}