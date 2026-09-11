using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A single outstanding "forgot my password" token.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only the SHA-256 of the emailed token is stored, exactly as <see cref="WebSessionEntity"/>
	/// stores only the hash of a session identifier: a database read must not yield working reset
	/// tokens. A plain hash is correct here rather than a slow KDF because the token is 32 bytes
	/// of <c>RandomNumberGenerator</c> output and therefore not guessable — the hash exists to
	/// stop a leaked table being replayed, not to resist a dictionary.
	/// </para>
	/// <para>
	/// Possession of a token proves control of the mailbox and nothing else. Redeeming one
	/// replaces the SRP credentials and must never touch <c>totp_enabled</c>, <c>totp_secret</c>,
	/// <c>totp_verified_at</c> or the recovery codes: if an emailed link could also clear the
	/// second factor, a compromised mailbox would be a complete account takeover and the second
	/// factor would protect nothing.
	/// </para>
	/// <para>
	/// There is deliberately no foreign key to <c>accounts</c>, which is where this table differs
	/// from <see cref="AuthTokenEntity"/> and <see cref="WebSessionEntity"/>. Accounts are never
	/// deleted in this system, so the cascade those tables buy can never fire; and a row is only
	/// ever written after an account has already been read, so the constraint could never reject
	/// a row this code would otherwise create. The cost is that a row could outlive an account
	/// removed by hand — harmless, because redemption re-reads the account and refuses when it is
	/// missing or banned.
	/// </para>
	/// <para>
	/// There is no <c>xmin</c> concurrency token either. Redemption is one conditional UPDATE
	/// (<c>used_utc IS NULL AND expires_utc &gt; now</c>) whose WHERE clause is the concurrency
	/// control: two simultaneous redemptions of the same token cannot both affect a row.
	/// </para>
	/// </remarks>
	public class PasswordResetTokenEntity
	{
		/// <summary>Surrogate key.</summary>
		public long ID { get; set; }

		/// <summary>Account the token was issued for, matching <c>accounts.name</c>.</summary>
		public string AccountName { get; set; }

		/// <summary>
		/// Lowercase hex SHA-256 of the emailed token. The token itself is never stored.
		/// </summary>
		public string TokenHash { get; set; }

		/// <summary>When the token was issued. Drives the per-account resend cooldown.</summary>
		public DateTime CreatedUtc { get; set; }

		/// <summary>When the token stops being redeemable.</summary>
		public DateTime ExpiresUtc { get; set; }

		/// <summary>
		/// When the token was redeemed, or null while it is still outstanding. Redeeming one
		/// token also stamps this on every other outstanding token for the account.
		/// </summary>
		public DateTime? UsedUtc { get; set; }

		/// <summary>Client address that asked for the reset, for abuse investigation.</summary>
		public string? RequestedIp { get; set; }
	}
}
