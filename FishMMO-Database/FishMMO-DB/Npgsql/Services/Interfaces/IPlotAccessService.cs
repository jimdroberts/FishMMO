using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Reads and writes who an owner has let into their plot.
	/// </summary>
	public interface IPlotAccessService
	{
		/// <summary>
		/// Grants or replaces one character's permissions on a plot.
		/// </summary>
		/// <remarks>
		/// Replaces rather than adds. A grant is the whole answer to "what may this person do here",
		/// so a re-grant that merged with what was already there could never take a permission away
		/// — an owner narrowing a friend's access would watch it succeed and change nothing.
		/// </remarks>
		Task<DatabaseResult<int>> GrantAsync(long plotID, long characterID, int permissions, long grantedByCharacterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes one character's access to a plot.
		/// </summary>
		/// <returns>The number of rows removed: one when they had access, zero when they did not.</returns>
		Task<DatabaseResult<int>> RevokeAsync(long plotID, long characterID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes everybody's access to a plot.
		/// </summary>
		/// <remarks>
		/// Run when a plot changes hands. A new owner must not inherit the last one's guest list,
		/// and a returning owner must not find people they evicted still holding keys.
		/// </remarks>
		Task<DatabaseResult<int>> RevokeAllAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Everybody with access to one plot.
		/// </summary>
		Task<DatabaseResult<List<PlotAccessData>>> FetchByPlotAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Every grant across a set of plots, for populating a scene's foundations in one query.
		/// </summary>
		Task<DatabaseResult<List<PlotAccessData>>> FetchByPlotsAsync(List<long> plotIDs, CancellationToken cancellationToken = default);

		/// <summary>
		/// Every plot one character has been let into.
		/// </summary>
		Task<DatabaseResult<List<PlotAccessData>>> FetchByCharacterAsync(long characterID, CancellationToken cancellationToken = default);
	}
}
