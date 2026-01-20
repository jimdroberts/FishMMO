using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character guild membership in the database.
	/// Provides async operations for CRUD operations on character guild membership data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character guild membership including:
	/// - Guild membership save/update with atomic UPSERT operations
	/// - Rank updates
	/// - Membership deletion
	/// - Membership retrieval (individual and guild-wide)
	/// - Member count queries
	/// 
	/// Methods that perform database write operations use execution strategies
	/// to automatically retry on transient failures (up to 3 attempts by default).
	/// This includes connection timeouts, deadlocks, and network interruptions.
	/// 
	/// Guild membership uses character_id as unique constraint for one guild per character.
	/// UPSERT operations handle guild changes automatically.
	/// 
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </remarks>
	public interface ICharacterGuildService
	{
		/// <summary>
		/// Saves or updates a character's guild membership.
		/// Uses atomic UPSERT operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="guildData">The guild membership data to save. CharacterID and GuildID must be greater than 0.</param>
		/// <param name="maxCapacity">Maximum number of members allowed in the guild. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses transaction with row-level locking to atomically validate capacity and save membership.
		/// 
		/// Process:
		/// 1. Checks if character is already a member (UPDATE vs INSERT case)
		/// 2. For new joins, locks guild member rows with FOR UPDATE and counts members
		/// 3. Rejects join if capacity reached with CAPACITY_EXCEEDED error
		/// 4. Performs UPSERT (INSERT ... ON CONFLICT DO UPDATE) on character_id
		/// 
		/// Uses UPSERT (INSERT ... ON CONFLICT DO UPDATE) on character_id:
		/// - If character has guild membership: Updates guild_id, rank, location
		/// - If character has no guild: Inserts new membership if capacity available
		/// 
		/// Possible return scenarios:
		/// - Success: Guild membership saved successfully
		/// - Failure with VALIDATION_ERROR: Invalid character ID, guild ID, or max capacity
		/// - Failure with CAPACITY_EXCEEDED: Guild has reached maximum capacity
		/// - Failure with DatabaseConstraintException: Constraint violation (foreign key)
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Database operation failed
		/// 
		/// Thread-safe due to transaction with row-level locking (FOR UPDATE).
		/// Only one guild per character enforced at database level.
		/// Capacity validation is race-condition safe.
		/// </remarks>
		Task<DatabaseResult> SaveGuildMembershipAsync(CharacterGuildData guildData, int maxCapacity, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates a character's guild rank.
		/// Uses atomic UPDATE operation wrapped in execution strategy for automatic retry.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="guildId">The guild ID. Must be greater than 0.</param>
		/// <param name="rank">The new rank value.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Wrapped in execution strategy to automatically retry on transient failures.
		/// 
		/// Update behavior:
		/// - Updates rank for character in specified guild (WHERE character_id AND guild_id)
		/// - If membership doesn't exist, returns failure with entity not found
		/// - Uses atomic UPDATE without loading entity for performance
		/// 
		/// Possible return scenarios:
		/// - Success: Rank updated successfully
		/// - Failure with VALIDATION_ERROR: Invalid character ID or guild ID
		/// - Failure with DatabaseEntityNotFoundException: Character not in guild or guild mismatch
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Update operation failed
		/// 
		/// Use case: Guild officer promoting/demoting members.
		/// </remarks>
		Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a character's guild membership.
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
		/// - Deletes guild membership for the specified character
		/// - If character has no guild membership, operation succeeds
		/// - Uses raw SQL for optimal performance
		/// 
		/// Possible return scenarios:
		/// - Success: Guild membership deleted successfully (or character has no guild)
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Delete operation failed
		/// 
		/// Use case: Character leaving guild or character deletion cleanup.
		/// </remarks>
		Task<DatabaseResult> DeleteGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character's guild membership.
		/// </summary>
		/// <param name="characterId">The character ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the character guild data if found, or null if not in guild,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns guild membership data for the specified character
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Character is in a guild
		/// - Success with null: Character not in guild
		/// - Failure with VALIDATION_ERROR: Invalid character ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTO is safe to use after database context disposal.
		/// </remarks>
		Task<DatabaseResult<CharacterGuildData?>> GetGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all members of a guild.
		/// </summary>
		/// <param name="guildId">The guild ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only collection of character guild data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns all guild memberships for the specified guild
		/// - Uses AsNoTracking() for optimal read performance
		/// - Projects to DTO to decouple from entity layer
		/// 
		/// Return scenarios:
		/// - Success with data: Guild has members
		/// - Success with empty collection: Guild has no members
		/// - Failure with VALIDATION_ERROR: Invalid guild ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// The returned DTOs are safe to use after database context disposal.
		/// Use case: Guild roster display or member management.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterGuildData>>> GetGuildMembersAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the count of members in a guild.
		/// </summary>
		/// <param name="guildId">The guild ID. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
		/// <returns>
		/// DatabaseResult containing the count of guild members on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ query which automatically benefits from EF Core's
		/// configured retry policy for transient failures.
		/// 
		/// Query behavior:
		/// - Returns count of guild memberships for the specified guild
		/// - Uses AsNoTracking() for optimal read performance
		/// - Uses CountAsync for efficient database-side counting
		/// 
		/// Return scenarios:
		/// - Success with count > 0: Guild has members
		/// - Success with count = 0: Guild has no members
		/// - Failure with VALIDATION_ERROR: Invalid guild ID
		/// - Failure with DatabaseTimeoutException: Operation timed out
		/// - Failure with DatabaseConnectionException: Connection error
		/// - Failure with DatabaseQueryException: Query execution failed
		/// 
		/// Use case: Guild member limit validation or UI display of member count.
		/// </remarks>
		Task<DatabaseResult<int>> GetGuildMemberCountAsync(long guildId, CancellationToken cancellationToken = default);
	}
}