using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for world server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para><b>Execution Strategy:</b> All write operations (PersistAsync, PulseAsync, DeleteAsync) use execution strategy wrappers to handle transient database failures with automatic retry. FromSqlRaw and ExecuteSqlRawAsync calls do not automatically retry without manual wrapping.</para>
	/// <para><b>Read Operations:</b> Read operations use LINQ queries and are executed via BaseService execution wrappers for consistent exception mapping. Explicit transactions are used only when a write requires multiple database statements.</para>
	/// <para><b>Error Handling:</b> All database operations return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> with structured error codes (e.g., STALE_STATE, NOT_FOUND, UNIQUE_VIOLATION, DATABASE_ERROR) for comprehensive, safe error handling.</para>
	/// </remarks>
	public interface IWorldServerService : IFetchByKeyAction<long, WorldServerData>
	{
		/// <summary>
		/// Persists a world server registration (insert or update).
		/// Uses an insert-first approach and falls back to update on unique constraint conflicts.
		/// </summary>
		/// <param name="name">Server name (unique identifier for conflict resolution).</param>
		/// <param name="address">Server IP address or hostname.</param>
		/// <param name="port">Server port number.</param>
		/// <param name="characterCount">Current character count on server.</param>
		/// <param name="locked">Whether server is locked from accepting new connections.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult containing tuple (ServerId, ServerData) if successful.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Attempts INSERT; on unique violation, loads the existing row and updates it.</para>
		/// <para><b>Returns:</b> The returned ServerId is populated after SaveChanges completes inside the BaseService execution wrapper.</para>
		/// <para><b>Returns:</b> Failure if name/address empty or operation fails; Success with (ServerId, ServerData) on success.</para>
		/// </remarks>
		Task<DatabaseResult<(long ServerId, WorldServerData ServerData)>> PersistAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last pulse timestamp and character count for a world server (heartbeat).
		/// Keeps server registration alive by updating last_pulse to prevent timeout.
		/// </summary>
		/// <param name="serverId">Server ID to pulse.</param>
		/// <param name="characterCount">Current character count on server.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult indicating success or failure with detailed error information.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Uses ExecuteSqlRawAsync to UPDATE last_pulse and character_count.</para>
		/// <para><b>Execution Strategy:</b> Wrapped with CreateExecutionStrategy().ExecuteAsync() for transient failure retry (ExecuteSqlRawAsync doesn't auto-retry).</para>
		/// <para><b>Returns:</b> Failure if serverId <= 0; DatabaseEntityNotFoundException if no rows affected; Success if updated.</para>
		/// </remarks>
		Task<DatabaseResult<ServerControlState>> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens or closes this world server to new connections.
		/// </summary>
		/// <param name="serverId">World server row to update.</param>
		/// <param name="locked">True to close it to new arrivals.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Success, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// The row is the authority; the server adopts it on its next pulse. Locking drains
		/// rather than evicts — see <see cref="ServerControlState.Locked"/>.
		/// </remarks>
		Task<DatabaseResult> SetLockedAsync(long serverId, bool locked, CancellationToken cancellationToken = default);

		/// <summary>
		/// Schedules or cancels this world server's shutdown.
		/// </summary>
		/// <param name="serverId">World server row to update.</param>
		/// <param name="shutdownAtUtc">Absolute UTC stop time, or <c>null</c> to cancel.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Success, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// Scheduling also locks the server, in the same statement. Cancelling does not unlock
		/// it: halting a shutdown and reopening to players are separate decisions.
		/// </remarks>
		Task<DatabaseResult> SetShutdownAsync(long serverId, DateTime? shutdownAtUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads a world server's lock and shutdown state without writing a pulse.
		/// </summary>
		/// <param name="serverId">World server row to read.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The control state, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// For scene servers, which host scenes on behalf of a world but do not pulse its row.
		/// A world-wide shutdown has to reach them so they can warn their players and clear the
		/// world's characters out on the same deadline.
		/// </remarks>
		Task<DatabaseResult<ServerControlState>> FetchControlStateAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a world server registration from the database.
		/// Removes all server registration data including name, address, port, and statistics.
		/// </summary>
		/// <param name="serverId">Server ID to delete.</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult indicating success or failure with detailed error information.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> Uses ExecuteSqlRawAsync to DELETE server registration by ID.</para>
		/// <para><b>Execution Strategy:</b> Wrapped with CreateExecutionStrategy().ExecuteAsync() for transient failure retry (ExecuteSqlRawAsync doesn't auto-retry).</para>
		/// <para><b>Returns:</b> Failure if serverId <= 0; DatabaseEntityNotFoundException if no rows affected; Success if deleted.</para>
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches active world servers that have pulsed within the timeout window.
		/// Filters servers by last_pulse timestamp to return only servers that are currently online.
		/// </summary>
		/// <param name="idleTimeoutSeconds">Idle timeout in seconds before server considered inactive (default 60).</param>
		/// <param name="cancellationToken">Cancellation token for async operation.</param>
		/// <returns>DatabaseResult containing List of active WorldServerData ordered by name; empty list if no active servers.</returns>
		/// <remarks>
		/// <para><b>Operation:</b> LINQ query filtering by last_pulse >= (UtcNow - timeout), ordered by name.</para>
		/// <para><b>Execution Strategy:</b> BaseService handles retries and centralized exception mapping; explicit transactions are used only when a write requires multiple database statements.</para>
		/// </remarks>
		Task<DatabaseResult<List<WorldServerData>>> FetchActiveAsync(
			float idleTimeoutSeconds = 60.0f,
			CancellationToken cancellationToken = default);
	}
}