using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Account operations a signed-in player performs on their own account: changing a
	/// password or email, and managing two-factor enrolment.
	/// </summary>
	/// <remarks>
	/// Everything here has an in-game counterpart or is deliberately web-only because the game
	/// client has nowhere sensible to put it. What it does NOT contain is anything that creates
	/// or destroys a character — that is in-game only, by decision, and the panel's character
	/// surface is read-only.
	/// </remarks>
	public sealed class SelfServiceService
	{
		private readonly IAccountService accounts;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly IEmailQueueService emailQueue;
		private readonly IAuthTokenService authTokens;
		private readonly IUnitOfWorkService unitOfWork;
		private readonly TotpKeyProvider totpKeys;
		private readonly ILogger<SelfServiceService> log;

		/// <summary>Matches the in-game 24-hour verification code lifetime.</summary>
		private static readonly TimeSpan VerifyCodeLifetime = TimeSpan.FromHours(24);

		public SelfServiceService(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IEmailQueueService emailQueue,
			IAuthTokenService authTokens,
			IUnitOfWorkService unitOfWork,
			TotpKeyProvider totpKeys,
			ILogger<SelfServiceService> log)
		{
			this.accounts = accounts;
			this.recoveryCodes = recoveryCodes;
			this.emailQueue = emailQueue;
			this.authTokens = authTokens;
			this.unitOfWork = unitOfWork;
			this.totpKeys = totpKeys;
			this.log = log;
		}

		/// <summary>Outcome of an operation that either worked or did not.</summary>
		public sealed record Outcome(bool Ok, string Error);

		/// <summary>
		/// Outcome of a password change: whether the credentials changed, and whether every game
		/// token went with them.
		/// </summary>
		/// <remarks>
		/// The second half is its own field because it can fail after the first succeeded, and the
		/// two need different answers: a password that did not change is an error, a password that
		/// changed while old tokens survived is a success the account holder must be told is partial.
		/// </remarks>
		public sealed record PasswordChange(bool Ok, string Error, bool GameTokensRevoked);

		/// <summary>Outcome of an email change, and whether a verification code for the new address was issued.</summary>
		public sealed record EmailChange(bool Ok, string Error, bool VerificationSent);

		/// <summary>Two-factor enrolment material, returned exactly once.</summary>
		public sealed record TwoFactorSetup(bool Ok, string Error, string OtpauthUri, IReadOnlyList<string> RecoveryCodes);

		/// <summary>
		/// Replaces the account's SRP credentials after its current password has been proved.
		/// </summary>
		/// <remarks>
		/// The proof is the caller's job — the panel runs a full SRP exchange against the stored
		/// verifier first, because a plaintext password exists nowhere on this side to check.
		/// Every game token and every panel session is revoked afterwards: a password change that
		/// leaves the old sessions alive is not a password change.
		/// </remarks>
		public async Task<PasswordChange> ChangePasswordAsync(
			string username,
			string newSalt,
			string newVerifier,
			CancellationToken cancellationToken = default)
		{
			var result = await accounts.PersistSrpCredentialsAsync(username, newSalt, newVerifier, cancellationToken);
			if (!result.IsSuccess)
			{
				log.LogWarning("PersistSrpCredentialsAsync failed for '{User}': [{Code}] {Message}",
					username, result.ErrorCode, result.ErrorMessage);
				return new PasswordChange(false, "That password could not be changed.", false);
			}

			// Not fatal: the credentials have already changed, so failing the whole call here would
			// leave the caller thinking they had not. It is REPORTED, though — the caller tells the
			// account holder that a game client may still be signed in, rather than that none is.
			var revoked = await authTokens.RevokeAllForAccountAsync(username, cancellationToken);
			if (!revoked.IsSuccess)
			{
				log.LogError("Password for '{User}' changed but game tokens were NOT revoked: [{Code}] {Message}",
					username, revoked.ErrorCode, revoked.ErrorMessage);
			}

			return new PasswordChange(true, null, revoked.IsSuccess);
		}

		/// <summary>
		/// Changes the contact email and starts verification again.
		/// </summary>
		/// <remarks>
		/// The account drops back to unverified, because an unverified address is exactly what it
		/// now has. <c>PersistEmailAsync</c> clears the verified flag and the pending code as one
		/// statement, then a fresh code is issued and queued.
		/// </remarks>
		public async Task<EmailChange> ChangeEmailAsync(
			string username,
			string email,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(email) || !FishMMO.Shared.Authentication.IsAllowedEmailUsername(email))
			{
				return new EmailChange(false, "That does not look like an email address.", false);
			}

			var result = await accounts.PersistEmailAsync(username, email, cancellationToken);
			if (!result.IsSuccess)
			{
				log.LogWarning("PersistEmailAsync failed for '{User}': [{Code}] {Message}",
					username, result.ErrorCode, result.ErrorMessage);
				return new EmailChange(false, "That email could not be saved.", false);
			}

			/* The address is changed and the account is unverified from here on, so what is left is
			 * issuing the code — and a code that was not stored must never be mailed. It used to be:
			 * the write failed, the mail went out anyway, and the player typed a code that could not
			 * match into a form that counts wrong codes towards a support ticket, while the pending
			 * mail also held off the resend that would have fixed it. Now the mail goes only with a
			 * stored code, and the caller says when none was sent so the player uses Resend. */
			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var codeResult = await accounts.PersistVerifyCodeAsync(
				username, verifyCode, DateTime.UtcNow + VerifyCodeLifetime, cancellationToken);
			if (!codeResult.IsSuccess)
			{
				log.LogWarning("PersistVerifyCodeAsync failed for '{User}': [{Code}] {Message}. No verification email was queued.",
					username, codeResult.ErrorCode, codeResult.ErrorMessage);
				return new EmailChange(true, null, false);
			}

			var enqueue = await emailQueue.EnqueueAsync(
				email, username, "FishMMO - Verify Your Email", BuildVerificationEmailBody(username, verifyCode),
				EmailKind.Verification, cancellationToken);
			if (!enqueue.IsSuccess)
			{
				log.LogWarning("Failed to enqueue the verification email for '{User}': [{Code}] {Message}",
					username, enqueue.ErrorCode, enqueue.ErrorMessage);
				return new EmailChange(true, null, false);
			}

			return new EmailChange(true, null, true);
		}

		/// <summary>
		/// Generates fresh two-factor enrolment material and STAGES it: nothing sign-in reads changes
		/// until <see cref="ConfirmTwoFactorAsync"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The new secret goes into the account's pending column and the new recovery codes in as
		/// pending codes, beside the live ones. Confirming with a code from the new authenticator
		/// promotes both at once; abandoning the setup leaves the account exactly as it was. This used
		/// to write the live secret and replace the live codes here, so a re-enrolment the player did
		/// not finish — a scan that never happened, a closed tab — left them with an authenticator
		/// that no longer worked and codes they may never have saved (issue #267).
		/// </para>
		/// <para>
		/// A first enrolment is staged the same way; confirming it is what turns two-factor on. A
		/// re-enrolment still needs a fresh step-up to start: whoever confirms holds the new
		/// authenticator, so without it a borrowed signed-in browser could swap the owner's for its
		/// own. See <c>AccountController.BeginTwoFactorSetup</c>.
		/// </para>
		/// </remarks>
		public async Task<TwoFactorSetup> BeginTwoFactorSetupAsync(
			string username,
			CancellationToken cancellationToken = default)
		{
			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("TOTP master KEK unavailable; enrolment is impossible. {Error}", totpKeys.LoadError);
				return new TwoFactorSetup(false, "Two-factor is unavailable on this server right now.", null, null);
			}

			var begun = await unitOfWork.BeginAsync(cancellationToken);
			if (!begun.IsSuccess)
			{
				log.LogWarning("Could not begin staging two-factor for '{User}': [{Code}] {Message}",
					username, begun.ErrorCode, begun.ErrorMessage);
				return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
			}

			byte[] secret = null;
			try
			{
				await using IUnitOfWork uow = begun.Data;

				secret = CryptoHelper.TwoFactor.GenerateTotpSecret();
				string encrypted = CryptoHelper.TwoFactor.EncryptTotpSecret(masterKek, username, secret);
				var staged = await accounts.PersistPendingTotpSecretAsync(username, encrypted, cancellationToken);
				if (!staged.IsSuccess)
				{
					log.LogWarning("PersistPendingTotpSecretAsync failed for '{User}': [{Code}] {Message}",
						username, staged.ErrorCode, staged.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				var hashes = new List<string>(codes.Length);
				foreach (string code in codes)
				{
					hashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
				}
				// Codes that were not stored must never be shown: the player would keep them as a way back in.
				var stagedCodes = await recoveryCodes.StagePendingAsync(username, hashes, cancellationToken);
				if (!stagedCodes.IsSuccess)
				{
					log.LogError("Staging recovery codes failed for '{User}': [{Code}] {Message}",
						username, stagedCodes.ErrorCode, stagedCodes.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				var committed = await uow.CommitAsync(cancellationToken);
				if (!committed.IsSuccess)
				{
					log.LogWarning("Staging two-factor for '{User}' could not be committed; nothing changed: [{Code}] {Message}",
						username, committed.ErrorCode, committed.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				return new TwoFactorSetup(true, null, CryptoHelper.TwoFactor.BuildOtpauthUri(secret, username), codes);
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Staging two-factor failed for '{User}'.", username);
				return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
			}
			finally
			{
				if (secret != null)
				{
					CryptographicOperations.ZeroMemory(secret);
				}
			}
		}

		/// <summary>
		/// Replaces the account's authenticator secret and recovery codes in ONE transaction, and
		/// turns two-factor on in the same one when <paramref name="enable"/> is set.
		/// </summary>
		/// <remarks>
		/// <para>
		/// One transaction because every partial state is worse than none. These writes used to be
		/// separate, each failing on its own: a delete of the old recovery codes whose result was
		/// discarded left codes for a discarded authenticator still opening the account, and a failed
		/// insert of the new ones still handed the player ten codes that existed nowhere. Now the
		/// account either has the new secret and exactly the new codes, or it has what it had
		/// before, and the caller is told which.
		/// </para>
		/// <para>
		/// Registration uses this with <paramref name="enable"/> set, so a new account never ends up
		/// with a stored secret but two-factor off, or two-factor on with codes that were not saved.
		/// </para>
		/// </remarks>
		public async Task<TwoFactorSetup> EnrolAsync(
			string username,
			bool enable,
			CancellationToken cancellationToken = default)
		{
			var begun = await unitOfWork.BeginAsync(cancellationToken);
			if (!begun.IsSuccess)
			{
				log.LogWarning("Could not begin two-factor enrolment for '{User}': [{Code}] {Message}",
					username, begun.ErrorCode, begun.ErrorMessage);
				return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
			}

			await using (IUnitOfWork uow = begun.Data)
			{
				TwoFactorSetup setup = await WriteEnrolmentAsync(username, enable, cancellationToken);
				if (!setup.Ok)
				{
					// Leaving without a commit rolls the unit of work back: nothing it wrote survives.
					return setup;
				}

				var committed = await uow.CommitAsync(cancellationToken);
				if (!committed.IsSuccess)
				{
					log.LogWarning("Two-factor enrolment for '{User}' could not be committed; nothing changed: [{Code}] {Message}",
						username, committed.ErrorCode, committed.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}
				return setup;
			}
		}

		/// <summary>
		/// The enrolment writes, with no transaction of their own.
		/// </summary>
		/// <remarks>
		/// Must run inside a unit of work the caller owns and commits — <see cref="EnrolAsync"/>, or
		/// the self-service two-factor reset, which completes its request in the same transaction so a
		/// failed enrolment does not spend the player's waiting period. Every failure here returns
		/// before anything else is written, and the caller's rollback undoes what came before it.
		/// </remarks>
		internal async Task<TwoFactorSetup> WriteEnrolmentAsync(
			string username,
			bool enable,
			CancellationToken cancellationToken)
		{
			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				log.LogError("TOTP master KEK unavailable; enrolment is impossible. {Error}", totpKeys.LoadError);
				return new TwoFactorSetup(false, "Two-factor is unavailable on this server right now.", null, null);
			}

			byte[] secret = null;
			try
			{
				secret = CryptoHelper.TwoFactor.GenerateTotpSecret();
				string encrypted = CryptoHelper.TwoFactor.EncryptTotpSecret(masterKek, username, secret);

				/* Replace the recovery codes with the new secret. Keeping the old ones would let a
				 * code issued against a discarded authenticator still open the account, so a failed
				 * delete fails the enrolment rather than being stepped over. */
				var deleted = await recoveryCodes.DeleteAllForAccountAsync(username, cancellationToken);
				if (!deleted.IsSuccess)
				{
					log.LogError("Could not delete the previous recovery codes for '{User}': [{Code}] {Message}",
						username, deleted.ErrorCode, deleted.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				var stored = await accounts.PersistTotpSecretAsync(username, encrypted, cancellationToken);
				if (!stored.IsSuccess)
				{
					log.LogWarning("PersistTotpSecretAsync failed for '{User}': [{Code}] {Message}",
						username, stored.ErrorCode, stored.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				if (enable)
				{
					var enabled = await accounts.PersistTotpEnabledAsync(username, true, cancellationToken);
					if (!enabled.IsSuccess)
					{
						log.LogWarning("PersistTotpEnabledAsync failed for '{User}': [{Code}] {Message}",
							username, enabled.ErrorCode, enabled.ErrorMessage);
						return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
					}
				}

				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				var hashes = new List<string>(codes.Length);
				foreach (string code in codes)
				{
					hashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
				}
				// Codes that were not stored must never be shown: the player would keep them as a way back in.
				var persisted = await recoveryCodes.PersistManyAsync(username, hashes, cancellationToken);
				if (!persisted.IsSuccess)
				{
					log.LogError("Recovery code persistence failed for '{User}': [{Code}] {Message}",
						username, persisted.ErrorCode, persisted.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				return new TwoFactorSetup(true, null, CryptoHelper.TwoFactor.BuildOtpauthUri(secret, username), codes);
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Two-factor enrolment failed for '{User}'.", username);
				return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
			}
			finally
			{
				if (secret != null)
				{
					CryptographicOperations.ZeroMemory(secret);
				}
			}
		}

		/// <summary>
		/// Promotes the staged authenticator and recovery codes once a code from the new
		/// authenticator verifies, and turns two-factor on.
		/// </summary>
		/// <param name="username">The signed-in account.</param>
		/// <param name="pending">The outcome of <c>TwoFactorService.VerifyPendingAsync</c> for the submitted code.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <remarks>
		/// The secret and the codes go live in one transaction, or neither does: a live secret with the
		/// old codes, or new codes with the old secret, would leave one of the two unusable.
		/// </remarks>
		public async Task<Outcome> ConfirmTwoFactorAsync(
			string username,
			TwoFactorService.PendingCheck pending,
			CancellationToken cancellationToken = default)
		{
			if (pending.Verdict == TwoFactorService.PendingVerdict.NothingStaged)
			{
				return new Outcome(false, "There is no new authenticator waiting to be confirmed. Start the setup again.");
			}
			if (pending.Verdict != TwoFactorService.PendingVerdict.Valid)
			{
				return new Outcome(false, "That code is not valid.");
			}

			var begun = await unitOfWork.BeginAsync(cancellationToken);
			if (!begun.IsSuccess)
			{
				log.LogWarning("Could not begin confirming two-factor for '{User}': [{Code}] {Message}",
					username, begun.ErrorCode, begun.ErrorMessage);
				return new Outcome(false, "Two-factor could not be enabled.");
			}

			await using (IUnitOfWork uow = begun.Data)
			{
				var promoted = await accounts.PromotePendingTotpSecretAsync(username, pending.Window, cancellationToken);
				if (!promoted.IsSuccess || !promoted.Data)
				{
					log.LogWarning("PromotePendingTotpSecretAsync did not promote for '{User}': [{Code}] {Message}",
						username, promoted.ErrorCode, promoted.ErrorMessage);
					return new Outcome(false, promoted.IsSuccess
						? "There is no new authenticator waiting to be confirmed. Start the setup again."
						: "Two-factor could not be enabled.");
				}

				var codes = await recoveryCodes.PromotePendingAsync(username, cancellationToken);
				if (!codes.IsSuccess)
				{
					log.LogError("Promoting staged recovery codes failed for '{User}'; nothing changed: [{Code}] {Message}",
						username, codes.ErrorCode, codes.ErrorMessage);
					return new Outcome(false, "Two-factor could not be enabled.");
				}

				var committed = await uow.CommitAsync(cancellationToken);
				if (!committed.IsSuccess)
				{
					log.LogWarning("Confirming two-factor for '{User}' could not be committed; nothing changed: [{Code}] {Message}",
						username, committed.ErrorCode, committed.ErrorMessage);
					return new Outcome(false, "Two-factor could not be enabled.");
				}
			}
			return new Outcome(true, null);
		}

		/// <summary>
		/// Turns two-factor off and invalidates every recovery code with it.
		/// </summary>
		/// <remarks>
		/// <c>ClearTotpAsync</c> resets the secret, the enabled flag, the verified timestamp and
		/// the replay window atomically. The recovery codes are a separate table and have to go
		/// separately, or codes for a secret that no longer exists would linger.
		/// </remarks>
		public async Task<Outcome> DisableTwoFactorAsync(
			string username,
			CancellationToken cancellationToken = default)
		{
			var cleared = await accounts.ClearTotpAsync(username, cancellationToken);
			if (!cleared.IsSuccess)
			{
				log.LogWarning("ClearTotpAsync failed for '{User}': [{Code}] {Message}",
					username, cleared.ErrorCode, cleared.ErrorMessage);
				return new Outcome(false, "Two-factor could not be disabled.");
			}

			var deleted = await recoveryCodes.DeleteAllForAccountAsync(username, cancellationToken);
			if (!deleted.IsSuccess)
			{
				log.LogError("TOTP cleared for '{User}' but recovery codes were NOT deleted: [{Code}] {Message}",
					username, deleted.ErrorCode, deleted.ErrorMessage);
			}

			return new Outcome(true, null);
		}

		/// <summary>
		/// Issues a fresh set of recovery codes, discarding the previous ones.
		/// </summary>
		/// <remarks>
		/// The delete and the insert are one transaction. Apart, a failed insert after a good delete
		/// left the account with no codes at all while the player was told they "could not be
		/// replaced" — and so still believed the old ones worked.
		/// </remarks>
		public async Task<TwoFactorSetup> RegenerateRecoveryCodesAsync(
			string username,
			CancellationToken cancellationToken = default)
		{
			var begun = await unitOfWork.BeginAsync(cancellationToken);
			if (!begun.IsSuccess)
			{
				log.LogWarning("Could not begin replacing the recovery codes for '{User}': [{Code}] {Message}",
					username, begun.ErrorCode, begun.ErrorMessage);
				return new TwoFactorSetup(false, "Those codes could not be replaced.", null, null);
			}

			await using (IUnitOfWork uow = begun.Data)
			{
				var deleted = await recoveryCodes.DeleteAllForAccountAsync(username, cancellationToken);
				if (!deleted.IsSuccess)
				{
					// Leaving the old codes valid alongside new ones would quietly double the number
					// of ways in. Returning without a commit rolls the unit of work back.
					log.LogError("Could not delete the previous recovery codes for '{User}': [{Code}] {Message}",
						username, deleted.ErrorCode, deleted.ErrorMessage);
					return new TwoFactorSetup(false, "Those codes could not be replaced.", null, null);
				}

				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				var hashes = new List<string>(codes.Length);
				foreach (string code in codes)
				{
					hashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
				}

				var persisted = await recoveryCodes.PersistManyAsync(username, hashes, cancellationToken);
				if (!persisted.IsSuccess)
				{
					log.LogError("Recovery code persistence failed for '{User}': [{Code}] {Message}",
						username, persisted.ErrorCode, persisted.ErrorMessage);
					return new TwoFactorSetup(false, "Those codes could not be replaced.", null, null);
				}

				var committed = await uow.CommitAsync(cancellationToken);
				if (!committed.IsSuccess)
				{
					log.LogError("Replacing the recovery codes for '{User}' could not be committed; the old codes still stand: [{Code}] {Message}",
						username, committed.ErrorCode, committed.ErrorMessage);
					return new TwoFactorSetup(false, "Those codes could not be replaced.", null, null);
				}

				return new TwoFactorSetup(true, null, null, codes);
			}
		}

		/// <summary>How many unused recovery codes an account has left.</summary>
		public async Task<int> CountRemainingRecoveryCodesAsync(
			string username,
			CancellationToken cancellationToken = default)
		{
			var result = await recoveryCodes.FetchUnusedByAccountAsync(username, cancellationToken);
			return result.IsSuccess && result.Data != null ? result.Data.Count : 0;
		}

		private static string BuildVerificationEmailBody(string username, int verifyCode)
		{
			return
				$"Hello {username},\n\n" +
				$"Your FishMMO account email was changed.\n\n" +
				$"Your verification code is: {verifyCode}\n\n" +
				$"Enter this code in the game client or on the web control panel to verify this address.\n" +
				$"This code expires in 24 hours.\n\n" +
				$"If you did not request this, contact support.\n";
		}
	}
}
