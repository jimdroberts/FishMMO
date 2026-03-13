using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Tracks an issued authentication token for revocation and audit.
	/// The LoginServer issues tokens after successful SRP authentication.
	/// WorldServers and SceneServers validate tokens cryptographically and check this table for revocation.
	/// </summary>
	public class AuthTokenEntity
	{
		public long ID { get; set; }
		public string TokenHash { get; set; }
		public string AccountName { get; set; }
		public long LoginServerId { get; set; }
		public DateTime TimeCreated { get; set; }
		public DateTime ExpiresUtc { get; set; }
		public bool Revoked { get; set; }

		// Navigation properties
		public AccountEntity Account { get; set; }
		public LoginServerEntity LoginServer { get; set; }
	}
}