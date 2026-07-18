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
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>SHA-256 hash of the issued token for lookup and comparison.</summary>
		public string TokenHash { get; set; }
		/// <summary>Account name the token was issued to.</summary>
		public string AccountName { get; set; }
		/// <summary>Login server ID that issued this token.</summary>
		public long LoginServerId { get; set; }
		/// <summary>Row creation timestamp (UTC) — when the token was issued.</summary>
		public DateTime TimeCreated { get; set; }
		/// <summary>UTC timestamp when the token expires.</summary>
		public DateTime ExpiresUtc { get; set; }
		/// <summary>Whether this token has been explicitly revoked.</summary>
		public bool Revoked { get; set; }

		/// <summary>
		/// PostgreSQL <c>xmin</c> system column exposed as an EF Core concurrency token to detect
		/// concurrent revocations or future per-token mutations. Updated automatically by the
		/// database on every row change.
		/// </summary>
		public uint Version { get; set; }

		// Navigation properties
		/// <summary>Navigation reference to the associated account entity.</summary>
		public AccountEntity Account { get; set; }
		/// <summary>Navigation reference to the issuing login server entity.</summary>
		public LoginServerEntity LoginServer { get; set; }
	}
}