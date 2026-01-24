using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for guild update timestamp tracking.
	/// Provides async methods for saving, deleting, and fetching guild update records.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Save*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because ExecuteSqlRawAsync does not automatically benefit from
	/// EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Not found scenarios (guild doesn't exist)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// SaveAsync uses atomic UPSERT to prevent race conditions during concurrent updates.
	/// </para>
	/// </remarks>
	public interface IGuildUpdateService
	{
		/// <summary>
		/// Saves or updates the last update timestamp for a guild using atomic UPSERT.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Uses PostgreSQL ON CONFLICT for atomic UPSERT with conditional
		/// update to prevent race conditions.
		/// </remarks>
		Task<DatabaseResult> SaveAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all update records for a guild.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the number of records deleted on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns 0 rows deleted if guild doesn't exist (idempotent).
		/// </remarks>
		Task<DatabaseResult<int>> DeleteAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches guild update records for specified guilds updated since last fetch.
		/// </summary>
		/// <param name="guildIds">List of guild IDs to check.</param>
		/// <param name="lastFetch">Timestamp to compare against.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the list of guild update data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (ToListAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Filters by both timestamp and guild ID list.
		/// </remarks>
		Task<DatabaseResult<List<GuildUpdateData>>> FetchAsync(
			List<long> guildIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default);
	}
}