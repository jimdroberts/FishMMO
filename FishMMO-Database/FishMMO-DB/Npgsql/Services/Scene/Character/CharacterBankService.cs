using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

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
	/// This service manages character bank storage including:
	/// - Single-item save/update via atomic UPSERT (INSERT ... ON CONFLICT DO UPDATE)
	/// - Batch save/update via UNNEST + UPSERT
	/// - Soft-delete operations (bulk and slot-specific)
	/// - Retrieval of current bank items
	/// 
	/// Write operations are executed inside the BaseService execution wrappers for retry and exception mapping.
	/// Version/authority semantics are enforced in UPSERT via <see cref="BaseService{TEntity}.ExecuteBulkUpsertAsync"/>.
	/// </remarks>
	public sealed class CharacterBankService : BaseService<CharacterBankEntity>, ICharacterBankService
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
		/// Compiled query for retrieving character bank items (hot path for bank access).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterBankEntity>>> getBankItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBankService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBankService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterBankData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			if (item.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
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
				await ExecuteBulkUpsertAsync(
					dbContext,
					GetUpsertSql(),
					expectedRowsAffected: 1,
					new object[]
					{
						new[] { item.CharacterID },
						new[] { item.Slot },
						new[] { item.Version },
						new[] { item.TemplateID },
						new[] { item.Seed },
						new[] { item.Amount },
						now,
					},
					"Bank item was rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);

				var id = await dbContext.CharacterBankItems
					.AsNoTracking()
					.Where(i => i.CharacterID == item.CharacterID && i.Slot == item.Slot && !i.Deleted)
					.Select(i => i.ID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (id <= 0)
				{
					throw new DatabaseEntityNotFoundException("CharacterBankItem", $"(CharacterID: {item.CharacterID}, Slot: {item.Slot})");
				}

				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterBankData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"No items to save. Items collection must not be null or empty.",
					isTransient: false);
			}

			if (itemList.Any(i => i.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more bank items had an invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (itemList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Slot), CharacterBankData>();
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

				if (activeCharacterIdSet.Count != characterIds.Length)
				{
					var missingCharacterId = characterIds.First(id => !activeCharacterIdSet.Contains(id));
					throw new DatabaseEntityNotFoundException("Character", missingCharacterId.ToString(), "Character not found or deleted.");
				}

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
					"One or more bank items were rejected due to a stale Version.",
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
					EXCLUDED.version > {TableName}.version;";
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
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
					var anyActiveItems = await dbContext.CharacterBankItems
						.AsNoTracking()
						.AnyAsync(i => i.CharacterID == characterId && !i.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActiveItems)
					{
						throw new StaleStateException("Bank delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
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
					var stillActive = await dbContext.CharacterBankItems
						.AsNoTracking()
						.AnyAsync(i => i.CharacterID == characterId && i.Slot == slot && !i.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Bank slot delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBankData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getBankItemsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var items = entities.Select(i => new CharacterBankData(
					id: i.ID,
					version: i.Version,
					characterID: i.CharacterID,
					templateID: i.TemplateID,
					slot: i.Slot,
					seed: i.Seed,
					amount: i.Amount
				)).ToList();

				return (IReadOnlyList<CharacterBankData>)items;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}