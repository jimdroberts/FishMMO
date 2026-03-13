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
		/// Temporary code for Discord account linking. Null when no link is pending.
		/// </summary>
		public readonly string? DiscordLinkCode;

		/// <summary>
		/// Whether the account email has been verified via the registration verification link.
		/// </summary>
		public readonly bool Verified;

		/// <summary>
		/// Random verification code sent to the account email. Zero when no verification is pending.
		/// </summary>
		public readonly int VerifyCode;

		/// <summary>
		/// Account creation timestamp (UTC).
		/// </summary>
		public readonly DateTime Created;

		/// <summary>
		/// Last login timestamp (UTC).
		/// </summary>
		public readonly DateTime LastLogin;

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
			string? discordLinkCode,
			bool verified,
			int verifyCode,
			DateTime created,
			DateTime lastLogin)
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
			DiscordLinkCode = discordLinkCode;
			Verified = verified;
			VerifyCode = verifyCode;
			Created = created;
			LastLogin = lastLogin;
		}
	}
}