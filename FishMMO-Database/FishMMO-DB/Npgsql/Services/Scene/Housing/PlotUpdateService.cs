using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Records when plots change, so scene servers hosting other channels can notice.
	/// </summary>
	/// <remarks>
	/// The same polling shape guilds use, for the same reason: a plot is visible from every scene
	/// server hosting a channel of its scene, and the server that processed a claim is not the one
	/// that has to redraw the foundation everywhere else.
	/// </remarks>
	public sealed class PlotUpdateService : BaseService<PlotUpdateEntity>, IPlotUpdateService
	{
		public PlotUpdateService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult> PersistAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* The guarded UPSERT guilds use. The trailing WHERE keeps the stored timestamp
				 * monotonic: two scene servers recording the same plot within the same instant can
				 * arrive out of order, and letting the older one win would move last_update
				 * backwards past a poller that had already read it — which is how a change gets
				 * skipped rather than merely delayed. */

				/* Stamped by the DATABASE clock, not this server's. Every scene server writes these
				 * markers and every other one reads "everything at or after my watermark", so a
				 * writer whose clock ran behind the readers' stamped changes they had already swept
				 * past: the change was never delivered, not merely late (issue #267 audit).
				 * clock_timestamp() is one clock for every writer, and is read at the statement,
				 * not at transaction start. AT TIME ZONE 'UTC' because the column holds UTC without
				 * a zone and the server's own zone is whatever it was installed with. */
				string sql = $@"INSERT INTO {TableName} (plot_id, time_created, last_update)
					VALUES ({{0}}, clock_timestamp() AT TIME ZONE 'UTC', clock_timestamp() AT TIME ZONE 'UTC')
					ON CONFLICT (plot_id) DO UPDATE
					SET last_update = EXCLUDED.last_update
					WHERE {TableName}.last_update < EXCLUDED.last_update";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotUpdateData>>> FetchAsync(List<long> plotIDs, DateTime lastFetch, CancellationToken cancellationToken = default)
		{
			if (plotIDs == null || plotIDs.Count < 1)
			{
				return DatabaseResult<List<PlotUpdateData>>.Success(new List<PlotUpdateData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				long[] ids = plotIDs.Distinct().ToArray();

				List<PlotUpdateEntity> updates = await dbContext.PlotUpdates
					.AsNoTracking()
					.Where(e => e.LastUpdate >= lastFetch && ids.Contains(e.PlotID))
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<PlotUpdateData> results = new List<PlotUpdateData>(updates.Count);
				foreach (PlotUpdateEntity update in updates)
				{
					results.Add(new PlotUpdateData(update.ID, update.PlotID, update.LastUpdate));
				}
				return results;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> DeleteAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"DELETE FROM {TableName} WHERE plot_id = {{0}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
