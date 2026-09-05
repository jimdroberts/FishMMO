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
	/// Holds what was standing on a plot when its owner lost it.
	/// </summary>
	public sealed class PlotVaultService : BaseService<PlotVaultEntity>, IPlotVaultService
	{
		/// <summary>
		/// The structures table, named literally because this service empties it into its own.
		/// </summary>
		/// <remarks>
		/// <see cref="BaseService{T}.TableName"/> only knows the entity this service is for, and the
		/// move is one statement spanning both tables — so the other end has to be written out. Kept
		/// as a constant rather than inlined so the coupling is visible from the top of the file
		/// rather than buried in a SQL string.
		/// </remarks>
		private const string StructuresTableName = "plot_structures";

		public PlotVaultService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> StorePlotContentsAsync(long plotID, long characterID, long baseFeePerEntry, float feeRatePerDay, CancellationToken cancellationToken = default)
		{
			if (plotID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Plot ID must be greater than zero.");
			}
			if (characterID <= 0)
			{
				/* Guild-owned land has no character to give the furniture back to, and there is no
				 * guild vault yet. Refused rather than quietly dropped, so the caller decides what
				 * to do instead of discovering later that a guild hall was demolished outright. */
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "A vault belongs to a character; the owning character must be greater than zero.");
			}

			DateTime now = DateTime.UtcNow;
			long baseFee = Math.Max(0L, baseFeePerEntry);
			float rate = feeRatePerDay < 0f || float.IsNaN(feeRatePerDay) ? 0f : feeRatePerDay;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement, because storing and clearing must not be able to happen separately.
				 *
				 * The DELETE runs inside a data-modifying CTE and hands its rows to the INSERT, so
				 * either the structures are gone and the vault rows exist, or neither happened.
				 * Split across two statements this has two half-states and both are bad: stop after
				 * the store and the owner has their furniture in the vault while it is also still
				 * standing on land somebody else is about to buy; stop before it and the house is
				 * destroyed with nothing to show for it. Neither is fixable by retrying, because a
				 * retry cannot tell which half already ran.
				 *
				 * Grouped by template on the way through, so forty identical fence panels become one
				 * row of forty rather than forty rows each carrying their own retrieval fee. */
				string sql = $@"WITH moved AS (
						DELETE FROM {StructuresTableName}
						WHERE plot_id = {{0}}
						RETURNING template_id
					), grouped AS (
						SELECT template_id, COUNT(*)::int AS amount
						FROM moved
						GROUP BY template_id
					)
					INSERT INTO {TableName} (character_id, template_id, amount, original_plot_id, stored_at_utc, base_fee, fee_rate_per_day, version)
					SELECT {{1}}, g.template_id, g.amount, {{0}}, {{2}}, {{3}}, {{4}}, 1
					FROM grouped g";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { plotID, characterID, now, baseFee, rate },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<PlotVaultData>>> FetchByCharacterAsync(long characterID, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<List<PlotVaultData>>.Success(new List<PlotVaultData>());
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				List<PlotVaultEntity> entries = await dbContext.PlotVault
					.AsNoTracking()
					.Where(e => e.CharacterID == characterID)
					.OrderBy(e => e.StoredAtUtc)
					.ThenBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return MapMany(entries);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<PlotVaultData?>> FetchEntryAsync(long vaultID, long characterID, CancellationToken cancellationToken = default)
		{
			if (vaultID <= 0 || characterID <= 0)
			{
				return DatabaseResult<PlotVaultData?>.Success(null);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* The owner is a predicate rather than a check made on the result. A request naming
				 * somebody else's row then finds nothing, instead of finding something that a later
				 * reader has to remember to reject. */
				PlotVaultEntity entry = await dbContext.PlotVault
					.AsNoTracking()
					.FirstOrDefaultAsync(e => e.ID == vaultID && e.CharacterID == characterID, cancellationToken)
					.ConfigureAwait(false);

				return entry == null ? (PlotVaultData?)null : Map(entry);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> TryRemoveEntryAsync(long vaultID, long characterID, CancellationToken cancellationToken = default)
		{
			if (vaultID <= 0 || characterID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Vault and character IDs must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Pinned to the owner, and the row count is the answer. Two requests for the same
				 * entry — a double click, or a retry after a slow response — produce one delete that
				 * returns 1 and one that returns 0, so the caller charging on a 1 charges exactly
				 * once for exactly one handover. */
				string sql = $@"DELETE FROM {TableName} WHERE id = {{0}} AND character_id = {{1}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { vaultID, characterID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ForfeitAllAsync(long characterID, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"DELETE FROM {TableName} WHERE character_id = {{0}}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterID },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps a vault entity to its data transfer object.
		/// </summary>
		private static PlotVaultData Map(PlotVaultEntity entry)
		{
			return new PlotVaultData(entry.ID, entry.CharacterID, entry.TemplateID, entry.Amount, entry.OriginalPlotID, entry.StoredAtUtc, entry.BaseFee, entry.FeeRatePerDay);
		}

		/// <summary>
		/// Maps vault entities to their data transfer objects.
		/// </summary>
		private static List<PlotVaultData> MapMany(List<PlotVaultEntity> entries)
		{
			List<PlotVaultData> results = new List<PlotVaultData>(entries.Count);
			foreach (PlotVaultEntity entry in entries)
			{
				results.Add(Map(entry));
			}
			return results;
		}
	}
}
