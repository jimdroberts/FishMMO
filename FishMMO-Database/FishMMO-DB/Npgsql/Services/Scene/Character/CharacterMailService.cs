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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterMailEntity>>> getMailQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterMail
					.AsNoTracking()
					.Where(m => m.CharacterID == characterId && !m.Deleted)
					.OrderByDescending(m => m.TimeCreated)
					.ToList());

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
			long recipientCharacterId,
			string subject,
			string message,
			int itemAttachmentTemplateID,
			int itemAttachmentSeed,
			uint itemAttachmentAmount,
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

			if (string.IsNullOrWhiteSpace(message))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Mail message cannot be null or empty.");
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
						(sender_character_id, character_id, subject, message, item_attachment_template_id, item_attachment_seed, item_attachment_amount, version, time_created, deleted, time_deleted)
					VALUES
						({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, FALSE, NULL)";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { senderCharacterId, recipientCharacterId, subject, message, itemAttachmentTemplateID, itemAttachmentSeed, itemAttachmentAmount, incomingVersion, now },
					cancellationToken)
					.ConfigureAwait(false);
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
				var entities = await getMailQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);

				// Batch-fetch sender names for all unique sender IDs
				var senderIds = entities.Select(m => m.SenderCharacterID).Distinct().ToList();
				var senderNames = await dbContext.Characters
					.AsNoTracking()
					.Where(c => senderIds.Contains(c.ID))
					.Select(c => new { c.ID, c.Name })
					.ToDictionaryAsync(c => c.ID, c => c.Name, cancellationToken)
					.ConfigureAwait(false);

				var mail = entities.Select(m => new CharacterMailData(
					id: m.ID,
					version: m.Version,
					characterID: m.CharacterID,
					senderID: m.SenderCharacterID,
					senderName: senderNames.TryGetValue(m.SenderCharacterID, out var name) ? name : string.Empty,
					subject: m.Subject,
					body: m.Message,
					timeSent: m.TimeCreated,
					read: false,
					// ItemAttachmentTemplateID is used for the currency attachment field because
					// the mail system stores item template IDs (which may represent currency-item
					// types, e.g. gold coin templates) in CurrencyAttachment, while the seed for
					// item randomization is stored in ItemAttachment. Both database column names
					// (item_attachment_template_id, item_attachment_seed) and DTO parameter names
					// (currencyAttachment, itemAttachment) use semantically distinct naming, but
					// the int-to-int mapping is correct for the data they carry.
					currencyAttachment: m.ItemAttachmentTemplateID,
					itemAttachment: m.ItemAttachmentSeed
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