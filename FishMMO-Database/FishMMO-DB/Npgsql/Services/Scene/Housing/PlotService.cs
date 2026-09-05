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
	/// Reads and writes ownership of authored plots of land.
	/// </summary>
	public sealed class PlotService : BaseService<PlotEntity>, IPlotService
	{
		public PlotService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> RegisterAsync(string sceneName, IReadOnlyList<string> plotKeys, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Scene name must not be empty.");
			}
			if (plotKeys == null || plotKeys.Count < 1)
			{
				return DatabaseResult<int>.Success(0);
			}

			string[] keys = plotKeys
				.Where(key => !string.IsNullOrWhiteSpace(key))
				.Distinct(StringComparer.Ordinal)
				.ToArray();

			if (keys.Length < 1)
			{
				return DatabaseResult<int>.Success(0);
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, one row per authored key, and DO NOTHING for the ones already
				 * registered.
				 *
				 * Every scene server hosting a channel of this scene runs this on load, describing
				 * the same land, so conflicts are the normal case rather than an error. The
				 * conflict has to be a no-op rather than an update: an update would write over the
				 * ownership of a plot somebody already lives on, every time a server restarts. */
				string sql = $@"INSERT INTO {TableName} (scene_name, plot_key, owner_character_id, owner_guild_id, version, time_created)
					SELECT {{0}}, u.plot_key, 0, 0, 1, {{1}}
					FROM UNNEST({{2}}::text[]) AS u(plot_key)
					ON CONFLICT (scene_name, plot_key) DO NOTHING";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { sceneName, now, keys },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchBySceneAsync(string sceneName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.SceneName == sceneName)
					.OrderBy(e => e.PlotKey)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchByOwnerCharacterAsync(long characterID, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.OwnerCharacterID == characterID)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchByOwnerGuildAsync(long guildID, CancellationToken cancellationToken = default)
		{
			if (guildID <= 0)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.OwnerGuildID == guildID)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryClaimAsync(long plotID, long ownerCharacterID, long ownerGuildID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (ownerCharacterID < 0 || ownerGuildID < 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Owner identifiers must not be negative.");
			}
			if ((ownerCharacterID > 0) == (ownerGuildID > 0))
			{
				/* Both set is the contradiction PlotOwner exists to prevent. Neither set is a
				 * release wearing a claim's name, and would hand the plot back rather than over.
				 * Neither is something to store. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A claim must name exactly one of a character or a guild.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* The WHERE clause pins the plot as unowned, so two players claiming the same
				 * foundation at once produce one winner and one caller that sees zero rows.
				 *
				 * Reading ownership first and then writing would not do this: both reads would see
				 * unowned land, both writes would succeed, and the second would silently evict the
				 * first, who has already paid. Whoever gets the 1 back is the owner; everybody else
				 * has to be told no. */
				string sql = $@"UPDATE {TableName}
					SET owner_character_id = {{1}}, owner_guild_id = {{2}}, time_claimed = {{3}}, version = version + 1
					WHERE id = {{0}} AND owner_character_id = 0 AND owner_guild_id = 0";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, ownerCharacterID, ownerGuildID, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReleaseAsync(long plotID, long expectedOwnerCharacterID, long expectedOwnerGuildID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if ((expectedOwnerCharacterID > 0) == (expectedOwnerGuildID > 0))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A release must name exactly one of a character or a guild.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the expected owner, not just to the plot. A release that was in flight
				 * while the plot changed hands would otherwise evict its new owner, and the player
				 * who sent it would never know they had done it. */
				string sql = $@"UPDATE {TableName}
					SET owner_character_id = 0, owner_guild_id = 0, time_claimed = NULL, version = version + 1
					WHERE id = {{0}} AND owner_character_id = {{1}} AND owner_guild_id = {{2}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedOwnerCharacterID, expectedOwnerGuildID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReleaseAllForGuildAsync(long guildID, CancellationToken cancellationToken = default)
		{
			if (guildID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Guild ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"UPDATE {TableName}
					SET owner_guild_id = 0, time_claimed = NULL, version = version + 1
					WHERE owner_guild_id = {{0}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { guildID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps plot entities to their data transfer objects.
		/// </summary>
		private static List<PlotData> MapMany(List<PlotEntity> plots)
		{
			List<PlotData> results = new List<PlotData>(plots.Count);
			foreach (PlotEntity plot in plots)
			{
				results.Add(new PlotData(plot.ID, plot.SceneName, plot.PlotKey, plot.OwnerCharacterID, plot.OwnerGuildID, plot.TimeClaimed));
			}
			return results;
		}
	}
}
