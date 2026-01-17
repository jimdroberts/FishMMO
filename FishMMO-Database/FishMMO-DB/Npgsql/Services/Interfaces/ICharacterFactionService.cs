using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character factions in the database.
	/// Provides async operations for CRUD operations on character faction reputation data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character faction reputation including:
	/// - Batch faction save/update with atomic UPSERT operations
	/// - Faction deletion (bulk operations)
	/// - Faction retrieval
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Faction data uses (character_id, template_id) as unique constraint for UPSERT operations.
	/// Thread-safe updates handle concurrent faction value changes.
	/// 
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </remarks>
	public interface ICharacterFactionService
	{
		/// <summary>
		/// Saves or updates multiple character factions in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="factions">Collection of character faction data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All factions are processed in a single transaction
		/// - If any faction fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each faction:
		/// - If faction exists (character_id, template_id): Updates value (reputation)
		/// - If faction doesn't exist: Inserts new record
		/// 
		/// Possible return scenarios:
		/// - Success: All factions saved successfully
		/// - Failure with VALIDATION_ERROR: Empty or null factions collection
		/// - Failure with DatabaseConstraintException: Constraint violation (unique/foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Transaction rolled back or query error
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
		/// Thread-safe due to UPSERT constraint on (character_id, template_id).
		/// </remarks>
		Task<DatabaseResult> SaveFactionsAsync(IEnumerable<CharacterFactionData> factions, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all factions for a specific character.
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
		/// - Deletes all factions for the specified character in a single atomic operation
		/// - If character has no factions, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Possible return scenarios:
		/// - Success: All factions deleted successfully (or character has no factions)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// </remarks>
		Task<DatabaseResult> DeleteFactionsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all factions for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character faction data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all factions for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Character has factions
		/// - Success with empty collection: Character has no factions
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> GetFactionsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}