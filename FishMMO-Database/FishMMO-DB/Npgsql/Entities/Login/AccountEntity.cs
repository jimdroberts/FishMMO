using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a player account.
	/// </summary>
	public class AccountEntity
	{
		/// <summary>
		/// Unique account name (primary key).
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// SRP password salt.
		/// </summary>
		public string Salt { get; set; }

		/// <summary>
		/// SRP password verifier.
		/// </summary>
		public string Verifier { get; set; }

		/// <summary>
		/// Account access level.
		/// </summary>
		public byte AccessLevel { get; set; }

		/// <summary>
		/// Contact email address. Null if not provided.
		/// </summary>
		public string? Email { get; set; }

		/// <summary>
		/// Account holder age. Zero if not provided.
		/// </summary>
		public int Age { get; set; }

		/// <summary>
		/// Whether two-factor authentication is enabled.
		/// </summary>
		public bool TwoFactorEnabled { get; set; }

		/// <summary>
		/// Current two-factor authentication code. Null when 2FA is disabled or no code is active.
		/// </summary>
		public string? TwoFactorCode { get; set; }

		/// <summary>
		/// Temporary code used to link a Discord account. Null when no link is pending.
		/// The Discord bot generates this code and the user verifies in-game.
		/// </summary>
		public string? DiscordLinkCode { get; set; }

		/// <summary>
		/// Whether the account email has been verified via the registration verification link.
		/// Defaults to false until the user clicks the verification URL sent to their email.
		/// </summary>
		public bool Verified { get; set; }

		/// <summary>
		/// Random verification code sent to the account email during registration.
		/// The user must provide this code to toggle <see cref="Verified"/> to true.
		/// Zero when no verification is pending.
		/// </summary>
		public int VerifyCode { get; set; }

		/// <summary>
		/// Account creation timestamp (UTC).
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Last successful login timestamp (UTC).
		/// </summary>
		public DateTime LastLogin { get; set; }
	}
}