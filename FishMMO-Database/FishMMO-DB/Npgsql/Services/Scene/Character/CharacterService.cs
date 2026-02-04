using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Database.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterService : BaseService<CharacterEntity>, ICharacterService
	{
		private static readonly TimeSpan DefaultSessionLeaseDuration = TimeSpan.FromMinutes(2);

		private enum SaveCharacterWriteOutcome
		{
			Success = 0,
			NotFound = 1,
			AuthorityLost = 2,
		}

		/// <summary>
		/// Compiled query for FetchAsync (by id) hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterEntity?>> fetchByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterEntity?)context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.ID == characterId && !c.Deleted));

		/// <summary>
		/// Compiled query for retrieving character by name (hot path for login/character selection).
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<CharacterEntity?>> fetchByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLower, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.FirstOrDefault(c => c.NameLowercase == nameLower && !c.Deleted));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for counting characters by account (hot path for character creation validation).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<int>> countByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string account, CancellationToken ct) =>
				context.Characters
						.AsNoTracking()
						.Where(c => c.Account == account && !c.Deleted)
						.Count());

		/// <summary>
		/// Compiled query for retrieving all characters by account (hot path for character selection).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<List<CharacterEntity>>> fetchManyByAccountQuery =
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
		public async Task<DatabaseResult<int>> CountAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", Authentication.InvalidUsernameError);
			}

			var result = await ExecuteReadAsync(async dbContext =>
				await countByAccountQuery(dbContext, account, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterOperationResult>> CreateCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedCharacterName(characterData.Name))
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.InvalidName);
			}

			if (!Authentication.IsAllowedUsername(characterData.Account))
			{
				return DatabaseResult<CharacterOperationResult>.Success(CharacterOperationResult.DatabaseError);
			}

			var nameLower = characterData.Name.Trim().ToLowerInvariant();
			var result = await ExecuteWriteAsync<int>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sceneName = characterData.SceneName ?? string.Empty;
				var bindScene = characterData.BindScene ?? string.Empty;

				var sql = $@"
					WITH inserted AS (
						INSERT INTO {TableName} (
							name,
							account,
							selected,
							world_server_id,
							scene_name,
							scene_handle,
							bind_scene,
							bind_x,
							bind_y,
							bind_z,
							instance_id,
							instance_x,
							instance_y,
							instance_z,
							instance_rot_x,
							instance_rot_y,
							instance_rot_z,
							instance_rot_w,
							race_id,
							model_index,
							x,
							y,
							z,
							rot_x,
							rot_y,
							rot_z,
							rot_w,
							access_level,
							session_state,
							session_owner_server_id,
							session_owner_token,
							session_lease_expires_utc,
							flags,
							version,
							time_created,
							last_saved
						)
						VALUES (
							{{0}},
							{{1}},
							{{2}},
							{{3}},
							{{4}},
							{{5}},
							{{6}},
							{{7}},
							{{8}},
							{{9}},
							{{10}},
							{{11}},
							{{12}},
							{{13}},
							{{14}},
							{{15}},
							{{16}},
							{{17}},
							{{18}},
							{{19}},
							{{20}},
							{{21}},
							{{22}},
							{{23}},
							{{24}},
							{{25}},
							{{26}},
							{{27}},
							0,
							0,
							{{28}},
							{{29}},
							{{30}},
							1,
							{{31}},
							{{32}}
						)
						ON CONFLICT (name_lowercase)
						DO NOTHING
						RETURNING 1
					),
					existing AS (
						SELECT account, deleted
						FROM {TableName}
						WHERE name_lowercase = {{33}}
						LIMIT 1
					)
					SELECT CASE
						WHEN EXISTS (SELECT 1 FROM inserted) THEN 0
						WHEN EXISTS (SELECT 1 FROM existing WHERE deleted = FALSE AND account = {{1}}) THEN 1
						WHEN EXISTS (SELECT 1 FROM existing WHERE deleted = FALSE AND account <> {{1}}) THEN 2
						ELSE 3
					END::integer AS value";

				var status = await dbContext.Set<SqlIntValue>()
					.FromSqlRaw(
						sql,
						characterData.Name,
						characterData.Account,
						characterData.Selected,
						characterData.WorldServerID,
						sceneName,
						characterData.SceneHandle,
						bindScene,
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
						Guid.Empty,
						DateTime.UnixEpoch,
						characterData.Flags,
						now,
						now,
						nameLower)
					.AsNoTracking()
					.SingleAsync(cancellationToken)
					.ConfigureAwait(false);

				return status.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<CharacterOperationResult>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			return DatabaseResult<CharacterOperationResult>.Success(result.Data switch
			{
				0 => CharacterOperationResult.CharacterCreated,
				1 => CharacterOperationResult.CharacterCreated,
				2 => CharacterOperationResult.NameAlreadyExists,
				_ => CharacterOperationResult.DatabaseError
			});
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(CharacterData characterData, CancellationToken cancellationToken = default)
		{
			if (characterData.ID <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID.", isTransient: false);
			}

			// Version is required and authoritative.
			// LastSaved is treated as analytics only and is not used for concurrency.
			if (characterData.Version <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than zero.",
					isTransient: false);
			}

			var saveResult = await ExecuteTransactionAsync<SaveCharacterWriteOutcome>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var sceneName = characterData.SceneName ?? string.Empty;
				var bindScene = characterData.BindScene ?? string.Empty;

				var sql = $@"UPDATE {TableName}
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
						flags = {{28}},
						version = {{29}},
						last_saved = {{30}},
						session_lease_expires_utc = CASE
							WHEN session_state <> 0 THEN {{31}}
							ELSE session_lease_expires_utc
						END
					WHERE id = {{32}} AND deleted = FALSE AND version < {{29}}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[]
					{
						characterData.Name,
						characterData.Account,
						characterData.Selected,
						characterData.WorldServerID,
						sceneName,
						characterData.SceneHandle,
						bindScene,
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
						characterData.Flags,
						characterData.Version,
						now,
						newLeaseExpiresUtc,
						characterData.ID,
					},
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected > 0)
				{
					return SaveCharacterWriteOutcome.Success;
				}

				// No row updated: either missing, deleted, or authority lost.
				// Load current state (no tracking) only for the conflict path to preserve idempotency.
				var current = await dbContext.Characters
					.AsNoTracking()
					.FirstOrDefaultAsync(c => c.ID == characterData.ID && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);

				if (current == null)
				{
					return SaveCharacterWriteOutcome.NotFound;
				}

				if (current.Version > characterData.Version)
				{
					return SaveCharacterWriteOutcome.AuthorityLost;
				}

				throw new DuplicateReplayException();
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

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
		/// sets <c>deleted=true</c>, and stamps the character <c>Version</c> to the incoming authoritative value.
		/// Character guild/party memberships are hard-deleted (temporary state).
		/// This method does not soft-delete character-owned sub-entities; those entities have independent Version streams.
		/// </remarks>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID.", isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid incoming version.", isTransient: false);
			}

			var transactionResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var guid = Guid.NewGuid().ToString("D");
				var suffix = $"_DELETED_{guid}";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
						SET name = (COALESCE(name, '') || {{1}}),
							deleted = TRUE,
							time_deleted = CURRENT_TIMESTAMP,
							version = {{2}}
						WHERE id = {{0}} AND deleted = FALSE AND version < {{2}}",
					new object[] { characterId, suffix, incomingVersion },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					var currentState = await dbContext.Set<CharacterEntity>()
						.AsNoTracking()
						.IgnoreQueryFilters()
						.Where(x => x.ID == characterId)
						.Select(x => new { x.Deleted, x.Version })
						.SingleOrDefaultAsync(cancellationToken)
						.ConfigureAwait(false);

					if (currentState == null || currentState.Deleted)
					{
						return;
					}

					if (currentState.Version == incomingVersion)
					{
						throw new DuplicateReplayException();
					}

					throw new StaleStateException("Stale character delete (incoming version is not newer than persisted state).");
				}

				// Hard-delete temporary membership state (intentional).
				await dbContext.Database.ExecuteSqlRawAsync(
					"DELETE FROM character_guild WHERE character_id = {0}",
					new object[] { characterId },
					cancellationToken).ConfigureAwait(false);

				await dbContext.Database.ExecuteSqlRawAsync(
					"DELETE FROM character_party WHERE character_id = {0}",
					new object[] { characterId },
					cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			return transactionResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", "Invalid character ID.", isTransient: false);
			}

			var result = await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				var entity = await fetchByIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterData>>> FetchManyAsync(string account, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account))
			{
				return DatabaseResult<IReadOnlyList<CharacterData>>.Failure("VALIDATION_ERROR", Authentication.InvalidUsernameError);
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var entities = await fetchManyByAccountQuery(dbContext, account, cancellationToken).ConfigureAwait(false);
				return (IReadOnlyList<CharacterData>)entities.Select(MapEntityToData).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterData?>> FetchAsync(string name, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedCharacterName(name))
			{
				return DatabaseResult<CharacterData?>.Failure("VALIDATION_ERROR", Authentication.InvalidCharacterNameError);
			}

			var result = await ExecuteReadAsync<CharacterData?>(async dbContext =>
			{
				var nameLower = name.ToLowerInvariant();
				var entity = await fetchByNameQuery(dbContext, nameLower, cancellationToken).ConfigureAwait(false);
				return entity == null ? null : MapEntityToData(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Concurrency Safety:</b></para>
		/// Uses a single atomic statement with SELECT FOR UPDATE to prevent race conditions
		/// when multiple requests attempt to select different characters concurrently.
		/// Row-level locks are acquired for all characters belonging to the account,
		/// ensuring only one selection operation can proceed at a time per account.
		/// </remarks>
		public async Task<DatabaseResult> SetSelectedAsync(string account, long characterId, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedUsername(account) || characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid account or character ID.", isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
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
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<Guid>> TryClaimAsync(long characterId, long ownerServerId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0)
			{
				return DatabaseResult<Guid>.Failure("VALIDATION_ERROR", "Invalid character ID or owner server ID.", isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newToken = Guid.NewGuid();
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_state = {{0}},
						session_owner_server_id = {{1}},
						session_owner_token = {{2}},
						session_lease_expires_utc = {{3}},
						last_saved = {{4}}
					WHERE id = {{5}} AND deleted = false
						AND (session_state = {{6}} OR session_lease_expires_utc <= {{4}})",
					(short)CharacterSessionState.Online,
					ownerServerId,
					newToken,
					newLeaseExpiresUtc,
					now,
					characterId,
					(short)CharacterSessionState.Offline
				).ConfigureAwait(false);

				if (rowsAffected > 0)
				{
					return newToken;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is already owned or transitioning.",
					"INVALID_OPERATION",
					isTransient: false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> BeginTransitionAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0 || ownerToken == Guid.Empty)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID, owner server ID, or owner token.", isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_state = {{0}},
						session_lease_expires_utc = {{1}},
						last_saved = {{2}}
					WHERE id = {{3}} AND deleted = false
						AND session_state = {{4}}
						AND session_owner_server_id = {{5}}
						AND session_owner_token = {{6}}",
					(short)CharacterSessionState.Transitioning,
					newLeaseExpiresUtc,
					now,
					characterId,
					(short)CharacterSessionState.Online,
					ownerServerId,
					ownerToken
				).ConfigureAwait(false);

					if (rowsAffected == 1)
					{
						return;
					}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is not owned by this server or is not online.",
					"INVALID_OPERATION",
					isTransient: false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ReleaseToOfflineAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0 || ownerToken == Guid.Empty)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID, owner server ID, or owner token.", isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_state = {{0}},
						session_owner_server_id = {{1}},
						session_owner_token = {{2}},
						session_lease_expires_utc = {{3}},
						last_saved = {{4}}
					WHERE id = {{5}} AND deleted = false
						AND session_state = {{6}}
						AND session_owner_server_id = {{7}}
						AND session_owner_token = {{8}}",
					(short)CharacterSessionState.Offline,
					0L,
					Guid.Empty,
					DateTime.UnixEpoch,
					now,
					characterId,
					(short)CharacterSessionState.Transitioning,
					ownerServerId,
					ownerToken
				).ConfigureAwait(false);

				if (rowsAffected == 1)
				{
					return;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is not transitioning under this server.",
					"INVALID_OPERATION",
					isTransient: false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> RefreshSessionLeaseAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || ownerServerId <= 0 || ownerToken == Guid.Empty)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID, owner server ID, or owner token.", isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var newLeaseExpiresUtc = now + DefaultSessionLeaseDuration;
				var tableName = dbContext.GetTableName<CharacterEntity>();

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						session_lease_expires_utc = {{0}},
						last_saved = {{1}}
					WHERE id = {{2}} AND deleted = false
						AND session_state <> {{3}}
						AND session_owner_server_id = {{4}}
						AND session_owner_token = {{5}}",
					newLeaseExpiresUtc,
					now,
					characterId,
					(short)CharacterSessionState.Offline,
					ownerServerId,
					ownerToken
				).ConfigureAwait(false);

				if (rowsAffected == 1)
				{
					return;
				}

				var exists = await dbContext.Characters
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);
				if (!exists)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				throw new DatabaseException(
					"Character is not owned by this server.",
					"INVALID_OPERATION",
					isTransient: false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID.", isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						x = {{0}},
						y = {{1}},
						z = {{2}},
						rot_x = {{3}},
						rot_y = {{4}},
						rot_z = {{5}},
						rot_w = {{6}},
						last_saved = {{7}}
					WHERE id = {{8}} AND deleted = FALSE",
					x,
					y,
					z,
					rotX,
					rotY,
					rotZ,
					rotW,
					now,
					characterId).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateSceneAsync(long characterId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID.", isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var tableName = dbContext.GetTableName<CharacterEntity>();
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					$@"UPDATE {tableName}
					SET
						scene_name = {{0}},
						scene_handle = {{1}},
						last_saved = {{2}}
					WHERE id = {{3}} AND deleted = FALSE",
					sceneName ?? string.Empty,
					sceneHandle,
					now,
					characterId).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// Maps a CharacterEntity to CharacterData DTO.
		/// </summary>
		/// <param name="entity">The character entity.</param>
		/// <returns>The character data DTO.</returns>
		private static CharacterData MapEntityToData(CharacterEntity entity)
		{
			var online = entity.SessionState != CharacterSessionState.Offline;

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
				online,
				entity.Flags,
				entity.Version,
				entity.TimeCreated,
				entity.LastSaved
			);
		}
	}
}