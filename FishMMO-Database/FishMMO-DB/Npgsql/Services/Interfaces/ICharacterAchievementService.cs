using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character achievements in the database.
	/// Provides async operations for CRUD operations on character achievement data.
	/// Uses service-level execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character achievements including:
	/// - Batch achievement save/update with atomic UPSERT operations
	/// - Achievement deletion (bulk operations)
	/// - Achievement retrieval
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
	public interface ICharacterAchievementService
	{
		/// <summary>
		/// Saves or updates multiple character achievements in a single transaction.
		/// Uses a single transactional operation wrapped in the service retry strategy.
		/// </summary>
		/// <param name="achievements">Collection of character achievement data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Transaction behavior:
		/// - All achievements are processed in a single transaction
		/// - If any achievement fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// - Achievements targeting deleted/missing characters are skipped
		/// 
		/// Success: All achievements saved successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null achievements collection
		/// - DATABASE_ERROR: Transaction failed, all changes rolled back
		/// </remarks>
		Task<DatabaseResult> SaveAchievementsAsync(IEnumerable<CharacterAchievementData> achievements, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all achievements for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Deletion behavior:
		/// - Deletes all achievements for the specified character in a single atomic operation
		/// - If character has no achievements, operation succeeds
		/// 
		/// Success: All achievements deleted successfully (or character has no achievements).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Database operation error
		/// </remarks>
		Task<DatabaseResult> DeleteAchievementsAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all achievements for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing a read-only collection of character achievement data on success.</returns>
		/// <remarks>
		/// Query behavior:
		/// - Returns all achievements for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Success: Returns collection (may be empty if character has no achievements).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Query error
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAchievementData>>> GetAchievementsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}