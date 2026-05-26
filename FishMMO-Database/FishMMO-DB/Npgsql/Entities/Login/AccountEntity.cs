using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a player account.
	/// </summary>
	public class AccountEntity
	{
		/// <summary>
		/// Unique account name (primary key) — preserves the casing the user registered with.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Case-insensitive lookup column for <see cref="Name"/>. Computed as <c>LOWER(name)</c> in the database
		/// and bound by a UNIQUE index so that two accounts cannot differ only in case.
		/// Application code must filter on this column (lowercased input) for all account-name lookups.
		/// </summary>
		public string NameLowercase { get; set; }

		/// <summary>
		/// PostgreSQL <c>xmin</c> system column exposed as an EF Core concurrency token to detect
		/// concurrent modifications. Updated automatically by the database on every row change.
		/// </summary>
		public uint Version { get; set; }

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
		/// Whether TOTP two-factor authentication is enabled for this account.
		/// </summary>
		public bool TotpEnabled { get; set; }

		/// <summary>
		/// Base32-encoded TOTP secret key, encrypted at rest by the server layer.
		/// Null when TOTP has not been set up.
		/// </summary>
		public string? TotpSecret { get; set; }

		/// <summary>
		/// Timestamp (UTC) of the first successful TOTP verification, confirming setup completion.
		/// Null when TOTP setup has not been confirmed.
		/// </summary>
		public DateTime? TotpVerifiedAt { get; set; }

		/// <summary>
		/// The last TOTP time-step window that was successfully used, for replay attack prevention.
		/// Zero when no TOTP code has been verified yet.
		/// </summary>
		public long LastTotpWindow { get; set; }

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