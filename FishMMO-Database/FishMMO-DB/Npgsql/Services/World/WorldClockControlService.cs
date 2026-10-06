using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// The world clock row: reads, the scene servers' seed, and the admin writes. See
	/// <see cref="IWorldClockControlService"/> for the contract.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>All clock arithmetic is SQL against <c>clock_timestamp()</c>.</b> The world time a write
	/// replaces, the anchor it writes and the instant it stamps all come from one evaluation of the
	/// database clock inside the statement that writes them. A world time computed in this process
	/// and sent back would carry this host's skew and the round trip, and two admins writing at
	/// once would each overwrite the other's re-anchor.
	/// </para>
	/// <para>
	/// <b>Why the write is a CTE that locks the row.</b> The <c>o</c> CTE takes the row with
	/// <c>FOR UPDATE</c>; under READ COMMITTED a second writer waits there and then sees the FIRST
	/// writer's row, not the one its snapshot started with. The database clock is read in the next
	/// CTE (<c>k</c>), which projects only once a locked row has been returned, so the waiting writer
	/// measures elapsed time from after the wait, against the anchor the first writer left. Two
	/// concurrent shifts of +1h therefore move the world 2h, exactly.
	/// </para>
	/// <para>
	/// <b>Idempotency.</b> <c>ExecuteWriteAsync</c> retries a dropped connection, and a connection
	/// can drop after the commit but before the reply. Each write takes a key once, outside the
	/// retried delegate, writes it to <c>request_key</c>, and skips a row already carrying it; a
	/// write that matches nothing then looks to see whether its own key is there, and if so reports
	/// the row as written (a replay) instead of shifting the world a second time.
	/// </para>
	/// </remarks>
	public sealed class WorldClockControlService : BaseService<WorldClockControlEntity>, IWorldClockControlService
	{
		/// <summary>The only row id.</summary>
		private const int RowId = 1;

		/// <summary>The database clock as Unix milliseconds, floored, evaluated where it is written.</summary>
		private const string NowMsSql = "floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint";

		/// <summary>The latest instant a world timestamp can name, as a SQL literal.</summary>
		private static readonly string MaxCalendarMsSql = WorldClockText.MaxCalendarUnixMs.ToString(System.Globalization.CultureInfo.InvariantCulture);

		/// <summary>
		/// The largest magnitude a shift may have: the whole representable calendar range. Anything
		/// larger is refused before it can overflow a bigint in SQL.
		/// </summary>
		private const long MaxShiftMs = WorldClockText.MaxCalendarUnixMs - WorldClockText.MinEpochUnixSeconds * 1000L;

		/// <summary>The sentence every write gives when there is no row to write.</summary>
		private const string NoRowMessage =
			"The world clock has no row yet: no scene server has started against this database. " +
			"A scene server creates it when it starts; start one, then try again.";

		/// <summary>Initializes the service.</summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		public WorldClockControlService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/* ── Reads ─────────────────────────────────────────────────── */

		/// <summary>
		/// The clock and the row in one statement. LEFT JOIN from the clock, so a database with no
		/// row still answers with its time.
		/// </summary>
		private string ReadSql => $@"SELECT k.now_ms,
				c.id IS NOT NULL,
				c.base_world_ms,
				c.base_reference_ms,
				c.rate,
				c.resume_rate,
				c.epoch_unix_seconds,
				c.revision,
				c.updated_by,
				c.updated_at,
				(c.base_world_ms + floor(c.rate * GREATEST(0, k.now_ms - c.base_reference_ms)))::bigint
			FROM (SELECT {NowMsSql} AS now_ms) AS k
			LEFT JOIN {TableName} AS c ON c.id = {RowId}";

		private static WorldClockReading MapRead(DbDataReader reader)
		{
			long now = reader.GetInt64(0);
			if (!reader.GetBoolean(1))
			{
				return WorldClockReading.Missing(now);
			}
			return new WorldClockReading(
				exists: true,
				dbNowMs: now,
				baseWorldMs: reader.GetInt64(2),
				baseReferenceMs: reader.GetInt64(3),
				rate: reader.GetDouble(4),
				resumeRate: reader.GetDouble(5),
				epochUnixSeconds: reader.GetInt64(6),
				revision: reader.GetInt64(7),
				updatedBy: reader.IsDBNull(8) ? null : reader.GetString(8),
				updatedAt: reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
				worldMsNow: reader.GetInt64(10));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldClockReading>> ReadAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(dbContext =>
				ExecuteReturningAsync(dbContext, ReadSql, Array.Empty<object>(), MapRead, cancellationToken),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<WorldClockReading>> EnsureSeededAsync(long epochUnixSeconds, CancellationToken cancellationToken = default)
		{
			if (!WorldClockText.IsValidEpoch(epochUnixSeconds))
			{
				return DatabaseResult<WorldClockReading>.Failure(DatabaseErrorCodes.ValidationError,
					"The calendar epoch must fall between the years 1 and 9999.");
			}

			/* Idempotent by construction (ON CONFLICT DO NOTHING), so a retry after a lost reply is
			 * harmless and needs no key. The insert and the read are two statements: a CTE insert
			 * cannot show its own row to the outer SELECT, and the read must report the row that
			 * WON when two scene servers seed at once, which is not necessarily ours. */
			return await ExecuteWriteAsync(async dbContext =>
			{
				string insert = $@"INSERT INTO {TableName}
						(id, base_world_ms, base_reference_ms, rate, resume_rate, epoch_unix_seconds, revision, updated_by, updated_at)
					SELECT {RowId}, GREATEST(0, k.now_ms - {{0}}::bigint * 1000), k.now_ms, 1, 1, {{0}}::bigint, 1, NULL, NULL
					FROM (SELECT {NowMsSql} AS now_ms) AS k
					ON CONFLICT (id) DO NOTHING";

				await dbContext.Database.ExecuteSqlRawAsync(insert, new object[] { epochUnixSeconds }, cancellationToken)
					.ConfigureAwait(false);

				return await ExecuteReturningAsync(dbContext, ReadSql, Array.Empty<object>(), MapRead, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/* ── Writes ────────────────────────────────────────────────── */

		/// <inheritdoc/>
		public Task<DatabaseResult<WorldClockReading>> SetAsync(long worldMs, string actor, CancellationToken cancellationToken = default)
		{
			if (worldMs < 0)
			{
				return Refuse("World time cannot be before the calendar epoch (it would be negative).");
			}
			if (worldMs > MaxShiftMs)
			{
				return Refuse("That world time is past the last date the calendar can name (9999-12-31).");
			}

			/* Keeps the pace: the anchor moves to (worldMs, now) and the rate is untouched. The guard
			 * needs the row's epoch, so it lives in the statement. */
			return WriteAsync(
				actor,
				newBaseSql: "{2}::bigint",
				newRateSql: "n.rate",
				newResumeRateSql: "n.resume_rate",
				guardSql: $"n.epoch_unix_seconds * 1000 + {{2}}::bigint <= {MaxCalendarMsSql}",
				extra: new object[] { worldMs },
				guardFailure: _ => "That world time is past the last date the calendar can name (9999-12-31).",
				cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<WorldClockReading>> ShiftAsync(long deltaMs, string actor, CancellationToken cancellationToken = default)
		{
			if (deltaMs > MaxShiftMs || deltaMs < -MaxShiftMs)
			{
				return Refuse("That change is larger than the whole calendar.");
			}

			return WriteAsync(
				actor,
				newBaseSql: "n.world_now + {2}::bigint",
				newRateSql: "n.rate",
				newResumeRateSql: "n.resume_rate",
				guardSql: $"n.world_now + {{2}}::bigint >= 0 AND n.epoch_unix_seconds * 1000 + n.world_now + {{2}}::bigint <= {MaxCalendarMsSql}",
				extra: new object[] { deltaMs },
				guardFailure: current => current.Exists && current.WorldMsNow + deltaMs < 0
					? "That change would put world time before the calendar epoch; world time cannot be negative."
					: "That change would put world time past the last date the calendar can name (9999-12-31).",
				cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<WorldClockReading>> SetRateAsync(double rate, string actor, CancellationToken cancellationToken = default)
		{
			/* Refused rather than clamped. A negative pace is a typo, and quietly turning "-60" into a
			 * hold would acknowledge something the admin did not ask for. */
			if (double.IsNaN(rate) || double.IsInfinity(rate))
			{
				return Refuse("The pace must be a number.");
			}
			if (rate < 0)
			{
				return Refuse("The pace cannot be negative: world time does not run backwards. Use 0 to hold it.");
			}
			if (rate > WorldClockLimits.MaxRate)
			{
				return Refuse($"The pace cannot exceed {WorldClockLimits.MaxRate:0}x.");
			}

			/* Re-anchor at world-now, then run at the new pace. A non-zero pace becomes the resume
			 * pace; a zero pace is a hold and keeps the running pace to resume to, as HoldAsync does. */
			return WriteAsync(
				actor,
				newBaseSql: "n.world_now",
				newRateSql: "{2}::double precision",
				newResumeRateSql: "CASE WHEN {2}::double precision > 0 THEN {2}::double precision WHEN n.rate > 0 THEN n.rate ELSE n.resume_rate END",
				guardSql: null,
				extra: new object[] { rate },
				guardFailure: null,
				cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<WorldClockReading>> HoldAsync(string actor, CancellationToken cancellationToken = default)
		{
			return WriteAsync(
				actor,
				newBaseSql: "n.world_now",
				newRateSql: "0",
				newResumeRateSql: "CASE WHEN n.rate > 0 THEN n.rate ELSE n.resume_rate END",
				guardSql: null,
				extra: Array.Empty<object>(),
				guardFailure: null,
				cancellationToken);
		}

		/// <inheritdoc/>
		public Task<DatabaseResult<WorldClockReading>> ResumeAsync(string actor, CancellationToken cancellationToken = default)
		{
			// While held, world_now IS base_world_ms (rate 0), so the clock resumes from where it stopped.
			return WriteAsync(
				actor,
				newBaseSql: "n.world_now",
				newRateSql: "n.resume_rate",
				newResumeRateSql: "n.resume_rate",
				guardSql: null,
				extra: Array.Empty<object>(),
				guardFailure: null,
				cancellationToken);
		}

		/// <summary>
		/// The one write statement, specialised by its SET expressions and an optional guard.
		/// </summary>
		/// <param name="actor">The account writing; bound as <c>{0}</c>.</param>
		/// <param name="newBaseSql">New base_world_ms, over <c>n</c> (the locked row plus now_ms and world_now).</param>
		/// <param name="newRateSql">New rate.</param>
		/// <param name="newResumeRateSql">New resume_rate.</param>
		/// <param name="guardSql">An extra WHERE condition that refuses the write, or null.</param>
		/// <param name="extra">Parameters bound from <c>{2}</c> on (<c>{1}</c> is the idempotency key).</param>
		/// <param name="guardFailure">The refusal when the guard rejected the write, given the current reading.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		private async Task<DatabaseResult<WorldClockReading>> WriteAsync(
			string actor,
			string newBaseSql,
			string newRateSql,
			string newResumeRateSql,
			string? guardSql,
			object[] extra,
			Func<WorldClockReading, string>? guardFailure,
			CancellationToken cancellationToken)
		{
			string? who = actor?.Trim();
			if (string.IsNullOrEmpty(who))
			{
				return DatabaseResult<WorldClockReading>.Failure(DatabaseErrorCodes.ValidationError,
					"A world clock write must name the account making it.");
			}
			if (who!.Length > WorldClockLimits.MaxActorLength)
			{
				who = who.Substring(0, WorldClockLimits.MaxActorLength);
			}

			// Taken ONCE, outside the retried delegate. See the remarks on this class.
			Guid requestKey = Guid.NewGuid();

			var parameters = new object[2 + extra.Length];
			parameters[0] = who;
			parameters[1] = requestKey;
			Array.Copy(extra, 0, parameters, 2, extra.Length);

			string sql = $@"WITH o AS (
					SELECT * FROM {TableName} WHERE id = {RowId} FOR UPDATE
				), k AS (
					SELECT o.*, {NowMsSql} AS now_ms FROM o
				), n AS (
					SELECT k.*, (k.base_world_ms + floor(k.rate * GREATEST(0, k.now_ms - k.base_reference_ms)))::bigint AS world_now FROM k
				)
				UPDATE {TableName} AS c SET
					base_world_ms = {newBaseSql},
					base_reference_ms = n.now_ms,
					rate = {newRateSql},
					resume_rate = {newResumeRateSql},
					revision = n.revision + 1,
					updated_by = {{0}}::text,
					updated_at = (to_timestamp(n.now_ms / 1000.0) AT TIME ZONE 'UTC'),
					request_key = {{1}}::uuid
				FROM n
				WHERE c.id = n.id
				  AND n.request_key IS DISTINCT FROM {{1}}::uuid
				  {(guardSql == null ? string.Empty : "AND " + guardSql)}
				RETURNING n.now_ms, c.base_world_ms, c.base_reference_ms, c.rate, c.resume_rate,
					c.epoch_unix_seconds, c.revision, c.updated_by, c.updated_at,
					n.world_now, n.rate";

			return await ExecuteWriteAsync(async dbContext =>
			{
				WorldClockReading? written = await ExecuteReturningOrDefaultAsync(dbContext, sql, parameters, reader =>
					new WorldClockReading(
						exists: true,
						dbNowMs: reader.GetInt64(0),
						baseWorldMs: reader.GetInt64(1),
						baseReferenceMs: reader.GetInt64(2),
						rate: reader.GetDouble(3),
						resumeRate: reader.GetDouble(4),
						epochUnixSeconds: reader.GetInt64(5),
						revision: reader.GetInt64(6),
						updatedBy: reader.IsDBNull(7) ? null : reader.GetString(7),
						updatedAt: reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8),
						// Anchored at this very instant, so world-now IS the new base.
						worldMsNow: reader.GetInt64(1),
						replacedWorldMs: reader.GetInt64(9),
						replacedRate: reader.GetDouble(10)),
					cancellationToken).ConfigureAwait(false);

				if (written != null)
				{
					return written;
				}

				/* Nothing was written. Three reasons, told apart here: no row at all, our own key
				 * already on the row (a replay of a write that committed), or the guard refused. */
				bool? ours = await ExecuteReturningOrDefaultAsync(dbContext,
					$"SELECT (request_key IS NOT DISTINCT FROM {{0}}::uuid) FROM {TableName} WHERE id = {RowId}",
					new object[] { requestKey },
					reader => (bool?)reader.GetBoolean(0),
					cancellationToken).ConfigureAwait(false);

				if (!ours.HasValue)
				{
					throw new DatabaseEntityNotFoundException("WorldClockControl", RowId.ToString(), NoRowMessage);
				}

				WorldClockReading current = await ExecuteReturningAsync(dbContext, ReadSql, Array.Empty<object>(), MapRead, cancellationToken)
					.ConfigureAwait(false);

				if (ours.Value)
				{
					// Written exactly once, by this call's first attempt; what it replaced is gone.
					return current;
				}

				string refusal = guardFailure?.Invoke(current) ?? "The world clock write was refused.";
				throw new DatabaseException(
					safeMessage: refusal,
					detailedMessage: refusal,
					errorCode: DatabaseErrorCodes.ValidationError);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static Task<DatabaseResult<WorldClockReading>> Refuse(string message) =>
			Task.FromResult(DatabaseResult<WorldClockReading>.Failure(DatabaseErrorCodes.ValidationError, message));
	}
}
