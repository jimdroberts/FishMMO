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
		/// Whether the account is verified: the one flag sign-in reads.
		/// </summary>
		/// <remarks>
		/// Set by the first correct code on any channel the player chose, or by a server that verifies
		/// nothing. See <c>AccountVerificationRules</c>.
		/// </remarks>
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

		/* ── Contact and identity (issue #252) ────────────────────────────────────────────────
		 * Optional, player-supplied, and shown only to staff and to the player. Used to establish
		 * who owns an account when everything else about it is lost; never an input to signing in. */

		/// <summary>Phone number in E.164 form (<c>+</c> and up to 15 digits), or null.</summary>
		public string? Phone { get; set; }

		/// <summary>Whether <see cref="Phone"/> has been proven by an SMS code. Cleared when the number changes.</summary>
		public bool PhoneVerified { get; set; }

		/// <summary>The outstanding SMS verification code, or 0 when none is pending.</summary>
		public int PhoneVerifyCode { get; set; }

		/// <summary>When <see cref="PhoneVerifyCode"/> stops being accepted.</summary>
		public DateTime? PhoneVerifyCodeExpiresUtc { get; set; }

		/// <summary>Whether the email address has been proven by its code.</summary>
		/// <remarks>
		/// Per channel. <see cref="Verified"/> remains the one flag sign-in reads, and is true once
		/// every channel in <see cref="VerificationChannels"/> is satisfied.
		/// </remarks>
		public bool EmailVerified { get; set; }

		/// <summary>
		/// Which channels the player chose to verify with, as <c>AccountVerificationChannels</c> flags:
		/// 1 email, 2 SMS.
		/// </summary>
		public byte VerificationChannels { get; set; } = 1;

		/// <summary>The account holder's real name, as they gave it, or null.</summary>
		public string? RealName { get; set; }

		/// <summary>Country or region, as they gave it, or null.</summary>
		public string? Country { get; set; }

		/// <summary>Postal address, as they gave it, or null.</summary>
		public string? Address { get; set; }

		/// <summary>The account that referred this one, as typed at registration, or null. No foreign key.</summary>
		public string? ReferralAccount { get; set; }

		/* ── Discord: the verification DM and the bot's account link ─────────────────────────────
		 * The player gives a username; the bot resolves it to a user in the game's Discord server and
		 * sends ONE direct message carrying the code. The user it went to becomes the account's linked
		 * Discord user when the code is redeemed. See DiscordVerification for the once-only rules. */

		/// <summary>
		/// The Discord username the player gave, lowercase, or null: <c>fishfan</c>, or <c>fishfan#1234</c>
		/// for a name that still carries its discriminator. See <c>AccountProfileRules.NormalizeDiscordUsername</c>.
		/// </summary>
		public string? DiscordUsername { get; set; }

		/// <summary>
		/// The Discord user this account is linked to, or null. Unique: one Discord account links one game account.
		/// </summary>
		/// <remarks>A snowflake is an unsigned 64-bit number; it is stored signed, which holds every snowflake until 2084.</remarks>
		public long? DiscordUserId { get; set; }

		/// <summary>When <see cref="DiscordUserId"/> was set, or null.</summary>
		public DateTime? DiscordLinkedAt { get; set; }

		/// <summary>Whether the Discord channel has been proven: the DM's code redeemed, or the bot's link made.</summary>
		public bool DiscordVerified { get; set; }

		/// <summary>The code the one DM carries, or 0 when none has been issued or it has been used.</summary>
		public int DiscordVerifyCode { get; set; }

		/// <summary>When the bot took the DM to send it, or null. Never reclaimed automatically.</summary>
		public DateTime? DiscordDmClaimedAt { get; set; }

		/// <summary>When the DM was delivered, or null. Once set, no further DM is sent for this account.</summary>
		public DateTime? DiscordDmSentAt { get; set; }

		/// <summary>The Discord user the DM was delivered to, or null.</summary>
		public long? DiscordDmUserId { get; set; }

		/// <summary>Sends Discord refused. Waiting for the player to join the server is not counted.</summary>
		public int DiscordDmAttempts { get; set; }

		/// <summary>Why the DM has not gone out yet, as the bot last recorded it, or null.</summary>
		public string? DiscordDmLastError { get; set; }

		/// <summary>
		/// Incorrect verification codes since the last correct one. A support ticket is opened when it
		/// reaches <c>AccountVerificationRules.FailedCodesBeforeTicket</c>.
		/// </summary>
		public int VerifyFailedCount { get; set; }

		/* ── Sign-in lockout (issue #252) ─────────────────────────────────────────────────────
		 * Counted in the database, not in a process, so the game's login servers and the panel
		 * share one count: an attacker cannot reset it by switching surface or by waiting for a
		 * restart. Passwords and authenticator codes are counted apart, because they are guessed
		 * apart — a player who fat-fingers the code must not burn the password budget. */

		/// <summary>Failed password proofs in the current window.</summary>
		public int FailedLoginCount { get; set; }

		/// <summary>When the current password-failure window began, or null.</summary>
		public DateTime? FailedLoginSinceUtc { get; set; }

		/// <summary>Until when password sign-in is refused, or null.</summary>
		public DateTime? LoginLockedUntilUtc { get; set; }

		/// <summary>Failed authenticator or recovery codes in the current window.</summary>
		public int FailedTwoFactorCount { get; set; }

		/// <summary>When the current code-failure window began, or null.</summary>
		public DateTime? FailedTwoFactorSinceUtc { get; set; }

		/// <summary>Until when the second step is refused, or null.</summary>
		public DateTime? TwoFactorLockedUntilUtc { get; set; }

		/// <summary>
		/// Last successful login timestamp (UTC).
		/// </summary>
		public DateTime LastLogin { get; set; }
	}
}