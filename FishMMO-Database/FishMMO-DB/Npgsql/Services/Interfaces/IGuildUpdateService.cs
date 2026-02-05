using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for guild update timestamp tracking.
	/// Provides async methods for persisting, deleting, and fetching guild update records.
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
	/// - Not found scenarios (guild doesn't exist)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// PersistAsync uses a single-statement UPSERT to prevent race conditions during concurrent updates.
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
		/// Uses a single-statement PostgreSQL <c>INSERT ... ON CONFLICT DO UPDATE</c> with a conditional
		/// update to avoid regressing <c>last_update</c>.
		/// </remarks>
		Task<DatabaseResult> PersistAsync(long guildId, CancellationToken cancellationToken = default);

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
		/// <para>
		/// This is an idempotent cleanup operation. Unlike entity delete methods, this method does NOT throw
		/// <see cref="DatabaseEntityNotFoundException"/> when no records exist. Instead, it returns 0 rows deleted.
		/// This design supports safe concurrent cleanup where multiple callers may attempt to delete the same records.
		/// </para>
		/// <para>
		/// Uses a single-statement <c>DELETE</c>.
		/// </para>
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
		/// Filters by both timestamp and guild ID list.
		/// </remarks>
		Task<DatabaseResult<List<GuildUpdateData>>> FetchAsync(
			List<long> guildIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default);
	}
}