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
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to
	/// <see cref="DatabaseResult"/> error codes. Transient failures are retried automatically.</para>
	/// </remarks>
	public sealed class GuildLogService : BaseService<GuildLogEntity>, IGuildLogService
	{
		/// <summary>
		/// Initializes a new instance of GuildLogService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		public GuildLogService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> AppendAsync(GuildLogData entry, CancellationToken cancellationToken = default)
		{
			if (entry.GuildID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			string detail = entry.Detail ?? string.Empty;
			if (detail.Length > 64)
			{
				detail = detail.Substring(0, 64);
			}

			DateTime timeCreated = entry.TimeCreated == default ? DateTime.UtcNow : entry.TimeCreated;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* A plain INSERT with no conflict clause. Two identical events a millisecond apart
				 * are two real events, not a duplicate to be collapsed — this is a history, and
				 * de-duplicating it would hide exactly the repetition somebody reads it to find. */
				var sql = $@"
					INSERT INTO {TableName} (guild_id, event_type, actor_character_id, target_character_id, detail, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}})";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[]
					{
						entry.GuildID,
						(short)entry.EventType,
						entry.ActorCharacterID,
						entry.TargetCharacterID,
						detail,
						timeCreated,
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GuildLogData>>> FetchRecentAsync(long guildId, int limit, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<IReadOnlyList<GuildLogData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid guild ID.");
			}

			// Clamped rather than rejected: a caller asking for a silly page size gets a sensible
			// one, and the cap is what stops a hand-built request pulling a guild's whole history.
			int take = Math.Clamp(limit, 1, 200);

			return await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.GuildLogs
					.AsNoTracking()
					.Where(e => e.GuildID == guildId)
					.OrderByDescending(e => e.TimeCreated)
					.ThenByDescending(e => e.ID)
					.Take(take)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<GuildLogData> entries = rows.Select(e => new GuildLogData(
					e.ID,
					e.GuildID,
					(GuildLogEventType)e.EventType,
					e.ActorCharacterID,
					e.TargetCharacterID,
					e.Detail ?? string.Empty,
					e.TimeCreated)).ToList();

				return (IReadOnlyList<GuildLogData>)entries;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> PruneAsync(long guildId, int keep, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			int retain = Math.Clamp(keep, 1, 1000);

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement. Selecting the ids to keep and deleting the rest in two round
				 * trips would leave a window in which rows written between them are deleted
				 * despite being newer than everything retained. */
				var sql = $@"
					DELETE FROM {TableName}
					WHERE guild_id = {{0}}
					  AND id NOT IN (
						SELECT id FROM {TableName}
						WHERE guild_id = {{0}}
						ORDER BY time_created DESC, id DESC
						LIMIT {{1}}
					  )";

				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { guildId, retain }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
