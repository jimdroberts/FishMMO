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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - Inserts a new item if missing, otherwise updates the existing item by (CharacterID, Slot)
		/// - Returns the affected row ID
		/// - Fails with ENTITY_NOT_FOUND if the character is missing or marked deleted
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - Inserts missing items and updates existing ones by (CharacterID, Slot)
		/// - Skips characters that are missing or marked deleted
		/// - Duplicates in the input are de-duplicated by (CharacterID, Slot)
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// If the character has no bank items, operation succeeds.
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// If the slot does not exist for the character, operation succeeds.
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// Returns an empty collection if the character has no items.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterBankData>>> GetBankItemsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}