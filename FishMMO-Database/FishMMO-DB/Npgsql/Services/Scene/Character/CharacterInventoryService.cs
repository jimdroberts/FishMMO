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
	/// All exceptions are classified by <c>BaseService</c> and mapped to <see cref="DatabaseResult"/> error codes
	/// (e.g., UNIQUE_VIOLATION, FOREIGN_KEY_VIOLATION, STALE_STATE, DATABASE_ERROR). Transient failures are retried automatically.
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// Unique constraint violations are not used as normal control flow; write paths prefer deterministic SQL (e.g. UPSERT) where appropriate.
	/// </remarks>
	/// <remarks>
	/// SLOT VERSIONING CONTRACT — read this before touching the delete or upsert SQL below.
	/// <para>
	/// A row in this table represents <b>the occupancy of one (character_id, slot) pair</b>, and
	/// <c>version</c> is the optimistic-concurrency counter of the <b>item currently occupying it</b>.
	/// The upsert and the delete have to agree on that, or the table eats items.
	/// </para>
	/// <para>
	/// The upsert is gated <c>EXCLUDED.version &gt; version</c> so a late write cannot overwrite a newer
	/// state of the same slot. The slot delete used to SOFT-delete the row and stamp the caller's
	/// version into it — and because a vacated slot has no item whose version could be quoted, every
	/// ordinary move passed <c>long.MaxValue</c> ("to ensure the delete succeeds"). The row therefore
	/// survived at version 9223372036854775807, nothing could ever exceed it, and that slot became
	/// permanently unwritable: the next item placed there was rejected with a StaleStateException and
	/// vanished on the next login. Moving any item out of any slot once was enough, in ordinary play,
	/// with no exploit. That reassuring comment is exactly how the bug was introduced.
	/// </para>
	/// <para>
	/// Resolution, in two halves that between them make the failure unreachable:
	/// </para>
	/// <para>
	/// 1. <b>Vacating a slot HARD-deletes the row.</b> An empty slot is the absence of a row, not a row
	/// wearing a flag. There is then no surviving version for the next occupant to have to beat, which
	/// is the honest answer: the departed item's version and the arriving item's version are two
	/// unrelated counters, and no ordering between them means anything. The delete keeps a
	/// <c>version &lt;= incoming</c> guard so a genuinely stale delete cannot remove a row that has
	/// already moved on; <c>long.MaxValue</c> still reads as "unconditional", but it can no longer
	/// leave a poisoned corpse behind, because it leaves nothing behind at all.
	/// </para>
	/// <para>
	/// 2. <b>The upsert may reclaim a soft-deleted row unconditionally</b>
	/// (<c>WHERE deleted = TRUE OR EXCLUDED.version &gt; version</c>). A deleted row holds no item, so
	/// there is no concurrent state to protect and the version comparison is meaningless. This half is
	/// what repairs rows that are ALREADY poisoned in a live database: they are tombstones, so the
	/// first write to that slot now simply takes it over. No migration and no manual SQL is required.
	/// Live (non-deleted) rows remain fully version-gated exactly as before.
	/// </para>
	/// <para>
	/// KNOWN GAP, deliberately not closed here: <c>version</c> travels with the <i>item</i>, not with the
	/// slot, so a per-item sequence is being used as a per-slot sequence. With the tombstone path gone
	/// the remaining exposure is narrow — per-character writes are serialised FIFO by the async
	/// worker's entity key — but two scene servers writing the same character concurrently are still
	/// unordered. Making <c>version</c> a slot-owned sequence is the real fix, and it is a schema
	/// change rather than a targeted one.
	/// </para>
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
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterInventoryEntity>> getInventoryItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterInventoryItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterInventoryService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterInventoryService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterInventoryData item, CancellationToken cancellationToken = default)
		{
			if (item.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (item.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, item.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", item.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
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
							{TableName}.deleted = TRUE
							OR EXCLUDED.version > {TableName}.version
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM upserted LIMIT 1), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
						dbContext,
						sql,
						new object[] { item.CharacterID, item.Slot, item.Version, item.TemplateID, item.Seed, item.Amount, now },
						cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Inventory item was rejected due to a stale Version.");
				}
				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Empty or null items collection");
			}

			if (itemList.Any(i => i.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more inventory items had an invalid Version. Version must be greater than 0.");
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
				// Amount is a uint in the DTO and a bigint in the table. Both halves of that mismatch
				// mattered: Npgsql 5 cannot bind a CLR uint[] at all ("The CLR array type
				// System.UInt32[] isn't supported"), so every batched item write threw
				// NotSupportedException at runtime and was reported as a plain DATABASE_ERROR; and
				// the UNNEST cast was ::integer[], which would have silently overflowed any stack
				// above int.MaxValue had the bind ever succeeded. Projecting to long[] and casting
				// to bigint[] fixes both. (Caught against PostgreSQL 18.6 — a batched persist could
				// not complete at all before this.)
				var amountArray = activeItems.Select(i => (long)i.Amount).ToArray();

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

		/// <summary>
		/// Builds the UNNEST + UPSERT statement for a batch of rows.
		/// </summary>
		/// <param name="versionGated">
		/// <c>true</c> for incremental writes, which must lose to a newer state of the same slot.
		/// <c>false</c> for <see cref="SaveSnapshotAsync"/>, which is authoritative and must land.
		/// </param>
		private string GetUpsertSql(bool versionGated = true)
		{
			// Reclaiming a soft-deleted row is unconditional in both modes: a deleted row holds no
			// item, so there is no concurrent state for the version comparison to protect.
			string gate = versionGated
				? $"\n\t\t\t\tWHERE\n\t\t\t\t\t{TableName}.deleted = TRUE\n\t\t\t\t\tOR EXCLUDED.version > {TableName}.version"
				: string.Empty;

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
					{{5}}::bigint[]
				) AS u(character_id, slot, version, template_id, seed, amount)
				ON CONFLICT (character_id, slot)
				DO UPDATE SET
					template_id = EXCLUDED.template_id,
					seed = EXCLUDED.seed,
					amount = EXCLUDED.amount,
					deleted = FALSE,
					time_deleted = NULL,
					version = EXCLUDED.version{gate};";
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// See the SLOT VERSIONING CONTRACT on this class: vacating a slot removes its row
				// outright rather than leaving a tombstone stamped with the caller's version.
				// Callers legitimately pass long.MaxValue here (character deletion), and that must
				// not be able to render every slot unwritable for the rest of the row's life.
				var sql = $@"DELETE FROM {TableName}
					WHERE character_id = {{1}} AND version <= {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { incomingVersion, characterId }, cancellationToken)
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
		public async Task<DatabaseResult> DeleteAsync(long characterId, int slot, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// See the SLOT VERSIONING CONTRACT on this class. The row IS the slot occupancy:
				// when the item leaves, the row goes with it. The old statement soft-deleted the row
				// and stamped the incoming version into it, so a caller passing long.MaxValue — which
				// every ordinary move did — left an unwritable slot behind for the character's life.
				var sql = $@"DELETE FROM {TableName}
					WHERE character_id = {{1}} AND slot = {{2}} AND version <= {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { incomingVersion, characterId, slot }, cancellationToken)
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
		public async Task<DatabaseResult> SaveSnapshotAsync(long characterId, IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			// A null collection means "this character holds nothing", which is a legitimate snapshot
			// and must still prune. It is NOT the same as the batch PersistAsync above, which treats
			// an empty collection as a caller error because it has nothing to say.
			var snapshot = items?.ToList() ?? new List<CharacterInventoryData>();

			if (snapshot.Any(i => i.CharacterID != characterId))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Snapshot contained rows belonging to a different character.");
			}

			if (snapshot.Any(i => i.Version <= 0))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more inventory items had an invalid Version. Version must be greater than 0.");
			}

			// Two rows claiming one slot would make the upsert "affect row a second time"; the last
			// writer wins, matching the in-memory container which can only hold one item per slot.
			if (snapshot.Count > 1)
			{
				var deduped = new Dictionary<int, CharacterInventoryData>();
				foreach (var item in snapshot)
				{
					deduped[item.Slot] = item;
				}

				if (deduped.Count != snapshot.Count)
				{
					snapshot = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				var slotArray = snapshot.Select(i => i.Slot).ToArray();

				// Prune first: any slot the character no longer occupies loses its row. Without this
				// the snapshot could only ever add, and a delete that failed to persist would leave a
				// phantom item that reappears on every login. `slot <> ALL('{}')` is TRUE, so an
				// empty snapshot correctly empties the container.
				var pruneSql = $@"DELETE FROM {TableName}
					WHERE character_id = {{0}} AND slot <> ALL({{1}}::integer[])";

				await dbContext.Database
					.ExecuteSqlRawAsync(pruneSql, new object[] { characterId, slotArray }, cancellationToken)
					.ConfigureAwait(false);

				if (snapshot.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = snapshot.Select(i => i.CharacterID).ToArray();
				var versionArray = snapshot.Select(i => i.Version).ToArray();
				var templateIdArray = snapshot.Select(i => i.TemplateID).ToArray();
				var seedArray = snapshot.Select(i => i.Seed).ToArray();
				// Amount is a uint in the DTO and a bigint in the table. Both halves of that mismatch
				// mattered: Npgsql 5 cannot bind a CLR uint[] at all ("The CLR array type
				// System.UInt32[] isn't supported"), so every batched item write threw
				// NotSupportedException at runtime and was reported as a plain DATABASE_ERROR; and
				// the UNNEST cast was ::integer[], which would have silently overflowed any stack
				// above int.MaxValue had the bind ever succeeded. Projecting to long[] and casting
				// to bigint[] fixes both. (Caught against PostgreSQL 18.6 — a batched persist could
				// not complete at all before this.)
				var amountArray = snapshot.Select(i => (long)i.Amount).ToArray();

				// Ungated on purpose — see the interface remarks. Every row must land, so there is no
				// expected-row-count assertion to make either: the upsert affects all of them.
				await dbContext.Database
					.ExecuteSqlRawAsync(
						GetUpsertSql(versionGated: false),
						new object[] { characterIdArray, slotArray, versionArray, templateIdArray, seedArray, amountArray, now },
						cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterInventoryData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getInventoryItemsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
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