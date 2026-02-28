using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for a LoginServer's HMAC signing key.
	/// </summary>
	public struct LoginServerSigningKeyData
	{
		public readonly long ID;
		public readonly long LoginServerId;
		public readonly byte[] HmacKey;
		public readonly DateTime TimeCreated;

		public LoginServerSigningKeyData(long id, long loginServerId, byte[] hmacKey, DateTime timeCreated)
		{
			ID = id;
			LoginServerId = loginServerId;
			HmacKey = hmacKey;
			TimeCreated = timeCreated;
		}
	}
}
