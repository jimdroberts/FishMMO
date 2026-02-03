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
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<SceneEntity?>> getCharacterInstanceQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int sceneType, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.FirstOrDefault(s => s.CharacterID == characterId && s.SceneType == sceneType));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving scene by ID (hot path for scene loading).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<SceneEntity?>> fetchByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long sceneId, CancellationToken ct) =>
				context.Scenes
					.AsNoTracking()
					.FirstOrDefault(s => s.ID == sceneId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving available scenes (hot path for scene matchmaking).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, string, int, int, CancellationToken, Task<List<SceneEntity>>> fetchAvailableQuery =
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
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<List<SceneEntity>>> fetchReadyQuery =
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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var entity = new SceneEntity
				{
					WorldServerID = worldServerId,
					SceneName = sceneName,
					SceneType = (int)sceneType,
					SceneStatus = (int)SceneStatus.Pending,
					CharacterID = characterId,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.Scenes.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<long>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<long>.Failure("DATABASE_ERROR", "Failed to enqueue scene.", isTransient: true);
			}

			return DatabaseResult<long>.Success(result.Data.ID);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> DequeueAsync(CancellationToken cancellationToken = default)
		{
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"WITH scene_to_update AS (
						SELECT id FROM {TableName}
						WHERE scene_status = {{0}}
						ORDER BY time_created, id
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

				var entity = await dbContext.Scenes
						.FromSqlRaw(sql, pendingStatus, loadingStatus)
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

				return entity != null ? (SceneData?)MapEntityToDto(entity) : null;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

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

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET scene_status = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { (int)status, sceneId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			// Still performs a best-effort "already ready" check to be robust to in-call retries.
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var claimedScenes = await dbContext.Scenes
					.FromSqlRaw($@"
							WITH claimable_scene AS (
								SELECT id FROM {TableName}
								WHERE world_server_id = {{0}}
									AND scene_name = {{1}}
									AND scene_status = {{2}}
								ORDER BY time_created, id
								FOR UPDATE SKIP LOCKED
								LIMIT 1
							)
							UPDATE {TableName}
							SET scene_status = {{3}},
								scene_server_id = {{4}},
								scene_handle = {{5}}
							FROM claimable_scene
							WHERE {TableName}.id = claimable_scene.id
							RETURNING {TableName}.id",
						worldServerId,
						sceneName,
						(int)SceneStatus.Loading,
						(int)SceneStatus.Ready,
						sceneServerId,
						sceneHandle)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (claimedScenes.Count > 0)
				{
					return (long?)claimedScenes[0].ID;
				}

				// Fallback: check if already ready (idempotency on retry)
				var alreadyReadyId = await dbContext.Scenes
					.AsNoTracking()
					.Where(s =>
						s.WorldServerID == worldServerId
						&& s.SceneName == sceneName
						&& s.SceneStatus == (int)SceneStatus.Ready
						&& s.SceneServerID == sceneServerId
						&& s.SceneHandle == sceneHandle)
					.OrderBy(s => s.TimeCreated)
					.ThenBy(s => s.ID)
					.Select(s => (long?)s.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				return alreadyReadyId;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? (result.Data.HasValue ? DatabaseResult.Success() : DatabaseResult.Failure("SCENE_NOT_CLAIMABLE", "No matching loading scene could be claimed."))
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(int sceneHandle, int characterCount, CancellationToken cancellationToken = default)
		{
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET character_count = {{0}}
					WHERE scene_handle = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, sceneHandle },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", $"handle {sceneHandle}");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE scene_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE world_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE scene_server_id = {{0}} AND scene_handle = {{1}}";
				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneServerId, sceneHandle },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<SceneData>.Failure("VALIDATION_ERROR", "Invalid character ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var type = (int)sceneType;
				var scene = await getCharacterInstanceQuery(dbContext, characterId, type, cancellationToken).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", $"character {characterId}, type {sceneType}");
				}

				return MapEntityToDto(scene);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<SceneData>.Success(result.Data)
				: DatabaseResult<SceneData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<SceneData>.Failure("VALIDATION_ERROR", "Invalid scene ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var scene = await fetchByIdQuery(dbContext, sceneId, cancellationToken).ConfigureAwait(false);

				if (scene == null)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}

				return MapEntityToDto(scene);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<SceneData>.Success(result.Data)
				: DatabaseResult<SceneData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchAvailableAsync(
			long worldServerId,
			string sceneName,
			int maxClients,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure("VALIDATION_ERROR", "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchAvailableQuery(dbContext, worldServerId, sceneName, maxClients, readyStatus, cancellationToken).ConfigureAwait(false);

				return scenes.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<IReadOnlyList<SceneData>>.Success(result.Data)
				: DatabaseResult<IReadOnlyList<SceneData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchManyAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchReadyQuery(dbContext, worldServerId, readyStatus, cancellationToken).ConfigureAwait(false);

				return scenes.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<IReadOnlyList<SceneData>>.Success(result.Data)
				: DatabaseResult<IReadOnlyList<SceneData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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