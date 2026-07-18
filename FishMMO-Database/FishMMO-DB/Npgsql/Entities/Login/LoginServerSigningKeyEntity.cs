using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Stores the HMAC-SHA256 signing key for a specific LoginServer instance.
	/// WorldServers and SceneServers look up this key by LoginServerId to validate auth tokens.
	/// </summary>
	public class LoginServerSigningKeyEntity
	{
		/// <summary>
		/// Primary key (auto-increment).
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Foreign key to the LoginServer that owns this signing key.
		/// </summary>
		public long LoginServerId { get; set; }

		/// <summary>
		/// HMAC-SHA256 key used to sign and verify auth tokens.
		///
		/// The raw key material is stored as a PostgreSQL <c>bytea</c> column.
		/// On read, the column is returned as a <c>byte[]</c> that callers can
		/// zero out after use.
		///
		/// SERIALIZATION: The byte array is written and read directly via
		/// EF Core / Npgsql's <c>bytea</c> handling — no encoding, no envelope.
		/// The column width accommodates keys up to 64 bytes (SHA-512 half-length);
		/// callers generating 32-byte (SHA-256 half-length) keys are within range.
		///
		/// KEK ENVELOPE: In production, this value should be the ciphertext of
		/// the actual HMAC key encrypted under a Key Encryption Key (KEK) stored
		/// outside the database (e.g., AWS KMS, Azure Key Vault, or a local HSM).
		/// The LoginServer decrypts the envelope at startup to obtain the raw
		/// signing key in memory. This entity schema stores only the wrapped key;
		/// the KEK itself never touches the database.
		/// </summary>
		public byte[] HmacKey { get; set; }

		/// <summary>
		/// UTC timestamp when this key was first persisted.
		/// </summary>
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