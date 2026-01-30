using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class KickRequestService : BaseService<KickRequestEntity>, IKickRequestService
	{
		/// <summary>
		/// Initializes a new instance of KickRequestService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public KickRequestService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Race Condition Protection:</b></para>
		/// Uses ON CONFLICT clause with unique constraint on account_name to prevent duplicate
		/// kick requests in distributed environments. If a duplicate request is attempted, the
		/// timestamp is updated instead, ensuring idempotency and preventing DOS attacks through
		/// request flooding.
		/// </remarks>
		public async Task<DatabaseResult> SaveAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				// Use database server time to avoid clock skew issues.
				// Keep atomic UPSERT semantics to prevent duplicate kick requests under concurrency.
				var sql = $@"INSERT INTO {TableName}
					(account_name, time_created)
					VALUES ({{0}}, CURRENT_TIMESTAMP)
					ON CONFLICT (account_name)
					DO UPDATE SET time_created = CURRENT_TIMESTAMP";

				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var sql = $"DELETE FROM {TableName} WHERE account_name = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<KickRequestData>>> FetchAsync(
			DateTime lastFetch,
			long lastPosition,
			int amount,
			CancellationToken cancellationToken = default)
		{
			if (amount <= 0)
				return DatabaseResult<List<KickRequestData>>.Success(new List<KickRequestData>());

			return await ExecuteReadAsync(async dbContext =>
			{
				var requests = await dbContext.KickRequests
					.AsNoTracking()
					.Where(kr =>
						kr.TimeCreated > lastFetch ||
						(kr.TimeCreated == lastFetch && kr.ID > lastPosition))
					.OrderBy(kr => kr.TimeCreated)
					.ThenBy(kr => kr.ID)
					.Take(amount)
					.ToListAsync(cancellationToken).ConfigureAwait(false);

				return requests.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps KickRequestEntity to KickRequestData DTO.
		/// </summary>
		/// <param name="entity">Kick request entity from database.</param>
		/// <returns>Kick request data DTO.</returns>
		private KickRequestData MapEntityToDto(KickRequestEntity entity)
		{
			return new KickRequestData(
				id: entity.ID,
				accountName: entity.AccountName,
				timeCreated: entity.TimeCreated
			);
		}
	}
}