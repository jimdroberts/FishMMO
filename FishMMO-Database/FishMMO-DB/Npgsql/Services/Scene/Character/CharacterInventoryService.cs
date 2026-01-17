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
	/// Service for managing character inventory items in the database.
	/// Provides async operations for CRUD operations on character inventory data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character inventory including:
	/// - Single inventory item save/update with atomic UPSERT operations
	/// - Batch inventory save/update with transactions
	/// - Inventory deletion (bulk and slot-specific)
	/// - Inventory retrieval
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
	public sealed class CharacterInventoryService : ICharacterInventoryService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterInventoryService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterInventoryService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveInventoryItemAsync(CharacterInventoryData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterInventoryEntity>();

					// Use PostgreSQL UPSERT for atomic insert-or-update
					return await dbContext.CharacterInventoryItems
						.FromSqlInterpolated($@"
						INSERT INTO {tableName} 
							(character_id, template_id, slot, seed, amount)
						VALUES 
							({item.CharacterID}, {item.TemplateID}, {item.Slot}, {item.Seed}, {item.Amount})
						ON CONFLICT (character_id, slot) 
						DO UPDATE SET 
							template_id = EXCLUDED.template_id,
							seed = EXCLUDED.seed,
							amount = EXCLUDED.amount
						RETURNING id, character_id, template_id, slot, seed, amount")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);
				});

				if (result == null || result.ID == 0)
				{
					return DatabaseResult<long>.Failure("SAVE_FAILED", "Failed to save item");
				}

				return DatabaseResult<long>.Success(result.ID);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseTimeoutException("SaveInventoryItem", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_inventory_items_character_id_slot_key",
						"An inventory item already exists in this slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_inventory_items_character_id_fkey",
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
						"SaveInventoryItem",
						"Failed to save inventory item due to a database error.",
						$"DbUpdateException in SaveInventoryItemAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveInventoryItem",
						"An unexpected error occurred while saving the inventory item.",
						$"Unexpected error in SaveInventoryItemAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveInventoryItemsAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null items collection");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterInventoryEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = itemList.Select(i => i.CharacterID).ToArray();
					var templateIds = itemList.Select(i => i.TemplateID).ToArray();
					var slots = itemList.Select(i => i.Slot).ToArray();
					var seeds = itemList.Select(i => i.Seed).ToArray();
					var amounts = itemList.Select(i => (int)i.Amount).ToArray();

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
					new DatabaseTimeoutException("SaveInventoryItems", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_inventory_items_character_id_slot_key",
						"An inventory item already exists in one of the slots.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_inventory_items_character_id_fkey",
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
						"SaveInventoryItems",
						"Failed to save inventory items due to a database error.",
						$"DbUpdateException in SaveInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveInventoryItems",
						"An unexpected error occurred while saving inventory items.",
						$"Unexpected error in SaveInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterInventoryEntity>();

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
					new DatabaseTimeoutException("DeleteInventoryItems", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_inventory_constraint",
						"Constraint violation while deleting inventory items.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_inventory_constraint",
						"Cannot delete inventory items due to foreign key constraint.",
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
						"DeleteInventoryItems",
						"Failed to delete inventory items due to a database error.",
						$"DbUpdateException in DeleteInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteInventoryItems",
						"An unexpected error occurred while deleting inventory items.",
						$"Unexpected error in DeleteInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterInventoryEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId} AND slot = {slot}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteInventorySlot", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_inventory_constraint",
						"Constraint violation while deleting inventory slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_inventory_constraint",
						"Cannot delete inventory slot due to foreign key constraint.",
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
						"DeleteInventorySlot",
						"Failed to delete inventory slot due to a database error.",
						$"DbUpdateException in DeleteInventorySlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteInventorySlot",
						"An unexpected error occurred while deleting inventory slot.",
						$"Unexpected error in DeleteInventorySlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> GetInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var items = await dbContext.CharacterInventoryItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId)
					.Select(i => new CharacterInventoryData
					{
						ID = i.ID,
						CharacterID = i.CharacterID,
						TemplateID = i.TemplateID,
						Slot = i.Slot,
						Seed = i.Seed,
						Amount = i.Amount
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Success(items);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseTimeoutException("GetInventoryItems", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_inventory_constraint",
						"Constraint violation while retrieving inventory items.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_inventory_constraint",
						"Foreign key constraint issue while retrieving inventory items.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseQueryException(
						"GetInventoryItems",
						"Failed to retrieve inventory items due to a database error.",
						$"DbUpdateException in GetInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.FromException(
					new DatabaseQueryException(
						"GetInventoryItems",
						"An unexpected error occurred while retrieving inventory items.",
						$"Unexpected error in GetInventoryItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}