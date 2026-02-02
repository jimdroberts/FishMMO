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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - Inserts missing factions and updates existing factions by (CharacterID, TemplateID)
		/// - Skips characters that are missing or marked deleted
		/// - Duplicates in the input are de-duplicated by (CharacterID, TemplateID)
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// If the character has no factions, operation succeeds.
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
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// Returns an empty collection if the character has no factions.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterFactionData>>> GetFactionsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}