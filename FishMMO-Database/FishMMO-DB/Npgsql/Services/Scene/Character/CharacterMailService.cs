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
	/// <summary>
	/// Service for managing character mail in the database.
	/// Provides async operations for CRUD operations on character mail data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character mail including:
	/// - Sending new mail with atomic INSERT operations
	/// - Mail deletion (individual and bulk by character)
	/// - Mail retrieval and count queries
	/// 
	/// All exceptions are classified by <c>BaseService</c> and mapped to <see cref="DatabaseResult"/> error codes
	/// (e.g., UNIQUE_VIOLATION, FOREIGN_KEY_VIOLATION, STALE_STATE, DATABASE_ERROR). Transient failures are retried automatically.
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// </remarks>
	public sealed class CharacterMailService : BaseService<CharacterMailEntity>, ICharacterMailService
	{
		/// <summary>
		/// Compiled query for retrieving character mail by recipient character ID.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterMailEntity>> getMailQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterMail
					.AsNoTracking()
					.Where(m => m.CharacterID == characterId && !m.Deleted)
					// AsQueryable() keeps overload resolution on the IQueryable<T> (sequence)
					// form of CompileAsyncQuery: OrderByDescending returns IOrderedQueryable<T>,
					// which otherwise binds to the scalar Task<TResult> overload instead.
					.OrderByDescending(m => m.TimeCreated)
					.AsQueryable());

		/// <summary>
		/// Compiled query for counting character mail.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getMailCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterMail
					.AsNoTracking()
					.Where(m => m.CharacterID == characterId && !m.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterMailService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterMailService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SendAsync(
			long senderCharacterId,
			string senderName,
			long recipientCharacterId,
			string subject,
			string body,
			int itemAttachmentTemplateID,
			int itemAttachmentSeed,
			uint itemAttachmentAmount,
			int currencyAttachment,
			long incomingVersion,
			CancellationToken cancellationToken = default)
		{
			if (senderCharacterId <= 0 || recipientCharacterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid sender or recipient character ID. Both must be greater than 0.");
			}

			if (string.IsNullOrWhiteSpace(subject))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Mail subject cannot be null or empty.");
			}

			if (string.IsNullOrWhiteSpace(body))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Mail body cannot be null or empty.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var isActiveRecipient = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == recipientCharacterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);

				if (!isActiveRecipient)
				{
					throw new DatabaseEntityNotFoundException("Character", recipientCharacterId.ToString(), "Recipient character not found or deleted.");
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					INSERT INTO {TableName}
						(sender_id, sender_name, character_id, subject, body, item_attachment_template_id, item_attachment_seed, item_attachment_amount, currency_attachment, read, version, time_created, deleted, time_deleted)
					VALUES
						({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, {{9}}, {{10}}, {{11}}, FALSE, NULL)";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					// itemAttachmentAmount is a uint, and Npgsql cannot bind System.UInt32 — the boxed
					// parameter throws NotSupportedException whether or not mail carries an attachment,
					// because the declared type is what gets boxed. item_attachment_amount is bigint, so
					// long binds exactly. Same defect as the item services' Amount.
					// currencyAttachment was hard-coded to 0 here, so a mail could carry an item but
					// never money — the column existed, the DTO exposed it, and nothing could put a
					// value in it.
					new object[] { senderCharacterId, senderName, recipientCharacterId, subject, body, itemAttachmentTemplateID, itemAttachmentSeed, (long)itemAttachmentAmount, currencyAttachment, false, incomingVersion, now },
					cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterMailAttachmentData?>> ClaimAttachmentAsync(
			long mailId,
			long characterId,
			long incomingVersion,
			CancellationToken cancellationToken = default)
		{
			if (mailId <= 0 || characterId <= 0)
			{
				return DatabaseResult<CharacterMailAttachmentData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid mail ID or character ID. Both must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult<CharacterMailAttachmentData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync<CharacterMailAttachmentData?>(async dbContext =>
			{
				/* Read and clear in one statement.
				 *
				 * The self-join to `prev` is what makes RETURNING give the values as they were
				 * BEFORE the update — RETURNING on its own reports the new values, which here are
				 * all zero and therefore useless. The `<> 0` predicate is the anti-double-claim
				 * guard: once the first claim has zeroed the row, a second UPDATE matches nothing
				 * and returns no row, and Postgres serialises the two on the row lock so they
				 * cannot both see the attachment. */
				var sql = $@"
					UPDATE {TableName} AS m
					SET item_attachment_template_id = 0,
						item_attachment_seed = 0,
						item_attachment_amount = 0,
						currency_attachment = 0,
						version = {{2}}
					FROM {TableName} AS prev
					WHERE m.id = prev.id
						AND m.id = {{0}}
						AND m.character_id = {{1}}
						AND m.deleted = FALSE
						AND m.version < {{2}}
						AND (m.item_attachment_template_id <> 0 OR m.currency_attachment <> 0)
					RETURNING prev.item_attachment_template_id,
							  prev.item_attachment_seed,
							  prev.item_attachment_amount,
							  prev.currency_attachment";

				return await ExecuteReturningOrDefaultAsync<CharacterMailAttachmentData?>(
					dbContext,
					sql,
					new object[] { mailId, characterId, incomingVersion },
					reader => new CharacterMailAttachmentData(
						reader.GetInt32(0),
						reader.GetInt32(1),
						// item_attachment_amount is bigint; Npgsql cannot bind or read uint
						// directly, so it is stored and read as long and narrowed here.
						(uint)reader.GetInt64(2),
						reader.GetInt32(3)),
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long mailId, long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (mailId <= 0 || characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid mail ID or character ID. Both must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE id = {{2}} AND character_id = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, mailId, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var stillActive = await dbContext.CharacterMail
						.AsNoTracking()
						.AnyAsync(m => m.ID == mailId && m.CharacterID == characterId && !m.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Mail delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterMail
						.AsNoTracking()
						.AnyAsync(m => m.CharacterID == characterId && !m.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Mail delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterMailData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterMailData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getMailQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);

				// Batch-fetch sender names as fallback for any entries missing SenderName
				var senderIds = entities.Where(m => string.IsNullOrEmpty(m.SenderName)).Select(m => m.SenderID).Distinct().ToList();
				Dictionary<long, string>? senderNames = null;
				if (senderIds.Count > 0)
				{
					senderNames = await dbContext.Characters
						.AsNoTracking()
						.Where(c => senderIds.Contains(c.ID))
						.Select(c => new { c.ID, c.Name })
						.ToDictionaryAsync(c => c.ID, c => c.Name, cancellationToken)
						.ConfigureAwait(false);
				}

				var mail = entities.Select(m => new CharacterMailData(
					id: m.ID,
					version: m.Version,
					characterID: m.CharacterID,
					senderID: m.SenderID,
					senderName: !string.IsNullOrEmpty(m.SenderName) ? m.SenderName
						: (senderNames != null && senderNames.TryGetValue(m.SenderID, out var name) ? name : string.Empty),
					subject: m.Subject,
					body: m.Body,
					timeSent: m.TimeCreated,
					read: m.Read,
					currencyAttachment: m.CurrencyAttachment,
					itemAttachmentTemplateID: m.ItemAttachmentTemplateID,
					itemAttachmentSeed: m.ItemAttachmentSeed,
					itemAttachmentAmount: (int)m.ItemAttachmentAmount
				)).ToList();

				return (IReadOnlyList<CharacterMailData>)mail;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
				await getMailCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}