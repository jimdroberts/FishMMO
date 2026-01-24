using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for scene instance lifecycle management operations.
	/// Provides async methods for scene loading, status updates, and retrieval.
	/// All methods return DatabaseResult or DatabaseResult&lt;T&gt; with comprehensive exception handling.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Enqueue*, Dequeue*, Update*, Set*, Pulse*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because SaveChangesAsync, ExecuteSqlRawAsync, and FromSqlRaw do not automatically
	/// benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods use the DatabaseResult pattern to provide structured success/failure information:
	/// - Success: Operation completed successfully with optional data
	/// - Failure: Operation failed with error code, safe message, and transient flag for retry logic
	/// Exception handling converts specific database exceptions into appropriate DatabaseResult failures.
	/// </para>
	/// <para>
	/// DequeueAsync uses atomic FOR UPDATE SKIP LOCKED pattern to prevent race conditions during concurrent dequeuing.
	/// EnqueueAsync relies on unique constraints to prevent duplicate scene loads.
	/// </para>
	/// </remarks>
	public interface ISceneService
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
		/// are automatically retried. Returns constraint exception if scene is already enqueued (unique constraint violation).
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
		/// DatabaseResult containing scene data if a pending scene was found and dequeued, empty result if no pending scenes, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses FromSqlRaw with FOR UPDATE SKIP LOCKED and execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Atomically updates status from Pending to Loading to prevent race conditions.
		/// Returns success with null data when no pending scenes exist (not an error condition).
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
		/// Sets a loading scene to ready status with server and handle information.
		/// </summary>
		/// <param name="sceneServerId">Scene server ID.</param>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Only updates scenes in Loading status.
		/// Returns entity not found exception if scene doesn't exist or is not in Loading status.
		/// </remarks>
		Task<DatabaseResult> SetReadyAsync(
			long sceneServerId,
			long worldServerId,
			string sceneName,
			int sceneHandle,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the character count for a scene (heartbeat).
		/// </summary>
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="characterCount">Current character count.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Updates character count for scene matching the handle.
		/// Returns entity not found exception if scene doesn't exist.
		/// </remarks>
		Task<DatabaseResult> PulseAsync(int sceneHandle, int characterCount, CancellationToken cancellationToken = default);

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
		/// <param name="sceneHandle">Scene handle.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult indicating success or error information on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns entity not found exception if scene doesn't exist.
		/// </remarks>
		Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default);

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
		Task<DatabaseResult<SceneData>> GetCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets a scene instance by ID.
		/// </summary>
		/// <param name="sceneId">Scene ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing scene data if found, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Returns entity not found exception if scene doesn't exist.
		/// </remarks>
		Task<DatabaseResult<SceneData>> GetInstanceByIdAsync(long sceneId, CancellationToken cancellationToken = default);

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
		Task<DatabaseResult<List<SceneData>>> GetAvailableScenesAsync(
			long worldServerId,
			string sceneName,
			int maxClients,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets all ready scenes for a world server.
		/// </summary>
		/// <param name="worldServerId">World server ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// DatabaseResult containing list of ready scene data for the world server, or error information on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (ToListAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Filters by Ready status only. Returns empty list if no scenes match.
		/// </remarks>
		Task<DatabaseResult<List<SceneData>>> GetReadyScenesAsync(long worldServerId, CancellationToken cancellationToken = default);
	}
}