using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character bank items in the database.
	/// Provides async operations for CRUD operations on character bank data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character bank items including:
	/// - Individual bank item save/update with atomic UPSERT operations
	/// - Batch bank item operations with transaction safety
	/// - Bank item deletion (individual slot and bulk operations)
	/// - Bank item retrieval
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// All methods return DatabaseResult for safe, typed error handling with sanitized messages
	/// that are suitable for client communication while preserving detailed information for logging.
	/// </remarks>
	public interface ICharacterBankService
	{
		/// <summary>
		/// Saves or updates a single bank item in the database.
		/// Uses atomic UPSERT operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="item">The bank item data to save. CharacterID must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the saved bank item ID on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Operation logic:
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) with RETURNING clause:
		/// - If bank item exists (character_id, slot): Updates template_id, seed, amount
		/// - If bank item doesn't exist: Inserts new record
		/// - Returns the ID of the affected row
		/// 
		/// Performance note: Uses raw SQL with UPSERT and RETURNING for optimal atomicity.
		/// </remarks>
		Task<DatabaseResult<long>> SaveBankItemAsync(CharacterBankData item, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates multiple bank items in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="items">Collection of bank item data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All bank items are processed in a single transaction
		/// - If any item fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each item:
		/// - If bank item exists (character_id, slot): Updates template_id, seed, amount
		/// - If bank item doesn't exist: Inserts new record
		/// </remarks>
		Task<DatabaseResult> SaveBankItemsAsync(IEnumerable<CharacterBankData> items, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all bank items for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all bank items for the specified character in a single atomic operation
		/// - If character has no bank items, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// </remarks>
		Task<DatabaseResult> DeleteBankItemsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific bank item by slot.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="slot">The bank slot to delete.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes only the specific bank item matching both character ID and slot
		/// - If item doesn't exist, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Security note: Requires both character ID and slot to prevent
		/// accidental deletion of items from wrong character.
		/// </remarks>
		Task<DatabaseResult> DeleteBankSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all bank items for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character bank data on success, or error information on failure.
		/// Returns empty collection if character has no items.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all bank items for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}