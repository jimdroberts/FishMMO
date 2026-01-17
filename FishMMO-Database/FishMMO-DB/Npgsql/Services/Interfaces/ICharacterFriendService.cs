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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Uses INSERT ... ON CONFLICT DO NOTHING for idempotent friend additions:
		/// - If friend relationship already exists: No-op, returns success
		/// - If friend relationship doesn't exist: Inserts new record
		/// 
		/// Possible return scenarios:
		/// - Success: Friend relationship saved successfully (or already exists)
		/// - Failure with VALIDATION_ERROR: Invalid character ID or friend character ID
		/// - Failure with DatabaseConstraintException: Constraint violation (foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Database operation failed
		/// 
		/// Thread-safe due to ON CONFLICT constraint on (character_id, friend_character_id).
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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes the specific friend relationship (character_id, friend_character_id)
		/// - If relationship doesn't exist, operation succeeds
		/// - Uses raw SQL with specific WHERE clause for atomic deletion
		/// 
		/// Possible return scenarios:
		/// - Success: Friend relationship deleted successfully (or didn't exist)
		/// - Failure with VALIDATION_ERROR: Invalid character ID or friend character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// 
		/// Note: This is NOT bidirectional. Only removes characterId -> friendCharacterId.
		/// Call twice with swapped parameters for bidirectional unfriend.
		/// </remarks>
		Task<DatabaseResult> DeleteFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default);

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
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Deletion behavior:
		/// - Deletes all friend relationships for the specified character
		/// - If character has no friends, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Possible return scenarios:
		/// - Success: All friends deleted successfully (or character has no friends)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// 
		/// Use case: Character deletion cleanup or friend list reset.
		/// </remarks>
		Task<DatabaseResult> DeleteAllFriendsAsync(long characterId, CancellationToken cancellationToken = default);

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
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all friend relationships for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Character has friends
		/// - Success with empty collection: Character has no friends
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTOs are safe to use after database context disposal.
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
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns count of friend relationships for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Uses CountAsync for efficient database-side counting
		/// 
		/// Return scenarios:
		/// - Success with count > 0: Character has friends
		/// - Success with count = 0: Character has no friends
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// Use case: Friend limit validation or UI display of friend count.
		/// </remarks>
		Task<DatabaseResult<int>> GetFriendCountAsync(long characterId, CancellationToken cancellationToken = default);
	}
}