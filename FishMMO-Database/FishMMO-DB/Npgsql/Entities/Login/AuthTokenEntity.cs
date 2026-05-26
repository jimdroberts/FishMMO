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

		/// <summary>
		/// PostgreSQL <c>xmin</c> system column exposed as an EF Core concurrency token to detect
		/// concurrent revocations or future per-token mutations. Updated automatically by the
		/// database on every row change.
		/// </summary>
		public uint Version { get; set; }

		// Navigation properties
		public AccountEntity Account { get; set; }
		public LoginServerEntity LoginServer { get; set; }
	}
}