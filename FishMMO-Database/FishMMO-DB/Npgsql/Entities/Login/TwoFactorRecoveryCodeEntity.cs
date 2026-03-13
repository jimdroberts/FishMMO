using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Stores hashed two-factor recovery codes for an account.
	/// Recovery codes are one-time-use fallbacks when TOTP is unavailable.
	/// The server hashes codes before storage; this layer stores opaque hashes only.
	/// </summary>
	public class TwoFactorRecoveryCodeEntity
	{
		public long ID { get; set; }
		public string AccountName { get; set; }
		public string CodeHash { get; set; }
		public DateTime? UsedAt { get; set; }
		public DateTime TimeCreated { get; set; }

		// Navigation property
		public AccountEntity Account { get; set; }
	}
}