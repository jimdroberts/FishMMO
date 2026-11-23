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
	/// Persists account-wide fog of war: one opaque encoded row per account per scene (issue #241).
	/// </summary>
	public sealed class AccountMapFogService : BaseService<AccountMapFogEntity>, IAccountMapFogService
	{
		/// <summary>Largest encoding accepted. A fully explored 4 km scene is far below it; anything near it is a bug.</summary>
		public const int MaxDataBytes = 512 * 1024;

		public AccountMapFogService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<byte[]>> FetchAsync(string accountName, string sceneName, CancellationToken cancellationToken = default)
		{
			if (!IsValidKey(accountName, sceneName, out string error))
			{
				return DatabaseResult<byte[]>.Failure(DatabaseErrorCodes.ValidationError, error);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				AccountMapFogEntity entity = await dbContext.Set<AccountMapFogEntity>()
					.AsNoTracking()
					.FirstOrDefaultAsync(f => f.AccountName == accountName && f.SceneName == sceneName, cancellationToken)
					.ConfigureAwait(false);
				return entity?.Data;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<AccountMapFogSummary>>> FetchScenesAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<IReadOnlyList<AccountMapFogSummary>>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.Set<AccountMapFogEntity>()
					.AsNoTracking()
					.Where(f => f.AccountName == accountName)
					.Select(f => new { f.SceneName, f.ExploredCount })
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				return (IReadOnlyList<AccountMapFogSummary>)rows.Select(r => new AccountMapFogSummary(r.SceneName, r.ExploredCount)).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> MergeAsync(string accountName, string sceneName, Func<byte[], AccountMapFogMerge> merge, CancellationToken cancellationToken = default)
		{
			if (!IsValidKey(accountName, sceneName, out string error))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, error);
			}
			if (merge == null)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "A merge is required.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				byte[] stored = await ExecuteReturningOrDefaultAsync(
					dbContext,
					$"SELECT data FROM {TableName} WHERE account_name = {{0}} AND scene_name = {{1}} FOR UPDATE",
					new object[] { accountName, sceneName },
					reader => reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0),
					cancellationToken).ConfigureAwait(false);

				AccountMapFogMerge merged = merge(stored);
				if (merged.Data == null || merged.Data.Length == 0)
				{
					return;
				}
				if (merged.Data.Length > MaxDataBytes)
				{
					throw new DatabaseException($"A fog encoding of {merged.Data.Length} bytes exceeds the {MaxDataBytes}-byte limit.", errorCode: DatabaseErrorCodes.ValidationError);
				}

				await dbContext.Database.ExecuteSqlRawAsync($@"
					INSERT INTO {TableName} (account_name, scene_name, data, explored_count, time_updated)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (account_name, scene_name)
					DO UPDATE SET
						data = EXCLUDED.data,
						explored_count = EXCLUDED.explored_count,
						time_updated = EXCLUDED.time_updated;",
					new object[] { accountName, sceneName, merged.Data, Math.Max(0, merged.ExploredCount), DateTime.UtcNow },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		private static bool IsValidKey(string accountName, string sceneName, out string error)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > 50)
			{
				error = "Account name must be 1 to 50 characters.";
				return false;
			}
			if (string.IsNullOrWhiteSpace(sceneName) || sceneName.Length > 64)
			{
				error = "Scene name must be 1 to 64 characters.";
				return false;
			}
			error = null;
			return true;
		}
	}
}
