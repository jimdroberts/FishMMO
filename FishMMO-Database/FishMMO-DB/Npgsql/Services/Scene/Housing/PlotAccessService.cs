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
	/// Reads and writes who an owner has let into their plot.
	/// </summary>
	public sealed class PlotAccessService : BaseService<PlotAccessEntity>, IPlotAccessService
	{
		public PlotAccessService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> GrantAsync(long plotID, long characterID, int permissions, long grantedByCharacterID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (characterID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}
			if (permissions == 0)
			{
				/* An empty grant is a revoke wearing a grant's name. Storing it would leave a row
				 * that means nothing, shows up in the owner's guest list, and reads as access. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A grant must carry at least one permission; use RevokeAsync to remove access.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* UPSERT, with the conflict overwriting rather than merging. Re-granting is how an
				 * owner narrows somebody's access as well as how they widen it, so the new mask has
				 * to replace the old one outright — a merge could only ever add permissions, and an
				 * owner taking one away would watch the write succeed and nothing change. */
				string sql = $@"INSERT INTO {TableName} (plot_id, character_id, permissions, granted_by_character_id, version, time_granted)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, 1, {{4}})
					ON CONFLICT (plot_id, character_id) DO UPDATE
					SET permissions = EXCLUDED.permissions,
						granted_by_character_id = EXCLUDED.granted_by_character_id,
						time_granted = EXCLUDED.time_granted,
						version = {TableName}.version + 1";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, characterID, permissions, grantedByCharacterID, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> RevokeAsync(long plotID, long characterID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0 || characterID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot and character IDs must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"DELETE FROM {TableName} WHERE plot_id = {{0}} AND character_id = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, characterID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> RevokeAllAsync(long plotID, CancellationToken cancellationToken = default)
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
		public async Task<DatabaseResult<List<PlotAccessData>>> FetchByPlotAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<List<PlotAccessData>>.Success(new List<PlotAccessData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotAccessEntity> grants = await dbContext.PlotAccess
					.AsNoTracking()
					.Where(e => e.PlotID == plotID)
					.OrderBy(e => e.CharacterID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(grants);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotAccessData>>> FetchByPlotsAsync(List<long> plotIDs, CancellationToken cancellationToken = default)
		{
			if (plotIDs == null || plotIDs.Count < 1)
			{
				return DatabaseResult<List<PlotAccessData>>.Success(new List<PlotAccessData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				long[] ids = plotIDs.Where(id => id > 0).Distinct().ToArray();
				if (ids.Length < 1)
				{
					return new List<PlotAccessData>();
				}

				List<PlotAccessEntity> grants = await dbContext.PlotAccess
					.AsNoTracking()
					.Where(e => ids.Contains(e.PlotID))
					.OrderBy(e => e.PlotID)
					.ThenBy(e => e.CharacterID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(grants);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotAccessData>>> FetchByCharacterAsync(long characterID, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<List<PlotAccessData>>.Success(new List<PlotAccessData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotAccessEntity> grants = await dbContext.PlotAccess
					.AsNoTracking()
					.Where(e => e.CharacterID == characterID)
					.OrderBy(e => e.PlotID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(grants);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps access entities to their data transfer objects.
		/// </summary>
		private static List<PlotAccessData> MapMany(List<PlotAccessEntity> grants)
		{
			List<PlotAccessData> results = new List<PlotAccessData>(grants.Count);
			foreach (PlotAccessEntity grant in grants)
			{
				results.Add(new PlotAccessData(grant.ID, grant.PlotID, grant.CharacterID, grant.Permissions, grant.GrantedByCharacterID, grant.TimeGranted));
			}
			return results;
		}
	}
}
