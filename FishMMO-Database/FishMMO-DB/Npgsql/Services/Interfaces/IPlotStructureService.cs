using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Reads and writes the structures built on plots.
	/// </summary>
	public interface IPlotStructureService
	{
		/// <summary>
		/// Records a newly built structure.
		/// </summary>
		/// <returns>The new structure's identity, or zero on failure.</returns>
		Task<DatabaseResult<long>> PlaceAsync(long plotID, int templateID, float localX, float localY, float localZ, float yaw, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes a structure, if it stands on the expected plot.
		/// </summary>
		/// <param name="structureID">The structure to remove.</param>
		/// <param name="plotID">The plot it is expected to stand on.</param>
		/// <returns>1 when removed, 0 when it does not exist or is on another plot.</returns>
		/// <remarks>
		/// Pinned to the plot so a demolition request can only ever affect the plot the caller was
		/// judged against. Without it, a permission check that passed for one plot would authorise
		/// deleting a structure on somebody else's.
		/// </remarks>
		Task<DatabaseResult<int>> DemolishAsync(long structureID, long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Removes everything built on a plot.
		/// </summary>
		/// <returns>The number of structures removed.</returns>
		/// <remarks>
		/// Used when a plot is released or reclaimed: land handed to somebody else must not come
		/// with the previous owner's house still standing on it.
		/// </remarks>
		Task<DatabaseResult<int>> DemolishAllAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches everything built on a plot.
		/// </summary>
		Task<DatabaseResult<List<PlotStructureData>>> FetchByPlotAsync(long plotID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches everything built on any of the given plots.
		/// </summary>
		/// <remarks>
		/// One query for a whole scene's worth of plots. Fetching per plot would be one round trip
		/// per foundation on every scene load, which is the cost this exists to avoid.
		/// </remarks>
		Task<DatabaseResult<List<PlotStructureData>>> FetchByPlotsAsync(List<long> plotIDs, CancellationToken cancellationToken = default);
	}
}
