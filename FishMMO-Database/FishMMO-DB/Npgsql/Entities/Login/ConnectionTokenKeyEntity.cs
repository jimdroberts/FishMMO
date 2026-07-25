using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Stores HMAC verification keys for connection tokens, indexed by a logical key ID.
	/// LoginServers read these keys at startup and periodically poll for new keys from
	/// new regions. Each key is stored as a base64-encoded HMAC key and can be
	/// deactivated gracefully (in-flight tokens remain verifiable until DeactivatedAt).
	/// </summary>
	public class ConnectionTokenKeyEntity
	{
		/// <summary>
		/// Primary key (auto-increment).
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Logical identifier for this key (e.g., "region-us-west", "region-eu-central").
		/// Used by LoginServers to look up the correct verification key for a given token.
		/// Unique across all keys — a second entry with the same key_id is rejected.
		/// </summary>
		public string KeyId { get; set; }

		/// <summary>
		/// HMAC key encoded as a base64 string.
		/// LoginServers decode this at startup to obtain the raw HMAC key material.
		/// </summary>
		public string HmacKeyBase64 { get; set; }

		/// <summary>
		/// Whether this key is currently active for signing new tokens.
		/// Inactive keys remain in the table so in-flight tokens can still be verified
		/// during the deactivation grace window.
		/// </summary>
		public bool IsActive { get; set; } = true;

		/// <summary>
		/// UTC timestamp when this key was first persisted.
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// UTC timestamp at which the key was deactivated. Null while active.
		/// Used to bound the verification overlap window and to identify
		/// which keys are safe to remove.
		/// </summary>
		public DateTime? DeactivatedAt { get; set; }
	}
}
