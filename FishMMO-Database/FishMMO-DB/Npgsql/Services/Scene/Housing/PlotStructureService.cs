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
	/// Reads and writes the structures built on plots.
	/// </summary>
	public sealed class PlotStructureService : BaseService<PlotStructureEntity>, IPlotStructureService
	{
		public PlotStructureService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<long>> PlaceAsync(long plotID, int templateID, float localX, float localY, float localZ, float yaw, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (templateID == 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Template ID must not be zero.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* RETURNING, so the caller has the row's identity before it spawns anything. A
				 * structure it cannot name is one it cannot later demolish. */
				string sql = $@"INSERT INTO {TableName} (plot_id, template_id, local_x, local_y, local_z, yaw, version, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, 1, {{6}})
					RETURNING id";

				return await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { plotID, templateID, localX, localY, localZ, yaw, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> DemolishAsync(long structureID, long plotID, CancellationToken cancellationToken = default)
		{
			if (structureID <= 0 || plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Structure and plot IDs must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the plot the caller was authorised against, so a permission check that
				 * passed for one plot cannot delete a structure standing on another. */
				string sql = $@"DELETE FROM {TableName} WHERE id = {{0}} AND plot_id = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { structureID, plotID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> DemolishAllAsync(long plotID, CancellationToken cancellationToken = default)
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

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotStructureData>>> FetchByPlotAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<List<PlotStructureData>>.Success(new List<PlotStructureData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotStructureEntity> structures = await dbContext.PlotStructures
					.AsNoTracking()
					.Where(e => e.PlotID == plotID)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(structures);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotStructureData>>> FetchByPlotsAsync(List<long> plotIDs, CancellationToken cancellationToken = default)
		{
			if (plotIDs == null || plotIDs.Count < 1)
			{
				return DatabaseResult<List<PlotStructureData>>.Success(new List<PlotStructureData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				long[] ids = plotIDs.Distinct().ToArray();

				List<PlotStructureEntity> structures = await dbContext.PlotStructures
					.AsNoTracking()
					.Where(e => ids.Contains(e.PlotID))
					.OrderBy(e => e.PlotID)
					.ThenBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(structures);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps structure entities to their data transfer objects.
		/// </summary>
		private static List<PlotStructureData> MapMany(List<PlotStructureEntity> structures)
		{
			List<PlotStructureData> results = new List<PlotStructureData>(structures.Count);
			foreach (PlotStructureEntity structure in structures)
			{
				results.Add(new PlotStructureData(
					structure.ID,
					structure.PlotID,
					structure.TemplateID,
					structure.LocalX,
					structure.LocalY,
					structure.LocalZ,
					structure.Yaw));
			}
			return results;
		}
	}
}
