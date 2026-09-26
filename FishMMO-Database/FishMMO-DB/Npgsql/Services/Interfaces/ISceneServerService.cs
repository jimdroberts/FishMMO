using System;
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
		/// Updates the last pulse timestamp and character count for a scene server (heartbeat), and
		/// reads its operator control state back in the same statement.
		/// </summary>
		/// <param name="serverId">Server ID.</param>
		/// <param name="characterCount">Current character count.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The row's <see cref="ServerControlState"/>: its lock, and any scheduled shutdown with the
		/// seconds left before it as the database clock measured them
		/// (<see cref="ServerControlState.ShutdownInSeconds"/>). NotFound when the row is gone.
		/// </returns>
		/// <remarks>
		/// Transient failures are retried by the execution wrapper. The pulse writes neither
		/// control column: the row is the authority for both, and the server adopts what it reads.
		/// </remarks>
		Task<DatabaseResult<ServerControlState>> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Opens or closes this scene server to new arrivals.
		/// </summary>
		/// <param name="serverId">Scene server row to update.</param>
		/// <param name="locked">True to close it.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Success, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// A locked scene server is skipped by the world server's routing and stops dequeuing
		/// scene-load requests, so it drains as its players leave. Players already on it keep
		/// playing. The row is the authority; the server adopts it on its next pulse.
		/// </remarks>
		Task<DatabaseResult> SetLockedAsync(long serverId, bool locked, CancellationToken cancellationToken = default);

		/// <summary>
		/// Schedules or cancels this scene server's shutdown.
		/// </summary>
		/// <param name="serverId">Scene server row to update.</param>
		/// <param name="shutdownAtUtc">Absolute UTC stop time, or <c>null</c> to cancel.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Success, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// Scheduling also locks the server; cancelling does not unlock it. For an absolute instant
		/// the caller already holds; a delay belongs on <see cref="SetShutdownInAsync"/>, for the
		/// reason given on <see cref="IWorldServerService.SetShutdownAsync"/>.
		/// </remarks>
		Task<DatabaseResult> SetShutdownAsync(long serverId, DateTime? shutdownAtUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Schedules this scene server's shutdown a number of seconds from now by the database
		/// clock, and locks it.
		/// </summary>
		/// <param name="serverId">Scene server row to update.</param>
		/// <param name="seconds">Delay in seconds. Zero means now; negative is refused.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The UTC deadline written, or NotFound when the row is gone.</returns>
		/// <remarks>
		/// The deadline is taken from the database clock inside the writing statement, so the
		/// delay asked for is the delay that elapses. See
		/// <see cref="IWorldServerService.SetShutdownInAsync"/>, including what a retry does.
		/// </remarks>
		Task<DatabaseResult<DateTime>> SetShutdownInAsync(long serverId, int seconds, CancellationToken cancellationToken = default);

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
		/// <remarks>
		/// Each row carries <see cref="SceneServerData.PulseAgeSeconds"/>, measured by the database
		/// clock as it was read, which is what a caller must judge liveness by. So does
		/// <c>FetchAsync</c>.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<SceneServerData>>> FetchSceneServersByIDsAsync(List<long> serverIds, int maxBatchSize = 500, CancellationToken cancellationToken = default);
	}
}