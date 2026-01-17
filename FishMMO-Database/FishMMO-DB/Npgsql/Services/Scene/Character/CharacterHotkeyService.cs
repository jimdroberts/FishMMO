using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
	public sealed class CharacterHotkeyService : ICharacterHotkeyService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterHotkeyService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterHotkeyService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveHotkeyAsync(CharacterHotkeyData hotkey, CancellationToken cancellationToken = default)
		{
			if (hotkey.CharacterID == 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var result = await strategy.ExecuteAsync(async () =>
				{
					// Use PostgreSQL UPSERT for atomic insert-or-update
					var tableName = dbContext.GetTableName<CharacterHotkeyEntity>();
					return await dbContext.CharacterHotkeys
							.FromSqlInterpolated($@"
							INSERT INTO {tableName} 
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
					return DatabaseResult<long>.Failure("SAVE_FAILED", "Failed to save hotkey");
				}

				return DatabaseResult<long>.Success(result.ID);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseTimeoutException("SaveHotkey", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_hotkeys_character_id_slot_key",
						"A hotkey already exists in this slot.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_hotkeys_character_id_fkey",
						"Character does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveHotkey",
						"Failed to save hotkey due to a database error.",
						$"DbUpdateException in SaveHotkeyAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveHotkey",
						"An unexpected error occurred while saving the hotkey.",
						$"Unexpected error in SaveHotkeyAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveHotkeysAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default)
		{
			var hotkeyList = hotkeys?.ToList();
			if (hotkeyList == null || hotkeyList.Count == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Empty or null hotkeys collection");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterHotkeyEntity>();

					// Extract arrays for bulk UPSERT
					var characterIds = hotkeyList.Select(h => h.CharacterID).ToArray();
					var types = hotkeyList.Select(h => (short)h.Type).ToArray();
					var slots = hotkeyList.Select(h => h.Slot).ToArray();
					var referenceIds = hotkeyList.Select(h => h.ReferenceID).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, type, slot, reference_id)
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

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveHotkeys", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_hotkeys_character_id_slot_key",
						"A hotkey already exists in one of the slots.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_hotkeys_character_id_fkey",
						"One or more characters do not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveHotkeys",
						"Failed to save hotkeys due to a database error.",
						$"DbUpdateException in SaveHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveHotkeys",
						"An unexpected error occurred while saving hotkeys.",
						$"Unexpected error in SaveHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterHotkeyEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteHotkeys", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_hotkeys_constraint",
						"Constraint violation while deleting hotkeys.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_hotkeys_constraint",
						"Cannot delete hotkeys due to foreign key constraint.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteHotkeys",
						"Failed to delete hotkeys due to a database error.",
						$"DbUpdateException in DeleteHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteHotkeys",
						"An unexpected error occurred while deleting hotkeys.",
						$"Unexpected error in DeleteHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterHotkeyData>>> GetHotkeysAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var hotkeys = await dbContext.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId)
					.Select(h => new CharacterHotkeyData
					{
						ID = h.ID,
						CharacterID = h.CharacterID,
						Type = h.Type,
						Slot = h.Slot,
						ReferenceID = h.ReferenceID
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.Success(hotkeys);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseTimeoutException("GetHotkeys", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_hotkeys_constraint",
						"Constraint violation while retrieving hotkeys.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_hotkeys_constraint",
						"Foreign key constraint issue while retrieving hotkeys.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseQueryException(
						"GetHotkeys",
						"Failed to retrieve hotkeys due to a database error.",
						$"DbUpdateException in GetHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterHotkeyData>>.FromException(
					new DatabaseQueryException(
						"GetHotkeys",
						"An unexpected error occurred while retrieving hotkeys.",
						$"Unexpected error in GetHotkeysAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetHotkeyCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var count = await dbContext.CharacterHotkeys
					.AsNoTracking()
					.Where(h => h.CharacterID == characterId)
					.CountAsync(cancellationToken);

				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetHotkeyCount", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_hotkeys_constraint",
						"Constraint violation while counting hotkeys.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_hotkeys_constraint",
						"Foreign key constraint issue while counting hotkeys.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetHotkeyCount",
						"Failed to count hotkeys due to a database error.",
						$"DbUpdateException in GetHotkeyCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetHotkeyCount",
						"An unexpected error occurred while counting hotkeys.",
						$"Unexpected error in GetHotkeyCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}