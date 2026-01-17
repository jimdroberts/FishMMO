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
	/// Character bank service with async operations, atomic SQL, and DTO pattern.
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
	public sealed class CharacterBankService : ICharacterBankService
	{
		/// <summary>
		/// Factory for creating database context instances with proper connection pooling and retry configuration.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBankService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBankService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveBankItemAsync(CharacterBankData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID == 0)
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
					var tableName = dbContext.GetTableName<CharacterBankEntity>();

					// Use PostgreSQL UPSERT for atomic insert-or-update
					return await dbContext.CharacterBankItems
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

				if (result == null)
				{
					return DatabaseResult<long>.FromException(
						new DatabaseQueryException(
							"SaveBankItem",
							"Failed to save bank item.",
							"UPSERT returned null result",
							isTransient: false));
				}

				return DatabaseResult<long>.Success(result.ID);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseTimeoutException("SaveBankItem", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_bank_items_character_id_slot_key",
						"A bank item already exists in this slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_bank_items_character_id_fkey",
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
						"SaveBankItem",
						"Failed to save bank item due to a database error.",
						$"DbUpdateException in SaveBankItemAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveBankItem",
						"An unexpected error occurred while saving the bank item.",
						$"Unexpected error in SaveBankItemAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveBankItemsAsync(IEnumerable<CharacterBankData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No items to save. Items collection must not be null or empty.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterBankEntity>();

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
					new DatabaseTimeoutException("SaveBankItems", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_bank_items_character_id_slot_key",
						"One or more bank items have conflicting slots.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_bank_items_character_id_fkey",
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
						"SaveBankItems",
						"Failed to save bank items due to a database error.",
						$"DbUpdateException in SaveBankItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveBankItems",
						"An unexpected error occurred while saving bank items.",
						$"Unexpected error in SaveBankItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterBankEntity>();

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
					new DatabaseTimeoutException("DeleteBankItems", 10));
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
						"DeleteBankItems",
						"Failed to delete bank items due to a database error.",
						$"DbUpdateException in DeleteBankItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteBankItems",
						"An unexpected error occurred while deleting bank items.",
						$"Unexpected error in DeleteBankItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteBankSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterBankEntity>();

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
					new DatabaseTimeoutException("DeleteBankSlot", 10));
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
						"DeleteBankSlot",
						"Failed to delete bank slot due to a database error.",
						$"DbUpdateException in DeleteBankSlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteBankSlot",
						"An unexpected error occurred while deleting the bank slot.",
						$"Unexpected error in DeleteBankSlotAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var items = await dbContext.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId)
					.Select(i => new CharacterBankData
					{
						ID = i.ID,
						CharacterID = i.CharacterID,
						TemplateID = i.TemplateID,
						Slot = i.Slot,
						Seed = i.Seed,
						Amount = i.Amount
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterBankData>>.Success(items);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.FromException(
					new DatabaseTimeoutException("GetBankItems", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.FromException(
					new DatabaseQueryException(
						"GetBankItems",
						"Failed to retrieve bank items.",
						$"Unexpected error in GetBankItemsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}