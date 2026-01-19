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

			return await ExecuteWithStrategyAsync<long>(
				async (dbContext, strategy) =>
				{
					var result = await strategy.ExecuteAsync(async () =>
					{
						// Use PostgreSQL UPSERT for atomic insert-or-update
						return await dbContext.CharacterHotkeys
								.FromSqlInterpolated($@"
								INSERT INTO {TableName} 
									(character_id, type, slot, reference_id)
								VALUES 
									({hotkey.CharacterID}, {hotkey.Type}, {hotkey.Slot}, {hotkey.ReferenceID})
								ON CONFLICT (character_id, slot) 
								DO UPDATE SET 
									type = EXCLUDED.type,
									reference_id = EXCLUDED.reference_id
								RETURNING id, character_id, type, slot, reference_id")
								.AsNoTracking()
								.FirstOrDefaultAsync(cancellationToken);
					});

					if (result == null || result.ID == 0)
					{
						throw new Exception("Failed to save hotkey");
					}

					return result.ID;
				},
				"SaveHotkey",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveHotkeysAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default)
		{
			var hotkeyList = hotkeys?.ToList();
			if (hotkeyList == null || hotkeyList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null hotkeys collection");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext, strategy) =>
				{
					await strategy.ExecuteAsync(async () =>
					{
						// Extract arrays for bulk UPSERT
						var characterIds = hotkeyList.Select(h => h.CharacterID).ToArray();
						var types = hotkeyList.Select(h => (short)h.Type).ToArray();
						var slots = hotkeyList.Select(h => h.Slot).ToArray();
						var referenceIds = hotkeyList.Select(h => h.ReferenceID).ToArray();

						// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
						await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"INSERT INTO {TableName} (character_id, type, slot, reference_id)
							SELECT * FROM UNNEST(
								{characterIds}::bigint[],
								{types}::smallint[],
								{slots}::int[],
								{referenceIds}::bigint[]
							)
							ON CONFLICT (character_id, slot) DO UPDATE SET
								type = EXCLUDED.type,
								reference_id = EXCLUDED.reference_id",
							cancellationToken);
					});
				},
				"SaveHotkeys",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext, strategy) =>
				{
					await strategy.ExecuteAsync(async () =>
					{
						// Use atomic DELETE for thread safety
						await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
							cancellationToken);
					});
				},
				"DeleteHotkeys",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterHotkeyData>>> GetHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					var entities = await GetHotkeysQuery(dbContext, characterId, cancellationToken);
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
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetHotkeyCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					return await GetHotkeyCountQuery(dbContext, characterId, cancellationToken);
				},
				"GetHotkeyCount",
				cancellationToken);
		}
	}
}