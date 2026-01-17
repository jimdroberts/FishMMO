using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class CharacterService : ICharacterService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Compiled query for GetCharacterAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterEntity?>> GetCharacterByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterEntity?)context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.ID == characterId && !c.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">The database context factory.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetCountAsync(string account, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account))
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid account");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var count = await dbContext.Characters
					.AsNoTracking()
					.Where(c => c.Account == account && !c.Deleted)
					.CountAsync(cancellationToken);
				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetCount", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while counting characters.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while counting characters.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetCount",
						"Failed to count characters due to a database error.",
						$"DbUpdateException in GetCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetCount",
						"An unexpected error occurred while counting characters.",
						$"Unexpected error in GetCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterOperationResult>> CreateCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(characterData.Name))
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.InvalidName);
			}

			if (string.IsNullOrWhiteSpace(characterData.Account))
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.DatabaseError);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var characterId = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();
					var nameLower = characterData.Name.ToLower();

					// Use CURRENT_TIMESTAMP from database server for consistency
					var result = await dbContext.Characters
						.FromSqlInterpolated($@"
						INSERT INTO {tableName} 
							(name, name_lowercase, account, selected, world_server_id, scene_name, scene_handle, 
							 bind_scene, bind_x, bind_y, bind_z, instance_id, instance_x, instance_y, instance_z, 
							 instance_rot_x, instance_rot_y, instance_rot_z, instance_rot_w, race_id, model_index, 
							 x, y, z, rot_x, rot_y, rot_z, rot_w, access_level, online, flags, 
							 time_created, last_saved, time_deleted, deleted)
						VALUES 
							({characterData.Name}, {nameLower}, {characterData.Account}, {characterData.Selected}, 
							 {characterData.WorldServerID}, {characterData.SceneName ?? string.Empty}, {characterData.SceneHandle}, 
							 {characterData.BindScene ?? string.Empty}, {characterData.BindX}, {characterData.BindY}, {characterData.BindZ}, 
							 {characterData.InstanceID}, {characterData.InstanceX}, {characterData.InstanceY}, {characterData.InstanceZ}, 
							 {characterData.InstanceRotX}, {characterData.InstanceRotY}, {characterData.InstanceRotZ}, {characterData.InstanceRotW}, 
							 {characterData.RaceID}, {characterData.ModelIndex}, 
							 {characterData.X}, {characterData.Y}, {characterData.Z}, 
							 {characterData.RotX}, {characterData.RotY}, {characterData.RotZ}, {characterData.RotW}, 
							 {characterData.AccessLevel}, {characterData.Online}, {characterData.Flags}, 
							 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, NULL, false)
						RETURNING id, name, name_lowercase, account, selected, world_server_id, scene_name, scene_handle, 
							 bind_scene, bind_x, bind_y, bind_z, instance_id, instance_x, instance_y, instance_z, 
							 instance_rot_x, instance_rot_y, instance_rot_z, instance_rot_w, race_id, model_index, 
							 x, y, z, rot_x, rot_y, rot_z, rot_w, access_level, online, flags, 
							 time_created, last_saved, time_deleted, deleted")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);

					return result?.ID ?? 0;
				});

				var result = characterId > 0 ? CharacterOperationResult.CharacterCreated : CharacterOperationResult.DatabaseError;
				return DatabaseResult<CharacterOperationResult>.Success(result);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterOperationResult>.FromException(
					new DatabaseTimeoutException("CreateCharacter", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				// Name already exists
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.NameAlreadyExists);
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterOperationResult>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_account_fkey",
						"Account does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterOperationResult>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("name_lowercase") == true ||
												   ex.InnerException?.Message?.Contains("duplicate key") == true)
			{
				// Unique constraint violation on character name
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.NameAlreadyExists);
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterOperationResult>.FromException(
					new DatabaseQueryException(
						"CreateCharacter",
						"Failed to create character due to a database error.",
						$"DbUpdateException in CreateCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterOperationResult>.FromException(
					new DatabaseQueryException(
						"CreateCharacter",
						"An unexpected error occurred while creating character.",
						$"Unexpected error in CreateCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (characterData.ID <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Use atomic UPDATE for thread safety
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						   SET name = {characterData.Name},
						       name_lowercase = {characterData.Name.ToLower()},
						       account = {characterData.Account},
						       selected = {characterData.Selected},
						       world_server_id = {characterData.WorldServerID},
						       scene_name = {characterData.SceneName ?? string.Empty},
						       scene_handle = {characterData.SceneHandle},
						       bind_scene = {characterData.BindScene ?? string.Empty},
						       bind_x = {characterData.BindX},
						       bind_y = {characterData.BindY},
						       bind_z = {characterData.BindZ},
						       instance_id = {characterData.InstanceID},
						       instance_x = {characterData.InstanceX},
						       instance_y = {characterData.InstanceY},
						       instance_z = {characterData.InstanceZ},
						       instance_rot_x = {characterData.InstanceRotX},
						       instance_rot_y = {characterData.InstanceRotY},
						       instance_rot_z = {characterData.InstanceRotZ},
						       instance_rot_w = {characterData.InstanceRotW},
						       race_id = {characterData.RaceID},
						       model_index = {characterData.ModelIndex},
						       x = {characterData.X},
						       y = {characterData.Y},
						       z = {characterData.Z},
						       rot_x = {characterData.RotX},
						       rot_y = {characterData.RotY},
						       rot_z = {characterData.RotZ},
						       rot_w = {characterData.RotW},
						       access_level = {characterData.AccessLevel},
						       online = {characterData.Online},
						       flags = {characterData.Flags},
						       last_saved = CURRENT_TIMESTAMP 
						   WHERE id = {characterData.ID} AND deleted = false",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterData.ID.ToString(),
							"Character not found or deleted"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveCharacter", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_name_lowercase_key",
						"Character name already exists.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_account_fkey",
						"Account does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacter",
						"Failed to save character due to a database error.",
						$"DbUpdateException in SaveCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacter",
						"An unexpected error occurred while saving character.",
						$"Unexpected error in SaveCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteCharacterAsync(long characterId, bool softDelete, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Use atomic UPDATE or DELETE for thread safety
					if (softDelete)
					{
						return await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"UPDATE {tableName} 
							   SET time_deleted = CURRENT_TIMESTAMP,
							       deleted = true,
							       online = false
							   WHERE id = {characterId}",
							cancellationToken);
					}
					else
					{
						return await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"DELETE FROM {tableName} 
							   WHERE id = {characterId}",
							cancellationToken);
					}
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterId.ToString(),
							"Character not found"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteCharacter", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while deleting character.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Cannot delete character due to foreign key constraint.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacter",
						"Failed to delete character due to a database error.",
						$"DbUpdateException in DeleteCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacter",
						"An unexpected error occurred while deleting character.",
						$"Unexpected error in DeleteCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterData?>.Success(null);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				// Use compiled query for hot path performance
				var entity = await GetCharacterByIdQuery(dbContext, characterId, cancellationToken);

				if (entity == null)
				{
					return DatabaseResult<CharacterData?>.Success(null);
				}

				return DatabaseResult<CharacterData?>.Success(MapEntityToData(entity));
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseTimeoutException("GetCharacter", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while getting character.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while getting character.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseQueryException(
						"GetCharacter",
						"Failed to get character due to a database error.",
						$"DbUpdateException in GetCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseQueryException(
						"GetCharacter",
						"An unexpected error occurred while getting character.",
						$"Unexpected error in GetCharacterAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> GetCharactersAsync(string account, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account))
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Success(Array.Empty<CharacterData>());
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var entities = await dbContext.Characters
					.AsNoTracking()
					.Where(c => c.Account == account && !c.Deleted)
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterData>>.Success(entities.Select(MapEntityToData).ToList());
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseTimeoutException("GetCharacters", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while getting characters.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while getting characters.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseQueryException(
						"GetCharacters",
						"Failed to get characters due to a database error.",
						$"DbUpdateException in GetCharactersAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.FromException(
					new DatabaseQueryException(
						"GetCharacters",
						"An unexpected error occurred while getting characters.",
						$"Unexpected error in GetCharactersAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<CharacterData?>.Success(null);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var nameLower = name.ToLower();
				var entity = await dbContext.Characters
					.AsNoTracking()
					.FirstOrDefaultAsync(c => c.NameLowercase == nameLower && !c.Deleted, cancellationToken);

				if (entity == null)
				{
					return DatabaseResult<CharacterData?>.Success(null);
				}

				return DatabaseResult<CharacterData?>.Success(MapEntityToData(entity));
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseTimeoutException("GetCharacterByName", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while getting character by name.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while getting character by name.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseQueryException(
						"GetCharacterByName",
						"Failed to get character by name due to a database error.",
						$"DbUpdateException in GetCharacterByNameAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterData?>.FromException(
					new DatabaseQueryException(
						"GetCharacterByName",
						"An unexpected error occurred while getting character by name.",
						$"Unexpected error in GetCharacterByNameAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetSelectedAsync(string account, long characterId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account) || characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid account or character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Atomic operation: Set selected = true only for the specified character
					// and selected = false for all other characters in one UPDATE statement
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET selected = (id = {characterId})
						WHERE account = {account} AND NOT deleted",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterId.ToString(),
							"No characters found for account"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SetSelected", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while setting selected character.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while setting selected character.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SetSelected",
						"Failed to set selected character due to a database error.",
						$"DbUpdateException in SetSelectedAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SetSelected",
						"An unexpected error occurred while setting selected character.",
						$"Unexpected error in SetSelectedAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetOnlineStatusAsync(long characterId, bool online, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Atomic update without loading entity
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET online = {online}, last_saved = CURRENT_TIMESTAMP 
						WHERE id = {characterId} AND NOT deleted",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterId.ToString(),
							"Character not found or deleted"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SetOnlineStatus", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while setting online status.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while setting online status.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SetOnlineStatus",
						"Failed to set online status due to a database error.",
						$"DbUpdateException in SetOnlineStatusAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SetOnlineStatus",
						"An unexpected error occurred while setting online status.",
						$"Unexpected error in SetOnlineStatusAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Atomic update without loading entity
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET x = {x}, y = {y}, z = {z}, 
							rot_x = {rotX}, rot_y = {rotY}, rot_z = {rotZ}, rot_w = {rotW}, 
							last_saved = CURRENT_TIMESTAMP 
						WHERE id = {characterId} AND NOT deleted",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterId.ToString(),
							"Character not found or deleted"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("UpdatePosition", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while updating position.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while updating position.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdatePosition",
						"Failed to update position due to a database error.",
						$"DbUpdateException in UpdatePositionAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdatePosition",
						"An unexpected error occurred while updating position.",
						$"Unexpected error in UpdatePositionAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateSceneAsync(long characterId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEntity>();

					// Use atomic UPDATE for thread safety
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET scene_name = {sceneName ?? string.Empty}, 
							scene_handle = {sceneHandle}, 
							last_saved = CURRENT_TIMESTAMP 
						WHERE id = {characterId} AND deleted = false",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"Character",
							characterId.ToString(),
							"Character not found or deleted"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("UpdateScene", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"characters_constraint",
						"Constraint violation while updating scene.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"characters_constraint",
						"Foreign key constraint issue while updating scene.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateScene",
						"Failed to update scene due to a database error.",
						$"DbUpdateException in UpdateSceneAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateScene",
						"An unexpected error occurred while updating scene.",
						$"Unexpected error in UpdateSceneAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <summary>
		/// Maps a CharacterEntity to CharacterData DTO.
		/// </summary>
		/// <param name="entity">The character entity.</param>
		/// <returns>The character data DTO.</returns>
		private static CharacterData MapEntityToData(CharacterEntity entity)
		{
			return new CharacterData
			{
				ID = entity.ID,
				Name = entity.Name,
				NameLowercase = entity.NameLowercase,
				Account = entity.Account,
				Selected = entity.Selected,
				WorldServerID = entity.WorldServerID,
				SceneName = entity.SceneName,
				SceneHandle = entity.SceneHandle,
				BindScene = entity.BindScene,
				BindX = entity.BindX,
				BindY = entity.BindY,
				BindZ = entity.BindZ,
				InstanceID = entity.InstanceID,
				InstanceX = entity.InstanceX,
				InstanceY = entity.InstanceY,
				InstanceZ = entity.InstanceZ,
				InstanceRotX = entity.InstanceRotX,
				InstanceRotY = entity.InstanceRotY,
				InstanceRotZ = entity.InstanceRotZ,
				InstanceRotW = entity.InstanceRotW,
				RaceID = entity.RaceID,
				ModelIndex = entity.ModelIndex,
				X = entity.X,
				Y = entity.Y,
				Z = entity.Z,
				RotX = entity.RotX,
				RotY = entity.RotY,
				RotZ = entity.RotZ,
				RotW = entity.RotW,
				AccessLevel = entity.AccessLevel,
				Online = entity.Online,
				Flags = entity.Flags,
				TimeCreated = entity.TimeCreated,
				LastSaved = entity.LastSaved,
				TimeDeleted = entity.TimeDeleted,
				Deleted = entity.Deleted
			};
		}
	}
}