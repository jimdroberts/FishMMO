using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Creates accounts from the browser, performing exactly the same steps, in the same order,
	/// as the LoginServer's <c>AccountCreationSystem</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The two paths must not drift: an account created on the web and an account created in the
	/// game client have to be indistinguishable afterwards, or a player who registered on one and
	/// signed in on the other would find their 2FA missing, their verification never sent, or
	/// their salt and verifier shaped differently.
	/// </para>
	/// <para>
	/// The sequence mirrored from <c>AccountCreationSystem.ProcessAccountCreationAsync</c>:
	/// </para>
	/// <list type="number">
	///   <item><description>Validate the username against <see cref="Authentication.IsAllowedUsername"/>.</description></item>
	///   <item><description>Bound the salt and verifier lengths (256 / 1024).</description></item>
	///   <item><description>Persist the account with <c>IAccountService.PersistAsync</c>.</description></item>
	///   <item><description>Auto-verify when the development policy says so, and stop.</description></item>
	///   <item><description>Otherwise: generate a six-digit code with a 24-hour expiry and persist it.</description></item>
	///   <item><description>Enqueue the verification email, skipping a duplicate pending one.</description></item>
	///   <item><description>Generate a TOTP secret, encrypt it under the deployment master KEK, persist it, then enable TOTP.</description></item>
	///   <item><description>Generate recovery codes, hash them, persist them best-effort.</description></item>
	///   <item><description>Return the otpauth URI and the plaintext recovery codes once.</description></item>
	/// </list>
	/// <para>
	/// The differences from the in-game path are transport, not policy: there is no AES-GCM
	/// session to decrypt fields from and no FishNet broadcast to marshal a reply onto, because
	/// the browser speaks HTTPS and computes its own salt and verifier client-side.
	/// </para>
	/// </remarks>
	public sealed class AccountRegistrationService
	{
		/// <summary>Matches <c>AccountCreationSystem.MaxSaltLength</c>.</summary>
		private const int MaxSaltLength = 256;

		/// <summary>Matches <c>AccountCreationSystem.MaxVerifierLength</c>.</summary>
		private const int MaxVerifierLength = 1024;

		/// <summary>Matches the in-game 24-hour verification code lifetime.</summary>
		private static readonly TimeSpan VerifyCodeLifetime = TimeSpan.FromHours(24);

		private readonly IAccountService accounts;
		private readonly IEmailQueueService emailQueue;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly TotpKeyProvider totpKeys;
		private readonly PanelRegistrationOptions options;
		private readonly ILogger<AccountRegistrationService> log;

		public AccountRegistrationService(
			IAccountService accounts,
			IEmailQueueService emailQueue,
			ITwoFactorRecoveryCodeService recoveryCodes,
			TotpKeyProvider totpKeys,
			PanelRegistrationOptions options,
			ILogger<AccountRegistrationService> log)
		{
			this.accounts = accounts;
			this.emailQueue = emailQueue;
			this.recoveryCodes = recoveryCodes;
			this.totpKeys = totpKeys;
			this.options = options;
			this.log = log;
		}

		/// <summary>Outcome of a registration attempt.</summary>
		public sealed record RegistrationResult(
			bool Ok,
			string Error,
			bool AutoVerified,
			string OtpauthUri,
			IReadOnlyList<string> RecoveryCodes);

		/// <summary>
		/// Creates an account from a browser-computed salt and verifier.
		/// </summary>
		/// <remarks>
		/// The password is never a parameter here, and there is nowhere for it to arrive: the
		/// browser derives the salt and verifier with the same SRP rules the game client uses and
		/// sends only those, so this service could not log or leak a password if it tried.
		/// </remarks>
		public async Task<RegistrationResult> RegisterAsync(
			string username,
			string salt,
			string verifier,
			string email,
			int age,
			CancellationToken cancellationToken = default)
		{
			// ── Validation, matching the in-game order ──────────────────────────
			if (!Authentication.IsAllowedUsername(username))
			{
				return new RegistrationResult(false, Authentication.InvalidUsernameError, false, null, null);
			}

			if (string.IsNullOrWhiteSpace(email) || !Authentication.IsAllowedEmailUsername(email))
			{
				return new RegistrationResult(false, "A valid email address is required to register.", false, null, null);
			}

			// The client dropdown starts at 13 and the server stores whatever it is told, so the
			// bound is enforced here as well rather than trusted from the browser.
			if (age < 13 || age > 120)
			{
				return new RegistrationResult(false, "You must confirm your age to register.", false, null, null);
			}

			if (string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(verifier))
			{
				return new RegistrationResult(false, "Invalid credentials.", false, null, null);
			}

			if (salt.Length > MaxSaltLength || verifier.Length > MaxVerifierLength)
			{
				return new RegistrationResult(false, "Invalid credentials.", false, null, null);
			}

			if (!IsHex(salt) || !IsHex(verifier))
			{
				// The SRP layer produces lowercase hex on both sides; anything else is a client
				// that is not speaking the protocol.
				return new RegistrationResult(false, "Invalid credentials.", false, null, null);
			}

			// ── Persist ─────────────────────────────────────────────────────────
			DatabaseResult persistResult = await accounts.PersistAsync(username, salt, verifier, email, age, cancellationToken);
			if (!persistResult.IsSuccess)
			{
				// The in-game path maps a unique violation and a validation error to the same
				// answer the client sees for bad credentials, so registration cannot be used to
				// enumerate which names are taken.
				log.LogWarning("Account creation failed: [{Code}] {Message}", persistResult.ErrorCode, persistResult.ErrorMessage);
				return new RegistrationResult(false, "That account could not be created.", false, null, null);
			}

			// ── Auto-verify (development only) ──────────────────────────────────
			// This branch must NOT return: an auto-verified account still enrols in two-factor
			// below. Returning here once created development accounts with no TOTP secret and
			// no recovery codes, which then could not sign in to anything that requires them.
			bool autoVerified = options.AutoVerifyAccounts;
			if (autoVerified)
			{
				var autoVerify = await accounts.PersistAutoVerifiedAsync(username, cancellationToken);
				if (!autoVerify.IsSuccess)
				{
					log.LogWarning("PersistAutoVerifiedAsync failed for '{User}': [{Code}] {Message}",
						username, autoVerify.ErrorCode, autoVerify.ErrorMessage);
				}
			}
			else
			{
				// ── Verification code + email ───────────────────────────────────────
				int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
				DateTime verifyExpiresUtc = DateTime.UtcNow + VerifyCodeLifetime;
				var codeResult = await accounts.PersistVerifyCodeAsync(username, verifyCode, verifyExpiresUtc, cancellationToken);
				if (!codeResult.IsSuccess)
				{
					log.LogWarning("PersistVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
						username, codeResult.ErrorCode, codeResult.ErrorMessage);
				}

				var duplicate = await emailQueue.HasPendingForUserAsync(username, EmailKind.Verification, cancellationToken);
				if (duplicate.IsSuccess && duplicate.Data)
				{
					log.LogDebug("Skipping duplicate verification email for '{User}'.", username);
				}
				else
				{
					var enqueue = await emailQueue.EnqueueAsync(
						email, username, "FishMMO - Verify Your Account", BuildVerificationEmailBody(username, verifyCode),
						EmailKind.Verification, cancellationToken);
					if (!enqueue.IsSuccess)
					{
						log.LogWarning("Failed to enqueue verification email for '{User}': [{Code}] {Message}",
							username, enqueue.ErrorCode, enqueue.ErrorMessage);
					}
				}
			}

			// ── Mandatory two-factor enrolment ──────────────────────────────────
			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				// The account exists and can log in; it simply has no TOTP. Failing the whole
				// registration here would be worse, and this matches the in-game behaviour where
				// a 2FA failure is caught and the account is kept.
				log.LogError("TOTP master KEK unavailable — '{User}' was created WITHOUT two-factor.", username);
				return new RegistrationResult(true, null, autoVerified, null, null);
			}

			byte[] totpSecret = null;
			try
			{
				totpSecret = CryptoHelper.TwoFactor.GenerateTotpSecret();
				string encrypted = CryptoHelper.TwoFactor.EncryptTotpSecret(masterKek, username, totpSecret);

				var secretResult = await accounts.PersistTotpSecretAsync(username, encrypted, cancellationToken);
				if (!secretResult.IsSuccess)
				{
					// Never enable TOTP without a stored secret: an account with totp_enabled and
					// no secret is permanently locked out, because verification checks the secret
					// for emptiness and returns false.
					log.LogWarning("PersistTotpSecretAsync failed for '{User}': [{Code}] {Message}",
						username, secretResult.ErrorCode, secretResult.ErrorMessage);
					return new RegistrationResult(true, null, autoVerified, null, null);
				}

				var enableResult = await accounts.PersistTotpEnabledAsync(username, true, cancellationToken);
				if (!enableResult.IsSuccess)
				{
					log.LogWarning("PersistTotpEnabledAsync failed for '{User}': [{Code}] {Message}",
						username, enableResult.ErrorCode, enableResult.ErrorMessage);
				}

				// Recovery codes are best-effort, exactly as in-game: an authenticator that works
				// without recovery codes beats a registration that fails.
				string[] codes = CryptoHelper.TwoFactor.GenerateRecoveryCodes();
				var hashes = new List<string>(codes.Length);
				foreach (string code in codes)
				{
					hashes.Add(CryptoHelper.TwoFactor.HashRecoveryCode(username, code));
				}
				var recoveryResult = await recoveryCodes.PersistManyAsync(username, hashes, cancellationToken);
				if (!recoveryResult.IsSuccess)
				{
					log.LogWarning("Recovery code persistence failed for '{User}': [{Code}] {Message}",
						username, recoveryResult.ErrorCode, recoveryResult.ErrorMessage);
				}

				string otpauthUri = CryptoHelper.TwoFactor.BuildOtpauthUri(totpSecret, username);
				return new RegistrationResult(true, null, autoVerified, otpauthUri, codes);
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Two-factor enrolment failed for '{User}'; the account exists without it.", username);
				return new RegistrationResult(true, null, autoVerified, null, null);
			}
			finally
			{
				if (totpSecret != null)
				{
					CryptographicOperations.ZeroMemory(totpSecret);
				}
			}
		}

		/// <summary>
		/// Redeems a verification code.
		/// </summary>
		/// <remarks>
		/// <c>PersistVerifiedAsync</c> checks the code and the expiry atomically in the database,
		/// so there is no read-then-write window for a racing attempt to slip through.
		/// </remarks>
		public async Task<(bool Ok, string Error)> VerifyAsync(
			string username,
			int code,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(username))
			{
				return (false, "That verification code is not valid.");
			}

			var result = await accounts.PersistVerifiedAsync(username, code, cancellationToken);
			if (!result.IsSuccess)
			{
				// One message for a wrong code, an unknown account and an expired code alike.
				return (false, "That verification code is not valid.");
			}
			return (true, null);
		}

		private static bool IsHex(string value)
		{
			foreach (char c in value)
			{
				bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if (!ok) return false;
			}
			return true;
		}

		/// <summary>
		/// Builds the verification email body. Kept textually close to the LoginServer's
		/// <c>BuildVerificationEmailBody</c> so a player gets the same message either way.
		/// </summary>
		private static string BuildVerificationEmailBody(string username, int verifyCode)
		{
			return
				$"Hello {username},\n\n" +
				$"Thank you for creating a FishMMO account.\n\n" +
				$"Your verification code is: {verifyCode}\n\n" +
				$"Enter this code in the game client or on the web control panel to verify your account.\n" +
				$"This code expires in 24 hours.\n\n" +
				$"If you did not create this account, you can ignore this message.\n";
		}
	}

	/// <summary>
	/// Registration policy switches for the panel.
	/// </summary>
	public sealed class PanelRegistrationOptions
	{
		/// <summary>
		/// Whether new accounts skip email verification and two-factor enrolment.
		/// </summary>
		/// <remarks>
		/// The panel's counterpart to <c>AccountVerificationPolicy.IsAutoVerifyEnabled</c>. Like
		/// that one it must never be reachable in production: it is honoured only outside the
		/// Production environment, so a development configuration file that reaches a production
		/// host cannot re-enable the bypass.
		/// </remarks>
		public bool AutoVerifyAccounts { get; init; }

		/// <summary>Whether self-service registration is open at all.</summary>
		public bool RegistrationEnabled { get; init; } = true;

		/// <summary>
		/// Whether a password reset returns the emailed code in the response body.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A development affordance, and the same shape as <see cref="AutoVerifyAccounts"/>: with
		/// no mail sender running there is no way to exercise the recovery flow locally at all.
		/// </para>
		/// <para>
		/// It is honoured only outside the Production environment, and
		/// <see cref="PasswordResetService"/> checks the environment itself rather than trusting
		/// this flag alone — so a development configuration that reaches a production host cannot
		/// re-enable it. The default is on because the environment check, not this switch, is
		/// what keeps it out of production.
		/// </para>
		/// </remarks>
		public bool ExposeResetCodes { get; init; } = true;
	}
}
