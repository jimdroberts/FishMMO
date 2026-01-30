using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character abilities in the database.
	/// Provides async operations for CRUD operations on character ability data.
	/// Uses service-level execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character abilities including:
	/// - Individual ability save/update
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
		/// Success: Returns count (may be 0 if character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Unexpected database error
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
		/// Success: Returns the ability ID (either existing or newly generated).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - ENTITY_NOT_FOUND: Character does not exist or is deleted
		/// - DATABASE_ERROR: Unexpected database error
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
		/// Transaction behavior:
		/// - All abilities are processed in a single transaction
		/// - If any ability fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Success: All abilities saved successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null abilities collection
		/// - DATABASE_ERROR: Transaction failed, all changes rolled back
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
		/// Deletion behavior:
		/// - Deletes all abilities for the specified character in a single atomic operation
		/// - If character has no abilities, operation succeeds
		/// 
		/// Success: All abilities deleted successfully (or character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Database operation error
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
		/// Deletion behavior:
		/// - Deletes only the specific ability matching both character ID and ability ID
		/// - If ability doesn't exist, operation succeeds
		/// 
		/// Success: Ability deleted successfully (or didn't exist).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID or ability ID (less than or equal to 0)
		/// - DATABASE_ERROR: Database operation error
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
		/// Query behavior:
		/// - Returns all abilities for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Success: Returns collection (may be empty if character has no abilities).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Query error
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> GetAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);
	}
}