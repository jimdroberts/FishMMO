// Nullable annotations without null-state analysis. The authentication path annotates the
// references that are legitimately absent — an unestablished session, a challenge that never
// arrived — and Unity compiles this assembly with the nullable context off, so every one of those
// annotations raised CS8632. `annotations` alone enables them without switching on flow analysis,
// which would bury real warnings under hundreds more for code never written against it.
#nullable enable annotations

using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using AccountVerificationChannels = FishMMO.Database.Data.Enums.AccountVerificationChannels;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using System;
using System.Collections.Generic;
using System.Net;
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
		private ServerAuthenticatorCore core;

		/// <inheritdoc/>
		protected override BaseAuthenticatorCore<NetworkConnection> Core => core;

		/// <summary>Backing field for <see cref="LoginServerId"/>.</summary>
		private long loginServerId;
		/// <summary>LoginServer database ID embedded in issued tokens.</summary>
		public long LoginServerId
		{
			get => loginServerId;
			set { loginServerId = value; if (core != null) core.LoginServerId = value; }
		}

		/// <summary>Backing field for <see cref="TokenSigningKeyId"/>.</summary>
		private long tokenSigningKeyId;
		/// <summary>Database ID of the HMAC signing key used for issued tokens.</summary>
		public long TokenSigningKeyId
		{
			get => tokenSigningKeyId;
			set { tokenSigningKeyId = value; if (core != null) core.TokenSigningKeyId = value; }
		}

		/// <summary>Backing field for <see cref="TokenSigningKey"/>. Used when <see cref="core"/> is not yet created.</summary>
		private byte[] tokenSigningKeyBacking;
		/// <summary>Backing field for <see cref="TotpMasterKey"/>. Used when <see cref="core"/> is not yet created.</summary>
		private byte[] totpMasterKeyBacking;

		/// <summary>HMAC signing key for token generation. Set by LoginServerSystem after workers start.</summary>
		public byte[] TokenSigningKey
		{
			get => core?.TokenSigningKey ?? tokenSigningKeyBacking;
			set
			{
				if (core != null)
				{
					if (value != null)
					{
						byte[] copy = new byte[value.Length];
						Buffer.BlockCopy(value, 0, copy, 0, value.Length);
						core.TokenSigningKey = copy;
					}
					else
					{
						core.TokenSigningKey = null;
					}
				}
				else
				{
					if (tokenSigningKeyBacking != null)
					{
						CryptographicOperationsCompat.ZeroMemory(tokenSigningKeyBacking);
					}

					if (value != null)
					{
						byte[] copy = new byte[value.Length];
						Buffer.BlockCopy(value, 0, copy, 0, value.Length);
						tokenSigningKeyBacking = copy;
					}
					else
					{
						tokenSigningKeyBacking = null;
					}
				}
			}
		}

		/// <summary>AES-256 master key for TOTP secret decryption. Set by LoginServerSystem after workers start.</summary>
		public byte[] TotpMasterKey
		{
			get => core?.TotpMasterKey ?? totpMasterKeyBacking;
			set
			{
				if (core != null)
				{
					if (value != null)
					{
						byte[] copy = new byte[value.Length];
						Buffer.BlockCopy(value, 0, copy, 0, value.Length);
						core.TotpMasterKey = copy;
					}
					else
					{
						core.TotpMasterKey = null;
					}
				}
				else
				{
					if (totpMasterKeyBacking != null)
					{
						CryptographicOperationsCompat.ZeroMemory(totpMasterKeyBacking);
					}

					if (value != null)
					{
						byte[] copy = new byte[value.Length];
						Buffer.BlockCopy(value, 0, copy, 0, value.Length);
						totpMasterKeyBacking = copy;
					}
					else
					{
						totpMasterKeyBacking = null;
					}
				}
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Consults the login queue. A queued client holds an open, unauthenticated
		/// connection for the whole wait — which the handshake-timeout sweep would
		/// otherwise treat as a client that never handshook and disconnect.
		/// </remarks>
		protected override bool IsConnectionAwaitingQueueAdmission(NetworkConnection conn)
		{
			return Server?.BehaviourRegistry != null &&
				Server.BehaviourRegistry.TryGet<LoginServer.LoginQueueSystem>(out var queueSystem) &&
				queueSystem.IsAwaitingAdmission(conn);
		}

		#region Lifecycle

		/// <inheritdoc/>
		protected override void InitializeCoreInstance()
		{
			var sam = Server.AccountManager as ISrpAccountManager<NetworkConnection>
				?? throw new InvalidOperationException(
					$"{LogPrefix}: Server.AccountManager must implement ISrpAccountManager<NetworkConnection>. " +
					$"Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			core = new ServerAuthenticatorCore(this, sam);
			core.LoginServerId = loginServerId;
			core.TokenSigningKeyId = tokenSigningKeyId;
			core.TokenExpirationMinutes = tokenExpirationMinutes;

			// Read SRP worker/channel configuration from the server .cfg file.
			// Defaults match the previous hardcoded constants; operators can tune
			// these per deployment for login-storm resilience.
			if (Server?.Configuration != null)
			{
				core.VerifyWorkerCount = Server.Configuration.GetInt("AuthSrpVerifyWorkerCount", 2);
				core.ProofWorkerCount = Server.Configuration.GetInt("AuthSrpProofWorkerCount", 2);
				core.VerifyChannelCapacity = Server.Configuration.GetInt("AuthSrpVerifyChannelCapacity", 500);
				core.ProofChannelCapacity = Server.Configuration.GetInt("AuthSrpProofChannelCapacity", 500);
				core.MaxConcurrentTotpVerifications = Server.Configuration.GetInt("AuthMaxConcurrentTotpVerifications", 4);
			}

			// Apply cached key material that was set before the core was created,
			// then release the cache references so callers observe the core values.
			if (tokenSigningKeyBacking != null)
			{
				byte[] copy = new byte[tokenSigningKeyBacking.Length];
				Buffer.BlockCopy(tokenSigningKeyBacking, 0, copy, 0, copy.Length);
				core.TokenSigningKey = copy;
				CryptographicOperationsCompat.ZeroMemory(tokenSigningKeyBacking);
				tokenSigningKeyBacking = null;
			}
			if (totpMasterKeyBacking != null)
			{
				byte[] copy = new byte[totpMasterKeyBacking.Length];
				Buffer.BlockCopy(totpMasterKeyBacking, 0, copy, 0, copy.Length);
				core.TotpMasterKey = copy;
				CryptographicOperationsCompat.ZeroMemory(totpMasterKeyBacking);
				totpMasterKeyBacking = null;
			}
		}

		/// <inheritdoc/>
		public override IAccountManager<NetworkConnection> CreateAccountManager() =>
			new SrpAccountManager();

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<SrpVerifyRequestBroadcast>(OnServerSrpVerifyBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<SrpProofBroadcast>(OnServerSrpProofBroadcastReceived, false);
			networkManager.ServerManager.RegisterBroadcast<TwoFactorVerifyBroadcast>(OnServerTwoFactorVerifyBroadcastReceived, false);
			// RevokeTokenBroadcast is registered by BaseServerAuthenticator, so that World and
			// Scene servers honour a logout revocation as well as the LoginServer.
		}

		/// <summary>Calls <see cref="SrpAuthenticatorCore{TConnection}.TickRateLimits"/> every frame.</summary>
		protected override void OnUpdate()
		{
			core?.TickRateLimits();
		}

		#endregion

		#region UDP Receiver Gates (routes to core)

		/// <summary>Routes an incoming <see cref="SrpVerifyRequestBroadcast"/> to the core SRP verify channel.</summary>
		internal void OnServerSrpVerifyBroadcastReceived(NetworkConnection conn, SrpVerifyRequestBroadcast msg, Channel channel)
		{
			// Validate wire-format bounds before any allocation or crypto work.
			// Reject oversized payloads on the network thread.
			if (msg.Username == null || msg.Username.Length > AuthSizeLimits.MaxSrpUsernameSize ||
				msg.PublicEphemeral == null || msg.PublicEphemeral.Length > AuthSizeLimits.MaxSrpEphemeralSize)
			{
				conn.Disconnect(true);
				return;
			}
			core?.OnSrpVerifyReceived(conn, msg.Username, msg.PublicEphemeral, msg.Seq);
		}

		/// <summary>Routes an incoming <see cref="SrpProofBroadcast"/> to the core SRP proof channel.</summary>
		internal void OnServerSrpProofBroadcastReceived(NetworkConnection conn, SrpProofBroadcast msg, Channel channel)
		{
			// Validate wire-format bounds before any allocation or crypto work.
			if (msg.Proof == null || msg.Proof.Length > AuthSizeLimits.MaxSrpProofSize)
			{
				conn.Disconnect(true);
				return;
			}
			core?.OnSrpProofReceived(conn, msg.Proof, msg.Seq);
		}

		/// <summary>Routes an incoming <see cref="TwoFactorVerifyBroadcast"/> to the core TOTP verification handler.</summary>
		internal void OnServerTwoFactorVerifyBroadcastReceived(NetworkConnection conn, TwoFactorVerifyBroadcast msg, Channel channel)
		{
			// Validate wire-format bounds before any allocation or crypto work.
			if (msg.Code == null || msg.Code.Length > AuthSizeLimits.MaxTotpCodeSize)
			{
				conn.Disconnect(true);
				return;
			}
			core?.OnTwoFactorVerifyReceived(conn, msg.Code, msg.Seq);
		}

		#endregion

		#region DB Implementations (called by ServerAuthenticatorCore)

		/// <summary>Fetches the SRP account data required for login from the database.</summary>
		private async Task<SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult> FetchAccountForLoginCoreAsync(string identifier, bool isEmail)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
			{
				// No account service is this server failing, not the account missing.
				return new SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult { IsSuccess = false, IsServerError = true };
			}

			var result = await svc.FetchForLoginAsync(identifier, isEmail);
			if (!result.IsSuccess)
			{
				/* NotFound, Forbidden (banned) and ValidationError are the answers the core must not
				 * tell apart, and it does not: all three take the fake-verifier path. Anything else is
				 * the database failing to answer, and is flagged as a server error so the core answers
				 * ServerBusy instead of treating a correct password as a wrong one and counting it
				 * (issue #267). That answer depends on no account, so it leaks nothing. */
				bool serverError = result.ErrorCode != DatabaseErrorCodes.NotFound &&
					result.ErrorCode != DatabaseErrorCodes.Forbidden &&
					result.ErrorCode != DatabaseErrorCodes.ValidationError;
				if (serverError)
				{
					await Log.Warning(LogPrefix, $"FetchForLoginAsync DB error: [{result.ErrorCode}] {result.ErrorMessage}. The sign-in is answered ServerBusy.");
				}
				return new SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult { IsSuccess = false, IsServerError = serverError };
			}

			var d = result.Data;

			/* Which codes this account is asked for, under THIS server's switches: AccountVerificationRules,
			 * the one rule the Control Panel's sign-in applies too. Any one code verifies the account, and
			 * there is no grace period: an unverified account is asked for a code from the moment it exists,
			 * whether or not a message has been delivered yet. An account no enabled channel can reach, and
			 * every account on a server that verifies nothing, is not asked. AutoVerifyAccounts (development
			 * builds only) bypasses the gate outright so accounts created before the flag was set are not
			 * locked out of a local server. */
			IServerConfiguration configuration = Server?.Configuration;
			AccountVerificationChannels outstanding = AccountVerificationRules.Outstanding(d, AccountVerificationPolicy.EnabledChannels(configuration));

			return new SrpAuthenticatorCore<NetworkConnection>.SrpAccountLookupResult
			{
				IsSuccess = true,
				IsVerified = d.Verified ||
					AccountVerificationPolicy.IsAutoVerifyEnabled(configuration) ||
					outstanding == AccountVerificationChannels.None,
				AccountName = d.Name,
				EmailVerificationPending = (outstanding & AccountVerificationChannels.Email) != 0,
				PhoneVerificationPending = (outstanding & AccountVerificationChannels.Sms) != 0,
				DiscordVerificationCodeOwed = (outstanding & AccountVerificationChannels.Discord) != 0 && !d.DiscordVerifyCodeIssued,
				Salt = d.Salt,
				Verifier = d.Verifier,
				AccessLevel = (AccessLevel)d.AccessLevel,
				TotpEnabled = d.TotpEnabled,
				// Required by the core's expired-verify-code resend. Leaving this unset left it
				// null for every account, which silently disabled the resend entirely.
				VerifyCodeExpiresUtc = d.VerifyCodeExpiresUtc,
				// Required by the core's expired-or-missing SMS code resend, for the same reason.
				PhoneVerifyCodeExpiresUtc = d.PhoneVerifyCodeExpiresUtc,
			};
		}

		/// <summary>
		/// Returns <c>true</c> if the account has any online characters in the database.
		/// Fails closed: a database error returns <c>true</c> (treat as online), so an outage
		/// cannot be used to slip a second session past the duplicate-login gate.
		/// </summary>
		private async Task<bool> CheckIsOnlineCoreAsync(string username)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"ICharacterService unavailable; the duplicate-session gate cannot be evaluated for '{username}'.");
				return false;
			}
			var r = await svc.AnyOnlineAsync(username);
			if (!r.IsSuccess)
			{
				await Log.Error(LogPrefix, $"AnyOnlineAsync DB error for '{username}': [{r.ErrorCode}] {r.ErrorMessage}. Treating the account as online (fail-closed).");
				return true;
			}
			return r.Data;
		}

		/// <summary>
		/// Returns <c>true</c> if the account has a pending kick request in the database.
		/// Fails closed: a database error returns <c>true</c> (treat as kicked), so an outage
		/// cannot be used to log straight back in past a pending kick.
		/// </summary>
		private async Task<bool> CheckHasPendingKickCoreAsync(string username)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IKickRequestService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"IKickRequestService unavailable; the pending-kick gate cannot be evaluated for '{username}'.");
				return false;
			}
			var r = await svc.HasPendingAsync(username);
			if (!r.IsSuccess)
			{
				await Log.Error(LogPrefix, $"HasPendingAsync DB error for '{username}': [{r.ErrorCode}] {r.ErrorMessage}. Treating the account as kicked (fail-closed).");
				return true;
			}
			return r.Data;
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
		/// <returns>
		/// True when the hash was recorded. False makes the core refuse the sign-in with ServerBusy:
		/// world and scene servers treat a hash they cannot find as revoked (TokenServerAuthenticator's
		/// revocation check fails closed), so a token without its row would be turned away there with
		/// TokenRevoked after the player had been told the login succeeded (issue #267).
		/// </returns>
		private async Task<bool> PersistTokenHashCoreAsync(string username, string tokenHash, int expirationMinutes)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var svc))
			{
				await Log.Error(LogPrefix, $"No auth token service to record the token for '{username}'; the sign-in is refused.");
				return false;
			}
			var r = await svc.IssueAsync(tokenHash, username, loginServerId, DateTime.UtcNow.AddMinutes(expirationMinutes));
			if (!r.IsSuccess)
			{
				await Log.Error(LogPrefix, $"IssueAsync token DB error for '{username}': [{r.ErrorCode}] {r.ErrorMessage}. The sign-in is refused with ServerBusy.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Verifies a TOTP code or single-use recovery code for the account.
		/// Handles both 6-digit TOTP and XXXX-XXXX-XXXX-XXXX hex recovery code formats.
		/// Zeroes the decrypted TOTP secret in all paths.
		/// </summary>
		/// <returns>
		/// Valid or Invalid for a code that was checked; ServerError when it could not be checked.
		/// The core counts only Invalid as a failed attempt. Every database step used to answer a
		/// plain false when it failed, so a correct code refused for a timeout was reported as
		/// TwoFactorInvalid and counted toward the two-factor lockout (issue #267).
		/// </returns>
		private async Task<SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome> VerifyTotpCodeCoreAsync(string username, string totpCode, byte[] totpMasterKeySnapshot)
		{
			if (totpMasterKeySnapshot == null || totpMasterKeySnapshot.Length != 32 ||
				Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService))
			{
				// A server without its key or its database cannot check anything; the player's code is not at fault.
				return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
			}

			var accountResult = await accountService.FetchForLoginAsync(username, username.Contains('@'));
			if (!accountResult.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"Two-factor account fetch failed for '{username}': [{accountResult.ErrorCode}] {accountResult.ErrorMessage}. Answered ServerBusy.");
				return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
			}
			if (string.IsNullOrEmpty(accountResult.Data.TotpSecret))
				return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;

			/* Shape test lives next to the generator now. This used to be a local copy looking
			 * for "eleven characters with a hyphen at index five", which GenerateRecoveryCodes
			 * has never produced — it emits XXXX-XXXX-XXXX-XXXX. Nothing matched, so every
			 * recovery code fell through to the TOTP verifier and was rejected. */
			bool isRecoveryCode = CryptoHelper.TwoFactor.LooksLikeRecoveryCode(totpCode);

			if (isRecoveryCode)
			{
				if (!Server.Database.ServiceRegistry.TryGet<ITwoFactorRecoveryCodeService>(out var rcSvc))
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
				var codesResult = await rcSvc.FetchUnusedByAccountAsync(username);
				if (!codesResult.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"Recovery code fetch failed for '{username}': [{codesResult.ErrorCode}] {codesResult.ErrorMessage}. Answered ServerBusy.");
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
				}
				if (codesResult.Data == null || codesResult.Data.Count == 0)
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;
				string matchedHash = null;
				foreach (var code in codesResult.Data)
				{
					if (CryptoHelper.TwoFactor.VerifyRecoveryCode(username, totpCode, code.CodeHash))
					{
						matchedHash = code.CodeHash;
						break;
					}
				}
				if (matchedHash == null) return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;
				var consumeResult = await rcSvc.ConsumeCodeAsync(username, matchedHash);
				if (consumeResult.IsSuccess)
				{
					await CancelPendingTwoFactorResetAsync(accountResult.Data.Name, accountResult.Data.Email);
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Valid;
				}
				if (IsWrongTwoFactorCodeAnswer(consumeResult.ErrorCode))
				{
					// Already consumed: the one-use guard doing its job.
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;
				}
				await Log.Warning(LogPrefix, $"Recovery code consume failed for '{username}': [{consumeResult.ErrorCode}] {consumeResult.ErrorMessage}. Answered ServerBusy.");
				return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
			}
			else
			{
				byte[] plaintextSecret = null;
				try
				{
					plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(totpMasterKeySnapshot, username, accountResult.Data.TotpSecret);
					var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(plaintextSecret, totpCode, accountResult.Data.LastTotpWindow);
					if (!valid) return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;
					var persistResult = accountResult.Data.TotpVerifiedAt == null
						? await accountService.PersistTotpVerifiedAtAsync(username, windowUsed)
						: await accountService.PersistLastTotpWindowAsync(username, windowUsed);
					if (persistResult.IsSuccess)
					{
						await CancelPendingTwoFactorResetAsync(accountResult.Data.Name, accountResult.Data.Email);
						return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Valid;
					}
					if (IsWrongTwoFactorCodeAnswer(persistResult.ErrorCode))
					{
						// The window was already used: the replay guard doing its job.
						return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.Invalid;
					}
					await Log.Warning(LogPrefix, $"TOTP window persist failed for '{username}': [{persistResult.ErrorCode}] {persistResult.ErrorMessage}. Answered ServerBusy.");
					return SrpAuthenticatorCore<NetworkConnection>.TwoFactorVerifyOutcome.ServerError;
				}
				finally
				{
					if (plaintextSecret != null) CryptographicOperationsCompat.ZeroMemory(plaintextSecret);
				}
			}
		}

		/// <summary>
		/// Whether a failed two-factor write is the database saying the code does not count: a
		/// recovery code already consumed (NotFound), or a TOTP window already used or an
		/// enrolment already confirmed (ValidationError). Those are the replay and one-use guards
		/// doing their job; anything else is a fault.
		/// </summary>
		private static bool IsWrongTwoFactorCodeAnswer(string errorCode) =>
			errorCode == DatabaseErrorCodes.NotFound || errorCode == DatabaseErrorCodes.ValidationError;

		/// <summary>
		/// Cancels the account's pending self-service two-factor reset, if any, and tells the owner.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A reset exists for someone who has lost their authenticator AND their recovery codes, and it
		/// waits before taking effect precisely so that the real owner — who still has a factor — can
		/// stop one they did not ask for. Passing two-factor here proves exactly that, so the reset is
		/// cancelled. Nothing in the game completes a reset; that is the Control Panel's flow.
		/// </para>
		/// <para>
		/// Best effort and never fatal to the sign-in: the player has just proven both factors, and a
		/// queue that is briefly unavailable must not turn that into a refusal. A notice is queued only
		/// when a request was actually cancelled.
		/// </para>
		/// </remarks>
		private async Task CancelPendingTwoFactorResetAsync(string accountName, string email)
		{
			try
			{
				if (string.IsNullOrEmpty(accountName) ||
					Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ITwoFactorResetRequestService>(out var resetService))
				{
					return;
				}

				var cancelResult = await resetService.CancelAsync(accountName, "owner", null);
				if (!cancelResult.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"Could not cancel a pending two-factor reset for '{accountName}': [{cancelResult.ErrorCode}] {cancelResult.ErrorMessage}");
					return;
				}
				if (!cancelResult.Data)
				{
					return;
				}

				await Log.Info(LogPrefix, $"Cancelled a pending two-factor reset for '{accountName}': the owner passed two-factor in game.");

				if (string.IsNullOrWhiteSpace(email) ||
					!Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueue))
				{
					await Log.Warning(LogPrefix, $"No email address or email queue; the reset-cancelled notice for '{accountName}' was not sent.");
					return;
				}

				var enqueueResult = await emailQueue.EnqueueAsync(
					email,
					accountName,
					"FishMMO - Two-factor reset cancelled",
					BuildTwoFactorResetCancelledEmailBody(accountName),
					EmailKind.SecurityNotice);
				if (!enqueueResult.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"Failed to queue the reset-cancelled notice for '{accountName}': [{enqueueResult.ErrorCode}] {enqueueResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Cancelling a pending two-factor reset failed: {ex.Message}");
			}
		}

		/// <summary>Builds the HTML body telling the owner their pending two-factor reset was cancelled.</summary>
		private static string BuildTwoFactorResetCancelledEmailBody(string accountName)
		{
			return $@"<html><body style='font-family: Arial, sans-serif; color: #333;'>
				<h2>FishMMO — Two-factor reset cancelled</h2>
				<p>A request to reset two-factor authentication on your account <b>{System.Net.WebUtility.HtmlEncode(accountName)}</b> was waiting to take effect.</p>
				<p>It has been cancelled, because your account just signed in to the game with its authenticator or a recovery code.</p>
				<p>If you asked for that reset yourself and still need it, you can request it again from the Control Panel. If you did not, someone may know your password: change it now.</p>
				<hr/>
				<p style='font-size: 12px; color: #999;'>— The FishMMO Team</p>
			</body></html>";
		}

		/// <summary>Reads whether password sign-in is locked in the database. Fails open (logged); see the core's hook wrappers.</summary>
		private async Task<bool> IsLoginLockedCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return false;
			var r = await svc.FetchAuthLockoutAsync(accountName);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"FetchAuthLockoutAsync failed: [{r.ErrorCode}] {r.ErrorMessage}. The database lockout is not applied to this attempt.");
				return false;
			}
			return r.Data.IsLocked(AuthFailureKind.Password, DateTime.UtcNow);
		}

		/// <summary>Counts one failed SRP proof against the shared lockout.</summary>
		private async Task RecordLoginFailureCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return;
			AuthLockoutSettings settings = LoginSecurityPolicy.GetPasswordLockout(Server.Configuration);
			var r = await svc.RecordAuthFailureAsync(accountName, AuthFailureKind.Password, settings.Threshold, settings.Window, settings.Lockout);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"RecordAuthFailureAsync (password) failed: [{r.ErrorCode}] {r.ErrorMessage}");
			}
			else if (r.Data.HasValue)
			{
				await Log.Warning(LogPrefix, $"Password sign-in for '{accountName}' is locked until {r.Data.Value:u} after repeated failures.");
			}
		}

		/// <summary>Clears the shared password failure count after a correct proof.</summary>
		private async Task ClearLoginFailuresCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return;
			var r = await svc.ClearAuthFailuresAsync(accountName, AuthFailureKind.Password);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"ClearAuthFailuresAsync (password) failed: [{r.ErrorCode}] {r.ErrorMessage}");
			}
		}

		/// <summary>Until when the two-factor step is locked, or null.</summary>
		private async Task<DateTime?> GetTwoFactorLockedUntilCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return null;
			var r = await svc.FetchAuthLockoutAsync(accountName);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"FetchAuthLockoutAsync failed: [{r.ErrorCode}] {r.ErrorMessage}. The two-factor lockout is not applied to this attempt.");
				return null;
			}
			return r.Data.IsLocked(AuthFailureKind.TwoFactor, DateTime.UtcNow) ? r.Data.TwoFactorLockedUntilUtc : null;
		}

		/// <summary>Counts one wrong second-factor code; returns the lock instant when this trips it.</summary>
		private async Task<DateTime?> RecordTwoFactorFailureCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return null;
			AuthLockoutSettings settings = LoginSecurityPolicy.GetTwoFactorLockout(Server.Configuration);
			var r = await svc.RecordAuthFailureAsync(accountName, AuthFailureKind.TwoFactor, settings.Threshold, settings.Window, settings.Lockout);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"RecordAuthFailureAsync (two-factor) failed: [{r.ErrorCode}] {r.ErrorMessage}");
				return null;
			}
			if (r.Data.HasValue)
			{
				await Log.Warning(LogPrefix, $"Two-factor sign-in for '{accountName}' is locked until {r.Data.Value:u} after repeated wrong codes.");
			}
			return r.Data;
		}

		/// <summary>Clears the shared two-factor failure count after a correct code.</summary>
		private async Task ClearTwoFactorFailuresCoreAsync(string accountName)
		{
			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAccountService>(out var svc))
				return;
			var r = await svc.ClearAuthFailuresAsync(accountName, AuthFailureKind.TwoFactor);
			if (!r.IsSuccess)
			{
				await Log.Warning(LogPrefix, $"ClearAuthFailuresAsync (two-factor) failed: [{r.ErrorCode}] {r.ErrorMessage}");
			}
		}

		/// <summary>
		/// The closed-test gate, run by the core only after a correct SRP proof. Staff always pass;
		/// everyone else needs an un-revoked code of an active program. A gate that cannot be evaluated
		/// refuses with ServerBusy rather than admitting.
		/// </summary>
		private async Task<ClientAuthenticationResult?> CheckSignInAccessCoreAsync(string accountName, AccessLevel accessLevel)
		{
			IServerConfiguration configuration = Server?.Configuration;
			if (!LoginSecurityPolicy.IsBetaModeEnabled(configuration) || accessLevel >= AccessLevel.GameMaster)
			{
				return null;
			}

			IReadOnlyList<string> programs = LoginSecurityPolicy.GetBetaPrograms(configuration, out int rejected);
			if (rejected > 0 || programs.Count == 0)
			{
				await Log.Error(LogPrefix, $"BetaMode is on with {programs.Count} valid program(s) and {rejected} invalid name(s) in BetaPrograms. With no valid program nobody below GameMaster can sign in.");
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IBetaCodeService>(out var betaService))
			{
				await Log.Error(LogPrefix, "BetaMode is on but IBetaCodeService is not registered; refusing sign-in rather than admitting untested accounts.");
				return ClientAuthenticationResult.ServerBusy;
			}

			var r = await betaService.HasAccessAsync(accountName, programs);
			if (!r.IsSuccess)
			{
				await Log.Error(LogPrefix, $"HasAccessAsync failed: [{r.ErrorCode}] {r.ErrorMessage}. Refusing sign-in (fail-closed).");
				return ClientAuthenticationResult.ServerBusy;
			}
			return r.Data ? (ClientAuthenticationResult?)null : ClientAuthenticationResult.BetaAccessRequired;
		}

		#endregion

		#region Inner Core (bridges FishNet callbacks to SrpAuthenticatorCore)

		/// <summary>
		/// Inner sealed implementation of <see cref="SrpAuthenticatorCore{TConnection}"/> bound to
		/// <see cref="NetworkConnection"/>. All abstract callbacks route to <see cref="outer"/>
		/// (the enclosing <see cref="ServerAuthenticator"/>), which provides FishNet broadcasts,
		/// DB access, and event invocation.
		/// </summary>
		private sealed class ServerAuthenticatorCore : SrpAuthenticatorCore<NetworkConnection>
		{
			/// <summary>The enclosing <see cref="ServerAuthenticator"/> instance that hosts this core.</summary>
			private readonly ServerAuthenticator outer;

			/// <summary>
			/// Initializes the core with the enclosing authenticator and the SRP account manager.
			/// </summary>
			public ServerAuthenticatorCore(ServerAuthenticator outer, ISrpAccountManager<NetworkConnection> accountManager)
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
			protected override string? ResolveClientRealIp(NetworkConnection conn)
			{
				if (conn == null) return null;
				// Authenticator-owned cache is the source of truth (written during
				// ClientHandshake token verify). The account-creation cache is a
				// Login-only mirror and can miss after ClientId reuse.
				string? ip = outer.ResolveRateLimitKey(conn);
				if (!string.IsNullOrEmpty(ip))
					return ip;
				if (outer.Server?.DataContainerRegistry != null &&
					outer.Server.DataContainerRegistry.TryGet<IAccountCreationSystemRuntimeData>(out var rt) &&
					rt.ConnectionIpCache != null)
				{
					if (rt.ConnectionIpCache.TryGetAndTouch(conn.ClientId, DateTime.UtcNow, out ip))
						return ip;
				}
				return null;
			}

			/// <inheritdoc/>
			protected override bool OnHandshakeDeferred(NetworkConnection conn)
			{
				/* NOTE: Both code paths below broadcast ServerBusy. The queue-rejection path
				 * returns false, and the caller (the core handshake handler) may also broadcast
				 * a separate ServerBusy result. The client handles duplicate ServerBusy
				 * gracefully (it opens the same dialog twice at worst).
				 *
				 * Reliable, matching every other BroadcastAuthResult call site in the auth core.
				 * This is the terminal answer to a handshake — there is no retry behind it and
				 * nothing else will ever be sent on this connection. Dropping it left the client
				 * sitting on "Connecting..." for the full 15s server-side handshake timeout and
				 * then being disconnected with no reason given. */
				if (outer.Server?.BehaviourRegistry != null &&
					outer.Server.BehaviourRegistry.TryGet<LoginServer.LoginQueueSystem>(out var queueSystem))
				{
					if (queueSystem.TryEnqueue(conn))
					{
						// The client will re-send a cookieless handshake on this
						// connection once admitted. Clear the handshake rate-limit
						// window so the server-invited retry — including the
						// immediate fast-pass re-admission path — can never trip it.
						outer.ClearHandshakeRateLimit(conn);
						return true;
					}
					// Queue is full — tell the client so they don't hang waiting
					// for a handshake response that will never arrive.
					BroadcastAuthResult(conn, ClientAuthenticationResult.ServerBusy, reliable: true);
					return false;
				}
				// No queue system and auth cap reached — reject immediately with
				// a clear signal rather than leaving the client hanging.
				BroadcastAuthResult(conn, ClientAuthenticationResult.ServerBusy, reliable: true);
				return false;
			}

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

			// ── SrpAuthenticatorCore abstracts ───────────────────────────────
			/// <inheritdoc/>
			protected override void OnAuthenticationResult(NetworkConnection conn, bool authenticated)
			{
				outer.OnAuthentication(conn, authenticated);
				outer.InvokeClientAuthenticationResult(conn, authenticated);
			}

			/// <inheritdoc/>
			protected override bool IsConnectionActive(NetworkConnection conn) => conn.IsActive;

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable, int retryAfterSeconds) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result, RetryAfterSeconds = retryAfterSeconds }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void BroadcastSrpVerifyResponse(NetworkConnection conn, byte[] encSalt, byte[] encServerEphemeral) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new SrpVerifyResponseBroadcast { Salt = encSalt, PublicEphemeral = encServerEphemeral }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void BroadcastSrpSuccess(NetworkConnection conn, byte[] encServerProof, ClientAuthenticationResult result, byte[] encToken) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new SrpSuccessBroadcast { Proof = encServerProof, Result = result, Token = encToken }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void EnqueueMainThread(NetworkConnection conn, Action action) =>
				outer.EnqueueMainThreadAction(action);

			/// <inheritdoc/>
			protected override bool IsAllowedUsername(string username) =>
				Authentication.IsAllowedUsername(username);

			/// <inheritdoc/>
			protected override bool IsAllowedEmailUsername(string username) =>
				Authentication.IsAllowedEmailUsername(username);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username) =>
				outer.TryLoginAsync(defaultResult, username);

			// ── DB callbacks ─────────────────────────────────────────────────
			/// <inheritdoc/>
			protected override Task<SrpAccountLookupResult> FetchAccountForLoginAsync(string identifier, bool isEmail) =>
				outer.FetchAccountForLoginCoreAsync(identifier, isEmail);

			/// <inheritdoc/>
			protected override Task<bool> CheckIsOnlineAsync(string username) =>
				outer.CheckIsOnlineCoreAsync(username);

			/// <inheritdoc/>
			protected override Task<bool> CheckHasPendingKickAsync(string username) =>
				outer.CheckHasPendingKickCoreAsync(username);

			/// <inheritdoc/>
			protected override Task PersistKickRequestAsync(string username) =>
				outer.PersistKickRequestCoreAsync(username);

			/// <inheritdoc/>
			protected override Task<bool> PersistTokenHashAsync(string username, string tokenHash, int expirationMinutes) =>
				outer.PersistTokenHashCoreAsync(username, tokenHash, expirationMinutes);

			/// <inheritdoc/>
			protected override Task<TwoFactorVerifyOutcome> VerifyTotpCodeAsync(string username, string totpCode, byte[] totpMasterKey) =>
				outer.VerifyTotpCodeCoreAsync(username, totpCode, totpMasterKey);

			// ── Database-backed lockout and closed-test gate ─────────────────
			/// <inheritdoc/>
			protected override Task<bool> IsLoginLockedAsync(string accountName) =>
				outer.IsLoginLockedCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task RecordLoginFailureAsync(string accountName) =>
				outer.RecordLoginFailureCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task ClearLoginFailuresAsync(string accountName) =>
				outer.ClearLoginFailuresCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task<DateTime?> GetTwoFactorLockedUntilAsync(string accountName) =>
				outer.GetTwoFactorLockedUntilCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task<DateTime?> RecordTwoFactorFailureAsync(string accountName) =>
				outer.RecordTwoFactorFailureCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task ClearTwoFactorFailuresAsync(string accountName) =>
				outer.ClearTwoFactorFailuresCoreAsync(accountName);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult?> CheckSignInAccessAsync(string accountName, AccessLevel accessLevel) =>
				outer.CheckSignInAccessCoreAsync(accountName, accessLevel);

			/// <inheritdoc/>
			protected override async Task<bool> TryResendVerificationEmailIfExpiredAsync(string username, DateTime? verifyCodeExpiresUtc)
			{
				/* A missing code counts as due, like SMS: with no grace period, an account asked for an email
				 * code it was never sent (email became its channel after it registered) would otherwise wait
				 * for a message nobody is going to send. A live code is never replaced. */
				if (verifyCodeExpiresUtc != null && verifyCodeExpiresUtc.Value > DateTime.UtcNow)
					return false;

				/* Fire-and-forget from the core, like the SMS and Discord twins, so it must never throw:
				 * an exception here became an unobserved task fault that nobody logged. */
				try
				{
					int newCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
					DateTime newExpires = DateTime.UtcNow.AddHours(24);

					if (outer.Server?.Database?.ServiceRegistry == null) return false;
					if (!outer.Server.Database.ServiceRegistry.TryGet<IAccountService>(out var accountService)) return false;

					/* No queue, no new code. Storing a code that nothing will deliver replaces the one the
					 * player may still hold and gives them nothing in its place. */
					if (!outer.Server.Database.ServiceRegistry.TryGet<IEmailQueueService>(out var emailQueueService))
					{
						await Log.Warning(outer.LogPrefix, $"IEmailQueueService not registered -- verification email resend skipped for '{username}'.");
						return false;
					}

					/* The address is read BEFORE the code is replaced, for the reason just given. This read
					 * used to come after the store, and on failure the mail was addressed to the account
					 * name — not an address — so the player's code was replaced by one that was never
					 * delivered, and it looked live for a day so no later sign-in resent either. The same
					 * read re-checks what the verify step saw, as the SMS twin does: a code another login
					 * server or the Control Panel issued since is live and is not replaced. */
					var accountResult = await accountService.FetchForLoginAsync(username, false);
					if (!accountResult.IsSuccess)
					{
						await Log.Warning(outer.LogPrefix, $"FetchForLoginAsync DB error for '{username}': [{accountResult.ErrorCode}] {accountResult.ErrorMessage}. Verification email resend skipped.");
						return false;
					}
					var account = accountResult.Data;
					if (account.Verified ||
						(account.VerifyCodeExpiresUtc != null && account.VerifyCodeExpiresUtc.Value > DateTime.UtcNow))
					{
						return false;
					}
					if (string.IsNullOrWhiteSpace(account.Email))
					{
						await Log.Warning(outer.LogPrefix, $"Email verification is outstanding for '{username}' but no email address is on record; no email code was re-sent.");
						return false;
					}

					var persistResult = await accountService.PersistVerifyCodeAsync(username, newCode, newExpires);
					if (!persistResult.IsSuccess)
					{
						await Log.Warning(outer.LogPrefix, $"PersistVerifyCodeAsync DB error for '{username}': [{persistResult.ErrorCode}] {persistResult.ErrorMessage}. Verification email resend skipped.");
						return false;
					}

					/* The new code is always mailed. This used to skip the mail when a verification email was
					 * already queued — after the new code had been stored — so the queued mail carried the old
					 * code, which the store had just replaced, and the new code looked valid for a day, so no
					 * later sign-in resent either: the player was stuck until it lapsed. Any mail still queued
					 * here carries a code that has already expired (that is why this ran), so it is not worth
					 * protecting; the cost is at most one extra mail per expiry, and an expiry takes a day. */
					string subject = "FishMMO - Verify Your Account";
					string body = outer.BuildLoginVerificationEmailBody(username, newCode);
					var enqueueResult = await emailQueueService.EnqueueAsync(account.Email, username, subject, body);
					if (!enqueueResult.IsSuccess)
					{
						// Stored but not queued: retire it, or it would sit live and undelivered for the day.
						await Log.Warning(outer.LogPrefix, $"Failed to enqueue verification email resend for '{username}': [{enqueueResult.ErrorCode}] {enqueueResult.ErrorMessage}");
						await AccountVerificationPolicy.ExpireUndeliveredCodeAsync(accountService, username, newCode, AccountVerificationChannels.Email, outer.LogPrefix);
						return false;
					}

					// VerificationEmailSentAt is deliberately NOT stamped here. Queueing an email
					// is not sending one: the queue processor stamps it after SMTP confirms
					// delivery, and staff (and the support ticket opened after three wrong codes)
					// read it as "a code actually reached this player".
					return true;
				}
				catch (Exception ex)
				{
					await Log.Error(outer.LogPrefix, $"Verification email resend for '{username}' failed: {ex}");
					return false;
				}
			}

			/// <inheritdoc/>
			protected override Task<bool> TryResendVerificationSmsIfExpiredAsync(string username, DateTime? phoneVerifyCodeExpiresUtc) =>
				outer.TryResendVerificationSmsIfDueCoreAsync(username, phoneVerifyCodeExpiresUtc);

			/// <inheritdoc/>
			protected override Task<bool> TryIssueDiscordVerificationCodeAsync(string username) =>
				outer.TryIssueDiscordVerificationCodeCoreAsync(username);
		}

		#endregion


		/// <summary>
		/// Usernames with a sign-in SMS resend in flight on this server, so two proofs racing for the
		/// same account cannot both generate a code (the second would invalidate the first message).
		/// </summary>
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> smsResendsInFlight =
			new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// The SMS twin of the sign-in email resend: when an unverified account's outstanding SMS code is
		/// missing or expired, issue a new one (same six-digit, 24-hour shape as registration) and queue it.
		/// </summary>
		/// <remarks>
		/// <para>Rate limiting matches the email path: it runs only after a correct password proof, only
		/// when no live code exists (a fresh code is good for 24 hours, so this fires at most once a day
		/// per account), and never while a verification message is still waiting in the queue.</para>
		/// <para>Two deliberate differences, both because a text costs money and a code rotation strands
		/// the message already on its way. The queue check comes BEFORE the new code is generated, so a
		/// queued message is never made worthless by the rotation — the email path rotates first. And the
		/// expiry is re-read from the database rather than trusted from the verify step, so a code another
		/// login server or the Control Panel issued a moment ago is not replaced.</para>
		/// <para>Fire-and-forget from the core: never throws.</para>
		/// </remarks>
		private async Task<bool> TryResendVerificationSmsIfDueCoreAsync(string username, DateTime? phoneVerifyCodeExpiresUtc)
		{
			if (!AccountVerificationPolicy.IsSmsCodeResendDue(phoneVerifyCodeExpiresUtc, DateTime.UtcNow) ||
				string.IsNullOrEmpty(username) ||
				!smsResendsInFlight.TryAdd(username, 0))
			{
				return false;
			}

			try
			{
				var registry = Server?.Database?.ServiceRegistry;
				if (registry == null || !registry.TryGet<IAccountService>(out var accountService))
				{
					return false;
				}
				if (!registry.TryGet<ISmsQueueService>(out var smsQueueService))
				{
					await Log.Warning(LogPrefix, $"ISmsQueueService not registered -- verification SMS resend skipped for '{username}'.");
					return false;
				}

				// A pending check that failed answers as though a message were pending: a text costs
				// money, so an unknown queue state sends nothing rather than risk a duplicate.
				var pending = await smsQueueService.HasPendingForUserAsync(username, SmsKind.Verification);
				if (!pending.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"HasPendingForUserAsync DB error for '{username}': [{pending.ErrorCode}] {pending.ErrorMessage}. Verification SMS resend skipped.");
					return false;
				}
				if (pending.Data)
				{
					return false;
				}

				var fetched = await accountService.FetchForLoginAsync(username, false);
				if (!fetched.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"FetchForLoginAsync DB error for '{username}': [{fetched.ErrorCode}] {fetched.ErrorMessage}. Verification SMS resend skipped.");
					return false;
				}
				var account = fetched.Data;
				if (account.Verified || account.PhoneVerified ||
					!AccountVerificationPolicy.IsSmsCodeResendDue(account.PhoneVerifyCodeExpiresUtc, DateTime.UtcNow))
				{
					return false;
				}
				if (string.IsNullOrEmpty(account.Phone))
				{
					await Log.Warning(LogPrefix, $"SMS verification is outstanding for '{username}' but no phone number is on record; no SMS code was re-sent.");
					return false;
				}

				int newCode = RandomNumberGenerator.GetInt32(100000, 1000000);
				DateTime newExpires = DateTime.UtcNow.AddHours(24);
				DatabaseResult persistResult = await accountService.PersistPhoneVerifyCodeAsync(account.Name, newCode, newExpires);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"PersistPhoneVerifyCodeAsync DB error for '{username}': [{persistResult.ErrorCode}] {persistResult.ErrorMessage}");
					return false;
				}

				DatabaseResult enqueueResult = await smsQueueService.EnqueueAsync(account.Phone, account.Name, BuildLoginVerificationSmsBody(newCode), SmsKind.Verification);
				if (!enqueueResult.IsSuccess)
				{
					// Stored but not queued: retire it, or it would sit live and undelivered for the day
					// and IsSmsCodeResendDue would refuse every sign-in's resend until it lapsed.
					await Log.Warning(LogPrefix, $"Failed to enqueue verification SMS resend for '{username}': [{enqueueResult.ErrorCode}] {enqueueResult.ErrorMessage}");
					await AccountVerificationPolicy.ExpireUndeliveredCodeAsync(accountService, account.Name, newCode, AccountVerificationChannels.Sms, LogPrefix);
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Verification SMS resend for '{username}' failed: {ex}");
				return false;
			}
			finally
			{
				smsResendsInFlight.TryRemove(username, out _);
			}
		}

		/// <summary>
		/// Issues the Discord verification code for an unverified account that chose Discord and was never
		/// issued one, because the channel was switched on after it registered. The Discord bot sends the DM.
		/// </summary>
		/// <remarks>
		/// Reached only after a correct password proof. Never a resend: the database issues a Discord code
		/// once and this does nothing for an account that has one. Fire-and-forget from the core: never throws.
		/// </remarks>
		private async Task<bool> TryIssueDiscordVerificationCodeCoreAsync(string username)
		{
			try
			{
				var registry = Server?.Database?.ServiceRegistry;
				if (string.IsNullOrEmpty(username) || registry == null || !registry.TryGet<IAccountService>(out var accountService))
				{
					return false;
				}

				DatabaseResult<bool> issued = await accountService.PersistDiscordVerifyCodeAsync(username, RandomNumberGenerator.GetInt32(100000, 1000000));
				if (!issued.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"PersistDiscordVerifyCodeAsync DB error for '{username}': [{issued.ErrorCode}] {issued.ErrorMessage}");
					return false;
				}
				return issued.Data;
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Issuing a Discord verification code for '{username}' failed: {ex}");
				return false;
			}
		}

		/// <summary>Builds the login-triggered verification SMS. Matches registration's wording and shape.</summary>
		private static string BuildLoginVerificationSmsBody(int verifyCode)
		{
			return $"FishMMO verification code: {verifyCode:D6}. It expires in 24 hours. If you did not create a FishMMO account, ignore this message.";
		}

		/// <summary>
		/// Builds the HTML body for a login-triggered verification email resend.
		/// </summary>
		/// <param name="username">The account username.</param>
		/// <param name="verifyCode">The 6-digit verification code.</param>
		/// <returns>The HTML email body string.</returns>
		private string BuildLoginVerificationEmailBody(string username, int verifyCode)
		{
			return $@"<html><body style='font-family: Arial, sans-serif; color: #333;'>
				<h2>FishMMO — Verification Code</h2>
				<p>You recently attempted to log in to your FishMMO account <b>{System.Net.WebUtility.HtmlEncode(username)}</b>.</p>
				<p>Your verification code is:</p>
				<h1 style='font-size: 32px; letter-spacing: 4px; color: #2563eb;'>{verifyCode:D6}</h1>
				<p>This code is valid for 24 hours. If you did not request this, you can safely ignore this email.</p>
				<hr/>
				<p style='font-size: 12px; color: #999;'>— The FishMMO Team</p>
			</body></html>";
		}

		/// <summary>Lock for atomic signing-key rotation. Guards swap of TokenSigningKey, TokenSigningKeyId, and (when supplied) TotpMasterKey.</summary>
		private readonly object signingKeySwapLock = new object();

		/// <summary>
		/// Atomically swaps the signing key, key ID, and TOTP master key on the core,
		/// ensuring concurrent token-issuance reads always see a consistent tuple.
		/// </summary>
		/// <remarks>
		/// <para><b>Key material lifetime:</b> Old key arrays are intentionally NOT zeroed
		/// by this method because a concurrent reader thread may hold a reference to them
		/// (obtained before the lock). Calling <see cref="CryptographicOperationsCompat.ZeroMemory"/>
		/// here would corrupt in-flight auth operations that are still using the old key
		/// material. The old arrays will be reclaimed by the garbage collector.</para>
		/// <para><b>NOTE:</b> Old key arrays remain in heap until GC. An attacker with a
		/// memory dump could recover historical signing keys. For defense-in-depth, consider
		/// pinning+zeroing old keys after all concurrent readers have completed.</para>
		/// <para>All key material is zeroed during server shutdown via
		/// <see cref="BaseAuthenticatorCore{TConnection}.ShutdownWorkers"/>. Any arrays held
		/// exclusively by the core are cleared at that point. If a subclass holds additional
		/// references (e.g. backings set before the core existed), it must zero them itself
		/// in its shutdown path.</para>
		/// </remarks>
		public void AtomicSwapSigningKey(byte[] newKey, long newKeyId, byte[] newTotpMasterKey)
		{
			lock (signingKeySwapLock)
			{
				if (core != null)
				{
					// Copy the incoming key arrays so the caller's references remain
					// independent. This prevents a shared-reference scenario where the
					// caller (e.g. RotateSigningKeyAsync) holds the same array reference
					// and passes it to another consumer (e.g. AccountCreationSystem), and
					// a subsequent ZeroMemory on one reference zeroes data the other needs.
					byte[] signingKeyCopy = newKey != null ? new byte[newKey.Length] : null;
					if (signingKeyCopy != null) Buffer.BlockCopy(newKey, 0, signingKeyCopy, 0, signingKeyCopy.Length);
					core.TokenSigningKey = signingKeyCopy;
					core.TokenSigningKeyId = newKeyId;

					/* A null TOTP key means LEAVE IT ALONE, not clear it. The KEK is a deployment
					 * secret that wraps every account's stored TOTP secret; it does not rotate with
					 * the token-signing key, and clearing it here would disable 2FA for the whole
					 * shard on the next rotation. Callers that genuinely want to replace it pass a
					 * 32-byte key. */
					if (newTotpMasterKey != null)
					{
						byte[] totpCopy = new byte[newTotpMasterKey.Length];
						Buffer.BlockCopy(newTotpMasterKey, 0, totpCopy, 0, totpCopy.Length);
						core.TotpMasterKey = totpCopy;
					}
				}
				tokenSigningKeyId = newKeyId;
			}
		}
	}
}