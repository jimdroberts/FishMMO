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
		/// Checks whether a pending kick request exists for the specified account.
		/// </summary>
		/// <remarks>
		/// A request is pending for a bounded time after its stamp, measured by the database's
		/// clock, which wrote the stamp; never by the calling process's.
		/// </remarks>
		/// <param name="accountName">Account name to check.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing <c>true</c> if a pending kick request exists,
		/// <c>false</c> otherwise, or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		Task<DatabaseResult<bool>> HasPendingAsync(string accountName, CancellationToken cancellationToken = default);

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
		/// <para>
		/// This is an idempotent cleanup operation. Unlike entity delete methods, this method does NOT throw
		/// <see cref="DatabaseEntityNotFoundException"/> when no records exist. Instead, it returns 0 rows deleted.
		/// This design supports safe concurrent cleanup where multiple callers may attempt to delete the same records.
		/// </para>
		/// <para>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried.
		/// </para>
		/// </remarks>
		Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Reads one page of kick requests for a game server's poll, on the database's clock.
		/// </summary>
		/// <param name="query">
		/// Where to read from and what to skip, built by <see cref="KickRequestReadWindow.BuildQuery"/>.
		/// </param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The page — requests stamped at or after the start and not already handled, in
		/// <c>(time_created, id)</c> order — with the database clock taken before it was read and
		/// the start it read from; or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// The reader keeps its place with <see cref="KickRequestReadWindow"/>, which reads a commit
		/// window (<see cref="KickRequestService.PollCommitWindowSeconds"/>) behind what it has
		/// settled and skips what it has handled, so a request that commits after a later-stamped
		/// one is still read. A first read (no <see cref="KickRequestPollQuery.FromUtc"/>) starts
		/// <see cref="KickRequestPollQuery.FirstReadLookbackSeconds"/> before the database's "now".
		/// Each request carries its account's last login (<see cref="KickRequestData.AccountLastLogin"/>),
		/// read in the same query, so the caller needs no per-kick lookup to tell a stale kick.
		/// </remarks>
		Task<DatabaseResult<KickRequestPage>> FetchAsync(
			KickRequestPollQuery query,
			CancellationToken cancellationToken = default);
	}
}