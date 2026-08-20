using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for scene instance lifecycle management operations.
	/// Provides async methods for scene loading, status updates, and retrieval.
	/// All methods return DatabaseResult or DatabaseResult&lt;T&gt; with comprehensive exception handling.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Enqueue*, Dequeue*, Update*, Set*, Pulse*, Delete*) in this service use BaseService execution wrappers
	/// to ensure transient database failures are automatically retried and exceptions are mapped to DatabaseResult.
	/// When a write requires multiple database statements, it is wrapped in ExecuteTransactionAsync; single-statement SQL operations
	/// (including CTE-based UPDATE/DELETE/UPSERT) are executed atomically without requiring an explicit transaction wrapper.
	/// </para>
	/// <para>
	/// All methods use the DatabaseResult pattern to provide structured success/failure information:
	/// - Success: Operation completed successfully with optional data
	/// - Failure: Operation failed with error code, safe message, and transient flag for retry logic
	/// Exception handling converts specific database exceptions into appropriate DatabaseResult failures.
	/// </para>
	/// <para>
	/// DequeueAsync uses atomic FOR UPDATE SKIP LOCKED pattern to prevent race conditions during concurrent dequeuing.
	/// EnqueueAsync is retry-idempotent (protects against EF Core execution-strategy retries after transient failures).
	/// </para>
	/// </remarks>
	public interface ISceneService : IFetchByKeyAction<long, SceneData>, IFetchManyByKeyAction<long, SceneData>
	{
		/// <summary>
		/// Enqueues a new scene load request.
		/// </summary>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneType">Scene type.</param>
		/// <param name="characterId">Character ID (optional, for instances).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing scene ID on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses SaveChangesAsync with execution strategy wrapping to ensure transient database failures
		/// are automatically retried.
		/// Uses BaseService execution wrappers for automatic transient failure retry and centralized exception mapping.
		/// </remarks>
		Task<DatabaseResult<long>> EnqueueAsync(
			long worldServerId,
			string sceneName,
			SceneType sceneType,
			long characterId = 0,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Dequeues the next pending scene load request and marks it as loading.
		/// </summary>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing scene data if a pending scene was found and dequeued, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses FromSqlRaw with FOR UPDATE SKIP LOCKED and execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Atomically updates status from Pending to Loading to prevent race conditions.
		/// Returns failure with error code <c>NO_PENDING_SCENES</c> when no pending scenes exist.
		/// </remarks>
		Task<DatabaseResult<SceneData>> DequeueAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the status of a scene.
		/// </summary>
		/// <param name="sceneId">Scene ID.</param>
		/// <param name="status">New scene status.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns entity not found exception if scene doesn't exist.
		/// </remarks>
		Task<DatabaseResult> UpdateStatusAsync(long sceneId, SceneStatus status, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the loading scene identified by <paramref name="sceneId"/> to ready status,
		/// recording which scene server hosts it and under which runtime handle.
		/// </summary>
		/// <param name="sceneId">Database ID of the scene row being made ready. This is the row the caller dequeued.</param>
		/// <param name="sceneServerId">Scene server ID.</param>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="sceneName">Scene name, validated against the row as a consistency check.</param>
		/// <param name="sceneHandle">The hosting process's scene-manager handle, recorded for diagnostics only. Instances are identified across processes by <paramref name="sceneId"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Only updates the named scene while it is in Loading status; the update is idempotent
		/// for a row already made ready by the same server and handle (in-call retry safety).
		/// <para>
		/// The row is addressed by ID rather than by (world, name) ordering. Ordering was
		/// ambiguous whenever two rows for the same scene name were loading at once, so the
		/// server/handle of one load could be written onto the other row. For instanced scenes
		/// that row also carries <c>character_id</c>, so the mix-up handed a character the
		/// scene instance created for somebody else.
		/// </para>
		/// </remarks>
		Task<DatabaseResult> SetReadyAsync(long sceneId, long sceneServerId, long worldServerId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the character count for a scene (heartbeat).
		/// </summary>
		/// <param name="sceneId">Scene row to update.</param>
		/// <param name="characterCount">Current character count.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns entity not found exception if no scene matches.
		/// <para>
		/// Addressed by row id rather than by scene handle. A scene handle is the owning process's
		/// own identifier for a loaded scene and is not unique anywhere else, so two scene servers
		/// that happened to allocate the same handle overwrote each other's population on every
		/// pulse — and that population is the number the world server routes and load-balances on.
		/// </para>
		/// </remarks>
		Task<DatabaseResult> PulseAsync(long sceneId, int characterCount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all scenes for a scene server.
		/// </summary>
		/// <param name="sceneServerId">Scene server ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing number of scenes deleted on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns 0 rows deleted if no scenes exist (idempotent).
		/// </remarks>
		Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all scenes for a world server.
		/// </summary>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing number of scenes deleted on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns 0 rows deleted if no scenes exist (idempotent).
		/// </remarks>
		Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific scene by server and handle.
		/// </summary>
		/// <param name="sceneServerId">Scene server ID.</param>
		/// <param name="sceneHandle">The owning process's scene-manager handle.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Retained for administrative use. Prefer <see cref="DeleteAsync"/>: a scene handle only
		/// identifies a row in combination with the scene server that allocated it, whereas the row
		/// id identifies it anywhere.
		/// </remarks>
		Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a single scene row by its database ID.
		/// </summary>
		/// <param name="sceneId">Scene row to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success, or NotFound when the row is already gone.</returns>
		/// <remarks>
		/// Preferred over <see cref="DeleteByHandleAsync"/>: the row id is the only identifier for
		/// a scene instance that means the same thing in every process. Idempotent — deleting a
		/// row that is already gone succeeds, because both callers are removing something they
		/// have already stopped using.
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long sceneId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a character's instance by character ID and scene type.
		/// </summary>
		/// <param name="characterId">Character ID.</param>
		/// <param name="sceneType">Scene type.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing scene data if found, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Returns entity not found exception if scene doesn't exist.
		/// </remarks>
		Task<DatabaseResult<SceneData>> FetchCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets list of ready scenes for a world server and scene name with available capacity.
		/// </summary>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="maxClients">Maximum client capacity.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing list of available scene data matching criteria, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (ToListAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Filters by Ready status and character_count less than maxClients. Returns empty list if no scenes match.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchAvailableAsync(
			long worldServerId,
			string sceneName,
			int maxClients,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the character count for multiple scenes in a single batched operation.
		/// </summary>
		/// <param name="pulses">List of (sceneId, characterCount) pairs to update.</param>
		/// <param name="maxBatchSize">Maximum number of scenes per database round-trip (500–2500).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The total number of rows affected across all batches.</returns>
		/// <remarks>Addressed by row id for the reason given on <see cref="PulseAsync"/>.</remarks>
		Task<DatabaseResult<int>> PulseBatchAsync(List<(long sceneId, int characterCount)> pulses, int maxBatchSize = 1000, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes scene rows for a world server that never reached <see cref="SceneStatus.Ready"/>
		/// and are older than <paramref name="olderThanUtc"/>.
		/// </summary>
		/// <param name="worldServerId">World server whose rows to reap.</param>
		/// <param name="olderThanUtc">Rows created strictly before this instant are eligible.</param>
		/// <param name="maxRows">Upper bound on rows removed in one call, so a large backlog is drained across several sweeps rather than in one long transaction.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing the number of rows deleted.</returns>
		/// <remarks>
		/// Nothing else removes a Pending, Loading or Failed row. That is not merely untidy:
		/// such a row keeps its <c>character_id</c>, and a character pointed at one can never
		/// finish entering the world. A Loading row orphaned by a scene server that died between
		/// dequeue and load still has <c>scene_server_id = 0</c>, so
		/// <see cref="DeleteBySceneServerAsync"/> does not match it on that server's restart, and
		/// it survives indefinitely. Reaping by age is what bounds both.
		/// <para>
		/// Ready rows are deliberately untouched: they represent live scene instances and are
		/// removed by the scene server that owns them when it unloads them or shuts down.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<int>> DeleteStaleUnreadyAsync(long worldServerId, DateTime olderThanUtc, int maxRows = 256, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes this world server's scene rows whose owning scene server has stopped pulsing,
		/// or is no longer registered at all.
		/// </summary>
		/// <param name="worldServerId">World server whose rows to reap.</param>
		/// <param name="pulseOlderThanUtc">A scene server that has not pulsed since this instant is treated as gone.</param>
		/// <param name="maxRows">Upper bound on rows removed in one call.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult containing the number of rows deleted.</returns>
		/// <remarks>
		/// A scene server only deletes its own rows on a graceful shutdown, so a crash leaves
		/// every scene it hosted advertised as Ready forever. Those rows are actively harmful
		/// rather than merely stale: the world server routes players to them, sending clients to
		/// an address that either refuses them or — once a replacement scene server reuses the
		/// port — answers as a server that does not have the scene. Either way the client is
		/// bounced back, re-routed from the same row, and bounced again, with nothing in the loop
		/// that ages out.
		/// <para>
		/// Rows whose <c>scene_server_id</c> is 0 are skipped: those are queued or loading scenes
		/// that have not been assigned a host yet, and belong to
		/// <see cref="DeleteStaleUnreadyAsync"/>.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<int>> DeleteByStaleSceneServersAsync(long worldServerId, DateTime pulseOlderThanUtc, int maxRows = 256, CancellationToken cancellationToken = default);
	}
}
