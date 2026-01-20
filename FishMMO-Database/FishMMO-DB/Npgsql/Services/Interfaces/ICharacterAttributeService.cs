using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character attributes in the database.
	/// Provides async operations for CRUD operations on character attribute data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character attributes including:
	/// - Batch attribute save/update with atomic UPSERT operations
	/// - Attribute deletion (bulk operations)
	/// - Attribute retrieval
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
	public interface ICharacterAttributeService
	{
		/// <summary>
		/// Saves or updates multiple character attributes in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="attributes">Collection of character attribute data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// All operations are performed within a database transaction for atomicity.
		/// 
		/// Transaction behavior:
		/// - All attributes are processed in a single transaction
		/// - If any attribute fails, entire transaction is rolled back
		/// - On success, all changes are committed atomically
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) for each attribute:
		/// - If attribute exists (character_id, template_id): Updates value and current_value
		/// - If attribute doesn't exist: Inserts new record
		/// 
		/// Success: All attributes saved successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null attributes collection
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Transaction failed, all changes rolled back
		/// 
		/// Performance note: Uses raw SQL with UPSERT for optimal concurrency and minimal round-trips.
		/// </remarks>
		Task<DatabaseResult> SaveAttributesAsync(IEnumerable<CharacterAttributeData> attributes, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all attributes for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all attributes for the specified character in a single atomic operation
		/// - If character has no attributes, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Success: All attributes deleted successfully (or character has no attributes).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_QUERY_FAILED: Database operation error
		/// </remarks>
		Task<DatabaseResult> DeleteAttributesAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all attributes for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing a read-only collection of character attribute data on success.</returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all attributes for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Success: Returns collection (may be empty if character has no attributes).
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Query timeout (transient)
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAttributeData>>> GetAttributesAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically increments or decrements a character attribute value.
		/// Uses database-level atomic operations to prevent race conditions.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="templateId">The attribute template ID.</param>
		/// <param name="valueDelta">Amount to add to the value field (can be negative for decrement).</param>
		/// <param name="currentValueDelta">Amount to add to the current_value field (can be negative for decrement).</param>
		/// <param name="allowNegative">Whether to allow negative values. If false, operation fails if result would be negative.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult indicating success or failure with error details.</returns>
		/// <remarks>
		/// This method provides atomic increment/decrement operations to prevent race conditions
		/// in concurrent currency/attribute modifications.
		/// 
		/// Atomicity guarantees:
		/// - Uses PostgreSQL's atomic UPDATE with arithmetic operations
		/// - Multiple concurrent increments will all be applied correctly
		/// - No lost updates from read-modify-write race conditions
		/// 
		/// Behavior:
		/// - If attribute exists: Atomically adds delta to current values
		/// - If attribute doesn't exist: Creates new attribute with delta as initial value
		/// - If allowNegative is false and result would be negative: Operation fails, no changes made
		/// 
		/// Success: Attribute incremented/decremented successfully.
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID or would result in negative value when not allowed
		/// - DB_CONNECTION_FAILED: Database connection error (transient)
		/// - DB_TIMEOUT: Operation timeout (transient)
		/// - DB_CONSTRAINT_VIOLATION: Constraint check failed (e.g., negative value when not allowed)
		/// 
		/// Performance: Single atomic SQL operation, no transaction overhead needed.
		/// </remarks>
		Task<DatabaseResult> IncrementAttributeAsync(
			long characterId,
			int templateId,
			int valueDelta,
			float currentValueDelta,
			bool allowNegative = false,
			CancellationToken cancellationToken = default);
	}
}