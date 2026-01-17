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
	/// Character buff service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// All methods that use ExecuteSqlInterpolatedAsync are wrapped in execution strategies
	/// to provide automatic retry logic (up to 3 attempts) for transient database failures
	/// such as connection timeouts, deadlocks, or network interruptions.
	/// 
	/// Exception Handling Strategy:
	/// - Catches specific exceptions (NpgsqlException, DbUpdateException, TimeoutException)
	/// - Converts to custom DatabaseException hierarchy with sanitized messages
	/// - Returns DatabaseResult for safe, typed error handling
	/// - Preserves detailed error information for logging while exposing safe messages to clients
	/// </remarks>
	public sealed class CharacterBuffService : ICharacterBuffService
	{
		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBuffService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBuffService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveBuffsAsync(IEnumerable<CharacterBuffData> buffs, CancellationToken cancellationToken = default)
		{
			var buffList = buffs?.ToList();
			if (buffList == null || buffList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No buffs to save. Buffs collection must not be null or empty.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterBuffEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = buffList.Select(b => b.CharacterID).ToArray();
					var templateIds = buffList.Select(b => b.TemplateID).ToArray();
					var remainingTimes = buffList.Select(b => b.RemainingTime).ToArray();
					var tickTimes = buffList.Select(b => b.TickTime).ToArray();
					var stacks = buffList.Select(b => b.Stacks).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, template_id, remaining_time, tick_time, stacks)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[],
							{remainingTimes}::float4[],
							{tickTimes}::float4[],
							{stacks}::int[]
						)
						ON CONFLICT (character_id, template_id) DO UPDATE SET
							remaining_time = EXCLUDED.remaining_time,
							tick_time = EXCLUDED.tick_time,
							stacks = EXCLUDED.stacks",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveBuffs", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_buffs_character_id_template_id_key",
						"One or more buffs have conflicting template IDs.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_buffs_character_id_fkey",
						"One or more characters do not exist.",
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
						"SaveBuffs",
						"Failed to save buffs due to a database error.",
						$"DbUpdateException in SaveBuffsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveBuffs",
						"An unexpected error occurred while saving buffs.",
						$"Unexpected error in SaveBuffsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBuffsAsync(long characterId, CancellationToken cancellationToken = default)
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
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterBuffEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteBuffs", 10));
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
						"DeleteBuffs",
						"Failed to delete buffs due to a database error.",
						$"DbUpdateException in DeleteBuffsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteBuffs",
						"An unexpected error occurred while deleting buffs.",
						$"Unexpected error in DeleteBuffsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBuffData>>> GetBuffsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var buffs = await dbContext.CharacterBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId)
					.Select(b => new CharacterBuffData
					{
						ID = b.ID,
						CharacterID = b.CharacterID,
						TemplateID = b.TemplateID,
						RemainingTime = b.RemainingTime,
						TickTime = b.TickTime,
						Stacks = b.Stacks
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.Success(buffs);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.FromException(
					new DatabaseTimeoutException("GetBuffs", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.FromException(
					new DatabaseQueryException(
						"GetBuffs",
						"Failed to retrieve buffs.",
						$"Unexpected error in GetBuffsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}