using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character buffs in the database.
	/// Provides async operations for CRUD operations on character buff data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// </summary>
	/// <remarks>
	/// This service manages character buffs including:
	/// - Batch buff save/update with atomic UPSERT operations
	/// - Buff deletion (bulk operations)
	/// - Buff retrieval
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// All methods return DatabaseResult for safe, typed error handling with sanitized messages
	/// that are suitable for client communication while preserving detailed information for logging.
	/// </remarks>
	public interface ICharacterBuffService
	{
		/// <summary>
		/// Saves or updates multiple character buffs in a single transaction.
		/// Uses atomic UPSERT operations wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="buffs">Collection of character buff data to save. Must not be null or empty.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - Inserts missing buffs and updates existing ones by (CharacterID, TemplateID)
		/// - Skips characters that are missing or marked deleted
		/// - Duplicates in the input are de-duplicated by (CharacterID, TemplateID)
		/// </remarks>
		Task<DatabaseResult> SaveBuffsAsync(IEnumerable<CharacterBuffData> buffs, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all buffs for a specific character.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or failure with error information.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// If the character has no buffs, operation succeeds.
		/// </remarks>
		Task<DatabaseResult> DeleteBuffsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all buffs for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character buff data on success, or error information on failure.
		/// Returns empty collection if character has no buffs.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// Returns an empty collection if the character has no buffs.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterBuffData>>> GetBuffsAsync(long characterId, CancellationToken cancellationToken = default);
	}
}