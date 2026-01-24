using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterService : BaseService<CharacterEntity>, ICharacterService
	{
		/// <summary>
		/// Compiled query for GetCharacterAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterEntity?>> GetCharacterByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterEntity?)context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.ID == characterId));

		/// <summary>
		/// Compiled query for retrieving character by name (hot path for login/character selection).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<CharacterEntity?>> GetCharacterByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLower, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.NameLowercase == nameLower));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for counting characters by account (hot path for character creation validation).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<int>> GetCharacterCountByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
						.AsNoTracking()
						.Where(c => c.Account == account)
						.Count());

		/// <summary>
		/// Compiled query for retrieving all characters by account (hot path for character selection).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<List<CharacterEntity>>> GetCharactersByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.Account == account)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">The database context factory.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetCountAsync(string account, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account))
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid account");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				return await GetCharacterCountByAccountQuery(dbContext, account, ct).ConfigureAwait(false);
			}, "GetCount", cancellationToken).ConfigureAwait(false);
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

			return await ExecuteAsync<CharacterOperationResult>(async (dbContext, ct) =>
			{
				// Use CURRENT_TIMESTAMP from database server for consistency
				// Optimized: RETURNING only id for better performance and reduced memory overhead
				var result = await dbContext.Characters
					.FromSqlRaw($@"
					INSERT INTO {TableName} 
						(name, account, selected, world_server_id, scene_name, scene_handle, 
						 bind_scene, bind_x, bind_y, bind_z, instance_id, instance_x, instance_y, instance_z, 
						 instance_rot_x, instance_rot_y, instance_rot_z, instance_rot_w, race_id, model_index, 
						 x, y, z, rot_x, rot_y, rot_z, rot_w, access_level, online, flags, 
						 time_created, last_saved)
					VALUES 
						({{0}}, {{1}}, {{2}}, {{3}}, 
						 {{4}}, {{5}}, {{6}}, 
						 {{7}}, {{8}}, {{9}}, 
						 {{10}}, {{11}}, {{12}}, {{13}}, 
						 {{14}}, {{15}}, {{16}}, {{17}}, 
						 {{18}}, {{19}}, 
						 {{20}}, {{21}}, {{22}}, 
						 {{23}}, {{24}}, {{25}}, {{26}}, 
						 {{27}}, {{28}}, {{29}}, 
						 CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
					RETURNING id",
					characterData.Name,
					characterData.Account,
					characterData.Selected,
					characterData.WorldServerID,
					characterData.SceneName ?? string.Empty,
					characterData.SceneHandle,
					characterData.BindScene ?? string.Empty,
					characterData.BindX,
					characterData.BindY,
					characterData.BindZ,
					characterData.InstanceID,
					characterData.InstanceX,
					characterData.InstanceY,
					characterData.InstanceZ,
					characterData.InstanceRotX,
					characterData.InstanceRotY,
					characterData.InstanceRotZ,
					characterData.InstanceRotW,
					characterData.RaceID,
					characterData.ModelIndex,
					characterData.X,
					characterData.Y,
					characterData.Z,
					characterData.RotX,
					characterData.RotY,
					characterData.RotZ,
					characterData.RotW,
					characterData.AccessLevel,
					characterData.Online,
					characterData.Flags)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				var characterId = result?.ID ?? 0;
				return characterId > 0 ? CharacterOperationResult.CharacterCreated : CharacterOperationResult.DatabaseError;
			}, "CreateCharacter", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (characterData.ID <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			// Use optimistic concurrency control to prevent lost updates from concurrent saves
			// Check last_saved timestamp to ensure character hasn't been modified by another server
			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
				   SET name = {{0}},
				       account = {{1}},
				       selected = {{2}},
				       world_server_id = {{3}},
				       scene_name = {{4}},
				       scene_handle = {{5}},
				       bind_scene = {{6}},
				       bind_x = {{7}},
				       bind_y = {{8}},
				       bind_z = {{9}},
				       instance_id = {{10}},
				       instance_x = {{11}},
				       instance_y = {{12}},
				       instance_z = {{13}},
				       instance_rot_x = {{14}},
				       instance_rot_y = {{15}},
				       instance_rot_z = {{16}},
				       instance_rot_w = {{17}},
				       race_id = {{18}},
				       model_index = {{19}},
				       x = {{20}},
				       y = {{21}},
				       z = {{22}},
				       rot_x = {{23}},
				       rot_y = {{24}},
				       rot_z = {{25}},
				       rot_w = {{26}},
				       access_level = {{27}},
				       online = {{28}},
				       flags = {{29}},
				       last_saved = CURRENT_TIMESTAMP 
				   WHERE id = {{30}} AND last_saved = {{31}} AND deleted = FALSE",
				"SaveCharacter",
				new object[]
				{
					characterData.Name,
					characterData.Account,
					characterData.Selected,
					characterData.WorldServerID,
					characterData.SceneName ?? string.Empty,
					characterData.SceneHandle,
					characterData.BindScene ?? string.Empty,
					characterData.BindX,
					characterData.BindY,
					characterData.BindZ,
					characterData.InstanceID,
					characterData.InstanceX,
					characterData.InstanceY,
					characterData.InstanceZ,
					characterData.InstanceRotX,
					characterData.InstanceRotY,
					characterData.InstanceRotZ,
					characterData.InstanceRotW,
					characterData.RaceID,
					characterData.ModelIndex,
					characterData.X,
					characterData.Y,
					characterData.Z,
					characterData.RotX,
					characterData.RotY,
					characterData.RotZ,
					characterData.RotW,
					characterData.AccessLevel,
					characterData.Online,
					characterData.Flags,
					characterData.ID,
					characterData.LastSaved,
				},
				entityName: "Character",
				entityId: characterData.ID,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			// rowsAffected == 0 can mean either:
			// - Concurrency conflict (last_saved mismatch)
			// - Not found
			if (result.Data == 0)
			{
				var existsResult = await ExecuteAsync(async (dbContext, ct) =>
				{
					return await dbContext.Characters
						.AsNoTracking()
						.AnyAsync(c => c.ID == characterData.ID, ct).ConfigureAwait(false);
				}, "CheckCharacterExistsForSave", cancellationToken).ConfigureAwait(false);

				if (!existsResult.IsSuccess)
				{
					return DatabaseResult.Failure(existsResult.ErrorCode, existsResult.ErrorMessage, existsResult.IsTransient);
				}

				if (!existsResult.Data)
				{
					return DatabaseResult.Failure(
						"DB_NOT_FOUND",
						"Character not found.",
						isTransient: false);
				}

				return DatabaseResult.Failure(
					"CONCURRENCY_CONFLICT",
					"Character was modified by another server. Please reload and try again.",
					isTransient: false);
			}

			return DatabaseResult.Success();
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Soft Delete:</b></para>
		/// This performs an atomic soft delete rather than removing data.
		/// It renames the character (appending <c>_DELETED_{GUID}</c>) to free up the original name,
		/// sets <c>deleted=true</c>, and applies a soft-cascade update to all character-owned tables.
		/// Character guild/party memberships are hard-deleted (temporary state).
		/// </remarks>
		public async Task<DatabaseResult> DeleteCharacterAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var transactionResult = await ExecuteTransactionAsync(async (dbContext, transaction, ct) =>
			{
				// Lock the character row to prevent concurrent deletes/renames.
				var character = await dbContext.Characters
					.FromSqlRaw($@"SELECT * FROM {TableName} WHERE id = {{0}} FOR UPDATE", characterId)
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				// Idempotent: not found or already deleted.
				if (character == null || character.Deleted)
					return true;

				var guid = Guid.NewGuid().ToString("D");
				var suffix = $"_DELETED_{guid}";
				var deletedName = (character.Name ?? string.Empty) + suffix;

				// Rename + mark deleted.
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {TableName}
						SET name = {{1}},
							deleted = TRUE,
							time_deleted = CURRENT_TIMESTAMP
						WHERE id = {{0}} AND deleted = FALSE",
					new object[] { characterId, deletedName },
					ct).ConfigureAwait(false);

				// Hard-delete temporary membership state.
				var characterGuildsTable = dbContext.GetTableName<CharacterGuildEntity>();
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"DELETE FROM {characterGuildsTable} WHERE character_id = {{0}}",
					new object[] { characterId },
					ct).ConfigureAwait(false);

				var characterPartiesTable = dbContext.GetTableName<CharacterPartyEntity>();
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"DELETE FROM {characterPartiesTable} WHERE character_id = {{0}}",
					new object[] { characterId },
					ct).ConfigureAwait(false);

				// Soft-cascade all character-owned tables.
				static Task SoftDeleteTableAsync(NpgsqlDbContext ctx, string tableName, long id, CancellationToken token)
				{
					return ctx.Database.ExecuteSqlRawAsync(
						$@"UPDATE {tableName}
							SET deleted = TRUE,
								time_deleted = CURRENT_TIMESTAMP
							WHERE character_id = {{0}} AND deleted = FALSE",
						new object[] { id },
						token);
				}

				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAbilityEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterKnownAbilityEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAttributeEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAchievementEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterInventoryEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterEquipmentEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterBankEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterHotkeyEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterMailEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterItemCooldownEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterSkillEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterBuffEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetAttributeEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetBuffEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterFactionEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterQuestEntity>(), characterId, ct).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterFriendEntity>(), characterId, ct).ConfigureAwait(false);

				return true;
			}, "SoftDeleteCharacter", cancellationToken).ConfigureAwait(false);

			return transactionResult.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(transactionResult.ErrorCode, transactionResult.ErrorMessage, transactionResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				// Use compiled query for hot path performance
				var entity = await GetCharacterByIdQuery(dbContext, characterId, ct).ConfigureAwait(false);

				if (entity == null)
				{
					return (CharacterData?)null;
				}

				return MapEntityToData(entity);
			}, "GetCharacter", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> GetCharactersAsync(string account, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account))
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Failure("VALIDATION_ERROR", "Account name is required");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetCharactersByAccountQuery(dbContext, account, ct).ConfigureAwait(false);

				return (IReadOnlyList<CharacterData>)entities.Select(MapEntityToData).ToList();
			}, "GetCharacters", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", "Character name is required");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var nameLower = name.ToLower();
				var entity = await GetCharacterByNameQuery(dbContext, nameLower, ct).ConfigureAwait(false);

				if (entity == null)
				{
					return (CharacterData?)null;
				}

				return MapEntityToData(entity);
			}, "GetCharacterByName", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Concurrency Safety:</b></para>
		/// Uses explicit transaction with SELECT FOR UPDATE to prevent race conditions
		/// when multiple requests attempt to select different characters concurrently.
		/// Row-level locks are acquired for all characters belonging to the account,
		/// ensuring only one selection operation can proceed at a time per account.
		/// </remarks>
		public async Task<DatabaseResult> SetSelectedAsync(string account, long characterId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account) || characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid account or character ID");
			}

			// Use explicit transaction with row-level locking to prevent race conditions
			var transactionResult = await ExecuteTransactionAsync(async (dbContext, transaction, ct) =>
			{
				// Use CTE to combine SELECT FOR UPDATE and UPDATE into single atomic operation
				// This ensures the lock is held throughout the entire update
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH locked_chars AS (
					SELECT id FROM {TableName} 
					WHERE account = {{0}} AND deleted = FALSE
					FOR UPDATE
					)
					UPDATE {TableName} 
					SET selected = (id = {{1}})
					WHERE account = {{0}} AND deleted = FALSE
					AND id IN (SELECT id FROM locked_chars)",
					new object[] { account, characterId },
					ct).ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				return true;
			}, "SetSelected", cancellationToken).ConfigureAwait(false);

			if (transactionResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}
			else
			{
				return DatabaseResult.Failure(transactionResult.ErrorCode, transactionResult.ErrorMessage, transactionResult.IsTransient);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetOnlineStatusAsync(long characterId, bool online, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
				SET online = {{0}}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {{1}} AND deleted = FALSE",
				"SetOnlineStatus",
				new object[] { online, characterId },
				entityName: "Character",
				entityId: characterId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
				SET x = {{0}}, y = {{1}}, z = {{2}}, 
					rot_x = {{3}}, rot_y = {{4}}, rot_z = {{5}}, rot_w = {{6}}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {{7}} AND deleted = FALSE",
				"UpdatePosition",
				new object[] { x, y, z, rotX, rotY, rotZ, rotW, characterId },
				entityName: "Character",
				entityId: characterId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateSceneAsync(long characterId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
				SET scene_name = {{0}}, 
					scene_handle = {{1}}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {{2}} AND deleted = FALSE",
				"UpdateScene",
				new object[] { sceneName ?? string.Empty, sceneHandle, characterId },
				entityName: "Character",
				entityId: characterId,
				requireRowsAffected: true,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Maps a CharacterEntity to CharacterData DTO.
		/// </summary>
		/// <param name="entity">The character entity.</param>
		/// <returns>The character data DTO.</returns>
		private static CharacterData MapEntityToData(CharacterEntity entity)
		{
			return new CharacterData(
				entity.ID,
				entity.Name,
				entity.NameLowercase,
				entity.Account,
				entity.Selected,
				entity.WorldServerID,
				entity.SceneName,
				entity.SceneHandle,
				entity.BindScene,
				entity.BindX,
				entity.BindY,
				entity.BindZ,
				entity.InstanceID,
				entity.InstanceX,
				entity.InstanceY,
				entity.InstanceZ,
				entity.InstanceRotX,
				entity.InstanceRotY,
				entity.InstanceRotZ,
				entity.InstanceRotW,
				entity.RaceID,
				entity.ModelIndex,
				entity.X,
				entity.Y,
				entity.Z,
				entity.RotX,
				entity.RotY,
				entity.RotZ,
				entity.RotW,
				entity.AccessLevel,
				entity.Online,
				entity.Flags,
				entity.TimeCreated,
				entity.LastSaved
			);
		}
	}
}