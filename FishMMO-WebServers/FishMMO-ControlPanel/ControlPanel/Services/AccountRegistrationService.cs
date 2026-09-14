using System.Collections.Concurrent;
using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
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
	/// The sequence mirrored from <c>AccountCreationSystem.ProcessAccountCreationAsync</c>, with the
	/// panel's additions marked:
	/// </para>
	/// <list type="number">
	///   <item><description>Validate the username against <see cref="Authentication.IsAllowedUsername"/>.</description></item>
	///   <item><description>Bound the salt and verifier lengths (256 / 1024).</description></item>
	///   <item><description>(panel) Validate the optional profile with <see cref="AccountProfileRules.TryValidate"/>.</description></item>
	///   <item><description>(panel) With the beta gate on, or a code given, check the code is redeemable — BEFORE the account exists.</description></item>
	///   <item><description>Persist the account with <c>IAccountService.PersistAsync</c>.</description></item>
	///   <item><description>(panel) Persist the profile and chosen channels, then redeem the beta code.</description></item>
	///   <item><description>Auto-verify when the development policy says so or the server verifies nothing; otherwise mark switched-off channels proven, and send a code for each remaining chosen channel.</description></item>
	///   <item><description>Generate a TOTP secret, encrypt it under the deployment master KEK, persist it, then enable TOTP.</description></item>
	///   <item><description>Generate recovery codes, hash them, persist them best-effort.</description></item>
	///   <item><description>Return the otpauth URI and the plaintext recovery codes once.</description></item>
	/// </list>
	/// <para>
	/// Everything after the account row exists is best effort, as it is in game: a follow-on step
	/// that fails is logged, and the account is kept rather than a half-made registration being
	/// reported as a failure the player then retries into "that name is taken".
	/// </para>
	/// </remarks>
	public sealed class AccountRegistrationService
	{
		/// <summary>Matches <c>AccountCreationSystem.MaxSaltLength</c>.</summary>
		private const int MaxSaltLength = 256;

		/// <summary>Matches <c>AccountCreationSystem.MaxVerifierLength</c>.</summary>
		private const int MaxVerifierLength = 1024;

		/// <summary>Matches the in-game 24-hour verification code lifetime. Email and SMS codes alike.</summary>
		private static readonly TimeSpan VerifyCodeLifetime = TimeSpan.FromHours(24);

		/// <summary>The one refusal for any beta code problem. Passed through from the service's own wording.</summary>
		public const string InvalidBetaCodeError = "That beta code is not valid.";

		/// <summary>The one refusal for a registration that could not be created, whatever the reason.</summary>
		public const string CouldNotCreateError = "That account could not be created.";

		private readonly IAccountService accounts;
		private readonly IEmailQueueService emailQueue;
		private readonly ISmsQueueService smsQueue;
		private readonly IBetaCodeService betaCodes;
		private readonly ITwoFactorRecoveryCodeService recoveryCodes;
		private readonly TotpKeyProvider totpKeys;
		private readonly PanelRegistrationOptions options;
		private readonly VerificationOptions verification;
		private readonly BetaOptions beta;
		private readonly VerificationResendThrottle throttle;
		private readonly ILogger<AccountRegistrationService> log;

		public AccountRegistrationService(
			IAccountService accounts,
			IEmailQueueService emailQueue,
			ISmsQueueService smsQueue,
			IBetaCodeService betaCodes,
			ITwoFactorRecoveryCodeService recoveryCodes,
			TotpKeyProvider totpKeys,
			PanelRegistrationOptions options,
			VerificationOptions verification,
			BetaOptions beta,
			VerificationResendThrottle throttle,
			ILogger<AccountRegistrationService> log)
		{
			this.accounts = accounts;
			this.emailQueue = emailQueue;
			this.smsQueue = smsQueue;
			this.betaCodes = betaCodes;
			this.recoveryCodes = recoveryCodes;
			this.totpKeys = totpKeys;
			this.options = options;
			this.verification = verification;
			this.beta = beta;
			this.throttle = throttle;
			this.log = log;
		}

		/// <summary>What a registration carries. No password, anywhere.</summary>
		public sealed record RegistrationInput(
			string Username,
			string Salt,
			string Verifier,
			string Email,
			int Age,
			AccountProfileData Profile,
			string BetaCode);

		/// <summary>Outcome of a registration attempt.</summary>
		/// <param name="Field">The form field an error belongs to, when it belongs to one.</param>
		/// <param name="VerificationPending">Channels a code was sent for and still has to be entered.</param>
		/// <param name="BetaWarning">Set when the account exists but the beta code could not be attached to it.</param>
		public sealed record RegistrationResult(
			bool Ok,
			string Error,
			string Field,
			bool AutoVerified,
			AccountVerificationChannels VerificationPending,
			string OtpauthUri,
			IReadOnlyList<string> RecoveryCodes,
			bool BetaRedeemed,
			string BetaWarning);

		private static RegistrationResult Refuse(string error, string field = null) =>
			new(false, error, field, false, AccountVerificationChannels.None, null, null, false, null);

		/// <summary>
		/// Creates an account from a browser-computed salt and verifier.
		/// </summary>
		/// <remarks>
		/// The password is never a parameter here, and there is nowhere for it to arrive: the
		/// browser derives the salt and verifier with the same SRP rules the game client uses and
		/// sends only those, so this service could not log or leak a password if it tried.
		/// </remarks>
		public async Task<RegistrationResult> RegisterAsync(RegistrationInput input, CancellationToken cancellationToken = default)
		{
			string username = input.Username;
			string email = input.Email;

			// ── Validation, matching the in-game order ──────────────────────────
			if (!Authentication.IsAllowedUsername(username))
			{
				return Refuse(Authentication.InvalidUsernameError, "username");
			}

			if (string.IsNullOrWhiteSpace(email) || !Authentication.IsAllowedEmailUsername(email))
			{
				return Refuse("A valid email address is required to register.", "email");
			}

			// The client dropdown starts at 13 and the server stores whatever it is told, so the
			// bound is enforced here as well rather than trusted from the browser.
			if (input.Age < 13 || input.Age > 120)
			{
				return Refuse("You must confirm your age to register.", "age");
			}

			if (string.IsNullOrWhiteSpace(input.Salt) || string.IsNullOrWhiteSpace(input.Verifier) ||
				input.Salt.Length > MaxSaltLength || input.Verifier.Length > MaxVerifierLength ||
				!IsHex(input.Salt) || !IsHex(input.Verifier))
			{
				// The SRP layer produces lowercase hex on both sides; anything else is a client
				// that is not speaking the protocol.
				return Refuse("Invalid credentials.");
			}

			// ── Profile (optional fields), validated by the one shared rule set ──
			var (profile, profileError, profileField) = ValidateProfile(input.Profile);
			if (profileError != null)
			{
				return Refuse(profileError, profileField);
			}

			// ── Beta gate: refuse a bad code BEFORE the account exists ──────────
			/* Redeeming first would attach a use to a name the insert may then find taken, handing
			 * beta access to whoever owns it. Every failure — unknown, revoked, expired, used up,
			 * malformed, or a code of a program the gate does not admit — answers with one message,
			 * or the endpoint would tell a script a real code from a guess. */
			string betaCode = string.IsNullOrWhiteSpace(input.BetaCode) ? null : input.BetaCode.Trim();
			if (beta.Enabled && betaCode == null)
			{
				return Refuse("A beta code is required to register.", "betaCode");
			}
			if (betaCode != null)
			{
				IReadOnlyCollection<string> programs = beta.Enabled ? beta.Programs : Array.Empty<string>();
				var redeemable = await betaCodes.CheckRedeemableAsync(betaCode, programs, cancellationToken);
				if (!redeemable.IsSuccess || !redeemable.Data)
				{
					return Refuse(InvalidBetaCodeError, "betaCode");
				}
			}

			// ── Persist ─────────────────────────────────────────────────────────
			DatabaseResult persistResult = await accounts.PersistAsync(username, input.Salt, input.Verifier, email, input.Age, cancellationToken);
			if (!persistResult.IsSuccess)
			{
				// The in-game path maps a unique violation and a validation error to the same
				// answer the client sees for bad credentials, so registration cannot be used to
				// enumerate which names are taken.
				log.LogWarning("Account creation failed: [{Code}] {Message}", persistResult.ErrorCode, persistResult.ErrorMessage);
				return Refuse(CouldNotCreateError);
			}

			// ── Profile and chosen channels, immediately after the row exists ───
			var profileResult = await accounts.PersistProfileAsync(username, profile, cancellationToken);
			if (!profileResult.IsSuccess)
			{
				log.LogWarning("PersistProfileAsync failed for '{User}': [{Code}] {Message}",
					username, profileResult.ErrorCode, profileResult.ErrorMessage);
			}

			// ── Beta redemption ─────────────────────────────────────────────────
			bool betaRedeemed = false;
			string betaWarning = null;
			if (betaCode != null)
			{
				var redeemed = await betaCodes.RedeemAsync(username, betaCode, cancellationToken);
				if (redeemed.IsSuccess)
				{
					betaRedeemed = true;
				}
				else
				{
					/* The check passed a moment ago and the redeem is the authority: the code's last
					 * use went to somebody else in between. The account exists and is kept; saying so
					 * and pointing at the self-service redeem is better than failing a registration
					 * the player would then retry into "that name is taken". */
					log.LogWarning("Beta code redemption lost a race for new account '{User}': [{Code}] {Message}",
						username, redeemed.ErrorCode, redeemed.ErrorMessage);
					betaWarning = "Your account was created, but the beta code could not be attached to it — it may have just been used up. " +
								  "Sign in and redeem a code from My account.";
				}
			}

			// ── Verification ────────────────────────────────────────────────────
			AccountVerificationChannels chosen = profile.VerificationChannels;
			AccountVerificationChannels pending = AccountVerificationChannels.None;

			/* One auto-verify mechanism, reached two ways: the development bypass, or a server that
			 * verifies no channel at all. This branch must NOT return: an auto-verified account still
			 * enrols in two-factor below. Returning here once created development accounts with no TOTP
			 * secret and no recovery codes, which then could not sign in to anything that requires them. */
			bool autoVerified = options.AutoVerifyAccounts || verification.VerifiesNothing;
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
				AccountVerificationChannels switchedOff = chosen & ~verification.Enabled;
				if (switchedOff != AccountVerificationChannels.None)
				{
					var marked = await accounts.PersistChannelsVerifiedAsync(username, switchedOff, cancellationToken);
					if (!marked.IsSuccess)
					{
						log.LogWarning("PersistChannelsVerifiedAsync({Channels}) failed for '{User}': [{Code}] {Message}",
							switchedOff, username, marked.ErrorCode, marked.ErrorMessage);
					}
				}

				pending = chosen & verification.Enabled;
				if ((pending & AccountVerificationChannels.Email) != 0)
				{
					await SendEmailCodeAsync(username, email, cancellationToken);
					throttle.TryAcquire(username, AccountVerificationChannels.Email);
				}
				if ((pending & AccountVerificationChannels.Sms) != 0)
				{
					await SendPhoneCodeAsync(username, profile.Phone, cancellationToken);
					throttle.TryAcquire(username, AccountVerificationChannels.Sms);
				}

				// Every chosen channel was switched off: the recompute above verified the account.
				autoVerified = pending == AccountVerificationChannels.None;
			}

			RegistrationResult Done(string otpauthUri, IReadOnlyList<string> codes) =>
				new(true, null, null, autoVerified, pending, otpauthUri, codes, betaRedeemed, betaWarning);

			// ── Mandatory two-factor enrolment ──────────────────────────────────
			byte[] masterKek = totpKeys.MasterKek;
			if (masterKek == null || masterKek.Length != TotpMasterKek.KeyLength)
			{
				// The account exists and can log in; it simply has no TOTP. Failing the whole
				// registration here would be worse, and this matches the in-game behaviour where
				// a 2FA failure is caught and the account is kept.
				log.LogError("TOTP master KEK unavailable — '{User}' was created WITHOUT two-factor.", username);
				return Done(null, null);
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
					return Done(null, null);
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

				return Done(CryptoHelper.TwoFactor.BuildOtpauthUri(totpSecret, username), codes);
			}
			catch (Exception ex)
			{
				log.LogError(ex, "Two-factor enrolment failed for '{User}'; the account exists without it.", username);
				return Done(null, null);
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
		/// Validates the optional profile, naming the field an error belongs to.
		/// </summary>
		/// <remarks>
		/// <see cref="AccountProfileRules.TryValidate"/> returns one message and no field. The message
		/// is the rule's own and must stay so; the field is found by re-running the same rules over
		/// each field on its own, so the form can put the message beside the right input without a
		/// second copy of any rule.
		/// </remarks>
		private static (AccountProfileData Clean, string Error, string Field) ValidateProfile(AccountProfileData input)
		{
			input ??= new AccountProfileData();
			// Choosing nothing means email, the channel every account has always had.
			if ((input.VerificationChannels & (AccountVerificationChannels.Email | AccountVerificationChannels.Sms)) == 0)
			{
				input.VerificationChannels = AccountVerificationChannels.Email;
			}

			if (AccountProfileRules.TryValidate(input, out AccountProfileData clean, out string error))
			{
				return (clean, null, null);
			}

			var probes = new (string Field, AccountProfileData Probe)[]
			{
				("phone", new AccountProfileData { Phone = input.Phone }),
				("realName", new AccountProfileData { RealName = input.RealName }),
				("country", new AccountProfileData { Country = input.Country }),
				("address", new AccountProfileData { Address = input.Address }),
				("referralAccount", new AccountProfileData { ReferralAccount = input.ReferralAccount }),
			};
			foreach (var (field, probe) in probes)
			{
				if (!AccountProfileRules.TryValidate(probe, out _, out string fieldError))
				{
					return (null, fieldError, field);
				}
			}
			return (null, error, "verifySms");
		}

		/// <summary>
		/// Redeems an emailed verification code.
		/// </summary>
		/// <remarks>
		/// <c>PersistVerifiedAsync</c> checks the code and the expiry atomically in the database, proves
		/// the email channel, and sets <c>verified</c> only once every chosen channel is proven.
		/// </remarks>
		/// <returns>Whether it worked, and the channels still outstanding afterwards.</returns>
		public async Task<(bool Ok, string Error, AccountVerificationChannels Outstanding)> VerifyAsync(
			string username,
			int code,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(username))
			{
				return (false, "That verification code is not valid.", AccountVerificationChannels.None);
			}

			var result = await accounts.PersistVerifiedAsync(username, code, cancellationToken);
			if (!result.IsSuccess)
			{
				// One message for a wrong code, an unknown account and an expired code alike.
				return (false, "That verification code is not valid.", AccountVerificationChannels.None);
			}
			return (true, null, await OutstandingAfterAsync(username, cancellationToken));
		}

		/// <summary>Redeems a texted verification code. The phone twin of <see cref="VerifyAsync"/>.</summary>
		public async Task<(bool Ok, string Error, AccountVerificationChannels Outstanding)> VerifyPhoneAsync(
			string username,
			int code,
			CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(username))
			{
				return (false, "That verification code is not valid.", AccountVerificationChannels.None);
			}

			var result = await accounts.PersistPhoneVerifiedAsync(username, code, cancellationToken);
			if (!result.IsSuccess)
			{
				return (false, "That verification code is not valid.", AccountVerificationChannels.None);
			}
			return (true, null, await OutstandingAfterAsync(username, cancellationToken));
		}

		/// <summary>
		/// Sends a fresh code for one channel, when the account is owed one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Returns nothing, deliberately. The endpoint is anonymous and answers identically whatever
		/// happens here — unknown account, verified account, a channel it never chose, a cooldown still
		/// running — or it would say which names exist and what they chose.
		/// </para>
		/// <para>
		/// A code still waiting in the queue is not replaced: generating a new one would invalidate the
		/// code in the message that is about to arrive.
		/// </para>
		/// </remarks>
		public async Task ResendAsync(string username, AccountVerificationChannels channel, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(username) ||
				(channel != AccountVerificationChannels.Email && channel != AccountVerificationChannels.Sms))
			{
				return;
			}
			if (!throttle.TryAcquire(username, channel))
			{
				log.LogDebug("Suppressed a {Channel} verification resend for '{User}': inside the cooldown.", channel, username);
				return;
			}

			var account = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (!account.IsSuccess || account.Data.Verified)
			{
				return;
			}
			var data = account.Data;
			AccountVerificationChannels outstanding = Outstanding(data);
			if ((outstanding & channel) == 0 || (verification.Enabled & channel) == 0)
			{
				return;
			}

			if (channel == AccountVerificationChannels.Email)
			{
				if (string.IsNullOrWhiteSpace(data.Email))
				{
					return;
				}
				var queued = await emailQueue.HasPendingForUserAsync(data.Name, EmailKind.Verification, cancellationToken);
				if (!queued.IsSuccess || queued.Data)
				{
					return;
				}
				await SendEmailCodeAsync(data.Name, data.Email, cancellationToken);
			}
			else
			{
				if (string.IsNullOrWhiteSpace(data.Phone))
				{
					return;
				}
				var queued = await smsQueue.HasPendingForUserAsync(data.Name, SmsKind.Verification, cancellationToken);
				if (!queued.IsSuccess || queued.Data)
				{
					return;
				}
				await SendPhoneCodeAsync(data.Name, data.Phone, cancellationToken);
			}
		}

		/// <summary>
		/// The channels an unverified account still has to prove.
		/// </summary>
		/// <remarks>
		/// An account written before channels existed has chosen nothing, which means email. An
		/// account whose flag says unverified but whose chosen channels all read proven is asked for
		/// what it chose rather than told nothing, so the player is never shown a blank requirement.
		/// </remarks>
		public static AccountVerificationChannels Outstanding(FishMMO.Database.Data.AccountData data)
		{
			if (data.Verified)
			{
				return AccountVerificationChannels.None;
			}
			var chosen = (AccountVerificationChannels)data.VerificationChannels &
						 (AccountVerificationChannels.Email | AccountVerificationChannels.Sms);
			if (chosen == AccountVerificationChannels.None)
			{
				chosen = AccountVerificationChannels.Email;
			}
			var outstanding = AccountVerificationChannels.None;
			if ((chosen & AccountVerificationChannels.Email) != 0 && !data.EmailVerified) outstanding |= AccountVerificationChannels.Email;
			if ((chosen & AccountVerificationChannels.Sms) != 0 && !data.PhoneVerified) outstanding |= AccountVerificationChannels.Sms;
			return outstanding == AccountVerificationChannels.None ? chosen : outstanding;
		}

		/// <summary>Wire names for a set of channels: "email", "sms".</summary>
		public static string[] ChannelNames(AccountVerificationChannels channels)
		{
			var names = new List<string>(2);
			if ((channels & AccountVerificationChannels.Email) != 0) names.Add("email");
			if ((channels & AccountVerificationChannels.Sms) != 0) names.Add("sms");
			return names.ToArray();
		}

		/// <summary>Parses a wire channel name, or None.</summary>
		public static AccountVerificationChannels ParseChannel(string name) => name?.Trim().ToLowerInvariant() switch
		{
			"email" => AccountVerificationChannels.Email,
			"sms" or "phone" => AccountVerificationChannels.Sms,
			_ => AccountVerificationChannels.None,
		};

		private async Task<AccountVerificationChannels> OutstandingAfterAsync(string username, CancellationToken cancellationToken)
		{
			var account = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			return account.IsSuccess ? Outstanding(account.Data) : AccountVerificationChannels.None;
		}

		private async Task SendEmailCodeAsync(string username, string email, CancellationToken cancellationToken)
		{
			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var codeResult = await accounts.PersistVerifyCodeAsync(username, verifyCode, DateTime.UtcNow + VerifyCodeLifetime, cancellationToken);
			if (!codeResult.IsSuccess)
			{
				log.LogWarning("PersistVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
					username, codeResult.ErrorCode, codeResult.ErrorMessage);
				return;
			}

			var duplicate = await emailQueue.HasPendingForUserAsync(username, EmailKind.Verification, cancellationToken);
			if (duplicate.IsSuccess && duplicate.Data)
			{
				log.LogDebug("Skipping duplicate verification email for '{User}'.", username);
				return;
			}

			var enqueue = await emailQueue.EnqueueAsync(
				email, username, "FishMMO - Verify Your Account", BuildVerificationEmailBody(username, verifyCode),
				EmailKind.Verification, cancellationToken);
			if (!enqueue.IsSuccess)
			{
				log.LogWarning("Failed to enqueue verification email for '{User}': [{Code}] {Message}",
					username, enqueue.ErrorCode, enqueue.ErrorMessage);
			}
		}

		private async Task SendPhoneCodeAsync(string username, string phone, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(phone))
			{
				return;
			}

			// Same shape and lifetime as the emailed code, so one field and one hint serve both.
			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var codeResult = await accounts.PersistPhoneVerifyCodeAsync(username, verifyCode, DateTime.UtcNow + VerifyCodeLifetime, cancellationToken);
			if (!codeResult.IsSuccess)
			{
				log.LogWarning("PersistPhoneVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
					username, codeResult.ErrorCode, codeResult.ErrorMessage);
				return;
			}

			var enqueue = await smsQueue.EnqueueAsync(
				phone, username,
				$"FishMMO: your verification code is {verifyCode}. It expires in 24 hours. If you did not create an account, ignore this.",
				SmsKind.Verification, cancellationToken);
			if (!enqueue.IsSuccess)
			{
				log.LogWarning("Failed to enqueue verification SMS for '{User}': [{Code}] {Message}",
					username, enqueue.ErrorCode, enqueue.ErrorMessage);
			}
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
	/// The per-account cooldown on verification code resends.
	/// </summary>
	/// <remarks>
	/// The resend endpoint's rate limit is per IP; this one is per account and channel, so the endpoint
	/// cannot be driven from a spread of addresses to flood one person's inbox or phone. In memory,
	/// because a restart forgetting it costs at most one extra message.
	/// </remarks>
	public sealed class VerificationResendThrottle
	{
		/// <summary>How long after one code another may be sent for the same account and channel.</summary>
		public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

		private const int MaxEntries = 50_000;
		private readonly ConcurrentDictionary<string, DateTime> lastSent = new();

		/// <summary>True, and the cooldown starts, when a message may be sent now.</summary>
		public bool TryAcquire(string username, AccountVerificationChannels channel)
		{
			DateTime now = DateTime.UtcNow;
			if (lastSent.Count > MaxEntries)
			{
				foreach (var pair in lastSent)
				{
					if (now - pair.Value > Cooldown) lastSent.TryRemove(pair.Key, out _);
				}
			}

			string key = $"{username.ToLowerInvariant()}|{(int)channel}";
			while (true)
			{
				if (lastSent.TryGetValue(key, out DateTime previous))
				{
					if (now - previous < Cooldown) return false;
					if (lastSent.TryUpdate(key, now, previous)) return true;
				}
				else if (lastSent.TryAdd(key, now))
				{
					return true;
				}
			}
		}
	}

	/// <summary>
	/// Registration policy switches for the panel.
	/// </summary>
	public sealed class PanelRegistrationOptions
	{
		/// <summary>
		/// Whether new accounts skip verification.
		/// </summary>
		/// <remarks>
		/// The panel's counterpart to <c>AccountVerificationPolicy.IsAutoVerifyEnabled</c>. Like
		/// that one it must never be reachable in production: it is honoured only outside the
		/// Production environment, so a development configuration file that reaches a production
		/// host cannot re-enable the bypass. A server whose <see cref="VerificationOptions"/> turn
		/// both channels off reaches the same auto-verify path by a different, production-legal road.
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
