using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for party management operations.
	/// Provides async methods for party creation, deletion, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Create*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because SaveChangesAsync and ExecuteSqlRawAsync do not automatically
	/// benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (party doesn't exist)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// </remarks>
	public interface IPartyService
	{
		/// <summary>
		/// Checks if a party exists by ID.
		/// </summary>
		/// <param name="partyId">Party ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing true if party exists, false if not found,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AnyAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Returns Success(false) for invalid party ID.
		/// </remarks>
		Task<DatabaseResult<bool>> ExistsAsync(long partyId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new party (idempotent).
		/// </summary>
		/// <param name="accountId">Account id associated with the request.</param>
		/// <param name="requestId">
			/// Required idempotency key.
			/// Retries of the same logical request must reuse this value to prevent duplicate writes.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the party ID on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method is retry-idempotent using the processed_requests table.
		/// Callers must supply a stable <paramref name="requestId"/> for the logical request; transient execution-strategy
		/// retries will reuse the same processed_requests entry and return deterministically.
		/// </remarks>
		Task<DatabaseResult<long>> CreateAsync(long accountId, Guid requestId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a party by ID.
		/// </summary>
		/// <param name="partyId">Party ID to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// Returns <see cref="DatabaseEntityNotFoundException"/> if party doesn't exist.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long partyId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Loads a party by ID.
		/// </summary>
		/// <param name="partyId">Party ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the party data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// Returns <see cref="DatabaseEntityNotFoundException"/> if party not found.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<PartyData>> LoadAsync(long partyId, CancellationToken cancellationToken = default);
	}
}