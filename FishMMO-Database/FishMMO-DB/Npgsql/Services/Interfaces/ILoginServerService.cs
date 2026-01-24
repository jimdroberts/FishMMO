using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for login server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (AddOrUpdate*, Pulse*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because ExecuteSqlRawAsync and FromSqlRaw do not automatically
	/// benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// DatabaseResult provides detailed error information to help distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (server doesn't exist)
	/// - Database errors (connection issues, constraint violations, transient failures)
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// AddOrUpdateAsync uses atomic UPSERT to prevent race conditions during concurrent registrations.
	/// </para>
	/// </remarks>
	public interface ILoginServerService
	{
		/// <summary>
		/// Adds or updates a login server registration with atomic UPSERT.
		/// </summary>
		/// <param name="name">Server name (unique identifier). Must not be null or whitespace.</param>
		/// <param name="address">Server address. Must not be null or whitespace.</param>
		/// <param name="port">Server port.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing LoginServerData with server ID and details on success.</returns>
		/// <remarks>
		/// Uses FromSqlRaw with RETURNING clause and execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Uses PostgreSQL ON CONFLICT for atomic UPSERT with full data return.
		/// 
		/// Success: Returns complete server data including generated ID and current timestamp.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid name or address (null/whitespace)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Unexpected database error
		/// </remarks>
		Task<DatabaseResult<LoginServerData>> AddOrUpdateAsync(
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
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Updates timestamp to current database server time.
		/// 
		/// Success: Pulse timestamp updated to CURRENT_TIMESTAMP.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid server ID (less than or equal to 0)
		/// - DB_NOT_FOUND: Server does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> PulseAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a login server registration.
		/// </summary>
		/// <param name="serverId">Server ID to delete. Must be greater than 0.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried.
		/// 
		/// Success: Server registration removed from database.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid server ID (less than or equal to 0)
		/// - DB_NOT_FOUND: Server does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Unexpected database error
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a login server by ID.
		/// </summary>
		/// <param name="serverId">Server ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing LoginServerData if found.</returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// 
		/// Success: Returns complete server data.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid server ID (less than or equal to 0)
		/// - DB_NOT_FOUND: Server does not exist
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// </remarks>
		Task<DatabaseResult<LoginServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default);
	}
}