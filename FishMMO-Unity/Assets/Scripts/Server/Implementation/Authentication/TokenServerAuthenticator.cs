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
		private TokenCore _core;

		/// <inheritdoc/>
		protected override BaseAuthenticatorCore<NetworkConnection> Core => _core;

		#region Lifecycle

		/// <inheritdoc/>
		protected override void InitializeCoreInstance()
		{
			var tam = Server.AccountManager as ITokenAccountManager<NetworkConnection>
				?? throw new InvalidOperationException(
					$"{LogPrefix}: Server.AccountManager must implement ITokenAccountManager<NetworkConnection>. " +
					$"Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			_core = new TokenCore(this, tam);
		}

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<TokenAuthBroadcast>(OnServerTokenAuthBroadcastReceived, false);
		}

		#endregion

		#region UDP Receiver Gate (routes to core)

		/// <summary>Routes an incoming <see cref="TokenAuthBroadcast"/> to the core token authentication channel.</summary>
		internal void OnServerTokenAuthBroadcastReceived(NetworkConnection conn, TokenAuthBroadcast msg, Channel channel)
			=> _core?.OnTokenAuthReceived(conn, msg.Token, msg.Seq);

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

			if (result.Data.HmacKey.Length < CryptoHelper.HmacKeyLength)
			{
				await Log.Warning(LogPrefix, $"Signing key too short for LoginServer {loginServerId}.");
				return null;
			}

			var key = new byte[result.Data.HmacKey.Length];
			Buffer.BlockCopy(result.Data.HmacKey, 0, key, 0, key.Length);
			return key;
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

			if (result.Data.HmacKey.Length < CryptoHelper.HmacKeyLength)
			{
				await Log.Warning(LogPrefix, $"Current signing key too short for LoginServer {loginServerId}.");
				return (null, 0);
			}

			var key = new byte[result.Data.HmacKey.Length];
			Buffer.BlockCopy(result.Data.HmacKey, 0, key, 0, key.Length);
			return (key, result.Data.ID);
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

			var currentSigningKey = await FetchCurrentSigningKeyCoreAsync(loginServerId);
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
		/// <see cref="NetworkConnection"/>. All abstract callbacks route to <see cref="_outer"/>
		/// (the enclosing <see cref="TokenServerAuthenticator"/>), which provides FishNet broadcasts,
		/// DB access, and event invocation.
		/// </summary>
		private sealed class TokenCore : TokenAuthenticatorCore<NetworkConnection>
		{
			/// <summary>The enclosing <see cref="TokenServerAuthenticator"/> instance that hosts this core.</summary>
			private readonly TokenServerAuthenticator _outer;

			/// <summary>
			/// Initializes the core with the enclosing authenticator and the token account manager.
			/// </summary>
			public TokenCore(TokenServerAuthenticator outer, ITokenAccountManager<NetworkConnection> accountManager)
				: base(accountManager) => _outer = outer;

			// ── BaseAuthenticatorCore abstracts ──────────────────────────────
			/// <inheritdoc/>
			protected override bool IsConnectionAuthenticated(NetworkConnection conn) => conn.IsAuthenticated;
			/// <inheritdoc/>
			protected override string GetConnectionAddress(NetworkConnection conn) => conn.GetAddress();
			/// <inheritdoc/>
			protected override int GetConnectionClientId(NetworkConnection conn) => conn.ClientId;
			/// <inheritdoc/>
			protected override string ResolveRateLimitKey(NetworkConnection conn) => _outer.ResolveRateLimitKey(conn);

			/// <inheritdoc/>
			protected override void BroadcastCookieChallenge(NetworkConnection conn, byte[] cookie) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
					new ServerHandshake { Cookie = cookie }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void BroadcastServerHandshake(NetworkConnection conn, byte[] key, ushort version) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
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
				_outer.OnAuthentication(conn, authenticated);
				_outer.InvokeClientAuthenticationResult(conn, authenticated);
			}

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void EnqueueMainThread(NetworkConnection conn, Action action) =>
				_outer.EnqueueMainThreadAction(action);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username) =>
				_outer.TryLoginAsync(defaultResult, username);

			// ── DB callbacks ─────────────────────────────────────────────────
			/// <inheritdoc/>
			protected override Task<byte[]> FetchSigningKeyAsync(long loginServerId, long signingKeyId) =>
				_outer.FetchSigningKeyCoreAsync(loginServerId, signingKeyId);

			/// <inheritdoc/>
			protected override Task<bool> CheckTokenRevocationAsync(string tokenHash) =>
				_outer.CheckTokenRevocationCoreAsync(tokenHash);

			/// <inheritdoc/>
			protected override Task OnTokenAuthSuccessAsync(NetworkConnection conn, string accountName, AccessLevel accessLevel, long loginServerId) =>
				_outer.IssueRenewalTokenCoreAsync(conn, accountName, accessLevel, loginServerId);
		}

		#endregion
	}
}