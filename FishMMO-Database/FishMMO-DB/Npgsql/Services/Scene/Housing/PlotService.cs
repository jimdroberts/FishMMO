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
		public async Task<DatabaseResult<int>> RegisterAsync(long worldServerID, string sceneName, IReadOnlyList<string> plotKeys, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}
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
				string sql = $@"INSERT INTO {TableName} (world_server_id, scene_name, plot_key, owner_character_id, owner_guild_id, state, version, time_created)
					SELECT {{0}}, {{1}}, u.plot_key, 0, 0, 0, 1, {{2}}
					FROM UNNEST({{3}}::text[]) AS u(plot_key)
					ON CONFLICT (world_server_id, scene_name, plot_key) DO NOTHING";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerID, sceneName, now, keys },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchBySceneAsync(long worldServerID, string sceneName, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0 || string.IsNullOrWhiteSpace(sceneName))
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.WorldServerID == worldServerID && e.SceneName == sceneName)
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
		public async Task<DatabaseResult<int>> TryClaimAsync(long plotID, long ownerCharacterID, long ownerGuildID, DateTime? taxDueUtc, int claimedState = 1, CancellationToken cancellationToken = default)
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
				/* The NOT EXISTS clause is the design's "one house per player", asked in the same
				 * statement that takes the plot so a claim cannot be judged against a house the
				 * character acquired a moment later.
				 *
				 * It is not the whole enforcement. Two claims on two scene servers can both read no
				 * existing house and both pass this; the partial unique index on owner_character_id
				 * is what turns the second into an error rather than a second house. This clause is
				 * here so the ordinary case — a player who simply already owns somewhere — comes
				 * back as zero rows, which the caller already knows how to report, rather than as a
				 * constraint violation it would have to translate. */
				string sql = $@"UPDATE {TableName}
					SET owner_character_id = {{1}}, owner_guild_id = {{2}}, state = {{5}}, time_claimed = {{3}}, tax_due_utc = {{4}}, tax_delinquent_since_utc = NULL, version = version + 1
					WHERE id = {{0}} AND owner_character_id = 0 AND owner_guild_id = 0
					  AND ({{1}} = 0 OR NOT EXISTS (
							SELECT 1 FROM {TableName} held WHERE held.owner_character_id = {{1}}
					  ))";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, ownerCharacterID, ownerGuildID, now, (object)taxDueUtc ?? DBNull.Value, claimedState },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReleaseAsync(long plotID, long expectedOwnerCharacterID, long expectedOwnerGuildID, int releasedState = 0, CancellationToken cancellationToken = default)
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
					SET owner_character_id = 0, owner_guild_id = 0, state = {{3}}, time_claimed = NULL, tax_due_utc = NULL, tax_delinquent_since_utc = NULL, version = version + 1
					WHERE id = {{0}} AND owner_character_id = {{1}} AND owner_guild_id = {{2}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedOwnerCharacterID, expectedOwnerGuildID, releasedState },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TrySetStateAsync(long plotID, int expectedState, int newState, long expectedOwnerCharacterID, long expectedOwnerGuildID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (expectedState == newState)
			{
				/* A transition to the state the plot is already in would report success having done
				 * nothing, and a caller waiting on the row count to tell it whether it won a race
				 * would read that as having lost one. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The new state must differ from the expected one.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to both the state being left and the owner leaving it, so a transition
				 * computed from a stale read cannot land.
				 *
				 * The owner matters as much as the state here. "Finish building" arriving a moment
				 * after the plot was reclaimed for unpaid tax would otherwise mark somebody else's
				 * land — or freshly abandoned land — as occupied, and the state would then describe
				 * a house that is not there. */
				string sql = $@"UPDATE {TableName}
					SET state = {{2}}, version = version + 1
					WHERE id = {{0}} AND state = {{1}} AND owner_character_id = {{3}} AND owner_guild_id = {{4}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedState, newState, expectedOwnerCharacterID, expectedOwnerGuildID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> BackfillTaxDueAsync(long worldServerID, DateTime firstDueUtc, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "World server ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* IS NULL is what makes this idempotent, and it runs on every scene resolve so that
				 * matters. A plot already being billed keeps the date it has; only land claimed
				 * while tax was off is given one. */
				string sql = $@"UPDATE {TableName}
					SET tax_due_utc = {{1}}, version = version + 1
					WHERE world_server_id = {{0}}
					  AND owner_character_id <> 0
					  AND tax_due_utc IS NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { worldServerID, firstDueUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotData>>> FetchTaxDueAsync(long worldServerID, DateTime asOfUtc, int limit, CancellationToken cancellationToken = default)
		{
			if (worldServerID <= 0 || limit < 1)
			{
				return DatabaseResult<List<PlotData>>.Success(new List<PlotData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotEntity> plots = await dbContext.Plots
					.AsNoTracking()
					.Where(e => e.WorldServerID == worldServerID &&
								e.TaxDueUtc != null &&
								e.TaxDueUtc <= asOfUtc)
					.OrderBy(e => e.TaxDueUtc)
					.Take(limit)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(plots);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryAdvanceTaxAsync(long plotID, DateTime expectedDueUtc, DateTime nextDueUtc, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (nextDueUtc <= expectedDueUtc)
			{
				/* A next date that is not later would leave the plot permanently due, charging the
				 * owner on every sweep forever. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "The next tax date must be later than the one being replaced.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the date the caller read. Several scene servers may host this world and
				 * all see the plot come due at once; only the one whose expected date still matches
				 * wins, so the period produces one charge rather than one per server. */
				string sql = $@"UPDATE {TableName}
					SET tax_due_utc = {{2}}, version = version + 1
					WHERE id = {{0}} AND tax_due_utc = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, expectedDueUtc, nextDueUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> MarkTaxDelinquentAsync(long plotID, DateTime delinquentSinceUtc, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* IS NULL in the WHERE clause is what keeps the grace clock running from the first
				 * miss. Without it every later failure would reset the date and an owner who never
				 * pays would never run out of grace. */
				string sql = $@"UPDATE {TableName}
					SET tax_delinquent_since_utc = {{1}}, version = version + 1
					WHERE id = {{0}} AND tax_delinquent_since_utc IS NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, delinquentSinceUtc },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ClearTaxDelinquencyAsync(long plotID, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"UPDATE {TableName}
					SET tax_delinquent_since_utc = NULL, version = version + 1
					WHERE id = {{0}} AND tax_delinquent_since_utc IS NOT NULL";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID },
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
					SET owner_guild_id = 0, state = 0, time_claimed = NULL, tax_due_utc = NULL, tax_delinquent_since_utc = NULL, version = version + 1
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
				results.Add(new PlotData(plot.ID, plot.WorldServerID, plot.SceneName, plot.PlotKey, plot.OwnerCharacterID, plot.OwnerGuildID, plot.TimeClaimed, plot.TaxDueUtc, plot.TaxDelinquentSinceUtc, plot.State));
			}
			return results;
		}
	}
}
