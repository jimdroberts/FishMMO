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
		/// Whether two-factor authentication is enabled.
		/// </summary>
		public readonly bool TwoFactorEnabled;

		/// <summary>
		/// Current two-factor authentication code. Null when 2FA is disabled or no code is active.
		/// </summary>
		public readonly string? TwoFactorCode;

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
			bool twoFactorEnabled,
			string? twoFactorCode,
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
			TwoFactorEnabled = twoFactorEnabled;
			TwoFactorCode = twoFactorCode;
			DiscordLinkCode = discordLinkCode;
			Verified = verified;
			VerifyCode = verifyCode;
			Created = created;
			LastLogin = lastLogin;
		}
	}
}