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
		public async Task<DatabaseResult> InsertAsync(GuildRankData rank, int maxRanks, byte maxRankOrder, CancellationToken cancellationToken = default)
		{
			if (rank.GuildID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			if (rank.RankOrder < 1 || rank.RankOrder > maxRankOrder)
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

			var result = await ExecuteTransactionAsync<int>(async dbContext =>
			{
				/* FOR UPDATE, and the whole ladder rather than the rows being moved. Two scene
				 * servers can be inserting into the same guild at the same moment, and each one's
				 * decisions — is there capacity, is there headroom, which rows move — are read
				 * from this list. Locking the ladder is what makes the second insert wait and then
				 * re-read, instead of computing a shift against a ladder that has already moved. */
				var existing = await dbContext.GuildRanks
					.FromSqlRaw($"SELECT * FROM {TableName} WHERE guild_id = {{0}} ORDER BY rank_order DESC FOR UPDATE", rank.GuildID)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				/* Re-sorted in memory rather than trusted from the query. The ORDER BY above is
				 * there for the lock's sake; EF composes over a raw query freely and the loop
				 * below is only correct while the list really is highest-first. */
				existing.Sort((a, b) => b.RankOrder.CompareTo(a.RankOrder));

				if (existing.Count >= maxRanks)
				{
					return ResultCapacity;
				}

				/* Headroom. Every rank at or above the insertion point moves up one, so the guild
				 * needs one unused order above its highest. Without this the shift would run the
				 * top rank past the legal range and leave a leader on an order the rest of the
				 * code refuses to accept. */
				if (existing.Count > 0 && existing[0].RankOrder >= maxRankOrder)
				{
					return ResultNoHeadroom;
				}

				/* Descending, one statement per row. See IGuildRankService.InsertAsync: the
				 * unique index on (guild_id, rank_order) is checked per row, so a single ranged
				 * UPDATE collides with whichever occupied target the planner reaches first. */
				for (int i = 0; i < existing.Count; ++i)
				{
					byte order = existing[i].RankOrder;
					if (order < rank.RankOrder)
					{
						// Ordered descending, so everything from here down stays where it is.
						break;
					}

					await dbContext.Database.ExecuteSqlRawAsync(
						$@"UPDATE {TableName}
							SET rank_order = rank_order + 1, version = version + 1
							WHERE guild_id = {{0}} AND rank_order = {{1}}",
						new object[] { rank.GuildID, (short)order },
						cancellationToken).ConfigureAwait(false);
				}

				/* The membership rows move with the ladder. One statement, because nothing is
				 * unique about character_guild.rank — the collision that forces the loop above
				 * cannot happen here.
				 *
				 * The VERSION moves too. Every membership write is guarded by "version < the
				 * one I read plus one", so a rank change decided on another scene server against
				 * the pre-shift ladder — "promote to 2", meaning the officer rank, computed a
				 * moment before 2 became a new empty tier — arrives with a version this bump has
				 * already overtaken, and is refused as stale instead of landing the member on a
				 * rank nobody chose. */
				await dbContext.Database.ExecuteSqlRawAsync(
					@"UPDATE character_guild
						SET rank = rank + 1, version = version + 1
						WHERE guild_id = {0} AND rank >= {1}",
					new object[] { rank.GuildID, (short)rank.RankOrder },
					cancellationToken).ConfigureAwait(false);

				int inserted = await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {TableName} (guild_id, version, rank_order, name, permissions, time_created)
						SELECT {{0}}, 1, {{1}}, {{2}}, {{3}}, {{4}}
						WHERE EXISTS (SELECT 1 FROM guilds WHERE id = {{0}})",
					new object[] { rank.GuildID, (short)rank.RankOrder, rank.Name, rank.Permissions, DateTime.UtcNow },
					cancellationToken).ConfigureAwait(false);

				/* Zero rows means the guild itself is gone — disbanded while this request was in
				 * flight. Reported rather than ignored, because the shift above has to roll back
				 * with it, which is what returning through the transaction wrapper does. */
				return inserted > 0 ? ResultInserted : ResultGuildMissing;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? result.Data switch
				{
					ResultInserted => DatabaseResult.Success(),
					ResultCapacity => DatabaseResult.Failure(DatabaseErrorCodes.CapacityExceeded, $"Guild already has the maximum of {maxRanks} ranks."),
					ResultNoHeadroom => DatabaseResult.Failure(DatabaseErrorCodes.CapacityExceeded, $"Guild has no rank order free below {maxRankOrder}."),
					_ => DatabaseResult.Failure(DatabaseErrorCodes.NotFound, "Guild not found."),
				}
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary><see cref="InsertAsync"/> outcome: the rank was created.</summary>
		private const int ResultInserted = 0;
		/// <summary><see cref="InsertAsync"/> outcome: the guild already holds its maximum ranks.</summary>
		private const int ResultCapacity = 1;
		/// <summary><see cref="InsertAsync"/> outcome: the ladder cannot be shifted up any further.</summary>
		private const int ResultNoHeadroom = 2;
		/// <summary><see cref="InsertAsync"/> outcome: the guild no longer exists.</summary>
		private const int ResultGuildMissing = 3;

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
