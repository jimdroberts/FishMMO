using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for an issued authentication token record.
	/// </summary>
	public struct AuthTokenData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Hashed authentication token.</summary>
		public readonly string TokenHash;
		/// <summary>Account name associated with token.</summary>
		public readonly string AccountName;
		/// <summary>Login server that issued the token.</summary>
		public readonly long LoginServerId;
		/// <summary>Timestamp when token was created.</summary>
		public readonly DateTime TimeCreated;
		/// <summary>Token expiration timestamp (UTC).</summary>
		public readonly DateTime ExpiresUtc;
		/// <summary>Whether the token has been revoked.</summary>
		public readonly bool Revoked;

		public AuthTokenData(long id, string tokenHash, string accountName, long loginServerId,
			DateTime timeCreated, DateTime expiresUtc, bool revoked)
		{
			ID = id;
			TokenHash = tokenHash;
			AccountName = accountName;
			LoginServerId = loginServerId;
			TimeCreated = timeCreated;
			ExpiresUtc = expiresUtc;
			Revoked = revoked;
		}
	}
}
