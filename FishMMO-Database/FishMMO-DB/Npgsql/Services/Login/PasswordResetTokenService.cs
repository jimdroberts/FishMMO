using System;
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
	/// Outstanding "forgot my password" tokens. Raw SQL for writes and compiled queries for
	/// reads, following the existing service pattern.
	/// </summary>
	/// <remarks>
	/// This layer only ever sees hashes. It also never inspects, and must never be extended to
	/// touch, anything two-factor: a reset replaces SRP credentials and nothing else.
	/// </remarks>
	public sealed class PasswordResetTokenService : BaseService<PasswordResetTokenEntity>, IPasswordResetTokenService
	{
#pragma warning disable CS8619
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<PasswordResetTokenEntity?>> getByHashNoTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string tokenHash, CancellationToken ct) =>
				context.PasswordResetTokens
					.AsNoTracking()
					.FirstOrDefault(t => t.TokenHash == tokenHash));
#pragma warning restore CS8619

		public PasswordResetTokenService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PasswordResetTokenData>> IssueAsync(
			string tokenHash,
			string accountName,
			DateTime expiresUtc,
			string? requestedIp,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<PasswordResetTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<PasswordResetTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			if (expiresUtc <= DateTime.UtcNow)
			{
				return DatabaseResult<PasswordResetTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Expiry must be in the future.");
			}

			var createdUtc = DateTime.UtcNow;

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (account_name, token_hash, created_utc, expires_utc, requested_ip)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					RETURNING id, account_name, token_hash, created_utc, expires_utc, used_utc, requested_ip";

				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { accountName, tokenHash, createdUtc, expiresUtc, requestedIp ?? (object)DBNull.Value },
					reader => new PasswordResetTokenEntity
					{
						ID = reader.GetInt64(0),
						AccountName = reader.GetString(1),
						TokenHash = reader.GetString(2),
						CreatedUtc = reader.GetDateTime(3),
						ExpiresUtc = reader.GetDateTime(4),
						UsedUtc = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
						RequestedIp = reader.IsDBNull(6) ? null : reader.GetString(6),
					},
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<PasswordResetTokenData>.Success(MapEntityToDto(result.Data))
				: DatabaseResult<PasswordResetTokenData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PasswordResetTokenData>> FetchByHashAsync(
			string tokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<PasswordResetTokenData>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getByHashNoTrackingQuery(dbContext, tokenHash, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					throw new DatabaseEntityNotFoundException("PasswordResetToken", "by hash");
				}

				return MapEntityToDto(entity);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string>> RedeemAsync(
			string tokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(tokenHash))
			{
				return DatabaseResult<string>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Token hash must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;

				/* The single gate. Checking "unused and unexpired" in the WHERE clause of the
				 * same UPDATE that marks it used is what makes a token single-use: a read
				 * followed by a write would let two simultaneous redemptions both pass the
				 * read. */
				var sql = $@"UPDATE {TableName} SET used_utc = {{0}}
					WHERE token_hash = {{1}} AND used_utc IS NULL AND expires_utc > {{0}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, tokenHash }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					// Unknown, expired and already-used are one answer on purpose.
					throw new DatabaseEntityNotFoundException("PasswordResetToken", "by hash",
						"Token not found, expired, or already used.");
				}

				/* Recovering the account name afterwards is safe: the UPDATE above already
				 * claimed the row exclusively, and account_name is never rewritten. */
				var accountName = await dbContext.PasswordResetTokens
					.AsNoTracking()
					.Where(e => e.TokenHash == tokenHash)
					.Select(e => e.AccountName)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				if (string.IsNullOrEmpty(accountName))
				{
					throw new DatabaseEntityNotFoundException("PasswordResetToken", "by hash",
						"Token not found, expired, or already used.");
				}

				return accountName;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> InvalidateAllForAccountAsync(
			string accountName,
			string? exceptTokenHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;

				if (string.IsNullOrWhiteSpace(exceptTokenHash))
				{
					var all = $@"UPDATE {TableName} SET used_utc = {{0}}
						WHERE account_name = {{1}} AND used_utc IS NULL";
					return await dbContext.Database
						.ExecuteSqlRawAsync(all, new object[] { now, accountName }, cancellationToken)
						.ConfigureAwait(false);
				}

				var sql = $@"UPDATE {TableName} SET used_utc = {{0}}
					WHERE account_name = {{1}} AND used_utc IS NULL AND token_hash <> {{2}}";
				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, accountName, exceptTokenHash }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasRecentForAccountAsync(
			string accountName,
			DateTime sinceUtc,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<bool>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
				await dbContext.PasswordResetTokens
					.AsNoTracking()
					.AnyAsync(e => e.AccountName == accountName && e.CreatedUtc >= sinceUtc, cancellationToken)
					.ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> CleanupExpiredAsync(
			DateTime cutoffUtc,
			CancellationToken cancellationToken = default)
		{
			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE expires_utc < {{0}}";
				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { cutoffUtc }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static PasswordResetTokenData MapEntityToDto(PasswordResetTokenEntity entity)
		{
			return new PasswordResetTokenData(
				id: entity.ID,
				accountName: entity.AccountName,
				tokenHash: entity.TokenHash,
				createdUtc: entity.CreatedUtc,
				expiresUtc: entity.ExpiresUtc,
				usedUtc: entity.UsedUtc,
				requestedIp: entity.RequestedIp
			);
		}
	}
}
