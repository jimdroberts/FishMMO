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

		/// <summary>
		/// PostgreSQL <c>xmin</c> system column exposed as an EF Core concurrency token to detect
		/// concurrent rotations writing the same row. Updated automatically by the database on
		/// every row change.
		/// </summary>
		public uint Version { get; set; }

		/// <summary>
		/// Whether this key is currently the active key used to sign new tokens.
		/// Old keys remain in the table (IsActive=false) so in-flight tokens can still be verified
		/// during the rotation overlap window.
		/// </summary>
		public bool IsActive { get; set; } = true;

		/// <summary>
		/// UTC timestamp at which the key became active (typically equals TimeCreated).
		/// </summary>
		public DateTime ActivatedAtUtc { get; set; }

		/// <summary>
		/// UTC timestamp at which the key was rotated out (became inactive). Null while active.
		/// Used to bound the verification overlap window and to identify which keys are safe to delete.
		/// </summary>
		public DateTime? RotatedAtUtc { get; set; }

		// Navigation property
		public LoginServerEntity LoginServer { get; set; }
	}
}