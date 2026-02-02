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
	/// - Batch inventory save/update with explicit transactions
	/// - Inventory deletion (bulk and slot-specific)
	/// - Inventory retrieval
	/// 
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseOperationCanceledException
	/// - PostgresException (23505) → DatabaseConstraintException (Unique constraint conflict; non-transient failure)
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// Unique constraint violations are not used as normal control flow; write paths prefer deterministic SQL (e.g. UPSERT) where appropriate.
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

			if (item.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"INSERT INTO {TableName}
					(character_id, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, FALSE, NULL)
					ON CONFLICT (character_id, slot)
					DO UPDATE SET
						template_id = EXCLUDED.template_id,
						seed = EXCLUDED.seed,
						amount = EXCLUDED.amount,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version
						OR (
							EXCLUDED.version = {TableName}.version
							AND {TableName}.template_id = EXCLUDED.template_id
							AND {TableName}.seed = EXCLUDED.seed
							AND {TableName}.amount = EXCLUDED.amount
							AND {TableName}.deleted = FALSE
							AND {TableName}.time_deleted IS NULL
						)
					RETURNING id, version, character_id, template_id, slot, seed, amount, time_created, deleted, time_deleted";

				var upserted = await dbContext.CharacterInventoryItems
					.FromSqlRaw(
						sql,
						item.CharacterID,
						item.Slot,
						item.Version,
						item.TemplateID,
						item.Seed,
						item.Amount,
						now)
					.AsNoTracking()
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (upserted == null)
				{
					throw new StaleStateException("Inventory item was rejected due to a stale Version.");
				}

				return upserted.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<long>.Success(result.Data)
				: DatabaseResult<long>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			if (itemList.Any(i => i.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more inventory items had an invalid Version. Version must be greater than 0.",
					isTransient: false);
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

				var sql = GetUpsertSql();

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeItems.Count,
					new object[] { characterIdArray, slotArray, versionArray, templateIdArray, seedArray, amountArray, now },
					"One or more inventory items were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private string GetUpsertSql()
		{
			return $@"
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
					version = EXCLUDED.version
				WHERE
					EXCLUDED.version > {TableName}.version
					OR (
						EXCLUDED.version = {TableName}.version
						AND {TableName}.template_id = EXCLUDED.template_id
						AND {TableName}.seed = EXCLUDED.seed
						AND {TableName}.amount = EXCLUDED.amount
						AND {TableName}.deleted = FALSE
						AND {TableName}.time_deleted IS NULL
					);";
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActiveItems = await dbContext.CharacterInventoryItems
						.AsNoTracking()
						.AnyAsync(i => i.CharacterID == characterId && !i.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActiveItems)
					{
						throw new StaleStateException("Inventory delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND slot = {{3}} AND deleted = FALSE AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId, slot }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var item = await dbContext.CharacterInventoryItems
						.AsNoTracking()
						.FirstOrDefaultAsync(i => i.CharacterID == characterId && i.Slot == slot, cancellationToken)
						.ConfigureAwait(false);

					if (item != null && !item.Deleted)
					{
						throw new StaleStateException("Inventory slot delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
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