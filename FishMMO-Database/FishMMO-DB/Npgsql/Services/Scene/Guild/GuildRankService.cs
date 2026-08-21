using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to
	/// <see cref="DatabaseResult"/> error codes. Transient failures are retried automatically.</para>
	/// </remarks>
	public sealed class GuildRankService : BaseService<GuildRankEntity>, IGuildRankService
	{
		/// <summary>
		/// Initializes a new instance of GuildRankService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		public GuildRankService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> EnsureDefaultsAsync(long guildId, IReadOnlyList<GuildRankData> defaults, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			if (defaults == null || defaults.Count == 0)
			{
				return DatabaseResult<int>.Success(0);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				int inserted = 0;

				/* One statement per seeded rank rather than a multi-row VALUES list. The ladder is
				 * three rows on the only path that calls this, and building a parameterised
				 * multi-row insert by string concatenation is how a parameterised query stops
				 * being one. */
				var sql = $@"
					INSERT INTO {TableName} (guild_id, version, rank_order, name, permissions, time_created)
					SELECT {{0}}, 1, {{1}}, {{2}}, {{3}}, {{4}}
					WHERE EXISTS (SELECT 1 FROM guilds WHERE id = {{0}})
					ON CONFLICT (guild_id, rank_order) DO NOTHING";

				DateTime now = DateTime.UtcNow;

				for (int i = 0; i < defaults.Count; ++i)
				{
					GuildRankData rank = defaults[i];
					inserted += await dbContext.Database.ExecuteSqlRawAsync(
						sql,
						new object[] { guildId, (short)rank.RankOrder, rank.Name ?? string.Empty, rank.Permissions, now },
						cancellationToken).ConfigureAwait(false);
				}

				return inserted;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GuildRankData>>> FetchManyAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<IReadOnlyList<GuildRankData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid guild ID.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.GuildRanks
					.AsNoTracking()
					.Where(r => r.GuildID == guildId)
					.OrderBy(r => r.RankOrder)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<GuildRankData> ranks = rows.Select(r => new GuildRankData(
					r.ID,
					r.Version,
					r.GuildID,
					r.RankOrder,
					r.Name ?? string.Empty,
					r.Permissions)).ToList();

				return (IReadOnlyList<GuildRankData>)ranks;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateAsync(long guildId, byte rankOrder, string name, long permissions, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Rank name must not be empty.");
			}

			if (name.Length > 24)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Rank name must not exceed 24 characters.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET name = {{0}}, permissions = {{1}}, version = {{2}}
					WHERE guild_id = {{3}} AND rank_order = {{4}} AND version < {{2}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { name, permissions, incomingVersion, guildId, (short)rankOrder }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.GuildRanks
						.AsNoTracking()
						.FirstOrDefaultAsync(r => r.GuildID == guildId && r.RankOrder == rankOrder, cancellationToken)
						.ConfigureAwait(false);

					if (existing == null)
					{
						throw new DatabaseEntityNotFoundException("GuildRank", $"{guildId}:{rankOrder}");
					}

					if (existing.Version == incomingVersion)
					{
						throw new DuplicateReplayException();
					}

					throw new StaleStateException("Guild rank update rejected due to a stale Version.");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> CreateAsync(GuildRankData rank, int maxRanks, CancellationToken cancellationToken = default)
		{
			if (rank.GuildID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			if (rank.RankOrder < 1)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid rank order.");
			}

			if (string.IsNullOrWhiteSpace(rank.Name) || rank.Name.Length > 24)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Rank name must be 1 to 24 characters.");
			}

			if (maxRanks <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid maximum rank count.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* The cap is enforced inside the INSERT, in the same statement that reads the
				 * current count. Counting first and inserting second lets two concurrent creates
				 * both see count = max - 1. */
				var sql = $@"
					WITH capacity_ok AS (
						SELECT 1 WHERE (SELECT COUNT(*) FROM {TableName} WHERE guild_id = {{0}}) < {{1}}
					),
					inserted AS (
						INSERT INTO {TableName} (guild_id, version, rank_order, name, permissions, time_created)
						SELECT {{0}}, 1, {{2}}, {{3}}, {{4}}, {{5}}
						WHERE EXISTS (SELECT 1 FROM capacity_ok)
						ON CONFLICT (guild_id, rank_order) DO NOTHING
						RETURNING 1
					)
					SELECT CASE
						WHEN NOT EXISTS (SELECT 1 FROM capacity_ok) THEN 1
						WHEN EXISTS (SELECT 1 FROM inserted) THEN 0
						ELSE 2
					END AS value";

				return await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { rank.GuildID, maxRanks, (short)rank.RankOrder, rank.Name, rank.Permissions, DateTime.UtcNow },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? result.Data switch
				{
					0 => DatabaseResult.Success(),
					1 => DatabaseResult.Failure(DatabaseErrorCodes.CapacityExceeded, $"Guild already has the maximum of {maxRanks} ranks."),
					_ => DatabaseResult.Failure(DatabaseErrorCodes.UniqueViolation, "A rank already occupies that position."),
				}
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long guildId, byte rankOrder, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"
					WITH occupied AS (
						SELECT 1 FROM character_guild WHERE guild_id = {{0}} AND rank = {{1}} LIMIT 1
					),
					deleted AS (
						DELETE FROM {TableName}
						WHERE guild_id = {{0}} AND rank_order = {{1}}
						  AND NOT EXISTS (SELECT 1 FROM occupied)
						RETURNING 1
					)
					SELECT CASE
						WHEN EXISTS (SELECT 1 FROM occupied) THEN 1
						WHEN EXISTS (SELECT 1 FROM deleted) THEN 0
						ELSE 2
					END AS value";

				return await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { guildId, (short)rankOrder },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? result.Data switch
				{
					0 => DatabaseResult.Success(),
					1 => DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Rank still has members."),
					_ => DatabaseResult.Failure(DatabaseErrorCodes.NotFound, "Rank not found."),
				}
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}
	}
}
