using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for a character's items across every container.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>IDENTITY CONTRACT — read this before touching any SQL below.</b> A row IS an item. The
	/// conflict target of every upsert is the primary key, and <c>container</c> and <c>slot</c> are
	/// ordinary columns that an update is free to change. Moving an item therefore updates the row
	/// it already had, and its <c>id</c> is stable for the item's whole life.
	/// </para>
	/// <para>
	/// The three tables this replaces keyed their rows <c>(character_id, slot)</c>, which made the
	/// row an occupancy record rather than an item. Three consequences followed, and all three are
	/// gone here: an item that moved slots became a different row with a different id; two items
	/// that passed through one slot shared an id; and because <c>character_inventory</c>,
	/// <c>character_equipment</c> and <c>character_bank</c> each had their own identity sequence,
	/// three unrelated items were routinely handed the same number. That last one is why the
	/// runtime <c>Item</c> had to carry a second, process-local identity to key its attribute
	/// contributions by.
	/// </para>
	/// <para>
	/// <b>An id of zero means "never written".</b> Both write paths draw the next identity from the
	/// table's own sequence for such a row and hand it back, and the caller writes it onto the
	/// runtime item. The write-back is not cosmetic: the item's attribute-ledger key is its id, so
	/// an item that never learns its id gets a new row — and a new ledger key — on every save.
	/// </para>
	/// <para>
	/// <b>The <c>(character_id, container, slot)</c> unique index is not a conflict target.</b> It
	/// exists to stop two items claiming one slot, a state the in-memory container cannot represent.
	/// It is checked per row, so a statement that moves several items at once can trip it halfway
	/// through even when the end state is legal. <see cref="SaveSnapshotAsync"/> avoids that by
	/// deleting the character's rows before re-inserting them; the incremental paths move one item
	/// at a time. A stale row still sitting on a slot that an incoming item claims surfaces as a
	/// UNIQUE_VIOLATION and is repaired by the next snapshot.
	/// </para>
	/// </remarks>
	public sealed class CharacterItemService : BaseService<CharacterItemEntity>, ICharacterItemService
	{
		/// <summary>
		/// Compiled query for checking whether a character exists and is not deleted.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<long>> getActiveCharacterIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId && !c.Deleted)
					.Select(c => c.ID)
					.FirstOrDefault());

		/// <summary>
		/// Compiled query for retrieving every item a character owns (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterItemEntity>> getItemsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterItems
					.AsNoTracking()
					.Where(i => i.CharacterID == characterId && !i.Deleted));

		public CharacterItemService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <summary>
		/// The expression that yields a row's identity: the caller's, or the next one from the
		/// table's own sequence when the caller has none.
		/// </summary>
		/// <remarks>
		/// <c>COALESCE</c> short-circuits, so <c>nextval</c> is only reached for a row that arrived
		/// with a zero id — and burning a sequence value would be harmless even if it were not.
		/// <c>pg_get_serial_sequence</c> resolves an identity column's sequence as well as a
		/// <c>serial</c>'s, so this does not depend on which of the two the migration produced.
		/// </remarks>
		private string IdentityExpression(string idExpression) =>
			$"COALESCE(NULLIF({idExpression}, 0), nextval(pg_get_serial_sequence('{TableName}', 'id')))";

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterItemData item, CancellationToken cancellationToken = default)
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

			return await ExecuteTransactionAsync(async dbContext =>
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
							(id, character_id, container, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
						VALUES ({IdentityExpression("{0}")}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, FALSE, NULL)
						ON CONFLICT (id)
						DO UPDATE SET
							character_id = EXCLUDED.character_id,
							container = EXCLUDED.container,
							slot = EXCLUDED.slot,
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

				// Amount is projected to long because Npgsql cannot bind System.UInt32 at all
				// ("The CLR type System.UInt32 isn't natively supported"); the column is bigint, so
				// long binds exactly. Container is projected to short because a byte-backed enum
				// maps to smallint.
				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[]
					{
						item.ID,
						item.CharacterID,
						(short)item.Container,
						item.Slot,
						item.Version,
						item.TemplateID,
						item.Seed,
						(long)item.Amount,
						now,
					},
					cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Item was rejected due to a stale Version.");
				}
				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterItemData> items, CancellationToken cancellationToken = default)
		{
			var itemList = items?.ToList();
			if (itemList == null || itemList.Count == 0)
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Empty or null items collection");
			}

			if (itemList.Any(i => i.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more items had an invalid Version. Version must be greater than 0.");
			}

			/* A batch that names one identity twice would make the upsert "affect row a second
			 * time". Rows with no identity yet cannot collide with anything, so they are all kept;
			 * the last writer wins for the rest, matching the in-memory container. */
			if (itemList.Count > 1)
			{
				var identified = new Dictionary<long, CharacterItemData>();
				var unidentified = new List<CharacterItemData>();
				foreach (var item in itemList)
				{
					if (item.ID > 0)
					{
						identified[item.ID] = item;
					}
					else
					{
						unidentified.Add(item);
					}
				}

				if (identified.Count + unidentified.Count != itemList.Count)
				{
					itemList = identified.Values.Concat(unidentified).ToList();
				}
			}

			int suppliedRows = itemList.Count;

			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
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
					return new BulkWriteResult(suppliedRows, 0, 0);
				}

				var now = DateTime.UtcNow;

				int appliedRows = await ExecuteBulkUpsertAsync(
					dbContext,
					GetUpsertSql(),
					activeItems.Count,
					BuildArrayParameters(activeItems, now),
					"One or more items were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);

				return new BulkWriteResult(suppliedRows, activeItems.Count, appliedRows);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Positional parameters for the UNNEST-based statements, in the order the SQL reads them.
		/// </summary>
		private static object[] BuildArrayParameters(IReadOnlyList<CharacterItemData> rows, DateTime now)
		{
			return new object[]
			{
				rows.Select(i => i.ID).ToArray(),
				rows.Select(i => i.CharacterID).ToArray(),
				rows.Select(i => (short)i.Container).ToArray(),
				rows.Select(i => i.Slot).ToArray(),
				rows.Select(i => i.Version).ToArray(),
				rows.Select(i => i.TemplateID).ToArray(),
				rows.Select(i => i.Seed).ToArray(),
				// Npgsql 5 cannot bind a CLR uint[] ("The CLR array type System.UInt32[] isn't
				// supported"), and the column is bigint, so the projection is required rather than
				// tidy. Casting the UNNEST to ::integer[] instead would silently overflow any stack
				// above int.MaxValue.
				rows.Select(i => (long)i.Amount).ToArray(),
				now,
			};
		}

		/// <summary>
		/// Array parameters for the replace path, carrying each row's own creation time.
		/// </summary>
		/// <remarks>
		/// A row that already exists keeps the time it was created; one arriving without an
		/// identity, or whose id no longer resolves, is new and takes the save time. Passing a
		/// single timestamp for the whole batch is what made every item look created at the moment
		/// of the last save.
		/// </remarks>
		private static object[] BuildReplaceArrayParameters(
			IReadOnlyList<CharacterItemData> rows,
			IReadOnlyDictionary<long, DateTime> existingCreationTimes,
			DateTime now)
		{
			var parameters = BuildArrayParameters(rows, now);

			parameters[8] = rows
				.Select(i => i.ID > 0 && existingCreationTimes.TryGetValue(i.ID, out DateTime created)
					? created
					: now)
				.ToArray();

			return parameters;
		}

		/// <summary>
		/// Builds the UNNEST + UPSERT statement for a batch of rows.
		/// </summary>
		/// <remarks>
		/// The conflict target is the primary key — the item — not the slot. See the identity
		/// contract on this class.
		/// </remarks>
		private string GetUpsertSql()
		{
			return $@"
				INSERT INTO {TableName}
					(id, character_id, container, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
				SELECT
					{IdentityExpression("u.id")},
					u.character_id,
					u.container,
					u.slot,
					u.version,
					u.template_id,
					u.seed,
					u.amount,
					{{8}},
					FALSE,
					NULL
				FROM UNNEST(
					{{0}}::bigint[],
					{{1}}::bigint[],
					{{2}}::smallint[],
					{{3}}::integer[],
					{{4}}::bigint[],
					{{5}}::integer[],
					{{6}}::integer[],
					{{7}}::bigint[]
				) AS u(id, character_id, container, slot, version, template_id, seed, amount)
				ON CONFLICT (id)
				DO UPDATE SET
					character_id = EXCLUDED.character_id,
					container = EXCLUDED.container,
					slot = EXCLUDED.slot,
					template_id = EXCLUDED.template_id,
					seed = EXCLUDED.seed,
					amount = EXCLUDED.amount,
					deleted = FALSE,
					time_deleted = NULL,
					version = EXCLUDED.version
				WHERE
					{TableName}.deleted = TRUE
					OR EXCLUDED.version > {TableName}.version;";
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
				// A vacated row is removed outright rather than left as a tombstone. Under the old
				// per-slot schema a soft delete stamped the caller's version into the surviving row,
				// and callers legitimately passed long.MaxValue — which left a row nothing could
				// ever outrank and a slot that could never be written again.
				var sql = $@"DELETE FROM {TableName}
					WHERE character_id = {{1}} AND version <= {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActiveItems = await dbContext.CharacterItems
						.AsNoTracking()
						.AnyAsync(i => i.CharacterID == characterId && !i.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActiveItems)
					{
						throw new StaleStateException("Item delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteItemAsync(long characterId, long itemId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (itemId <= 0)
			{
				// An item that was never written has nothing to delete, and asking to remove id 0
				// would match every unassigned row if the guard below were ever relaxed. Report it
				// rather than issuing the statement.
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid item ID. The item has no database identity to delete.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// The character is part of the predicate so one character's delete can never reach
				// another's row, even if a caller passes an id it does not own.
				var sql = $@"DELETE FROM {TableName}
					WHERE id = {{1}} AND character_id = {{2}} AND version <= {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { incomingVersion, itemId, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var row = await dbContext.CharacterItems
						.AsNoTracking()
						.FirstOrDefaultAsync(i => i.ID == itemId && i.CharacterID == characterId, cancellationToken)
						.ConfigureAwait(false);

					if (row != null && !row.Deleted)
					{
						throw new StaleStateException("Item delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>> SaveSnapshotAsync(
			long characterId,
			IReadOnlyCollection<ItemContainerType> containers,
			IEnumerable<CharacterItemData> items,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			if (containers == null || containers.Count == 0)
			{
				// A snapshot that speaks for no container would delete nothing and write nothing,
				// which is not a statement — it is a caller that failed to say what it read.
				return DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"A snapshot must name at least one container it speaks for.");
			}

			var scope = new HashSet<ItemContainerType>(containers);

			// A null collection means "this character holds nothing", which is a legitimate snapshot
			// and must still prune. That is NOT the same as the batch PersistAsync above, which
			// treats an empty collection as a caller error because it has nothing to say.
			var snapshot = items?.ToList() ?? new List<CharacterItemData>();

			if (snapshot.Any(i => i.CharacterID != characterId))
			{
				return DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Snapshot contained rows belonging to a different character.");
			}

			if (snapshot.Any(i => !scope.Contains(i.Container)))
			{
				// Writing a row for a container the snapshot does not claim to have read would
				// leave that container half-stated: the row lands, but nothing prunes what it
				// replaced.
				return DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Snapshot contained rows for a container it does not speak for.");
			}

			if (snapshot.Any(i => i.Version <= 0))
			{
				return DatabaseResult<IReadOnlyList<CharacterItemIdAssignment>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more items had an invalid Version. Version must be greater than 0.");
			}

			// Two rows claiming one (container, slot) is a state the in-memory container cannot
			// represent, and the unique index would reject the second. Last writer wins.
			if (snapshot.Count > 1)
			{
				var deduped = new Dictionary<(ItemContainerType Container, int Slot), CharacterItemData>();
				foreach (var item in snapshot)
				{
					deduped[(item.Container, item.Slot)] = item;
				}

				if (deduped.Count != snapshot.Count)
				{
					snapshot = deduped.Values.ToList();
				}
			}

			// Which rows arrived without an identity, so the assignments can be filtered to exactly
			// those the caller still needs.
			var awaitingIdentity = new HashSet<(ItemContainerType Container, int Slot)>(
				snapshot.Where(i => i.ID <= 0).Select(i => (i.Container, i.Slot)));

			return await ExecuteTransactionAsync<IReadOnlyList<CharacterItemIdAssignment>>(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString());
				}

				/* Delete everything, then re-insert everything, rather than upsert-and-prune.
				 *
				 * The snapshot is authoritative for every container it speaks for, so there is no
				 * state to preserve — and clearing first is what makes the statement immune to the
				 * (character_id, container, slot) unique index. That index is checked per row, so
				 * two items exchanging slots, or an item moving from the inventory into an
				 * equipment socket another item just left, would trip it partway through an
				 * upsert even though the end state is perfectly legal.
				 *
				 * Identities survive because they are supplied explicitly below. An item keeps its
				 * id across this; only rows that never had one draw a new value. */
				/* Creation times are read before the delete, because the delete is what would
				 * otherwise lose them.
				 *
				 * This path replaces rows rather than updating them, so time_created was being
				 * stamped with the save time on every write -- every item in a character's
				 * inventory ended up sharing the timestamp of the last save, which is not when any
				 * of them was created. The single-item upsert above never had this problem: it
				 * leaves time_created out of its DO UPDATE, and so preserves it.
				 *
				 * Worth more than tidiness. time_created is the only record of when an item came
				 * into existence, which is exactly what an investigation into duplicated items has
				 * to work from -- and with every row rewritten each save, there was nothing left to
				 * investigate. */
				var existingCreationTimes = new Dictionary<long, DateTime>();
				var knownIds = snapshot.Where(i => i.ID > 0).Select(i => i.ID).ToArray();
				if (knownIds.Length > 0)
				{
					var existing = await ExecuteReturningManyAsync(
						dbContext,
						$"SELECT id, time_created FROM {TableName} WHERE id = ANY({{0}}::bigint[])",
						new object[] { knownIds },
						reader => new KeyValuePair<long, DateTime>(reader.GetInt64(0), reader.GetDateTime(1)),
						cancellationToken).ConfigureAwait(false);

					foreach (var pair in existing)
					{
						existingCreationTimes[pair.Key] = pair.Value;
					}
				}
				await dbContext.Database
					.ExecuteSqlRawAsync(
						$"DELETE FROM {TableName} WHERE character_id = {{0}} AND container = ANY({{1}}::smallint[])",
						new object[] { characterId, scope.Select(c => (short)c).ToArray() },
						cancellationToken)
					.ConfigureAwait(false);

				if (snapshot.Count == 0)
				{
					return (IReadOnlyList<CharacterItemIdAssignment>)Array.Empty<CharacterItemIdAssignment>();
				}

				var now = DateTime.UtcNow;

				/* Ungated on purpose — see the interface remarks. Every row must land, so there is
				 * no version comparison and no expected-row-count assertion to make. */
				var insertSql = $@"
					INSERT INTO {TableName}
						(id, character_id, container, slot, version, template_id, seed, amount, time_created, deleted, time_deleted)
					SELECT
						{IdentityExpression("u.id")},
						u.character_id,
						u.container,
						u.slot,
						u.version,
						u.template_id,
						u.seed,
						u.amount,
						{{8}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::bigint[],
						{{2}}::smallint[],
						{{3}}::integer[],
						{{4}}::bigint[],
						{{5}}::integer[],
						{{6}}::integer[],
						{{7}}::bigint[]
					) AS u(id, character_id, container, slot, version, template_id, seed, amount)
					RETURNING id, container, slot";

				var written = await ExecuteReturningManyAsync(
					dbContext,
					insertSql,
					BuildReplaceArrayParameters(snapshot, existingCreationTimes, now),
					reader => new CharacterItemIdAssignment(
						(ItemContainerType)reader.GetInt16(1),
						reader.GetInt32(2),
						reader.GetInt64(0)),
					cancellationToken).ConfigureAwait(false);

				var assignments = written
					.Where(a => awaitingIdentity.Contains((a.Container, a.Slot)))
					.ToList();

				return (IReadOnlyList<CharacterItemIdAssignment>)assignments;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterItemData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterItemData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getItemsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var items = entities.Select(i => new CharacterItemData(
					id: i.ID,
					version: i.Version,
					characterID: i.CharacterID,
					container: i.Container,
					templateID: i.TemplateID,
					slot: i.Slot,
					seed: i.Seed,
					amount: i.Amount
				)).ToList();

				return (IReadOnlyList<CharacterItemData>)items;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Executes a statement that returns many rows and maps every one of them.
		/// </summary>
		/// <remarks>
		/// <see cref="BaseService{TEntity}"/> provides single-row and scalar forms only. This one
		/// exists for the snapshot's multi-row <c>RETURNING</c>, and reuses the ambient EF Core
		/// connection and transaction the same way they do, so it stays inside the caller's
		/// transaction rather than opening one of its own.
		/// </remarks>
		private static async Task<List<TResult>> ExecuteReturningManyAsync<TResult>(
			NpgsqlDbContext dbContext,
			string sql,
			object[] parameters,
			Func<DbDataReader, TResult> map,
			CancellationToken cancellationToken)
		{
			var connection = dbContext.Database.GetDbConnection();
			if (connection.State != ConnectionState.Open)
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
			}

			using var command = connection.CreateCommand();
			command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
			command.CommandText = ParameterPlaceholderRegex.Replace(sql, "@p$1");

			for (int i = 0; i < parameters.Length; i++)
			{
				var param = command.CreateParameter();
				param.ParameterName = "@p" + i;
				param.Value = parameters[i] ?? DBNull.Value;
				command.Parameters.Add(param);
			}

			var results = new List<TResult>();
			using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				results.Add(map(reader));
			}
			return results;
		}
	}
}
