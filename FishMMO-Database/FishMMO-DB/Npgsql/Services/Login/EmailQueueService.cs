using System;
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
	/// Outbound email queue service. Uses parameterized raw SQL for writes and
	/// EF Core compiled queries for reads, following the existing service pattern.
	/// </summary>
	public sealed class EmailQueueService : BaseService<EmailQueueEntity>, IEmailQueueService
	{

		public EmailQueueService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> EnqueueAsync(
			string recipientEmail,
			string recipientUsername,
			string subject,
			string body,
			EmailKind kind = EmailKind.Verification,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recipientEmail) ||
				string.IsNullOrWhiteSpace(recipientUsername) ||
				string.IsNullOrWhiteSpace(subject) ||
				string.IsNullOrWhiteSpace(body))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Recipient email, username, subject, and body must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* kind is written explicitly rather than left to the column default, so the
				 * row says what it is even when the default is what it would have been. The
				 * enum's integer is bound, matching the column's storage. */
				var sql = $@"INSERT INTO {TableName} (recipient_email, recipient_username, subject, body, kind)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { recipientEmail, recipientUsername, subject, body, (int)kind },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}


		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			EmailKind? kind = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recipientUsername))
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError, "recipientUsername must not be empty.");
			return await ExecuteReadAsync(async dbContext =>
			{
				/* Callers ask about ONE kind. This check exists to stop a second verification
				 * mail piling up behind the first, and once the queue carries more than one kind
				 * a blind check answers the wrong question: a pending password reset would
				 * suppress the verification mail an unverified player is waiting for, which is
				 * exactly the sequence "forgot my password before I ever verified" produces.
				 * Null still means any kind, so a caller that genuinely wants that can say so. */
				var exists = await dbContext.EmailQueue
					.AnyAsync(e => e.RecipientUsername == recipientUsername &&
								   e.SentAt == null &&
								   (kind == null || e.Kind == kind),
						cancellationToken).ConfigureAwait(false);
				return exists;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
		/// <inheritdoc/>
		public async Task<DatabaseResult<EmailQueueData>> DequeueNextAsync(
			string claimedBy,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(claimedBy))
				return DatabaseResult<EmailQueueData>.Failure(
					DatabaseErrorCodes.ValidationError, "claimedBy must not be empty.");

			return await ExecuteWriteAsync(async dbContext =>
			{
				// FOR UPDATE SKIP LOCKED atomically claims one row in a concurrent-safe
				// manner — multiple LoginServers can run this query simultaneously and
				// each will receive a different row (or none if the queue is empty).
				var sql = $@"WITH next AS (
					SELECT id FROM {TableName}
					WHERE sent_at IS NULL AND claimed_at IS NULL
					ORDER BY created_at
					LIMIT 1
					FOR UPDATE SKIP LOCKED
				)
				UPDATE {TableName} SET claimed_by = {{0}}, claimed_at = timezone('UTC', CURRENT_TIMESTAMP)
				FROM next WHERE {TableName}.id = next.id
				RETURNING {TableName}.id, {TableName}.recipient_email, {TableName}.recipient_username,
				          {TableName}.subject, {TableName}.body, {TableName}.created_at,
				          {TableName}.attempts, {TableName}.claimed_by, {TableName}.claimed_at,
				          {TableName}.kind";

				/* OrDefault, not the throwing variant: an empty queue is the NORMAL case here, and
				 * ExecuteReturningAsync turns "no row" into a DATABASE_ERROR — which made the
				 * null check below unreachable and reported a quiet queue as a fault. The
				 * LoginServer's drain hid that by discarding every failure silently; a caller
				 * that actually reports them backs off to a minute on an idle shard and then
				 * makes the next real message wait that long. */
				var entity = await ExecuteReturningOrDefaultAsync(
					dbContext, sql, new object[] { claimedBy },
					reader => new EmailQueueEntity
					{
						ID = reader.GetInt64(0),
						RecipientEmail = reader.GetString(1),
						RecipientUsername = reader.GetString(2),
						Subject = reader.GetString(3),
						Body = reader.GetString(4),
						CreatedAt = reader.GetDateTime(5),
						Attempts = reader.GetInt32(6),
						ClaimedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
						ClaimedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
						/* An unrecognised integer reads as verification rather than throwing:
						 * a row written by a newer process with a kind this build has never
						 * heard of should still be delivered, and the conservative reading of
						 * an unknown kind is the one every row had before kinds existed. */
						Kind = ToEmailKind(reader.GetInt32(9)),
					},
					cancellationToken).ConfigureAwait(false);

				if (entity == null)
					throw new DatabaseEntityNotFoundException("EmailQueue", "next pending");

				return new EmailQueueData(
					id: entity.ID,
					recipientEmail: entity.RecipientEmail,
					recipientUsername: entity.RecipientUsername,
					subject: entity.Subject,
					body: entity.Body,
					createdAt: entity.CreatedAt,
					attempts: entity.Attempts,
					claimedBy: entity.ClaimedBy,
					claimedAt: entity.ClaimedAt,
					kind: entity.Kind
				);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps a stored integer to <see cref="EmailKind"/>, treating anything unrecognised as
		/// <see cref="EmailKind.Verification"/>.
		/// </summary>
		private static EmailKind ToEmailKind(int value)
		{
			return Enum.IsDefined(typeof(EmailKind), value) ? (EmailKind)value : EmailKind.Verification;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MarkSentAsync(
			long id,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName}
					SET sent_at = timezone('UTC', CURRENT_TIMESTAMP)
					WHERE id = {{0}}";

				var affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { id },
					cancellationToken)
					.ConfigureAwait(false);

				if (affected == 0)
				{
					throw new DatabaseEntityNotFoundException("EmailQueue", id.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MarkFailedAsync(
			long id,
			string error,
			int maxAttempts = 5,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Releasing the claim is the whole point of recording a failure.
				 *
				 * DequeueNextAsync only ever selects rows with claimed_at IS NULL, so a row
				 * left claimed is a row no login server will ever look at again. Incrementing
				 * attempts without clearing it meant the documented retry limit could never be
				 * reached, because there was never a second attempt: one failed send left a
				 * player permanently unable to verify their account, and nothing said so.
				 *
				 * The claim is therefore dropped while attempts remain, and deliberately kept
				 * once they are spent — that is what "give up" looks like in a schema with no
				 * state column, and it is what stops a permanently undeliverable address being
				 * retried forever. The error stays either way: it is the evidence, and on a
				 * released row it is what an operator reads to see why it is back in line. */
				var sql = $@"UPDATE {TableName}
					SET attempts = attempts + 1,
					    last_error = {{0}},
					    claimed_at = CASE WHEN attempts + 1 < {{2}} THEN NULL ELSE claimed_at END,
					    claimed_by = CASE WHEN attempts + 1 < {{2}} THEN NULL ELSE claimed_by END
					WHERE id = {{1}}";

				var affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { error ?? "Unknown error", id, maxAttempts },
					cancellationToken)
					.ConfigureAwait(false);

				if (affected == 0)
				{
					throw new DatabaseEntityNotFoundException("EmailQueue", id.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}