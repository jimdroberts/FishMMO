using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for party update timestamp tracking.
	/// Provides async methods for saving, deleting, and fetching party update records.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Save*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because ExecuteSqlRawAsync does not automatically benefit from
	/// Execution is wrapped by BaseService for retries/transactions.
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
	/// <para>
	/// SaveAsync uses atomic UPSERT to prevent race conditions during concurrent updates.
	/// </para>
	/// </remarks>
	public interface IPartyUpdateService
	{
		/// <summary>
		/// Saves or updates the last update timestamp for a party using atomic UPSERT.
		/// </summary>
		/// <param name="partyId">Party ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Uses PostgreSQL ON CONFLICT for atomic UPSERT with conditional
		/// update to prevent race conditions.
		/// </remarks>
		Task<DatabaseResult> SaveAsync(long partyId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all update records for a party.
		/// </summary>
		/// <param name="partyId">Party ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the number of records deleted on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns 0 rows deleted if party doesn't exist (idempotent).
		/// </remarks>
		Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches party update records for specified parties updated since last fetch.
		/// </summary>
		/// <param name="partyIds">List of party IDs to check.</param>
		/// <param name="lastFetch">Timestamp to compare against.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the list of party update data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (ToListAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Filters by both timestamp and party ID list.
		/// </remarks>
		Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default);
	}
}