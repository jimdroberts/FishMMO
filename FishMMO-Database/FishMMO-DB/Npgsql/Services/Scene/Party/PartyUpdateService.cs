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
	public sealed class PartyUpdateService : IPartyUpdateService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of PartyUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public PartyUpdateService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Atomic UPSERT - PostgreSQL specific
					var tableName = context.GetTableName<PartyUpdateEntity>();
					await context.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (party_id, last_update) 
						VALUES ({partyId}, CURRENT_TIMESTAMP) 
						ON CONFLICT (party_id) 
						DO UPDATE SET last_update = EXCLUDED.last_update 
						WHERE {tableName}.last_update < EXCLUDED.last_update",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"SavePartyUpdate",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"party_updates_pkey",
					"A party update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"party_updates_party_id_fkey",
					"The referenced party does not exist.",
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
					"SavePartyUpdate",
					"Failed to save party update record.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SavePartyUpdate",
					"An unexpected error occurred while saving party update.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsDeleted = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<PartyUpdateEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE party_id = {partyId}",
						cancellationToken);
				});

				return DatabaseResult<int>.Success(rowsDeleted);
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseTimeoutException(
					"DeletePartyUpdate",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"party_updates_pkey",
					"A party update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"party_updates_party_id_fkey",
					"The referenced party does not exist.",
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
					"DeletePartyUpdate",
					"Failed to delete party update record.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseQueryException(
					"DeletePartyUpdate",
					"An unexpected error occurred while deleting party update.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (partyIds == null || partyIds.Count == 0)
				return DatabaseResult<List<PartyUpdateData>>.Success(new List<PartyUpdateData>());

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var updates = await context.PartyUpdates
					.AsNoTracking()
					.Where(u => u.LastUpdate >= lastFetch && partyIds.Contains(u.PartyID))
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<PartyUpdateData>>.Success(updates.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseTimeoutException(
					"FetchPartyUpdates",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"party_updates_pkey",
					"A party update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"party_updates_party_id_fkey",
					"The referenced party does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseQueryException(
					"FetchPartyUpdates",
					"Failed to fetch party updates.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<List<PartyUpdateData>>.FromException(new DatabaseQueryException(
					"FetchPartyUpdates",
					"An unexpected error occurred while fetching party updates.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps PartyUpdateEntity to PartyUpdateData DTO.
		/// </summary>
		/// <param name="entity">Party update entity from database.</param>
		/// <returns>Party update data DTO.</returns>
		private PartyUpdateData MapEntityToDto(PartyUpdateEntity entity)
		{
			return new PartyUpdateData
			{
				PartyID = entity.PartyID,
				LastUpdate = entity.LastUpdate
			};
		}
	}
}