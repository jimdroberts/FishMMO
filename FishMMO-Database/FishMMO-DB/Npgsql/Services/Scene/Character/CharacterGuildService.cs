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
	/// - OperationCanceledException → DatabaseTimeoutException
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
		public async Task<DatabaseResult> SaveGuildMembershipAsync(CharacterGuildData guildData, CancellationToken cancellationToken = default)
		{
			if (guildData.CharacterID == 0 || guildData.GuildID == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use PostgreSQL UPSERT for atomic insert-or-update
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} 
								(character_id, guild_id, rank, location)
								VALUES ({guildData.CharacterID}, {guildData.GuildID}, {guildData.Rank}, {guildData.Location})
								ON CONFLICT (character_id) 
								DO UPDATE SET 
									guild_id = EXCLUDED.guild_id,
									rank = EXCLUDED.rank,
									location = EXCLUDED.location",
					cancellationToken);
			}, "SaveGuildMembership", cancellationToken);
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

			var result = await ExecuteWithStrategyAsync<int>(
				async (dbContext, strategy) =>
				{
					return await strategy.ExecuteAsync(async () =>
					{
						// Atomic update without loading entity
						return await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"UPDATE {TableName} 
							SET rank = {rank} 
							WHERE character_id = {characterId} AND guild_id = {guildId}",
							cancellationToken);
					});
				},
				"UpdateRank",
				cancellationToken);

			if (!result.IsSuccess)
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage);

			return ValidateRowsAffected(
				result.Data,
				"CharacterGuild",
				$"characterId={characterId}, guildId={guildId}");
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

			return await ExecuteWithStrategyAsync(async dbContext =>
		{
			// Use atomic DELETE for thread safety
			await dbContext.Database.ExecuteSqlInterpolatedAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				cancellationToken);
		}, "DeleteGuildMembership", cancellationToken);
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

			return await ExecuteWithStrategyAsync<CharacterGuildData?>(async dbContext =>
			{
				var entity = await GetGuildMembershipQuery(dbContext, characterId, cancellationToken);
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

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					var entities = await GetGuildMembersQuery(dbContext, guildId, cancellationToken);
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

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					return await GetGuildMemberCountQuery(dbContext, guildId, cancellationToken);
				},
				"GetGuildMemberCount",
				cancellationToken);
		}
	}
}