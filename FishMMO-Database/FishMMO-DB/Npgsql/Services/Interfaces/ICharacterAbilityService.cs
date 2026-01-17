using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character abilities in the database.
	/// Provides async operations for CRUD operations on character ability data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character abilities including:
	/// - Individual ability save/update with atomic UPSERT operations
	/// - Batch ability operations with transaction safety
	/// - Ability deletion (individual and bulk)
	/// - Ability retrieval and counting
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// DatabaseResult provides detailed error information to help distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (entity doesn't exist)
	/// - Database errors (connection issues, constraint violations, transient failures)
	/// - Unexpected runtime errors
	/// </remarks>
	public interface ICharacterAbilityService
	{
		/// <summary>
		/// Gets the number of abilities for a given character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing the count of abilities on success.</returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Success: Returns count (may be 0 if character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// </remarks>
		Task<DatabaseResult<int>> GetCountAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates a single character ability in the database.
		/// Uses atomic operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="abilityData">The character ability data to save. CharacterID must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing the ability ID on success.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Operation logic:
		/// - If abilityData.ID > 0: Performs atomic UPDATE on existing ability
		/// - If abilityData.ID == 0: Inserts new ability and returns generated ID
		/// 
		/// Success: Returns the ability ID (either existing or newly generated).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Database operation error
		/// 
		/// The method handles both tracked entities (for INSERT) and raw SQL (for UPDATE)
		/// to optimize performance based on operation type.
		/// </remarks>
		Task<DatabaseResult<long>> SaveAbilityAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves or updates multiple character abilities in a single transaction.
		/// Uses atomic operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="abilities">Collection of character ability data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All abilities are processed in a single transaction
		/// - If any ability fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// For each ability:
		/// - If ID > 0: Performs UPDATE
		/// - If ID == 0: Performs INSERT
		/// 
		/// Success: All abilities saved successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null abilities collection
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Transaction failed, all changes rolled back
		/// 
		/// Performance note: Uses raw SQL for batch operations to minimize round-trips.
		/// </remarks>
		Task<DatabaseResult> SaveAbilitiesAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all abilities for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all abilities for the specified character in a single atomic operation
		/// - If character has no abilities, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Success: All abilities deleted successfully (or character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Database operation error
		/// </remarks>
		Task<DatabaseResult> DeleteAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific ability for a character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="abilityId">The ability ID to delete. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes only the specific ability matching both character ID and ability ID
		/// - If ability doesn't exist, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Success: Ability deleted successfully (or didn't exist).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID or ability ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Database operation error
		/// 
		/// Security note: Requires both character ID and ability ID to prevent
		/// accidental deletion of abilities from wrong character.
		/// </remarks>
		Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all abilities for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing a read-only collection of character ability data on success.</returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all abilities for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Success: Returns collection (may be empty if character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> GetAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);
	}
}