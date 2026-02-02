using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character friend relationships in the database.
	/// Provides async operations for CRUD operations on character friend data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character friend relationships including:
	/// - Friend relationship creation with atomic INSERT operations
	/// - Friend deletion (individual and bulk)
	/// - Friend retrieval and count queries
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Friend relationships use (character_id, friend_character_id) as unique constraint.
	/// INSERT ... ON CONFLICT DO NOTHING handles duplicate friend additions safely.
	/// 
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </remarks>
	public interface ICharacterFriendService
	{
		/// <summary>
		/// Saves a friend relationship to the database.
		/// Uses atomic INSERT with conflict handling wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="friendCharacterId">The friend character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// 
		/// Behavior:
		/// - If the source character is missing or marked deleted: no-op success
		/// - If the relationship already exists: no-op success
		/// - Otherwise inserts a new relationship
		/// </remarks>
		Task<DatabaseResult> SaveFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific friend relationship.
		/// Uses atomic DELETE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="friendCharacterId">The friend character ID to remove. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// If the relationship doesn't exist, operation succeeds.
		/// </remarks>
		Task<DatabaseResult> DeleteFriendAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all friends for a specific character.
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
		/// If the character has no friends, operation succeeds.
		/// </remarks>
		Task<DatabaseResult> DeleteAllFriendsAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all friends for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character friend data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// Returns an empty collection if the character has no friends.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterFriendData>>> GetFriendsAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the count of friends for a character.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the count of friends on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// Executes inside the BaseService execution wrapper (retry + centralized exception mapping).
		/// Uses an explicit transaction only when more than one database statement is required.
		/// </remarks>
		Task<DatabaseResult<int>> GetFriendCountAsync(long characterId, CancellationToken cancellationToken = default);
	}
}