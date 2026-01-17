using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
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
	public sealed class CharacterGuildService : ICharacterGuildService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterGuildService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterGuildService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveGuildMembershipAsync(CharacterGuildData guildData, CancellationToken cancellationToken = default)
		{
			if (guildData.CharacterID == 0 || guildData.GuildID == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterGuildEntity>();

					// Use PostgreSQL UPSERT for atomic insert-or-update
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						   (character_id, guild_id, rank, location)
						   VALUES ({guildData.CharacterID}, {guildData.GuildID}, {guildData.Rank}, {guildData.Location})
						   ON CONFLICT (character_id) 
						   DO UPDATE SET 
						       guild_id = EXCLUDED.guild_id,
						       rank = EXCLUDED.rank,
						       location = EXCLUDED.location",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveGuildMembership", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_guild_character_id_fkey",
						"Character or guild does not exist.",
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
						"SaveGuildMembership",
						"Failed to save guild membership due to a database error.",
						$"DbUpdateException in SaveGuildMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveGuildMembership",
						"An unexpected error occurred while saving guild membership.",
						$"Unexpected error in SaveGuildMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || guildId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterGuildEntity>();

					// Atomic update without loading entity
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET rank = {rank} 
						WHERE character_id = {characterId} AND guild_id = {guildId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"CharacterGuild",
							$"characterId={characterId}, guildId={guildId}",
							"Guild membership not found or character not in specified guild."));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("UpdateRank", 10));
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
						"UpdateRank",
						"Failed to update guild rank due to a database error.",
						$"DbUpdateException in UpdateRankAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateRank",
						"An unexpected error occurred while updating guild rank.",
						$"Unexpected error in UpdateRankAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterGuildEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteGuildMembership", 10));
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
						"DeleteGuildMembership",
						"Failed to delete guild membership due to a database error.",
						$"DbUpdateException in DeleteGuildMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteGuildMembership",
						"An unexpected error occurred while deleting guild membership.",
						$"Unexpected error in DeleteGuildMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterGuildData?>> GetGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterGuildData?>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var membership = await dbContext.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.CharacterID == characterId)
					.Select(g => new CharacterGuildData
					{
						ID = g.ID,
						CharacterID = g.CharacterID,
						GuildID = g.GuildID,
						Rank = g.Rank,
						Location = g.Location
					})
					.FirstOrDefaultAsync(cancellationToken);

				return DatabaseResult<CharacterGuildData?>.Success(membership);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterGuildData?>.FromException(
					new DatabaseTimeoutException("GetGuildMembership", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterGuildData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterGuildData?>.FromException(
					new DatabaseQueryException(
						"GetGuildMembership",
						"An unexpected error occurred while retrieving guild membership.",
						$"Unexpected error in GetGuildMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterGuildData>>> GetGuildMembersAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var members = await dbContext.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.Select(g => new CharacterGuildData
					{
						ID = g.ID,
						CharacterID = g.CharacterID,
						GuildID = g.GuildID,
						Rank = g.Rank,
						Location = g.Location
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.Success(members);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.FromException(
					new DatabaseTimeoutException("GetGuildMembers", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.FromException(
					new DatabaseQueryException(
						"GetGuildMembers",
						"An unexpected error occurred while retrieving guild members.",
						$"Unexpected error in GetGuildMembersAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetGuildMemberCountAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var count = await dbContext.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.CountAsync(cancellationToken);

				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetGuildMemberCount", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetGuildMemberCount",
						"An unexpected error occurred while retrieving guild member count.",
						$"Unexpected error in GetGuildMemberCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}