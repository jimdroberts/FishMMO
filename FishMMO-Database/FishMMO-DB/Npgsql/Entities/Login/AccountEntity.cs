using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a player account.
	/// </summary>
	public class AccountEntity
	{
		/// <summary>
		/// Unique account name, and the primary key.
		/// </summary>
		/// <remarks>
		/// <b>Stored lowercase.</b> It used to be documented as preserving the casing the user
		/// registered with, and it does not: <c>AccountService.PersistAsync</c> inserts
		/// <c>ToLowerInvariant()</c>. Anything that displays this back to a player shows
		/// lowercase, and any code written on the belief that the original casing survives is
		/// wrong. <see cref="NameLowercase"/> is a generated column that exists so the unique
		/// index is case-insensitive regardless of what is inserted here.
		/// </remarks>
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
		/// UTC expiry timestamp for <see cref="VerifyCode"/>. Once exceeded, the code is
		/// considered invalid and must be regenerated. Null while no verification is pending.
		/// </summary>
		public DateTime? VerifyCodeExpiresUtc { get; set; }

		/// <summary>
		/// UTC timestamp when the verification email was successfully sent via SMTP.
		/// Null while the email is still pending in the outbound queue.
		/// When non-null and <see cref="Verified"/> is false, login is blocked until the user verifies.
		/// </summary>
		public DateTime? VerificationEmailSentAt { get; set; }

		/// <summary>
		/// Whether the account is muted in chat.
		/// </summary>
		/// <remarks>
		/// An account mute silences every character on the account, which is what makes it the
		/// answer to a player who simply switches character. <see cref="MutedUntil"/> null with this
		/// set is a mute with no end; a past <see cref="MutedUntil"/> is an expired one, and nothing
		/// has to clear the row for it to stop applying.
		/// </remarks>
		public bool Muted { get; set; }

		/// <summary>When the account's mute lifts (UTC), or null for a mute with no end.</summary>
		public DateTime? MutedUntil { get; set; }

		/// <summary>Account of the operator who applied the mute. Not a foreign key, deliberately.</summary>
		public string? MutedBy { get; set; }

		/// <summary>What the operator gave as the reason for the mute.</summary>
		public string? MuteReason { get; set; }

		/// <summary>
		/// When a temporary ban lifts (UTC), or null for a permanent ban or no ban.
		/// </summary>
		/// <remarks>
		/// Meaningful only while <see cref="AccessLevel"/> is <c>Banned</c>. The login fetch restores
		/// <c>Player</c> the first time it reads a banned account whose instant has passed, so a
		/// temporary ban needs no scheduler to end — and an account nobody tries to sign in to stays
		/// banned on the row, harmlessly, until somebody does.
		/// </remarks>
		public DateTime? BannedUntil { get; set; }

		/// <summary>Account of the operator who applied the ban. Not a foreign key, deliberately.</summary>
		public string? BannedBy { get; set; }

		/// <summary>What the operator gave as the reason for the ban.</summary>
		public string? BanReason { get; set; }

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