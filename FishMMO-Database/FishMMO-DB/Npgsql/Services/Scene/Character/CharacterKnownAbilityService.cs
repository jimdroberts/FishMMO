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
	/// Service for managing character known abilities in the database.
	/// Provides async operations for CRUD operations on character known ability data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character known abilities including:
	/// - Single known ability save with atomic INSERT ON CONFLICT operations
	/// - Batch known ability save with transactions
	/// - Known ability deletion (single and bulk operations)
	/// - Known ability retrieval
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
	public sealed class CharacterKnownAbilityService : ICharacterKnownAbilityService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterKnownAbilityService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterKnownAbilityService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterKnownAbilityEntity>();

					// Use atomic INSERT with ON CONFLICT DO NOTHING for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, template_id)
						   VALUES ({characterId}, {templateId})
						   ON CONFLICT (character_id, template_id) DO NOTHING",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveKnownAbility", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_known_abilities_character_id_template_id_key",
						"This ability is already known by the character.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_known_abilities_character_id_fkey",
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
						"SaveKnownAbility",
						"Failed to save known ability due to a database error.",
						$"DbUpdateException in SaveKnownAbilityAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveKnownAbility",
						"An unexpected error occurred while saving known ability.",
						$"Unexpected error in SaveKnownAbilityAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveKnownAbilitiesAsync(IEnumerable<CharacterKnownAbilityData> knownAbilities, CancellationToken cancellationToken = default)
		{
			var abilityList = knownAbilities?.ToList();
			if (abilityList == null || abilityList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null abilities collection");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterKnownAbilityEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = abilityList.Select(a => a.CharacterID).ToArray();
					var templateIds = abilityList.Select(a => a.TemplateID).ToArray();

					// Single bulk INSERT using UNNEST with ON CONFLICT - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, template_id)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[]
						)
						ON CONFLICT (character_id, template_id) DO NOTHING",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveKnownAbilities", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_known_abilities_character_id_template_id_key",
						"One or more abilities are already known by the character.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_known_abilities_character_id_fkey",
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
						"SaveKnownAbilities",
						"Failed to save known abilities due to a database error.",
						$"DbUpdateException in SaveKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveKnownAbilities",
						"An unexpected error occurred while saving known abilities.",
						$"Unexpected error in SaveKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterKnownAbilityEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} 
						   WHERE character_id = {characterId} AND template_id = {templateId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteKnownAbility", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_known_abilities_constraint",
						"Constraint violation while deleting known ability.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_known_abilities_constraint",
						"Cannot delete known ability due to foreign key constraint.",
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
						"DeleteKnownAbility",
						"Failed to delete known ability due to a database error.",
						$"DbUpdateException in DeleteKnownAbilityAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteKnownAbility",
						"An unexpected error occurred while deleting known ability.",
						$"Unexpected error in DeleteKnownAbilityAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterKnownAbilityEntity>();

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
					new DatabaseTimeoutException("DeleteAllKnownAbilities", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_known_abilities_constraint",
						"Constraint violation while deleting all known abilities.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_known_abilities_constraint",
						"Cannot delete all known abilities due to foreign key constraint.",
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
						"DeleteAllKnownAbilities",
						"Failed to delete all known abilities due to a database error.",
						$"DbUpdateException in DeleteAllKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteAllKnownAbilities",
						"An unexpected error occurred while deleting all known abilities.",
						$"Unexpected error in DeleteAllKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> GetKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var abilities = await dbContext.CharacterKnownAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => new CharacterKnownAbilityData
					{
						ID = a.ID,
						CharacterID = a.CharacterID,
						TemplateID = a.TemplateID
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.Success(abilities);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseTimeoutException("GetKnownAbilities", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_known_abilities_constraint",
						"Constraint violation while retrieving known abilities.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_known_abilities_constraint",
						"Foreign key constraint issue while retrieving known abilities.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseQueryException(
						"GetKnownAbilities",
						"Failed to retrieve known abilities due to a database error.",
						$"DbUpdateException in GetKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>.FromException(
					new DatabaseQueryException(
						"GetKnownAbilities",
						"An unexpected error occurred while retrieving known abilities.",
						$"Unexpected error in GetKnownAbilitiesAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}