using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for party update timestamp tracking.
	/// Provides async methods for persisting, deleting, and fetching party update records.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Persist*, Delete*) are executed through <see cref="BaseService{TEntity}"/> wrappers,
	/// which provide consistent retry and error mapping behavior for transient database failures.
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
	/// PersistAsync uses a single-statement UPSERT to prevent race conditions during concurrent updates.
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
		/// Uses a single-statement PostgreSQL <c>INSERT ... ON CONFLICT DO UPDATE</c> with a conditional
		/// update to avoid regressing <c>last_update</c>.
		/// </remarks>
		Task<DatabaseResult> PersistAsync(long partyId, CancellationToken cancellationToken = default);

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
		/// <para>
		/// This is an idempotent cleanup operation. Unlike entity delete methods, this method does NOT throw
		/// <see cref="DatabaseEntityNotFoundException"/> when no records exist. Instead, it returns 0 rows deleted.
		/// This design supports safe concurrent cleanup where multiple callers may attempt to delete the same records.
		/// </para>
		/// <para>
		/// Uses a single-statement <c>DELETE</c>.
		/// </para>
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
		/// Filters by both timestamp and party ID list.
		/// </remarks>
		Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default);
	}
}