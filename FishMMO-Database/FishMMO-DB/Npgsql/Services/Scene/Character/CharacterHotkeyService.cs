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
	/// Service for managing character hotkeys in the database.
	/// Provides async operations for CRUD operations on character hotkey bar data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character hotkey bars including:
	/// - Single hotkey save/update with atomic UPSERT operations
	/// - Batch hotkey save/update with transactions
	/// - Hotkey deletion (bulk operations)
	/// - Hotkey retrieval and count queries
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
	public sealed class CharacterHotkeyService : BaseService<CharacterHotkeyEntity>, ICharacterHotkeyService
	{
		/// <summary>
		/// Compiled query for retrieving character hotkeys.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterHotkeyEntity>>> GetHotkeysQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Compiled query for counting character hotkeys.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> GetHotkeyCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId)
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
		public async Task<DatabaseResult<long>> SaveHotkeyAsync(CharacterHotkeyData hotkey, CancellationToken cancellationToken = default)
		{
			if (hotkey.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync<long>(
				async (dbContext, ct) =>
				{
					var charactersTableName = dbContext.GetTableName<CharacterEntity>();
					// Use PostgreSQL UPSERT for atomic insert-or-update
					var result = await dbContext.CharacterHotkeys
							.FromSqlRaw($@"
							WITH active_character AS (
								SELECT id
								FROM {charactersTableName}
								WHERE id = {{0}} AND deleted = FALSE
								FOR KEY SHARE
							)
							INSERT INTO {TableName} 
								(character_id, type, slot, reference_id, time_created)
							SELECT
								{{0}}, {{1}}, {{2}}, {{3}}, CURRENT_TIMESTAMP
							FROM active_character
							ON CONFLICT (character_id, slot) 
							DO UPDATE SET 
								type = EXCLUDED.type,
								reference_id = EXCLUDED.reference_id
							RETURNING id, character_id, type, slot, reference_id, time_created",
							hotkey.CharacterID,
							hotkey.Type,
							hotkey.Slot,
							hotkey.ReferenceID)
							.AsNoTracking()
							.FirstOrDefaultAsync(ct).ConfigureAwait(false);

					if (result == null)
					{
						throw new DatabaseEntityNotFoundException("Character", hotkey.CharacterID.ToString());
					}

					return result.ID;
				},
				"SaveHotkey",
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveHotkeysAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default)
		{
			var hotkeyList = hotkeys?.ToList();
			if (hotkeyList == null || hotkeyList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null hotkeys collection");
			}

			// Extract arrays for bulk UPSERT
			var characterIds = hotkeyList.Select(h => h.CharacterID).ToArray();
			var types = hotkeyList.Select(h => (short)h.Type).ToArray();
			var slots = hotkeyList.Select(h => h.Slot).ToArray();
			var referenceIds = hotkeyList.Select(h => h.ReferenceID).ToArray();

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
					INSERT INTO {TableName} (character_id, type, slot, reference_id, time_created)
					SELECT u.character_id, u.type, u.slot, u.reference_id, CURRENT_TIMESTAMP
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::smallint[],
						{{2}}::int[],
						{{3}}::bigint[]
					) AS u(character_id, type, slot, reference_id)
					JOIN active_characters ac ON ac.id = u.character_id
					ON CONFLICT (character_id, slot) DO UPDATE SET
						type = EXCLUDED.type,
						reference_id = EXCLUDED.reference_id",
					new object[] { characterIds, types, slots, referenceIds },
					ct);
			}, "SaveHotkeys", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteHotkeys",
				new object[] { characterId },
				entityName: "CharacterHotkey",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterHotkeyData>>> GetHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync(
				async (dbContext, ct) =>
				{
					var entities = await GetHotkeysQuery(dbContext, characterId, ct).ConfigureAwait(false);
					var hotkeys = entities.Select(h => new CharacterHotkeyData(
						id: h.ID,
						characterID: h.CharacterID,
						type: h.Type,
						slot: h.Slot,
						referenceID: h.ReferenceID
					)).ToList();

					return (IReadOnlyList<CharacterHotkeyData>)hotkeys;
				},
				"GetHotkeys",
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetHotkeyCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync(
				async (dbContext, ct) =>
				{
					return await GetHotkeyCountQuery(dbContext, characterId, ct).ConfigureAwait(false);
				},
				"GetHotkeyCount",
				cancellationToken).ConfigureAwait(false);
		}
	}
}