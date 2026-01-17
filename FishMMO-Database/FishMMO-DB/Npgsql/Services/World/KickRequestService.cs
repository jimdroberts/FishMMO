using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class KickRequestService : IKickRequestService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of KickRequestService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public KickRequestService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<KickRequestEntity>();

					// Use CURRENT_TIMESTAMP from database server for consistency
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						   (account_name, time_created)
						   VALUES ({accountName}, CURRENT_TIMESTAMP)",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.Failure("SAVE_FAILED", "Failed to save kick request.");
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"SaveKickRequest",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"kick_requests_pkey",
					"A kick request with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"kick_requests_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveKickRequest",
					"Failed to save kick request.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveKickRequest",
					"An unexpected error occurred while saving kick request.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(string accountName, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(accountName))
			{
				return DatabaseResult<int>.Failure("INVALID_ACCOUNT_NAME", "Account name must not be empty.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<KickRequestEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE account_name = {accountName}",
						cancellationToken);
				});

				return DatabaseResult<int>.Success(rowsAffected);
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseTimeoutException(
					"DeleteKickRequest",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"kick_requests_pkey",
					"A kick request with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"kick_requests_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseQueryException(
					"DeleteKickRequest",
					"Failed to delete kick request.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseQueryException(
					"DeleteKickRequest",
					"An unexpected error occurred while deleting kick request.",
					ex.Message,
					false,
					null,
					ex));
			}
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

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var requests = await context.KickRequests
					.AsNoTracking()
					.Where(kr => kr.TimeCreated >= lastFetch && kr.ID > lastPosition)
					.OrderBy(kr => kr.TimeCreated)
					.ThenBy(kr => kr.ID)
					.Take(amount)
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<KickRequestData>>.Success(requests.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseTimeoutException(
					"FetchKickRequests",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"kick_requests_pkey",
					"A kick request with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"kick_requests_foreign_key",
					"The referenced entity does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseQueryException(
					"FetchKickRequests",
					"Failed to fetch kick requests.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<List<KickRequestData>>.FromException(new DatabaseQueryException(
					"FetchKickRequests",
					"An unexpected error occurred while fetching kick requests.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps KickRequestEntity to KickRequestData DTO.
		/// </summary>
		/// <param name="entity">Kick request entity from database.</param>
		/// <returns>Kick request data DTO.</returns>
		private KickRequestData MapEntityToDto(KickRequestEntity entity)
		{
			return new KickRequestData
			{
				ID = entity.ID,
				AccountName = entity.AccountName,
				TimeCreated = entity.TimeCreated
			};
		}
	}
}