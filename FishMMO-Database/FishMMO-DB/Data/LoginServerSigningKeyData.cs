using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Data transfer object for a LoginServer's HMAC signing key.
	/// </summary>
	public struct LoginServerSigningKeyData
	{
		/// <summary>
		/// Primary key of the signing key record.
		/// </summary>
		public readonly long ID;

		/// <summary>
		/// Foreign key identifying the LoginServer that owns this key.
		/// </summary>
		public readonly long LoginServerId;

		/// <summary>
		/// HMAC signing key for token validation.
		/// CAUTION: <see cref="byte[]"/> is a reference type; the array is not copied on construction.
		/// Callers must not mutate the array contents after passing it to this struct.
		/// NOTE: The <c>readonly</c> modifier prevents reassignment of the field, but the
		/// underlying byte array contents are still mutable. Callers that receive this struct
		/// must not modify the array elements or the integrity of any downstream consumer
		/// that holds the same reference is compromised.
		/// </summary>
		public readonly byte[] HmacKey;

		/// <summary>
		/// UTC timestamp of when the key was created.
		/// </summary>
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