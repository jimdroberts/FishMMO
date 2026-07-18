using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One-time connection token issued by IPFetch for real-IP recovery.
	/// The token bridges the HTTP layer (where the real client IP is visible
	/// via X-Forwarded-For) and the QUIC/WebTransport layer (where the game
	/// server sees 127.0.0.1 due to L4 UDP proxying).
	///
	/// Lifecycle:
	///   1. IPFetch generates a token, stores the SHA-256 hash + real IP.
	///   2. Client echoes the token in the first ClientHandshake broadcast.
	///   3. Login Server looks up the hash, extracts the real IP, deletes the row.
	///   4. Expired rows are cleaned up by a periodic background job.
	/// </summary>
	public class ConnectionTokenEntity
	{
		/// <summary>Auto-increment primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// SHA-256 hash of the raw token (lowercase hex, 64 chars).
		/// Storing the hash instead of the raw token means a database
		/// compromise does not reveal usable tokens.
		/// </summary>
		public string TokenHash { get; set; }

		/// <summary>
		/// Real client IP address extracted from X-Forwarded-For or
		/// RemoteIpAddress at the HTTP layer. IPv4 or IPv6 string.
		/// </summary>
		public string RealIp { get; set; }

		/// <summary>Row creation timestamp (auto-set by EF convention).</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Absolute expiration time. Tokens are valid for 60 seconds
		/// from creation — enough time for the HTTP response to reach
		/// the client and the QUIC handshake to complete.
		/// </summary>
		public DateTime ExpiresAt { get; set; }
	}
}