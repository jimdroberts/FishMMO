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
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class CharacterPetService : ICharacterPetService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPetService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPetService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePetAsync(CharacterPetData petData, CancellationToken cancellationToken = default)
		{
			if (petData.CharacterID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				if (petData.ID > 0)
				{
					var rowsAffected = await strategy.ExecuteAsync(async () =>
					{
						var tableName = dbContext.GetTableName<CharacterPetEntity>();
						// Use atomic UPDATE for thread safety
						return await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"UPDATE {tableName} 
							   SET character_id = {petData.CharacterID},
							       template_id = {petData.TemplateID},
							       abilities = {petData.Abilities},
							       spawned = {petData.Spawned}
							   WHERE id = {petData.ID}",
							cancellationToken);
					});

					if (rowsAffected == 0)
					{
						return DatabaseResult.FromException(
							new DatabaseEntityNotFoundException(
								"Pet",
								petData.ID.ToString(),
								"Pet not found"));
					}
				}
				else
				{
					await strategy.ExecuteAsync(async () =>
					{
						var petEntity = new CharacterPetEntity
						{
							CharacterID = petData.CharacterID,
							TemplateID = petData.TemplateID,
							Abilities = petData.Abilities,
							Spawned = petData.Spawned
						};
						dbContext.CharacterPets.Add(petEntity);
						await dbContext.SaveChangesAsync(cancellationToken);
					});
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SavePet", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_pets_constraint",
						"Pet constraint violation.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_pets_character_id_fkey",
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
						"SavePet",
						"Failed to save pet due to a database error.",
						$"DbUpdateException in SavePetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SavePet",
						"An unexpected error occurred while saving pet.",
						$"Unexpected error in SavePetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <summary>
		/// Saves multiple pets (helper method, not in interface).
		/// </summary>
		/// <param name="pets">Collection of pet data to save.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A tuple containing:
		/// - Success: true if all pets were saved successfully, false otherwise.
		/// - ErrorReason: null on success, or a detailed error message on failure.
		/// </returns>
		public async Task<(bool Success, string? ErrorReason)> SavePetsAsync(IEnumerable<CharacterPetData> pets, CancellationToken cancellationToken = default)
		{
			var petList = pets?.Where(p => p.CharacterID > 0).ToList();
			if (petList == null || petList.Count == 0)
			{
				return (false, "Empty or null pets collection");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				// Execute multiple operations atomically within a transaction
				// Transaction ensures all-or-nothing semantics for UPDATE + INSERT
				// Execution strategy retries entire transaction block on transient failures
				await strategy.ExecuteAsync(async () =>
				{
					await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

					var tableName = dbContext.GetTableName<CharacterPetEntity>();

					// Separate pets into updates and inserts
					var petsToUpdate = petList.Where(p => p.ID > 0).ToList();
					var petsToInsert = petList.Where(p => p.ID == 0).ToList();

					// Bulk UPDATE for existing pets
					if (petsToUpdate.Count > 0)
					{
						var updateIds = petsToUpdate.Select(p => p.ID).ToArray();
						var updateCharacterIds = petsToUpdate.Select(p => p.CharacterID).ToArray();
						var updateTemplateIds = petsToUpdate.Select(p => p.TemplateID).ToArray();
						var updateAbilities = petsToUpdate.Select(p => p.Abilities.ToArray()).ToArray();
						var updateSpawned = petsToUpdate.Select(p => p.Spawned).ToArray();

						await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"UPDATE {tableName} AS t SET
								character_id = u.character_id,
								template_id = u.template_id,
								abilities = u.abilities,
								spawned = u.spawned
							FROM (SELECT * FROM UNNEST(
								{updateIds}::bigint[],
								{updateCharacterIds}::bigint[],
								{updateTemplateIds}::int[],
								{updateAbilities}::int[][],
								{updateSpawned}::boolean[]
							) AS u(id, character_id, template_id, abilities, spawned)) AS u
							WHERE t.id = u.id",
							cancellationToken);
					}

					// Bulk INSERT for new pets
					if (petsToInsert.Count > 0)
					{
						var insertCharacterIds = petsToInsert.Select(p => p.CharacterID).ToArray();
						var insertTemplateIds = petsToInsert.Select(p => p.TemplateID).ToArray();
						var insertAbilities = petsToInsert.Select(p => p.Abilities.ToArray()).ToArray();
						var insertSpawned = petsToInsert.Select(p => p.Spawned).ToArray();

						await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"INSERT INTO {tableName} (character_id, template_id, abilities, spawned)
							SELECT * FROM UNNEST(
								{insertCharacterIds}::bigint[],
								{insertTemplateIds}::int[],
								{insertAbilities}::int[][],
								{insertSpawned}::boolean[]
							)",
							cancellationToken);
					}

					// Commit transaction - auto-rollback on exception
					await transaction.CommitAsync(cancellationToken);
				});

				return (true, null);
			}
			catch (DbUpdateException ex)
			{
				return (false, $"Database error: {ex.Message}");
			}
			catch (Exception ex)
			{
				return (false, $"Unexpected error: {ex.Message}");
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePetAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterPetEntity>();

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
					new DatabaseTimeoutException("DeletePet", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_pets_constraint",
						"Constraint violation while deleting pet.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_pets_constraint",
						"Cannot delete pet due to foreign key constraint.",
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
						"DeletePet",
						"Failed to delete pet due to a database error.",
						$"DbUpdateException in DeletePetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeletePet",
						"An unexpected error occurred while deleting pet.",
						$"Unexpected error in DeletePetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <summary>
		/// Deletes all pets for a character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A tuple containing:
		/// - Success: true if all pets were deleted successfully, false otherwise.
		/// - ErrorReason: null on success, or a detailed error message on failure.
		/// </returns>
		public async Task<(bool Success, string? ErrorReason)> DeleteAllPetsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return (false, "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterPetEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return (true, null);
			}
			catch (DbUpdateException ex)
			{
				return (false, $"Database error: {ex.Message}");
			}
			catch (Exception ex)
			{
				return (false, $"Unexpected error: {ex.Message}");
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> GetPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var pet = await dbContext.CharacterPets
					.AsNoTracking()
					.Where(p => p.CharacterID == characterId)
					.Select(p => new CharacterPetData
					{
						ID = p.ID,
						CharacterID = p.CharacterID,
						TemplateID = p.TemplateID,
						Abilities = p.Abilities,
						Spawned = p.Spawned
					})
					.FirstOrDefaultAsync(cancellationToken);

				return DatabaseResult<CharacterPetData?>.Success(pet);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseTimeoutException("GetPet", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_pets_constraint",
						"Constraint violation while retrieving pet.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_pets_constraint",
						"Foreign key constraint issue while retrieving pet.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseQueryException(
						"GetPet",
						"Failed to retrieve pet due to a database error.",
						$"DbUpdateException in GetPetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseQueryException(
						"GetPet",
						"An unexpected error occurred while retrieving pet.",
						$"Unexpected error in GetPetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPetData?>> GetSpawnedPetAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPetData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var pet = await dbContext.CharacterPets
					.AsNoTracking()
					.Where(p => p.CharacterID == characterId && p.Spawned)
					.Select(p => new CharacterPetData
					{
						ID = p.ID,
						CharacterID = p.CharacterID,
						TemplateID = p.TemplateID,
						Abilities = p.Abilities,
						Spawned = p.Spawned
					})
					.FirstOrDefaultAsync(cancellationToken);

				return DatabaseResult<CharacterPetData?>.Success(pet);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseTimeoutException("GetSpawnedPet", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_pets_constraint",
						"Constraint violation while retrieving spawned pet.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_pets_constraint",
						"Foreign key constraint issue while retrieving spawned pet.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseQueryException(
						"GetSpawnedPet",
						"Failed to retrieve spawned pet due to a database error.",
						$"DbUpdateException in GetSpawnedPetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterPetData?>.FromException(
					new DatabaseQueryException(
						"GetSpawnedPet",
						"An unexpected error occurred while retrieving spawned pet.",
						$"Unexpected error in GetSpawnedPetAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}