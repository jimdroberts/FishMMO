using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for scene server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (AddOrUpdate*, Pulse*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because ExecuteSqlRawAsync and FromSqlRaw do not automatically retry on transient failures
	/// without an execution strategy wrapper.
	/// BaseService provides execution wrappers for retry and centralized exception mapping; explicit transactions
	/// are used only when a write requires multiple database statements.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (server doesn't exist)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// AddOrUpdateAsync uses atomic UPSERT to prevent race conditions during concurrent registrations.
	/// </para>
	/// </remarks>
	public interface ISceneServerService
	{
		/// <summary>
		/// Adds or updates a scene server registration with atomic UPSERT.
		/// </summary>
		/// <param name="name">Server name (unique identifier).</param>
		/// <param name="address">Server address.</param>
		/// <param name="port">Server port.</param>
		/// <param name="characterCount">Current character count.</param>
		/// <param name="locked">Whether server is locked.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing a tuple with (ServerId, ServerData) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses FromSqlRaw with RETURNING clause and execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Uses PostgreSQL ON CONFLICT for atomic UPSERT with full data return.
		/// </remarks>
		Task<DatabaseResult<(long ServerId, SceneServerData ServerData)>> AddOrUpdateAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the last pulse timestamp, character count, and lock state for a scene server (heartbeat).
		/// </summary>
		/// <param name="serverId">Server ID.</param>
		/// <param name="characterCount">Current character count.</param>
		/// <param name="locked">Whether server is locked.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// Returns <see cref="DatabaseEntityNotFoundException"/> if server doesn't exist.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Updates timestamp to current UTC time along with character count and lock state.
		/// </remarks>
		Task<DatabaseResult> PulseAsync(long serverId, int characterCount, bool locked, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a scene server registration.
		/// </summary>
		/// <param name="serverId">Server ID to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// Returns <see cref="DatabaseEntityNotFoundException"/> if server doesn't exist.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a scene server by ID.
		/// </summary>
		/// <param name="serverId">Server ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the server data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// Returns <see cref="DatabaseEntityNotFoundException"/> if server not found.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<SceneServerData>> GetServerAsync(long serverId, CancellationToken cancellationToken = default);
	}
}