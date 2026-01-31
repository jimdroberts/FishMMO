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
		/// Compiled query for checking whether a character exists and is not deleted.
		/// Returns the character ID if active, otherwise 0.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<long>> getActiveCharacterIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId && !c.Deleted)
					.Select(c => c.ID)
					.FirstOrDefault());

		/// <summary>
		/// Compiled query for retrieving character inventory (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterInventoryEntity>>> getInventoryItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterInventoryItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted)
					.ToList());

		/// <summary>
		/// Compiled query for retrieving a tracked inventory item by character ID and slot.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, int, CancellationToken, Task<CharacterInventoryEntity?>> getByCharacterAndSlotTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, int slot, CancellationToken ct) =>
				(CharacterInventoryEntity?)context.CharacterInventoryItems
					.FirstOrDefault(i => i.CharacterID == characterId && i.Slot == slot));

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
			if (item.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var entity = new CharacterInventoryEntity
				{
					CharacterID = item.CharacterID,
					Version = item.Version,
					TemplateID = item.TemplateID,
					Slot = item.Slot,
					Seed = item.Seed,
					Amount = item.Amount,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.CharacterInventoryItems.AddAsync(entity, cancellationToken).ConfigureAwait(false);
				return entity;
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult<long>.Success(insertResult.Data.ID);
			}

			if (insertResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult<long>.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var entity = await getByCharacterAndSlotTrackingQuery(dbContext, item.CharacterID, item.Slot, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterInventory", $"(CharacterID: {item.CharacterID}, Slot: {item.Slot})");
				}

				ValidateVersion(entity, item.Version);
				if (entity.Deleted)
				{
					entity.Deleted = false;
					entity.TimeDeleted = null;
				}

				entity.TemplateID = item.TemplateID;
				entity.Seed = item.Seed;
				entity.Amount = item.Amount;
				return entity;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult<long>.Success(updateResult.Data.ID)
				: DatabaseResult<long>.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveInventoryItemsAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null items collection",
					isTransient: false);
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

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = itemList.Select(i => i.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeItems = itemList.Where(i => activeCharacterIdSet.Contains(i.CharacterID)).ToList();
				if (activeItems.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeItems.Select(i => i.CharacterID).ToArray();
				var slotArray = activeItems.Select(i => i.Slot).ToArray();
				var versionArray = activeItems.Select(i => i.Version).ToArray();
				var templateIdArray = activeItems.Select(i => i.TemplateID).ToArray();
				var seedArray = activeItems.Select(i => i.Seed).ToArray();
				var amountArray = activeItems.Select(i => i.Amount).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.slot,
						u.version,
						u.template_id,
						u.seed,
						u.amount,
						{{6}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::integer[],
						{{4}}::integer[],
						{{5}}::integer[]
					) AS u(character_id, slot, version, template_id, seed, amount)
					ON CONFLICT (character_id, slot)
					DO UPDATE SET
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount,
						deleted = FALSE,
						time_deleted = NULL,
						version = CASE
							WHEN EXCLUDED.version > 0 THEN EXCLUDED.version
							ELSE {TableName}.version
						END
					WHERE EXCLUDED.version <= 0 OR EXCLUDED.version > {TableName}.version;";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeItems.Count,
					new object[] { characterIdArray, slotArray, versionArray, templateIdArray, seedArray, amountArray, now },
					"One or more inventory items were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND slot = {{2}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId, slot }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> GetInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getInventoryItemsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var items = entities.Select(i => new CharacterInventoryData(
					id: i.ID,
					version: i.Version,
					characterID: i.CharacterID,
					templateID: i.TemplateID,
					slot: i.Slot,
					seed: i.Seed,
					amount: i.Amount
				)).ToList();

				return (IReadOnlyList<CharacterInventoryData>)items;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}