using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for scene server registration and management operations.
	/// Provides async methods for server registration, heartbeat updates, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Persist*, Pulse*, Delete*) in this service use execution strategies to ensure transient
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
	/// PersistAsync uses atomic UPSERT to prevent race conditions during concurrent registrations.
	/// </para>
	/// </remarks>
	public interface ISceneServerService : IFetchByKeyAction<long, SceneServerData>
	{
		/// <summary>
		/// Persists a scene server registration with atomic UPSERT.
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
		Task<DatabaseResult<(long ServerId, SceneServerData ServerData)>> PersistAsync(
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
		/// Retrieves multiple scene servers by their IDs in batches.
		/// </summary>
		/// <param name="serverIds">List of server IDs to query.</param>
		/// <param name="maxBatchSize">Maximum number of IDs per database round-trip (500–1000).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of SceneServerData for each found server.</returns>
		Task<DatabaseResult<IReadOnlyList<SceneServerData>>> FetchSceneServersByIDsAsync(List<long> serverIds, int maxBatchSize = 500, CancellationToken cancellationToken = default);
	}
}