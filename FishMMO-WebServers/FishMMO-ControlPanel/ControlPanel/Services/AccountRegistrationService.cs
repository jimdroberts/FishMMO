using System.Collections.Concurrent;
using System.Security.Cryptography;
using FishMMO.Auth.Implementation;
using FishMMO.ControlPanel.Controllers;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services;
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
	///   <item><description>Auto-verify when the development policy says so or the server verifies nothing; otherwise send a code on every channel <see cref="AccountVerificationRules.Effective"/> names, or verify without one when it names none.</description></item>
	///   <item><description>Generate a TOTP secret, encrypt it under the deployment master KEK, persist it, then enable TOTP.</description></item>
	///   <item><description>Generate recovery codes, hash them, persist them. (panel) The secret, the enable and the codes are one transaction — see <see cref="SelfServiceService.EnrolAsync"/>.</description></item>
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

		/// <summary>
		/// The one answer to every code that does not verify: a wrong one, an expired one, an unknown
		/// account, an account already verified, and the third wrong code that opens a ticket.
		/// </summary>
		/// <remarks>
		/// The verify endpoint is anonymous, so anything that differed between those cases would say
		/// which names exist, which are verified, and how many guesses an account has had. Saying up front
		/// what three wrong codes do costs nothing, because it is true of every account.
		/// </remarks>
		public const string InvalidCodeError =
			"That verification code is not valid. After three incorrect codes a support ticket is opened for the account, and staff will contact you by email.";

		/// <summary>The one refusal for any beta code problem. Passed through from the service's own wording.</summary>
		public const string InvalidBetaCodeError = "That beta code is not valid.";

		/// <summary>The one refusal for a registration that could not be created, whatever the reason.</summary>
		public const string CouldNotCreateError = "That account could not be created.";

		private readonly IAccountService accounts;
		private readonly IEmailQueueService emailQueue;
		private readonly ISmsQueueService smsQueue;
		private readonly IBetaCodeService betaCodes;
		private readonly ISupportTicketService supportTickets;
		private readonly PanelRegistrationOptions options;
		private readonly VerificationOptions verification;
		private readonly BetaOptions beta;
		private readonly VerificationResendThrottle throttle;
		private readonly SelfServiceService selfService;
		private readonly ILogger<AccountRegistrationService> log;

		public AccountRegistrationService(
			IAccountService accounts,
			IEmailQueueService emailQueue,
			ISmsQueueService smsQueue,
			IBetaCodeService betaCodes,
			ISupportTicketService supportTickets,
			PanelRegistrationOptions options,
			VerificationOptions verification,
			BetaOptions beta,
			VerificationResendThrottle throttle,
			SelfServiceService selfService,
			ILogger<AccountRegistrationService> log)
		{
			this.selfService = selfService;
			this.accounts = accounts;
			this.emailQueue = emailQueue;
			this.smsQueue = smsQueue;
			this.betaCodes = betaCodes;
			this.supportTickets = supportTickets;
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
		/// <param name="VerificationPending">Channels a code was sent on; any one of them verifies the account.</param>
		/// <param name="BetaWarning">Set when the account exists but the beta code could not be attached to it.</param>
		/// <param name="TwoFactorWarning">Set when the account exists but two-factor could not be set up on it.</param>
		public sealed record RegistrationResult(
			bool Ok,
			string Error,
			string Field,
			bool AutoVerified,
			AccountVerificationChannels VerificationPending,
			string OtpauthUri,
			IReadOnlyList<string> RecoveryCodes,
			bool BetaRedeemed,
			string BetaWarning,
			string TwoFactorWarning = null);

		/// <summary>What a new account is told when it was created without two-factor.</summary>
		public const string TwoFactorNotSetUpWarning =
			"Your account was created, but two-factor could not be set up on it. Sign in and set it up from My account.";

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
			// Lowercased here as well as by the browser, so no client can register a spelling the rest
			// of the server does not use. See SrpIdentity.
			string username = SrpIdentity.NormalizeIdentifier(input.Username);
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

			// ── Reachable: a server that verifies must be able to send this account a code ──
			/* Email is always given, so this refuses only when email verification is off and the player gave
			 * nothing an enabled channel can use (a Discord-only server and no Discord username). The game's
			 * account creation refuses the same case. An existing account in that position is let in at sign-in
			 * instead (AccountVerificationRules.IsWaived), because it cannot be asked; a new one need not be made so. */
			if (!options.AutoVerifyAccounts && !verification.VerifiesNothing &&
				AccountVerificationRules.Effective(profile.VerificationChannels, verification.Enabled,
					AccountVerificationRules.Receivable(email, profile.Phone, profile.DiscordUsername)) == AccountVerificationChannels.None)
			{
				return (verification.Enabled & AccountVerificationChannels.Discord) != 0
					? Refuse("This server verifies accounts by Discord: enter your Discord username.", "discordUsername")
					: Refuse("This server verifies accounts by SMS: enter your phone number.", "phone");
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
				if (!redeemable.IsSuccess && DatabaseReplies.IsFault(redeemable.ErrorCode))
				{
					/* The database did not answer, which says nothing about the code. Telling the player
					 * it is invalid sent them hunting for a typo; this refusal is the same for every code,
					 * real or guessed, so it tells a script nothing either. */
					log.LogWarning("Beta code check failed during registration: [{Code}] {Message}",
						redeemable.ErrorCode, redeemable.ErrorMessage);
					return Refuse("Your beta code could not be checked right now. Try again shortly.", "betaCode");
				}
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
				/* Which codes go out is the shared rule's answer, computed from what the database row
				 * will actually say — so a failed profile write counts as the row it left behind: email
				 * chosen, no phone, no Discord username. Computing it from the form instead would send
				 * an SMS or a Discord code the account then has no record of. */
				AccountVerificationChannels effective = profileResult.IsSuccess
					? AccountVerificationRules.Effective(
						profile.VerificationChannels,
						verification.Enabled,
						AccountVerificationRules.Receivable(email, profile.Phone, profile.DiscordUsername))
					: AccountVerificationRules.Effective(
						AccountVerificationChannels.Email,
						verification.Enabled,
						AccountVerificationRules.Receivable(email, null, null));

				if (effective == AccountVerificationChannels.None)
				{
					/* Verification is on, but no enabled channel can reach this account — say, SMS alone
					 * is switched on and no phone was given. No code could ever arrive, so the account is
					 * verified without one rather than created unable to sign in. */
					var waived = await accounts.PersistChannelsVerifiedAsync(username, AccountVerificationChannels.None, cancellationToken);
					if (!waived.IsSuccess)
					{
						log.LogWarning("PersistChannelsVerifiedAsync(None) failed for '{User}': [{Code}] {Message}",
							username, waived.ErrorCode, waived.ErrorMessage);
					}
					autoVerified = true;
				}
				else
				{
					pending = effective;
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
					if ((pending & AccountVerificationChannels.Discord) != 0)
					{
						await SendDiscordCodeAsync(username, cancellationToken);
					}
				}
			}

			RegistrationResult Done(string otpauthUri, IReadOnlyList<string> codes) =>
				new(true, null, null, autoVerified, pending, otpauthUri, codes, betaRedeemed, betaWarning);

			// ── Mandatory two-factor enrolment ──────────────────────────────────
			/* The account exists and can log in whatever happens here; failing the whole registration
			 * over two-factor would be worse, and this matches the in-game behaviour where a 2FA failure
			 * is caught and the account is kept.
			 *
			 * What changed is that the secret, the enable and the recovery codes are now one
			 * transaction (SelfServiceService.EnrolAsync), and a failure is TOLD to the player. The
			 * writes used to be separate and best-effort, and the handover screen was shown whatever
			 * happened: a player could scan an authenticator for an account whose two-factor had not
			 * been switched on, or write down ten recovery codes that were never stored. Now it is all
			 * or nothing, and "nothing" comes with a warning saying to set it up from My account. */
			var setup = await selfService.EnrolAsync(username, enable: true, cancellationToken);
			if (!setup.Ok)
			{
				log.LogError("'{User}' was created WITHOUT two-factor: {Error}", username, setup.Error);
				return Done(null, null) with { TwoFactorWarning = TwoFactorNotSetUpWarning };
			}

			return Done(setup.OtpauthUri, setup.RecoveryCodes);
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
			if ((input.VerificationChannels & AccountVerificationRules.All) == 0)
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
				("discordUsername", new AccountProfileData { DiscordUsername = input.DiscordUsername }),
			};
			foreach (var (field, probe) in probes)
			{
				if (!AccountProfileRules.TryValidate(probe, out _, out string fieldError))
				{
					return (null, fieldError, field);
				}
			}

			/* Every field is valid on its own, so what failed is a channel chosen without what it needs.
			 * A probe carrying only the Discord choice and the Discord username tells the two apart
			 * without restating either rule here. */
			var discordProbe = new AccountProfileData
			{
				DiscordUsername = input.DiscordUsername,
				VerificationChannels = input.VerificationChannels & AccountVerificationChannels.Discord,
			};
			if (!AccountProfileRules.TryValidate(discordProbe, out _, out _))
			{
				return (null, error, "discordUsername");
			}
			return (null, error, "verifySms");
		}

		/// <summary>
		/// Redeems a verification code from any channel.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>PersistVerifiedByCodeAsync</c> matches the code against every code the account holds —
		/// email, SMS and Discord — and checks the expiry, in one statement. Any one match verifies the
		/// account outright; there is no second code to ask for afterwards.
		/// </para>
		/// <para>
		/// Every wrong code is counted through <see cref="VerificationFailureTicket.RecordAsync"/>, in the
		/// database the login server counts into, and the third in a row opens a support ticket. What that
		/// did is logged and never returned: the answer is <see cref="InvalidCodeError"/> whatever
		/// happened, so the endpoint cannot be used to learn anything about an account.
		/// </para>
		/// <para>
		/// A wrong code, an unknown account and a verified one all come back from the service as the
		/// same VALIDATION_ERROR, so counting only that keeps every one of them identical. A database
		/// fault is not a wrong code: it used to be counted as one, so a correct code typed during an
		/// outage was told it was invalid and moved the account towards an automatic ticket. It is
		/// answered with "try again" instead — the same for every account, so it says nothing either.
		/// </para>
		/// </remarks>
		public async Task<(bool Ok, string Error, bool Retry)> VerifyAsync(
			string username,
			int code,
			CancellationToken cancellationToken = default)
		{
			username = SrpIdentity.NormalizeIdentifier(username);
			if (!Authentication.IsAllowedUsername(username))
			{
				// No account can have this name, so there is nothing to count against.
				return (false, InvalidCodeError, false);
			}

			var result = await accounts.PersistVerifiedByCodeAsync(username, code, cancellationToken);
			if (result.IsSuccess)
			{
				return (true, null, false);
			}
			if (result.ErrorCode != DatabaseErrorCodes.ValidationError)
			{
				log.LogWarning("Verification code for '{User}' could not be checked; not counted: [{Code}] {Message}",
					username, result.ErrorCode, result.ErrorMessage);
				return (false, "Your code could not be checked right now. Try again shortly.", true);
			}

			VerificationFailureTicket.Outcome outcome = await VerificationFailureTicket.RecordAsync(accounts, supportTickets, username, cancellationToken);
			if (outcome.TicketOpened)
			{
				log.LogWarning("Opened support ticket {Ticket} for '{User}' after {Failures} incorrect verification codes.",
					outcome.TicketId, username, outcome.Failures);
			}
			else if (outcome.TicketError != null)
			{
				log.LogWarning("A support ticket was due for '{User}' after {Failures} incorrect verification codes but could not be opened: {Error}",
					username, outcome.Failures, outcome.TicketError);
			}
			else if (outcome.CountError != null)
			{
				log.LogWarning("An incorrect verification code for '{User}' could not be counted: {Error}", username, outcome.CountError);
			}
			return (false, InvalidCodeError, false);
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
		/// <para>
		/// Email and SMS only. The Discord code is delivered once, as one DM, and is never replaced —
		/// a second code would strand the first DM, and the bot never sends another — so the endpoint
		/// refuses a Discord resend before it gets here.
		/// </para>
		/// </remarks>
		public async Task ResendAsync(string username, AccountVerificationChannels channel, CancellationToken cancellationToken = default)
		{
			username = SrpIdentity.NormalizeIdentifier(username);
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
			// Only a channel the shared rule still asks this account for; that already excludes a switched-off one.
			if ((AccountVerificationRules.Outstanding(data, verification.Enabled) & channel) == 0)
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

		/// <summary>Wire names for a set of channels: "email", "sms", "discord".</summary>
		public static string[] ChannelNames(AccountVerificationChannels channels)
		{
			var names = new List<string>(3);
			if ((channels & AccountVerificationChannels.Email) != 0) names.Add("email");
			if ((channels & AccountVerificationChannels.Sms) != 0) names.Add("sms");
			if ((channels & AccountVerificationChannels.Discord) != 0) names.Add("discord");
			return names.ToArray();
		}

		/// <summary>Parses a wire channel name, or None.</summary>
		public static AccountVerificationChannels ParseChannel(string name) => name?.Trim().ToLowerInvariant() switch
		{
			"email" => AccountVerificationChannels.Email,
			"sms" or "phone" => AccountVerificationChannels.Sms,
			"discord" => AccountVerificationChannels.Discord,
			_ => AccountVerificationChannels.None,
		};

		private async Task SendEmailCodeAsync(string username, string email, CancellationToken cancellationToken)
		{
			/* Checked BEFORE a code is stored. A waiting email carries the code the account holds now;
			 * storing a fresh one first and then skipping the email as a duplicate sent the player a
			 * code that no longer matched (issue #267). */
			var duplicate = await emailQueue.HasPendingForUserAsync(username, EmailKind.Verification, cancellationToken);
			if (duplicate.IsSuccess && duplicate.Data)
			{
				log.LogDebug("Skipping verification email for '{User}': one is already waiting, with the current code.", username);
				return;
			}

			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var codeResult = await accounts.PersistVerifyCodeAsync(username, verifyCode, DateTime.UtcNow + VerifyCodeLifetime, cancellationToken);
			if (!codeResult.IsSuccess)
			{
				log.LogWarning("PersistVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
					username, codeResult.ErrorCode, codeResult.ErrorMessage);
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

		/// <summary>
		/// Issues the account's one Discord code. The Discord bot is woken by the database and sends the DM.
		/// </summary>
		/// <remarks>
		/// Nothing is sent from here: the bot owns delivery, including waiting for a player who has not
		/// joined the Discord server yet. The database issues a code once per account and refuses a
		/// second, so a false result is not a failure to report — the one DM already carries a code.
		/// </remarks>
		private async Task SendDiscordCodeAsync(string username, CancellationToken cancellationToken)
		{
			int verifyCode = RandomNumberGenerator.GetInt32(100000, 1000000);
			var issued = await accounts.PersistDiscordVerifyCodeAsync(username, verifyCode, cancellationToken);
			if (!issued.IsSuccess)
			{
				log.LogWarning("PersistDiscordVerifyCodeAsync failed for '{User}': [{Code}] {Message}",
					username, issued.ErrorCode, issued.ErrorMessage);
			}
			else if (!issued.Data)
			{
				log.LogDebug("No Discord verification code issued for '{User}': one already exists.", username);
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
		/// every channel off reaches the same auto-verify path by a different, production-legal road.
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
