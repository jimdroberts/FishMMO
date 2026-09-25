using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	/// Service for managing two-factor recovery codes.
	/// Uses raw SQL for writes and compiled queries for reads.
	/// </summary>
	public sealed class TwoFactorRecoveryCodeService : BaseService<TwoFactorRecoveryCodeEntity>, ITwoFactorRecoveryCodeService
	{
#pragma warning disable CS8619
		private static readonly Func<NpgsqlDbContext, string, IAsyncEnumerable<TwoFactorRecoveryCodeEntity>> getUnusedByAccountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string accountName) =>
				context.TwoFactorRecoveryCodes
					.AsNoTracking()
					// Never a pending code: one issued with an unconfirmed re-enrolment opens nothing.
					.Where(e => e.AccountName == accountName && e.UsedAt == null && !e.Pending));
#pragma warning restore CS8619

		public TwoFactorRecoveryCodeService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistManyAsync(
			string accountName,
			IReadOnlyList<string> codeHashes,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			if (codeHashes == null || codeHashes.Count == 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"At least one recovery code hash is required.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Build a parameterized multi-row INSERT
				var sb = new StringBuilder();
				sb.Append($"INSERT INTO {TableName} (account_name, code_hash) VALUES ");
				string canonicalName = AuthTokenService.CanonicalAccountName(accountName);

				var parameters = new List<object>(codeHashes.Count * 2);
				for (int i = 0; i < codeHashes.Count; i++)
				{
					if (i > 0) sb.Append(", ");
					int nameIdx = i * 2;
					int hashIdx = nameIdx + 1;
					sb.Append($"({{{nameIdx}}}, {{{hashIdx}}})");
					// The row's own spelling: account_name references accounts.name, and a sign-in's
					// typed spelling failed that key (see AuthTokenService.CanonicalAccountName).
					parameters.Add(canonicalName);
					parameters.Add(codeHashes[i]);
				}

				/* Every hash carries its own random salt, so a conflict can only be this call's own
				 * rows, landed by an attempt whose reply was lost. The retry used to fail on the
				 * unique index and the caller withheld codes that were in fact stored (issue #267). */
				sb.Append(" ON CONFLICT DO NOTHING");

				await dbContext.Database
					.ExecuteSqlRawAsync(sb.ToString(), parameters, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> StagePendingAsync(
			string accountName,
			IReadOnlyList<string> codeHashes,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}
			if (codeHashes == null || codeHashes.Count == 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "At least one recovery code hash is required.");
			}

			string canonicalName = AuthTokenService.CanonicalAccountName(accountName);
			return await ExecuteTransactionAsync(async dbContext =>
			{
				/* Replaces any earlier staging — an abandoned re-enrolment's codes must not pile up —
				 * and never touches the live codes, which keep working until the new authenticator
				 * is confirmed (issue #267). */
				await dbContext.Database
					.ExecuteSqlRawAsync($"DELETE FROM {TableName} WHERE account_name = {{0}} AND pending = TRUE", new object[] { canonicalName }, cancellationToken)
					.ConfigureAwait(false);

				var sb = new StringBuilder();
				sb.Append($"INSERT INTO {TableName} (account_name, code_hash, pending) VALUES ");
				var parameters = new List<object>(codeHashes.Count * 2);
				for (int i = 0; i < codeHashes.Count; i++)
				{
					if (i > 0) sb.Append(", ");
					sb.Append($"({{{i * 2}}}, {{{i * 2 + 1}}}, TRUE)");
					parameters.Add(canonicalName);
					parameters.Add(codeHashes[i]);
				}
				await dbContext.Database
					.ExecuteSqlRawAsync(sb.ToString(), parameters, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> PromotePendingAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			string canonicalName = AuthTokenService.CanonicalAccountName(accountName);
			return await ExecuteTransactionAsync(async dbContext =>
			{
				/* The old codes go as the staged ones go live, together: codes issued against the
				 * replaced authenticator must not keep opening the account. When nothing is staged
				 * this does nothing at all, rather than delete the live codes and promote none. */
				int staged = await ExecuteScalarLongCountAsync(dbContext,
					$"SELECT COUNT(*) FROM {TableName} WHERE account_name = {{0}} AND pending = TRUE", canonicalName, cancellationToken)
					.ConfigureAwait(false);
				if (staged == 0)
				{
					return 0;
				}
				await dbContext.Database
					.ExecuteSqlRawAsync($"DELETE FROM {TableName} WHERE account_name = {{0}} AND pending = FALSE", new object[] { canonicalName }, cancellationToken)
					.ConfigureAwait(false);
				return await dbContext.Database
					.ExecuteSqlRawAsync($"UPDATE {TableName} SET pending = FALSE WHERE account_name = {{0}} AND pending = TRUE", new object[] { canonicalName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static async Task<int> ExecuteScalarLongCountAsync(NpgsqlDbContext dbContext, string sql, string canonicalName, CancellationToken cancellationToken) =>
			(int)await ExecuteScalarLongAsync(dbContext, sql, new object[] { canonicalName }, cancellationToken).ConfigureAwait(false);

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<TwoFactorRecoveryCodeData>>> FetchUnusedByAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<List<TwoFactorRecoveryCodeData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var results = new List<TwoFactorRecoveryCodeData>();
				await foreach (var entity in getUnusedByAccountQuery(dbContext, AuthTokenService.CanonicalAccountName(accountName)).ConfigureAwait(false))
				{
					results.Add(MapEntityToDto(entity));
				}
				return results;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ConsumeCodeAsync(
			string accountName,
			string codeHash,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			if (string.IsNullOrWhiteSpace(codeHash))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Code hash must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName} SET used_at = {{0}} WHERE account_name = {{1}} AND code_hash = {{2}} AND used_at IS NULL AND pending = FALSE";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, AuthTokenService.CanonicalAccountName(accountName), codeHash }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("TwoFactorRecoveryCode", codeHash,
						"Recovery code not found or already used.");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllForAccountAsync(
			string accountName,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE account_name = {{0}}";
				await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { AuthTokenService.CanonicalAccountName(accountName) }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static TwoFactorRecoveryCodeData MapEntityToDto(TwoFactorRecoveryCodeEntity entity)
		{
			return new TwoFactorRecoveryCodeData(
				id: entity.ID,
				accountName: entity.AccountName,
				codeHash: entity.CodeHash,
				usedAt: entity.UsedAt,
				timeCreated: entity.TimeCreated
			);
		}
	}
}