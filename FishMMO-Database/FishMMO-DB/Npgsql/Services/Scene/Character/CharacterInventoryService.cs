using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
	/// - OperationCanceledException → DatabaseOperationCanceledException
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

			return await ExecuteAsync<long>(
				async (dbContext, ct) =>
				{
					var charactersTableName = dbContext.GetTableName<CharacterEntity>();
					// Use PostgreSQL UPSERT for atomic insert-or-update
					var result = await dbContext.CharacterInventoryItems
						.FromSqlRaw($@"
						WITH active_character AS (
							SELECT id
							FROM {charactersTableName}
							WHERE id = {{0}} AND deleted = FALSE
							FOR KEY SHARE
						)
						INSERT INTO {TableName} 
							(character_id, template_id, slot, seed, amount, time_created)
						SELECT
							{{0}}, {{1}}, {{2}}, {{3}}, {{4}}, CURRENT_TIMESTAMP
						FROM active_character
						ON CONFLICT (character_id, slot) 
						DO UPDATE SET 
							template_id = EXCLUDED.template_id,
							seed = EXCLUDED.seed,
							amount = EXCLUDED.amount
						RETURNING id, character_id, template_id, slot, seed, amount, time_created",
						item.CharacterID,
						item.TemplateID,
						item.Slot,
						item.Seed,
						item.Amount)
						.AsNoTracking()
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

					if (result == null)
					{
						throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
					}

					return result.ID;
				},
				"SaveInventoryItem",
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveInventoryItemsAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null items collection");
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (itemList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Slot), CharacterInventoryData>();
				foreach (var item in itemList)
				{
					deduped[(item.CharacterID, item.Slot)] = item;
				}

				if (deduped.Count != itemList.Count)
				{
					itemList = deduped.Values.ToList();
				}
			}

			// Extract arrays for bulk UPSERT
			var characterIds = itemList.Select(i => i.CharacterID).ToArray();
			var templateIds = itemList.Select(i => i.TemplateID).ToArray();
			var slots = itemList.Select(i => i.Slot).ToArray();
			var seeds = itemList.Select(i => i.Seed).ToArray();
			var amounts = itemList.Select(i => (int)i.Amount).ToArray();

			var result = await ExecuteAsync(async (dbContext, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				return await dbContext.Database.ExecuteSqlRawAsync(
					$@"WITH active_characters AS (
						SELECT id
						FROM {charactersTableName}
						WHERE id = ANY({{0}}::bigint[]) AND deleted = FALSE
						FOR KEY SHARE
					)
					INSERT INTO {TableName} (character_id, template_id, slot, seed, amount, time_created)
					SELECT u.character_id, u.template_id, u.slot, u.seed, u.amount, CURRENT_TIMESTAMP
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::int[],
						{{2}}::int[],
						{{3}}::int[],
						{{4}}::int[]
					) AS u(character_id, template_id, slot, seed, amount)
					JOIN active_characters ac ON ac.id = u.character_id
					ON CONFLICT (character_id, slot) DO UPDATE SET
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount",
					new object[] { characterIds, templateIds, slots, seeds, amounts },
					ct);
			}, "SaveInventoryItems", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteInventoryItems",
				new object[] { characterId },
				entityName: "CharacterInventory",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}} AND slot = {{1}}",
				"DeleteInventorySlot",
				new object[] { characterId, slot },
				entityName: "CharacterInventory",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> GetInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync(
				async (dbContext, ct) =>
				{
					var entities = await GetInventoryItemsQuery(dbContext, characterId, ct).ConfigureAwait(false);
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
				cancellationToken).ConfigureAwait(false);
		}
	}
}