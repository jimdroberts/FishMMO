using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class SceneService : BaseService<SceneEntity>, ISceneService
	{
		/// <summary>
		/// Compiled query for retrieving character instance scene (hot path for scene loading).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<SceneEntity?>> GetCharacterInstanceQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int sceneType, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.FirstOrDefault(s => s.CharacterID == characterId && s.SceneType == sceneType));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving scene by ID (hot path for scene loading).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<SceneEntity?>> GetInstanceByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long sceneId, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.FirstOrDefault(s => s.ID == sceneId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving available scenes (hot path for scene matchmaking).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, string, int, int, CancellationToken, Task<List<SceneEntity>>> GetAvailableScenesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, string sceneName, int maxClients, int readyStatus, CancellationToken ct) =>
				context.Scenes
						.AsNoTracking()
						.Where(s =>
							s.WorldServerID == worldServerId &&
							s.SceneName == sceneName &&
							s.CharacterCount < maxClients &&
							s.SceneStatus == readyStatus)
						.ToList());

		/// <summary>
		/// Compiled query for retrieving ready scenes (hot path for scene server queries).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<List<SceneEntity>>> GetReadyScenesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, int readyStatus, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.Where(s => s.WorldServerID == worldServerId && s.SceneStatus == readyStatus)
					.ToList());

		/// <summary>
		/// Initializes a new instance of SceneService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> EnqueueAsync(
			long worldServerId,
			string sceneName,
			SceneType sceneType,
			long characterId = 0,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid parameters: world server ID and scene name are required.");
			}

			return await ExecuteSqlAsync<long>(async (dbContext, ct) =>
			{
				var sceneTypeInt = (int)sceneType;
				var sceneStatusInt = (int)SceneStatus.Pending;

				// Use CURRENT_TIMESTAMP from database server for consistency
				// Optimized: RETURNING only id for better performance and reduced memory overhead
				var result = await dbContext.Scenes
					.FromSqlInterpolated($@"
					INSERT INTO {TableName} 
					(world_server_id, scene_name, scene_type, scene_status, character_id, time_created)
					VALUES ({worldServerId}, {sceneName}, {sceneTypeInt}, {sceneStatusInt}, {characterId}, CURRENT_TIMESTAMP)
					RETURNING id")
						.AsNoTracking()
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				var sceneId = result?.ID ?? 0;
				if (sceneId <= 0)
				{
					throw new DatabaseQueryException(
						"EnqueueScene",
						"Failed to enqueue scene.",
						"INSERT RETURNING returned no results",
						false,
						null);
				}

				return sceneId;
			}, "EnqueueScene", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> DequeueAsync(CancellationToken cancellationToken = default)
		{
			// Use SceneData? (nullable) since no pending scenes is valid business logic, not an error
			var result = await ExecuteSqlAsync<SceneData?>(async (dbContext, ct) =>
			{
				// Atomically dequeue next pending scene with CTE and FOR UPDATE SKIP LOCKED
				// CTE ensures the row is locked BEFORE the UPDATE, preventing race conditions
				var entity = await dbContext.Scenes
					.FromSqlInterpolated($@"
					WITH scene_to_update AS (
						SELECT id FROM {TableName}
						WHERE scene_status = {(int)SceneStatus.Pending}
						ORDER BY time_created
						FOR UPDATE SKIP LOCKED
						LIMIT 1
					)
					UPDATE {TableName}
					SET scene_status = {(int)SceneStatus.Loading}
					FROM scene_to_update
					WHERE {TableName}.id = scene_to_update.id
					RETURNING {TableName}.id, {TableName}.world_server_id, {TableName}.scene_server_id, {TableName}.scene_name, {TableName}.scene_handle, {TableName}.scene_status, {TableName}.scene_type, {TableName}.character_id, {TableName}.character_count, {TableName}.time_created")
					.AsNoTracking()
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				return entity != null ? MapEntityToDto(entity) : null;
			}, "DequeueScene", cancellationToken).ConfigureAwait(false);

			// Convert null result to business logic failure (not an exception case)
			if (result.IsSuccess && result.Data == null)
			{
				return DatabaseResult<SceneData>.Failure("NO_PENDING_SCENES", "No pending scenes available.");
			}

			// If failed, propagate the failure
			if (!result.IsSuccess)
			{
				return DatabaseResult<SceneData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			// Success with data (checked for null above)
			return DatabaseResult<SceneData>.Success(result.Data!.Value);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateStatusAsync(long sceneId, SceneStatus status, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid scene ID.");
			}

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
					SET scene_status = {(int)status} 
					WHERE id = {sceneId}",
				"UpdateSceneStatus",
				entityName: "Scene",
				entityId: sceneId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetReadyAsync(
			long sceneServerId,
			long worldServerId,
			string sceneName,
			int sceneHandle,
			CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0 || worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid parameters: scene server ID, world server ID, and scene name are required.");
			}

			// Use CTE with SELECT FOR UPDATE SKIP LOCKED to prevent race conditions
			// Only one scene server can claim a specific scene at a time
			var result = await ExecuteSqlAsync(
				$@"WITH claimable_scene AS (
				SELECT id FROM {TableName}
				WHERE world_server_id = {worldServerId} 
					AND scene_name = {sceneName} 
					AND scene_status = {(int)SceneStatus.Loading}
				FOR UPDATE SKIP LOCKED
				LIMIT 1
			)
			UPDATE {TableName} 
			SET scene_status = {(int)SceneStatus.Ready}, 
				scene_server_id = {sceneServerId}, 
				scene_handle = {sceneHandle}
			WHERE id IN (SELECT id FROM claimable_scene)",
				"SetSceneReady",
				entityName: "Scene",
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(int sceneHandle, int characterCount, CancellationToken cancellationToken = default)
		{
			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName}
					SET character_count = {characterCount} 
					WHERE scene_handle = {sceneHandle}",
				"PulseScene",
				entityName: "Scene",
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			return await ExecuteSqlAsync(
				$"DELETE FROM {TableName} WHERE scene_server_id = {sceneServerId}",
				"DeleteBySceneServer",
				entityName: "Scene",
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			return await ExecuteSqlAsync(
				$"DELETE FROM {TableName} WHERE world_server_id = {worldServerId}",
				"DeleteByWorldServer",
				entityName: "Scene",
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} 
					WHERE scene_server_id = {sceneServerId} AND scene_handle = {sceneHandle}",
				"DeleteByHandle",
				entityName: "Scene",
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> GetCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<SceneData>.Failure("VALIDATION_ERROR", "Invalid character ID.");
			}

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var type = (int)sceneType;
				var scene = await GetCharacterInstanceQuery(dbContext, characterId, type, ct).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", $"character {characterId}, type {sceneType}");
				}

				return MapEntityToDto(scene);
			}, "GetCharacterInstance", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> GetInstanceByIdAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<SceneData>.Failure("VALIDATION_ERROR", "Invalid scene ID.");
			}

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var scene = await GetInstanceByIdQuery(dbContext, sceneId, ct).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}

				return MapEntityToDto(scene);
			}, "GetSceneById", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<SceneData>>> GetAvailableScenesAsync(
			long worldServerId,
			string sceneName,
			int maxClients,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<List<SceneData>>.Failure("VALIDATION_ERROR", "Invalid parameters: world server ID and scene name are required.");
			}

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await GetAvailableScenesQuery(dbContext, worldServerId, sceneName, maxClients, readyStatus, ct).ConfigureAwait(false);

				return scenes.Select(MapEntityToDto).ToList();
			}, "GetAvailableScenes", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<SceneData>>> GetReadyScenesAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<List<SceneData>>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			return await ExecuteSqlAsync(async (dbContext, ct) =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await GetReadyScenesQuery(dbContext, worldServerId, readyStatus, ct).ConfigureAwait(false);

				return scenes.Select(MapEntityToDto).ToList();
			}, "GetReadyScenes", cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps SceneEntity to SceneData DTO.
		/// </summary>
		/// <param name="entity">Scene entity from database.</param>
		/// <returns>Scene data DTO.</returns>
		private SceneData MapEntityToDto(SceneEntity entity)
		{
			return new SceneData(
				id: entity.ID,
				sceneServerID: entity.SceneServerID,
				worldServerID: entity.WorldServerID,
				sceneName: entity.SceneName,
				sceneHandle: entity.SceneHandle,
				sceneStatus: entity.SceneStatus,
				sceneType: entity.SceneType,
				characterID: entity.CharacterID,
				characterCount: entity.CharacterCount,
				timeCreated: entity.TimeCreated
			);
		}
	}
}