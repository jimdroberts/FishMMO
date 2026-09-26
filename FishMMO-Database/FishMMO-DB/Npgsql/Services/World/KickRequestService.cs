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
	public sealed class KickRequestService : BaseService<KickRequestEntity>, IKickRequestService
	{
		/// <summary>
		/// Initializes a new instance of KickRequestService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public KickRequestService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <summary>
		/// One page of a poll. <c>{0}</c> the start, <c>{1}</c> and <c>{2}</c> the handled ids and
		/// stamps, <c>{3}</c> the page size.
		/// </summary>
		private static string BuildPollSql(string kickRequests, string accounts)
		{
			/* Each kicked account's last login is read here, as a correlated subquery on the
			 * accounts name_lowercase index, rather than by the caller one account at a time.
			 * Every login, world and scene server polls this page and used to follow it with one
			 * last-login query per kick — a hundred kicks cost every server a hundred round
			 * trips. The match is the one FetchLastLoginAsync used: the lowercased name.
			 *
			 * The handled requests are skipped by (id, stamp), not by id: a second kick of an
			 * account keeps its row and moves the stamp, and is a new kick. */
			return $@"SELECT kr.id, kr.account_name, kr.time_created,
					(SELECT a.last_login FROM {accounts} a WHERE a.name_lowercase = lower(kr.account_name) LIMIT 1) AS last_login
				FROM {kickRequests} kr
				WHERE kr.time_created >= {{0}}
					AND NOT EXISTS (
						SELECT 1 FROM unnest({{1}}::bigint[], {{2}}::timestamp[]) AS h(id, stamp)
						WHERE h.id = kr.id AND h.stamp = kr.time_created)
				ORDER BY kr.time_created, kr.id
				LIMIT {{3}}";
		}

		/// <summary>
		/// How long a kick request stays authoritative for <see cref="HasPendingAsync"/>.
		/// </summary>
		/// <remarks>
		/// A kick request is deleted by whichever game server acts on it, from its
		/// connection-stopped handler — so the normal lifetime is seconds. If the server holding
		/// the session dies before polling, nothing ever deletes the row, and an unbounded
		/// <see cref="HasPendingAsync"/> then reported the account as "already online" at every
		/// future login for the life of the database. Bounding it to slightly more than the
		/// character session lease means recovery takes at most one lease, which is the same
		/// window every other crash-recovery path in the session protocol uses.
		/// </remarks>
		private static readonly TimeSpan PendingKickRequestTtl = TimeSpan.FromMinutes(3);

		/// <summary>
		/// How far behind its own progress a game server's kick poll reads, in seconds.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A request's stamp is not its commit.</b> Both writers stamp <c>time_created</c> with
		/// the transaction's start (<see cref="PersistAsync"/>, and the ban in
		/// <c>AccountService</c>, which writes its kick after revoking the account's tokens and
		/// sessions in the same transaction), and the row becomes visible only when that
		/// transaction commits. A reader that paged by a strict <c>(time, id)</c> cursor could move
		/// past a stamp before its row committed and never see it: the operator's kick silently
		/// never landed.
		/// </para>
		/// <para>
		/// So the poll re-reads this far behind the newest point it has settled and skips what it
		/// has already handled (<see cref="KickRequestReadWindow"/>). A kick is caught as long as it
		/// commits within this long of its stamp; ten seconds is a stall, not a slow write. It lives
		/// here, beside the writers, so the two sides of the contract are one number.
		/// </para>
		/// </remarks>
		public const double PollCommitWindowSeconds = 10.0;

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Race Condition Protection:</b></para>
		/// Uses ON CONFLICT clause with unique constraint on account_name to prevent duplicate
		/// kick requests in distributed environments. If a duplicate request is attempted, the
		/// timestamp is updated instead, ensuring idempotency and preventing DOS attacks through
		/// request flooding.
		/// </remarks>
		public async Task<DatabaseResult> PersistAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Use database server time to avoid clock skew issues.
				// Keep atomic UPSERT semantics to prevent duplicate kick requests under concurrency.
				var sql = $@"INSERT INTO {TableName}
					(account_name, time_created)
					VALUES ({{0}}, timezone('UTC', CURRENT_TIMESTAMP))
					ON CONFLICT (account_name)
					DO UPDATE SET time_created = timezone('UTC', CURRENT_TIMESTAMP)";

				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $"DELETE FROM {TableName} WHERE account_name = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasPendingAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			/* Aged by the database's clock, which stamped the row. The cutoff used to be this
			 * process's DateTime.UtcNow less the TTL: a login server running ahead of the database
			 * treated a fresh kick as expired and let the kicked account straight back in, and one
			 * running behind held an expired kick against the account for the size of its lag. */
			string sql = $@"SELECT EXISTS (
					SELECT 1 FROM {TableName}
					WHERE account_name = {{0}}
						AND time_created > {DatabaseUtcClockSql} - {{1}}::interval)";

			return await ExecuteReadAsync(async dbContext =>
			{
				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { accountName, PendingKickRequestTtl },
					reader => reader.GetBoolean(0),
					cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<KickRequestPage>> FetchAsync(
			KickRequestPollQuery query,
			CancellationToken cancellationToken = default)
		{
			if (query == null)
			{
				return DatabaseResult<KickRequestPage>.Failure(DatabaseErrorCodes.ValidationError, "A poll query is required.");
			}

			long[] handledIds = query.HandledIds ?? Array.Empty<long>();
			DateTime[] handledStamps = query.HandledStamps ?? Array.Empty<DateTime>();
			if (handledIds.Length != handledStamps.Length)
			{
				return DatabaseResult<KickRequestPage>.Failure(DatabaseErrorCodes.ValidationError, "Handled ids and stamps must pair up.");
			}
			int pageSize = Math.Max(1, query.PageSize);
			double lookback = query.FirstReadLookbackSeconds > 0.0 && !double.IsInfinity(query.FirstReadLookbackSeconds)
				? query.FirstReadLookbackSeconds
				: 0.0;

			return await ExecuteReadAsync(async dbContext =>
			{
				/* The database clock before the page, so "everything committed before this instant
				 * has been read" is a statement about the clock the rows are stamped with. The reader
				 * advances its window from this, never from its own clock. */
				DateTime readStartedUtc = await ReadDatabaseUtcNowAsync(dbContext, cancellationToken).ConfigureAwait(false);
				DateTime readFromUtc = query.FromUtc.HasValue
					? DateTime.SpecifyKind(query.FromUtc.Value, DateTimeKind.Utc)
					: readStartedUtc.AddSeconds(-lookback);

				var page = new KickRequestPage
				{
					ReadStartedUtc = readStartedUtc,
					ReadFromUtc = readFromUtc,
				};

				List<KickRequestData> rows = await ReadRowsAsync(
					dbContext,
					BuildPollSql(TableName, dbContext.GetTableName<AccountEntity>()),
					new object[] { readFromUtc, handledIds, handledStamps, pageSize },
					reader => new KickRequestData(
						reader.GetInt64(0),
						reader.GetString(1),
						DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
						reader.IsDBNull(3) ? (DateTime?)null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc)),
					cancellationToken).ConfigureAwait(false);

				page.Requests.AddRange(rows);
				page.Drained = rows.Count < pageSize;
				return page;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}