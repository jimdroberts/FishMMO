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
	/// Service for managing character hotkeys in the database.
	/// Provides async operations for CRUD operations on character hotkey bar data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character hotkey bars including:
	/// - Single hotkey save/update with atomic UPSERT operations
	/// - Batch hotkey save/update with explicit transactions
	/// - Hotkey deletion (bulk operations)
	/// - Hotkey retrieval and count queries
	/// 
	/// All exceptions are classified by <c>BaseService</c> and mapped to <see cref="DatabaseResult"/> error codes
	/// (e.g., UNIQUE_VIOLATION, FOREIGN_KEY_VIOLATION, STALE_STATE, DATABASE_ERROR). Transient failures are retried automatically.
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// Unique constraint violations are not used as normal control flow; write paths prefer deterministic SQL (e.g. UPSERT) where appropriate.
	/// </remarks>
	public sealed class CharacterHotkeyService : BaseService<CharacterHotkeyEntity>, ICharacterHotkeyService
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
		/// Compiled query for retrieving character hotkeys.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterHotkeyEntity>> getHotkeysQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId && !h.Deleted));

		/// <summary>
		/// Compiled query for counting character hotkeys.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getHotkeyCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId && !h.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterHotkeyService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterHotkeyService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> PersistAsync(CharacterHotkeyData hotkey, CancellationToken cancellationToken = default)
		{
			if (hotkey.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID.");
			}

			if (hotkey.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, hotkey.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", hotkey.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					WITH upserted AS (
						INSERT INTO {TableName}
							(character_id, slot, version, type, reference_id, time_created, deleted, time_deleted)
						VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, FALSE, NULL)
						ON CONFLICT (character_id, slot)
						DO UPDATE SET
							type = EXCLUDED.type,
							reference_id = EXCLUDED.reference_id,
							deleted = FALSE,
							time_deleted = NULL,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM upserted LIMIT 1), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
						dbContext,
						sql,
						new object[] { hotkey.CharacterID, hotkey.Slot, hotkey.Version, (int)hotkey.Type, hotkey.ReferenceID, now },
						cancellationToken).ConfigureAwait(false);

				if (id <= 0)
				{
					throw new StaleStateException("Hotkey was rejected due to a stale Version.");
				}
				return id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default)
			=> PersistBatchAsync(hotkeys, null, cancellationToken);

		/// <inheritdoc/>
		public Task<DatabaseResult<BulkWriteResult>> PersistOwnedAsync(IEnumerable<CharacterHotkeyData> hotkeys, IReadOnlyCollection<CharacterSessionLeaseData> claims, CancellationToken cancellationToken = default)
		{
			string? invalid = CharacterWriteGate.ValidateClaims(claims);
			return invalid != null
				? Task.FromResult(DatabaseResult<BulkWriteResult>.Failure(DatabaseErrorCodes.ValidationError, invalid))
				: PersistBatchAsync(hotkeys, claims, cancellationToken);
		}

		/// <summary>
		/// The batch write behind <see cref="PersistAsync(IEnumerable{CharacterHotkeyData}, CancellationToken)"/> and
		/// <see cref="PersistOwnedAsync"/>.
		/// </summary>
		/// <param name="hotkeys">Rows to write.</param>
		/// <param name="claims">The writer's claims, or null for the ungated write. See <see cref="CharacterWriteGate"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<BulkWriteResult>> PersistBatchAsync(IEnumerable<CharacterHotkeyData> hotkeys, IReadOnlyCollection<CharacterSessionLeaseData>? claims, CancellationToken cancellationToken)
		{
			var hotkeyList = hotkeys?.ToList();
			if (hotkeyList == null || hotkeyList.Count == 0)
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Empty or null hotkeys collection");
			}

			if (hotkeyList.Any(h => h.Version <= 0))
			{
				return DatabaseResult<BulkWriteResult>.Failure(
					DatabaseErrorCodes.ValidationError,
					"One or more hotkeys had an invalid Version. Version must be greater than 0.");
			}

			// Counted before collapsing, so a key the batch names twice shows as Filtered. See BulkBatch.
			int suppliedRows = hotkeyList.Count;
			// One row per key, the newest version: see BulkBatch.KeepNewest. An upsert cannot touch one
			// row twice, and an UPDATE ... FROM matching one row twice is ambiguous.
			hotkeyList = BulkBatch.KeepNewest(hotkeyList, hotkey => (hotkey.CharacterID, hotkey.Slot), hotkey => hotkey.Version);


			return await ExecuteTransactionAsync<BulkWriteResult>(async dbContext =>
			{
				var characterIds = hotkeyList.Select(h => h.CharacterID).Distinct().ToArray();
				/* The ownership gate, and the per-character existence check it subsumes: the rows of a
				 * missing or deleted character, or — for an owned write — of one whose claim the writer
				 * no longer holds, are left out and reported as Filtered rather than failing every other
				 * character's rows with them. See CharacterWriteGate. */
				CharacterWriteAdmission admission = await CharacterWriteGate.AdmitAsync(dbContext, characterIds, claims, cancellationToken).ConfigureAwait(false);
				int unownedRows = admission.CountUnowned(hotkeyList, row => row.CharacterID);

				var activeHotkeys = hotkeyList.Where(h => admission.Admits(h.CharacterID)).ToList();
				if (activeHotkeys.Count == 0)
				{
					return new BulkWriteResult(suppliedRows, 0, 0, unownedRows);
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeHotkeys.Select(h => h.CharacterID).ToArray();
				var slotArray = activeHotkeys.Select(h => h.Slot).ToArray();
				var versionArray = activeHotkeys.Select(h => h.Version).ToArray();
				var typeArray = activeHotkeys.Select(h => (short)h.Type).ToArray();
				var referenceIdArray = activeHotkeys.Select(h => h.ReferenceID).ToArray();

				var sql = GetUpsertSql();

				/* Stale rows are skipped and counted as superseded, as every sibling batch does, rather
				 * than failing the batch. A batch can carry many characters' bars — the shutdown flush
				 * writes every resident's in one statement — and one character whose stored bar is
				 * newer (another server's clock ahead of this one's; hotkey versions are ticks) used to
				 * take every other character's bar down with it. A caller that needs the whole bar still
				 * sees a short write: Superseded makes it incomplete. */
				int appliedRows = await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeHotkeys.Count,
					new object[] { characterIdArray, slotArray, versionArray, typeArray, referenceIdArray, now },
					"One or more hotkeys were rejected due to a stale Version.",
					cancellationToken,
					BulkVersionConflictPolicy.SkipStaleRows).ConfigureAwait(false);

				return new BulkWriteResult(suppliedRows, activeHotkeys.Count, appliedRows, unownedRows);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private string GetUpsertSql()
		{
			return $@"
				INSERT INTO {TableName}
					(character_id, slot, version, type, reference_id, time_created, deleted, time_deleted)
				SELECT
					u.character_id,
					u.slot,
					u.version,
					u.type,
					u.reference_id,
					{{5}},
					FALSE,
					NULL
				FROM UNNEST(
					{{0}}::bigint[],
					{{1}}::integer[],
					{{2}}::bigint[],
					{{3}}::smallint[],
					{{4}}::bigint[]
				) AS u(character_id, slot, version, type, reference_id)
				ON CONFLICT (character_id, slot)
				DO UPDATE SET
					type = EXCLUDED.type,
					reference_id = EXCLUDED.reference_id,
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
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterHotkeys
						.AsNoTracking()
						.AnyAsync(h => h.CharacterID == characterId && !h.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Hotkey delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterHotkeyData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getHotkeysQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var hotkeys = entities.Select(h => new CharacterHotkeyData(
					id: h.ID,
					version: h.Version,
					characterID: h.CharacterID,
					type: h.Type,
					slot: h.Slot,
					referenceID: h.ReferenceID
				)).ToList();

				return (IReadOnlyList<CharacterHotkeyData>)hotkeys;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID");
			}

			return await ExecuteReadAsync(async dbContext =>
				await getHotkeyCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}