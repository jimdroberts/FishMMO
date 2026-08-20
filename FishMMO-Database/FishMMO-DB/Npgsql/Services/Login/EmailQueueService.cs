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
				var sql = $@"INSERT INTO {TableName} (recipient_email, recipient_username, subject, body)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}})";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { recipientEmail, recipientUsername, subject, body },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}


		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasPendingForUserAsync(
			string recipientUsername,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(recipientUsername))
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError, "recipientUsername must not be empty.");
			return await ExecuteReadAsync(async dbContext =>
			{
				var sql = $"SELECT COUNT(*) FROM {TableName} WHERE recipient_username = {0} AND sent_at IS NULL";
				// ExecuteScalarAsync not directly available; use LINQ Any() via EF
				var exists = await dbContext.EmailQueue
					.AnyAsync(e => e.RecipientUsername == recipientUsername && e.SentAt == null,
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
				          {TableName}.attempts, {TableName}.claimed_by, {TableName}.claimed_at";

				var entity = await ExecuteReturningAsync(
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
					claimedAt: entity.ClaimedAt
				);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
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
				var sql = $@"UPDATE {TableName}
					SET attempts = attempts + 1,
					    last_error = {{0}}
					WHERE id = {{1}}";

				var affected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { error ?? "Unknown error", id },
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