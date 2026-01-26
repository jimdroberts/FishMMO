using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;

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
	public sealed class CharacterGuildService : IdempotentBaseService<CharacterGuildEntity>, ICharacterGuildService
	{
		private const int MaxAllowedGuildCapacity = 256;

		/// <summary>
		/// Compiled query for retrieving guild membership by character ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterGuildEntity?>> GetGuildMembershipQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.FirstOrDefault(g => g.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving all guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterGuildEntity>>> GetGuildMembersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.ToList());

		/// <summary>
		/// Compiled query for counting guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> GetGuildMemberCountQuery =
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
		public async Task<DatabaseResult> SaveGuildMembershipAsync(CharacterGuildData guildData, int maxCapacity, Guid requestId, CancellationToken cancellationToken = default)
		{
			if (guildData.CharacterID == 0 || guildData.GuildID == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
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

			if (requestId == Guid.Empty)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "RequestId is required for idempotent guild membership save.");
			}

			// Use transaction to atomically check capacity and insert/update membership
			// RACE CONDITION MITIGATION: Uses strict lock ordering (character -> guild) and
			// SELECT FOR UPDATE to prevent deadlocks and ensure atomicity. The lock hierarchy
			// ensures that concurrent operations acquire locks in a consistent order, preventing
			// circular wait conditions.
			//
			// Lock Ordering Strategy:
			// 1. Lock character's membership row FIRST (establishes consistent order)
			// 2. Lock guild row SECOND (only if capacity check needed)
			// 3. Perform capacity check (protected by both locks)
			// 4. UPSERT membership (atomic operation)
			//
			// This pattern is safe because:
			// - All operations follow the same lock order (character -> guild)
			// - SELECT FOR UPDATE holds locks until transaction commits
			// - Capacity check is protected by guild lock
			// - UPSERT is atomic and idempotent
			var result = await ExecuteIdempotentResultAsync(
				requestId,
				scopeId: guildData.CharacterID,
				operationName: "SaveGuildMembership",
				operation: async (dbContext, transaction, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				var activeCharacter = await dbContext.Characters
					.FromSqlRaw($@"SELECT * FROM {charactersTableName} WHERE id = {{0}} AND deleted = FALSE FOR KEY SHARE", guildData.CharacterID)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct)
					.ConfigureAwait(false);
				if (activeCharacter == null)
				{
					return DatabaseResult.Failure(
						"DB_NOT_FOUND",
						"Character not found or deleted.");
				}

				// Lock character's membership row FIRST to establish consistent lock ordering
				// This prevents deadlocks and ensures atomicity across concurrent requests
				var existingMembership = await dbContext.CharacterGuilds
					.FromSqlRaw($"SELECT * FROM {TableName} WHERE character_id = {{0}} FOR UPDATE", guildData.CharacterID)
					.FirstOrDefaultAsync(ct);

				// Refined check: determine if capacity validation is needed
				// Skip capacity check if character is already in the same guild (rank/location update)
				bool needsCapacityCheck = existingMembership == null || existingMembership.GuildID != guildData.GuildID;

				if (needsCapacityCheck)
				{
					var guildTableName = dbContext.GetTableName<GuildEntity>();
					var membershipTableName = dbContext.GetTableName<CharacterGuildEntity>();

					// Atomic capacity check with proper locking to prevent race conditions
					// Uses FOR UPDATE on guild and FOR SHARE on member count to ensure consistent read
					// Lock ordering: character membership (already locked) -> guild -> member count
					var capacityResult = await dbContext.Guilds
						.FromSqlRaw($@"
							SELECT g.*
							FROM {guildTableName} g
							CROSS JOIN LATERAL (
								SELECT COUNT(*) as member_count
								FROM {membershipTableName}
								WHERE guild_id = {{0}}
								FOR SHARE
							) mc
							WHERE g.id = {{0}}
								AND mc.member_count < {{1}}
							FOR UPDATE",
							guildData.GuildID,
							maxCapacity)
						.FirstOrDefaultAsync(ct);

					if (capacityResult == null)
					{
						// Either guild doesn't exist or capacity exceeded - check which
						var guildExists = await dbContext.Guilds
							.AsNoTracking()
							.AnyAsync(g => g.ID == guildData.GuildID, ct);

						if (!guildExists)
						{
							return DatabaseResult.Failure(
								"NOT_FOUND",
								$"Guild with ID {guildData.GuildID} does not exist.");
						}

						return DatabaseResult.Failure(
							"CAPACITY_EXCEEDED",
							$"Guild has reached maximum capacity of {maxCapacity} members.");
					}
				}

				// Perform UPSERT - atomic operation that either inserts new membership
				// or updates existing membership (rank/location change within same guild)
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {TableName} 
						(character_id, guild_id, rank, location, time_created)
						VALUES ({{0}}, {{1}}, {{2}}, {{3}}, CURRENT_TIMESTAMP)
						ON CONFLICT (character_id) 
						DO UPDATE SET 
							guild_id = EXCLUDED.guild_id,
							rank = EXCLUDED.rank,
							location = EXCLUDED.location",
					new object[] { guildData.CharacterID, guildData.GuildID, guildData.Rank, guildData.Location },
					ct);

				return DatabaseResult.Success();
			},
				cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || guildId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
					SET rank = {{0}} 
					WHERE character_id = {{1}} AND guild_id = {{2}}",
				"UpdateRank",
				new object[] { rank, characterId, guildId },
				entityName: "CharacterGuild",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteGuildMembership",
				new object[] { characterId },
				entityName: "CharacterGuild",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterGuildData?>> GetGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterGuildData?>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteAsync<CharacterGuildData?>(async (dbContext, ct) =>
			{
				var entity = await GetGuildMembershipQuery(dbContext, characterId, ct);
				if (entity == null)
					return null;

				return new CharacterGuildData(
					id: entity.ID,
					characterID: entity.CharacterID,
					guildID: entity.GuildID,
					rank: entity.Rank,
					location: entity.Location
				);
			}, "GetGuildMembership", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterGuildData>>> GetGuildMembersAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			return await ExecuteAsync(
				async (dbContext, ct) =>
				{
					var entities = await GetGuildMembersQuery(dbContext, guildId, ct);
					var members = entities.Select(g => new CharacterGuildData(
						id: g.ID,
						characterID: g.CharacterID,
						guildID: g.GuildID,
						rank: g.Rank,
						location: g.Location
					)).ToList();

					return (IReadOnlyList<CharacterGuildData>)members;
				},
				"GetGuildMembers",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetGuildMemberCountAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			return await ExecuteAsync(
				async (dbContext, ct) =>
				{
					return await GetGuildMemberCountQuery(dbContext, guildId, ct);
				},
				"GetGuildMemberCount",
				cancellationToken);
		}
	}
}