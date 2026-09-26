using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// The bandwidth page's read, and the rollup and retention of the bandwidth tables.
	/// </summary>
	/// <remarks>
	/// Raw SQL throughout: every window is measured against the database clock inside the
	/// statement, and the aggregates are too wide for EF to translate sensibly. Table names come
	/// from the model, schema-qualified, never from input.
	/// </remarks>
	public sealed class ServerBandwidthReportService : BaseService<ServerBandwidthHourEntity>, IServerBandwidthReportService
	{
		/// <summary>
		/// The advisory lock every rollup and retention transaction takes first ("FMMOBWRU").
		/// </summary>
		/// <remarks>
		/// One key for both jobs: a retention batch folds the hour it deletes, which is a rollup,
		/// so the two must not interleave either. Nothing else in the codebase takes an advisory
		/// lock, so the key has no neighbour to collide with.
		/// </remarks>
		public const long RollupLockKey = 0x464D_4D4F_4257_5255;

		/// <summary>Expired hour rows deleted per retention batch.</summary>
		public const int HourPruneBatchRows = 5_000;

		/// <summary>The database's UTC clock, as a timestamp without a zone like every column here.</summary>
		private const string DatabaseUtcNowSql = "(now() AT TIME ZONE 'UTC')";

		public ServerBandwidthReportService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		#region Rollup and retention

		/// <summary>
		/// Recomputes the hour rows of every minute the WHERE clause admits, as a set.
		/// </summary>
		/// <remarks>
		/// <c>SET col = EXCLUDED.col</c> throughout: each hour row is a pure function of its
		/// minutes, so any number of passes over any overlap leave the same rows. The SUMs are
		/// numeric in PostgreSQL and are assigned back to bigint columns by the implicit
		/// assignment cast.
		/// </remarks>
		/// <param name="hour">The hour table.</param>
		/// <param name="minute">The minute table.</param>
		/// <param name="where">A predicate over <c>m</c>, the minute rows to fold.</param>
		private static string RollupSql(string hour, string minute, string where)
		{
			return $@"WITH rolled AS (
					INSERT INTO {hour} AS h
						(bucket_start, server_kind, server_name, interval_ms,
						 app_sent_bytes, app_recv_bytes, udp_sent_bytes, udp_recv_bytes,
						 udp_sent_datagrams, udp_recv_datagrams,
						 sessions_active_max, sessions_active_sum,
						 sessions_opened, connections_refused, handshake_failures,
						 minutes_sampled, instances)
					SELECT date_trunc('hour', m.bucket_start), m.server_kind, m.server_name, SUM(m.interval_ms),
						SUM(m.app_sent_bytes), SUM(m.app_recv_bytes), SUM(m.udp_sent_bytes), SUM(m.udp_recv_bytes),
						SUM(m.udp_sent_datagrams), SUM(m.udp_recv_datagrams),
						MAX(m.sessions_active), SUM(m.sessions_active),
						SUM(m.sessions_opened), SUM(m.connections_refused), SUM(m.handshake_failures),
						COUNT(*), COUNT(DISTINCT m.instance_id)
					FROM {minute} AS m
					WHERE {where}
					GROUP BY date_trunc('hour', m.bucket_start), m.server_kind, m.server_name
					ON CONFLICT (server_kind, server_name, bucket_start)
					DO UPDATE SET
						interval_ms = EXCLUDED.interval_ms,
						app_sent_bytes = EXCLUDED.app_sent_bytes,
						app_recv_bytes = EXCLUDED.app_recv_bytes,
						udp_sent_bytes = EXCLUDED.udp_sent_bytes,
						udp_recv_bytes = EXCLUDED.udp_recv_bytes,
						udp_sent_datagrams = EXCLUDED.udp_sent_datagrams,
						udp_recv_datagrams = EXCLUDED.udp_recv_datagrams,
						sessions_active_max = EXCLUDED.sessions_active_max,
						sessions_active_sum = EXCLUDED.sessions_active_sum,
						sessions_opened = EXCLUDED.sessions_opened,
						connections_refused = EXCLUDED.connections_refused,
						handshake_failures = EXCLUDED.handshake_failures,
						minutes_sampled = EXCLUDED.minutes_sampled,
						instances = EXCLUDED.instances
					RETURNING 1
				)
				SELECT COUNT(*)::integer FROM rolled";
		}

		/// <summary>
		/// Takes the rollup lock for the current transaction, or reports that another panel has it.
		/// Released by the transaction's end, whatever that end is.
		/// </summary>
		private static async Task<bool> TryLockAsync(NpgsqlDbContext dbContext, CancellationToken cancellationToken)
		{
			int got = await ExecuteScalarIntAsync(
				dbContext,
				"SELECT CASE WHEN pg_try_advisory_xact_lock({0}) THEN 1 ELSE 0 END",
				new object[] { RollupLockKey },
				cancellationToken).ConfigureAwait(false);
			return got == 1;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerBandwidthRollupResult>> RollupAsync(bool catchUp, CancellationToken cancellationToken = default)
		{
			return await ExecuteTransactionAsync(async dbContext =>
			{
				if (!await TryLockAsync(dbContext, cancellationToken).ConfigureAwait(false))
				{
					return new ServerBandwidthRollupResult { Skipped = true };
				}

				string minute = dbContext.GetTableName<ServerBandwidthMinuteEntity>();

				/* The current hour is included: re-rolling it every pass keeps the hour rows no more
				 * than one pass behind, and the page reads minutes for the last few hours anyway
				 * (ServerBandwidthMath.StitchHours), so nothing it shows waits on the hour closing.
				 *
				 * Catch-up takes every hour that still has minutes. That is safe because minutes are
				 * only ever deleted a whole hour at a time (PruneAsync), so any hour present is
				 * complete and recomputing it cannot shrink it. */
				string where = catchUp
					? "TRUE"
					: $"m.bucket_start >= date_trunc('hour', {DatabaseUtcNowSql}) - ({{0}} * interval '1 hour')";

				int written = await ExecuteScalarIntAsync(
					dbContext,
					RollupSql(TableName, minute, where),
					catchUp ? Array.Empty<object>() : new object[] { ServerBandwidthMath.RollupWindowHours },
					cancellationToken).ConfigureAwait(false);

				return new ServerBandwidthRollupResult { HoursWritten = written };
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerBandwidthPruneResult>> PruneAsync(int maxMinuteHours, int maxHourBatches, CancellationToken cancellationToken = default)
		{
			var total = new ServerBandwidthPruneResult();

			/* Minutes: one whole hour per transaction, oldest first, folded into its hour row and
			 * then deleted in the same transaction. Whole hours so that no hour is ever left
			 * partial, which a later catch-up would recompute as a smaller hour; folded first so
			 * that pruning never loses traffic the rollup had not reached — a panel that was down
			 * for three weeks still has every hour rolled before its minutes go. */
			for (int i = 0; i < Math.Max(0, maxMinuteHours); i++)
			{
				var step = await ExecuteTransactionAsync(async dbContext =>
				{
					if (!await TryLockAsync(dbContext, cancellationToken).ConfigureAwait(false))
					{
						return (Locked: true, Hour: (DateTime?)null, Deleted: 0L);
					}

					string minute = dbContext.GetTableName<ServerBandwidthMinuteEntity>();
					var oldest = await ReadRowsAsync(
						dbContext,
						$@"SELECT date_trunc('hour', MIN(m.bucket_start))
							FROM {minute} AS m
							HAVING MIN(m.bucket_start) < date_trunc('hour', {DatabaseUtcNowSql} - ({{0}} * interval '1 day'))",
						new object[] { ServerBandwidthMath.MinuteRetentionDays },
						reader => reader.GetDateTime(0),
						cancellationToken).ConfigureAwait(false);
					if (oldest.Count == 0)
					{
						return (Locked: false, Hour: (DateTime?)null, Deleted: 0L);
					}

					DateTime hour = oldest[0];
					await ExecuteScalarIntAsync(
						dbContext,
						RollupSql(TableName, minute, "m.bucket_start >= {0} AND m.bucket_start < {0} + interval '1 hour'"),
						new object[] { hour },
						cancellationToken).ConfigureAwait(false);

					int deleted = await dbContext.Database.ExecuteSqlRawAsync(
						$@"DELETE FROM {minute} WHERE bucket_start >= {{0}} AND bucket_start < {{0}} + interval '1 hour'",
						new object[] { hour },
						cancellationToken).ConfigureAwait(false);

					return (Locked: false, Hour: (DateTime?)hour, Deleted: (long)deleted);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!step.IsSuccess)
				{
					return Fail(step.ErrorCode, step.ErrorMessage, step.IsTransient);
				}
				if (step.Data.Locked)
				{
					total.Skipped = true;
					return DatabaseResult<ServerBandwidthPruneResult>.Success(total);
				}
				if (!step.Data.Hour.HasValue)
				{
					break;
				}
				total.MinuteHoursPruned++;
				total.MinuteRowsDeleted += step.Data.Deleted;
				if (i == maxMinuteHours - 1)
				{
					total.MoreRemaining = true;
				}
			}

			/* Hours: plain bounded deletes past the 13-month horizon. Nothing is derived from them,
			 * so a partial batch is harmless. */
			for (int i = 0; i < Math.Max(0, maxHourBatches); i++)
			{
				var step = await ExecuteTransactionAsync(async dbContext =>
				{
					if (!await TryLockAsync(dbContext, cancellationToken).ConfigureAwait(false))
					{
						return -1;
					}
					return await dbContext.Database.ExecuteSqlRawAsync(
						$@"DELETE FROM {TableName}
							WHERE ctid = ANY(ARRAY(
								SELECT h.ctid FROM {TableName} AS h
								WHERE h.bucket_start < date_trunc('hour', {DatabaseUtcNowSql} - ({{0}} * interval '1 month'))
								LIMIT {{1}}))",
						new object[] { ServerBandwidthMath.HourRetentionMonths, HourPruneBatchRows },
						cancellationToken).ConfigureAwait(false);
				}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!step.IsSuccess)
				{
					return Fail(step.ErrorCode, step.ErrorMessage, step.IsTransient);
				}
				if (step.Data < 0)
				{
					total.Skipped = true;
					break;
				}
				total.HourRowsDeleted += step.Data;
				if (step.Data < HourPruneBatchRows)
				{
					break;
				}
				if (i == maxHourBatches - 1)
				{
					total.MoreRemaining = true;
				}
			}

			return DatabaseResult<ServerBandwidthPruneResult>.Success(total);
		}

		private static DatabaseResult<ServerBandwidthPruneResult> Fail(string code, string message, bool transient)
		{
			return DatabaseResult<ServerBandwidthPruneResult>.Failure(code, message, transient);
		}

		#endregion

		#region Report

		/// <summary>The measured counter columns, in the order every row reader below expects.</summary>
		private const string CounterColumns =
			"interval_ms, app_sent_bytes, app_recv_bytes, udp_sent_bytes, udp_recv_bytes, udp_sent_datagrams, udp_recv_datagrams";

		/// <summary>
		/// One row per server and chart point for a range: its counters summed over the minutes
		/// (or hours) in the point. Returned with the parameters its placeholders use, from 0.
		/// </summary>
		/// <remarks>
		/// The 30-day range reads hour rows up to the stitch point and minutes after it, the same
		/// seam as the 30-day totals, so the chart and the table agree.
		/// </remarks>
		internal static (string Sql, List<object> Parameters) RangeBucketsSql(
			string minute, string hour, ServerBandwidthRange range, DateTime rangeStart, DateTime stitch)
		{
			const string sums = @"SUM(interval_ms) AS interval_ms, SUM(app_sent_bytes) AS app_sent_bytes,
				SUM(app_recv_bytes) AS app_recv_bytes, SUM(udp_sent_bytes) AS udp_sent_bytes,
				SUM(udp_recv_bytes) AS udp_recv_bytes, SUM(udp_sent_datagrams) AS udp_sent_datagrams,
				SUM(udp_recv_datagrams) AS udp_recv_datagrams";

			switch (range)
			{
				case ServerBandwidthRange.Hour:
					return ($@"SELECT server_kind, server_name, bucket_start AS bucket, {sums}
							FROM {minute}
							WHERE bucket_start >= {{0}}
							GROUP BY server_kind, server_name, bucket_start",
						new List<object> { rangeStart });

				case ServerBandwidthRange.Day:
					return ($@"SELECT server_kind, server_name,
								date_trunc('hour', bucket_start) + floor(date_part('minute', bucket_start) / 5) * interval '5 minutes' AS bucket,
								{sums}
							FROM {minute}
							WHERE bucket_start >= {{0}}
							GROUP BY 1, 2, 3",
						new List<object> { rangeStart });

				default:
					return ($@"SELECT server_kind, server_name, bucket, {sums}
							FROM (
								SELECT server_kind, server_name, bucket_start AS bucket, {CounterColumns}
								FROM {hour}
								WHERE bucket_start >= {{0}} AND bucket_start < {{1}}
								UNION ALL
								SELECT server_kind, server_name, date_trunc('hour', bucket_start), {CounterColumns}
								FROM {minute}
								WHERE bucket_start >= {{1}}
							) AS x
							GROUP BY server_kind, server_name, bucket",
						new List<object> { rangeStart, stitch });
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerBandwidthReport>> FetchReportAsync(
			ServerBandwidthRange range,
			int scopeKind,
			string? scopeName,
			CancellationToken cancellationToken = default)
		{
			if (range != ServerBandwidthRange.Hour && range != ServerBandwidthRange.Day && range != ServerBandwidthRange.Month)
			{
				return DatabaseResult<ServerBandwidthReport>.Failure(DatabaseErrorCodes.ValidationError, "The range must be an hour, a day or 30 days.");
			}
			if (scopeKind != 0 && !ServerBandwidthKind.IsValid(scopeKind))
			{
				return DatabaseResult<ServerBandwidthReport>.Failure(DatabaseErrorCodes.ValidationError, "The tier must be login, world or scene.");
			}
			string? name = string.IsNullOrWhiteSpace(scopeName) ? null : scopeName;
			if (name != null && (scopeKind == 0 || name.Length > ServerBandwidthService.MaxServerNameLength))
			{
				return DatabaseResult<ServerBandwidthReport>.Failure(DatabaseErrorCodes.ValidationError, "A server is charted by its tier and a name of at most 100 characters.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* Every statement in one REPEATABLE READ, read-only transaction, so the table, the
				 * totals and the chart are one snapshot and now() is one instant for all of them —
				 * the same reason the server board reads that way. */
				await using var snapshot = await dbContext.Database
					.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken)
					.ConfigureAwait(false);
				await dbContext.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken).ConfigureAwait(false);

				string minute = dbContext.GetTableName<ServerBandwidthMinuteEntity>();
				string hour = TableName;

				var clock = await ReadRowsAsync(dbContext, $"SELECT {DatabaseUtcNowSql}", Array.Empty<object>(),
					reader => reader.GetDateTime(0), cancellationToken).ConfigureAwait(false);
				DateTime now = clock[0];
				DateTime stitch = ServerBandwidthMath.StitchPoint(now);
				DateTime rangeStart = ServerBandwidthMath.RangeStart(range, now);

				/* The three fixed windows per server. The last hour and the last 24 hours are
				 * minutes; the last 30 days are hour rows up to the stitch point and minutes after
				 * it, so they never wait on the rollup's schedule. */
				var windows = await ReadRowsAsync(
					dbContext,
					$@"SELECT x.win, x.server_kind, x.server_name,
							SUM(x.n)::bigint, SUM(x.interval_ms)::bigint,
							SUM(x.app_sent_bytes)::bigint, SUM(x.app_recv_bytes)::bigint,
							SUM(x.udp_sent_bytes)::bigint, SUM(x.udp_recv_bytes)::bigint,
							SUM(x.udp_sent_datagrams)::bigint, SUM(x.udp_recv_datagrams)::bigint,
							SUM(x.sessions_opened)::bigint, SUM(x.connections_refused)::bigint, SUM(x.handshake_failures)::bigint,
							MAX(x.sessions_max)::bigint, MAX(x.bucket_start), COUNT(DISTINCT x.instance_id)::bigint
						FROM (
							SELECT w.win, m.server_kind, m.server_name, 1::bigint AS n, m.{CounterColumns.Replace(", ", ", m.")},
								m.sessions_opened, m.connections_refused, m.handshake_failures,
								m.sessions_active AS sessions_max, m.bucket_start, m.instance_id
							FROM {minute} AS m
							JOIN (VALUES (1, {{0}}::timestamp), (2, {{1}}::timestamp)) AS w(win, since) ON m.bucket_start >= w.since
							WHERE m.bucket_start >= {{1}}
							UNION ALL
							SELECT 3, h.server_kind, h.server_name, h.minutes_sampled, h.{CounterColumns.Replace(", ", ", h.")},
								h.sessions_opened, h.connections_refused, h.handshake_failures,
								h.sessions_active_max, h.bucket_start, NULL::uuid
							FROM {hour} AS h
							WHERE h.bucket_start >= {{2}} AND h.bucket_start < {{3}}
							UNION ALL
							SELECT 3, m.server_kind, m.server_name, 1::bigint, m.{CounterColumns.Replace(", ", ", m.")},
								m.sessions_opened, m.connections_refused, m.handshake_failures,
								m.sessions_active, m.bucket_start, NULL::uuid
							FROM {minute} AS m
							WHERE m.bucket_start >= {{3}}
						) AS x
						GROUP BY x.win, x.server_kind, x.server_name",
					new object[] { now.AddHours(-1), now.AddHours(-24), ServerBandwidthMath.MonthWindowStart(now), stitch },
					ReadWindowRow,
					cancellationToken).ConfigureAwait(false);

				/* Each server's latest minute, only while it is recent enough to be "now". A server
				 * with nothing that recent has no current rate at all — not a rate of zero. */
				var currents = await ReadRowsAsync(
					dbContext,
					$@"SELECT DISTINCT ON (m.server_kind, m.server_name)
							m.server_kind, m.server_name, m.bucket_start, m.{CounterColumns.Replace(", ", ", m.")}, m.sessions_active
						FROM {minute} AS m
						WHERE m.bucket_start >= {{0}}
						ORDER BY m.server_kind, m.server_name, m.bucket_start DESC, m.interval_ms DESC",
					new object[] { now.AddSeconds(-ServerBandwidthMath.CurrentFreshSeconds) },
					reader =>
					{
						var row = ReadPointRow(reader);
						row.Rates.SessionsActive = reader.GetInt64(10);
						return row;
					},
					cancellationToken).ConfigureAwait(false);

				var (bucketsSql, bucketParameters) = RangeBucketsSql(minute, hour, range, rangeStart, stitch);

				// Each server's busiest point of the range, by measured UDP payload sent per second.
				var peaks = await ReadRowsAsync(
					dbContext,
					$@"WITH b AS ({bucketsSql})
						SELECT DISTINCT ON (b.server_kind, b.server_name)
							b.server_kind, b.server_name, b.bucket, b.{CounterColumns.Replace(", ", ", b.")}
						FROM b
						ORDER BY b.server_kind, b.server_name,
							b.udp_sent_bytes::float8 * 1000.0 / GREATEST(b.interval_ms, 1) DESC, b.bucket",
					bucketParameters.ToArray(),
					ReadPointRow,
					cancellationToken).ConfigureAwait(false);

				/* Per tier and point, each server's rate over its own interval, summed. A rate is
				 * bytes over the time that server was measured, so a server that started half-way
				 * through a point is not diluted by the half it was not running. */
				var kindSeries = await ReadRowsAsync(
					dbContext,
					$@"WITH b AS ({bucketsSql})
						SELECT b.server_kind, b.bucket,
							SUM(b.app_sent_bytes::float8 * 1000.0 / GREATEST(b.interval_ms, 1)),
							SUM(b.app_recv_bytes::float8 * 1000.0 / GREATEST(b.interval_ms, 1)),
							SUM(b.udp_sent_bytes::float8 * 1000.0 / GREATEST(b.interval_ms, 1)),
							SUM(b.udp_recv_bytes::float8 * 1000.0 / GREATEST(b.interval_ms, 1)),
							SUM(b.udp_sent_datagrams::float8 * 1000.0 / GREATEST(b.interval_ms, 1)),
							SUM(b.udp_recv_datagrams::float8 * 1000.0 / GREATEST(b.interval_ms, 1))
						FROM b
						GROUP BY b.server_kind, b.bucket
						ORDER BY b.bucket, b.server_kind",
					bucketParameters.ToArray(),
					reader => new ServerBandwidthKindPointRow
					{
						Kind = reader.GetInt32(0),
						Rates = new ServerBandwidthRates
						{
							BucketUtc = Utc(reader.GetDateTime(1)),
							AppSentBps = reader.GetDouble(2),
							AppRecvBps = reader.GetDouble(3),
							UdpSentBps = reader.GetDouble(4),
							UdpRecvBps = reader.GetDouble(5),
							DatagramsSentPs = reader.GetDouble(6),
							DatagramsRecvPs = reader.GetDouble(7),
						},
					},
					cancellationToken).ConfigureAwait(false);

				List<ServerBandwidthRates>? serverSeries = null;
				if (name != null)
				{
					var scoped = new List<object>(bucketParameters) { scopeKind, name };
					int k = bucketParameters.Count;
					var rows = await ReadRowsAsync(
						dbContext,
						$@"WITH b AS ({bucketsSql})
							SELECT b.server_kind, b.server_name, b.bucket, b.{CounterColumns.Replace(", ", ", b.")}
							FROM b
							WHERE b.server_kind = {{{k}}} AND b.server_name = {{{k + 1}}}
							ORDER BY b.bucket",
						scoped.ToArray(),
						ReadPointRow,
						cancellationToken).ConfigureAwait(false);
					serverSeries = rows.ConvertAll(r => r.Rates);
				}

				/* Whether the hour rows reach the stitch point. If the hour just before it has
				 * minutes but the rollup has written nothing that recent, the 30-day figures are
				 * missing those hours, and the page says so rather than showing a short total. */
				var health = await ReadRowsAsync(
					dbContext,
					$@"SELECT (SELECT MAX(h.bucket_start) FROM {hour} AS h),
							EXISTS (SELECT 1 FROM {minute} AS m WHERE m.bucket_start >= {{0}} AND m.bucket_start < {{1}})",
					new object[] { stitch.AddHours(-1), stitch },
					reader => (Latest: reader.IsDBNull(0) ? (DateTime?)null : Utc(reader.GetDateTime(0)), MinutesBeforeStitch: reader.GetBoolean(1)),
					cancellationToken).ConfigureAwait(false);

				await snapshot.CommitAsync(cancellationToken).ConfigureAwait(false);

				var report = ServerBandwidthReportBuilder.Build(
					Utc(now), range, windows, currents, peaks, kindSeries, scopeKind, name, serverSeries);
				report.LatestRolledHourUtc = health[0].Latest;
				report.RollupBehind = health[0].MinutesBeforeStitch &&
					(!health[0].Latest.HasValue || health[0].Latest.Value < Utc(stitch).AddHours(-1));
				return report;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Maps one row of the windows query.</summary>
		private static ServerBandwidthWindowRow ReadWindowRow(DbDataReader reader)
		{
			return new ServerBandwidthWindowRow
			{
				Window = (ServerBandwidthWindow)reader.GetInt32(0),
				Kind = reader.GetInt32(1),
				Name = reader.GetString(2),
				Totals = new ServerBandwidthTotals
				{
					Samples = reader.GetInt64(3),
					IntervalMs = reader.GetInt64(4),
					AppSentBytes = reader.GetInt64(5),
					AppRecvBytes = reader.GetInt64(6),
					UdpSentBytes = reader.GetInt64(7),
					UdpRecvBytes = reader.GetInt64(8),
					UdpSentDatagrams = reader.GetInt64(9),
					UdpRecvDatagrams = reader.GetInt64(10),
					SessionsOpened = reader.GetInt64(11),
					ConnectionsRefused = reader.GetInt64(12),
					HandshakeFailures = reader.GetInt64(13),
					SessionsActiveMax = reader.GetInt64(14),
					LastBucketUtc = reader.IsDBNull(15) ? (DateTime?)null : Utc(reader.GetDateTime(15)),
				},
				Instances = reader.GetInt64(16),
			};
		}

		/// <summary>
		/// Maps a row laid out as kind, name, bucket, then <see cref="CounterColumns"/>: one
		/// server's counters over one bucket, turned into rates over the bucket's own interval.
		/// </summary>
		private static ServerBandwidthPointRow ReadPointRow(DbDataReader reader)
		{
			return new ServerBandwidthPointRow
			{
				Kind = reader.GetInt32(0),
				Name = reader.GetString(1),
				Rates = ServerBandwidthRates.FromCounters(
					Utc(reader.GetDateTime(2)),
					Convert.ToInt64(reader.GetValue(3)),
					Convert.ToInt64(reader.GetValue(4)),
					Convert.ToInt64(reader.GetValue(5)),
					Convert.ToInt64(reader.GetValue(6)),
					Convert.ToInt64(reader.GetValue(7)),
					Convert.ToInt64(reader.GetValue(8)),
					Convert.ToInt64(reader.GetValue(9))),
			};
		}

		private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

		#endregion
	}
}
