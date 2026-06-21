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
	/// SRP-6a server authenticator. Delegates all handshake, channel, worker, and protocol logic
	/// to <see cref="SrpAuthenticatorCore{TConnection}"/> in FishMMO-Auth.
	/// This class bridges FishNet broadcast events to the core and provides Unity/DB callbacks.
	/// </summary>
	public class ServerAuthenticator : BaseServerAuthenticator
	{
		/// <summary>Lifetime of issued auth tokens, in minutes. Configurable via the Unity Inspector.</summary>
		[SerializeField] private float tokenExpirationMinutes = 10f;

		/// <summary>The SRP-specific core instance. Null until <see cref="InitializeCoreInstance"/> is called.</summary>
		private ServerAuthenticatorCore _core;

		/// <inheritdoc/>
		protected override BaseAuthenticatorCore<NetworkConnection> Core => _core;

		/// <summary>Backing field for <see cref="LoginServerId"/>.</summary>
		private long _loginServerId;
		/// <summary>LoginServer database ID embedded in issued tokens.</summary>
		public long LoginServerId
		{
			get => _loginServerId;
			set { _loginServerId = value; if (_core != null) _core.LoginServerId = value; }
		}

		/// <summary>Backing field for <see cref="TokenSigningKeyId"/>.</summary>
		private long _tokenSigningKeyId;
		/// <summary>Database ID of the HMAC signing key used for issued tokens.</summary>
		public long TokenSigningKeyId
		{
			get => _tokenSigningKeyId;
			set { _tokenSigningKeyId = value; if (_core != null) _core.TokenSigningKeyId = value; }
		}

		/// <summary>HMAC signing key for token generation. Set by LoginServerSystem after workers start.</summary>
		public byte[] TokenSigningKey
		{
			get => _core?.TokenSigningKey;
			set { if (_core != null) _core.TokenSigningKey = value; }
		}

		/// <summary>AES-256 master key for TOTP secret decryption. Set by LoginServerSystem after workers start.</summary>
		public byte[] TotpMasterKey
		{
			get => _core?.TotpMasterKey;
			set { if (_core != null) _core.TotpMasterKey = value; }
		}

		#region Lifecycle

		/// <inheritdoc/>
		protected override void InitializeCoreInstance()
		{
			var sam = Server.AccountManager as ISrpAccountManager<NetworkConnection>
				?? throw new InvalidOperationException(
					$"{LogPrefix}: Server.AccountManager must implement ISrpAccountManager<NetworkConnection>. " +
					$"Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			_core = new ServerAuthenticatorCore(this, sam);
			_core.LoginServerId = _loginServerId;
			_core.TokenSigningKeyId = _tokenSigningKeyId;
			_core.TokenExpirationMinutes = tokenExpirationMinutes;
		}

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<TwoFactorVerifyBroadcast>(OnServerTwoFactorVerifyBroadcastReceived, false);
			// RevokeTokenBroadcast must be accepted from unauthenticated connections (the
			// client may invoke it after the auth channel has been torn down on logout).
			networkManager.ServerManager.RegisterBroadcast<RevokeTokenBroadcast>(OnServerRevokeTokenBroadcastReceived, false);
		}

		/// <summary>Calls <see cref="SrpAuthenticatorCore{TConnection}.TickRateLimits"/> every frame.</summary>
		protected override void OnUpdate() => _core?.TickRateLimits();

		#endregion

		#region UDP Receiver Gates (routes to core)

		/// <summary>Routes an incoming <see cref="SrpVerifyBroadcast"/> to the core SRP verify channel.</summary>
		internal void OnServerSrpVerifyBroadcastReceived(NetworkConnection conn, SrpVerifyBroadcast msg, Channel channel)
			=> _core?.OnSrpVerifyReceived(conn, msg.S, msg.PublicEphemeral, msg.Seq);

		/// <summary>Routes an incoming <see cref="SrpProofBroadcast"/> to the core SRP proof channel.</summary>
		internal void OnServerSrpProofBroadcastReceived(NetworkConnection conn, SrpProofBroadcast msg, Channel channel)
			=> _core?.OnSrpProofReceived(conn, msg.Proof, msg.Seq);

		/// <summary>Routes an incoming <see cref="TwoFactorVerifyBroadcast"/> to the core TOTP verification handler.</summary>
		internal void OnServerTwoFactorVerifyBroadcastReceived(NetworkConnection conn, TwoFactorVerifyBroadcast msg, Channel channel)
			=> _core?.OnTwoFactorVerifyReceived(conn, msg.Code, msg.Seq);

		/// <summary>
		/// Handles an incoming <see cref="RevokeTokenBroadcast"/> from a client that is explicitly
		/// logging out. Hashes the raw token bytes and asks the DB to mark the row revoked. The
		/// broadcast is best-effort and rate-limit-bounded by FishNet's connection lifecycle;
		/// invalid or unknown tokens are silently ignored to avoid leaking validity oracles.
		/// </summary>
		internal void OnServerRevokeTokenBroadcastReceived(NetworkConnection conn, RevokeTokenBroadcast msg, Channel channel)
		{
			// Validate payload bounds up front on the network thread; defer DB work to a Task.
			if (msg.Token == null || msg.Token.Length == 0 || msg.Token.Length > 4096)
			{
				return;
			}

			// Copy so the deserialized buffer is not mutated by FishNet after we hand off.
			byte[] tokenCopy = new byte[msg.Token.Length];
			Buffer.BlockCopy(msg.Token, 0, tokenCopy, 0, tokenCopy.Length);

			_ = Task.Run(async () =>
			{
				string tokenHash = null;
				try
				{
					tokenHash = TokenService.HashToken(tokenCopy);
				}
				finally
				{
					CryptographicOperations.ZeroMemory(tokenCopy);
				}

				if (string.IsNullOrEmpty(tokenHash))
				{
					return;
				}

				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var svc))
				{
					return;
				}

				try
				{
					var r = await svc.RevokeByHashAsync(tokenHash);
					if (!r.IsSuccess)
					{
						// Treat "not found" as a non-event: an attacker could otherwise probe
						// for valid token hashes by observing log differences.
						await Log.Debug(LogPrefix, $"RevokeByHashAsync returned {r.ErrorCode} for client-initiated revoke.");
					}
				}
				catch (Exception ex)
				{
					await Log.Warning(LogPrefix, $"Exception during client-initiated token revoke: {ex.Message}");
				}
			});
		}

		#endregion

		#region DB Implementations (called by ServerAuthenticatorCore)

		/// <summary>Fetches the SRP account data required for login from the database.</summary>
		private async Task<SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult> FetchAccountForLoginCoreAsync(string identifier, bool isEmail)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return default;

			var result = await svc.FetchForLoginAsync(identifier, isEmail);
			if (!result.IsSuccess)
				return new SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult { IsSuccess = false };

			var d = result.Data;
			return new SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult
			{
				IsSuccess = true,
				// Grace period: unverified accounts can log in until the verification
				// email is actually sent. Once sent, login is blocked until verified.
				IsVerified = d.Verified || d.VerificationEmailSentAt == null,
				Salt = d.Salt,
				Verifier = d.Verifier,
				AccessLevel = (AccessLevel)d.AccessLevel,
				TotpEnabled = d.TotpEnabled,
			};
		}

		/// <summary>Returns <c>true</c> if the account has any online characters in the database.</summary>
		private async Task<bool> CheckIsOnlineCoreAsync(string username)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var svc))
				return false;
			var r = await svc.AnyOnlineAsync(username);
			return r.IsSuccess && r.Data;
		}

		/// <summary>Returns <c>true</c> if the account has a pending kick request in the database.</summary>
		private async Task<bool> CheckHasPendingKickCoreAsync(string username)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var svc))
				return false;
			var r = await svc.HasPendingAsync(username);
			return r.IsSuccess && r.Data;
		}

		/// <summary>Persists a kick request for the account so the game server can disconnect the online session.</summary>
		private async Task PersistKickRequestCoreAsync(string username)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var svc))
				return;
			var r = await svc.PersistAsync(username);
			if (!r.IsSuccess)
				await Log.Warning(LogPrefix, $"PersistAsync kick request DB error for '{username}': {r.ErrorCode} - {r.ErrorMessage}");
		}

		/// <summary>Persists the auth token hash and expiration to the database for later revocation checks.</summary>
		private async Task PersistTokenHashCoreAsync(string username, string tokenHash, int expirationMinutes)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var svc))
				return;
			var r = await svc.IssueAsync(tokenHash, username, _loginServerId, DateTime.UtcNow.AddMinutes(expirationMinutes));
			if (!r.IsSuccess)
				await Log.Warning(LogPrefix, $"IssueAsync token DB error for '{username}': {r.ErrorCode} - {r.ErrorMessage}");
		}

		/// <summary>
		/// Verifies a TOTP code or single-use recovery code for the account.
		/// Handles both 6-digit TOTP and XXXXX-XXXXX hex recovery code formats.
		/// Returns <c>true</c> on success; zeroes the decrypted TOTP secret in all paths.
		/// </summary>
		private async Task<bool> VerifyTotpCodeCoreAsync(string username, string totpCode, byte[] totpMasterKeySnapshot)
		{
			if (totpMasterKeySnapshot == null || totpMasterKeySnapshot.Length != 32 ||
				Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
				return false;

			var accountResult = await accountService.FetchForLoginAsync(username, username.Contains('@'));
			if (!accountResult.IsSuccess || string.IsNullOrEmpty(accountResult.Data.TotpSecret))
				return false;

			// Detect recovery code format (XXXXX-XXXXX = 11-char hex with hyphen at position 5).
			bool isRecoveryCode = false;
			if (totpCode.Length == 11 && totpCode[5] == '-')
			{
				isRecoveryCode = true;
				for (int i = 0; i < 11; i++)
				{
					if (i == 5) continue;
					char c = char.ToUpperInvariant(totpCode[i]);
					if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
					{
						isRecoveryCode = false;
						break;
					}
				}
			}

			if (isRecoveryCode)
			{
				if (!Server.Database.ServiceRegistry.TryGet<ITwoFactorRecoveryCodeService>(out var rcSvc))
					return false;
				var codesResult = await rcSvc.FetchUnusedByAccountAsync(username);
				if (!codesResult.IsSuccess || codesResult.Data == null || codesResult.Data.Count == 0)
					return false;
				string matchedHash = null;
				foreach (var code in codesResult.Data)
				{
					if (CryptoHelper.TwoFactor.VerifyRecoveryCode(username, totpCode, code.CodeHash))
					{
						matchedHash = code.CodeHash;
						break;
					}
				}
				if (matchedHash == null) return false;
				var consumeResult = await rcSvc.ConsumeCodeAsync(username, matchedHash);
				return consumeResult.IsSuccess;
			}
			else
			{
				byte[] plaintextSecret = null;
				try
				{
					plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(totpMasterKeySnapshot, username, accountResult.Data.TotpSecret);
					var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(plaintextSecret, totpCode, accountResult.Data.LastTotpWindow);
					if (!valid) return false;
					var persistResult = accountResult.Data.TotpVerifiedAt == null
						? await accountService.PersistTotpVerifiedAtAsync(username, windowUsed)
						: await accountService.PersistLastTotpWindowAsync(username, windowUsed);
					return persistResult.IsSuccess;
				}
				finally
				{
					if (plaintextSecret != null) CryptographicOperations.ZeroMemory(plaintextSecret);
				}
			}
		}

		#endregion

		#region Inner Core (bridges FishNet callbacks to SrpAuthenticatorCore)

		/// <summary>
		/// Inner sealed implementation of <see cref="SrpAuthenticatorCore{TConnection}"/> bound to
		/// <see cref="NetworkConnection"/>. All abstract callbacks route to <see cref="_outer"/>
		/// (the enclosing <see cref="ServerAuthenticator"/>), which provides FishNet broadcasts,
		/// DB access, and event invocation.
		/// </summary>
		private sealed class ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>
		{
			/// <summary>The enclosing <see cref="ServerAuthenticator"/> instance that hosts this core.</summary>
			private readonly ServerAuthenticator _outer;

			/// <summary>
			/// Initializes the core with the enclosing authenticator and the SRP account manager.
			/// </summary>
			public ServerAuthenticatorCore(ServerAuthenticator outer, ISrpAccountManager<NetworkConnection> accountManager)
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

			// ── SrpAuthenticatorCore abstracts ───────────────────────────────
			/// <inheritdoc/>
			protected override void OnAuthenticationResult(NetworkConnection conn, bool authenticated)
			{
				_outer.OnAuthentication(conn, authenticated);
				_outer.InvokeClientAuthenticationResult(conn, authenticated);
			}

			/// <inheritdoc/>
			protected override bool IsConnectionActive(NetworkConnection conn) => conn.IsActive;

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void BroadcastSrpVerifyResponse(NetworkConnection conn, byte[] encSalt, byte[] encServerEphemeral) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
					new SrpVerifyBroadcast { S = encSalt, PublicEphemeral = encServerEphemeral }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void BroadcastSrpSuccess(NetworkConnection conn, byte[] encServerProof, ClientAuthenticationResult result, byte[] encToken) =>
				_outer.NetworkManager.ServerManager.Broadcast(conn,
					new SrpSuccessBroadcast { Proof = encServerProof, Result = result, Token = encToken }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void EnqueueMainThread(NetworkConnection conn, Action action) =>
				_outer.EnqueueMainThreadAction(action);

			/// <inheritdoc/>
			protected override bool IsAllowedUsername(string username) =>
				Authentication.IsAllowedUsername(username);

			/// <inheritdoc/>
			protected override bool IsAllowedEmailUsername(string username) =>
				Authentication.IsAllowedEmailUsername(username);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username) =>
				_outer.TryLoginAsync(defaultResult, username);

			// ── DB callbacks ─────────────────────────────────────────────────
			/// <inheritdoc/>
			protected override Task<SrpAccountLookupResult> FetchAccountForLoginAsync(string identifier, bool isEmail) =>
				_outer.FetchAccountForLoginCoreAsync(identifier, isEmail);

			/// <inheritdoc/>
			protected override Task<bool> CheckIsOnlineAsync(string username) =>
				_outer.CheckIsOnlineCoreAsync(username);

			/// <inheritdoc/>
			protected override Task<bool> CheckHasPendingKickAsync(string username) =>
				_outer.CheckHasPendingKickCoreAsync(username);

			/// <inheritdoc/>
			protected override Task PersistKickRequestAsync(string username) =>
				_outer.PersistKickRequestCoreAsync(username);

			/// <inheritdoc/>
			protected override Task PersistTokenHashAsync(string username, string tokenHash, int expirationMinutes) =>
				_outer.PersistTokenHashCoreAsync(username, tokenHash, expirationMinutes);

			/// <inheritdoc/>
			protected override Task<bool> VerifyTotpCodeAsync(string username, string totpCode, byte[] totpMasterKey) =>
				_outer.VerifyTotpCodeCoreAsync(username, totpCode, totpMasterKey);

			/// <inheritdoc/>
			protected override async Task<bool> TryResendVerificationEmailIfExpiredAsync(string username, DateTime? verifyCodeExpiresUtc)
			{
				if (verifyCodeExpiresUtc == null || verifyCodeExpiresUtc.Value > DateTime.UtcNow)
					return false;

				int newCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
				DateTime newExpires = DateTime.UtcNow.AddHours(24);

				if (_outer.Server?.Database?.ServiceRegistry == null) return false;
				if (!_outer.Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService)) return false;

				var persistResult = await accountService.PersistVerifyCodeAsync(username, newCode, newExpires);
				if (!persistResult.IsSuccess) return false;

				if (_outer.Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueueService))
				{
					var dupCheck = await emailQueueService.HasPendingForUserAsync(username);
					if (!dupCheck.IsSuccess || !dupCheck.Data)
					{
						var accountResult = await accountService.FetchForLoginAsync(username, false);
						string recipientEmail = accountResult.IsSuccess ? (accountResult.Data.Email ?? username) : username;
						string subject = "FishMMO - Verify Your Account";
						string body = _outer.BuildLoginVerificationEmailBody(username, newCode);
						await emailQueueService.EnqueueAsync(recipientEmail, username, subject, body);
					}
				}

				await _outer.PersistVerificationEmailSentCoreAsync(username);
				return true;
			}
		}

		#endregion

		/// <summary>
		/// Builds the HTML body for a login-triggered verification email resend.
		/// </summary>

		/// <summary>
		/// Sets VerificationEmailSentAt after resending a verification email during login.
		/// </summary>
		private async Task PersistVerificationEmailSentCoreAsync(string username)
		{
			if (Server?.Database?.ServiceRegistry != null &&
				Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				var r = await accountService.PersistVerificationEmailSentAsync(username);
				if (!r.IsSuccess)
					await Log.Warning(LogPrefix, $"PersistVerificationEmailSentAsync DB error for '{username}': {r.ErrorCode} - {r.ErrorMessage}");
			}
		}
		private string BuildLoginVerificationEmailBody(string username, int verifyCode)
		{
			return $@"<html><body style='font-family: Arial, sans-serif; color: #333;'>
				<h2>FishMMO — Verification Code</h2>
				<p>You recently attempted to log in to your FishMMO account <b>{username}</b>.</p>
				<p>Your verification code is:</p>
				<h1 style='font-size: 32px; letter-spacing: 4px; color: #2563eb;'>{verifyCode:D6}</h1>
				<p>This code is valid for 24 hours. If you did not request this, you can safely ignore this email.</p>
				<hr/>
				<p style='font-size: 12px; color: #999;'>— The FishMMO Team</p>
			</body></html>";
		}

	}
}