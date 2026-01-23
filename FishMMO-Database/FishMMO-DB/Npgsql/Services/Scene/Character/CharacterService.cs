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
					.FirstOrDefault(c => c.ID == characterId && !c.Deleted));

		/// <summary>
		/// Compiled query for retrieving character by name (hot path for login/character selection).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<CharacterEntity?>> GetCharacterByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLower, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.NameLowercase == nameLower && !c.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for counting characters by account (hot path for character creation validation).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<int>> GetCharacterCountByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
						.AsNoTracking()
						.Where(c => c.Account == account && !c.Deleted)
						.Count());

		/// <summary>
		/// Compiled query for retrieving all characters by account (hot path for character selection).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<List<CharacterEntity>>> GetCharactersByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.Account == account && !c.Deleted)
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
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

			return await ExecuteSqlAsync<CharacterOperationResult>(async (dbContext, ct) =>
			{
				var nameLower = characterData.Name.ToLower();

				// Use CURRENT_TIMESTAMP from database server for consistency
				// Optimized: RETURNING only id for better performance and reduced memory overhead
				var result = await dbContext.Characters
					.FromSqlInterpolated($@"
					INSERT INTO {TableName} 
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
					RETURNING id")
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
			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
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
				   WHERE id = {characterData.ID} AND deleted = false AND last_saved = {characterData.LastSaved}",
				"SaveCharacter",
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
			// - Not found / deleted
			if (result.Data == 0)
			{
				var existsResult = await ExecuteSqlAsync(async (dbContext, ct) =>
				{
					return await dbContext.Characters
						.AsNoTracking()
						.AnyAsync(c => c.ID == characterData.ID && !c.Deleted, ct).ConfigureAwait(false);
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
		/// <para><b>Transaction Scope:</b></para>
		/// This operation uses an explicit transaction to ensure atomicity across multiple tables.
		/// When performing a hard delete, the following related data is automatically deleted via 
		/// CASCADE constraints configured in entity relationships:
		/// <list type="bullet">
		/// <item>Character abilities (character_ability table)</item>
		/// <item>Character attributes (character_attribute table)</item>
		/// <item>Character equipment (character_equipment table)</item>
		/// <item>Character inventory (character_inventory table)</item>
		/// <item>Character guild membership (character_guild table)</item>
		/// <item>Character party membership (character_party table)</item>
		/// <item>Character mail (character_mail table)</item>
		/// <item>Character quests (character_quest table)</item>
		/// <item>Character pets (character_pet table)</item>
		/// <item>Character buffs, hotkeys, skills, etc.</item>
		/// </list>
		/// Soft deletes only mark the character as deleted without removing related data.
		/// </remarks>
		public async Task<DatabaseResult> DeleteCharacterAsync(long characterId, bool softDelete, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			if (softDelete)
			{
				// Soft delete: mark as deleted and set offline
				var result = await ExecuteSqlAsync(
					$@"UPDATE {TableName} 
					SET time_deleted = CURRENT_TIMESTAMP, 
						deleted = true, 
						online = false 
					WHERE id = {characterId}",
					"DeleteCharacter",
					entityName: "Character",
					entityId: characterId,
					requireRowsAffected: true,
					cancellationToken: cancellationToken).ConfigureAwait(false);

				return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}
			else
			{
				// Hard delete: CASCADE constraints will automatically delete related data
				// This includes abilities, attributes, equipment, inventory, guild/party membership, etc.
				var result = await ExecuteSqlAsync(
					$@"DELETE FROM {TableName} 
					WHERE id = {characterId}",
					"DeleteCharacter",
					entityName: "Character",
					entityId: characterId,
					requireRowsAffected: true,
					cancellationToken: cancellationToken).ConfigureAwait(false);

				return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteSqlAsync(async (dbContext, ct) =>
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
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

			return await ExecuteSqlAsync(async (dbContext, ct) =>
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
			var transactionResult = await ExecuteSqlAsync(async (dbContext, transaction, ct) =>
			{
				// Use CTE to combine SELECT FOR UPDATE and UPDATE into single atomic operation
				// This ensures the lock is held throughout the entire update
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"WITH locked_chars AS (
					SELECT id FROM {TableName} 
					WHERE account = {account} AND NOT deleted 
					FOR UPDATE
					)
					UPDATE {TableName} 
					SET selected = (id = {characterId})
					WHERE account = {account} AND NOT deleted
					AND id IN (SELECT id FROM locked_chars)",
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

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
				SET online = {online}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {characterId} AND NOT deleted",
				"SetOnlineStatus",
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

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
				SET x = {x}, y = {y}, z = {z}, 
					rot_x = {rotX}, rot_y = {rotY}, rot_z = {rotZ}, rot_w = {rotW}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {characterId} AND NOT deleted",
				"UpdatePosition",
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

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
				SET scene_name = {sceneName ?? string.Empty}, 
					scene_handle = {sceneHandle}, 
					last_saved = CURRENT_TIMESTAMP 
				WHERE id = {characterId} AND deleted = false",
				"UpdateScene",
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
				entity.LastSaved,
				entity.TimeDeleted,
				entity.Deleted
			);
		}
	}
}