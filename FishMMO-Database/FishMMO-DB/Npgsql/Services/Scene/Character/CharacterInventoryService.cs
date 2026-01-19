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
	public sealed class CharacterInventoryService : BaseService<CharacterInventoryEntity>, ICharacterInventoryService
	{
		/// <summary>
		/// Compiled query for retrieving character inventory (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterInventoryEntity>>> GetInventoryItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterInventoryItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterInventoryService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterInventoryService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveInventoryItemAsync(CharacterInventoryData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync<long>(
				async (dbContext, strategy) =>
				{
					var result = await strategy.ExecuteAsync(async () =>
					{
						// Use PostgreSQL UPSERT for atomic insert-or-update
						return await dbContext.CharacterInventoryItems
							.FromSqlInterpolated($@"
							INSERT INTO {TableName} 
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
						throw new InvalidOperationException("Failed to save item");
					}

					return result.ID;
				},
				"SaveInventoryItem",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveInventoryItemsAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null items collection");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Extract arrays for bulk UPSERT
				var characterIds = itemList.Select(i => i.CharacterID).ToArray();
				var templateIds = itemList.Select(i => i.TemplateID).ToArray();
				var slots = itemList.Select(i => i.Slot).ToArray();
				var seeds = itemList.Select(i => i.Seed).ToArray();
				var amounts = itemList.Select(i => (int)i.Amount).ToArray();

				// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} (character_id, template_id, slot, seed, amount)
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
			}, "SaveInventoryItems", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
					cancellationToken);
			}, "DeleteInventoryItems", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {characterId} AND slot = {slot}",
					cancellationToken);
			}, "DeleteInventorySlot", cancellationToken);
		}
		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> GetInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					var entities = await GetInventoryItemsQuery(dbContext, characterId, cancellationToken);
					var items = entities.Select(i => new CharacterInventoryData(
						id: i.ID,
						characterID: i.CharacterID,
						templateID: i.TemplateID,
						slot: i.Slot,
						seed: i.Seed,
						amount: i.Amount
					)).ToList();

					return (IReadOnlyList<CharacterInventoryData>)items;
				},
				"GetInventoryItems",
				cancellationToken);
		}
	}
}