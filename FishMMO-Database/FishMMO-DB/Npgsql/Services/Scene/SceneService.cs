using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// Exception Handling Strategy:
	/// All methods implement comprehensive exception handling that converts database exceptions
	/// into DatabaseResult failures with appropriate error codes and safe messages:
	/// </para>
	/// <list type="bullet">
	/// <item><description>OperationCanceledException → DatabaseTimeoutException (transient)</description></item>
	/// <item><description>PostgresException (23505) → DatabaseConstraintException (Unique)</description></item>
	/// <item><description>PostgresException (23503) → DatabaseConstraintException (ForeignKey)</description></item>
	/// <item><description>NpgsqlException → DatabaseConnectionException (transient)</description></item>
	/// <item><description>DbUpdateException → DatabaseQueryException</description></item>
	/// <item><description>Exception → DatabaseQueryException (fallback)</description></item>
	/// </list>
	/// <para>
	/// Update/Delete operations returning 0 rows affected are treated as DatabaseEntityNotFoundException.
	/// Get methods returning null entities are also treated as DatabaseEntityNotFoundException,
	/// except for DequeueAsync where null is a valid result indicating no pending scenes.
	/// </para>
	/// </remarks>
	public sealed class SceneService : ISceneService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of SceneService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var sceneId = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<SceneEntity>();
					var sceneTypeInt = (int)sceneType;
					var sceneStatusInt = (int)SceneStatus.Pending;

					// Use CURRENT_TIMESTAMP from database server for consistency
					var result = await dbContext.Scenes
						.FromSqlInterpolated($@"
					INSERT INTO {tableName} 
					   (world_server_id, scene_name, scene_type, scene_status, character_id, time_created)
					VALUES ({worldServerId}, {sceneName}, {sceneTypeInt}, {sceneStatusInt}, {characterId}, CURRENT_TIMESTAMP)
					RETURNING id, world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);

					return result?.ID ?? 0;
				});

				if (sceneId <= 0)
				{
					return DatabaseResult<long>.Failure("ENQUEUE_FAILED", "Failed to enqueue scene.");
				}

				return DatabaseResult<long>.Success(sceneId);
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("EnqueueScene", 30, ex);
				return DatabaseResult<long>.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_unique_constraint",
					"Scene is already enqueued.",
					pgEx);
				return DatabaseResult<long>.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Referenced server or character does not exist.",
					pgEx);
				return DatabaseResult<long>.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<long>.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"EnqueueScene",
					"Failed to enqueue scene.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult<long>.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"EnqueueScene",
					"An unexpected error occurred while enqueuing scene.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<long>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> DequeueAsync(CancellationToken cancellationToken = default)
		{
			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var entity = await strategy.ExecuteAsync(async () =>
				{
					// Atomically dequeue next pending scene with FOR UPDATE SKIP LOCKED
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Scenes
						.FromSqlInterpolated($@"
					UPDATE {tableName}
					SET scene_status = {(int)SceneStatus.Loading}
					WHERE id = (
						SELECT id FROM {tableName}
						WHERE scene_status = {(int)SceneStatus.Pending}
						ORDER BY time_created
						LIMIT 1
						FOR UPDATE SKIP LOCKED
					)
					RETURNING id, world_server_id, scene_server_id, scene_name, scene_handle, scene_status, scene_type, character_id, character_count, time_created")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);
				});

				// Null is valid here - no pending scenes available
				if (entity == null)
				{
					return DatabaseResult<SceneData>.Failure("NO_PENDING_SCENES", "No pending scenes available.");
				}

				return DatabaseResult<SceneData>.Success(MapEntityToDto(entity));
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("DequeueScene", 30, ex);
				return DatabaseResult<SceneData>.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during dequeue.",
					pgEx);
				return DatabaseResult<SceneData>.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during dequeue.",
					pgEx);
				return DatabaseResult<SceneData>.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<SceneData>.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"DequeueScene",
					"Failed to dequeue scene.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult<SceneData>.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"DequeueScene",
					"An unexpected error occurred while dequeuing scene.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<SceneData>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateStatusAsync(long sceneId, SceneStatus status, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid scene ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
					SET scene_status = {(int)status} 
					WHERE id = {sceneId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"ID {sceneId}");
					return DatabaseResult.FromException(notFoundEx);
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("UpdateSceneStatus", 30, ex);
				return DatabaseResult.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during status update.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during status update.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"UpdateSceneStatus",
					"Failed to update scene status.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"UpdateSceneStatus",
					"An unexpected error occurred while updating scene status.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult.FromException(queryEx);
			}
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

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
					SET scene_server_id = {sceneServerId}, 
						scene_handle = {sceneHandle}, 
						character_count = 0, 
						scene_status = {(int)SceneStatus.Ready} 
					WHERE world_server_id = {worldServerId} 
						AND scene_name = {sceneName} 
						AND scene_status = {(int)SceneStatus.Loading}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"world server {worldServerId}, scene {sceneName} in Loading status");
					return DatabaseResult.FromException(notFoundEx);
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("SetSceneReady", 30, ex);
				return DatabaseResult.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during set ready.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during set ready.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"SetSceneReady",
					"Failed to set scene ready.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"SetSceneReady",
					"An unexpected error occurred while setting scene ready.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PulseAsync(int sceneHandle, int characterCount, CancellationToken cancellationToken = default)
		{
			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
					SET character_count = {characterCount} 
					WHERE scene_handle = {sceneHandle}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"handle {sceneHandle}");
					return DatabaseResult.FromException(notFoundEx);
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("PulseScene", 30, ex);
				return DatabaseResult.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during pulse.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during pulse.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"PulseScene",
					"Failed to pulse scene.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"PulseScene",
					"An unexpected error occurred while pulsing scene.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteBySceneServerAsync(long sceneServerId, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsDeleted = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE scene_server_id = {sceneServerId}",
						cancellationToken);
				});

				// Idempotent: 0 rows is success
				return DatabaseResult<int>.Success(rowsDeleted);
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("DeleteScenesBySceneServer", 30, ex);
				return DatabaseResult<int>.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during delete.",
					pgEx);
				return DatabaseResult<int>.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during delete.",
					pgEx);
				return DatabaseResult<int>.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<int>.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteScenesBySceneServer",
					"Failed to delete scenes by scene server.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult<int>.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteScenesBySceneServer",
					"An unexpected error occurred while deleting scenes by scene server.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<int>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteByWorldServerAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsDeleted = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE world_server_id = {worldServerId}",
						cancellationToken);
				});

				// Idempotent: 0 rows is success
				return DatabaseResult<int>.Success(rowsDeleted);
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("DeleteScenesByWorldServer", 30, ex);
				return DatabaseResult<int>.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during delete.",
					pgEx);
				return DatabaseResult<int>.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during delete.",
					pgEx);
				return DatabaseResult<int>.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<int>.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteScenesByWorldServer",
					"Failed to delete scenes by world server.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult<int>.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteScenesByWorldServer",
					"An unexpected error occurred while deleting scenes by world server.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<int>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteByHandleAsync(long sceneServerId, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (sceneServerId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid scene server ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<SceneEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} 
					WHERE scene_server_id = {sceneServerId} AND scene_handle = {sceneHandle}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"scene server {sceneServerId}, handle {sceneHandle}");
					return DatabaseResult.FromException(notFoundEx);
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("DeleteSceneByHandle", 30, ex);
				return DatabaseResult.FromException(timeoutEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23505")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.Unique,
					"scene_constraint",
					"Unique constraint violation during delete.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (PostgresException pgEx) when (pgEx.SqlState == "23503")
			{
				var constraintEx = new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"scene_foreign_key",
					"Foreign key constraint violation during delete.",
					pgEx);
				return DatabaseResult.FromException(constraintEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult.FromException(connectionEx);
			}
			catch (DbUpdateException dbEx)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteSceneByHandle",
					"Failed to delete scene by handle.",
					$"Database update error: {dbEx.Message}",
					false,
					null,
					dbEx);
				return DatabaseResult.FromException(queryEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"DeleteSceneByHandle",
					"An unexpected error occurred while deleting scene by handle.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult.FromException(queryEx);
			}
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

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var type = (int)sceneType;
				var scene = await context.Scenes
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.CharacterID == characterId && s.SceneType == type, cancellationToken);

				if (scene == null)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"character {characterId}, type {sceneType}");
					return DatabaseResult<SceneData>.FromException(notFoundEx);
				}

				return DatabaseResult<SceneData>.Success(MapEntityToDto(scene));
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("GetCharacterInstance", 30, ex);
				return DatabaseResult<SceneData>.FromException(timeoutEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<SceneData>.FromException(connectionEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"GetCharacterInstance",
					"An unexpected error occurred while retrieving character instance.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<SceneData>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneData>> GetInstanceByIdAsync(long sceneId, CancellationToken cancellationToken = default)
		{
			if (sceneId <= 0)
			{
				return DatabaseResult<SceneData>.Failure("VALIDATION_ERROR", "Invalid scene ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var scene = await context.Scenes
					.AsNoTracking()
					.FirstOrDefaultAsync(s => s.ID == sceneId, cancellationToken);

				if (scene == null)
				{
					var notFoundEx = new DatabaseEntityNotFoundException("Scene", $"ID {sceneId}");
					return DatabaseResult<SceneData>.FromException(notFoundEx);
				}

				return DatabaseResult<SceneData>.Success(MapEntityToDto(scene));
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("GetSceneById", 30, ex);
				return DatabaseResult<SceneData>.FromException(timeoutEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<SceneData>.FromException(connectionEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"GetSceneById",
					"An unexpected error occurred while retrieving scene by ID.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<SceneData>.FromException(queryEx);
			}
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

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var scenes = await context.Scenes
					.AsNoTracking()
					.Where(s =>
						s.WorldServerID == worldServerId &&
						s.SceneName == sceneName &&
						s.CharacterCount < maxClients &&
						s.SceneStatus == (int)SceneStatus.Ready)
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<SceneData>>.Success(scenes.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("GetAvailableScenes", 30, ex);
				return DatabaseResult<List<SceneData>>.FromException(timeoutEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<List<SceneData>>.FromException(connectionEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"GetAvailableScenes",
					"An unexpected error occurred while retrieving available scenes.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<List<SceneData>>.FromException(queryEx);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<SceneData>>> GetReadyScenesAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			if (worldServerId <= 0)
			{
				return DatabaseResult<List<SceneData>>.Failure("VALIDATION_ERROR", "Invalid world server ID.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var scenes = await context.Scenes
					.AsNoTracking()
					.Where(s => s.WorldServerID == worldServerId && s.SceneStatus == (int)SceneStatus.Ready)
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<SceneData>>.Success(scenes.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				var timeoutEx = new DatabaseTimeoutException("GetReadyScenes", 30, ex);
				return DatabaseResult<List<SceneData>>.FromException(timeoutEx);
			}
			catch (NpgsqlException npgsqlEx)
			{
				var connectionEx = new DatabaseConnectionException("database", npgsqlEx);
				return DatabaseResult<List<SceneData>>.FromException(connectionEx);
			}
			catch (Exception ex)
			{
				var queryEx = new DatabaseQueryException(
					"GetReadyScenes",
					"An unexpected error occurred while retrieving ready scenes.",
					$"Unexpected error: {ex.Message}",
					false,
					null,
					ex);
				return DatabaseResult<List<SceneData>>.FromException(queryEx);
			}
		}

		/// <summary>
		/// Maps SceneEntity to SceneData DTO.
		/// </summary>
		/// <param name="entity">Scene entity from database.</param>
		/// <returns>Scene data DTO.</returns>
		private SceneData MapEntityToDto(SceneEntity entity)
		{
			return new SceneData
			{
				ID = entity.ID,
				WorldServerID = entity.WorldServerID,
				SceneServerID = entity.SceneServerID,
				SceneName = entity.SceneName,
				SceneHandle = entity.SceneHandle,
				SceneType = entity.SceneType,
				SceneStatus = entity.SceneStatus,
				CharacterID = entity.CharacterID,
				CharacterCount = entity.CharacterCount,
				TimeCreated = entity.TimeCreated
			};
		}
	}
}