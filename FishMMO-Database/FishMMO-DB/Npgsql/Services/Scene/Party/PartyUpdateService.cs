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

			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} (party_id, last_update) 
					VALUES ({partyId}, CURRENT_TIMESTAMP) 
					ON CONFLICT (party_id) 
					DO UPDATE SET last_update = GREATEST({TableName}.last_update, EXCLUDED.last_update)",
				"SavePartyUpdate",
				entityName: "PartyUpdate",
				entityId: partyId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			return await ExecuteSqlAsync(
				$"DELETE FROM {TableName} WHERE party_id = {partyId}",
				"DeletePartyUpdate",
				entityName: "PartyUpdate",
				entityId: partyId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (partyIds == null || partyIds.Count == 0)
				return DatabaseResult<List<PartyUpdateData>>.Success(new List<PartyUpdateData>());

			return await ExecuteSqlAsync(async dbContext =>
			{
				var updates = await dbContext.PartyUpdates
					.AsNoTracking()
					.Where(u => u.LastUpdate >= lastFetch && partyIds.Contains(u.PartyID))
					.ToListAsync(cancellationToken);

				return updates.Select(MapEntityToDto).ToList();
			}, "FetchPartyUpdates", cancellationToken);
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