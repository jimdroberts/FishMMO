using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character equipment in the database.
	/// Provides async operations for CRUD operations on character equipment data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character equipment including:
	/// - Single equipment item save/update with atomic UPSERT operations
	/// - Batch equipment save/update with transactions
	/// - Equipment deletion (bulk and slot-specific)
	/// - Equipment retrieval
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Equipment uses slot-based storage with (character_id, slot) as unique constraint.
	/// UPSERT operations handle conflicts automatically for thread-safe updates.
	/// 
	/// All methods return DatabaseResult for safe, typed error handling with sanitized messages
	/// that are suitable for client communication while preserving detailed information for logging.
	/// </remarks>
	public interface ICharacterEquipmentService
	{
		/// <summary>
		/// Saves or updates a single equipment item in the database.
		/// Uses atomic UPSERT operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="equipment">The equipment data to save. CharacterID must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the saved equipment item ID on success, or error information on failure.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService transaction + retry wrapper.
		/// 
		/// Behavior:
		/// - Inserts a new equipped item if missing, otherwise updates the existing item by (CharacterID, Slot)
		/// - Returns the affected row ID
		/// - Fails with ENTITY_NOT_FOUND if the character is missing or marked deleted
		/// </remarks>
		Task<DatabaseResult<long>> SaveEquipmentAsync(CharacterEquipmentData equipment, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates multiple equipment items in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="equipment">Collection of equipment data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService transaction + retry wrapper.
		/// 
		/// Behavior:
		/// - Inserts missing equipment and updates existing equipment by (CharacterID, Slot)
		/// - Skips characters that are missing or marked deleted
		/// - Duplicates in the input are de-duplicated by (CharacterID, Slot)
		/// </remarks>
		Task<DatabaseResult> SaveEquipmentMultipleAsync(IEnumerable<CharacterEquipmentData> equipment, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all equipment items for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService transaction + retry wrapper.
		/// If the character has no equipment, operation succeeds.
		/// </remarks>
		Task<DatabaseResult> DeleteEquipmentAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific equipment item by slot.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="slot">The equipment slot to clear.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService transaction + retry wrapper.
		/// If the slot is empty, operation succeeds.
		/// </remarks>
		Task<DatabaseResult> DeleteEquipmentSlotAsync(long characterId, int slot, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all equipment items for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character equipment data on success, or error information on failure.
		/// Returns empty collection if character has no equipment.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService transaction + retry wrapper.
		/// Returns an empty collection if the character has no equipment.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> GetEquipmentAsync(long characterId, CancellationToken cancellationToken = default);
	}
}