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
	/// <inheritdoc/>
	public sealed class CharacterAchievementService : ICharacterAchievementService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAchievementService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAchievementService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAchievementsAsync(IEnumerable<CharacterAchievementData> achievements, CancellationToken cancellationToken = default)
		{
			if (achievements == null || !achievements.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Achievements collection must not be null or empty.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterAchievementEntity>();
					var achievementList = achievements.ToList();

					// Extract arrays for bulk UPSERT
					var characterIds = achievementList.Select(a => a.CharacterID).ToArray();
					var templateIds = achievementList.Select(a => a.TemplateID).ToArray();
					var tiers = achievementList.Select(a => a.Tier).ToArray();
					var values = achievementList.Select(a => a.Value).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						(character_id, template_id, tier, value)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[],
							{tiers}::smallint[],
							{values}::int[]
						)
						ON CONFLICT (character_id, template_id) 
						DO UPDATE SET 
							tier = EXCLUDED.tier,
							value = EXCLUDED.value",
							cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveCharacterAchievements", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacterAchievements",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacterAchievements",
						"A database error occurred.",
						$"Database error: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterAchievementEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteCharacterAchievements", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAchievements",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAchievements",
						"A database error occurred.",
						$"Database error: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAchievementData>>> GetAchievementsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var achievements = await dbContext.CharacterAchievements
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => new CharacterAchievementData
					{
						ID = a.ID,
						CharacterID = a.CharacterID,
						TemplateID = a.TemplateID,
						Tier = a.Tier,
						Value = a.Value
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.Success(achievements);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.FromException(
					new DatabaseTimeoutException("GetCharacterAchievements", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.FromException(
					new DatabaseQueryException(
						"GetCharacterAchievements",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterAchievementData>>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}
	}
}