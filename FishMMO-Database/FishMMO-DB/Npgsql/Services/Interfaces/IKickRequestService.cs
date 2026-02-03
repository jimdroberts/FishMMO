using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for kick request operations.
	/// Provides async methods for persisting, deleting, and fetching kick requests.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Persist*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because SaveChangesAsync and ExecuteSqlRawAsync do not automatically retry on transient failures
	/// without an execution strategy wrapper.
	/// BaseService provides execution wrappers for retry and centralized exception mapping; explicit transactions
	/// are used only when a write requires multiple database statements.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// Kick requests are used to forcefully disconnect accounts from active sessions.
	/// </para>
	/// </remarks>
	public interface IKickRequestService
	{
		/// <summary>
		/// Persists a kick request for the specified account.
		/// </summary>
		/// <param name="accountName">Account name to kick.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses SaveChangesAsync with execution strategy wrapping to ensure transient database failures
		/// are automatically retried. Creates new kick request with current UTC timestamp.
		/// </remarks>
		Task<DatabaseResult> PersistAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all kick requests for the specified account.
		/// </summary>
		/// <param name="accountName">Account name whose kick requests will be deleted.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the number of kick requests deleted on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns 0 rows deleted if no requests exist (idempotent).
		/// </remarks>
		Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches paginated kick requests based on timestamp and position.
		/// </summary>
		/// <param name="lastFetch">Timestamp to compare requests against.</param>
		/// <param name="lastPosition">Last request ID fetched (for pagination).</param>
		/// <param name="amount">Maximum number of requests to fetch.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the list of kick request data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (ToListAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Uses pagination pattern with timestamp and ID for reliable cursor-based pagination.
		/// Returns empty list for invalid amount.
		/// </remarks>
		Task<DatabaseResult<List<KickRequestData>>> FetchAsync(
			DateTime lastFetch,
			long lastPosition,
			int amount,
			CancellationToken cancellationToken = default);
	}
}