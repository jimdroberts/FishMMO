using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character inventory items in the database.
	/// Provides async operations for CRUD operations on character inventory data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character inventory including:
	/// - Single inventory item save/update with atomic UPSERT operations
	/// - Batch inventory save/update with transactions
	/// - Inventory deletion (bulk and slot-specific)
	/// - Inventory retrieval
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Inventory uses (character_id, slot) as unique constraint for slot-based storage.
	/// UPSERT operations handle item changes at specific slots automatically.
	/// 
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </remarks>
	public interface ICharacterInventoryService
	{
		/// <summary>
		/// Saves or updates a single inventory item in the database.
		/// Uses atomic UPSERT operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="item">The inventory item data to save. CharacterID must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the ID of the saved item on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) with RETURNING clause:
		/// - If item exists at slot (character_id, slot): Updates template_id, seed, amount
		/// - If item doesn't exist at slot: Inserts new record
		/// - Returns the saved item data including ID
		/// 
		/// Possible return scenarios:
		/// - Success with ID: Item saved successfully
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseConstraintException: Constraint violation (unique/foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: UPSERT operation failed
		/// 
		/// Performance note: Uses FromSqlRaw with RETURNING for single round-trip efficiency.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
		/// </remarks>
		Task<DatabaseResult<long>> SaveInventoryItemAsync(CharacterInventoryData item, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates multiple inventory items in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="items">Collection of inventory item data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All items are processed in a single transaction
		/// - If any item fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each item:
		/// - If item exists at slot (character_id, slot): Updates template_id, seed, amount
		/// - If item doesn't exist at slot: Inserts new record
		/// 
		/// Possible return scenarios:
		/// - Success: All items saved successfully
		/// - Failure with VALIDATION_ERROR: Empty or null items collection
		/// - Failure with DatabaseConstraintException: Constraint violation (unique/foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Transaction rolled back or query error
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
		/// </remarks>
		Task<DatabaseResult> SaveInventoryItemsAsync(IEnumerable<CharacterInventoryData> items, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all inventory items for a specific character.
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
		/// - Deletes all inventory items for the specified character
		/// - If character has no items, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Possible return scenarios:
		/// - Success: All items deleted successfully (or character has no items)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// </remarks>
		Task<DatabaseResult> DeleteInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific inventory item by slot.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="slot">The inventory slot to clear.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes item at the specified slot for the character
		/// - If slot is empty, operation succeeds
		/// - Uses raw SQL with specific WHERE clause for atomic deletion
		/// 
		/// Possible return scenarios:
		/// - Success: Inventory slot cleared successfully (or slot was already empty)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// 
		/// Use case: Dropping a single item or clearing specific inventory slot.
		/// </remarks>
		Task<DatabaseResult> DeleteInventorySlotAsync(long characterId, int slot, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all inventory items for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character inventory data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all inventory items for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Character has inventory items
		/// - Success with empty collection: Character has no items
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterInventoryData>>> GetInventoryItemsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}