using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for the append-only guild activity log.
	/// </summary>
	/// <remarks>
	/// Deliberately does not implement the shared <c>IPersistAction</c>/<c>IFetchManyByKeyAction</c>
	/// shapes. Those describe a row that is written, re-read and updated under a version; a log row
	/// is written once and never touched again, and its read is "the most recent N", which the
	/// key-based fetch contracts have no way to express.
	/// </remarks>
	public interface IGuildLogService
	{
		/// <summary>
		/// Appends one row to a guild's activity log.
		/// </summary>
		/// <param name="entry">The event to record. <c>ID</c> is ignored.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>A <see cref="DatabaseResult"/> indicating success or failure.</returns>
		Task<DatabaseResult> AppendAsync(GuildLogData entry, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a guild's most recent log rows, newest first.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="limit">Maximum rows to return.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The most recent rows, newest first.</returns>
		Task<DatabaseResult<IReadOnlyList<GuildLogData>>> FetchRecentAsync(long guildId, int limit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes log rows beyond the most recent <paramref name="keep"/> for one guild.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="keep">Number of newest rows to retain.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The number of rows removed.</returns>
		/// <remarks>
		/// An unbounded append-only table attached to a long-lived guild grows without limit and
		/// nothing else in the schema would ever remove from it. Trimming to a fixed depth is what
		/// makes the feature safe to leave running for a year.
		/// </remarks>
		Task<DatabaseResult<int>> PruneAsync(long guildId, int keep, CancellationToken cancellationToken = default);
	}
}
