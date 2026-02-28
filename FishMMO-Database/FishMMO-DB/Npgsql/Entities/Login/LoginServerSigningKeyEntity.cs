using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Stores the HMAC-SHA256 signing key for a specific LoginServer instance.
	/// WorldServers and SceneServers look up this key by LoginServerId to validate auth tokens.
	/// </summary>
	public class LoginServerSigningKeyEntity
	{
		public long ID { get; set; }
		public long LoginServerId { get; set; }
		public byte[] HmacKey { get; set; }
		public DateTime TimeCreated { get; set; }

		// Navigation property
		public LoginServerEntity LoginServer { get; set; }
	}
}
