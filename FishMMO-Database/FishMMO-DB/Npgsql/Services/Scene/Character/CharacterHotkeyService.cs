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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterHotkeyEntity>>> getHotkeysQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId && !h.Deleted)
					.ToList());

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
					"VALIDATION_ERROR",
					"Invalid character ID.",
					isTransient: false);
			}

			if (hotkey.Version <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, hotkey.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					throw new DatabaseEntityNotFoundException("Character", hotkey.CharacterID.ToString());
				}

				var now = DateTime.UtcNow;
				var sql = $@"INSERT INTO {TableName}
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
					RETURNING id, version, character_id, type, slot, reference_id, time_created, deleted, time_deleted";

				var upserted = await dbContext.CharacterHotkeys
					.FromSqlRaw(
						sql,
						hotkey.CharacterID,
						hotkey.Slot,
						hotkey.Version,
						(short)hotkey.Type,
						hotkey.ReferenceID,
						now)
					.AsNoTracking()
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (upserted == null)
				{
					throw new StaleStateException("Hotkey was rejected due to a stale Version.");
				}

				return upserted.ID;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default)
		{
			var hotkeyList = hotkeys?.ToList();
			if (hotkeyList == null || hotkeyList.Count == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Empty or null hotkeys collection",
					isTransient: false);
			}

			if (hotkeyList.Any(h => h.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more hotkeys had an invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (hotkeyList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int Slot), CharacterHotkeyData>();
				foreach (var hotkey in hotkeyList)
				{
					deduped[(hotkey.CharacterID, hotkey.Slot)] = hotkey;
				}

				if (deduped.Count != hotkeyList.Count)
				{
					hotkeyList = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = hotkeyList.Select(h => h.CharacterID).Distinct().ToArray();
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

				var activeHotkeys = hotkeyList.Where(h => activeCharacterIdSet.Contains(h.CharacterID)).ToList();
				if (activeHotkeys.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeHotkeys.Select(h => h.CharacterID).ToArray();
				var slotArray = activeHotkeys.Select(h => h.Slot).ToArray();
				var versionArray = activeHotkeys.Select(h => h.Version).ToArray();
				var typeArray = activeHotkeys.Select(h => (short)h.Type).ToArray();
				var referenceIdArray = activeHotkeys.Select(h => h.ReferenceID).ToArray();

				var sql = GetUpsertSql();

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeHotkeys.Count,
					new object[] { characterIdArray, slotArray, versionArray, typeArray, referenceIdArray, now },
					"One or more hotkeys were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
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
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getHotkeysQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
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
					"VALIDATION_ERROR",
					"Invalid character ID",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
				await getHotkeyCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}