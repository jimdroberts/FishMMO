using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character party membership in the database.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// <para>
	/// All write operations (Save*, Update*, Delete*) in this service use execution strategies to ensure
	/// transient database failures are automatically retried according to the retry policy configured
	/// on the DbContext. This is critical because ExecuteSqlInterpolatedAsync does not automatically
	/// benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </para>
	/// <para>
	/// All SQL operations use atomic UPSERT, UPDATE, and DELETE commands to prevent race conditions
	/// when multiple servers or clients modify party memberships simultaneously.
	/// </para>
	/// </remarks>
	public interface ICharacterPartyService
	{
		/// <summary>
		/// Saves or updates a character's party membership using an atomic transaction with capacity validation.
		/// </summary>
		/// <param name="partyData">The party membership data to save.</param>
		/// <param name="maxCapacity">Maximum number of members allowed in the party. Must be greater than 0.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses transaction with row-level locking to atomically validate capacity and save membership.
		/// 
		/// Process:
		/// 1. Checks if character is already a member (UPDATE vs INSERT case)
		/// 2. For new joins, locks party member rows with FOR UPDATE and counts members
		/// 3. Rejects join if capacity reached with CAPACITY_EXCEEDED error
		/// 4. Performs UPSERT (INSERT ... ON CONFLICT DO UPDATE)
		/// 
		/// Uses PostgreSQL INSERT ON CONFLICT to ensure atomic insert-or-update operations.
		/// Transaction ensures capacity validation is race-condition safe.
		/// </remarks>
		Task<DatabaseResult> SavePartyMembershipAsync(CharacterPartyData partyData, int maxCapacity, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates a character's party rank atomically.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="partyId">The party ID.</param>
		/// <param name="rank">The new rank.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE without loading the entity. Returns DatabaseEntityNotFoundException if the membership doesn't exist.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a character's party membership.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses atomic DELETE operation. Returns success even if the membership doesn't exist (idempotent).
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character's party membership.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult containing the character party data if found (or null if not in a party),
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AsNoTracking) for optimal read performance and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<CharacterPartyData?>> GetPartyMembershipAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all members of a party.
		/// </summary>
		/// <param name="partyId">The party ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only list of character party data on success,
		/// or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AsNoTracking) for optimal read performance and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterPartyData>>> GetPartyMembersAsync(long partyId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the count of members in a party.
		/// </summary>
		/// <param name="partyId">The party ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult containing the count of party members on success, or error details on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (CountAsync with AsNoTracking) for optimal read performance and automatically
		/// benefits from the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<int>> GetPartyMemberCountAsync(long partyId, CancellationToken cancellationToken = default);
	}
}