using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Single source of truth for whether, and on which channels, new accounts must prove their
	/// contact details: the development-only <c>AutoVerifyAccounts</c> bypass and the per-channel
	/// <c>VerifyEmail</c> / <c>VerifySms</c> / <c>VerifyDiscord</c> switches. Which codes an account is
	/// then sent and asked for is <c>AccountVerificationRules</c>, shared with the Control Panel.
	///
	/// Two call sites must agree on these answers:
	///   * Account creation, which decides which codes to send and whether to verify the account outright.
	///   * The login lookup, which otherwise rejects an unverified account. Without the login side
	///     honouring the same switches, an account created before a switch changed would be asked
	///     for a code no server will ever send, or let in without one it still owes.
	///
	/// <para><b>Precedence, highest first.</b></para>
	/// <list type="number">
	///   <item><c>AutoVerifyAccounts=true</c> (editor and development builds only): every account is
	///   verified at creation, no code is sent, and authenticator enrolment is skipped. Unchanged
	///   from before the channel switches existed.</item>
	///   <item><c>VerifyEmail</c> / <c>VerifySms</c> / <c>VerifyDiscord</c> say which channels this server
	///   sends codes on. When all three are false, every new account is verified at creation through
	///   the same database write the bypass uses — but, unlike the bypass, it is still enrolled in
	///   two-factor authentication, because these switches are allowed in a production build and must
	///   not quietly turn the second factor off with them.</item>
	///   <item>Otherwise a code goes out on each channel the player chose that is switched on and the
	///   account can receive, falling back to email; <b>any one code verifies the account</b>, and
	///   there is no grace period: sign-in asks for a code until one is entered.</item>
	/// </list>
	///
	/// The compile-time guard on <see cref="IsAutoVerifyEnabled"/> is deliberate: a production player
	/// build ignores that key entirely, so a Development <c>LoginServer.cfg</c> that leaks into a
	/// production deployment cannot re-enable the bypass. Consequently the flag also has no effect in a
	/// server binary built with the Production working environment — build the server with the
	/// Development working environment for local testing. The channel switches have no such guard;
	/// a missing or unreadable value counts as <c>true</c>, so a typo verifies more, never less.
	/// </summary>
	public static class AccountVerificationPolicy
	{
		/// <summary>Configuration key that enables the development email-verification bypass.</summary>
		public const string AutoVerifyAccountsKey = "AutoVerifyAccounts";

		/// <summary>Configuration key switching email-code verification on or off. Default true.</summary>
		public const string VerifyEmailKey = "VerifyEmail";

		/// <summary>Configuration key switching SMS-code verification on or off. Default true.</summary>
		public const string VerifySmsKey = "VerifySms";

		/// <summary>Configuration key switching Discord-DM verification on or off. Default true.</summary>
		public const string VerifyDiscordKey = "VerifyDiscord";

		/// <summary>
		/// Returns <c>true</c> when new accounts should be verified on creation and unverified
		/// accounts should be allowed to log in. Always <c>false</c> outside the editor and
		/// development builds.
		/// </summary>
		/// <param name="configuration">Server configuration; a null configuration fails closed.</param>
		public static bool IsAutoVerifyEnabled(IServerConfiguration configuration)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			// Fail closed when configuration is unavailable — we cannot confirm this is a
			// local server, and silently verifying accounts is the riskier default.
			if (configuration == null)
			{
				return false;
			}

			if (configuration.TryGetString(AutoVerifyAccountsKey, out string autoVerifyStr) &&
				!string.IsNullOrWhiteSpace(autoVerifyStr))
			{
				return bool.TryParse(autoVerifyStr.Trim(), out bool enabled) && enabled;
			}

			// Key absent: dev convention is to auto-verify so a local server works without SMTP.
			return true;
#else
			return false;
#endif
		}

		/// <summary>Whether a player who chose email verification must enter an emailed code.</summary>
		/// <param name="configuration">Server configuration; null counts as switched on.</param>
		public static bool IsEmailVerificationEnabled(IServerConfiguration configuration) =>
			IsChannelEnabled(configuration, VerifyEmailKey);

		/// <summary>Whether a player who chose SMS verification must enter a texted code.</summary>
		/// <param name="configuration">Server configuration; null counts as switched on.</param>
		public static bool IsSmsVerificationEnabled(IServerConfiguration configuration) =>
			IsChannelEnabled(configuration, VerifySmsKey);

		/// <summary>Whether a player who chose Discord verification must enter the code the Discord bot sent.</summary>
		/// <remarks>Needs FishMMO-DiscordBot running against the same database: it delivers the code.</remarks>
		/// <param name="configuration">Server configuration; null counts as switched on.</param>
		public static bool IsDiscordVerificationEnabled(IServerConfiguration configuration) =>
			IsChannelEnabled(configuration, VerifyDiscordKey);

		/// <summary>The channels this server sends verification codes on.</summary>
		/// <param name="configuration">Server configuration; null counts as every channel switched on.</param>
		public static AccountVerificationChannels EnabledChannels(IServerConfiguration configuration) =>
			(IsEmailVerificationEnabled(configuration) ? AccountVerificationChannels.Email : AccountVerificationChannels.None) |
			(IsSmsVerificationEnabled(configuration) ? AccountVerificationChannels.Sms : AccountVerificationChannels.None) |
			(IsDiscordVerificationEnabled(configuration) ? AccountVerificationChannels.Discord : AccountVerificationChannels.None);

		/// <summary>
		/// Whether the sign-in path owes a fresh SMS verification code: none was ever issued (null), or
		/// the one on record has expired. Unlike the email resend, a missing code counts — SMS has no
		/// delivery-stamp grace period, so an account waiting on a code nobody sent would be stuck.
		/// </summary>
		/// <param name="phoneVerifyCodeExpiresUtc">The stored SMS code expiry, or null.</param>
		/// <param name="nowUtc">The current UTC time.</param>
		public static bool IsSmsCodeResendDue(System.DateTime? phoneVerifyCodeExpiresUtc, System.DateTime nowUtc) =>
			phoneVerifyCodeExpiresUtc == null || phoneVerifyCodeExpiresUtc.Value <= nowUtc;

		/// <summary>
		/// Expires an email or SMS verification code that was stored but could not be queued for
		/// delivery, so the next sign-in issues a fresh one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A code is stored before it is queued — the other order could deliver a code the database
		/// never held — and it is stored live for 24 hours. Sign-in re-issues a code only once the
		/// stored one has expired or is missing, and nothing else in the game re-issues one. So a code
		/// whose queue insert failed stayed live and undelivered for a day, and the player could not
		/// verify at all until it lapsed. Rewriting the same code with an expiry of now leaves nothing
		/// redeemable and makes the very next correct sign-in send a new one.
		/// </para>
		/// <para>
		/// Best effort and bounded to one write: if it fails as well, the day's wait is what remains,
		/// and the log says so. The write is unconditional, so it would also retire a code another
		/// server issued in the few milliseconds since this one's; the queue failure has already cost
		/// the player a message, and the next sign-in replaces whatever this clears.
		/// </para>
		/// </remarks>
		/// <param name="accountService">Account service.</param>
		/// <param name="accountName">Account whose code is retired.</param>
		/// <param name="verifyCode">The code that was stored and not delivered.</param>
		/// <param name="channel">Which code: <see cref="AccountVerificationChannels.Email"/> or <see cref="AccountVerificationChannels.Sms"/>.</param>
		/// <param name="logSource">Log source of the caller.</param>
		public static async Task ExpireUndeliveredCodeAsync(
			IAccountService accountService,
			string accountName,
			int verifyCode,
			AccountVerificationChannels channel,
			string logSource)
		{
			System.DateTime nowUtc = System.DateTime.UtcNow;
			DatabaseResult expired = channel == AccountVerificationChannels.Sms
				? await accountService.PersistPhoneVerifyCodeAsync(accountName, verifyCode, nowUtc)
				: await accountService.PersistVerifyCodeAsync(accountName, verifyCode, nowUtc);
			if (!expired.IsSuccess)
			{
				await Log.Warning(logSource, $"Could not expire the undelivered {channel} verification code for '{accountName}': [{expired.ErrorCode}] {expired.ErrorMessage}. It stays live, undelivered, until it lapses.");
			}
		}

		/// <summary>
		/// Reads a boolean key. <see cref="IServerConfiguration"/> has no boolean accessor, so every
		/// boolean in a <c>.cfg</c> is a string parsed here.
		/// </summary>
		/// <param name="configuration">Server configuration, or null.</param>
		/// <param name="key">The key.</param>
		/// <param name="value">The parsed value; false when the method returns false.</param>
		/// <returns>True only when the key is present and reads as <c>true</c> or <c>false</c>.</returns>
		public static bool TryReadBool(IServerConfiguration configuration, string key, out bool value)
		{
			value = false;
			if (configuration == null ||
				!configuration.TryGetString(key, out string raw) ||
				string.IsNullOrWhiteSpace(raw))
			{
				return false;
			}
			return bool.TryParse(raw.Trim(), out value);
		}

		private static bool IsChannelEnabled(IServerConfiguration configuration, string key)
		{
			// Absent or unreadable is "on": a switch that fails to parse must not waive verification.
			return !TryReadBool(configuration, key, out bool enabled) || enabled;
		}
	}
}
