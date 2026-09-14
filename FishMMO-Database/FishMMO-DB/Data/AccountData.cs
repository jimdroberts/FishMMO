using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Account data transfer object.
	/// </summary>
	public struct AccountData
	{
		/// <summary>
		/// Account name (unique identifier).
		/// </summary>
		public readonly string Name;

		/// <summary>
		/// Password salt for SRP authentication.
		/// </summary>
		public readonly string Salt;

		/// <summary>
		/// Password verifier for SRP authentication.
		/// </summary>
		public readonly string Verifier;

		/// <summary>
		/// Account access level.
		/// </summary>
		public readonly byte AccessLevel;

		/// <summary>
		/// Contact email address. Null if not provided.
		/// </summary>
		public readonly string? Email;

		/// <summary>
		/// Account holder age. Zero if not provided.
		/// </summary>
		public readonly int Age;

		/// <summary>
		/// Whether TOTP two-factor authentication is enabled.
		/// </summary>
		public readonly bool TotpEnabled;

		/// <summary>
		/// Base32-encoded TOTP secret key, encrypted at rest. Null when not set up.
		/// </summary>
		public readonly string? TotpSecret;

		/// <summary>
		/// Timestamp (UTC) of the first successful TOTP verification. Null when setup not confirmed.
		/// </summary>
		public readonly DateTime? TotpVerifiedAt;

		/// <summary>
		/// Last TOTP time-step window successfully used, for replay attack prevention.
		/// </summary>
		public readonly long LastTotpWindow;

		/// <summary>
		/// Whether the account is verified: the one flag sign-in reads. Any one proven channel sets it.
		/// </summary>
		public readonly bool Verified;

		/// <summary>
		/// Random verification code sent to the account email. Zero when no verification is pending.
		/// </summary>
		public readonly int VerifyCode;

		/// <summary>
		/// UTC expiry for the verification code. Null when no verification is pending.
		/// </summary>
		public readonly DateTime? VerifyCodeExpiresUtc;

		/// <summary>
		/// UTC timestamp when a verification email or text was last delivered. Null if none has been.
		/// </summary>
		public readonly DateTime? VerificationEmailSentAt;

		/// <summary>
		/// Account creation timestamp (UTC).
		/// </summary>
		public readonly DateTime Created;

		/// <summary>
		/// Last login timestamp (UTC).
		/// </summary>
		public readonly DateTime LastLogin;
		/// <summary>
		/// The verification channels the player chose, as <c>AccountVerificationChannels</c> flags.
		/// <see cref="Verified"/> is set by the first of them to be proven.
		/// </summary>
		public readonly byte VerificationChannels;
		/// <summary>Whether the email channel has been proven.</summary>
		public readonly bool EmailVerified;
		/// <summary>Whether the SMS channel has been proven.</summary>
		public readonly bool PhoneVerified;
		/// <summary>Phone number in E.164 form, or null. Where an SMS code is sent.</summary>
		public readonly string? Phone;
		/// <summary>Until when password sign-in is refused, or null. Shared by every login server and the panel.</summary>
		public readonly DateTime? LoginLockedUntilUtc;
		/// <summary>Until when the authenticator step is refused, or null.</summary>
		public readonly DateTime? TwoFactorLockedUntilUtc;
		/// <summary>
		/// UTC expiry for the SMS verification code, or null when no SMS code has been issued. The
		/// login server reads it to re-send an expired or missing SMS code at sign-in.
		/// </summary>
		public readonly DateTime? PhoneVerifyCodeExpiresUtc;
		/// <summary>The Discord username the player gave, lowercase, or null. Where the one verification DM goes.</summary>
		public readonly string? DiscordUsername;
		/// <summary>Whether the Discord channel has been proven (by the DM's code, or by the bot's link).</summary>
		public readonly bool DiscordVerified;
		/// <summary>Whether a Discord code has been issued. A code is issued once and never replaced.</summary>
		public readonly bool DiscordVerifyCodeIssued;
		/// <summary>When the verification DM was delivered, or null. Set once; no second DM is ever sent.</summary>
		public readonly DateTime? DiscordDmSentAt;

		/// <summary>
		/// Initializes a new instance of the <see cref="AccountData"/> struct.
		/// </summary>
		public AccountData(
			string name,
			string salt,
			string verifier,
			byte accessLevel,
			string? email,
			int age,
			bool totpEnabled,
			string? totpSecret,
			DateTime? totpVerifiedAt,
			long lastTotpWindow,
			bool verified,
			int verifyCode,
			DateTime? verifyCodeExpiresUtc,
			DateTime? verificationEmailSentAt,
			DateTime created,
			DateTime lastLogin,
			byte verificationChannels = 1,
			bool emailVerified = false,
			bool phoneVerified = false,
			string? phone = null,
			DateTime? loginLockedUntilUtc = null,
			DateTime? twoFactorLockedUntilUtc = null,
			DateTime? phoneVerifyCodeExpiresUtc = null,
			string? discordUsername = null,
			bool discordVerified = false,
			bool discordVerifyCodeIssued = false,
			DateTime? discordDmSentAt = null)
		{
			Name = name;
			Salt = salt;
			Verifier = verifier;
			AccessLevel = accessLevel;
			Email = email;
			Age = age;
			TotpEnabled = totpEnabled;
			TotpSecret = totpSecret;
			TotpVerifiedAt = totpVerifiedAt;
			LastTotpWindow = lastTotpWindow;
			Verified = verified;
			VerifyCode = verifyCode;
			VerifyCodeExpiresUtc = verifyCodeExpiresUtc;
			VerificationEmailSentAt = verificationEmailSentAt;
			Created = created;
			LastLogin = lastLogin;
			VerificationChannels = verificationChannels;
			EmailVerified = emailVerified;
			PhoneVerified = phoneVerified;
			Phone = phone;
			LoginLockedUntilUtc = loginLockedUntilUtc;
			TwoFactorLockedUntilUtc = twoFactorLockedUntilUtc;
			PhoneVerifyCodeExpiresUtc = phoneVerifyCodeExpiresUtc;
			DiscordUsername = discordUsername;
			DiscordVerified = discordVerified;
			DiscordVerifyCodeIssued = discordVerifyCodeIssued;
			DiscordDmSentAt = discordDmSentAt;
		}
	}
}
