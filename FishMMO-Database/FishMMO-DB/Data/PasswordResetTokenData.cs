using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for an outstanding password reset token.
	/// </summary>
	/// <remarks>
	/// Carries the stored hash, never a redeemable token: the token exists only in the email and
	/// in the browser that received it.
	/// </remarks>
	public struct PasswordResetTokenData
	{
		/// <summary>Surrogate key.</summary>
		public readonly long ID;

		/// <summary>Account the token was issued for.</summary>
		public readonly string AccountName;

		/// <summary>Lowercase hex SHA-256 of the emailed token.</summary>
		public readonly string TokenHash;

		/// <summary>When the token was issued.</summary>
		public readonly DateTime CreatedUtc;

		/// <summary>When the token stops being redeemable.</summary>
		public readonly DateTime ExpiresUtc;

		/// <summary>When the token was redeemed, or null while outstanding.</summary>
		public readonly DateTime? UsedUtc;

		/// <summary>Client address that asked for the reset, or null.</summary>
		public readonly string? RequestedIp;

		public PasswordResetTokenData(
			long id,
			string accountName,
			string tokenHash,
			DateTime createdUtc,
			DateTime expiresUtc,
			DateTime? usedUtc,
			string? requestedIp)
		{
			ID = id;
			AccountName = accountName;
			TokenHash = tokenHash;
			CreatedUtc = createdUtc;
			ExpiresUtc = expiresUtc;
			UsedUtc = usedUtc;
			RequestedIp = requestedIp;
		}
	}
}
