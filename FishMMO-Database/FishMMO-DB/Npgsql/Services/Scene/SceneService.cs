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

			return await ExecuteAsync<long>(async (dbContext, ct) =>
			{
				var sceneTypeInt = (int)sceneType;
				var sceneStatusInt = (int)SceneStatus.Pending;
				var sql = $@"INSERT INTO {TableName}
					(world_server_id, scene_name, scene_type, scene_status, character_id, time_created)
				VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, CURRENT_TIMESTAMP)
				RETURNING id";

				// Use CURRENT_TIMESTAMP from database server for consistency
				// Optimized: RETURNING only id for better performance and reduced memory overhead
				var result = await dbContext.Scenes
					.FromSqlRaw(sql, worldServerId, sceneName, sceneTypeInt, sceneStatusInt, characterId)
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
			var result = await ExecuteAsync<SceneData?>(async (dbContext, ct) =>
			{
				var sql = $@"WITH scene_to_update AS (
					SELECT id FROM {TableName}
					WHERE scene_status = {{0}}
					ORDER BY time_created
					FOR UPDATE SKIP LOCKED
					LIMIT 1
				)
				UPDATE {TableName}
				SET scene_status = {{1}}
				FROM scene_to_update
				WHERE {TableName}.id = scene_to_update.id
				RETURNING {TableName}.id, {TableName}.world_server_id, {TableName}.scene_server_id, {TableName}.scene_name, {TableName}.scene_handle, {TableName}.scene_status, {TableName}.scene_type, {TableName}.character_id, {TableName}.character_count, {TableName}.time_created";

				var pendingStatus = (int)SceneStatus.Pending;
				var loadingStatus = (int)SceneStatus.Loading;

				// Atomically dequeue next pending scene with CTE and FOR UPDATE SKIP LOCKED
				// CTE ensures the row is locked BEFORE the UPDATE, preventing race conditions
				var entity = await dbContext.Scenes
					.FromSqlRaw(sql, pendingStatus, loadingStatus)
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

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName}
					SET scene_status = {{0}}
					WHERE id = {{1}}",
				"UpdateSceneStatus",
				new object[] { (int)status, sceneId },
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

			// Retry-idempotent: if the claim already succeeded in a previous attempt and a transient error
			// caused a retry, the UPDATE may affect 0 rows. In that case, treat "already ready" as success.
			var result = await ExecuteAsync(async (dbContext, ct) =>
			{
				var sql = $@"WITH claimable_scene AS (
				SELECT id FROM {TableName}
				WHERE world_server_id = {{0}}
					AND scene_name = {{1}}
					AND scene_status = {{2}}
				FOR UPDATE SKIP LOCKED
				LIMIT 1
				)
				UPDATE {TableName}
				SET scene_status = {{3}},
					scene_server_id = {{4}},
					scene_handle = {{5}}
				WHERE id IN (SELECT id FROM claimable_scene)";

				var rows = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[]
					{
						worldServerId,
						sceneName,
						(int)SceneStatus.Loading,
						(int)SceneStatus.Ready,
						sceneServerId,
						sceneHandle
					},
					ct).ConfigureAwait(false);

				if (rows > 0)
				{
					return true;
				}

				// Idempotent success case: already ready with the same target values.
				var alreadyReady = await dbContext.Scenes
					.AsNoTracking()
					.AnyAsync(s =>
						s.WorldServerID == worldServerId
						&& s.SceneName == sceneName
						&& s.SceneStatus == (int)SceneStatus.Ready
						&& s.SceneServerID == sceneServerId
						&& s.SceneHandle == sceneHandle,
					ct).ConfigureAwait(false);

				if (alreadyReady)
				{
					return true;
				}

				return false;
			}, "SetSceneReady", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? (result.Data ? DatabaseResult.Success() : DatabaseResult.Failure("SCENE_NOT_CLAIMABLE", "No matching loading scene could be claimed."))
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(int sceneHandle, int characterCount, CancellationToken cancellationToken = default)
		{
			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName}
					SET character_count = {{0}}
					WHERE scene_handle = {{1}}",
				"PulseScene",
				new object[] { characterCount, sceneHandle },
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

			return await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE scene_server_id = {{0}}",
				"DeleteBySceneServer",
				new object[] { sceneServerId },
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

			return await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE world_server_id = {{0}}",
				"DeleteByWorldServer",
				new object[] { worldServerId },
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

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName}
					WHERE scene_server_id = {{0}} AND scene_handle = {{1}}",
				"DeleteByHandle",
				new object[] { sceneServerId, sceneHandle },
				entityName: "Scene",
				requireRowsAffected: false,
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

			return await ExecuteAsync(async (dbContext, ct) =>
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

			return await ExecuteAsync(async (dbContext, ct) =>
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

			return await ExecuteAsync(async (dbContext, ct) =>
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

			return await ExecuteAsync(async (dbContext, ct) =>
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