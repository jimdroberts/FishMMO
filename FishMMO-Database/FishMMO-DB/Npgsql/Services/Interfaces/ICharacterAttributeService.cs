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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - Inserts missing attributes and updates existing ones by (CharacterID, TemplateID)
		/// - Skips characters that are missing or marked deleted
		/// - Duplicates in the input are de-duplicated by (CharacterID, TemplateID)
		/// 
		/// Failure cases:
		/// - VALIDATION_ERROR: Empty or null attributes collection
		/// - DATABASE_ERROR: Database failure (may be transient)
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Deletion behavior:
		/// - Deletes all attributes for the specified character
		/// - If character has no attributes, operation succeeds
		/// 
		/// Failure cases:
		/// - VALIDATION_ERROR: Invalid character ID (less than or equal to 0)
		/// - DATABASE_ERROR: Database failure (may be transient)
		/// </remarks>
		Task<DatabaseResult> DeleteAttributesAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all attributes for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>DatabaseResult containing a read-only collection of character attribute data on success.</returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// Returns an empty collection if the character has no attributes.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterAttributeData>>> GetAttributesAsync(long characterId, CancellationToken cancellationToken = default);
	}
}