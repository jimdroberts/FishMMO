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
					.Where(e => e.AccountName == accountName && e.UsedAt == null));
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

				var parameters = new List<object>(codeHashes.Count * 2);
				for (int i = 0; i < codeHashes.Count; i++)
				{
					if (i > 0) sb.Append(", ");
					int nameIdx = i * 2;
					int hashIdx = nameIdx + 1;
					sb.Append($"({{{nameIdx}}}, {{{hashIdx}}})");
					parameters.Add(accountName);
					parameters.Add(codeHashes[i]);
				}

				await dbContext.Database
					.ExecuteSqlRawAsync(sb.ToString(), parameters, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

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
				await foreach (var entity in getUnusedByAccountQuery(dbContext, accountName).ConfigureAwait(false))
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
				var sql = $@"UPDATE {TableName} SET used_at = {{0}} WHERE account_name = {{1}} AND code_hash = {{2}} AND used_at IS NULL";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, accountName, codeHash }, cancellationToken)
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
					.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
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