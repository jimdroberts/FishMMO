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
		/// Fetches the plots among <paramref name="plotIDs"/> that changed at or after
		/// <paramref name="lastFetch"/>, and the database time the read was taken at.
		/// </summary>
		/// <param name="plotIDs">The plots to watch. May be empty: the poll still reports its time.</param>
		/// <param name="lastFetch">
		/// Where the window starts, on the DATABASE clock — a previous poll's
		/// <see cref="PlotUpdatePollData.AsOfUtc"/>, less a margin for writes committed after their
		/// stamp — or <see cref="DateTime.MinValue"/> for every mark there is.
		/// </param>
		/// <remarks>
		/// Marks are stamped by the database clock, so the window has to be measured on it too. A
		/// window measured on the poller's own clock lost every change stamped within however far
		/// that clock ran ahead.
		/// </remarks>
		Task<DatabaseResult<PlotUpdatePollData>> FetchAsync(List<long> plotIDs, DateTime lastFetch, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes the update record for a plot.
		/// </summary>
		Task<DatabaseResult<int>> DeleteAsync(long plotID, CancellationToken cancellationToken = default);
	}
}
