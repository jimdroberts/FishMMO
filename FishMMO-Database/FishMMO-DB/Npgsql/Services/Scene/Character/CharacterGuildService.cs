using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing character guild membership in the database.
	/// Provides async operations for CRUD operations on character guild membership data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character guild membership including:
	/// - Guild membership save/update with atomic UPSERT operations
	/// - Rank updates
	/// - Membership deletion
	/// - Membership retrieval (individual and guild-wide)
	/// - Member count queries
	/// 
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseOperationCanceledException
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// </remarks>
	public sealed class CharacterGuildService : BaseService<CharacterGuildEntity>, ICharacterGuildService
	{
		private const int MaxAllowedGuildCapacity = 256;

		/// <summary>
		/// Compiled query for retrieving guild membership by character ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterGuildEntity?>> getGuildMembershipQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.FirstOrDefault(g => g.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving all guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterGuildEntity>>> getGuildMembersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.ToList());

		/// <summary>
		/// Compiled query for counting guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getGuildMemberCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterGuildService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterGuildService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(CharacterGuildData guildData, int maxCapacity, CancellationToken cancellationToken = default)
		{
			if (guildData.CharacterID <= 0 || guildData.GuildID <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.",
					isTransient: false);
			}

			if (maxCapacity <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid max capacity. Max capacity must be greater than 0.");
			}

			if (maxCapacity > MaxAllowedGuildCapacity)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					$"Invalid max capacity. Max capacity must not exceed {MaxAllowedGuildCapacity}.");
			}

			if (guildData.Version <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var characterTableName = dbContext.GetTableName<CharacterEntity>();
				var guildTableName = dbContext.GetTableName<GuildEntity>();
				var sql = $@"
					WITH active_character AS (
						SELECT id FROM {characterTableName} WHERE id = {{0}} AND deleted = FALSE
					),
					guild_row AS (
						SELECT id FROM {guildTableName} WHERE id = {{1}} FOR UPDATE
					),
					existing_membership AS (
						SELECT guild_id FROM {TableName} WHERE character_id = {{0}}
					),
					capacity_ok AS (
						SELECT 1
						WHERE EXISTS (SELECT 1 FROM existing_membership WHERE guild_id = {{1}})
							OR (SELECT COUNT(*) FROM {TableName} WHERE guild_id = {{1}}) < {{2}}
					),
					upserted AS (
						INSERT INTO {TableName}
							(character_id, guild_id, version, rank, location, time_created)
						SELECT
							{{0}},
							{{1}},
							{{3}},
							{{4}},
							{{5}},
							{{6}}
						WHERE
							EXISTS (SELECT 1 FROM active_character)
							AND EXISTS (SELECT 1 FROM guild_row)
							AND EXISTS (SELECT 1 FROM capacity_ok)
						ON CONFLICT (character_id)
						DO UPDATE SET
							guild_id = EXCLUDED.guild_id,
							rank = EXCLUDED.rank,
							location = EXCLUDED.location,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING 1
					)
					SELECT CASE
						WHEN NOT EXISTS (SELECT 1 FROM active_character) THEN 1
						WHEN NOT EXISTS (SELECT 1 FROM guild_row) THEN 2
						WHEN NOT EXISTS (SELECT 1 FROM capacity_ok) THEN 3
						WHEN EXISTS (SELECT 1 FROM upserted) THEN 0
						ELSE 4
					END AS value";

				var status = await dbContext.Set<SqlIntValue>()
					.FromSqlRaw(sql, new object[] { guildData.CharacterID, guildData.GuildID, maxCapacity, guildData.Version, guildData.Rank, guildData.Location ?? string.Empty, now })
					.AsNoTracking()
					.SingleAsync(cancellationToken)
					.ConfigureAwait(false);

				return status.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? result.Data switch
				{
					0 => DatabaseResult.Success(),
					1 => throw new DatabaseEntityNotFoundException("Character", guildData.CharacterID.ToString(), "Character not found or deleted."),
					2 => DatabaseResult.Failure("NOT_FOUND", $"Guild with ID {guildData.GuildID} does not exist.", isTransient: false),
					3 => DatabaseResult.Failure("CAPACITY_EXCEEDED", $"Guild has reached maximum capacity of {maxCapacity} members.", isTransient: false),
					4 => DatabaseResult.Failure("STALE_STATE", "Guild membership update rejected due to a stale Version.", isTransient: false),
					_ => DatabaseResult.Failure("DATABASE_ERROR", "A database error occurred.", isTransient: true)
				}
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || guildId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var updateSql = $@"UPDATE {TableName}
					SET rank = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND guild_id = {{3}} AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(updateSql, new object[] { rank, incomingVersion, characterId, guildId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterGuilds
						.AsNoTracking()
						.FirstOrDefaultAsync(g => g.CharacterID == characterId && g.GuildID == guildId, cancellationToken)
						.ConfigureAwait(false);

					if (existing == null)
					{
						throw new DatabaseEntityNotFoundException("CharacterGuild", characterId.ToString());
					}

					if (existing.Version == incomingVersion)
					{
						throw new DuplicateReplayException();
					}

					throw new StaleStateException("Guild rank update rejected due to a stale Version.");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var deleteSql = $@"DELETE FROM {TableName}
					WHERE character_id = {{0}} AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(deleteSql, new object[] { characterId, incomingVersion }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterGuilds
						.AsNoTracking()
						.FirstOrDefaultAsync(g => g.CharacterID == characterId, cancellationToken)
						.ConfigureAwait(false);

					if (existing != null)
					{
						throw new StaleStateException("Guild membership delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterGuildData?>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterGuildData?>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteReadAsync<CharacterGuildData?>(async dbContext =>
			{
				var entity = await getGuildMembershipQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					return null;
				}

				return new CharacterGuildData(
					id: entity.ID,
					version: entity.Version,
					characterID: entity.CharacterID,
					guildID: entity.GuildID,
					rank: entity.Rank,
					location: entity.Location);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterGuildData>>> FetchManyAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getGuildMembersQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false);
				var members = entities.Select(g => new CharacterGuildData(
					id: g.ID,
					version: g.Version,
					characterID: g.CharacterID,
					guildID: g.GuildID,
					rank: g.Rank,
					location: g.Location)).ToList();

				return (IReadOnlyList<CharacterGuildData>)members;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
				await getGuildMemberCountQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}
	}
}