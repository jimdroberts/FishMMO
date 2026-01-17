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
	/// Character equipment service with async operations, atomic SQL, and DTO pattern.
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
	public sealed class CharacterEquipmentService : ICharacterEquipmentService
	{
		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterEquipmentService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterEquipmentService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveEquipmentAsync(CharacterEquipmentData equipment, CancellationToken cancellationToken = default)
		{
			if (equipment.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					// Use PostgreSQL UPSERT for atomic insert-or-update
					var tableName = dbContext.GetTableName<CharacterEquipmentEntity>();
					return await dbContext.CharacterEquippedItems
						.FromSqlInterpolated($@"
						INSERT INTO {tableName} 
							(character_id, template_id, slot, seed, amount)
						VALUES 
							({equipment.CharacterID}, {equipment.TemplateID}, {equipment.Slot}, {equipment.Seed}, {equipment.Amount})
						ON CONFLICT (character_id, slot) 
						DO UPDATE SET 
							template_id = EXCLUDED.template_id,
							seed = EXCLUDED.seed,
							amount = EXCLUDED.amount
						RETURNING id, character_id, template_id, slot, seed, amount")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);
				});

				if (result == null)
				{
					return DatabaseResult<long>.FromException(
						new DatabaseQueryException(
							"SaveEquipment",
							"Failed to save equipment.",
							"UPSERT returned null result",
							isTransient: false));
				}

				return DatabaseResult<long>.Success(result.ID);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseTimeoutException("SaveEquipment", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_equipment_character_id_slot_key",
						"An equipment item already exists in this slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_equipment_character_id_fkey",
						"Character does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveEquipment",
						"Failed to save equipment due to a database error.",
						$"DbUpdateException in SaveEquipmentAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveEquipment",
						"An unexpected error occurred while saving equipment.",
						$"Unexpected error in SaveEquipmentAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveEquipmentMultipleAsync(IEnumerable<CharacterEquipmentData> equipment, CancellationToken cancellationToken = default)
		{
			var equipmentList = equipment?.ToList();
			if (equipmentList == null || equipmentList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null equipment collection.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterEquipmentEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = equipmentList.Select(e => e.CharacterID).ToArray();
					var templateIds = equipmentList.Select(e => e.TemplateID).ToArray();
					var slots = equipmentList.Select(e => e.Slot).ToArray();
					var seeds = equipmentList.Select(e => e.Seed).ToArray();
					var amounts = equipmentList.Select(e => (int)e.Amount).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, template_id, slot, seed, amount)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[],
							{slots}::int[],
							{seeds}::int[],
							{amounts}::int[]
						)
						ON CONFLICT (character_id, slot) DO UPDATE SET
							template_id = EXCLUDED.template_id,
							seed = EXCLUDED.seed,
							amount = EXCLUDED.amount",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveEquipmentMultiple", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_equipment_character_id_slot_key",
						"An equipment item already exists in this slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_equipment_character_id_fkey",
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
						"SaveEquipmentMultiple",
						"Failed to save equipment due to a database error.",
						$"DbUpdateException in SaveEquipmentMultipleAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveEquipmentMultiple",
						"An unexpected error occurred while saving equipment.",
						$"Unexpected error in SaveEquipmentMultipleAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterEquipmentEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteEquipment", 10));
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
						"DeleteEquipment",
						"Failed to delete equipment due to a database error.",
						$"DbUpdateException in DeleteEquipmentAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteEquipment",
						"An unexpected error occurred while deleting equipment.",
						$"Unexpected error in DeleteEquipmentAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteEquipmentSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterEquipmentEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId} AND slot = {slot}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteEquipmentSlot", 10));
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
						"DeleteEquipmentSlot",
						"Failed to delete equipment slot due to a database error.",
						$"DbUpdateException in DeleteEquipmentSlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteEquipmentSlot",
						"An unexpected error occurred while deleting equipment slot.",
						$"Unexpected error in DeleteEquipmentSlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> GetEquipmentAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var equipment = await dbContext.CharacterEquippedItems
					.AsNoTracking()
					.Where(e => e.CharacterID == characterId)
					.Select(e => new CharacterEquipmentData
					{
						ID = e.ID,
						CharacterID = e.CharacterID,
						TemplateID = e.TemplateID,
						Slot = e.Slot,
						Seed = e.Seed,
						Amount = e.Amount
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.Success(equipment);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.FromException(
					new DatabaseTimeoutException("GetEquipment", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterEquipmentData>>.FromException(
					new DatabaseQueryException(
						"GetEquipment",
						"An unexpected error occurred while retrieving equipment.",
						$"Unexpected error in GetEquipmentAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}