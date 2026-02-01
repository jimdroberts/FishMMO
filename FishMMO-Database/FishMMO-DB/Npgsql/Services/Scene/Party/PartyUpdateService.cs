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
	public sealed class PartyUpdateService : BaseService<PartyUpdateEntity>, IPartyUpdateService
	{
		/// <summary>
		/// Compiled query for retrieving a tracked party update row by party ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<PartyUpdateEntity?>> getByPartyIdTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long partyId, CancellationToken ct) =>
				context.PartyUpdates.FirstOrDefault(u => u.PartyID == partyId));
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of PartyUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public PartyUpdateService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var existing = await getByPartyIdTrackingQuery(dbContext, partyId, cancellationToken).ConfigureAwait(false);
				if (existing == null)
				{
					existing = new PartyUpdateEntity
					{
						PartyID = partyId,
						TimeCreated = now,
						LastUpdate = now,
					};
					await dbContext.PartyUpdates.AddAsync(existing, cancellationToken).ConfigureAwait(false);
					return;
				}

				if (existing.LastUpdate < now)
				{
					existing.LastUpdate = now;
				}
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var existing = await getByPartyIdTrackingQuery(dbContext, partyId, cancellationToken).ConfigureAwait(false);
				if (existing == null)
				{
					return 0;
				}
				dbContext.PartyUpdates.Remove(existing);
				return 1;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (partyIds == null || partyIds.Count == 0)
				return DatabaseResult<List<PartyUpdateData>>.Success(new List<PartyUpdateData>());

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var partyIdArray = partyIds.Distinct().ToArray();
				var sql = $@"SELECT * FROM {TableName}
					WHERE last_update >= {{0}}
					AND party_id = ANY({{1}})";

				var updates = await dbContext.PartyUpdates
					.FromSqlRaw(sql, lastFetch, partyIdArray)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return updates.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<List<PartyUpdateData>>.Success(result.Data)
				: DatabaseResult<List<PartyUpdateData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Maps PartyUpdateEntity to PartyUpdateData DTO.
		/// </summary>
		/// <param name="entity">Party update entity from database.</param>
		/// <returns>Party update data DTO.</returns>
		private PartyUpdateData MapEntityToDto(PartyUpdateEntity entity)
		{
			return new PartyUpdateData(
				id: entity.ID,
				partyID: entity.PartyID,
				lastUpdate: entity.LastUpdate);
		}
	}
}