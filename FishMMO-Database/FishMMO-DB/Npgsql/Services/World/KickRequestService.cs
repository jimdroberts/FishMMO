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
		public async Task<DatabaseResult> SaveAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} 
					   (account_name, time_created)
					   VALUES ({accountName}, CURRENT_TIMESTAMP)",
					cancellationToken);

				if (rowsAffected == 0)
				{
					throw new DatabaseQueryException(
						"SaveKickRequest",
						"Failed to save kick request.",
						"No rows affected.",
						false,
						null,
						null);
				}
			}, "SaveKickRequest", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			return await ExecuteWithStrategyAsync<int>(async (dbContext, strategy) =>
			{
				return await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$"DELETE FROM {TableName} WHERE account_name = {accountName}",
					cancellationToken);
			}, "DeleteKickRequest", cancellationToken);
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

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var requests = await dbContext.KickRequests
					.AsNoTracking()
					.Where(kr => kr.TimeCreated >= lastFetch && kr.ID > lastPosition)
					.OrderBy(kr => kr.TimeCreated)
					.ThenBy(kr => kr.ID)
					.Take(amount)
					.ToListAsync(cancellationToken);

				return requests.Select(MapEntityToDto).ToList();
			}, "FetchKickRequests", cancellationToken);
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