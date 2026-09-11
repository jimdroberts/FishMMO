using System.Security.Cryptography;
using System.Text;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// "Forgot password" recovery: the path for an account holder who cannot sign in at all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The server never sees a password here either. The browser generates a new SRP salt and
	/// derives a new verifier from the username and the password the user typed, and sends only
	/// those; <see cref="SelfServiceService.ChangePasswordAsync"/> — the one credential write in
	/// the panel — persists them. There is deliberately no second path that writes a verifier.
	/// </para>
	/// <para>
	/// <b>A reset does not satisfy or weaken two-factor.</b> Nothing here touches
	/// <c>totp_enabled</c>, <c>totp_secret</c>, <c>totp_verified_at</c> or the recovery codes,
	/// and nothing may be added that does. After a reset the account holder still has to pass
	/// TOTP or burn a recovery code at sign-in. That is the whole point of the split: the eight
	/// recovery codes are the answer to a lost authenticator and email is the answer to a lost
	/// password. If an emailed link could also clear the second factor, compromising a mailbox
	/// would be a complete account takeover and the second factor would protect nothing.
	/// </para>
	/// <para>
	/// The token is a secret. At least 32 bytes of <see cref="RandomNumberGenerator"/> output go
	/// to the mailbox; only its SHA-256 is stored, so a database read yields no working tokens.
	/// A plain hash rather than a slow KDF is right because the token is full-entropy random and
	/// not guessable — the hash exists to stop a leaked table being replayed, not to resist a
	/// dictionary.
	/// </para>
	/// <para>
	/// No account enumeration: <see cref="RequestAsync"/> reports nothing about whether the
	/// address is registered, whether the account is banned, or whether the resend cooldown
	/// suppressed the mail. The caller answers the same way regardless.
	/// </para>
	/// </remarks>
	public sealed class PasswordResetService
	{
		/// <summary>How long a reset token stays redeemable.</summary>
		private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(60);

		/// <summary>
		/// How long after issuing one token the account must wait for another to be mailed.
		/// </summary>
		/// <remarks>
		/// The endpoint's own rate limit is per IP; this one is per account, so the endpoint
		/// cannot be used from a spread of addresses to flood one person's inbox. It changes what
		/// is sent, never what is answered.
		/// </remarks>
		private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(2);

		/// <summary>Matches <c>AccountCreationSystem.MaxSaltLength</c>.</summary>
		private const int MaxSaltLength = 256;

		/// <summary>Matches <c>AccountCreationSystem.MaxVerifierLength</c>.</summary>
		private const int MaxVerifierLength = 1024;

		/// <summary>
		/// Upper bound on a submitted code, so an unbounded body is not hashed. A real token is
		/// 43 characters.
		/// </summary>
		private const int MaxCodeLength = 128;

		/// <summary>
		/// The one answer for an unknown, malformed, expired or already-used code.
		/// </summary>
		/// <remarks>
		/// Distinguishing them would tell an attacker which of their guesses was structurally
		/// right, so every failure in this file uses this exact string.
		/// </remarks>
		public const string InvalidCodeError = "That reset code is not valid or has expired.";

		private readonly IAccountService accounts;
		private readonly IPasswordResetTokenService resetTokens;
		private readonly IEmailQueueService emailQueue;
		private readonly IWebSessionService webSessions;
		private readonly SelfServiceService selfService;
		private readonly PanelRegistrationOptions options;
		private readonly IWebHostEnvironment environment;
		private readonly ILogger<PasswordResetService> log;

		public PasswordResetService(
			IAccountService accounts,
			IPasswordResetTokenService resetTokens,
			IEmailQueueService emailQueue,
			IWebSessionService webSessions,
			SelfServiceService selfService,
			PanelRegistrationOptions options,
			IWebHostEnvironment environment,
			ILogger<PasswordResetService> log)
		{
			this.accounts = accounts;
			this.resetTokens = resetTokens;
			this.emailQueue = emailQueue;
			this.webSessions = webSessions;
			this.selfService = selfService;
			this.options = options;
			this.environment = environment;
			this.log = log;
		}

		/// <summary>Outcome of an operation that either worked or did not.</summary>
		public sealed record Outcome(bool Ok, string Error);

		/// <summary>
		/// What a reset request produced. <see cref="DevelopmentCode"/> is null everywhere except
		/// a non-production host that has the affordance switched on.
		/// </summary>
		public sealed record RequestOutcome(string DevelopmentCode);

		/// <summary>
		/// Whether the emailed code may be echoed in the response.
		/// </summary>
		/// <remarks>
		/// Without a mail sender running, nobody can exercise this flow locally. Gated exactly the
		/// way <see cref="AccountRegistrationService"/> gates auto-verification, and then gated a
		/// second time here on the host's own environment: a development configuration file that
		/// reaches a production host cannot re-enable it, because Production fails this check no
		/// matter what the configuration says.
		/// </remarks>
		private bool MayExposeCode => !environment.IsProduction() && options.ExposeResetCodes;

		/// <summary>
		/// Issues a reset token for the account at an email address and queues the mail.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every refusal returns the same empty outcome, and the caller answers identically in
		/// all of them: an unknown address, a malformed one, a banned account, a cooldown still
		/// running, or a database that is down. An endpoint that answered differently for a
		/// registered address would be an account enumeration oracle, which is exactly what a
		/// password reset form is probed for.
		/// </para>
		/// <para>
		/// Banned accounts are never mailed. A reset cannot help them — the access level, not the
		/// password, is what refuses the login — and sending would make the endpoint an abuse
		/// vector against whoever owns the address. <c>FetchForLoginAsync</c> already refuses a
		/// banned account, and refuses it with the same failure as a missing one.
		/// </para>
		/// </remarks>
		public async Task<RequestOutcome> RequestAsync(
			string email,
			string requestedIp,
			CancellationToken cancellationToken = default)
		{
			var nothing = new RequestOutcome(null);

			if (string.IsNullOrWhiteSpace(email) || !Authentication.IsAllowedEmailUsername(email))
			{
				return nothing;
			}

			// Refuses unknown and banned alike, with the same failure for both.
			var account = await accounts.FetchForLoginAsync(email.Trim(), true, cancellationToken);
			if (!account.IsSuccess)
			{
				log.LogDebug("Password reset requested for an address with no eligible account.");
				return nothing;
			}

			string username = account.Data.Name;
			string storedEmail = account.Data.Email;
			if (string.IsNullOrWhiteSpace(storedEmail))
			{
				// Matched by email, so this cannot normally happen; mailing a value the account
				// does not hold would be worse than doing nothing.
				return nothing;
			}

			var recent = await resetTokens.HasRecentForAccountAsync(
				username, DateTime.UtcNow - ResendCooldown, cancellationToken);
			if (recent.IsSuccess && recent.Data)
			{
				log.LogDebug("Suppressed a reset email for '{User}': inside the resend cooldown.", username);
				return nothing;
			}
			if (!recent.IsSuccess)
			{
				// Cannot prove the cooldown is clear, so do not send. Failing open here would
				// turn a database blip into an inbox flood.
				log.LogWarning("Reset cooldown check failed for '{User}': [{Code}] {Message}",
					username, recent.ErrorCode, recent.ErrorMessage);
				return nothing;
			}

			(string token, string tokenHash) = GenerateToken();

			var issued = await resetTokens.IssueAsync(
				tokenHash, username, DateTime.UtcNow + TokenLifetime, Truncate(requestedIp, 64), cancellationToken);
			if (!issued.IsSuccess)
			{
				log.LogError("Could not issue a password reset token for '{User}': [{Code}] {Message}",
					username, issued.ErrorCode, issued.ErrorMessage);
				return nothing;
			}

			/* EmailKind.PasswordReset is load-bearing, not labelling. The drain stamps
			 * verification_email_sent_at after delivering a VERIFICATION email, which ends the
			 * grace period an unverified account signs in under. The account being mailed here
			 * may well be unverified — registering, never typing the code and then forgetting the
			 * password is an ordinary sequence — and stamping it would lock that player out of
			 * the game and the panel the moment they recovered their password. */
			var enqueue = await emailQueue.EnqueueAsync(
				storedEmail, username, "FishMMO - Reset Your Password",
				BuildResetEmailBody(username, token),
				EmailKind.PasswordReset, cancellationToken);
			if (!enqueue.IsSuccess)
			{
				log.LogWarning("Failed to enqueue the password reset email for '{User}': [{Code}] {Message}",
					username, enqueue.ErrorCode, enqueue.ErrorMessage);
			}

			// The token is never logged. It exists in the mail, in the browser that asked, and —
			// outside Production only — in this response.
			return new RequestOutcome(MayExposeCode ? token : null);
		}

		/// <summary>
		/// Returns the account name a valid, unexpired, unused token belongs to, or null.
		/// </summary>
		/// <remarks>
		/// The browser needs the username before it can derive a verifier, because the username
		/// is an input to the SRP key derivation. Handing it to somebody holding the token gives
		/// away nothing: the token went to that account's mailbox, and the mail names the account
		/// already. This is a read — it does not spend the token, which only
		/// <see cref="CompleteAsync"/> does.
		/// </remarks>
		public async Task<string> LookupUsernameAsync(
			string code,
			CancellationToken cancellationToken = default)
		{
			if (!TryHashCode(code, out string tokenHash, out byte[] digest))
			{
				return null;
			}

			var fetched = await resetTokens.FetchByHashAsync(tokenHash, cancellationToken);
			if (!fetched.IsSuccess)
			{
				return null;
			}

			var row = fetched.Data;

			/* The row was found by an indexed equality match, which is not a constant-time
			 * comparison and is at the mercy of the column's collation. Re-comparing the stored
			 * hash against the computed one in constant time is what actually decides it. */
			if (!HashesMatch(row.TokenHash, digest))
			{
				return null;
			}

			if (row.UsedUtc != null || row.ExpiresUtc <= DateTime.UtcNow)
			{
				return null;
			}

			return row.AccountName;
		}

		/// <summary>
		/// Spends a reset token and installs the browser-derived credentials.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Order matters and is deliberate. The token is spent first, by one conditional UPDATE
		/// in the database, so a double submission cannot write twice; a credential write that
		/// then fails costs the account holder a second email, which is the cheap direction to
		/// fail in. Afterwards every panel session and every game token for the account is
		/// revoked and any other outstanding reset token is invalidated.
		/// </para>
		/// <para>
		/// The revocations are the point rather than housekeeping. A reset is what somebody does
		/// when they believe their account is compromised, so a session left alive would defeat
		/// it, and a reset token requested earlier by an attacker would still be live in their
		/// hands the moment the victim finished resetting.
		/// </para>
		/// <para>
		/// The game tokens are revoked inside <see cref="SelfServiceService.ChangePasswordAsync"/>
		/// rather than here, which is the one place in this sequence that differs from the
		/// obvious reading of "credentials, sessions, tokens": reusing the panel's single
		/// credential write is worth more than the interleaving, and all three are gone before
		/// this method returns.
		/// </para>
		/// </remarks>
		public async Task<Outcome> CompleteAsync(
			string code,
			string salt,
			string verifier,
			CancellationToken cancellationToken = default)
		{
			// Validate the new credentials BEFORE spending the token, so a malformed body does
			// not burn a token the account holder would then have to request again.
			if (string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(verifier) ||
				salt.Length > MaxSaltLength || verifier.Length > MaxVerifierLength ||
				!IsHex(salt) || !IsHex(verifier))
			{
				// Exactly what registration says to the same shape of input.
				return new Outcome(false, "Invalid credentials.");
			}

			if (!TryHashCode(code, out string tokenHash, out _))
			{
				return new Outcome(false, InvalidCodeError);
			}

			/* Marks the token used and hands back its account in one statement whose WHERE clause
			 * carries the unused and unexpired checks. Unknown, expired and already-used all fail
			 * here, identically. */
			var redeemed = await resetTokens.RedeemAsync(tokenHash, cancellationToken);
			if (!redeemed.IsSuccess || string.IsNullOrWhiteSpace(redeemed.Data))
			{
				return new Outcome(false, InvalidCodeError);
			}

			string username = redeemed.Data;

			/* Re-check the account between issue and redemption: it may have been banned in the
			 * hour the token was live, and the ban must win. Same refusal message, because the
			 * holder of a token is not entitled to learn that either. */
			var account = await accounts.FetchForLoginAsync(username, false, cancellationToken);
			if (!account.IsSuccess)
			{
				log.LogWarning("A reset token for '{User}' was redeemed but the account is no longer eligible.", username);
				return new Outcome(false, InvalidCodeError);
			}

			/* The panel's only credential write. It persists the salt and verifier and revokes
			 * the account's game auth tokens. It does not touch two-factor, and must not. */
			var changed = await selfService.ChangePasswordAsync(username, salt, verifier, cancellationToken);
			if (!changed.Ok)
			{
				return new Outcome(false, changed.Error);
			}

			var revoked = await webSessions.RevokeAllForAccountAsync(username, cancellationToken);
			if (!revoked.IsSuccess)
			{
				// Not fatal — the credentials have already changed — but loud, because a live
				// session surviving a reset is the failure this whole step exists to prevent.
				log.LogError("Password reset for '{User}' completed but panel sessions were NOT revoked: [{Code}] {Message}",
					username, revoked.ErrorCode, revoked.ErrorMessage);
			}

			var invalidated = await resetTokens.InvalidateAllForAccountAsync(username, null, cancellationToken);
			if (!invalidated.IsSuccess)
			{
				log.LogError("Password reset for '{User}' completed but other reset tokens were NOT invalidated: [{Code}] {Message}",
					username, invalidated.ErrorCode, invalidated.ErrorMessage);
			}

			log.LogInformation("Password reset completed for '{User}'. Two-factor is unchanged.", username);
			return new Outcome(true, null);
		}

		/// <summary>
		/// Mints a token and its stored hash.
		/// </summary>
		/// <returns>The token for the mail, and the lowercase hex SHA-256 for the database.</returns>
		private static (string Token, string TokenHash) GenerateToken()
		{
			// 32 bytes of CSPRNG output, base64url so it survives a mail client, a copy-paste and
			// a URL unmangled. 43 characters, 256 bits.
			string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_');

			return (token, HashToken(token).Hex);
		}

		/// <summary>Hashes a token the way the store expects.</summary>
		private static (string Hex, byte[] Digest) HashToken(string token)
		{
			byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
			return (Convert.ToHexString(digest).ToLowerInvariant(), digest);
		}

		/// <summary>
		/// Validates the shape of a submitted code and hashes it.
		/// </summary>
		/// <remarks>
		/// The shape check is a bound on work, not a security check: it keeps an arbitrarily
		/// large body out of the hash and a lookup key out of the database, and it refuses
		/// exactly what a real token could never be.
		/// </remarks>
		private static bool TryHashCode(string code, out string tokenHash, out byte[] digest)
		{
			tokenHash = null;
			digest = null;

			if (string.IsNullOrWhiteSpace(code))
			{
				return false;
			}

			string trimmed = code.Trim();
			if (trimmed.Length == 0 || trimmed.Length > MaxCodeLength || !IsBase64Url(trimmed))
			{
				return false;
			}

			(tokenHash, digest) = HashToken(trimmed);
			return true;
		}

		/// <summary>Constant-time comparison of a stored hex hash against a computed digest.</summary>
		private static bool HashesMatch(string storedHex, byte[] digest)
		{
			if (string.IsNullOrEmpty(storedHex) || digest == null || storedHex.Length != digest.Length * 2)
			{
				return false;
			}

			byte[] stored;
			try
			{
				stored = Convert.FromHexString(storedHex);
			}
			catch (FormatException)
			{
				return false;
			}

			return CryptographicOperations.FixedTimeEquals(stored, digest);
		}

		private static bool IsBase64Url(string value)
		{
			foreach (char c in value)
			{
				bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
					c == '-' || c == '_';
				if (!ok) return false;
			}
			return true;
		}

		/// <summary>Matches <see cref="AccountRegistrationService"/>'s check on the same fields.</summary>
		private static bool IsHex(string value)
		{
			foreach (char c in value)
			{
				bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if (!ok) return false;
			}
			return true;
		}

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value)) return null;
			return value.Length <= max ? value : value.Substring(0, max);
		}

		/// <summary>
		/// Builds the reset email body, kept close in style to the verification emails so a
		/// player gets one recognisable voice from the shard.
		/// </summary>
		private static string BuildResetEmailBody(string username, string token)
		{
			return
				$"Hello {username},\n\n" +
				$"Someone asked to reset the password on your FishMMO account.\n\n" +
				$"Your reset code is: {token}\n\n" +
				$"Enter this code on the web control panel to choose a new password.\n" +
				$"This code expires in 60 minutes and can be used once.\n\n" +
				$"Resetting your password does NOT change your two-factor authentication: you will\n" +
				$"still need your authenticator app or a recovery code to sign in.\n\n" +
				$"If you did not request this, you can ignore this message — your password has not\n" +
				$"changed. If it keeps happening, contact support.\n";
		}
	}
}
