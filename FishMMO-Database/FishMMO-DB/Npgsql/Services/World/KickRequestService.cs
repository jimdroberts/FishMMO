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

		/// <summary>
		/// How long a kick request stays authoritative for <see cref="HasPendingAsync"/>.
		/// </summary>
		/// <remarks>
		/// A kick request is deleted by whichever game server acts on it, from its
		/// connection-stopped handler — so the normal lifetime is seconds. If the server holding
		/// the session dies before polling, nothing ever deletes the row, and an unbounded
		/// <see cref="HasPendingAsync"/> then reported the account as "already online" at every
		/// future login for the life of the database. Bounding it to slightly more than the
		/// character session lease means recovery takes at most one lease, which is the same
		/// window every other crash-recovery path in the session protocol uses.
		/// </remarks>
		private static readonly TimeSpan PendingKickRequestTtl = TimeSpan.FromMinutes(3);

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Race Condition Protection:</b></para>
		/// Uses ON CONFLICT clause with unique constraint on account_name to prevent duplicate
		/// kick requests in distributed environments. If a duplicate request is attempted, the
		/// timestamp is updated instead, ensuring idempotency and preventing DOS attacks through
		/// request flooding.
		/// </remarks>
		public async Task<DatabaseResult> PersistAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Use database server time to avoid clock skew issues.
				// Keep atomic UPSERT semantics to prevent duplicate kick requests under concurrency.
				var sql = $@"INSERT INTO {TableName}
					(account_name, time_created)
					VALUES ({{0}}, timezone('UTC', CURRENT_TIMESTAMP))
					ON CONFLICT (account_name)
					DO UPDATE SET time_created = timezone('UTC', CURRENT_TIMESTAMP)";

				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $"DELETE FROM {TableName} WHERE account_name = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { accountName }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> HasPendingAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Account name must not be empty.");
			}

			var staleBeforeUtc = DateTime.UtcNow - PendingKickRequestTtl;

			return await ExecuteReadAsync(async dbContext =>
			{
				return await dbContext.KickRequests
					.AsNoTracking()
					.AnyAsync(kr => kr.AccountName == accountName &&
									kr.TimeCreated > staleBeforeUtc, cancellationToken)
					.ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
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