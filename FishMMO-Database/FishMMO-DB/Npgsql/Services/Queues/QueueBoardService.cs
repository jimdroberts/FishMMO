using System;
using System.Collections.Generic;
using System.Data.Common;
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
	/// <summary>
	/// Reads the email and group finder queues for the operator board, and releases a stuck
	/// email back into its queue.
	/// </summary>
	/// <remarks>
	/// <para>
	/// EF throughout for the reads, one parameterized statement for the single write. See
	/// <see cref="IQueueBoardService"/> for why this is a service of its own and why it cannot
	/// empty anything.
	/// </para>
	/// <para>
	/// <b>The counts are four separate <c>COUNT</c> statements rather than one grouped scan.</b>
	/// A single <c>GROUP BY</c> over a computed state expression would be one round trip, but the
	/// expression is a nested conditional and this project is pinned to EF Core 5 (see the
	/// csproj: netstandard2.1 for Unity), whose GROUP BY translation gives up on shapes like
	/// that and falls back to evaluating them <em>in memory over the whole table</em>. On the one
	/// page whose job is to notice a queue that has grown without bound, silently reading the
	/// entire queue into the panel to count it is the worst available failure. Four counts
	/// against the table's existing <c>(sent_at, created_at)</c> and <c>(claimed_at, sent_at)</c>
	/// indexes are cheap, obviously correct, and cannot degrade into that.
	/// </para>
	/// </remarks>
	public sealed class QueueBoardService : BaseService<EmailQueueEntity>, IQueueBoardService
	{
		/// <summary>The most rows one page will return.</summary>
		private const int MaxPageSize = 200;

		/// <summary>Rows per page when the caller asks for nothing sensible.</summary>
		private const int DefaultPageSize = 25;

		/// <summary>The value <c>group_finder_queue.status</c> holds for a waiting row.</summary>
		private const int GroupFinderWaiting = (int)Data.Enums.GroupFinderQueueStatus.Waiting;

		/// <summary>The value <c>group_finder_queue.status</c> holds for a matched row.</summary>
		private const int GroupFinderMatched = (int)Data.Enums.GroupFinderQueueStatus.Matched;

		public QueueBoardService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<EmailQueuePage>> FetchEmailQueueAsync(
			EmailQueueState? state,
			string search,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			ClampPaging(ref page, ref pageSize);
			string term = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<EmailQueueEntity> all = dbContext.EmailQueue.AsNoTracking();

				/* The counts and the two oldest-row timestamps are taken over the WHOLE table,
				 * before any filter is applied. They are the alarm this page exists to raise, and
				 * an alarm that changed when the operator narrowed a filter would be worse than
				 * no alarm at all — somebody filtering to "sent" to check one player's mail would
				 * watch the pending count fall to zero. */
				var counts = new EmailQueueCounts
				{
					Pending = await all.CountAsync(e => e.SentAt == null && e.ClaimedAt == null, cancellationToken).ConfigureAwait(false),
					Claimed = await all.CountAsync(e => e.SentAt == null && e.ClaimedAt != null && e.LastError == null, cancellationToken).ConfigureAwait(false),
					Failed = await all.CountAsync(e => e.SentAt == null && e.ClaimedAt != null && e.LastError != null, cancellationToken).ConfigureAwait(false),
					Sent = await all.CountAsync(e => e.SentAt != null, cancellationToken).ConfigureAwait(false),
				};

				/* Projected to a nullable DateTime before Min so that an empty queue answers null
				 * instead of throwing — the empty queue is the normal case and must not be an
				 * error path on a page that is polled. */
				DateTime? oldestPending = await all
					.Where(e => e.SentAt == null && e.ClaimedAt == null)
					.Select(e => (DateTime?)e.CreatedAt)
					.MinAsync(cancellationToken)
					.ConfigureAwait(false);

				DateTime? oldestUnsent = await all
					.Where(e => e.SentAt == null)
					.Select(e => (DateTime?)e.CreatedAt)
					.MinAsync(cancellationToken)
					.ConfigureAwait(false);

				IQueryable<EmailQueueEntity> q = all;

				if (state.HasValue)
				{
					/* The state lives in three nullable columns rather than one value, so the
					 * filter is written out per state instead of compared to a column. Each arm
					 * is the same predicate the matching count above uses; they are kept next to
					 * each other deliberately, because a filter and a count that disagree about
					 * what "failed" means is a bug nobody would think to look for. */
					switch (state.Value)
					{
						case EmailQueueState.Pending:
							q = q.Where(e => e.SentAt == null && e.ClaimedAt == null);
							break;
						case EmailQueueState.Claimed:
							q = q.Where(e => e.SentAt == null && e.ClaimedAt != null && e.LastError == null);
							break;
						case EmailQueueState.Failed:
							q = q.Where(e => e.SentAt == null && e.ClaimedAt != null && e.LastError != null);
							break;
						case EmailQueueState.Sent:
							q = q.Where(e => e.SentAt != null);
							break;
						default:
							throw new DatabaseException(
								"That is not an email queue state.",
								errorCode: DatabaseErrorCodes.ValidationError);
					}
				}

				if (term != null)
				{
					/* Lower-cased Contains rather than ILIKE with a built pattern: Npgsql
					 * translates it to strpos(), so a search for "100%" or "a_b" cannot smuggle
					 * a wildcard into the predicate and match half the table. */
					q = q.Where(e => e.RecipientEmail.ToLower().Contains(term) || e.RecipientUsername.ToLower().Contains(term));
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				var rows = await q
					/* Undelivered first, then oldest first. This is a queue, so the head of it is
					 * what an operator came to see, and the oldest undelivered row is the one the
					 * headline age is measured from — it must be on page one. Delivered rows sort
					 * after because they are history. */
					.OrderBy(e => e.SentAt != null ? 1 : 0)
					.ThenBy(e => e.CreatedAt)
					.ThenBy(e => e.ID)
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					/* The body column is deliberately not named here. A verification body carries
					 * the verification link, which is a credential: read it and you can verify
					 * somebody else's account without their mailbox. Leaving it out of the
					 * projection keeps it out of the panel process entirely. */
					.Select(e => new EmailQueueAdminData
					{
						ID = e.ID,
						RecipientEmail = e.RecipientEmail,
						RecipientUsername = e.RecipientUsername,
						Subject = e.Subject,
						CreatedAt = e.CreatedAt,
						SentAt = e.SentAt,
						Attempts = e.Attempts,
						ClaimedBy = e.ClaimedBy,
						ClaimedAt = e.ClaimedAt,
						LastError = e.LastError,
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var row in rows)
				{
					row.State = ClassifyEmail(row.SentAt, row.ClaimedAt, row.LastError);
				}

				return new EmailQueuePage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
					Counts = counts,
					OldestPendingCreatedAt = oldestPending,
					OldestUnsentCreatedAt = oldestUnsent,
					ReadAtUtc = DateTime.UtcNow,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// The one place the four columns become a state.
		/// </summary>
		/// <remarks>
		/// The order of the tests is the definition, not an implementation detail — see
		/// <see cref="EmailQueueState"/>. In particular, unclaimed beats a recorded error, so a
		/// row a retry put back in line reads as pending while still carrying the evidence of why
		/// it was retried.
		/// </remarks>
		private static EmailQueueState ClassifyEmail(DateTime? sentAt, DateTime? claimedAt, string lastError)
		{
			if (sentAt != null)
			{
				return EmailQueueState.Sent;
			}
			if (claimedAt == null)
			{
				return EmailQueueState.Pending;
			}
			return lastError != null ? EmailQueueState.Failed : EmailQueueState.Claimed;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<EmailRetryResult>> RetryEmailAsync(
			long id,
			CancellationToken cancellationToken = default)
		{
			if (id <= 0)
			{
				return DatabaseResult<EmailRetryResult>.Failure(DatabaseErrorCodes.ValidationError, "An email id is required.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* One statement: lock the row, decide on it, write it, and report what the row
				 * looked like either way.
				 *
				 * It has to be one statement rather than a read followed by a write. A login
				 * server claiming this same row runs DequeueNextAsync, which is itself a single
				 * FOR UPDATE SKIP LOCKED statement; between a separate read and write here, that
				 * server could claim the row and begin delivering it, and this would then clear
				 * the claim of a delivery already in flight — producing a duplicate send. The
				 * FOR UPDATE in `target` makes the two serialise on the row instead.
				 *
				 * The final SELECT is over `target`, not over `released`, so a refusal still
				 * comes back with the row's actual state and this can say WHY it did nothing
				 * rather than reporting an unexplained zero rows affected.
				 *
				 * attempts and last_error are deliberately left alone. They are the evidence of
				 * what went wrong, and an operator pressing retry a second time needs to see that
				 * the first one came back. version is bumped because the entity declares it as a
				 * concurrency token that every write moves. */
				string sql = $@"WITH target AS (
						SELECT id, version, sent_at, claimed_at, claimed_by, attempts,
						       recipient_email, recipient_username
						FROM {TableName}
						WHERE id = {{0}}
						FOR UPDATE
					),
					released AS (
						UPDATE {TableName} AS e
						SET claimed_by = NULL,
						    claimed_at = NULL,
						    version = e.version + 1
						FROM target AS t
						WHERE e.id = t.id
						  AND t.sent_at IS NULL
						  AND t.claimed_at IS NOT NULL
						RETURNING e.id
					)
					SELECT t.recipient_email, t.recipient_username, t.attempts,
					       t.sent_at, t.claimed_at, t.claimed_by,
					       EXISTS (SELECT 1 FROM released) AS retried
					FROM target AS t";

				var outcome = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { id },
					reader => new RetryRow
					{
						RecipientEmail = reader.IsDBNull(0) ? null : reader.GetString(0),
						RecipientUsername = reader.IsDBNull(1) ? null : reader.GetString(1),
						Attempts = reader.GetInt32(2),
						SentAt = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
						ClaimedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
						ClaimedBy = reader.IsDBNull(5) ? null : reader.GetString(5),
						Retried = reader.GetBoolean(6),
					},
					cancellationToken).ConfigureAwait(false);

				if (outcome == null)
				{
					throw new DatabaseEntityNotFoundException("EmailQueue", id.ToString());
				}

				var result = new EmailRetryResult
				{
					ID = id,
					Retried = outcome.Retried,
					RecipientEmail = outcome.RecipientEmail,
					RecipientUsername = outcome.RecipientUsername,
					Attempts = outcome.Attempts,
					ClaimedBy = outcome.ClaimedBy,
				};

				if (!outcome.Retried)
				{
					/* Two ways to be refused, and they mean opposite things, so they are never
					 * collapsed into one message. "Already sent" means stop — re-sending mail
					 * somebody has already acted on is its own harm. "Still unclaimed" means the
					 * message is fine and the login server is not: the row is already at the
					 * front of the queue and pressing retry again will never help. */
					result.Refusal = outcome.SentAt != null
						? $"That message was already delivered at {outcome.SentAt:u}. Retrying it would send it a second time."
						: "That message has not been claimed by a login server, so it is already in line and there is nothing to release. What it is waiting for is a login server that claims and sends it.";
				}

				return result;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>The shape the retry statement returns. Never leaves this class.</summary>
		private sealed class RetryRow
		{
			public string RecipientEmail { get; set; }
			public string RecipientUsername { get; set; }
			public int Attempts { get; set; }
			public DateTime? SentAt { get; set; }
			public DateTime? ClaimedAt { get; set; }
			public string ClaimedBy { get; set; }
			public bool Retried { get; set; }
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GroupFinderQueuePage>> FetchGroupFinderQueueAsync(
			int? status,
			int page,
			int pageSize,
			CancellationToken cancellationToken = default)
		{
			ClampPaging(ref page, ref pageSize);

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<GroupFinderQueueEntity> all = dbContext.GroupFinderQueue.AsNoTracking();

				// Over the whole table, for the same reason the email counts are. See above.
				var counts = new GroupFinderQueueCounts
				{
					Waiting = await all.CountAsync(g => g.Status == GroupFinderWaiting, cancellationToken).ConfigureAwait(false),
					Matched = await all.CountAsync(g => g.Status == GroupFinderMatched, cancellationToken).ConfigureAwait(false),
				};

				IQueryable<GroupFinderQueueEntity> q = all;
				if (status.HasValue)
				{
					int wanted = status.Value;
					q = q.Where(g => g.Status == wanted);
				}

				int total = await q.CountAsync(cancellationToken).ConfigureAwait(false);

				/* Left joined to characters, not inner joined. The foreign key cascades on
				 * delete, so a queue row without a character should be impossible — which is
				 * exactly why an inner join is the wrong shape here: it would answer an
				 * impossible row by silently not listing it, and an operator counting rows
				 * against the count above would see a discrepancy with no cause on screen. */
				var rows = await (
					from g in q
					join c in dbContext.Characters.AsNoTracking() on g.CharacterID equals c.ID into joined
					from c in joined.DefaultIfEmpty()
					// Longest waiting first: the head of the queue is the question being asked.
					orderby g.TimeCreated, g.ID
					select new GroupFinderQueueAdminData
					{
						ID = g.ID,
						WorldServerID = g.WorldServerID,
						CharacterID = g.CharacterID,
						CharacterName = c == null ? null : c.Name,
						AccountName = c == null ? null : c.Account,
						SceneType = g.SceneType,
						SceneName = g.SceneName,
						Difficulty = g.Difficulty,
						Status = g.Status,
						GroupID = g.GroupID,
						PartyID = g.PartyID,
						InstanceID = g.InstanceID,
						TimeCreated = g.TimeCreated,
						LastPulse = g.LastPulse,
						TimeMatched = g.TimeMatched,
					})
					.Skip((page - 1) * pageSize)
					.Take(pageSize)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return new GroupFinderQueuePage
				{
					Items = rows,
					Page = page,
					PageSize = pageSize,
					TotalCount = total,
					Counts = counts,
					ReadAtUtc = DateTime.UtcNow,
				};
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Brings a caller's paging into range rather than refusing it.
		/// </summary>
		/// <remarks>
		/// A page number below one, or a page size of ten thousand, is a mistake in a URL and not
		/// an attack worth an error page; clamping shows the operator something while a refusal
		/// would show them nothing. The ceiling is what stops one query string from asking the
		/// panel to serialize the entire queue.
		/// </remarks>
		private static void ClampPaging(ref int page, ref int pageSize)
		{
			if (page < 1)
			{
				page = 1;
			}
			if (pageSize < 1)
			{
				pageSize = DefaultPageSize;
			}
			if (pageSize > MaxPageSize)
			{
				pageSize = MaxPageSize;
			}
		}
	}
}
