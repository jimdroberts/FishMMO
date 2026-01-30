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
		private enum SaveCharacterWriteOutcome
		{
			Success = 0,
			NotFound = 1,
			ConcurrencyConflict = 2,
			AuthorityLost = 3,
		}

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

			var result = await ExecuteMirrorAsync(async dbContext =>
				await GetCharacterCountByAccountQuery(dbContext, account, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			var insertResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var entity = new CharacterEntity
				{
					Name = characterData.Name,
					Account = characterData.Account,
					Selected = characterData.Selected,
					WorldServerID = characterData.WorldServerID,
					SceneName = characterData.SceneName ?? string.Empty,
					SceneHandle = characterData.SceneHandle,
					BindScene = characterData.BindScene ?? string.Empty,
					BindX = characterData.BindX,
					BindY = characterData.BindY,
					BindZ = characterData.BindZ,
					InstanceID = characterData.InstanceID,
					InstanceX = characterData.InstanceX,
					InstanceY = characterData.InstanceY,
					InstanceZ = characterData.InstanceZ,
					InstanceRotX = characterData.InstanceRotX,
					InstanceRotY = characterData.InstanceRotY,
					InstanceRotZ = characterData.InstanceRotZ,
					InstanceRotW = characterData.InstanceRotW,
					RaceID = characterData.RaceID,
					ModelIndex = characterData.ModelIndex,
					X = characterData.X,
					Y = characterData.Y,
					Z = characterData.Z,
					RotX = characterData.RotX,
					RotY = characterData.RotY,
					RotZ = characterData.RotZ,
					RotW = characterData.RotW,
					AccessLevel = characterData.AccessLevel,
					Online = characterData.Online,
					Flags = characterData.Flags,
					Version = characterData.Version,
					TimeCreated = now,
					LastSaved = now,
				};

				await dbContext.Characters.AddAsync(entity, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.CharacterCreated);
			}

			if (insertResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult<CharacterOperationResult>.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var nameLower = characterData.Name.Trim().ToLowerInvariant();
			var existingResult = await ExecuteMirrorAsync(async dbContext =>
				await GetCharacterByNameQuery(dbContext, nameLower, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

			if (!existingResult.IsSuccess)
			{
				return DatabaseResult<CharacterOperationResult>.Failure(existingResult.ErrorCode, existingResult.ErrorMessage, existingResult.IsTransient);
			}

			var existing = existingResult.Data;
			if (existing == null)
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.DatabaseError);
			}

			return DatabaseResult<CharacterOperationResult>.Success(
				string.Equals(existing.Account, characterData.Account, StringComparison.Ordinal)
					? CharacterOperationResult.CharacterCreated
					: CharacterOperationResult.NameAlreadyExists);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (characterData.ID <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var saveResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var existing = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterData.ID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);

				if (existing == null)
				{
					return SaveCharacterWriteOutcome.NotFound;
				}

				// Game-logic authority check (Version is the boss):
				// If the caller doesn't provide a Version (0), fall back to legacy LastSaved semantics.
				if (characterData.Version > 0)
				{
					// - If DB has a higher Version, the caller is stale and must not overwrite progress.
					// - If Versions match, only allow idempotent success (same final state).
					if (existing.Version > characterData.Version)
					{
						return SaveCharacterWriteOutcome.AuthorityLost;
					}
					if (existing.Version == characterData.Version)
					{
						return HasSameState(existing, characterData)
							? SaveCharacterWriteOutcome.Success
							: SaveCharacterWriteOutcome.AuthorityLost;
					}
				}
				else
				{
					// Legacy optimistic concurrency: LastSaved acts as the caller's "snapshot".
					if (existing.LastSaved != characterData.LastSaved)
					{
						return HasSameState(existing, characterData)
							? SaveCharacterWriteOutcome.Success
							: SaveCharacterWriteOutcome.ConcurrencyConflict;
					}
				}

				existing.Name = characterData.Name;
				existing.Account = characterData.Account;
				existing.Selected = characterData.Selected;
				existing.WorldServerID = characterData.WorldServerID;
				existing.SceneName = characterData.SceneName ?? string.Empty;
				existing.SceneHandle = characterData.SceneHandle;
				existing.BindScene = characterData.BindScene ?? string.Empty;
				existing.BindX = characterData.BindX;
				existing.BindY = characterData.BindY;
				existing.BindZ = characterData.BindZ;
				existing.InstanceID = characterData.InstanceID;
				existing.InstanceX = characterData.InstanceX;
				existing.InstanceY = characterData.InstanceY;
				existing.InstanceZ = characterData.InstanceZ;
				existing.InstanceRotX = characterData.InstanceRotX;
				existing.InstanceRotY = characterData.InstanceRotY;
				existing.InstanceRotZ = characterData.InstanceRotZ;
				existing.InstanceRotW = characterData.InstanceRotW;
				existing.RaceID = characterData.RaceID;
				existing.ModelIndex = characterData.ModelIndex;
				existing.X = characterData.X;
				existing.Y = characterData.Y;
				existing.Z = characterData.Z;
				existing.RotX = characterData.RotX;
				existing.RotY = characterData.RotY;
				existing.RotZ = characterData.RotZ;
				existing.RotW = characterData.RotW;
				existing.AccessLevel = characterData.AccessLevel;
				existing.Online = characterData.Online;
				existing.Flags = characterData.Flags;
				if (characterData.Version > 0)
				{
					existing.Version = characterData.Version;
				}
				existing.LastSaved = DateTime.UtcNow;

				return SaveCharacterWriteOutcome.Success;
			}).ConfigureAwait(false);

			if (!saveResult.IsSuccess)
			{
				return DatabaseResult.Failure(saveResult.ErrorCode, saveResult.ErrorMessage, saveResult.IsTransient);
			}

			switch (saveResult.Data)
			{
				case SaveCharacterWriteOutcome.Success:
					return DatabaseResult.Success();
				case SaveCharacterWriteOutcome.NotFound:
					return DatabaseResult.Failure(
						"DB_NOT_FOUND",
						"Character not found.",
						isTransient: false);
				case SaveCharacterWriteOutcome.ConcurrencyConflict:
					return DatabaseResult.Failure(
						"CONCURRENCY_CONFLICT",
						"Character was modified by another server. Please reload and try again.",
						isTransient: false);
				case SaveCharacterWriteOutcome.AuthorityLost:
					return DatabaseResult.Failure(
						"AUTHORITY_LOST",
						"A newer server process has already saved this character. Refusing to overwrite progress.",
						isTransient: false);
				default:
					return DatabaseResult.Failure("DATABASE_ERROR", "Unexpected save outcome.");
			}
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

			var transactionResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var guid = Guid.NewGuid().ToString("D");
				var suffix = $"_DELETED_{guid}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
						SET name = (COALESCE(name, '') || {{1}}),
							deleted = TRUE,
							time_deleted = CURRENT_TIMESTAMP
						WHERE id = {{0}} AND deleted = FALSE",
					new object[] { characterId, suffix },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					return;
				}

				// Hard-delete temporary membership state (intentional).
				var guildMemberships = await dbContext.CharacterGuilds
					.Where(e => e.CharacterID == characterId)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				if (guildMemberships.Count > 0)
				{
					dbContext.CharacterGuilds.RemoveRange(guildMemberships);
				}

				var partyMemberships = await dbContext.CharacterParties
					.Where(e => e.CharacterID == characterId)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				if (partyMemberships.Count > 0)
				{
					dbContext.CharacterParties.RemoveRange(partyMemberships);
				}

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

				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAbilityEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterKnownAbilityEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAttributeEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterAchievementEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterInventoryEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterEquipmentEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterBankEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterHotkeyEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterMailEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterItemCooldownEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterSkillEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterBuffEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetAttributeEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterPetBuffEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterFactionEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterQuestEntity>(), characterId, cancellationToken).ConfigureAwait(false);
				await SoftDeleteTableAsync(dbContext, dbContext.GetTableName<CharacterFriendEntity>(), characterId, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

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

			var result = await ExecuteMirrorAsync<CharacterData?>(async dbContext =>
			{
				var entity = await GetCharacterByIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<CharacterData?>.Success(result.Data)
				: DatabaseResult<CharacterData?>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> GetCharactersAsync(string account, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(account))
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Failure("VALIDATION_ERROR", "Account name is required");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var entities = await GetCharactersByAccountQuery(dbContext, account, cancellationToken).ConfigureAwait(false);
				return (IReadOnlyList<CharacterData>)entities.Select(MapEntityToData).ToList();
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<IReadOnlyList<CharacterData>>.Success(result.Data)
				: DatabaseResult<IReadOnlyList<CharacterData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> GetCharacterByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", "Character name is required");
			}

			var result = await ExecuteMirrorAsync<CharacterData?>(async dbContext =>
			{
				var nameLower = name.ToLowerInvariant();
				var entity = await GetCharacterByNameQuery(dbContext, nameLower, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<CharacterData?>.Success(result.Data)
				: DatabaseResult<CharacterData?>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH locked_chars AS (
					SELECT id FROM {tableName} 
					WHERE account = {{0}} AND deleted = FALSE
					ORDER BY id
					FOR UPDATE
					)
					UPDATE {tableName} 
					SET selected = (id = {{1}})
					WHERE account = {{0}} AND deleted = FALSE
					AND id IN (SELECT id FROM locked_chars)",
					new object[] { account, characterId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetOnlineStatusAsync(long characterId, bool online, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var character = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (character == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				character.Online = online;
				character.LastSaved = now;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var character = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (character == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				character.X = x;
				character.Y = y;
				character.Z = z;
				character.RotX = rotX;
				character.RotY = rotY;
				character.RotZ = rotZ;
				character.RotW = rotW;
				character.LastSaved = now;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateSceneAsync(long characterId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var character = await dbContext.Characters
					.FirstOrDefaultAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (character == null)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
				character.SceneName = sceneName ?? string.Empty;
				character.SceneHandle = sceneHandle;
				character.LastSaved = now;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		private static bool HasSameState(CharacterEntity existing, CharacterData expected)
		{
			return string.Equals(existing.Name ?? string.Empty, expected.Name ?? string.Empty, StringComparison.Ordinal)
				&& string.Equals(existing.Account ?? string.Empty, expected.Account ?? string.Empty, StringComparison.Ordinal)
				&& existing.Selected == expected.Selected
				&& existing.WorldServerID == expected.WorldServerID
				&& string.Equals(existing.SceneName ?? string.Empty, expected.SceneName ?? string.Empty, StringComparison.Ordinal)
				&& existing.SceneHandle == expected.SceneHandle
				&& string.Equals(existing.BindScene ?? string.Empty, expected.BindScene ?? string.Empty, StringComparison.Ordinal)
				&& existing.BindX.Equals(expected.BindX)
				&& existing.BindY.Equals(expected.BindY)
				&& existing.BindZ.Equals(expected.BindZ)
				&& existing.InstanceID == expected.InstanceID
				&& existing.InstanceX.Equals(expected.InstanceX)
				&& existing.InstanceY.Equals(expected.InstanceY)
				&& existing.InstanceZ.Equals(expected.InstanceZ)
				&& existing.InstanceRotX.Equals(expected.InstanceRotX)
				&& existing.InstanceRotY.Equals(expected.InstanceRotY)
				&& existing.InstanceRotZ.Equals(expected.InstanceRotZ)
				&& existing.InstanceRotW.Equals(expected.InstanceRotW)
				&& existing.RaceID == expected.RaceID
				&& existing.ModelIndex == expected.ModelIndex
				&& existing.X.Equals(expected.X)
				&& existing.Y.Equals(expected.Y)
				&& existing.Z.Equals(expected.Z)
				&& existing.RotX.Equals(expected.RotX)
				&& existing.RotY.Equals(expected.RotY)
				&& existing.RotZ.Equals(expected.RotZ)
				&& existing.RotW.Equals(expected.RotW)
				&& existing.AccessLevel == expected.AccessLevel
				&& existing.Online == expected.Online
				&& existing.Flags == expected.Flags
				&& existing.Version == expected.Version;
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
				entity.Version,
				entity.TimeCreated,
				entity.LastSaved
			);
		}
	}
}