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
		private static readonly Func<NpgsqlDbContext, long, string, int, int, IAsyncEnumerable<SceneEntity>> fetchAvailableQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, string sceneName, int maxClients, int readyStatus) =>
				context.Scenes
						.AsNoTracking()
						.Where(s =>
							s.WorldServerID == worldServerId &&
							s.SceneName == sceneName &&
							s.CharacterCount < maxClients &&
							s.SceneStatus == readyStatus));

		/// <summary>
		/// Compiled query for retrieving ready scenes (hot path for scene server queries).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, IAsyncEnumerable<SceneEntity>> fetchReadyQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long worldServerId, int readyStatus) =>
				context.Scenes
					.AsNoTracking()
					.Where(s => s.WorldServerID == worldServerId && s.SceneStatus == readyStatus));

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
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
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
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.DatabaseError, "Failed to enqueue scene.", isTransient: true);
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

				var entity = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { pendingStatus, loadingStatus },
					reader => new SceneEntity
					{
						ID = reader.GetInt64(0),
						WorldServerID = reader.GetInt64(1),
						SceneServerID = reader.GetInt64(2),
						SceneName = reader.GetString(3),
						SceneHandle = reader.GetInt32(4),
						SceneStatus = reader.GetInt32(5),
						SceneType = reader.GetInt32(6),
						CharacterID = reader.GetInt64(7),
						CharacterCount = reader.GetInt32(8),
						TimeCreated = reader.GetDateTime(9),
					},
					cancellationToken).ConfigureAwait(false);

				return entity != null ? (SceneData?)MapEntityToDto(entity) : null;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// Convert null result to business logic failure (not an exception case)
			if (result.IsSuccess && result.Data == null)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.NotFound, "No pending scenes available.");
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
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
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
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetReadyAsync(
			long sceneId,
			long sceneServerId,
			long worldServerId,
			string sceneName,
			int sceneHandle,
			CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0 || sceneServerId <= 0 || worldServerId <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: scene ID, scene server ID, world server ID, and scene name are required.");
			}

			// Still performs a best-effort "already ready" check to be robust to in-call retries.
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				/* Addressed by ID: this is the row the caller dequeued and loaded, and only that
				 * row may be told where the resulting scene instance lives. Selecting "the oldest
				 * loading row with this name" instead meant two concurrent loads of the same
				 * scene could each stamp their server and handle onto the other's row — which,
				 * for an instanced scene, hands a character the instance created for somebody
				 * else, because character_id stays with the row.
				 *
				 * scene_name is still matched as a consistency check so a caller that passes a
				 * mismatched ID fails rather than silently rewriting an unrelated row. */
				var claimSql = $@"WITH claimable_scene AS (
						SELECT id FROM {TableName}
						WHERE id = {{0}}
							AND world_server_id = {{1}}
							AND scene_name = {{2}}
							AND scene_status = {{3}}
						FOR UPDATE
					)
					UPDATE {TableName}
					SET scene_status = {{4}},
						scene_server_id = {{5}},
						scene_handle = {{6}}
					FROM claimable_scene
					WHERE {TableName}.id = claimable_scene.id
					RETURNING {TableName}.id";

				var claimedId = await ExecuteReturningOrDefaultAsync(
					dbContext,
					claimSql,
					new object[] { sceneId, worldServerId, sceneName, (int)SceneStatus.Loading, (int)SceneStatus.Ready, sceneServerId, sceneHandle },
					reader => reader.GetInt64(0),
					cancellationToken).ConfigureAwait(false);

				if (claimedId > 0)
				{
					return (long?)claimedId;
				}

				// Fallback: check if already ready (idempotency on retry)
				var alreadyReadyId = await dbContext.Scenes
					.AsNoTracking()
					.Where(s =>
						s.ID == sceneId
						&& s.WorldServerID == worldServerId
						&& s.SceneName == sceneName
						&& s.SceneStatus == (int)SceneStatus.Ready
						&& s.SceneServerID == sceneServerId
						&& s.SceneHandle == sceneHandle)
					.Select(s => (long?)s.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				return alreadyReadyId;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? (result.Data.HasValue ? DatabaseResult.Success() : DatabaseResult.Failure(DatabaseErrorCodes.NotFound, $"Scene {sceneId} could not be claimed as ready."))
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(long sceneId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Addressed by row id: a scene handle is process-local. See ISceneService.PulseAsync.
				var sql = $@"UPDATE {TableName}
					SET character_count = {{0}}
					WHERE id = {{1}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterCount, sceneId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", sceneId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
			}

			// Deliberately not throwing on zero rows: both callers are deleting a scene they have
			// already stopped serving, and a row someone else reaped first is the same outcome.
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE scene_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE world_server_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene server ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE scene_server_id = {{0}} AND scene_handle = {{1}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneServerId, sceneHandle },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Scene", $"SceneServerId={sceneServerId}, Handle={sceneHandle}");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchCharacterInstanceAsync(
			long characterId,
			SceneType sceneType,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
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
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> FetchAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<SceneData>.Failure(DatabaseErrorCodes.ValidationError, "Invalid scene ID.");
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
			return result;
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
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid parameters: world server ID and scene name are required.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchAvailableQuery(dbContext, worldServerId, sceneName, maxClients, readyStatus).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneData>>> FetchManyAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<IReadOnlyList<SceneData>>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var readyStatus = (int)SceneStatus.Ready;
				var scenes = await fetchReadyQuery(dbContext, worldServerId, readyStatus).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				IReadOnlyList<SceneData> data = scenes.Select(MapEntityToDto).ToList();
				return data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> PulseBatchAsync(
			List<(long sceneId, int characterCount)> pulses,
			int maxBatchSize = 1000,
			CancellationToken cancellationToken = default)
		{
			if (pulses == null || pulses.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			if (maxBatchSize < 500) maxBatchSize = 500;
			else if (maxBatchSize > 2500) maxBatchSize = 2500;

			int totalRowsAffected = 0;

			for (int offset = 0; offset < pulses.Count; offset += maxBatchSize)
			{
				var batchCount = Math.Min(maxBatchSize, pulses.Count - offset);

				// Build parallel arrays for PostgreSQL unnest.
				var sceneIds = new long[batchCount];
				var counts = new int[batchCount];
				for (int i = 0; i < batchCount; i++)
				{
					var (sceneId, count) = pulses[offset + i];
					sceneIds[i] = sceneId;
					counts[i] = count;
				}

				var result = await ExecuteWriteAsync(async dbContext =>
				{
					// Use unnest to efficiently join an array of values into an UPDATE.
					// Addressed by row id: a scene handle is process-local.
					var sql = $@"UPDATE {TableName} AS t
						SET character_count = batch.new_count
						FROM unnest({{0}}::bigint[], {{1}}::int[]) AS batch(scene_id, new_count)
						WHERE t.id = batch.scene_id";

					return await dbContext.Database.ExecuteSqlRawAsync(
						sql,
						new object[] { sceneIds, counts },
						cancellationToken).ConfigureAwait(false);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
				}

				totalRowsAffected += result.Data;
			}

			return DatabaseResult<int>.Success(totalRowsAffected);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteStaleUnreadyAsync(
			long worldServerId,
			DateTime olderThanUtc,
			int maxRows = 256,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			if (maxRows < 1)
			{
				maxRows = 1;
			}
			else if (maxRows > 4096)
			{
				maxRows = 4096;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// SKIP LOCKED so a row a scene server is concurrently dequeuing is left to it
				// rather than deleted out from under an in-flight load.
				var sql = $@"WITH stale AS (
						SELECT id FROM {TableName}
						WHERE world_server_id = {{0}}
							AND scene_status <> {{1}}
							AND time_created < {{2}}
						ORDER BY time_created, id
						FOR UPDATE SKIP LOCKED
						LIMIT {{3}}
					)
					DELETE FROM {TableName}
					USING stale
					WHERE {TableName}.id = stale.id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId, (int)SceneStatus.Ready, olderThanUtc, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByStaleSceneServersAsync(
			long worldServerId,
			DateTime pulseOlderThanUtc,
			int maxRows = 256,
			CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid world server ID.");
			}

			if (maxRows < 1)
			{
				maxRows = 1;
			}
			else if (maxRows > 4096)
			{
				maxRows = 4096;
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* NOT EXISTS covers both halves of "the host is gone": a scene server that
				 * deregistered (no row) and one that crashed (row present, pulse stopped). A
				 * plain join against scene_servers would silently keep the first case. */
				var sql = $@"WITH orphaned AS (
						SELECT s.id FROM {TableName} AS s
						WHERE s.world_server_id = {{0}}
							AND s.scene_server_id <> 0
							AND NOT EXISTS (
								SELECT 1 FROM scene_servers AS ss
								WHERE ss.id = s.scene_server_id
									AND ss.last_pulse >= {{1}}
							)
						ORDER BY s.id
						FOR UPDATE SKIP LOCKED
						LIMIT {{2}}
					)
					DELETE FROM {TableName}
					USING orphaned
					WHERE {TableName}.id = orphaned.id";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerId, pulseOlderThanUtc, maxRows },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
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