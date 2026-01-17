using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character achievements in the database.
	/// Provides async operations for CRUD operations on character achievement data.
	/// Implements execution strategies for automatic retry on transient database failures.
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
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="achievements">Collection of character achievement data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All achievements are processed in a single transaction
		/// - If any achievement fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each achievement:
		/// - If achievement exists (character_id, template_id): Updates tier and value
		/// - If achievement doesn't exist: Inserts new record
		/// 
		/// Success: All achievements saved successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null achievements collection
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Transaction failed, all changes rolled back
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all achievements for the specified character in a single atomic operation
		/// - If character has no achievements, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Success: All achievements deleted successfully (or character has no achievements).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Database operation error
		/// </remarks>
		Task<DatabaseResult> DeleteAchievementsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all achievements for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing a read-only collection of character achievement data on success.</returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all achievements for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Success: Returns collection (may be empty if character has no achievements).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAchievementData>>> GetAchievementsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}