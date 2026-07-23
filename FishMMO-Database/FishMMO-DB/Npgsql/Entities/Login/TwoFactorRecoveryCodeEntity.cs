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
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Account the recovery code belongs to.</summary>
		public string AccountName { get; set; }
		/// <summary>Hashed recovery code value.</summary>
		public string CodeHash { get; set; }
		/// <summary>Timestamp when the code was used (null if unused).</summary>
		public DateTime? UsedAt { get; set; }
		/// <summary>Timestamp when the code was created.</summary>
		public DateTime TimeCreated { get; set; }

		// Navigation property
		/// <summary>Navigation property to the account entity.</summary>
		public AccountEntity Account { get; set; }
	}
}