using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service for validating and consuming one-time connection tokens.
	/// Connection tokens bridge the real client IP from the HTTP layer
	/// (where X-Forwarded-For is visible) to the QUIC/WebTransport layer
	/// (where the game server sees 127.0.0.1 behind an L4 UDP proxy).
	///
	/// Lifecycle:
	///   1. IPFetch issues a token, stores hash + real IP in connection_tokens.
	///   2. Client echoes the token in its first ClientHandshake.
	///   3. Login Server calls ValidateAndConsumeAsync to look up the real IP.
	///   4. Token row is deleted (one-time use).
	/// </summary>
	public interface IConnectionTokenService
	{
		/// <summary>
		/// Validates a connection token and returns the associated real client IP.
		/// The token is consumed (deleted) on first successful lookup — subsequent
		/// lookups with the same hash return null.
		/// </summary>
		/// <param name="tokenHash">
		/// SHA-256 hash of the raw token, as a lowercase hex string (64 chars).
		/// The raw token is never stored — only the hash.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The real client IP if the token is valid and not expired;
		/// null if the token is not found, expired, or already consumed.
		/// </returns>
		Task<DatabaseResult<string?>> ValidateAndConsumeAsync(
			string tokenHash,
			CancellationToken cancellationToken = default);
	}
}