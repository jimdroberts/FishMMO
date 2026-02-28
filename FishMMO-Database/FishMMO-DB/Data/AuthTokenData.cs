using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for an issued authentication token record.
	/// </summary>
	public struct AuthTokenData
	{
		public readonly long ID;
		public readonly string TokenHash;
		public readonly string AccountName;
		public readonly long LoginServerId;
		public readonly DateTime TimeCreated;
		public readonly DateTime ExpiresUtc;
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
