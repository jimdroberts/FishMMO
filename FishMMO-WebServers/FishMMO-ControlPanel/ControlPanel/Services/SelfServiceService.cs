using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
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
		private readonly TotpKeyProvider totpKeys;
		private readonly ILogger<SelfServiceService> log;

		/// <summary>Matches the in-game 24-hour verification code lifetime.</summary>
		private static readonly TimeSpan VerifyCodeLifetime = TimeSpan.FromHours(24);

		public SelfServiceService(
			IAccountService accounts,
			ITwoFactorRecoveryCodeService recoveryCodes,
			IEmailQueueService emailQueue,
			IAuthTokenService authTokens,
			TotpKeyProvider totpKeys,
			ILogger<SelfServiceService> log)
		{
			this.accounts = accounts;
			this.recoveryCodes = recoveryCodes;
			this.emailQueue = emailQueue;
			this.authTokens = authTokens;
			this.totpKeys = totpKeys;
			this.log = log;
		}

		/// <summary>Outcome of an operation that either worked or did not.</summary>
		public sealed record Outcome(bool Ok, string Error);

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
		public async Task<Outcome> ChangePasswordAsync(
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
				return new Outcome(false, "That password could not be changed.");
			}

			// Best effort, and deliberately not fatal: the credentials have already changed, so
			// failing the whole call here would leave the caller thinking they had not.
			var revoked = await authTokens.RevokeAllForAccountAsync(username, cancellationToken);
			if (!revoked.IsSuccess)
			{
				log.LogError("Password for '{User}' changed but game tokens were NOT revoked: [{Code}] {Message}",
					username, revoked.ErrorCode, revoked.ErrorMessage);
			}

			return new Outcome(true, null);
		}

		/// <summary>
		/// Changes the contact email and starts verification again.
		/// </summary>
		/// <remarks>
		/// The account drops back to unverified, because an unverified address is exactly what it
		/// now has. <c>PersistEmailAsync</c> clears the verified flag and the pending code as one
		/// statement, then a fresh code is issued and queued.
		/// </remarks>
		public async Task<Outcome> ChangeEmailAsync(
			string username,
			string email,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(email) || !FishMMO.Shared.Authentication.IsAllowedEmailUsername(email))
			{
				return new Outcome(false, "That does not look like an email address.");
			}

			var result = await accounts.PersistEmailAsync(username, email, cancellationToken);
			if (!result.IsSuccess)
			{
				log.LogWarning("PersistEmailAsync failed for '{User}': [{Code}] {Message}",
					username, result.ErrorCode, result.ErrorMessage);
				return new Outcome(false, "That email could not be saved.");
			}

			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var codeResult = await accounts.PersistVerifyCodeAsync(
				username, verifyCode, DateTime.UtcNow + VerifyCodeLifetime, cancellationToken);
			if (!codeResult.IsSuccess)
			{
				log.LogWarning("PersistVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
					username, codeResult.ErrorCode, codeResult.ErrorMessage);
			}

			var enqueue = await emailQueue.EnqueueAsync(
				email, username, "FishMMO - Verify Your Email", BuildVerificationEmailBody(username, verifyCode), cancellationToken);
			if (!enqueue.IsSuccess)
			{
				log.LogWarning("Failed to enqueue the verification email for '{User}': [{Code}] {Message}",
					username, enqueue.ErrorCode, enqueue.ErrorMessage);
			}

			return new Outcome(true, null);
		}

		/// <summary>
		/// Generates fresh two-factor enrolment material and stores the secret, WITHOUT enabling
		/// two-factor.
		/// </summary>
		/// <remarks>
		/// Enabling happens only in <see cref="ConfirmTwoFactorAsync"/>, once the account holder
		/// has proved their authenticator actually works. Enabling first would let a mistyped
		/// scan lock somebody out of their own account permanently.
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

			byte[] secret = null;
			try
			{
				secret = CryptoHelper.TwoFactor.GenerateTotpSecret();
				string encrypted = CryptoHelper.TwoFactor.EncryptTotpSecret(masterKek, username, secret);

				var stored = await accounts.PersistTotpSecretAsync(username, encrypted, cancellationToken);
				if (!stored.IsSuccess)
				{
					log.LogWarning("PersistTotpSecretAsync failed for '{User}': [{Code}] {Message}",
						username, stored.ErrorCode, stored.ErrorMessage);
					return new TwoFactorSetup(false, "That enrolment could not be started.", null, null);
				}

				/* Replace the recovery codes with the new secret. Keeping the old ones would let a
				 * code issued against a discarded authenticator still open the account. */
				await recoveryCodes.DeleteAllForAccountAsync(username, cancellationToken);

				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				var hashes = new List<string>(codes.Length);
				foreach (string code in codes)
				{
					hashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
				}
				var persisted = await recoveryCodes.PersistManyAsync(username, hashes, cancellationToken);
				if (!persisted.IsSuccess)
				{
					log.LogWarning("Recovery code persistence failed for '{User}': [{Code}] {Message}",
						username, persisted.ErrorCode, persisted.ErrorMessage);
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
		/// Enables two-factor once a code from the new authenticator verifies.
		/// </summary>
		public async Task<Outcome> ConfirmTwoFactorAsync(
			string username,
			bool codeVerified,
			CancellationToken cancellationToken = default)
		{
			if (!codeVerified)
			{
				return new Outcome(false, "That code is not valid.");
			}

			var enabled = await accounts.PersistTotpEnabledAsync(username, true, cancellationToken);
			if (!enabled.IsSuccess)
			{
				log.LogWarning("PersistTotpEnabledAsync failed for '{User}': [{Code}] {Message}",
					username, enabled.ErrorCode, enabled.ErrorMessage);
				return new Outcome(false, "Two-factor could not be enabled.");
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
		public async Task<TwoFactorSetup> RegenerateRecoveryCodesAsync(
			string username,
			CancellationToken cancellationToken = default)
		{
			var deleted = await recoveryCodes.DeleteAllForAccountAsync(username, cancellationToken);
			if (!deleted.IsSuccess)
			{
				// Not fatal on its own, but leaving the old codes valid alongside new ones would
				// quietly double the number of ways in.
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

			return new TwoFactorSetup(true, null, null, codes);
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
