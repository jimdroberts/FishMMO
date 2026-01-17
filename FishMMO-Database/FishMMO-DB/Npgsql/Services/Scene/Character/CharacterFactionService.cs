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
	/// Service for managing character factions in the database.
	/// Provides async operations for CRUD operations on character faction reputation data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character faction reputation including:
	/// - Batch faction save/update with atomic UPSERT operations
	/// - Faction deletion (bulk operations)
	/// - Faction retrieval
	/// 
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseTimeoutException
	/// - PostgresException (23505) → DatabaseConstraintException (Unique violation)
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// </remarks>
	public sealed class CharacterFactionService : ICharacterFactionService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterFactionService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterFactionService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveFactionsAsync(IEnumerable<CharacterFactionData> factions, CancellationToken cancellationToken = default)
		{
			var factionList = factions?.ToList();
			if (factionList == null || factionList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null factions collection.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterFactionEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = factionList.Select(f => f.CharacterID).ToArray();
					var templateIds = factionList.Select(f => f.TemplateID).ToArray();
					var values = factionList.Select(f => f.Value).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, template_id, value)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[],
							{values}::int[]
						)
						ON CONFLICT (character_id, template_id) DO UPDATE SET
							value = EXCLUDED.value",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveFactions", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_faction_character_id_template_id_key",
						"A faction with this character and template ID already exists.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_faction_character_id_fkey",
						"Character does not exist.",
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
						"SaveFactions",
						"Failed to save factions due to a database error.",
						$"DbUpdateException in SaveFactionsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveFactions",
						"An unexpected error occurred while saving factions.",
						$"Unexpected error in SaveFactionsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFactionsAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterFactionEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteFactions", 10));
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
						"DeleteFactions",
						"Failed to delete factions due to a database error.",
						$"DbUpdateException in DeleteFactionsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteFactions",
						"An unexpected error occurred while deleting factions.",
						$"Unexpected error in DeleteFactionsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> GetFactionsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var factions = await dbContext.CharacterFactions
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
					.Select(f => new CharacterFactionData
					{
						ID = f.ID,
						CharacterID = f.CharacterID,
						TemplateID = f.TemplateID,
						Value = f.Value
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.Success(factions);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.FromException(
					new DatabaseTimeoutException("GetFactions", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterFactionData>>.FromException(
					new DatabaseQueryException(
						"GetFactions",
						"An unexpected error occurred while retrieving factions.",
						$"Unexpected error in GetFactionsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}