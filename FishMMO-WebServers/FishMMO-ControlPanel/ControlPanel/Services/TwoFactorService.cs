using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Verifies TOTP codes and recovery codes, mirroring
	/// <c>ServerAuthenticator.VerifyTotpCodeCoreAsync</c> exactly.
	/// </summary>
	/// <remarks>
	/// Identical rules to the game path, deliberately: the recovery-code shape test, the replay
	/// window persistence, the first-verification stamp, and single-use consumption. A code that
	/// works in the client works here, and one consumed here is gone there too.
	/// </remarks>
	public sealed class TwoFactorService
	{
		private readonly IAccountService accounts;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly ITwoFactorResetRequestService resetRequests;
		private readonly SecurityNoticeService notices;
		private readonly AuthLockoutOptions lockout;
		private readonly TotpKeyProvider totpKeys;
		private readonly ILogger<TwoFactorService> log;

		public TwoFactorService(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			ITwoFactorResetRequestService resetRequests,
			SecurityNoticeService notices,
			AuthLockoutOptions lockout,
			TotpKeyProvider totpKeys,
			ILogger<TwoFactorService> log)
		{
			this.accounts = accounts;
			this.recoveryCodes = recoveryCodes;
			this.resetRequests = resetRequests;
			this.notices = notices;
			this.lockout = lockout;
			this.totpKeys = totpKeys;
			this.log = log;
		}

		/// <summary>How a guarded two-factor check ended.</summary>
		public enum Verdict
		{
			/// <summary>The code was good; failures cleared, any pending reset cancelled.</summary>
			Ok,
			/// <summary>The code was wrong, used, replayed, or the wrong kind.</summary>
			Invalid,
			/// <summary>The two-factor step is locked; <see cref="Check.LockedUntilUtc"/> says until when.</summary>
			Locked,
			/// <summary>
			/// The lockout could not be read, so no code was tried and nothing was counted. The caller
			/// answers "try again shortly", never "invalid".
			/// </summary>
			Unavailable,
		}

		/// <summary>The result of <see cref="VerifyForSignInAsync"/>.</summary>
		public sealed record Check(Verdict Verdict, DateTime? LockedUntilUtc);

		/// <summary>
		/// A two-factor check at sign-in or step-up: lockout, verification, failure count, and the
		/// cancellation of a pending self-service reset.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The session asking has already proven the password, so unlike the password step a lock is
		/// stated plainly, with its end: there is nobody left to hide the account's existence from.
		/// </para>
		/// <para>
		/// <b>A good code cancels a pending two-factor reset</b>, and the holder is told. Passing the
		/// real factor proves the owner still has it, which is precisely the case the reset's waiting
		/// period exists to catch.
		/// </para>
		/// </remarks>
		/// <param name="authenticatorOnly">Refuse recovery codes: the reset confirmation must prove the NEW authenticator.</param>
		public async Task<Check> VerifyForSignInAsync(string username, string code, bool authenticatorOnly, CancellationToken cancellationToken = default)
		{
			DateTime now = DateTime.UtcNow;
			var state = await accounts.FetchAuthLockoutAsync(username, cancellationToken);
			if (!state.IsSuccess)
			{
				/* FAIL CLOSED. The lock is the only thing between a guesser and a six-digit code space,
				 * and this read failing used to mean "not locked": a code was tried against an account
				 * that may well be locked, and — the failure counter living in the same database — the
				 * wrong guess usually went uncounted too. So a lockout that cannot be read tries no code
				 * at all. FetchAuthLockoutAsync answers success for an unknown account, so only a
				 * database fault lands here. */
				log.LogWarning("Could not read the two-factor lockout for '{User}'; no code was tried: [{Code}] {Message}",
					username, state.ErrorCode, state.ErrorMessage);
				return new Check(Verdict.Unavailable, null);
			}
			if (state.Data.IsLocked(AuthFailureKind.TwoFactor, now))
			{
				return new Check(Verdict.Locked, state.Data.TwoFactorLockedUntilUtc);
			}

			bool wrongKind = authenticatorOnly && code != null && CryptoHelper.TwoFactor.LooksLikeRecoveryCode(code.Trim());
			bool ok = !wrongKind && await VerifyAsync(username, code, cancellationToken);
			if (!ok)
			{
				var recorded = await accounts.RecordAuthFailureAsync(
					username, AuthFailureKind.TwoFactor,
					lockout.TwoFactorThreshold,
					TimeSpan.FromMinutes(lockout.TwoFactorWindowMinutes),
					TimeSpan.FromMinutes(lockout.TwoFactorLockMinutes),
					cancellationToken);
				if (!recorded.IsSuccess)
				{
					log.LogWarning("Could not count a failed two-factor code for '{User}': [{Code}] {Message}",
						username, recorded.ErrorCode, recorded.ErrorMessage);
					return new Check(Verdict.Invalid, null);
				}
				if (recorded.Data.HasValue && recorded.Data.Value > now)
				{
					log.LogWarning("Two-factor for '{User}' locked until {Until:o} after repeated failures.", username, recorded.Data.Value);
					await notices.SignInLockedAsync(username, AuthFailureKind.TwoFactor, recorded.Data.Value, cancellationToken);
					return new Check(Verdict.Locked, recorded.Data.Value);
				}
				return new Check(Verdict.Invalid, null);
			}

			var cleared = await accounts.ClearAuthFailuresAsync(username, AuthFailureKind.TwoFactor, cancellationToken);
			if (!cleared.IsSuccess)
			{
				log.LogWarning("Could not clear the two-factor failure count for '{User}': [{Code}] {Message}",
					username, cleared.ErrorCode, cleared.ErrorMessage);
			}

			// Cancelled by the account itself, which is who the service expects to be named here.
			var cancelled = await resetRequests.CancelAsync(username, username, null, cancellationToken);
			if (!cancelled.IsSuccess)
			{
				log.LogError("A good two-factor code for '{User}' could NOT cancel a pending reset check: [{Code}] {Message}",
					username, cancelled.ErrorCode, cancelled.ErrorMessage);
			}
			else if (cancelled.Data)
			{
				log.LogWarning("Pending two-factor reset for '{User}' cancelled by a successful two-factor sign-in.", username);
				await notices.ResetCancelledAsync(username, byStaff: false, cancellationToken);
			}

			return new Check(Verdict.Ok, null);
		}

		/// <summary>What checking a code against a staged, unconfirmed authenticator found.</summary>
		public enum PendingVerdict
		{
			/// <summary>The code came from the staged authenticator.</summary>
			Valid,
			/// <summary>The code did not verify, or could not be checked.</summary>
			Invalid,
			/// <summary>No authenticator is staged for this account.</summary>
			NothingStaged,
		}

		/// <summary>The verdict, and the time window of a valid code for the replay guard.</summary>
		public readonly record struct PendingCheck(PendingVerdict Verdict, long Window);

		/// <summary>
		/// Checks a code against the account's STAGED authenticator — the one a re-enrolment has not
		/// confirmed yet — without touching anything sign-in reads.
		/// </summary>
		/// <remarks>
		/// An authenticator code only: confirming a new authenticator with a recovery code would prove
		/// nothing about the new one. Nothing is persisted here; the window a valid code used is
		/// returned for <c>SelfServiceService.ConfirmTwoFactorAsync</c>, which records it as it
		/// promotes the secret, so the confirming code cannot be replayed at sign-in.
		/// </remarks>
		public async Task<PendingCheck> VerifyPendingAsync(string username, string code, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
			{
				return new PendingCheck(PendingVerdict.Invalid, 0);
			}

			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("TOTP master KEK unavailable; no code can be verified. {Error}", totpKeys.LoadError);
				return new PendingCheck(PendingVerdict.Invalid, 0);
			}

			var pending = await accounts.FetchPendingTotpSecretAsync(username, cancellationToken);
			if (!pending.IsSuccess)
			{
				log.LogWarning("Could not read the staged authenticator for '{User}': [{Code}] {Message}",
					username, pending.ErrorCode, pending.ErrorMessage);
				return new PendingCheck(PendingVerdict.Invalid, 0);
			}
			if (string.IsNullOrEmpty(pending.Data))
			{
				return new PendingCheck(PendingVerdict.NothingStaged, 0);
			}

			byte[] plaintextSecret = null;
			try
			{
				plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(masterKek, username, pending.Data);
				// No earlier window applies: this secret has never verified a code.
				var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(plaintextSecret, code.Trim(), 0);
				return valid
					? new PendingCheck(PendingVerdict.Valid, windowUsed)
					: new PendingCheck(PendingVerdict.Invalid, 0);
			}
			catch (CryptographicException ex)
			{
				log.LogError(ex, "Could not decrypt the staged TOTP secret for '{User}'.", username);
				return new PendingCheck(PendingVerdict.Invalid, 0);
			}
			finally
			{
				if (plaintextSecret != null)
				{
					CryptographicOperations.ZeroMemory(plaintextSecret);
				}
			}
		}

		/// <summary>
		/// Verifies a TOTP code or a recovery code for an account.
		/// </summary>
		/// <returns><c>true</c> when the code was valid and any state it consumes was persisted.</returns>
		public async Task<bool> VerifyAsync(string username, string code, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
			{
				return false;
			}

			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("TOTP master KEK unavailable; no code can be verified. {Error}", totpKeys.LoadError);
				return false;
			}

			var accountResult = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (!accountResult.IsSuccess || string.IsNullOrEmpty(accountResult.Data.TotpSecret))
			{
				return false;
			}

			code = code.Trim();

			if (CryptoHelper.TwoFactor.LooksLikeRecoveryCode(code))
			{
				var codesResult = await recoveryCodes.FetchUnusedByAccountAsync(username, cancellationToken);
				if (!codesResult.IsSuccess || codesResult.Data == null || codesResult.Data.Count == 0)
				{
					return false;
				}

				string matchedHash = null;
				foreach (var stored in codesResult.Data)
				{
					if (CryptoHelper.TwoFactor.VerifyRecoveryCode(username, code, stored.CodeHash))
					{
						matchedHash = stored.CodeHash;
						break;
					}
				}
				if (matchedHash == null)
				{
					return false;
				}

				// Single use: the consume is what makes it so, and a failed consume must not
				// count as a successful verification.
				var consume = await recoveryCodes.ConsumeCodeAsync(username, matchedHash, cancellationToken);
				return consume.IsSuccess;
			}

			byte[] plaintextSecret = null;
			try
			{
				plaintextSecret = CryptoHelper.TwoFactor.DecryptTotpSecret(masterKek, username, accountResult.Data.TotpSecret);
				var (valid, windowUsed) = CryptoHelper.TwoFactor.VerifyTotpCode(
					plaintextSecret, code, accountResult.Data.LastTotpWindow);
				if (!valid)
				{
					return false;
				}

				// Persisting the window is the replay guard, so a failure to persist is a failed
				// verification rather than a successful one with a hole in it.
				var persist = accountResult.Data.TotpVerifiedAt == null
					? await accounts.PersistTotpVerifiedAtAsync(username, windowUsed, cancellationToken)
					: await accounts.PersistLastTotpWindowAsync(username, windowUsed, cancellationToken);
				return persist.IsSuccess;
			}
			catch (CryptographicException ex)
			{
				// A stored envelope that will not open under the deployment KEK. Before the KEK
				// became a deployment secret this was the normal state of every account after a
				// LoginServer restart.
				log.LogError(ex, "Could not decrypt the stored TOTP secret for '{User}'.", username);
				return false;
			}
			finally
			{
				if (plaintextSecret != null)
				{
					CryptographicOperations.ZeroMemory(plaintextSecret);
				}
			}
		}

	}
}
