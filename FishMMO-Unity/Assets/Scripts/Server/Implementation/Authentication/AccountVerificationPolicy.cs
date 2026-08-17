using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Single source of truth for the development-only "skip email verification" policy
	/// driven by the <c>AutoVerifyAccounts</c> configuration key.
	///
	/// Two call sites must agree on this answer:
	///   * Account creation, which persists <c>verified = true</c> instead of generating a
	///     verify code, queueing a verification email, and enrolling mandatory TOTP.
	///   * The login lookup, which otherwise rejects an unverified account once
	///     <c>verification_email_sent_at</c> has been stamped. Without the login side
	///     honouring the flag, accounts created before auto-verify was enabled (or created
	///     against a production build) stay locked out of a local server forever.
	///
	/// The compile-time guard is deliberate: a production player build ignores the key
	/// entirely, so a Development <c>LoginServer.cfg</c> that leaks into a production
	/// deployment cannot re-enable the bypass. Consequently the flag also has no effect in a
	/// server binary built with the Production working environment — build the server with
	/// the Development working environment for local testing.
	/// </summary>
	public static class AccountVerificationPolicy
	{
		/// <summary>Configuration key that enables the development email-verification bypass.</summary>
		public const string AutoVerifyAccountsKey = "AutoVerifyAccounts";

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
	}
}
