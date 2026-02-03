using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for login server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// DatabaseResult provides detailed error information to help distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (server doesn't exist)
	/// - Database errors (connection issues, constraint violations, transient failures)
	/// - Unexpected runtime errors
	/// </remarks>
	public interface ILoginServerService : IFetchByKeyAction<long, LoginServerData>
	{
		/// <summary>
		/// Adds or updates a login server registration.
		/// </summary>
		/// <param name="name">Server name (unique identifier). Must not be null or whitespace.</param>
		/// <param name="address">Server address. Must not be null or whitespace.</param>
		/// <param name="port">Server port.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing LoginServerData with server ID and details on success.</returns>
		/// <remarks>
		/// Success: Returns complete server data including generated ID.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid name or address (null/whitespace)
		/// - UNIQUE_VIOLATION: A unique constraint was violated (non-transient). Note: normal concurrent registration by name is handled via UPSERT and should not produce this.
		/// - DATABASE_ERROR: Unexpected database error
		/// </remarks>
		Task<DatabaseResult<LoginServerData>> PersistAsync(
			string name,
			string address,
			ushort port,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last pulse timestamp for a login server (heartbeat).
		/// </summary>
		/// <param name="serverId">Server ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Success: Pulse timestamp updated.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid server ID (less than or equal to 0)
		/// - ENTITY_NOT_FOUND: Server does not exist
		/// - DATABASE_ERROR: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> PulseAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a login server registration.
		/// </summary>
		/// <param name="serverId">Server ID to delete. Must be greater than 0.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Success: Server registration removed from database.
		/// If the server does not exist, this operation still succeeds.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid server ID (less than or equal to 0)
		/// - DATABASE_ERROR: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default);

	}
}