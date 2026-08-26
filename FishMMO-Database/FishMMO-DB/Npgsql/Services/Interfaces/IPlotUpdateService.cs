using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Records when plots change, so scene servers hosting other channels can notice.
	/// </summary>
	public interface IPlotUpdateService
	{
		/// <summary>
		/// Marks a plot as changed as of now.
		/// </summary>
		Task<DatabaseResult> PersistAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches the plots among <paramref name="plotIDs"/> that changed at or after <paramref name="lastFetch"/>.
		/// </summary>
		Task<DatabaseResult<List<PlotUpdateData>>> FetchAsync(List<long> plotIDs, DateTime lastFetch, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes the update record for a plot.
		/// </summary>
		Task<DatabaseResult<int>> DeleteAsync(long plotID, CancellationToken cancellationToken = default);
	}
}
