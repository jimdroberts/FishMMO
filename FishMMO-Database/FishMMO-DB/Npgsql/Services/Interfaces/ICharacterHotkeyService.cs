using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character hotkeys in the database.
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
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Hotkeys use (character_id, slot) as unique constraint for slot-based storage.
	/// UPSERT operations handle hotkey changes at specific slots automatically.
	/// 
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </remarks>
	public interface ICharacterHotkeyService
	{
		/// <summary>
		/// Saves or updates a single hotkey in the database.
		/// Uses atomic UPSERT operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="hotkey">The hotkey data to save. CharacterID must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the ID of the saved hotkey on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) with RETURNING clause:
		/// - If hotkey exists at slot (character_id, slot): Updates type, reference_id
		/// - If hotkey doesn't exist at slot: Inserts new record
		/// - Returns the saved hotkey data including ID
		/// 
		/// Possible return scenarios:
		/// - Success with ID: Hotkey saved successfully
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseConstraintException: Constraint violation (unique/foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: UPSERT operation failed
		/// 
		/// Performance note: Uses FromSqlRaw with RETURNING for single round-trip efficiency.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
		/// </remarks>
		Task<DatabaseResult<long>> SaveHotkeyAsync(CharacterHotkeyData hotkey, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates multiple hotkeys in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="hotkeys">Collection of hotkey data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All hotkeys are processed in a single transaction
		/// - If any hotkey fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each hotkey:
		/// - If hotkey exists at slot (character_id, slot): Updates type, reference_id
		/// - If hotkey doesn't exist at slot: Inserts new record
		/// 
		/// Possible return scenarios:
		/// - Success: All hotkeys saved successfully
		/// - Failure with VALIDATION_ERROR: Empty or null hotkeys collection
		/// - Failure with DatabaseConstraintException: Constraint violation (unique/foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Transaction rolled back or query error
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
		/// </remarks>
		Task<DatabaseResult> SaveHotkeysAsync(IEnumerable<CharacterHotkeyData> hotkeys, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all hotkeys for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all hotkeys for the specified character in a single atomic operation
		/// - If character has no hotkeys, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Possible return scenarios:
		/// - Success: All hotkeys deleted successfully (or character has no hotkeys)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// 
		/// Use case: Character deletion cleanup or hotkey bar reset.
		/// </remarks>
		Task<DatabaseResult> DeleteHotkeysAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all hotkeys for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character hotkey data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all hotkeys for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Character has hotkeys
		/// - Success with empty collection: Character has no hotkeys
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterHotkeyData>>> GetHotkeysAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the count of hotkeys for a character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the count of hotkeys on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns count of hotkeys for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Uses CountAsync for efficient database-side counting
		/// 
		/// Return scenarios:
		/// - Success with count > 0: Character has hotkeys
		/// - Success with count = 0: Character has no hotkeys
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// Use case: Hotkey slot limit validation or UI display.
		/// </remarks>
		Task<DatabaseResult<int>> GetHotkeyCountAsync(long characterId, CancellationToken cancellationToken = default);
	}
}