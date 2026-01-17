using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for world server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para><b>Execution Strategy:</b> All write operations (AddOrUpdateAsync, PulseAsync, DeleteAsync) use execution strategy wrappers to handle transient database failures with automatic retry. FromSqlInterpolated and ExecuteSqlInterpolatedAsync calls do not automatically retry without manual wrapping.</para>
	/// <para><b>Read Operations:</b> Read operations (GetServerAsync, GetActiveServersAsync) use LINQ queries which automatically benefit from EnableRetryOnFailure without additional wrapping.</para>
	/// <para><b>Error Handling:</b> All database operations return DatabaseResult or DatabaseResult&lt;T&gt; for comprehensive exception handling with typed database exceptions (DatabaseConnectionException, DatabaseConstraintException, DatabaseQueryException, DatabaseTimeoutException, DatabaseEntityNotFoundException).</para>
	/// </remarks>
	public interface IWorldServerService
	{
		/// <summary>
		/// Adds or updates a world server registration with atomic UPSERT operation.
		/// Uses PostgreSQL INSERT...ON CONFLICT for atomic upsert of server registrations.
		/// </summary>
		/// <param name="name">Server name (unique identifier for conflict resolution).</param>
		/// <param name="address">Server IP address or hostname.</param>
		/// <param name="port">Server port number.</param>
		/// <param name="characterCount">Current character count on server.</param>
		/// <param name="locked">Whether server is locked from accepting new connections.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult containing tuple (ServerId, ServerData) if successful.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Performs atomic UPSERT using FromSqlInterpolated with RETURNING clause.</para>
		/// <para><b>Execution Strategy:</b> Wrapped with CreateExecutionStrategy().ExecuteAsync() for transient failure retry (FromSqlInterpolated doesn't auto-retry).</para>
		/// <para><b>Returns:</b> Failure if name/address empty or operation fails; Success with (ServerId, ServerData) on success.</para>
		/// </remarks>
		Task<DatabaseResult<(long ServerId, WorldServerData ServerData)>> AddOrUpdateAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last pulse timestamp and character count for a world server (heartbeat).
		/// Keeps server registration alive by updating lastpulse to prevent timeout.
		/// </summary>
		/// <param name="serverId">Server ID to pulse.</param>
		/// <param name="characterCount">Current character count on server.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult indicating success or failure with detailed error information.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Uses ExecuteSqlInterpolatedAsync to UPDATE lastpulse and character_count.</para>
		/// <para><b>Execution Strategy:</b> Wrapped with CreateExecutionStrategy().ExecuteAsync() for transient failure retry (ExecuteSqlInterpolatedAsync doesn't auto-retry).</para>
		/// <para><b>Returns:</b> Failure if serverId <= 0; DatabaseEntityNotFoundException if no rows affected; Success if updated.</para>
		/// </remarks>
		Task<DatabaseResult> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a world server registration from the database.
		/// Removes all server registration data including name, address, port, and statistics.
		/// </summary>
		/// <param name="serverId">Server ID to delete.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult indicating success or failure with detailed error information.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Uses ExecuteSqlInterpolatedAsync to DELETE server registration by ID.</para>
		/// <para><b>Execution Strategy:</b> Wrapped with CreateExecutionStrategy().ExecuteAsync() for transient failure retry (ExecuteSqlInterpolatedAsync doesn't auto-retry).</para>
		/// <para><b>Returns:</b> Failure if serverId <= 0; DatabaseEntityNotFoundException if no rows affected; Success if deleted.</para>
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a world server registration by ID.
		/// Retrieves full server data including name, address, port, character count, locked status, and last pulse.
		/// </summary>
		/// <param name="serverId">Server ID to retrieve.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult containing WorldServerData if found; DatabaseEntityNotFoundException if not found.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> LINQ query with AsNoTracking for read-only retrieval.</para>
		/// <para><b>Execution Strategy:</b> Automatic retry via EnableRetryOnFailure (LINQ queries benefit automatically).</para>
		/// </remarks>
		Task<DatabaseResult<WorldServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets list of active world servers that have pulsed within the timeout window.
		/// Filters servers by lastpulse timestamp to return only servers that are currently online.
		/// </summary>
		/// <param name="idleTimeoutSeconds">Idle timeout in seconds before server considered inactive (default 60).</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult containing List of active WorldServerData ordered by name; empty list if no active servers.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> LINQ query filtering by lastpulse >= (UtcNow - timeout), ordered by name.</para>
		/// <para><b>Execution Strategy:</b> Automatic retry via EnableRetryOnFailure (LINQ queries benefit automatically).</para>
		/// </remarks>
		Task<DatabaseResult<List<WorldServerData>>> GetActiveServersAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default);
	}
}