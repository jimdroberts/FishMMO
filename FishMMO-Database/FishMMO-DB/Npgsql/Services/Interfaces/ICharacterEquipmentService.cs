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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) with RETURNING clause:
		/// - If equipment exists at slot (character_id, slot): Updates template_id, seed, amount
		/// - If equipment doesn't exist at slot: Inserts new record
		/// - Returns the saved equipment data including ID
		/// 
		/// Performance note: Uses FromSqlInterpolated with RETURNING for single round-trip efficiency.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All equipment items are processed in a single transaction
		/// - If any item fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each item:
		/// - If equipment exists at slot (character_id, slot): Updates template_id, seed, amount
		/// - If equipment doesn't exist at slot: Inserts new record
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
		/// Thread-safe due to UPSERT constraint on (character_id, slot).
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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all equipment items for the specified character in a single atomic operation
		/// - If character has no equipment, operation succeeds
		/// - Uses raw SQL for optimal performance
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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes equipment item at the specified slot for the character
		/// - If slot is empty, operation succeeds
		/// - Uses raw SQL with specific WHERE clause for atomic deletion
		/// 
		/// Use case: Unequipping a single item without affecting other equipped items.
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
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all equipment items for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterEquipmentData>>> GetEquipmentAsync(long characterId, CancellationToken cancellationToken = default);
	}
}