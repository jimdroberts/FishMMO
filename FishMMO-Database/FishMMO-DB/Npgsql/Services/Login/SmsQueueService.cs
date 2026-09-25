using System;
using System.Text.RegularExpressions;
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
	/// Outbound SMS queue service, the twin of <see cref="EmailQueueService"/>. Parameterized raw
	/// SQL for writes, EF for reads.
	/// </summary>
	/// <remarks>
	/// No provider is wired yet; the sender that drains this queue only logs. The claim, retry and
	/// give-up semantics are nonetheless the email queue's exactly, so a real provider slots into
	/// the drain without changing a caller.
	/// </remarks>
	public sealed class SmsQueueService : BaseService<SmsQueueEntity>, ISmsQueueService
	{
		private const int PhoneMaxLength = 16;
		private const int UsernameMaxLength = 50;
		private const int BodyMaxLength = 480;

		/// <summary>
		/// E.164: a plus, a non-zero leading digit, and at most 15 digits in all.
		/// </summary>
		/// <remarks>
		/// Validated here rather than trusted, because a number that is not E.164 is one no
		/// provider will accept, and finding that out at delivery time means a player waiting on a
		/// code that was never going to arrive.
		/// </remarks>
		private static readonly Regex e164Regex = new Regex(@"^\+[1-9][0-9]{1,14}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>Creates the service.</summary>
		public SmsQueueService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> EnqueueAsync(
			string recipientPhone,
			string recipientUsername,
			string body,
			SmsKind kind = SmsKind.Verification,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recipientPhone) ||
				string.IsNullOrWhiteSpace(recipientUsername) ||
				string.IsNullOrWhiteSpace(body))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Recipient phone, username, and body must not be empty.");
			}

			if (recipientPhone.Length > PhoneMaxLength || !e164Regex.IsMatch(recipientPhone))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"The phone number must be in international form: + and up to 15 digits.");
			}

			if (recipientUsername.Length > UsernameMaxLength)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					$"Recipient username must be {UsernameMaxLength} characters or fewer.");
			}

			if (body.Length > BodyMaxLength)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					$"The message must be {BodyMaxLength} characters or fewer.");
			}

			/* Taken once, outside the retried delegate: a retry after a reply lost past the commit
			 * carries the same key and the conflict clause turns it into a no-op, where it used to
			 * write the row a second time (issue #267). */
			Guid requestKey = Guid.NewGuid();

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* kind is written explicitly rather than left to the column default, so the row
				 * says what it is even when the default is what it would have been. The enum's
				 * integer is bound, matching the column's storage. */
				var sql = $@"INSERT INTO {TableName} (recipient_phone, recipient_username, body, kind, request_key)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (request_key) WHERE request_key IS NOT NULL DO NOTHING";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { recipientPhone, recipientUsername, body, (int)kind, requestKey },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			SmsKind? kind = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recipientUsername))
			{
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError, "recipientUsername must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				/* Callers ask about ONE kind, for the reason the email queue does: a blind check
				 * lets a pending message of another kind suppress the verification code a player
				 * is waiting for. Null still means any kind, for a caller that genuinely wants it. */
				return await dbContext.SmsQueue
					.AnyAsync(e => e.RecipientUsername == recipientUsername &&
								   e.SentAt == null &&
								   (kind == null || e.Kind == kind),
						cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SmsQueueData>> DequeueNextAsync(
			string claimedBy,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(claimedBy))
			{
				return DatabaseResult<SmsQueueData>.Failure(
					DatabaseErrorCodes.ValidationError, "claimedBy must not be empty.");
			}

			/* Taken once, outside the retried delegate, and written as the claim's own stamp. A
			 * connection lost after the claim committed but before its reply arrived is retried,
			 * and the retry used to claim the NEXT row, leaving the first claimed by this sender
			 * and never delivered, since nothing but an operator's queue-board retry releases a
			 * claim. The retry now finds its own claim by (claimant, stamp) and returns that row
			 * again (issue #267). */
			DateTime claimStampUtc = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				// FOR UPDATE SKIP LOCKED atomically claims one row in a concurrent-safe
				// manner — multiple senders can run this query simultaneously and each
				// will receive a different row (or none if the queue is empty).
				var sql = $@"WITH mine AS (
					SELECT id FROM {TableName}
					WHERE claimed_by = {{0}} AND claimed_at = {{1}} AND sent_at IS NULL
				),
				next AS (
					SELECT id FROM {TableName}
					WHERE sent_at IS NULL AND claimed_at IS NULL
					  AND NOT EXISTS (SELECT 1 FROM mine)
					ORDER BY created_at
					LIMIT 1
					FOR UPDATE SKIP LOCKED
				),
				pick AS (
					SELECT id FROM mine
					UNION ALL
					SELECT id FROM next
				)
				UPDATE {TableName} SET claimed_by = {{0}}, claimed_at = {{1}}
				FROM pick WHERE {TableName}.id = pick.id
				RETURNING {TableName}.id, {TableName}.recipient_phone, {TableName}.recipient_username,
				          {TableName}.body, {TableName}.created_at,
				          {TableName}.attempts, {TableName}.claimed_by, {TableName}.claimed_at,
				          {TableName}.kind";

				/* OrDefault, not the throwing variant: an empty queue is the NORMAL case, and
				 * ExecuteReturningAsync turns "no row" into a DATABASE_ERROR that would report a
				 * quiet queue as a fault and make a drain that reports failures back off while
				 * real messages wait. */
				var entity = await ExecuteReturningOrDefaultAsync(
					dbContext, sql, new object[] { claimedBy, claimStampUtc },
					reader => new SmsQueueEntity
					{
						ID = reader.GetInt64(0),
						RecipientPhone = reader.GetString(1),
						RecipientUsername = reader.GetString(2),
						Body = reader.GetString(3),
						CreatedAt = reader.GetDateTime(4),
						Attempts = reader.GetInt32(5),
						ClaimedBy = reader.IsDBNull(6) ? null : reader.GetString(6),
						ClaimedAt = reader.IsDBNull(7) ? (DateTime?)null : reader.GetDateTime(7),
						/* An unrecognised integer reads as verification rather than throwing: a
						 * row written by a newer process with a kind this build has never heard of
						 * should still be delivered. */
						Kind = ToSmsKind(reader.GetInt32(8)),
					},
					cancellationToken).ConfigureAwait(false);

				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("SmsQueue", "next pending");
				}

				return new SmsQueueData(
					id: entity.ID,
					recipientPhone: entity.RecipientPhone,
					recipientUsername: entity.RecipientUsername,
					body: entity.Body,
					createdAt: entity.CreatedAt,
					attempts: entity.Attempts,
					claimedBy: entity.ClaimedBy,
					claimedAt: entity.ClaimedAt,
					kind: entity.Kind);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps a stored integer to <see cref="SmsKind"/>, treating anything unrecognised as
		/// <see cref="SmsKind.Verification"/>.
		/// </summary>
		private static SmsKind ToSmsKind(int value)
		{
			return Enum.IsDefined(typeof(SmsKind), value) ? (SmsKind)value : SmsKind.Verification;
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
					throw new DatabaseEntityNotFoundException("SmsQueue", id.ToString());
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
				 * DequeueNextAsync only ever selects rows with claimed_at IS NULL, so a row left
				 * claimed is a row no sender will ever look at again, and the retry limit could
				 * never be reached because there would never be a second attempt.
				 *
				 * The claim is therefore dropped while attempts remain, and deliberately kept once
				 * they are spent — that is what "give up" looks like in a schema with no state
				 * column, and it stops a permanently undeliverable number being retried forever.
				 * The error stays either way: it is the evidence an operator reads. */
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
					throw new DatabaseEntityNotFoundException("SmsQueue", id.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
